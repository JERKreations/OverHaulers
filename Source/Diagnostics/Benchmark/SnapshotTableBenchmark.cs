using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace OverHaulers
{
    #region 1. [DIAG-03] SNAPSHOT BENCHMARK DATA MODELS

    /// <summary>
    /// Single-thread read cost of the lock-free snapshot table.
    /// </summary>
    public sealed class SnapshotReadResult
    {
        /// <summary>Number of pawns resident in the table during the measurement.</summary>
        public int PawnCount;
        /// <summary>Slot count of the benchmarked table.</summary>
        public int TableCapacity;
        /// <summary>Median cost of reading a resident pawn on one thread, in nanoseconds (includes loop overhead).</summary>
        public double HitNanoseconds;
        /// <summary>Median cost of reading an absent pawn on one thread, in nanoseconds (includes loop overhead).</summary>
        public double MissNanoseconds;
    }

    /// <summary>
    /// Outcome of one reader-threads-versus-writer scenario on the snapshot table.
    /// </summary>
    public sealed class SnapshotContentionResult
    {
        /// <summary>Writer operations performed per frame; 0 means the writer ran flat out (saturating worst case).</summary>
        public int WritesPerFrame;
        /// <summary>Number of background reader threads used.</summary>
        public int ReaderThreads;
        /// <summary>Combined read throughput of all reader threads, in reads per second.</summary>
        public double ReadsPerSecond;
        /// <summary>Writer operations per second performed by the main thread.</summary>
        public double WritesPerSecond;
        /// <summary>Share of reads that overlapped a write and had to be repeated, as a percentage of all reads.</summary>
        public double RetryPercent;
        /// <summary>Share of reads that exhausted every retry and returned the neutral fallback, as a percentage of all reads.</summary>
        public double TimeoutPercent;
    }

    #endregion

    /// <summary>
    /// [DIAG-03] Snapshot table benchmark stage. Measures the lock-free table used by background pathfinding threads
    /// against a private <see cref="SnapshotTable"/> instance, so live caravan mass capacity data is never touched.
    /// Driven one time slice per frame by <see cref="BenchmarkRunner"/>; reader threads run continuously in the background
    /// while the main thread acts as the single writer, exactly as in play.
    /// Resides under Source/Diagnostics/Benchmark/.
    /// </summary>
    public static class SnapshotTableBenchmark
    {
        #region 2. CONFIGURATION & PUBLIC STATE

        private const int ResidentPawnCount = 1500;
        private const int ReadBatchSize = 50000;
        private const int ReadBatchCount = 40;
        private const double SliceBudgetMs = 6.0;
        private const int MissIdBase = 900000;

        // A busy frame of invalidation-driven re-solves, versus a writer with no pause at all (artificial worst case).
        private const int PacedWritesPerFrame = 16;
        private const double PacedDurationMs = 400.0;
        private const double SaturatingDurationMs = 250.0;
        private const int EvictOneInEveryNOps = 8;
        private const int MaxReaderThreads = 4;
        private const int ReaderStopTimeoutMs = 2000;

        /// <summary>Single-thread read cost of the last completed run; null until measured.</summary>
        public static SnapshotReadResult ReadResult { get; private set; }
        public static readonly List<SnapshotContentionResult> ContentionResults = new List<SnapshotContentionResult>(2);

        /// <summary>Overall progress of this stage in the range 0 to 1.</summary>
        public static float Progress01 { get; private set; }

        /// <summary>True once a run has recorded both the single-thread read cost and at least one contended scenario.</summary>
        public static bool HasResults => ReadResult != null && ContentionResults.Count > 0;

        #endregion

        #region 3. STATE MACHINE FIELDS

        /// <summary>
        /// Phases of the snapshot table benchmark, in execution order.
        /// </summary>
        private enum Phase
        {
            Idle,
            ReadHits,
            ReadMisses,
            ContendedPaced,
            ContendedSaturating,
            Done
        }

        /// <summary>
        /// One background reader. Each worker owns its stop flag so a worker that outlives an aborted run can never be revived by a later run.
        /// </summary>
        private sealed class ReaderWorker
        {
            private readonly SnapshotTable table;
            private readonly int[] ids;
            private readonly int startIndex;

            public volatile bool StopRequested;
            /// <summary>Total reads performed by this worker; written when its thread ends.</summary>
            public long Reads;
            /// <summary>Sum of the values read, so the reads cannot be optimised away; written when its thread ends.</summary>
            public float SinkValue;

            public ReaderWorker(SnapshotTable table, int[] ids, int startIndex)
            {
                this.table = table;
                this.ids = ids;
                this.startIndex = startIndex;
            }

            /// <summary>
            /// Thread entry point: reads resident pawns in a tight loop until a stop is requested.
            /// </summary>
            public void Run()
            {
                long localReads = 0;
                float localSink = 0f;
                int index = startIndex;

                while (!StopRequested)
                {
                    for (int i = 0; i < 256; i++)
                    {
                        localSink += table.GetOffsetThreadSafe(ids[index]);
                        if (++index == ids.Length) index = 0;
                    }
                    localReads += 256;
                }

                SinkValue = localSink;
                Reads = localReads;
            }
        }

        private static Phase phase = Phase.Idle;
        private static SnapshotTable table;
        private static int[] hitIds;
        private static int[] missIds;
        private static double[] batchNanoseconds;
        private static int batchesDone;
        private static int readCursor;
        private static double pendingHitNanoseconds;
        private static Random writerRandom;
        private static long writerOperations;

        private static ReaderWorker[] workers;
        private static Thread[] readerThreads;
        private static Stopwatch scenarioWatch;

        // Accumulates every read value so the optimiser cannot eliminate the timed reads as dead code.
        private static float optimiserDefeatSink;

        #endregion

        #region 4. LIFECYCLE CONTROL

        /// <summary>
        /// Number of background reader threads: leaves two logical cores for the game's own threads, capped at four.
        /// </summary>
        public static int ReaderThreadCount => Math.Max(1, Math.Min(MaxReaderThreads, Environment.ProcessorCount - 2));

        /// <summary>
        /// Discards results from a previous run.
        /// </summary>
        public static void ClearResults()
        {
            ReadResult = null;
            ContentionResults.Clear();
            Progress01 = 0f;
        }

        /// <summary>
        /// Prepares a fresh run: builds a private table pre-populated with a realistic resident pawn population.
        /// </summary>
        public static void Begin()
        {
            Abort();
            ClearResults();

            table = new SnapshotTable(SnapshotTable.DefaultCapacity);
            hitIds = new int[ResidentPawnCount];
            missIds = new int[ResidentPawnCount];

            // Sequential-ish IDs with gaps, like real Thing IDs; the miss set is guaranteed absent from the table.
            for (int i = 0; i < ResidentPawnCount; i++)
            {
                hitIds[i] = 1000 + i * 7;
                missIds[i] = MissIdBase + i * 13;
                table.WriteState(hitIds[i], i, 1f);
            }

            batchNanoseconds = new double[ReadBatchCount];
            writerRandom = new Random(2024);
            BeginPhase(Phase.ReadHits);
        }

        /// <summary>
        /// Stops any reader threads and releases the private table. Results already gathered are kept.
        /// </summary>
        public static void Abort()
        {
            StopReaders();
            table = null;
            phase = Phase.Idle;
        }

        /// <summary>
        /// Advances the stage by one time slice.
        /// </summary>
        /// <returns>True once every phase has finished.</returns>
        public static bool Step()
        {
            switch (phase)
            {
                case Phase.ReadHits:
                    if (RunReadBatches(hitIds))
                    {
                        pendingHitNanoseconds = MedianOfBatches();
                        BeginPhase(Phase.ReadMisses);
                    }
                    break;

                case Phase.ReadMisses:
                    if (RunReadBatches(missIds))
                    {
                        ReadResult = new SnapshotReadResult
                        {
                            PawnCount = ResidentPawnCount,
                            TableCapacity = table.Capacity,
                            HitNanoseconds = pendingHitNanoseconds,
                            MissNanoseconds = MedianOfBatches()
                        };
                        StartContendedScenario(Phase.ContendedPaced);
                    }
                    break;

                case Phase.ContendedPaced:
                    if (StepContendedScenario(PacedWritesPerFrame, PacedDurationMs))
                    {
                        StartContendedScenario(Phase.ContendedSaturating);
                    }
                    break;

                case Phase.ContendedSaturating:
                    if (StepContendedScenario(0, SaturatingDurationMs))
                    {
                        Progress01 = 1f;
                        Abort();
                        phase = Phase.Done;
                    }
                    break;
            }

            return phase == Phase.Done;
        }

        /// <summary>
        /// Switches to a phase and resets the per-phase batch counters.
        /// </summary>
        /// <param name="next">The phase to enter.</param>
        private static void BeginPhase(Phase next)
        {
            phase = next;
            batchesDone = 0;
            readCursor = 0;
        }

        #endregion

        #region 5. SINGLE-THREAD READ PHASES

        /// <summary>
        /// Times batches of reads on the calling thread until the slice budget is spent or every batch is done.
        /// </summary>
        /// <returns>True when all batches for the current phase are complete.</returns>
        private static bool RunReadBatches(int[] ids)
        {
            long budgetTicks = (long)(SliceBudgetMs * Stopwatch.Frequency / 1000.0);
            long sliceStart = Stopwatch.GetTimestamp();
            float sink = 0f;
            int cursor = readCursor;

            while (batchesDone < ReadBatchCount)
            {
                long t0 = Stopwatch.GetTimestamp();

                for (int i = 0; i < ReadBatchSize; i++)
                {
                    sink += table.GetOffsetThreadSafe(ids[cursor]);
                    if (++cursor == ids.Length) cursor = 0;
                }

                long t1 = Stopwatch.GetTimestamp();
                batchNanoseconds[batchesDone++] = (t1 - t0) * 1000000000.0 / Stopwatch.Frequency / ReadBatchSize;

                if (t1 - sliceStart >= budgetTicks) break;
            }

            readCursor = cursor;
            optimiserDefeatSink += sink;
            UpdateProgress((float)batchesDone / ReadBatchCount);
            return batchesDone >= ReadBatchCount;
        }

        /// <summary>
        /// Returns the median per-read time of the completed read batches. Sorts the batch buffer in place.
        /// </summary>
        /// <returns>The median time of one read, in nanoseconds.</returns>
        private static double MedianOfBatches()
        {
            Array.Sort(batchNanoseconds);
            return batchNanoseconds[ReadBatchCount / 2];
        }

        #endregion

        #region 6. CONTENDED READER/WRITER PHASES

        /// <summary>
        /// Starts the reader threads and the scenario clock, then enters the given contended phase.
        /// </summary>
        /// <param name="next">The contended phase to enter.</param>
        private static void StartContendedScenario(Phase next)
        {
            StopReaders();
            table.ResetCounters();
            writerOperations = 0;

            int count = ReaderThreadCount;
            workers = new ReaderWorker[count];
            readerThreads = new Thread[count];

            for (int r = 0; r < count; r++)
            {
                workers[r] = new ReaderWorker(table, hitIds, r * (hitIds.Length / count));
                readerThreads[r] = new Thread(workers[r].Run)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = "OverHaulers_BenchmarkReader"
                };
            }

            scenarioWatch = Stopwatch.StartNew();
            for (int r = 0; r < count; r++)
            {
                readerThreads[r].Start();
            }

            BeginPhase(next);
        }

        /// <summary>
        /// Performs one slice of writer work on the main thread and finishes the scenario once its duration has elapsed.
        /// </summary>
        /// <param name="writesPerFrame">Writer operations per call, or 0 to write flat out for the whole slice.</param>
        /// <param name="durationMs">How long the scenario runs, in wall-clock milliseconds.</param>
        /// <returns>True when the scenario has finished and its result has been recorded.</returns>
        private static bool StepContendedScenario(int writesPerFrame, double durationMs)
        {
            double elapsedMs = scenarioWatch.Elapsed.TotalMilliseconds;
            if (elapsedMs >= durationMs)
            {
                FinishContendedScenario(writesPerFrame);
                return true;
            }

            if (writesPerFrame > 0)
            {
                for (int i = 0; i < writesPerFrame; i++)
                {
                    PerformWriterOperation();
                }
            }
            else
            {
                long budgetTicks = (long)(SliceBudgetMs * Stopwatch.Frequency / 1000.0);
                long sliceStart = Stopwatch.GetTimestamp();
                do
                {
                    for (int i = 0; i < 64; i++)
                    {
                        PerformWriterOperation();
                    }
                }
                while (Stopwatch.GetTimestamp() - sliceStart < budgetTicks);
            }

            UpdateProgress((float)(elapsedMs / durationMs));
            return false;
        }

        /// <summary>
        /// One writer operation: normally an in-place update of a resident pawn, occasionally an eviction followed by a re-insert
        /// (a pawn leaving and returning), mirroring the operations the live registry performs.
        /// </summary>
        private static void PerformWriterOperation()
        {
            int id = hitIds[writerRandom.Next(hitIds.Length)];
            float value = writerOperations & 1023;

            if (writerRandom.Next(EvictOneInEveryNOps) == 0)
            {
                table.Evict(id);
            }

            table.WriteState(id, value, 1f);
            writerOperations++;
        }

        /// <summary>
        /// Stops the readers and records the scenario's throughput, retry and timeout figures.
        /// </summary>
        /// <param name="writesPerFrame">Writer operations per frame used by the scenario, or 0 for the saturating writer.</param>
        private static void FinishContendedScenario(int writesPerFrame)
        {
            int readerCount = workers.Length;
            long reads = StopReaders();
            double seconds = scenarioWatch.Elapsed.TotalSeconds;

            ContentionResults.Add(new SnapshotContentionResult
            {
                WritesPerFrame = writesPerFrame,
                ReaderThreads = readerCount,
                ReadsPerSecond = seconds > 0.0 ? reads / seconds : 0.0,
                WritesPerSecond = seconds > 0.0 ? writerOperations / seconds : 0.0,
                RetryPercent = reads > 0 ? 100.0 * table.ReaderRetries / reads : 0.0,
                TimeoutPercent = reads > 0 ? 100.0 * table.ReaderTimeouts / reads : 0.0
            });
        }

        /// <summary>
        /// Signals every reader to stop, waits for them and returns the total reads they performed. Safe to call when nothing is running.
        /// </summary>
        private static long StopReaders()
        {
            long totalReads = 0;

            if (workers != null)
            {
                for (int i = 0; i < workers.Length; i++)
                {
                    workers[i].StopRequested = true;
                }

                for (int i = 0; i < readerThreads.Length; i++)
                {
                    if (!readerThreads[i].Join(ReaderStopTimeoutMs))
                    {
                        OHLog.TestBench.Warn("SnapshotTableBenchmark", null, "A benchmark reader thread did not stop within " + ReaderStopTimeoutMs + " ms.");
                    }
                }

                for (int i = 0; i < workers.Length; i++)
                {
                    totalReads += workers[i].Reads;
                    optimiserDefeatSink += workers[i].SinkValue;
                }
            }

            workers = null;
            readerThreads = null;
            return totalReads;
        }

        /// <summary>
        /// Maps progress within the current phase onto the overall 0 to 1 progress of this stage.
        /// </summary>
        /// <param name="fractionOfPhase">Progress within the current phase, clamped to 0 to 1.</param>
        private static void UpdateProgress(float fractionOfPhase)
        {
            float clamped = fractionOfPhase < 0f ? 0f : (fractionOfPhase > 1f ? 1f : fractionOfPhase);

            switch (phase)
            {
                case Phase.ReadHits: Progress01 = 0.10f * clamped; break;
                case Phase.ReadMisses: Progress01 = 0.10f + 0.10f * clamped; break;
                case Phase.ContendedPaced: Progress01 = 0.20f + 0.40f * clamped; break;
                case Phase.ContendedSaturating: Progress01 = 0.60f + 0.40f * clamped; break;
            }
        }

        #endregion

        #region 7. REPORT GENERATION

        /// <summary>
        /// Appends the snapshot table section to an exported benchmark report. No-op when no results exist.
        /// </summary>
        /// <param name="report">The report being built.</param>
        public static void AppendReport(StringBuilder report)
        {
            if (!HasResults) return;

            double loadPercent = 100.0 * ReadResult.PawnCount / ReadResult.TableCapacity;

            report.AppendLine("-- Snapshot table (lock-free reads used by background threads) --");
            report.AppendLine("Table: " + ReadResult.TableCapacity + " slots, " + ReadResult.PawnCount + " pawns resident ("
                + BenchmarkRunner.FormatInvariant(loadPercent, "F0") + "% load)");
            report.AppendLine("Single-thread read: " + BenchmarkRunner.FormatInvariant(ReadResult.HitNanoseconds, "F1") + " ns (hit), "
                + BenchmarkRunner.FormatInvariant(ReadResult.MissNanoseconds, "F1") + " ns (miss), including loop overhead");
            report.AppendLine(string.Format("{0} | {1} | {2} | {3} | {4} | {5}",
                DiagnosticExportUtility.PadOrTruncate("Scenario", 32), "Readers", "     Reads/s", "   Writes/s", "Retry %", "Timeout %"));

            for (int i = 0; i < ContentionResults.Count; i++)
            {
                SnapshotContentionResult r = ContentionResults[i];
                string scenario = r.WritesPerFrame > 0 ? "Busy frame (" + r.WritesPerFrame + " writes/frame)" : "Saturating writer (worst case)";

                report.AppendLine(string.Format("{0} | {1,7} | {2,12} | {3,10} | {4,7} | {5,9}",
                    DiagnosticExportUtility.PadOrTruncate(scenario, 32),
                    r.ReaderThreads,
                    BenchmarkRunner.FormatInvariant(r.ReadsPerSecond, "F0"),
                    BenchmarkRunner.FormatInvariant(r.WritesPerSecond, "F0"),
                    BenchmarkRunner.FormatInvariant(r.RetryPercent, "F2"),
                    BenchmarkRunner.FormatInvariant(r.TimeoutPercent, "F2")));
            }

            report.AppendLine("The saturating writer is an artificial worst case far beyond real play; reads that time out safely return the neutral fallback value.");
            report.AppendLine();
        }

        #endregion
    }
}

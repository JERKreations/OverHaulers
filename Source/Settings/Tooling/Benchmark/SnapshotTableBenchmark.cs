using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace OverHaulers
{
    #region 1. [DIAG-03] SNAPSHOT BENCHMARK DATA MODELS

    /// <summary>
    /// [DIAG-03] Single-thread read latency metrics for a specific snapshot table capacity tier.
    /// </summary>
    public struct SnapshotTierMetrics
    {
        /// <summary>Number of pawns resident in the table during the measurement.</summary>
        public int PawnCount;
        /// <summary>Slot count of the table tier.</summary>
        public int TableCapacity;
        /// <summary>Median cost of reading a resident pawn on one thread, in nanoseconds (includes loop overhead).</summary>
        public double HitNanoseconds;
        /// <summary>Median cost of reading an absent pawn on one thread, in nanoseconds (includes loop overhead).</summary>
        public double MissNanoseconds;

        /// <summary>Current load factor percentage in the range 0 to 100.</summary>
        public double LoadFactorPercent => TableCapacity > 0 ? (100.0 * PawnCount / TableCapacity) : 0.0;
    }

    /// <summary>
    /// [DIAG-03] Multi-tier single-thread read latency results for the lock-free snapshot table.
    /// Evaluates both L1-resident colony baselines (512 slots) and L2-resident high-load surges (4096 slots).
    /// </summary>
    public sealed class SnapshotReadResult
    {
        /// <summary>Tier 1 metrics representing standard colony play (512 slots, 100% L1 Data Cache resident).</summary>
        public SnapshotTierMetrics Baseline;

        /// <summary>Tier 2 metrics representing extreme high-fauna surges (4096 slots, L2 resident).</summary>
        public SnapshotTierMetrics Surge;
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
    /// against a private <see cref="SnapshotTable"/> instance across both L1 colony baselines and L2 scaled surges.
    /// Driven one time slice per frame by <see cref="BenchmarkRunner"/>; reader threads run continuously in the background
    /// while the main thread acts as the single writer.
    /// Resides under Source/Diagnostics/Benchmark/.
    /// </summary>
    public static class SnapshotTableBenchmark
    {
        #region 2. CONFIGURATION & PUBLIC STATE

        // Population Tiers
        private const int BaselinePawnCount = 100;
        private const int SurgePawnCount = 1500;

        // Calibrated Read Batches (20 batches of 25,000 = 500,000 reads per phase)
        private const int ReadBatchSize = 25000;
        private const int ReadBatchCount = 20;
        private const double SliceBudgetMs = 6.0;
        private const int MissIdBase = 900000;

        // Contention Scenarios
        private const int PacedWritesPerFrame = 16;
        private const double PacedDurationMs = 400.0;
        private const double SaturatingDurationMs = 250.0;
        private const int EvictOneInEveryNOps = 8;
        private const int MaxReaderThreads = 4;
        private const int ReaderStopTimeoutMs = 2000;

        /// <summary>Multi-tier read cost of the last completed run; null until measured.</summary>
        public static SnapshotReadResult ReadResult { get; private set; }
        public static readonly List<SnapshotContentionResult> ContentionResults = new List<SnapshotContentionResult>(2);

        /// <summary>Overall progress of this stage in the range 0 to 1.</summary>
        public static float Progress01 { get; private set; }

        /// <summary>True once a run has recorded both read costs and contention scenarios.</summary>
        public static bool HasResults => ReadResult != null && ContentionResults.Count > 0;

        #endregion

        #region 3. STATE MACHINE FIELDS

        /// <summary>
        /// Phases of the two-tier snapshot table benchmark in execution order.
        /// </summary>
        private enum Phase
        {
            Idle,
            BaselineReadHits,
            BaselineReadMisses,
            PopulateSurge,
            SurgeReadHits,
            SurgeReadMisses,
            ContendedPaced,
            ContendedSaturating,
            Done
        }

        /// <summary>
        /// One background reader running tight read loops until stop is requested.
        /// </summary>
        private sealed class ReaderWorker
        {
            private readonly SnapshotTable table;
            private readonly int[] ids;
            private readonly int startIndex;

            public volatile bool StopRequested;
            public long Reads;
            public float SinkValue;

            public ReaderWorker(SnapshotTable table, int[] ids, int startIndex)
            {
                this.table = table;
                this.ids = ids;
                this.startIndex = startIndex;
            }

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

        // Baseline ID pools
        private static int[] baselineHitIds;
        private static int[] baselineMissIds;

        // Surge ID pools
        private static int[] surgeHitIds;
        private static int[] surgeMissIds;

        private static double[] batchNanoseconds;
        private static int batchesDone;
        private static int readCursor;

        // Pending latency captures
        private static double pendingBaselineHitNs;
        private static double pendingBaselineMissNs;
        private static int capturedBaselineCapacity;

        private static double pendingSurgeHitNs;

        private static Random writerRandom;
        private static long writerOperations;

        private static ReaderWorker[] workers;
        private static Thread[] readerThreads;
        private static Stopwatch scenarioWatch;

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
        /// Prepares a fresh run: initializes a 512-slot table with a realistic baseline colony population (100 pawns).
        /// </summary>
        public static void Begin()
        {
            Abort();
            ClearResults();

            // Start with dynamic scaling enabled from default 512 capacity
            table = new SnapshotTable(SnapshotTable.DefaultCapacity, forwardTelemetry: false, enableDynamicScaling: true);

            baselineHitIds = new int[BaselinePawnCount];
            baselineMissIds = new int[BaselinePawnCount];

            // 1. Populate Tier 1 Baseline (100 pawns, sits inside 512 slots / 4 KB L1 cache)
            for (int i = 0; i < BaselinePawnCount; i++)
            {
                baselineHitIds[i] = 1000 + i * 7;
                baselineMissIds[i] = MissIdBase + i * 13;
                table.WriteState(baselineHitIds[i], (float)i);
            }

            capturedBaselineCapacity = table.Capacity;

            // Prepare Tier 2 Surge arrays
            surgeHitIds = new int[SurgePawnCount];
            surgeMissIds = new int[SurgePawnCount];

            for (int i = 0; i < SurgePawnCount; i++)
            {
                surgeHitIds[i] = 1000 + i * 7;
                surgeMissIds[i] = MissIdBase + i * 13;
            }

            batchNanoseconds = new double[ReadBatchCount];
            writerRandom = new Random(2024);
            BeginPhase(Phase.BaselineReadHits);
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
                // --- TIER 1: BASELINE COLONY (L1 CACHE) ---
                case Phase.BaselineReadHits:
                    if (RunReadBatches(baselineHitIds))
                    {
                        pendingBaselineHitNs = MedianOfBatches();
                        BeginPhase(Phase.BaselineReadMisses);
                    }
                    break;

                case Phase.BaselineReadMisses:
                    if (RunReadBatches(baselineMissIds))
                    {
                        pendingBaselineMissNs = MedianOfBatches();
                        BeginPhase(Phase.PopulateSurge);
                    }
                    break;

                // --- TRANSITION: DYNAMIC SCALE-UP (512 -> 4096) ---
                case Phase.PopulateSurge:
                    // Populate remaining pawns up to 1,500, triggering dynamic power-of-two expansions
                    for (int i = BaselinePawnCount; i < SurgePawnCount; i++)
                    {
                        table.WriteState(surgeHitIds[i], (float)i);
                    }
                    BeginPhase(Phase.SurgeReadHits);
                    break;

                // --- TIER 2: SURGE POPULATION (L2 CACHE) ---
                case Phase.SurgeReadHits:
                    if (RunReadBatches(surgeHitIds))
                    {
                        pendingSurgeHitNs = MedianOfBatches();
                        BeginPhase(Phase.SurgeReadMisses);
                    }
                    break;

                case Phase.SurgeReadMisses:
                    if (RunReadBatches(surgeMissIds))
                    {
                        ReadResult = new SnapshotReadResult
                        {
                            Baseline = new SnapshotTierMetrics
                            {
                                PawnCount = BaselinePawnCount,
                                TableCapacity = capturedBaselineCapacity,
                                HitNanoseconds = pendingBaselineHitNs,
                                MissNanoseconds = pendingBaselineMissNs
                            },
                            Surge = new SnapshotTierMetrics
                            {
                                PawnCount = SurgePawnCount,
                                TableCapacity = table.Capacity,
                                HitNanoseconds = pendingSurgeHitNs,
                                MissNanoseconds = MedianOfBatches()
                            }
                        };

                        StartContendedScenario(Phase.ContendedPaced);
                    }
                    break;

                // --- CONTENTION SCENARIOS ---
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
            UpdateProgressByBatches();
            return batchesDone >= ReadBatchCount;
        }

        /// <summary>
        /// Returns the median per-read time of the completed read batches.
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
                workers[r] = new ReaderWorker(table, surgeHitIds, r * (surgeHitIds.Length / count));
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

            float baseProgress = phase == Phase.ContendedPaced ? 0.40f : 0.70f;
            float phaseWeight = phase == Phase.ContendedPaced ? 0.30f : 0.30f;
            Progress01 = baseProgress + (float)(elapsedMs / durationMs) * phaseWeight;
            return false;
        }

        /// <summary>
        /// One writer operation: in-place updates, occasional evictions, and re-insertions.
        /// </summary>
        private static void PerformWriterOperation()
        {
            int id = surgeHitIds[writerRandom.Next(surgeHitIds.Length)];
            float value = writerOperations & 1023;

            if (writerRandom.Next(EvictOneInEveryNOps) == 0)
            {
                table.Evict(id);
            }

            table.WriteState(id, value);
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
        /// Signals every reader to stop, waits for them and returns the total reads they performed.
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
        /// Maps progress across the read batch phases onto the overall 0 to 1 progress bar.
        /// </summary>
        private static void UpdateProgressByBatches()
        {
            float fraction = (float)batchesDone / ReadBatchCount;
            switch (phase)
            {
                case Phase.BaselineReadHits: Progress01 = 0.00f + fraction * 0.10f; break;
                case Phase.BaselineReadMisses: Progress01 = 0.10f + fraction * 0.10f; break;
                case Phase.PopulateSurge: Progress01 = 0.20f; break;
                case Phase.SurgeReadHits: Progress01 = 0.20f + fraction * 0.10f; break;
                case Phase.SurgeReadMisses: Progress01 = 0.30f + fraction * 0.10f; break;
            }
        }

        #endregion

        #region 7. REPORT GENERATION

        /// <summary>
        /// Appends the multi-tier snapshot table section to an exported benchmark report.
        /// </summary>
        /// <param name="report">The report being built.</param>
        public static void AppendReport(StringBuilder report)
        {
            if (!HasResults) return;

            SnapshotTierMetrics baseTier = ReadResult.Baseline;
            SnapshotTierMetrics surgeTier = ReadResult.Surge;

            report.AppendLine("-- Snapshot table (lock-free reads used by background threads) --");
            report.AppendLine("Colony baseline (L1 cache, " + baseTier.TableCapacity + " slots, " + baseTier.PawnCount + " pawns):   "
                + BenchmarkRunner.FormatInvariant(baseTier.HitNanoseconds, "F1") + " ns (hit), "
                + BenchmarkRunner.FormatInvariant(baseTier.MissNanoseconds, "F1") + " ns (miss) ["
                + BenchmarkRunner.FormatInvariant(baseTier.LoadFactorPercent, "F0") + "% load]");
            report.AppendLine("High-load surge (L2 cache, " + surgeTier.TableCapacity + " slots, " + surgeTier.PawnCount + " pawns):  "
                + BenchmarkRunner.FormatInvariant(surgeTier.HitNanoseconds, "F1") + " ns (hit), "
                + BenchmarkRunner.FormatInvariant(surgeTier.MissNanoseconds, "F1") + " ns (miss) ["
                + BenchmarkRunner.FormatInvariant(surgeTier.LoadFactorPercent, "F0") + "% load]");
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    #region 1. BENCHMARK DATA MODELS

    /// <summary>Lifecycle state of a benchmark run.</summary>
    public enum BenchmarkState
    {
        Idle = 0,
        Running = 1,
        Completed = 2,
        Cancelled = 3,
        Failed = 4
    }

    /// <summary>Health profile applied to the sandbox pawn for a solver row.</summary>
    public enum BenchmarkProfile
    {
        Pristine = 0,
        Wounded = 1
    }

    /// <summary>
    /// Timing statistics for one subject/profile combination of the solver hot path.
    /// </summary>
    public sealed class BenchmarkSolverResult
    {
        /// <summary>Display label of the benchmarked test subject.</summary>
        public string SubjectLabel;
        /// <summary>defName of the subject's body definition.</summary>
        public string BodyDefName;
        /// <summary>Number of body parts in the subject's compiled topology.</summary>
        public int PartCount;
        /// <summary>Health profile the row was measured with.</summary>
        public BenchmarkProfile Profile;
        /// <summary>Number of timed solves recorded after warm-up.</summary>
        public int Samples;
        /// <summary>Median time of one from-scratch solve, in microseconds.</summary>
        public double MedianUs;
        /// <summary>95th-percentile solve time, in microseconds.</summary>
        public double P95Us;
        /// <summary>Slowest recorded solve, in microseconds.</summary>
        public double MaxUs;
        /// <summary>Solve throughput over the timed loop, in solves per second.</summary>
        public double SolvesPerSecond;
        /// <summary>Generation-0 garbage collections observed during the timed loop; 0 is expected.</summary>
        public int GcCollections;
        /// <summary>True when the sandbox pawn could not be built or solved, so the timing fields are not valid.</summary>
        public bool Failed;
    }

    #endregion

    /// <summary>
    /// [DIAG-03] In-game performance benchmark for the Over Haulers solver.
    /// Runs entirely against headless sandbox pawns and a private snapshot table instance (see <see cref="SnapshotTableBenchmark"/>), so it never
    /// touches live pawns, the pawn cache or the live background snapshot table. Work is time-sliced across frames (see <see cref="Pump"/>) so the settings window stays responsive,
    /// and every result is synthetic: it measures Over Haulers' own solver on this machine and mod list, nothing else.
    /// Resides under Source/Diagnostics/Benchmark/.
    /// </summary>
    public static class BenchmarkRunner
    {
        #region 2. CONFIGURATION & PUBLIC STATE

        private const int WarmupIterations = 300;
        private const int SampleCount = 2000;
        private const double SliceBudgetMs = 6.0;
        private const int MaxSubjects = 4;

        /// <summary>Share of the overall progress bar reserved for the snapshot table stage.</summary>
        private const int SnapshotTableProgressUnits = 1000;

        /// <summary>Frame time budget of a 60 fps game, used to express estimates as a share of a frame.</summary>
        public const double FrameBudgetMs60Fps = 1000.0 / 60.0;

        /// <summary>Population sizes shown in the full-refresh estimate table.</summary>
        public static readonly int[] RampPawnCounts = { 10, 50, 100, 250, 500, 1000 };

        /// <summary>Current lifecycle state of the benchmark.</summary>
        public static BenchmarkState State { get; private set; } = BenchmarkState.Idle;
        /// <summary>Overall progress of the run, from 0 to 1.</summary>
        public static float Progress { get; private set; }
        /// <summary>Localised description of the stage currently running; empty when idle.</summary>
        public static string CurrentLabel { get; private set; } = string.Empty;
        /// <summary>Message of the exception that aborted the last run; empty otherwise.</summary>
        public static string ErrorMessage { get; private set; } = string.Empty;

        /// <summary>Time taken to recompile every species topology, in milliseconds.</summary>
        public static double TopologyCompileMs { get; private set; }
        /// <summary>Number of body definitions covered by the topology recompile.</summary>
        public static int TopologyBodyDefCount { get; private set; }
        /// <summary>Part count of the largest compiled body.</summary>
        public static int TopologyMaxParts { get; private set; }
        /// <summary>defName of the body with the most parts.</summary>
        public static string TopologyMaxPartsBody { get; private set; } = string.Empty;

        public static readonly List<BenchmarkSolverResult> Results = new List<BenchmarkSolverResult>(8);

        /// <summary>True once a run has completed and produced at least one result row.</summary>
        public static bool HasResults => State == BenchmarkState.Completed && Results.Count > 0;

        /// <summary>Incremented whenever results are cleared or completed, so the UI can rebuild its cached row strings only when needed.</summary>
        public static int ResultsVersion { get; private set; }

        #endregion

        #region 3. STATE MACHINE FIELDS

        /// <summary>Steps of the benchmark state machine; each frame executes one time slice of the current step.</summary>
        private enum Stage
        {
            Startup,
            JobSetup,
            JobWarmup,
            JobMeasure,
            JobTeardown,
            SnapshotTable,
            Finish
        }

        /// <summary>
        /// One subject and health profile combination to measure.
        /// </summary>
        private sealed class BenchJob
        {
            /// <summary>The test subject to solve.</summary>
            public TestSubjectEntry Subject;
            /// <summary>The health profile to apply to the subject before solving.</summary>
            public BenchmarkProfile Profile;
        }

        private static Stage stage;
        private static readonly List<BenchJob> jobs = new List<BenchJob>(8);
        private static int jobIndex;

        private static SandboxPawnHarness harness;
        private static Pawn sandboxPawn;
        private static float baseline;

        private static long[] samples;
        private static int sampleCount;
        private static int warmupDone;
        private static int failedSolves;
        private static int gcAccumulated;
        private static long measuredTicks;

        // Accumulates every solved offset so the optimiser cannot eliminate the timed solver calls as dead code.
        private static float optimiserDefeatSink;

        private static int unitsDone;
        private static int unitsTotal;

        #endregion

        #region 4. LIFECYCLE CONTROL

        /// <summary>
        /// Starts a new benchmark run, discarding previous results.
        /// </summary>
        public static void Start()
        {
            if (State == BenchmarkState.Running) return;

            Cleanup();
            Results.Clear();
            SnapshotTableBenchmark.ClearResults();
            ResultsVersion++;
            jobs.Clear();
            jobIndex = 0;
            ErrorMessage = string.Empty;
            TopologyCompileMs = 0.0;
            samples = new long[SampleCount];

            stage = Stage.Startup;
            State = BenchmarkState.Running;
            Progress = 0f;
            CurrentLabel = "OverHaulers_Benchmark_Stage_Startup".Translate().ToString();
            unitsDone = 0;
            unitsTotal = 1;
        }

        /// <summary>
        /// Cancels a running benchmark and releases every resource it holds. No-op when nothing is running.
        /// </summary>
        public static void Cancel()
        {
            if (State != BenchmarkState.Running) return;

            Cleanup();
            State = BenchmarkState.Cancelled;
            CurrentLabel = string.Empty;
        }

        /// <summary>
        /// Advances the benchmark by one time slice. Called every frame by the lifecycle driver; cheap when idle.
        /// </summary>
        public static void Pump()
        {
            if (State != BenchmarkState.Running) return;

            try
            {
                Step();
            }
            catch (Exception ex)
            {
                Cleanup();
                State = BenchmarkState.Failed;
                ErrorMessage = ex.Message;
                CurrentLabel = string.Empty;
                OHLog.TestBench.Warn("BenchmarkRunner", ex, "The benchmark failed and was aborted.");
            }
        }

        /// <summary>
        /// Disposes the sandbox harness and aborts any snapshot table benchmark still holding reader threads.
        /// </summary>
        private static void Cleanup()
        {
            if (harness != null)
            {
                harness.Dispose();
                harness = null;
            }

            sandboxPawn = null;
            SnapshotTableBenchmark.Abort();
        }

        #endregion

        #region 5. STAGE EXECUTION

        /// <summary>
        /// Executes one time slice of the current stage and updates the overall progress.
        /// </summary>
        private static void Step()
        {
            switch (stage)
            {
                case Stage.Startup:
                    RunStartupBenchmark();
                    BuildJobs();
                    unitsTotal = Math.Max(1, jobs.Count * (WarmupIterations + SampleCount)) + SnapshotTableProgressUnits;
                    stage = jobs.Count > 0 ? Stage.JobSetup : BeginSnapshotTableStage();
                    break;

                case Stage.JobSetup:
                    SetupJob(jobs[jobIndex]);
                    break;

                case Stage.JobWarmup:
                    RunSolveBatch(isWarmup: true);
                    break;

                case Stage.JobMeasure:
                    RunSolveBatch(isWarmup: false);
                    break;

                case Stage.JobTeardown:
                    FinishJob(jobs[jobIndex]);
                    break;

                case Stage.SnapshotTable:
                    if (SnapshotTableBenchmark.Step())
                    {
                        unitsDone += SnapshotTableProgressUnits;
                        stage = Stage.Finish;
                    }
                    break;

                case Stage.Finish:
                    Cleanup();
                    Progress = 1f;
                    CurrentLabel = string.Empty;
                    State = BenchmarkState.Completed;
                    ResultsVersion++;
                    return;
            }

            float snapshotUnits = stage == Stage.SnapshotTable ? SnapshotTableBenchmark.Progress01 * SnapshotTableProgressUnits : 0f;
            Progress = Mathf.Clamp01((unitsDone + snapshotUnits) / unitsTotal);
        }

        /// <summary>
        /// Starts the snapshot table stage that follows the solver rows.
        /// </summary>
        /// <returns>The stage the state machine should move to.</returns>
        private static Stage BeginSnapshotTableStage()
        {
            CurrentLabel = "OverHaulers_Benchmark_Stage_Table".Translate().ToString();
            SnapshotTableBenchmark.Begin();
            return Stage.SnapshotTable;
        }

        /// <summary>
        /// Times a full topology recompile. This is a one-off hitch of the same size players see on world load.
        /// </summary>
        private static void RunStartupBenchmark()
        {
            Stopwatch watch = Stopwatch.StartNew();
            TopologyLayoutCompiler.InvalidateAllTopologies();
            TopologyLayoutCompiler.EnsureInitialized();
            watch.Stop();

            TopologyCompileMs = watch.Elapsed.TotalMilliseconds;
            TopologyBodyDefCount = DefDatabase<BodyDef>.DefCount;
            TopologyMaxParts = TopologyLayoutCompiler.globalMaxPartCount;
            TopologyMaxPartsBody = TopologyLayoutCompiler.globalMaxPartBodyDefName;
        }

        /// <summary>
        /// Picks a small, representative set of subjects: the default humanlike body, the body with the most parts,
        /// a pack animal and a hauling mechanoid, each in a pristine and a wounded profile.
        /// </summary>
        private static void BuildJobs()
        {
            jobs.Clear();
            List<TestSubjectEntry> all = TestSubjectRegistry.AllSubjects;
            if (all == null || all.Count == 0) return;

            List<TestSubjectEntry> picked = new List<TestSubjectEntry>(MaxSubjects);
            AddDistinct(picked, TestSubjectRegistry.GetDefaultSubject());

            for (int i = 0; i < all.Count && picked.Count < MaxSubjects; i++)
            {
                TestSubjectEntry s = all[i];
                if (s?.BodyDef != null && s.BodyDef.defName == TopologyMaxPartsBody && !s.IsLivePawn)
                {
                    AddDistinct(picked, s);
                    break;
                }
            }

            for (int i = 0; i < all.Count && picked.Count < MaxSubjects; i++)
            {
                RaceProperties race = all[i]?.RaceDef?.race;
                if (race != null && race.packAnimal && !race.Humanlike && !all[i].IsLivePawn)
                {
                    AddDistinct(picked, all[i]);
                    break;
                }
            }

            for (int i = 0; i < all.Count && picked.Count < MaxSubjects; i++)
            {
                RaceProperties race = all[i]?.RaceDef?.race;
                if (race != null && race.IsMechanoid && !all[i].IsLivePawn && PawnDataRegistry.IsCaravanCapable(all[i].RaceDef))
                {
                    AddDistinct(picked, all[i]);
                    break;
                }
            }

            for (int i = 0; i < picked.Count; i++)
            {
                jobs.Add(new BenchJob { Subject = picked[i], Profile = BenchmarkProfile.Pristine });
                jobs.Add(new BenchJob { Subject = picked[i], Profile = BenchmarkProfile.Wounded });
            }
        }

        /// <summary>
        /// Adds a subject to the pick list unless one with the same body definition and race is already present.
        /// </summary>
        /// <param name="list">The subjects picked so far.</param>
        /// <param name="candidate">The subject to add; ignored when it has no body definition.</param>
        private static void AddDistinct(List<TestSubjectEntry> list, TestSubjectEntry candidate)
        {
            if (candidate?.BodyDef == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].BodyDef == candidate.BodyDef && list[i].RaceDef == candidate.RaceDef) return;
            }

            list.Add(candidate);
        }

        /// <summary>
        /// Builds the sandbox pawn for a job, applies its health profile and resolves the species baseline, then moves to warm-up
        /// (or straight to teardown when the pawn cannot be built).
        /// </summary>
        /// <param name="job">The subject and health profile to prepare.</param>
        private static void SetupJob(BenchJob job)
        {
            CurrentLabel = "OverHaulers_Benchmark_Stage_Solver".Translate(job.Subject.Label, ProfileLabel(job.Profile)).ToString();

            sampleCount = 0;
            warmupDone = 0;
            failedSolves = 0;
            gcAccumulated = 0;
            measuredTicks = 0;

            harness = SandboxPawnHarness.CreateForCurrentProgramState();
            harness.BindSubject(job.Subject);

            if (!harness.IsValid)
            {
                sandboxPawn = null;
                stage = Stage.JobTeardown;
                return;
            }

            if (job.Profile == BenchmarkProfile.Wounded)
            {
                harness.SimulateRepresentativeWounds();
            }

            sandboxPawn = harness.SandboxPawn;
            baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(sandboxPawn);
            stage = Stage.JobWarmup;
        }

        /// <summary>
        /// Runs solves back to back until the slice budget is spent or the stage completes.
        /// GC collections are counted only across the measured loop, where only our own code is running.
        /// </summary>
        private static void RunSolveBatch(bool isWarmup)
        {
            Settings settings = OverHaulers.settings;
            long budgetTicks = (long)(SliceBudgetMs * Stopwatch.Frequency / 1000.0);
            int gcBefore = GC.CollectionCount(0);
            long sliceStart = Stopwatch.GetTimestamp();

            while (true)
            {
                long t0 = Stopwatch.GetTimestamp();

                float offset = MassCapacitySolver.SolveMassCapacityOffset(
                    sandboxPawn, baseline, out float _, settings, false, out AnatomicalWorkspace workspace);

                bool solved = workspace != null;
                if (solved)
                {
                    WorkspacePool.ReleaseWorkspace(workspace);
                }

                long t1 = Stopwatch.GetTimestamp();
                optimiserDefeatSink += offset;
                unitsDone++;

                if (isWarmup)
                {
                    warmupDone++;
                    if (warmupDone >= WarmupIterations)
                    {
                        stage = Stage.JobMeasure;
                        break;
                    }
                }
                else
                {
                    if (!solved) failedSolves++;
                    samples[sampleCount++] = t1 - t0;
                    measuredTicks += t1 - t0;

                    if (sampleCount >= SampleCount)
                    {
                        stage = Stage.JobTeardown;
                        break;
                    }
                }

                if (t1 - sliceStart >= budgetTicks) break;
            }

            if (!isWarmup)
            {
                gcAccumulated += GC.CollectionCount(0) - gcBefore;
            }
        }

        /// <summary>
        /// Converts the samples collected for a job into a result row (median, 95th percentile, maximum, throughput), releases the harness
        /// and advances to the next job or, after the last one, to the snapshot table stage.
        /// </summary>
        /// <param name="job">The job that has just finished measuring.</param>
        private static void FinishJob(BenchJob job)
        {
            BenchmarkSolverResult result = new BenchmarkSolverResult
            {
                SubjectLabel = job.Subject.Label,
                BodyDefName = job.Subject.BodyDef?.defName ?? "?",
                PartCount = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(job.Subject.BodyDef)?.PartCount ?? 0,
                Profile = job.Profile,
                Samples = sampleCount,
                GcCollections = gcAccumulated,
                Failed = sandboxPawn == null || sampleCount == 0 || failedSolves > sampleCount / 2
            };

            if (!result.Failed)
            {
                long[] sorted = new long[sampleCount];
                Array.Copy(samples, sorted, sampleCount);
                Array.Sort(sorted);

                double toMicroseconds = 1000000.0 / Stopwatch.Frequency;
                result.MedianUs = sorted[sampleCount / 2] * toMicroseconds;
                result.P95Us = sorted[Math.Min(sampleCount - 1, (int)(sampleCount * 0.95))] * toMicroseconds;
                result.MaxUs = sorted[sampleCount - 1] * toMicroseconds;

                double totalSeconds = (double)measuredTicks / Stopwatch.Frequency;
                result.SolvesPerSecond = totalSeconds > 0.0 ? sampleCount / totalSeconds : 0.0;
            }

            Results.Add(result);
            Cleanup();

            // A failed setup never reached the solve loop, so credit its share of progress here.
            if (sampleCount == 0)
            {
                unitsDone += WarmupIterations + SampleCount;
            }

            jobIndex++;
            stage = jobIndex < jobs.Count ? Stage.JobSetup : BeginSnapshotTableStage();
        }

        #endregion

        #region 6. DERIVED ESTIMATES

        /// <summary>
        /// Returns the median solve time of the first successful pristine run (the default humanlike subject), or 0 when unavailable.
        /// </summary>
        public static double ReferenceMedianUs()
        {
            for (int i = 0; i < Results.Count; i++)
            {
                if (!Results[i].Failed && Results[i].Profile == BenchmarkProfile.Pristine) return Results[i].MedianUs;
            }
            return 0.0;
        }

        /// <summary>
        /// Returns the highest median solve time across all successful runs, or 0 when unavailable.
        /// </summary>
        public static double WorstMedianUs()
        {
            double worst = 0.0;
            for (int i = 0; i < Results.Count; i++)
            {
                if (!Results[i].Failed && Results[i].MedianUs > worst) worst = Results[i].MedianUs;
            }
            return worst;
        }

        /// <summary>
        /// Returns the localised display name of a health profile.
        /// </summary>
        /// <param name="profile">The health profile.</param>
        /// <returns>The localised profile label.</returns>
        public static string ProfileLabel(BenchmarkProfile profile)
        {
            return profile == BenchmarkProfile.Wounded
                ? "OverHaulers_Benchmark_Profile_Wounded".Translate().ToString()
                : "OverHaulers_Benchmark_Profile_Pristine".Translate().ToString();
        }

        #endregion

        #region 7. REPORT GENERATION

        /// <summary>
        /// Formats a number with the invariant culture so exported reports read the same on every locale.
        /// </summary>
        /// <param name="value">The number to format.</param>
        /// <param name="format">A standard numeric format string such as F1.</param>
        /// <returns>The formatted text.</returns>
        internal static string FormatInvariant(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Builds a plain-text report (system details, results and estimates) suitable for the clipboard or a file.
        /// Intentionally English-only, like the other diagnostic exports.
        /// </summary>
        public static string BuildReport()
        {
            StringBuilder report = new StringBuilder(2048);

            report.AppendLine("=== Over Haulers Benchmark Report ===");
            report.AppendLine("Generated:       " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            report.AppendLine("Over Haulers:    " + typeof(BenchmarkRunner).Assembly.GetName().Version);
            report.AppendLine("RimWorld:        " + VersionControl.CurrentVersionString);
            report.AppendLine("Active mods:     " + LoadedModManager.RunningModsListForReading.Count);
            report.AppendLine("Runtime:         .NET " + Environment.Version);
            report.AppendLine("CPU:             " + SystemInfo.processorType + " (" + SystemInfo.processorCount + " logical cores, " + SystemInfo.processorFrequency + " MHz)");
            report.AppendLine("RAM:             " + SystemInfo.systemMemorySize + " MB");
            report.AppendLine("OS:              " + SystemInfo.operatingSystem);
            report.AppendLine("Other patches on MassUtility.Capacity: " + DescribeForeignCapacityPatches());
            report.AppendLine("Program state:   " + Current.ProgramState);
            report.AppendLine();

            report.AppendLine("-- Startup --");
            report.AppendLine("Topology compile: " + FormatInvariant(TopologyCompileMs, "F1") + " ms for " + TopologyBodyDefCount + " body definitions (largest: "
                + TopologyMaxPartsBody + ", " + TopologyMaxParts + " parts)");
            report.AppendLine();

            report.AppendLine("-- Solver hot path (single thread, " + SampleCount + " samples per row after " + WarmupIterations + " warm-up solves) --");
            report.AppendLine(string.Format("{0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8}",
                DiagnosticExportUtility.PadOrTruncate("Subject", 28),
                DiagnosticExportUtility.PadOrTruncate("BodyDef", 24),
                "Parts",
                "Profile ",
                " Median us",
                "    P95 us",
                "    Max us",
                "  Solves/s",
                "GC"));

            for (int i = 0; i < Results.Count; i++)
            {
                BenchmarkSolverResult r = Results[i];
                if (r.Failed)
                {
                    report.AppendLine(DiagnosticExportUtility.PadOrTruncate(r.SubjectLabel, 28) + " | " + DiagnosticExportUtility.PadOrTruncate(r.BodyDefName, 24)
                        + " | " + r.Profile + ": FAILED (sandbox pawn could not be solved)");
                    continue;
                }

                report.AppendLine(string.Format("{0} | {1} | {2,5} | {3,-8} | {4,10} | {5,10} | {6,10} | {7,10} | {8}",
                    DiagnosticExportUtility.PadOrTruncate(r.SubjectLabel, 28),
                    DiagnosticExportUtility.PadOrTruncate(r.BodyDefName, 24),
                    r.PartCount,
                    r.Profile,
                    FormatInvariant(r.MedianUs, "F1"),
                    FormatInvariant(r.P95Us, "F1"),
                    FormatInvariant(r.MaxUs, "F1"),
                    FormatInvariant(r.SolvesPerSecond, "F0"),
                    r.GcCollections));
            }
            report.AppendLine();

            double reference = ReferenceMedianUs();
            double worst = WorstMedianUs();
            report.AppendLine("-- Estimated cost of re-solving N pawns from scratch in one go --");
            report.AppendLine("Pawns | Default subject (pristine) | Worst measured row");
            for (int i = 0; i < RampPawnCounts.Length; i++)
            {
                int n = RampPawnCounts[i];
                double refMs = n * reference / 1000.0;
                double worstMs = n * worst / 1000.0;
                report.AppendLine(string.Format("{0,5} | {1,10} ms ({2,5}% of a 60 fps frame) | {3,10} ms ({4,5}%)",
                    n, FormatInvariant(refMs, "F2"), FormatInvariant(refMs / FrameBudgetMs60Fps * 100.0, "F1"), FormatInvariant(worstMs, "F2"), FormatInvariant(worstMs / FrameBudgetMs60Fps * 100.0, "F1")));
            }
            report.AppendLine();

            SnapshotTableBenchmark.AppendReport(report);

            SelfCheckRunner.AppendReport(report);

            report.AppendLine("Notes:");
            report.AppendLine("- Synthetic numbers for Over Haulers' own solver only; vanilla, other mods and rendering are not included.");
            report.AppendLine("- Cached lookups (the normal case) are far cheaper than a from-scratch solve; a full re-solve only happens after health, gear or settings changes.");
            report.AppendLine("- 'GC' counts garbage collections during the measured loops. 0 is expected; a non-zero value can also come from other threads allocating at the same time.");
            report.AppendLine("- Results vary between runs and machines. Compare runs on the same machine, not across different ones.");

            return report.ToString();
        }

        /// <summary>
        /// Lists the Harmony owners, other than Over Haulers, that patch MassUtility.Capacity, for the report header.
        /// </summary>
        /// <returns>A comma-separated list of owner ids, "none", or "unknown" if the patch information could not be read.</returns>
        private static string DescribeForeignCapacityPatches()
        {
            try
            {
                System.Reflection.MethodBase target = AccessTools.Method(typeof(MassUtility), nameof(MassUtility.Capacity));
                Patches info = target != null ? Harmony.GetPatchInfo(target) : null;
                if (info == null) return "none";

                List<string> owners = new List<string>(4);
                foreach (string owner in info.Owners)
                {
                    if (owner != "com.overhaulers.mod" && !owners.Contains(owner)) owners.Add(owner);
                }

                return owners.Count == 0 ? "none" : string.Join(", ", owners.ToArray());
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Writes the report to the diagnostic dumps folder and returns the file path.
        /// </summary>
        public static string ExportReport()
        {
            return DiagnosticExportUtility.WriteExportFile(new StringBuilder(BuildReport()), "Benchmark", "Report");
        }

        #endregion
    }
}

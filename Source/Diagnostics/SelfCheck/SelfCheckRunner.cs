using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Verse;

namespace OverHaulers
{
    #region 1. SELF-CHECK DATA MODELS

    /// <summary>Lifecycle state of a self-check run.</summary>
    public enum SelfCheckState
    {
        Idle = 0,
        Running = 1,
        Completed = 2
    }

    /// <summary>Result of one check.</summary>
    public enum SelfCheckOutcome
    {
        Passed = 0,
        Failed = 1,
        Skipped = 2
    }

    /// <summary>
    /// Thrown by a check when an expectation is not met.
    /// </summary>
    public sealed class SelfCheckFailedException : Exception
    {
        public SelfCheckFailedException(string message) : base(message) { }
    }

    /// <summary>
    /// Handed to every check so it can report a skip or attach a detail line to a pass.
    /// Both texts are informative and therefore expected to be already-localised strings.
    /// </summary>
    public sealed class SelfCheckContext
    {
        /// <summary>True when the check opted out instead of running.</summary>
        public bool Skipped { get; private set; }
        /// <summary>Localised note or skip reason shown next to the result; empty when there is none.</summary>
        public string Detail { get; private set; } = string.Empty;

        /// <summary>Marks the check as skipped (for example when no suitable subject exists on this mod list).</summary>
        public void Skip(string localisedReason)
        {
            Skipped = true;
            Detail = localisedReason ?? string.Empty;
        }

        /// <summary>Attaches an informational note to a passing check.</summary>
        public void Note(string localisedNote)
        {
            Detail = localisedNote ?? string.Empty;
        }
    }

    /// <summary>
    /// One registered check. Group and name are Keyed-translation keys, so the UI shows localised text while exported
    /// reports use the stable key as an unambiguous identifier.
    /// </summary>
    public sealed class SelfCheckDef
    {
        /// <summary>Keyed-translation key of the group the check belongs to.</summary>
        public readonly string GroupKey;
        /// <summary>Keyed-translation key of the check's name; also its stable identifier in exported reports.</summary>
        public readonly string NameKey;
        /// <summary>The check itself: returns to pass, throws to fail.</summary>
        public readonly Action<SelfCheckContext> Body;

        public SelfCheckDef(string groupKey, string nameKey, Action<SelfCheckContext> body)
        {
            GroupKey = groupKey;
            NameKey = nameKey;
            Body = body;
        }
    }

    /// <summary>
    /// Recorded outcome of one executed check.
    /// </summary>
    public sealed class SelfCheckResult
    {
        /// <summary>Keyed-translation key of the check's group.</summary>
        public string GroupKey;
        /// <summary>Keyed-translation key of the check's name.</summary>
        public string NameKey;
        /// <summary>Whether the check passed, failed or was skipped.</summary>
        public SelfCheckOutcome Outcome;
        /// <summary>Localised note or skip reason, or the hardcoded English failure message.</summary>
        public string Detail;
        /// <summary>Time the check took, in milliseconds; kept for diagnostics and not shown in the UI.</summary>
        public double Milliseconds;
    }

    /// <summary>
    /// Minimal assertion helpers; any unmet expectation fails the current check with a descriptive message.
    /// </summary>
    internal static class Expect
    {
        /// <summary>
        /// Fails the current check when a condition is not met.
        /// </summary>
        /// <param name="condition">The condition that must hold.</param>
        /// <param name="what">Description of the expectation, used as the failure message.</param>
        public static void True(bool condition, string what)
        {
            if (!condition) throw new SelfCheckFailedException(what);
        }

        /// <summary>
        /// Fails the current check when two values differ.
        /// </summary>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The value actually observed.</param>
        /// <param name="what">Description of what was compared, prefixed to the failure message.</param>
        public static void Equal<T>(T expected, T actual, string what) where T : IEquatable<T>
        {
            if (!expected.Equals(actual))
            {
                throw new SelfCheckFailedException(what + ": expected " + expected + " but got " + actual);
            }
        }
    }

    #endregion

    /// <summary>
    /// [DIAG-04] In-game self-check. Verifies the mod's core algorithms and its integration with the loaded game data,
    /// on the real runtime and mod list. Checks that stress shared state (the snapshot table) run against private instances,
    /// so live caravan mass capacity data is never touched. One check runs per frame so the window never blocks for long.
    /// Resides under Source/Diagnostics/SelfCheck/.
    /// </summary>
    public static class SelfCheckRunner
    {
        #region 2. STATE

        /// <summary>Current state of the self-check.</summary>
        public static SelfCheckState State { get; private set; } = SelfCheckState.Idle;

        public static readonly List<SelfCheckResult> Results = new List<SelfCheckResult>(24);

        private static readonly List<SelfCheckDef> queue = new List<SelfCheckDef>(24);
        private static int nextIndex;

        /// <summary>Number of checks queued for the current run.</summary>
        public static int TotalCount => queue.Count;
        /// <summary>Number of checks that have finished so far.</summary>
        public static int FinishedCount => Results.Count;

        /// <summary>Number of recorded results that passed.</summary>
        public static int PassedCount => CountOutcome(SelfCheckOutcome.Passed);
        /// <summary>Number of recorded results that failed.</summary>
        public static int FailedCount => CountOutcome(SelfCheckOutcome.Failed);
        /// <summary>Number of recorded results that were skipped.</summary>
        public static int SkippedCount => CountOutcome(SelfCheckOutcome.Skipped);

        /// <summary>True once a run has completed and produced at least one result.</summary>
        public static bool HasResults => State == SelfCheckState.Completed && Results.Count > 0;

        /// <summary>Incremented whenever results are cleared or completed, so the UI can rebuild its cached row strings only when needed.</summary>
        public static int ResultsVersion { get; private set; }

        /// <summary>
        /// Counts how many results so far have a given outcome.
        /// </summary>
        /// <param name="outcome">The outcome to count.</param>
        /// <returns>The number of matching results.</returns>
        private static int CountOutcome(SelfCheckOutcome outcome)
        {
            int count = 0;
            for (int i = 0; i < Results.Count; i++)
            {
                if (Results[i].Outcome == outcome) count++;
            }
            return count;
        }

        #endregion

        #region 3. LIFECYCLE CONTROL

        /// <summary>
        /// Starts a new self-check run, discarding previous results.
        /// </summary>
        public static void Start()
        {
            if (State == SelfCheckState.Running) return;

            Results.Clear();
            ResultsVersion++;
            queue.Clear();
            SelfCheckSuites.RegisterAll(queue);
            nextIndex = 0;
            State = SelfCheckState.Running;
        }

        /// <summary>
        /// Abandons a running self-check. Results gathered so far are discarded.
        /// </summary>
        public static void Cancel()
        {
            if (State != SelfCheckState.Running) return;

            Results.Clear();
            ResultsVersion++;
            State = SelfCheckState.Idle;
        }

        /// <summary>
        /// Runs the next queued check. Called every frame by the lifecycle driver; cheap when idle.
        /// </summary>
        public static void Pump()
        {
            if (State != SelfCheckState.Running) return;

            if (nextIndex < queue.Count)
            {
                RunCheck(queue[nextIndex++]);
            }

            if (nextIndex >= queue.Count)
            {
                State = SelfCheckState.Completed;
                ResultsVersion++;
            }
        }

        /// <summary>
        /// Executes one check, timing it and converting its pass, skip or exception into a recorded result.
        /// </summary>
        /// <param name="check">The check to run.</param>
        private static void RunCheck(SelfCheckDef check)
        {
            SelfCheckContext context = new SelfCheckContext();
            SelfCheckResult result = new SelfCheckResult { GroupKey = check.GroupKey, NameKey = check.NameKey, Detail = string.Empty };
            Stopwatch watch = Stopwatch.StartNew();

            try
            {
                check.Body(context);
                result.Outcome = context.Skipped ? SelfCheckOutcome.Skipped : SelfCheckOutcome.Passed;
                result.Detail = context.Detail;
            }
            catch (SelfCheckFailedException ex)
            {
                result.Outcome = SelfCheckOutcome.Failed;
                result.Detail = ex.Message;
            }
            catch (Exception ex)
            {
                result.Outcome = SelfCheckOutcome.Failed;
                result.Detail = "Unhandled " + ex.GetType().Name + ": " + ex.Message;
                OHLog.TestBench.Warn("SelfCheck:" + check.NameKey, ex, "A self-check threw an unexpected exception.");
            }

            watch.Stop();
            result.Milliseconds = watch.Elapsed.TotalMilliseconds;
            Results.Add(result);
        }

        #endregion

        #region 4. REPORTING

        /// <summary>
        /// Appends a compact self-check summary (and any failures or skips) to an exported report. No-op if no run has completed.
        /// Checks are identified by their stable translation key so reports read the same regardless of the player's language.
        /// </summary>
        public static void AppendReport(StringBuilder builder)
        {
            if (!HasResults) return;

            int skipped = SkippedCount;
            builder.AppendLine("-- Self-check --");
            builder.AppendLine(PassedCount + "/" + (Results.Count - skipped) + " passed"
                + (skipped > 0 ? ", " + skipped + " skipped" : string.Empty));

            for (int i = 0; i < Results.Count; i++)
            {
                SelfCheckResult r = Results[i];
                if (r.Outcome == SelfCheckOutcome.Passed) continue;

                // Failure details are hardcoded English; skip reasons are localised informative text, so only the key is exported.
                builder.AppendLine("  [" + (r.Outcome == SelfCheckOutcome.Failed ? "FAIL" : "SKIP") + "] " + r.NameKey
                    + (r.Outcome == SelfCheckOutcome.Failed && !string.IsNullOrEmpty(r.Detail) ? " - " + r.Detail : string.Empty));
            }

            builder.AppendLine();
        }

        #endregion
    }
}

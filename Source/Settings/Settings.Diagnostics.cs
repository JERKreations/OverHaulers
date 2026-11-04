using RimWorld;
using Verse;

namespace OverHaulers
{
    public partial class Settings
    {
        #region 1. [SEC-09] DIAGNOSTICS & DEVELOPER FIELDS

        public bool verboseBreakdown = PresentationDefaults.VerboseBreakdown;

        /// <summary>
        /// High-load population stress-test benchmark toggle forcing 100% of biological fauna
        /// (livestock, wild animals, vermin, predators) through the solver and dynamic cache.
        /// </summary>
        public bool devStressTestAllFauna = CoreDefaults.DevStressTestAllFauna;

        /// <summary>
        /// Backwards-compatible alias for <see cref="devStressTestAllFauna"/>.
        /// </summary>
        [System.Obsolete("Use devStressTestAllFauna instead.")]
        public bool devAllowNonPackSpeciesInLivePlay
        {
            get => devStressTestAllFauna;
            set => devStressTestAllFauna = value;
        }

        public int reportMetricsIntervalHours = CoreDefaults.ReportMetricsIntervalHours;
        public bool logQueryMetrics = CoreDefaults.LogQueryMetrics;
        public bool logCacheMetrics = CoreDefaults.LogCacheMetrics;
        public bool logSnapshotTableMetrics = CoreDefaults.LogSnapshotTableMetrics;
        public bool logPresentationMetrics = CoreDefaults.LogPresentationMetrics;
        public bool logLifecycleMetrics = CoreDefaults.LogLifecycleMetrics;
        public bool logWorkspaceMetrics = CoreDefaults.LogWorkspaceMetrics;
        public bool logSafetyFloorClamps = CoreDefaults.LogSafetyFloorClamps;
        public bool logPawnEvictions = CoreDefaults.LogPawnEvictions;
        public int pawnEvictionTimeframeHours = CoreDefaults.PawnEvictionTimeframeHours;

        public int ReportMetricsIntervalTicks => reportMetricsIntervalHours * GenDate.TicksPerHour;
        public int PawnEvictionTimeframeTicks => pawnEvictionTimeframeHours * GenDate.TicksPerHour;

        #endregion

        #region 2. [SEC-09] RESET

        /// <summary>
        /// Resets all diagnostic-related settings to their default values.
        /// </summary>
        public void ResetDiagnostics()
        {
            verboseBreakdown = PresentationDefaults.VerboseBreakdown;
            devStressTestAllFauna = CoreDefaults.DevStressTestAllFauna;
            reportMetricsIntervalHours = CoreDefaults.ReportMetricsIntervalHours;
            logQueryMetrics = CoreDefaults.LogQueryMetrics;
            logCacheMetrics = CoreDefaults.LogCacheMetrics;
            logSnapshotTableMetrics = CoreDefaults.LogSnapshotTableMetrics;
            logPresentationMetrics = CoreDefaults.LogPresentationMetrics;
            logLifecycleMetrics = CoreDefaults.LogLifecycleMetrics;
            logWorkspaceMetrics = CoreDefaults.LogWorkspaceMetrics;
            logSafetyFloorClamps = CoreDefaults.LogSafetyFloorClamps;
            logPawnEvictions = CoreDefaults.LogPawnEvictions;
            pawnEvictionTimeframeHours = CoreDefaults.PawnEvictionTimeframeHours;
            SettingsViewUtilities.ClearInputBuffers();
        }

        #endregion

        #region 3. [SEC-09] SERIALIZATION & SANITISATION

        private void ExposeDiagnostics()
        {
            Scribe_Values.Look(ref verboseBreakdown, "verboseBreakdown", PresentationDefaults.VerboseBreakdown);
            Scribe_Values.Look(ref devStressTestAllFauna, "devStressTestAllFauna", CoreDefaults.DevStressTestAllFauna);
            Scribe_Values.Look(ref reportMetricsIntervalHours, "reportMetricsIntervalHours", CoreDefaults.ReportMetricsIntervalHours);
            Scribe_Values.Look(ref logQueryMetrics, "logQueryMetrics", CoreDefaults.LogQueryMetrics);
            Scribe_Values.Look(ref logCacheMetrics, "logCacheMetrics", CoreDefaults.LogCacheMetrics);
            Scribe_Values.Look(ref logSnapshotTableMetrics, "logSnapshotTableMetrics", CoreDefaults.LogSnapshotTableMetrics);
            Scribe_Values.Look(ref logPresentationMetrics, "logPresentationMetrics", CoreDefaults.LogPresentationMetrics);
            Scribe_Values.Look(ref logLifecycleMetrics, "logLifecycleMetrics", CoreDefaults.LogLifecycleMetrics);
            Scribe_Values.Look(ref logWorkspaceMetrics, "logWorkspaceMetrics", CoreDefaults.LogWorkspaceMetrics);
            Scribe_Values.Look(ref logSafetyFloorClamps, "logSafetyFloorClamps", CoreDefaults.LogSafetyFloorClamps);
            Scribe_Values.Look(ref logPawnEvictions, "logPawnEvictions", CoreDefaults.LogPawnEvictions);
            Scribe_Values.Look(ref pawnEvictionTimeframeHours, "pawnEvictionTimeframeHours", CoreDefaults.PawnEvictionTimeframeHours);
        }

        private void SanitizeDiagnostics()
        {
            reportMetricsIntervalHours = System.Math.Max(CoreDefaults.ReportMetricsIntervalHoursMin, System.Math.Min(CoreDefaults.ReportMetricsIntervalHoursMax, reportMetricsIntervalHours));
            pawnEvictionTimeframeHours = System.Math.Max(CoreDefaults.PawnEvictionTimeframeHoursMin, System.Math.Min(CoreDefaults.PawnEvictionTimeframeHoursMax, pawnEvictionTimeframeHours));
        }

        #endregion
    }
}

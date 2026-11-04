namespace OverHaulers
{
    /// <summary>
    /// [CORE-CONF-02] IMMUTABLE VALUE-TYPE RUNTIME & INFRASTRUCTURE CONFIGURATION
    /// Encapsulates engine infrastructure, cache lifecycle policies, fauna stress-testing,
    /// and diagnostic telemetry toggles. Resides under Source/Core/Data/.
    /// Holds zero duplicate constants, preserving CoreDefaults as the Single Source of Truth (SSoT).
    /// </summary>
    public readonly struct RuntimeConfiguration
    {
        #region 1. INSTANCE FIELDS & IMMUTABLE BACKING STATE

        // Registry & Cache Lifecycle
        public readonly int PawnEvictionTimeframeTicks;
        public readonly bool DevStressTestAllFauna;

        // Diagnostic Telemetry & Profiling
        public readonly int ReportMetricsIntervalHours;
        public readonly bool LogCacheMetrics;
        public readonly bool LogQueryMetrics;
        public readonly bool LogSnapshotTableMetrics;
        public readonly bool LogPresentationMetrics;
        public readonly bool LogLifecycleMetrics;
        public readonly bool LogWorkspaceMetrics;
        public readonly bool LogSafetyFloorClamps;
        public readonly bool LogPawnEvictions;

        #endregion

        #region 2. LIFECYCLE: CONSTRUCTOR

        public RuntimeConfiguration(
            int pawnEvictionTimeframeTicks,
            bool devStressTestAllFauna,
            int reportMetricsIntervalHours,
            bool logCache,
            bool logQuery,
            bool logSnapshot,
            bool logPresentation,
            bool logLifecycle,
            bool logWorkspace,
            bool logSafetyFloorClamps,
            bool logPawnEvictions)
        {
            PawnEvictionTimeframeTicks = pawnEvictionTimeframeTicks;
            DevStressTestAllFauna = devStressTestAllFauna;
            ReportMetricsIntervalHours = reportMetricsIntervalHours;
            LogCacheMetrics = logCache;
            LogQueryMetrics = logQuery;
            LogSnapshotTableMetrics = logSnapshot;
            LogPresentationMetrics = logPresentation;
            LogLifecycleMetrics = logLifecycle;
            LogWorkspaceMetrics = logWorkspace;
            LogSafetyFloorClamps = logSafetyFloorClamps;
            LogPawnEvictions = logPawnEvictions;
        }

        #endregion
    }
}
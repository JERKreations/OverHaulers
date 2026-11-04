namespace OverHaulers
{
    /// <summary>
    /// [CORE-CONF-02] IMMUTABLE VALUE-TYPE RUNTIME & INFRASTRUCTURE CONFIGURATION
    /// Encapsulates engine infrastructure, cache lifecycle policies, fauna stress-testing,
    /// and diagnostic telemetry toggles. Resides under Source/Core/Configs/.
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

        #region 3. DERIVED TELEMETRY GATES

        /// <summary>Single owner of the master gate: all telemetry collection and reporting is off when the report interval is zero.</summary>
        public bool IsTelemetryEnabled => ReportMetricsIntervalHours > 0;

        /// <summary>Hit/miss counters feed both the cache and query report sections, so either flag enables collection.</summary>
        public bool CollectsCacheCounters => IsTelemetryEnabled && (LogCacheMetrics || LogQueryMetrics);

        public bool CollectsSnapshotCounters => IsTelemetryEnabled && LogSnapshotTableMetrics;

        public bool CollectsLifecycleCounters => IsTelemetryEnabled && LogLifecycleMetrics;

        public bool CollectsPresentationCounters => IsTelemetryEnabled && LogPresentationMetrics;

        #endregion
    }
}
namespace OverHaulers
{
    /// <summary>
    /// [CORE-CONF-02] IMMUTABLE VALUE-TYPE RUNTIME & INFRASTRUCTURE CONFIGURATION
    /// Encapsulates engine infrastructure, cache lifecycle policies, fauna stress-testing,
    /// and diagnostic telemetry toggles. Resides strictly under Source/Core/Data/.
    /// Holds zero duplicate constants, preserving SettingsDefaults as the Single Source of Truth (SSoT).
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
        public readonly bool LogLifecycleMetrics;
        public readonly bool LogPresentationMetrics;

        // Engine & Cache Invariants
        public readonly int MinFastPathTicks;
        public readonly int CacheCleanupInterval;
        public readonly int DynamicExpiryScaleK;
        public readonly float EfficiencyEpsilon;

        #endregion

        #region 2. LIFECYCLE: CONSTRUCTOR

        public RuntimeConfiguration(
            int pawnEvictionTimeframeTicks,
            bool devStressTestAllFauna,
            int reportMetricsIntervalHours,
            bool logCache,
            bool logQuery,
            bool logSnapshot,
            bool logLifecycle,
            bool logPresentation,
            int minFastPathTicks,
            int cacheCleanupInterval,
            int dynamicExpiryScaleK,
            float efficiencyEpsilon)
        {
            PawnEvictionTimeframeTicks = pawnEvictionTimeframeTicks;
            DevStressTestAllFauna = devStressTestAllFauna;
            ReportMetricsIntervalHours = reportMetricsIntervalHours;
            LogCacheMetrics = logCache;
            LogQueryMetrics = logQuery;
            LogSnapshotTableMetrics = logSnapshot;
            LogLifecycleMetrics = logLifecycle;
            LogPresentationMetrics = logPresentation;

            MinFastPathTicks = minFastPathTicks;
            CacheCleanupInterval = cacheCleanupInterval;
            DynamicExpiryScaleK = dynamicExpiryScaleK;
            EfficiencyEpsilon = efficiencyEpsilon;
        }

        #endregion
    }
}
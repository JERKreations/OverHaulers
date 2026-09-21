using System.Runtime.CompilerServices;
using System.Threading;

namespace OverHaulers
{
    /// <summary>
    /// Central registry tracking performance queries, cache hits, invalidation events, 
    /// and workspace counts. Utilizes gated plain increments on the main thread to eliminate
    /// atomic probe-overhead, and atomic structures exclusively for off-thread background workers.
    /// Resides under Source/Core/Registry/.
    /// </summary>
    public static class PerformanceTelemetry
    {
        #region 1. TELEMETRY STATE & HIGH-FREQUENCY COUNTERS

        private static int cacheHits = 0;
        private static int cacheMisses = 0;
        private static int backgroundQueries = 0;
        private static int invalidations = 0;
        private static int evictions = 0;

        private static bool isTelemetryActive = false;
        private static bool isCacheMetricsActive = false;

        /// <summary>Indicates whether any performance telemetry logging is active.</summary>
        public static bool IsActive => isTelemetryActive;

        /// <summary>Indicates whether high-frequency cache hit/miss tracking is active.</summary>
        public static bool IsCacheMetricsActive => isCacheMetricsActive;

        /// <summary>
        /// Synchronizes telemetry active flags against current ModSettings to ensure zero-cost branching.
        /// Invoked on mod startup, save load, and setting mutations.
        /// </summary>
        public static void SyncSettingsState()
        {
            var settings = OverHaulers.settings;
            if (settings == null || settings.reportMetricsIntervalHours <= 0)
            {
                isTelemetryActive = false;
                isCacheMetricsActive = false;
                return;
            }

            isTelemetryActive = true;
            isCacheMetricsActive = settings.logCacheMetrics || settings.logQueryMetrics;
        }

        /// <summary>
        /// Increments the main-thread cache hit counter without atomic bus locks when telemetry is active.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementCacheHits()
        {
            if (isCacheMetricsActive) cacheHits++;
        }

        /// <summary>
        /// Increments the main-thread cache miss (solver recalculation) counter without atomic bus locks when telemetry is active.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementCacheMisses()
        {
            if (isCacheMetricsActive) cacheMisses++;
        }

        /// <summary>
        /// Atomically increments the off-thread background worker query counter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementBackgroundQueries()
        {
            if (isTelemetryActive) Interlocked.Increment(ref backgroundQueries);
        }

        /// <summary>
        /// Increments the reactive cache invalidation counter on the main thread when telemetry is active.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementInvalidations()
        {
            if (isTelemetryActive) invalidations++;
        }

        /// <summary>
        /// Increments the pawn cache eviction counter on the main thread when telemetry is active.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementEvictions()
        {
            if (isTelemetryActive) evictions++;
        }

        #endregion

        #region 2. REPORT GENERATION & DISPATCH

        /// <summary>
        /// Flushes current counters, compiles performance hit ratios, and dispatches the localized report.
        /// </summary>
        /// <param name="elapsedHours">The game-hour duration elapsed since the last performance report.</param>
        public static void GenerateAndDispatchReport(int elapsedHours)
        {
            if (!isTelemetryActive) return;

            // Main-thread single-threaded counter reset (zero atomic bus stalls)
            int hits = cacheHits;
            cacheHits = 0;

            int misses = cacheMisses;
            cacheMisses = 0;

            // Background worker queries require atomic exchange due to off-thread pathfinders
            int bgQueries = Interlocked.Exchange(ref backgroundQueries, 0);

            int invs = invalidations;
            invalidations = 0;

            int evicts = evictions;
            evictions = 0;

            int totalQueries = hits + misses + bgQueries;
            int mainQueries = hits + misses;

            // BREAKPOINT ANCHOR: Main-Thread Hit Ratio Calculation
            float hitRatio = mainQueries > 0 
                ? ((float)hits / mainQueries) * 100f 
                : 100f;

            int activeWorkspaces = WorkspacePool.GetAllocatedWorkspaceCount();

            // Dispatch to centralized localized diagnostic reporter
            OHLog.Performance.DispatchCumulativeReport(
                elapsedHours,
                totalQueries, mainQueries, bgQueries,
                hitRatio, hits, misses,
                invs, evicts,
                activeWorkspaces
            );
        }

        #endregion
    }
}
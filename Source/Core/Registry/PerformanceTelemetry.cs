using System.Threading;

namespace OverHaulers
{
    /// <summary>
    /// Central registry tracking performance queries, cache hits, invalidation events, 
    /// and workspace counts. Utilizes atomic structures to prevent thread contention.
    /// </summary>
    public static class PerformanceTelemetry
    {
        #region 1. ATOMIC COUNTERS & INCREMENTORS

        private static int cacheHits = 0;
        private static int cacheMisses = 0;
        private static int backgroundQueries = 0;
        private static int invalidations = 0;
        private static int evictions = 0;

        /// <summary>Atomically increments the main-thread cache hit counter.</summary>
        public static void IncrementCacheHits() => Interlocked.Increment(ref cacheHits);

        /// <summary>Atomically increments the main-thread cache miss (solver recalculation) counter.</summary>
        public static void IncrementCacheMisses() => Interlocked.Increment(ref cacheMisses);

        /// <summary>Atomically increments the off-thread background worker query counter.</summary>
        public static void IncrementBackgroundQueries() => Interlocked.Increment(ref backgroundQueries);

        /// <summary>Atomically increments the reactive cache invalidation counter.</summary>
        public static void IncrementInvalidations() => Interlocked.Increment(ref invalidations);

        /// <summary>Atomically increments the pawn cache eviction counter.</summary>
        public static void IncrementEvictions() => Interlocked.Increment(ref evictions);

        #endregion

        #region 2. REPORT GENERATION & DISPATCH

        /// <summary>
        /// Flushes current atomic counters, compiles performance hit ratios, and dispatches the localized report.
        /// </summary>
        /// <param name="elapsedHours">The game-hour duration elapsed since the last performance report.</param>
        public static void GenerateAndDispatchReport(int elapsedHours)
        {
            // BREAKPOINT ANCHOR: Performance Counters Atomic Exchange
            int hits = Interlocked.Exchange(ref cacheHits, 0);
            int misses = Interlocked.Exchange(ref cacheMisses, 0);
            int bgQueries = Interlocked.Exchange(ref backgroundQueries, 0);
            int invs = Interlocked.Exchange(ref invalidations, 0);
            int evicts = Interlocked.Exchange(ref evictions, 0);

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
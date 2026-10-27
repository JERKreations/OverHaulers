using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace OverHaulers
{
    #region 1. [DIAG-01] TELEMETRY SNAPSHOT DATA MODEL

    /// <summary>
    /// [DIAG-01] Immutable snapshot of aggregated performance metrics across a reporting interval.
    /// Passed by readonly reference (in) to guarantee zero GC heap allocations during report assembly.
    /// </summary>
    public readonly struct TelemetrySnapshot
    {
        public readonly int ElapsedHours;
        public readonly int TotalQueries;
        public readonly int MainQueries;
        public readonly int BgQueries;
        public readonly float MainHitRatio;
        public readonly int Hits;
        public readonly int Misses;
        public readonly float BgHitRatio;
        public readonly int BgHits;
        public readonly int BgMisses;
        public readonly float TotalHitRatio;
        public readonly int Invalidations;
        public readonly int CulledInvalidations;
        public readonly float DebounceEfficiency;
        public readonly int Evictions;
        public readonly int MainCacheCount;
        public readonly int SnapshotOccupied;
        public readonly int TableCapacity;
        public readonly float AvgProbeDepth;
        public readonly int PeakProbe;
        public readonly int MaxProbeSteps;
        public readonly int Displacements;
        public readonly int Retries;
        public readonly int Timeouts;
        public readonly int ActiveWorkspaces;
        public readonly int PresentationActiveCount;
        public readonly int PresentationCapacity;
        public readonly int PresentationHits;
        public readonly int PresentationMisses;
        public readonly float PresentationHitRatio;
        public readonly int PresentationRecycles;

        public TelemetrySnapshot(
            int elapsedHours,
            int totalQueries, int mainQueries, int bgQueries,
            float mainHitRatio, int hits, int misses,
            float bgHitRatio, int bgHits, int bgMisses,
            float totalHitRatio,
            int invalidations, int culledInvalidations, float debounceEfficiency, int evictions,
            int mainCacheCount, int snapshotOccupied, int tableCapacity,
            float avgProbeDepth, int peakProbe, int maxProbeSteps, int displacements,
            int retries, int timeouts,
            int activeWorkspaces,
            int presentationActiveCount, int presentationCapacity,
            int presentationHits, int presentationMisses, float presentationHitRatio,
            int presentationRecycles)
        {
            ElapsedHours = elapsedHours;
            TotalQueries = totalQueries;
            MainQueries = mainQueries;
            BgQueries = bgQueries;
            MainHitRatio = mainHitRatio;
            Hits = hits;
            Misses = misses;
            BgHitRatio = bgHitRatio;
            BgHits = bgHits;
            BgMisses = bgMisses;
            TotalHitRatio = totalHitRatio;
            Invalidations = invalidations;
            CulledInvalidations = culledInvalidations;
            DebounceEfficiency = debounceEfficiency;
            Evictions = evictions;
            MainCacheCount = mainCacheCount;
            SnapshotOccupied = snapshotOccupied;
            TableCapacity = tableCapacity;
            AvgProbeDepth = avgProbeDepth;
            PeakProbe = peakProbe;
            MaxProbeSteps = maxProbeSteps;
            Displacements = displacements;
            Retries = retries;
            Timeouts = timeouts;
            ActiveWorkspaces = activeWorkspaces;
            PresentationActiveCount = presentationActiveCount;
            PresentationCapacity = presentationCapacity;
            PresentationHits = presentationHits;
            PresentationMisses = presentationMisses;
            PresentationHitRatio = presentationHitRatio;
            PresentationRecycles = presentationRecycles;
        }
    }

    #endregion

    /// <summary>
    /// [DIAG-01] PERFORMANCE TELEMETRY REGISTRY
    /// Central registry tracking performance queries, cache hits, invalidation events, 
    /// hash table probe metrics, and workspace counts. Utilizes gated plain increments on the main thread 
    /// to eliminate atomic bus overhead, and atomic operations exclusively for off-thread background workers.
    /// Resides under Source/Core/Registry/.
    /// </summary>
    public static class PerformanceTelemetry
    {
        #region 1. [DIAG-01] TELEMETRY STATE & HIGH-FREQUENCY COUNTERS

        // Main-thread query & cache metrics (zero-cost plain increments)
        private static int cacheHits = 0;
        private static int cacheMisses = 0;

        // Background worker query metrics (atomic operations for off-thread pathfinders)
        private static int backgroundHits = 0;
        private static int backgroundMisses = 0;

        // Invalidation & lifecycle metrics
        private static int invalidations = 0;
        private static int culledInvalidations = 0;
        private static int evictions = 0;

        // Snapshot table probing & displacement metrics (main-thread write path)
        private static int totalWriteProbes = 0;
        private static int totalWrites = 0;
        private static int maxWriteProbe = 0;
        private static int probeDisplacements = 0;

        // SeqLock concurrency contention metrics (atomic increments on conflict)
        private static int readerRetries = 0;
        private static int readerTimeouts = 0;

        // Presentation layer metrics (injected via downward delegates to preserve layer directionality)
        private static int presentationHits = 0;
        private static int presentationMisses = 0;
        private static int presentationRecycles = 0;

        /// <summary>Provider delegate retrieving active entry count from the presentation layer.</summary>
        public static Func<int> GetPresentationActiveCount;

        /// <summary>Provider delegate retrieving active capacity from the presentation layer.</summary>
        public static Func<int> GetPresentationCapacity;

        // Gating flags synchronized against Settings
        private static bool isTelemetryActive = false;
        private static bool isCacheMetricsActive = false;
        private static bool isSnapshotMetricsActive = false;
        private static bool isLifecycleMetricsActive = false;
        private static bool isPresentationMetricsActive = false;

        /// <summary>Indicates whether any performance telemetry logging is active.</summary>
        public static bool IsActive => isTelemetryActive;

        /// <summary>Indicates whether high-frequency cache hit/miss tracking is active.</summary>
        public static bool IsCacheMetricsActive => isCacheMetricsActive;

        /// <summary>Indicates whether snapshot table probe tracking is active.</summary>
        public static bool IsSnapshotMetricsActive => isSnapshotMetricsActive;

        /// <summary>Indicates whether lifecycle and invalidation tracking is active.</summary>
        public static bool IsLifecycleMetricsActive => isLifecycleMetricsActive;

        /// <summary>Indicates whether presentation model cache tracking is active.</summary>
        public static bool IsPresentationMetricsActive => isPresentationMetricsActive;

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
                isSnapshotMetricsActive = false;
                isLifecycleMetricsActive = false;
                isPresentationMetricsActive = false;
                return;
            }

            isTelemetryActive = true;
            isCacheMetricsActive = settings.logCacheMetrics || settings.logQueryMetrics;
            isSnapshotMetricsActive = settings.logSnapshotTableMetrics;
            isLifecycleMetricsActive = settings.logLifecycleMetrics;
            isPresentationMetricsActive = settings.logPresentationMetrics;
        }

        #endregion

        #region 2. [DIAG-01-A] QUERY & CACHE HIT COUNTERS

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
        /// Atomically increments the off-thread background cache hit counter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementBackgroundHit()
        {
            if (isTelemetryActive) Interlocked.Increment(ref backgroundHits);
        }

        /// <summary>
        /// Atomically increments the off-thread background cache miss counter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementBackgroundMiss()
        {
            if (isTelemetryActive) Interlocked.Increment(ref backgroundMisses);
        }

        /// <summary>
        /// Increments the presentation visual model cache hit counter on the main thread.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordPresentationHit()
        {
            if (isPresentationMetricsActive) presentationHits++;
        }

        /// <summary>
        /// Increments the presentation visual model cache miss counter on the main thread.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordPresentationMiss()
        {
            if (isPresentationMetricsActive) presentationMisses++;
        }

        /// <summary>
        /// Increments the presentation visual model LRU node recycle counter on the main thread.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordPresentationRecycle()
        {
            if (isPresentationMetricsActive) presentationRecycles++;
        }

        #endregion

        #region 3. [DIAG-01-B] LIFECYCLE & DEBOUNCE COUNTERS

        /// <summary>
        /// Increments the reactive cache invalidation counter on the main thread when telemetry is active.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementInvalidations()
        {
            if (isLifecycleMetricsActive) invalidations++;
        }

        /// <summary>
        /// Increments the count of redundant/burst invalidations safely culled by the debounce shield.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementCulledInvalidations()
        {
            if (isLifecycleMetricsActive) culledInvalidations++;
        }

        /// <summary>
        /// Increments the pawn cache eviction counter on the main thread when telemetry is active.
        /// Strictly reserved for true pawn lifecycle purges (death, despawn, or long-term inactivity).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementEvictions()
        {
            if (isLifecycleMetricsActive) evictions++;
        }

        #endregion

        #region 4. [DIAG-01-C] SNAPSHOT TABLE PROBING & CONCURRENCY COUNTERS

        /// <summary>
        /// Records write probe depth and slot displacements on the main thread during snapshot table insertions.
        /// </summary>
        /// <param name="probeSteps">The number of linear probe steps taken (1-based).</param>
        /// <param name="displaced">Whether the linear probe window was saturated, forcing a baseSlot overwrite.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordWriteProbe(int probeSteps, bool displaced)
        {
            if (!isSnapshotMetricsActive) return;

            totalWrites++;
            totalWriteProbes += probeSteps;

            if (probeSteps > maxWriteProbe)
            {
                maxWriteProbe = probeSteps;
            }

            if (displaced)
            {
                probeDisplacements++;
            }
        }

        /// <summary>
        /// Atomically records a background reader retry caused by an in-flight writer mutation or version mismatch.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementReaderRetry()
        {
            if (isSnapshotMetricsActive) Interlocked.Increment(ref readerRetries);
        }

        /// <summary>
        /// Atomically records a background reader timeout where MaxReaderRetries was exhausted.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementReaderTimeout()
        {
            if (isSnapshotMetricsActive) Interlocked.Increment(ref readerTimeouts);
        }

        #endregion

        #region 5. [DIAG-01-D] REPORT GENERATION & DISPATCH

        /// <summary>
        /// Flushes current counters, compiles performance hit ratios and probe statistics, and dispatches the localized report.
        /// </summary>
        /// <param name="elapsedHours">The game-hour duration elapsed since the last performance report.</param>
        public static void GenerateAndDispatchReport(int elapsedHours)
        {
            if (!isTelemetryActive) return;

            // 1. Main-thread counter reset (zero atomic bus stalls)
            int hits = cacheHits;
            cacheHits = 0;

            int misses = cacheMisses;
            cacheMisses = 0;

            int invs = invalidations;
            invalidations = 0;

            int culledInvs = culledInvalidations;
            culledInvalidations = 0;

            int evicts = evictions;
            evictions = 0;

            int wProbes = totalWriteProbes;
            totalWriteProbes = 0;

            int writes = totalWrites;
            totalWrites = 0;

            int peakProbe = maxWriteProbe;
            maxWriteProbe = 0;

            int displacements = probeDisplacements;
            probeDisplacements = 0;

            int presHits = presentationHits;
            presentationHits = 0;

            int presMisses = presentationMisses;
            presentationMisses = 0;

            int presRecycles = presentationRecycles;
            presentationRecycles = 0;

            // 2. Atomic counter reset for off-thread background workers
            int bgHits = Interlocked.Exchange(ref backgroundHits, 0);
            int bgMisses = Interlocked.Exchange(ref backgroundMisses, 0);
            int retries = Interlocked.Exchange(ref readerRetries, 0);
            int timeouts = Interlocked.Exchange(ref readerTimeouts, 0);

            // 3. Query & Hit Ratio Aggregation
            int mainQueries = hits + misses;
            int bgQueries = bgHits + bgMisses;
            int totalQueries = mainQueries + bgQueries;

            float mainHitRatio = mainQueries > 0 
                ? ((float)hits / mainQueries) * 100f 
                : 100f;

            float bgHitRatio = bgQueries > 0 
                ? ((float)bgHits / bgQueries) * 100f 
                : 100f;

            int totalHits = hits + bgHits;
            float totalHitRatio = totalQueries > 0 
                ? ((float)totalHits / totalQueries) * 100f 
                : 100f;

            // Presentation ratio aggregation
            int presQueries = presHits + presMisses;
            float presHitRatio = presQueries > 0
                ? ((float)presHits / presQueries) * 100f
                : 100f;

            int presActive = GetPresentationActiveCount != null ? GetPresentationActiveCount() : 0;
            int presCap = GetPresentationCapacity != null ? GetPresentationCapacity() : 0;

            // 4. Debounce Efficiency Calculation
            int totalInvalEvents = invs + culledInvs;
            float debounceEfficiency = totalInvalEvents > 0 
                ? ((float)culledInvs / totalInvalEvents) * 100f 
                : 0f;

            // 5. Probe Diagnostics
            float avgProbeDepth = writes > 0 
                ? ((float)wProbes / writes) 
                : 1f;

            int activeWorkspaces = WorkspacePool.GetAllocatedWorkspaceCount();
            int mainCacheCount = PawnDataRegistry.CachedPawnCount;
            int snapshotOccupied = MassSnapshotCache.OccupiedSlotsCount;

            // 6. Dispatch to centralized localized diagnostic reporter
            var snapshot = new TelemetrySnapshot(
                elapsedHours,
                // Query Metrics
                totalQueries, mainQueries, bgQueries,
                // Hit Ratios
                mainHitRatio, hits, misses,
                bgHitRatio, bgHits, bgMisses,
                totalHitRatio,
                // Lifecycle & Debounce
                invs, culledInvs, debounceEfficiency, evicts,
                // Snapshot Table Health & Parity
                mainCacheCount, snapshotOccupied, MassSnapshotCache.TableCapacity,
                avgProbeDepth, peakProbe, MassSnapshotCache.MaxProbeSteps, displacements,
                retries, timeouts,
                // Pooled Workspaces
                activeWorkspaces,
                // Presentation UI Model Cache
                presActive, presCap, presHits, presMisses, presHitRatio, presRecycles
            );

            OHLog.Performance.DispatchCumulativeReport(in snapshot);
        }

        #endregion
    }
}
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-01] Centralized registry for caching and retrieving calculated Caravan Mass Capacity data for pawns.
    /// </summary>
    public static class PawnDataRegistry
    {
        #region 1. FIELDS & METADATA STORAGE

        private static readonly Dictionary<int, CachedMassData> capacityCache = new Dictionary<int, CachedMassData>();
        private static readonly List<int> staleKeysScratch = new List<int>(256);

        private static volatile int lastCapturedMainThreadTick = 0;
        private static volatile int lastCleanupTick = 0;
        private static int activeCleanupSentinel = 0;

        [ThreadStatic]
        private static bool isEvaluatingReentrant;

        public static int CurrentTick => lastCapturedMainThreadTick;

        #endregion

        #region 2. FAST-PATH PREDICATES

        /// <summary>
        /// Primary gate determining if a live pawn should be processed by OverHaulers.
        /// By default, only caravan-capable pawns are evaluated. When the dev testing toggle is active,
        /// all species are evaluated to facilitate live sandbox testing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanCarryCaravanMass(Pawn pawn)
        {
            if (pawn == null) return false;

            Settings settings = OverHaulers.settings;
            if (settings != null && settings.devAllowNonPackSpeciesInLivePlay)
            {
                return true;
            }

            return IsCaravanCapable(pawn);
        }

        /// <summary>
        /// Evaluates whether a live pawn is officially designated for caravan mass transport.
        /// Strictly requires Humanlike, designated pack animal, or hauling-capable mechanoid.
        /// </summary>
        /// <param name="pawn">The pawn being evaluated for caravan capability.</param>
        /// <returns>True if the pawn is designated for caravan transport; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCaravanCapable(Pawn pawn)
        {
            if (pawn == null) return false;
            RaceProperties race = pawn.RaceProps;
            if (race == null) return false;

            if (race.Humanlike) return true;
            if (race.packAnimal) return true;

            if (race.IsMechanoid)
            {
                WorkTypeDef hauling = WorkTypeDefOf.Hauling ?? DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling");
                if (hauling != null && !pawn.WorkTypeIsDisabled(hauling))
                {
                    return true;
                }

                if (race.mechEnabledWorkTypes != null)
                {
                    for (int i = 0; i < race.mechEnabledWorkTypes.Count; i++)
                    {
                        WorkTypeDef w = race.mechEnabledWorkTypes[i];
                        if (w != null && (w == hauling || w.defName == "Hauling")) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Evaluates whether a species ThingDef archetype is designated for caravan transport.
        /// Used for startup batch sweeps and archetype calibration before pawn spawning.
        /// </summary>
        /// <param name="raceDef">The ThingDef representing the species archetype.</param>
        /// <returns>True if the species archetype is designated for caravan transport; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCaravanCapable(ThingDef raceDef)
        {
            if (raceDef == null || raceDef.race == null) return false;
            RaceProperties race = raceDef.race;

            if (race.Humanlike) return true;
            if (race.packAnimal) return true;

            if (race.IsMechanoid && race.mechEnabledWorkTypes != null)
            {
                for (int i = 0; i < race.mechEnabledWorkTypes.Count; i++)
                {
                    WorkTypeDef w = race.mechEnabledWorkTypes[i];
                    if (w != null && (w == WorkTypeDefOf.Hauling || w.defName == "Hauling")) return true;
                }
            }

            return false;
        }

        #endregion

        #region 3. [CACHE-01] PRIMARY CACHE ENTRY POINTS

        /// <summary>
        /// Retrieves the offset of the pawn's mass capacity relative to the baseline capacity.
        /// </summary>
        /// <param name="pawn">The pawn whose mass capacity offset is being retrieved.</param>
        /// <param name="baselineCapacity">The baseline capacity used for the calculation.</param>
        /// <returns>The offset of the pawn's mass capacity relative to the baseline capacity.</returns>
        public static float GetOffset(Pawn pawn, float baselineCapacity)
        {
            if (pawn == null || !CanCarryCaravanMass(pawn)) return 0f;

            if (!UnityData.IsInMainThread)
            {
                return MassSnapshotCache.GetOffsetThreadSafe(pawn.thingIDNumber);
            }

            if (isEvaluatingReentrant)
            {
                if (capacityCache.TryGetValue(pawn.thingIDNumber, out CachedMassData reentrantNode))
                {
                    return reentrantNode.Offset;
                }
                return 0f;
            }

            if (pawn.Dead || pawn.Suspended)
            {
                EvictCacheEntry(pawn.thingIDNumber);
                return 0f;
            }

            int currentTick = GetSafeCurrentTick();

            if (!capacityCache.TryGetValue(pawn.thingIDNumber, out CachedMassData cachedNode))
            {
                cachedNode = new CachedMassData();
                capacityCache.Add(pawn.thingIDNumber, cachedNode);
                cachedNode.IsStale = true;
            }

            if (IsCacheNodeValid(cachedNode, baselineCapacity, currentTick, cachedNode.CalculatedTick))
            {
                cachedNode.BaselineCapacity = baselineCapacity;
                PerformanceTelemetry.IncrementCacheHits();
                return cachedNode.Offset;
            }

            int jitteredExpiryDuration = GetCacheExpiryDuration(pawn.thingIDNumber);
            int elapsedTicks = currentTick - cachedNode.CalculatedTick;

            if (elapsedTicks >= jitteredExpiryDuration || cachedNode.IsStale)
            {
                try
                {
                    isEvaluatingReentrant = true;
                    SolveAndCacheEntry(pawn, cachedNode, baselineCapacity, currentTick, shouldCompileUIProperties: false, out _);
                }
                finally
                {
                    isEvaluatingReentrant = false;
                }
                return cachedNode.Offset;
            }

            cachedNode.BaselineCapacity = baselineCapacity;
            PerformanceTelemetry.IncrementCacheHits();
            return cachedNode.Offset;
        }

        /// <summary>
        /// Retrieves the detailed mass capacity model for the specified pawn.
        /// </summary>
        /// <param name="pawn">The pawn whose detailed mass capacity model is being requested.</param>
        /// <param name="baselineCapacity">The baseline capacity used for the calculation.</param>
        /// <returns>The detailed mass capacity model for the pawn.</returns>
        public static MassCapacityModel GetDetailedModel(Pawn pawn, float baselineCapacity)
        {
            if (pawn == null || !CanCarryCaravanMass(pawn)) return MassCapacityModel.Empty;

            if (!UnityData.IsInMainThread) return MassCapacityModel.Empty;

            if (isEvaluatingReentrant)
            {
                if (capacityCache.TryGetValue(pawn.thingIDNumber, out CachedMassData reentrantNode) && reentrantNode.FullModel != null)
                {
                    return reentrantNode.FullModel;
                }
                return MassCapacityModel.Empty;
            }

            if (pawn.Dead || pawn.Suspended)
            {
                EvictCacheEntry(pawn.thingIDNumber);
                return MassCapacityModel.Empty;
            }

            int currentTick = GetSafeCurrentTick();
            bool isVerboseMode = OverHaulers.settings?.verboseBreakdown ?? false;

            if (!capacityCache.TryGetValue(pawn.thingIDNumber, out CachedMassData cachedNodeRef))
            {
                cachedNodeRef = new CachedMassData();
                capacityCache.Add(pawn.thingIDNumber, cachedNodeRef);
                cachedNodeRef.IsStale = true;
            }

            bool isFullModelFresh = IsCacheNodeValid(cachedNodeRef, baselineCapacity, currentTick, cachedNodeRef.FullModelCalculatedTick);

            if (!isVerboseMode && isFullModelFresh && cachedNodeRef.FullModel != null)
            {
                cachedNodeRef.BaselineCapacity = baselineCapacity;
                PerformanceTelemetry.IncrementCacheHits();
                return cachedNodeRef.FullModel;
            }

            int jitteredExpiryDuration = GetCacheExpiryDuration(pawn.thingIDNumber);
            int elapsedFullModelTicks = currentTick - cachedNodeRef.FullModelCalculatedTick;

            if (isVerboseMode || elapsedFullModelTicks >= jitteredExpiryDuration || cachedNodeRef.IsStale || cachedNodeRef.FullModel == null)
            {
                AnatomicalWorkspace workspace = null;
                try
                {
                    try
                    {
                        isEvaluatingReentrant = true;
                        SolveAndCacheEntry(pawn, cachedNodeRef, baselineCapacity, currentTick, shouldCompileUIProperties: true, out workspace);
                    }
                    finally
                    {
                        isEvaluatingReentrant = false;
                    }

                    if (cachedNodeRef.FullModel == null)
                    {
                        cachedNodeRef.FullModel = new MassCapacityModel();
                    }

                    cachedNodeRef.FullModel.ResetPool();
                    ViewProjection.RebuildDetailedModel_Internal(pawn, cachedNodeRef.FullModel, cachedNodeRef.Offset, cachedNodeRef.BaselineCapacity, workspace);
                    cachedNodeRef.FullModelCalculatedTick = currentTick;

                    return cachedNodeRef.FullModel;
                }
                finally
                {
                    workspace?.Clear();
                }
            }

            cachedNodeRef.BaselineCapacity = baselineCapacity;
            PerformanceTelemetry.IncrementCacheHits();
            return cachedNodeRef.FullModel;
        }

        #endregion

        #region 4. UNIFIED SOLVER & CACHE PIPELINE HELPER

        /// <summary>
        /// Solves the mass capacity for the specified pawn and caches the result in the provided node.
        /// </summary>
        /// <param name="pawn">The pawn whose mass capacity is being solved.</param>
        /// <param name="node">The cache node where the solved mass capacity will be stored.</param>
        /// <param name="baselineCapacity">The baseline capacity used for the calculation.</param>
        /// <param name="currentTick">The current game tick used to timestamp the calculation.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI properties should be compiled during the calculation.</param>
        /// <param name="workspace">The anatomical workspace used for the calculation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SolveAndCacheEntry(
            Pawn pawn, 
            CachedMassData node, 
            float baselineCapacity, 
            int currentTick, 
            bool shouldCompileUIProperties, 
            out AnatomicalWorkspace workspace)
        {
            float bioBaseline;
            float solvedOffset = MassCapacitySolver.SolveMassCapacityOffset(
                pawn, baselineCapacity, out bioBaseline, OverHaulers.settings, 
                shouldCompileUIProperties, out workspace);

            node.Offset = solvedOffset;
            node.CalculatedTick = currentTick;
            node.BaselineCapacity = baselineCapacity;
            node.IsStale = false;

            float calculatedMultiplier = bioBaseline > 0f ? ((bioBaseline + solvedOffset) / bioBaseline) : 1.0f;
            MassSnapshotCache.WriteState(pawn.thingIDNumber, solvedOffset, calculatedMultiplier);

            PerformanceTelemetry.IncrementCacheMisses();

            if (!shouldCompileUIProperties)
            {
                workspace?.Clear();
                workspace = null;
            }
        }

        /// <summary>
        /// Determines whether the specified cache node is still valid based on its age and staleness.
        /// </summary>
        /// <param name="node">The cache node to validate.</param>
        /// <param name="baselineCapacity">The baseline capacity to compare against the node's stored baseline capacity.</param>
        /// <param name="currentTick">The current game tick used to determine the age of the cache node.</param>
        /// <param name="calculatedTick">The tick at which the cache node was last calculated.</param>
        /// <returns>True if the cache node is still valid; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCacheNodeValid(CachedMassData node, float baselineCapacity, int currentTick, int calculatedTick)
        {
            if (node == null) return false;

            if (Math.Abs(node.BaselineCapacity - baselineCapacity) >= SettingsDefaults.EfficiencyEpsilon)
            {
                node.IsStale = true;
            }

            int age = currentTick - calculatedTick;
            return age >= 0 && age < SettingsDefaults.DefaultCacheMinFastPathTicks && !node.IsStale;
        }

        #endregion

        #region 5. UNIFIED LIFECYCLE, INVALIDATION & EVICTION PIPELINE

        /// <summary>
        /// Determines whether the cache entry for the specified pawn should be culled due to invalidation.
        /// </summary>
        /// <param name="thingID">The unique identifier of the pawn whose cache entry is being checked for invalidation.</param>
        /// <returns>True if the cache entry should be culled due to invalidation; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldCullInvalidation(int thingID)
        {
            if (!UnityData.IsInMainThread) return false;

            if (capacityCache.TryGetValue(thingID, out CachedMassData cachedNode))
            {
                return cachedNode.IsStale;
            }

            return false;
        }

        /// <summary>
        /// Reactive cache invalidator. Marks the pawn's main-thread cache entry as stale,
        /// evicts the entry from the background snapshot cache, and increments the invalidation telemetry.
        /// </summary>
        /// <param name="thingID">The unique identifier of the pawn to invalidate.</param>
        public static void Invalidate(int thingID)
        {
            AssertMainThread("OverHaulers_Context_CacheInvalidation".Translate().ToString());

            bool invalidatedMain = false;
            if (capacityCache.TryGetValue(thingID, out CachedMassData cachedNode))
            {
                if (!cachedNode.IsStale)
                {
                    cachedNode.IsStale = true;
                    invalidatedMain = true;
                }
            }

            bool evictedGlobal = MassSnapshotCache.Evict(thingID);

            if (invalidatedMain || evictedGlobal)
            {
                PerformanceTelemetry.IncrementInvalidations();
            }
        }

        /// <summary>
        /// Evicts the cache entry for the specified pawn, removing it from both the main and global caches.
        /// </summary>
        /// <param name="thingID">The unique identifier of the pawn whose cache entry is to be evicted.</param>
        public static void EvictCacheEntry(int thingID)
        {
            AssertMainThread("OverHaulers_Context_CacheEviction".Translate().ToString());

            bool removedMain = capacityCache.Remove(thingID);
            bool removedGlobal = MassSnapshotCache.Evict(thingID);

            if (removedMain || removedGlobal)
            {
                PerformanceTelemetry.IncrementEvictions();
                OHLog.Performance.RecordPawnEvicted(thingID);
            }
        }

        /// <summary>
        /// Flushes all main-thread and thread-safe snapshot capacity caches, scratch buffers,
        /// and resets tick counters. Invoked on save loads, world resets, and setting mutations.
        /// </summary>
        public static void ClearAllCaches()
        {
            capacityCache.Clear();
            staleKeysScratch.Clear();
            lastCapturedMainThreadTick = 0;
            lastCleanupTick = 0;
            System.Threading.Interlocked.Exchange(ref activeCleanupSentinel, 0);

            MassSnapshotCache.Reset();
            WorkspacePool.ClearPools();
            OHLog.Performance.ClearTraceBuffers();
        }

        #endregion

        #region 6. METADATA READERS & CACHE WRITERS

        /// <summary>
        /// Attempts to retrieve the cached baseline Caravan Mass Capacity for a pawn if valid and fresh.
        /// Returns false if the cache entry is marked stale, forcing a native baseline re-evaluation.
        /// </summary>
        /// <param name="thingID">The unique identifier of the pawn whose cached baseline is being retrieved.</param>
        /// <param name="baseline">The cached baseline Caravan Mass Capacity if available and fresh; otherwise, 0.</param>
        /// <returns>True if a valid and fresh cached baseline was found; otherwise, false.</returns>
        public static bool TryGetCachedBaseline(int thingID, out float baseline)
        {
            if (!UnityData.IsInMainThread) { baseline = 0f; return false; }

            // BREAKPOINT ANCHOR: Stale Gate Check
            // Prevents returning outdated baseline values when gear, hediffs, or drivers mutate
            if (capacityCache.TryGetValue(thingID, out CachedMassData cachedNode) && !cachedNode.IsStale)
            {
                baseline = cachedNode.BaselineCapacity;
                return true;
            }

            baseline = 0f;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve the cached Caravan Mass Capacity offset for a pawn if valid and fresh.
        /// </summary>
        /// <param name="thingID">The unique identifier of the pawn whose cached offset is being retrieved.</param>
        /// <param name="offset">The cached Caravan Mass Capacity offset if available and fresh; otherwise, 0.</param>
        /// <returns>True if a valid and fresh cached offset was found; otherwise, false.</returns>
        public static bool TryGetCachedOffset(int thingID, out float offset)
        {
            if (!UnityData.IsInMainThread) { offset = 0f; return false; }

            if (capacityCache.TryGetValue(thingID, out CachedMassData cachedNode) && !cachedNode.IsStale)
            {
                offset = cachedNode.Offset;
                return true;
            }

            offset = 0f;
            return false;
        }

        #endregion

        #region 7. OUT-OF-BAND CLEANUP & HELPER UTILITIES

        /// <summary>
        /// Updates the internal main thread tick counter and triggers cache cleanup if the current tick differs from the last captured tick.
        /// </summary>
        /// <param name="currentTick">The current game tick used to determine if cache cleanup should be performed.</param>
        /// <remarks>
        /// This method should be invoked from the main thread to maintain thread safety.
        /// </remarks>
        public static void UpdateTick(int currentTick)
        {
            if (currentTick != lastCapturedMainThreadTick)
            {
                lastCapturedMainThreadTick = currentTick;
                MaybeCleanupCache(currentTick);
            }
        }

        /// <summary>
        /// Updates the internal tick counter and triggers cache cleanup if necessary.
        /// </summary>
        /// <param name="currentTick">The current game tick used to determine if cache cleanup should be performed.</param>
        /// <remarks>
        /// This method should be invoked from the main thread to maintain thread safety.
        /// </remarks>
        private static void MaybeCleanupCache(int currentTick)
        {
            int interval = SettingsDefaults.CacheCleanupInterval;

            if (currentTick < lastCleanupTick || (currentTick - lastCleanupTick) > interval)
            {
                if (System.Threading.Interlocked.CompareExchange(ref activeCleanupSentinel, 1, 0) == 0)
                {
                    try
                    {
                        if (currentTick < lastCleanupTick || (currentTick - lastCleanupTick) > interval)
                        {
                            AssertMainThread("OverHaulers_Context_RegistryCleanup".Translate().ToString());
                            lastCleanupTick = currentTick;

                            int longTermInactivityThreshold = OverHaulers.settings != null 
                                ? OverHaulers.settings.PawnEvictionTimeframeTicks 
                                : SettingsDefaults.PawnEvictionTimeframeHours * GenDate.TicksPerHour;

                            staleKeysScratch.Clear();

                            foreach (var keyValuePair in capacityCache)
                            {
                                int age = currentTick - keyValuePair.Value.CalculatedTick;
                                if (age > longTermInactivityThreshold)
                                {
                                    staleKeysScratch.Add(keyValuePair.Key);
                                }
                            }

                            for (int i = 0; i < staleKeysScratch.Count; i++)
                            {
                                EvictCacheEntry(staleKeysScratch[i]);
                            }

                            staleKeysScratch.Clear();
                            WorkspacePool.CleanDeadReferences();
                        }
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref activeCleanupSentinel, 0);
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the cache expiry duration for the specified pawn ID, incorporating a base duration and optional jitter.
        /// </summary>
        /// <param name="pawnId">The unique identifier of the pawn for which to retrieve the cache expiry duration.</param>
        /// <returns>The calculated cache expiry duration for the specified pawn ID.</returns>
        private static int GetCacheExpiryDuration(int pawnId)
        {
            int expiryBase = SettingsDefaults.CacheExpiryBase; 
            int expiryJitter = SettingsDefaults.CacheExpiryJitter; 

            return expiryBase + (expiryJitter > 0 ? (pawnId % expiryJitter) : 0);
        }

        /// <summary>
        /// Retrieves the current game tick in a thread-safe manner, ensuring that the value is updated on the main thread if necessary.
        /// </summary>
        /// <returns>The current game tick, updated in a thread-safe manner.</returns>
        private static int GetSafeCurrentTick()
        {
            if (UnityData.IsInMainThread)
            {
                if (Current.ProgramState == ProgramState.Playing && Find.TickManager != null)
                {
                    int ticks = Find.TickManager.TicksGame;
                    if (ticks != lastCapturedMainThreadTick)
                    {
                        UpdateTick(ticks);
                    }
                }
            }
            return lastCapturedMainThreadTick;
        }

        /// <summary>
        /// Asserts that the current code is executing on the main thread. Throws an exception if not.
        /// </summary>
        /// <param name="context">A description of the context in which the main thread assertion is being checked.</param>
        /// <exception cref="InvalidOperationException"></exception>
        private static void AssertMainThread(string context)
        {
            if (!UnityData.IsInMainThread)
            {
                throw new InvalidOperationException($"[Over Haulers] Thread-safety violation: {context} must be executed exclusively on the main thread.");
            }
        }

        #endregion
    }
}
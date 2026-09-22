using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-01] Centralized registry for caching and retrieving calculated Caravan Mass Capacity data for pawns.
    /// Resides under Source/Core/Registry/.
    /// </summary>
    public static class PawnDataRegistry
    {
        #region 1. FIELDS & METADATA STORAGE

        // pre-seed the dictionary with an initial capacity to reduce rehashing during early game ticks.
        private static readonly Dictionary<int, CachedMassData> capacityCache = new Dictionary<int, CachedMassData>(512);

        /// Scratch list for temporarily storing stale keys during cleanup operations.
        private static readonly List<int> staleKeysScratch = new List<int>(256);

        private static volatile int lastCapturedMainThreadTick = 0;
        private static volatile int lastCleanupTick = 0;
        private static int activeCleanupSentinel = 0;

        // CACHED EXPIRY ARITHMETIC: Eliminates Mathf.Sqrt calls when active population is stable
        private static int cachedActiveCount = -1;
        private static int cachedExpiryBase = SettingsDefaults.DefaultCacheMinFastPathTicks;

        [ThreadStatic]
        private static int currentEvaluatingPawnId;

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
        /// Attempts to retrieve a fresh, non-stale cached mass capacity offset for the specified pawn ID.
        /// Bypasses species baseline calibration, monitor locks, and Pawn.BodySize evaluations on cache hits.
        /// </summary>
        /// <param name="pawnId">The unique thingIDNumber of the pawn.</param>
        /// <param name="offset">The cached capacity offset if valid.</param>
        /// <returns>True if a valid, unexpired cached offset was found; otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFreshOffset(int pawnId, out float offset)
        {
            if (!UnityData.IsInMainThread || currentEvaluatingPawnId == pawnId)
            {
                offset = 0f;
                return false;
            }

            // Attempt to retrieve a fresh cached offset first
            if (capacityCache.TryGetValue(pawnId, out CachedMassData cachedNode) && !cachedNode.IsStale)
            {
                int currentTick = GetSafeCurrentTick();
                int elapsedTicks = currentTick - cachedNode.CalculatedTick;

                if (elapsedTicks >= 0)
                {
                    // Ultra-fast path: within minimum fast-path ticks (bypasses dynamic expiry math)
                    if (elapsedTicks < SettingsDefaults.DefaultCacheMinFastPathTicks ||
                        elapsedTicks < GetCacheExpiryDuration(pawnId))
                    {
                        PerformanceTelemetry.IncrementCacheHits();
                        offset = cachedNode.Offset;
                        return true;
                    }
                }
            }

            offset = 0f;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve the fresh, non-stale final clamped mass capacity (kg) for the specified pawn ID.
        /// Bypasses species baseline calibration, monitor locks, and Pawn.BodySize evaluations on cache hits.
        /// </summary>
        /// <param name="pawnId">The unique thingIDNumber of the pawn.</param>
        /// <param name="finalCapacity">The cached final clamped capacity in kilograms if valid.</param>
        /// <returns>True if a valid, unexpired cached entry was found; otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFreshCapacity(int pawnId, out float finalCapacity)
        {
            if (!UnityData.IsInMainThread || currentEvaluatingPawnId == pawnId)
            {
                finalCapacity = 0f;
                return false;
            }

            // Attempt to retrieve a fresh cached offset first
            if (capacityCache.TryGetValue(pawnId, out CachedMassData cachedNode) && !cachedNode.IsStale)
            {
                int currentTick = GetSafeCurrentTick();
                int elapsedTicks = currentTick - cachedNode.CalculatedTick;

                // Ultra-fast path: within minimum fast-path ticks (bypasses dynamic expiry math)
                if (elapsedTicks >= 0)
                {
                    if (elapsedTicks < SettingsDefaults.DefaultCacheMinFastPathTicks ||
                        elapsedTicks < GetCacheExpiryDuration(pawnId))
                    {
                        PerformanceTelemetry.IncrementCacheHits();
                        finalCapacity = cachedNode.FinalCapacity;
                        return true;
                    }
                }
            }

            finalCapacity = 0f;
            return false;
        }

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

            int pawnId = pawn.thingIDNumber;

            // BREAKPOINT ANCHOR: Exact Pawn Reentrancy Shield
            // If the exact same pawn is already being evaluated on this thread (e.g. recursive stat query),
            // return its existing or baseline cache value to prevent infinite recursion.
            if (currentEvaluatingPawnId == pawnId)
            {
                if (capacityCache.TryGetValue(pawnId, out CachedMassData reentrantNode))
                {
                    return reentrantNode.Offset;
                }
                return 0f;
            }

            if (pawn.Dead || pawn.Suspended)
            {
                EvictCacheEntry(pawnId);
                return 0f;
            }

            int currentTick = GetSafeCurrentTick();

            if (!capacityCache.TryGetValue(pawnId, out CachedMassData cachedNode))
            {
                cachedNode = new CachedMassData();
                capacityCache.Add(pawnId, cachedNode);
                cachedNode.IsStale = true;
            }

            if (IsCacheNodeValid(cachedNode, baselineCapacity, currentTick, cachedNode.CalculatedTick))
            {
                cachedNode.BaselineCapacity = baselineCapacity;
                PerformanceTelemetry.IncrementCacheHits();
                return cachedNode.Offset;
            }

            int jitteredExpiryDuration = GetCacheExpiryDuration(pawnId);
            int elapsedTicks = currentTick - cachedNode.CalculatedTick;

            if (elapsedTicks >= jitteredExpiryDuration || cachedNode.IsStale)
            {
                int prevEvaluatingPawnId = currentEvaluatingPawnId;
                try
                {
                    currentEvaluatingPawnId = pawnId;
                    SolveAndCacheEntry(pawn, cachedNode, baselineCapacity, currentTick, shouldCompileUIProperties: false, out _);
                }
                finally
                {
                    currentEvaluatingPawnId = prevEvaluatingPawnId;
                }
                return cachedNode.Offset;
            }

            cachedNode.BaselineCapacity = baselineCapacity;
            PerformanceTelemetry.IncrementCacheHits();
            return cachedNode.Offset;
        }

        /// <summary>
        /// Retrieves the fully clamped, gameplay-ready Caravan Mass Capacity (kg) for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn whose caravan mass capacity is being retrieved.</param>
        /// <param name="baselineCapacity">The species baseline capacity.</param>
        /// <returns>The final clamped capacity in kilograms (kg).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetCapacity(Pawn pawn, float baselineCapacity)
        {
            if (pawn == null || !CanCarryCaravanMass(pawn)) return 0f;
            GetOffset(pawn, baselineCapacity); // Ensures cache entry is evaluated and fresh
            return capacityCache.TryGetValue(pawn.thingIDNumber, out CachedMassData node) ? node.FinalCapacity : baselineCapacity;
        }

        /// <summary>
        /// Resolves species baseline capacity via baseline calibration.
        /// </summary>
        /// <param name="pawn">The pawn whose baseline capacity is being resolved.</param>
        /// <returns>The resolved baseline capacity for the pawn.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ResolveBaseline(Pawn pawn)
        {
            if (pawn == null) return 0f;
            return SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
        }

        /// <summary>
        /// Retrieves pre-computed fleet breakdown metrics for a pawn without allocating UI models.
        /// </summary>
        /// <param name="pawn">The pawn whose fleet metrics are being retrieved.</param>
        /// <param name="baselineCapacity">The species baseline capacity.</param>
        /// <param name="prostheticBoost">Output parameter for the prosthetic boost.</param>
        /// <param name="healthDeficit">Output parameter for the health deficit.</param>
        /// <param name="athleticOffset">Output parameter for the athletic offset.</param>
        /// <returns>True if the fleet metrics were successfully retrieved; otherwise, false.</returns>
        public static bool TryGetFleetMetrics(Pawn pawn, float baselineCapacity, out float prostheticBoost, out float healthDeficit, out float athleticOffset)
        {
            prostheticBoost = 0f;
            healthDeficit = 0f;
            athleticOffset = 0f;

            if (pawn == null || !CanCarryCaravanMass(pawn)) return false;
            GetOffset(pawn, baselineCapacity);

            if (capacityCache.TryGetValue(pawn.thingIDNumber, out CachedMassData node))
            {
                prostheticBoost = node.ProstheticBoost;
                healthDeficit = node.HealthDeficit;
                athleticOffset = node.AthleticOffset;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Retrieves the detailed mass capacity model for the specified pawn.
        /// </summary>
        /// <param name="pawn">The pawn whose detailed mass capacity model is being requested.</param>
        /// <param name="baselineCapacity">The baseline capacity used for the calculation.</param>
        /// <returns>The detailed mass capacity model for the pawn.</returns>
        public static MassCapacityModel GetDetailedModel(Pawn pawn, float baselineCapacity)
        {
            // Early exit if the pawn is null or cannot carry caravan mass
            if (pawn == null || !CanCarryCaravanMass(pawn)) return null;

            int pawnId = pawn.thingIDNumber;
            int currentTick = GetSafeCurrentTick();

            // Attempt to retrieve the cached mass data for the pawn
            if (currentEvaluatingPawnId == pawnId)
            {
                if (capacityCache.TryGetValue(pawnId, out CachedMassData reentrantNode) && reentrantNode.FullModel != null)
                {
                    return reentrantNode.FullModel;
                }
                return null;
            }

            // Store the previous evaluating pawn ID to restore it after processing
            int prevEvaluatingPawnId = currentEvaluatingPawnId;
            try
            {
                currentEvaluatingPawnId = pawnId;

                // Attempt to retrieve the cached mass data for the pawn after setting the current evaluating pawn ID
                if (!capacityCache.TryGetValue(pawnId, out CachedMassData cachedNodeRef))
                {
                    cachedNodeRef = new CachedMassData();
                    capacityCache.Add(pawnId, cachedNodeRef);
                }

                int expiry = GetCacheExpiryDuration(pawnId);
                bool isOffsetStale = cachedNodeRef.IsStale || (currentTick - cachedNodeRef.CalculatedTick) > expiry;
                bool isModelStale = cachedNodeRef.FullModel == null || (currentTick - cachedNodeRef.FullModelCalculatedTick) > expiry;

                // Recompute the detailed model if either the offset or the model is stale.
                if (isOffsetStale || isModelStale)
                {
                    AnatomicalWorkspace workspace = null;
                    try
                    {
                        SolveAndCacheEntry(pawn, cachedNodeRef, baselineCapacity, currentTick, true, out workspace);

                        if (cachedNodeRef.FullModel == null)
                        {
                            cachedNodeRef.FullModel = new MassCapacityModel();
                        }

                        cachedNodeRef.FullModel.ResetPool();

                        // Seed presentation model with domain values already solved in Core
                        cachedNodeRef.FullModel.Offset = cachedNodeRef.Offset;
                        cachedNodeRef.FullModel.FinalCapacity = cachedNodeRef.FinalCapacity;
                        cachedNodeRef.FullModel.TotalMultiplier = cachedNodeRef.TotalMultiplier;

                        ViewProjection.RebuildDetailedModel_Internal(pawn, cachedNodeRef.FullModel, cachedNodeRef.Offset, cachedNodeRef.BaselineCapacity, workspace);
                        cachedNodeRef.FullModelCalculatedTick = currentTick;

                        return cachedNodeRef.FullModel;
                    }
                    finally
                    {
                        if (workspace != null)
                        {
                            WorkspacePool.ReleaseWorkspace(workspace);
                        }
                    }
                }

                PerformanceTelemetry.IncrementCacheHits();
                return cachedNodeRef.FullModel;
            }
            finally
            {
                currentEvaluatingPawnId = prevEvaluatingPawnId;
            }
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
        /// <returns>The anatomical workspace used during the calculation.</returns>
        /// <remarks>
        /// This method calculates the mass capacity offset for the specified pawn, enforces safety rules,
        /// and caches the results in the provided node. The anatomical workspace is used to store intermediate
        /// calculation details and is returned to the caller for further inspection if needed.
        /// </remarks>
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

            // Enforce domain rules (safety floor & multiplier) directly in Core
            float finalCapacity = MassCapacitySolver.EnforceSafetyFloor(bioBaseline + solvedOffset, pawn.LabelShortCap);
            float calculatedMultiplier = bioBaseline > 0f ? (finalCapacity / bioBaseline) : 0f;

            // Commit complete domain state to the cache entry
            node.Offset = solvedOffset;
            node.FinalCapacity = finalCapacity;
            node.TotalMultiplier = calculatedMultiplier;
            node.CalculatedTick = currentTick;
            // Extract summary metrics directly from contiguous solver workspace
            float prostheticBoost = 0f;
            float healthDeficit = 0f;
            float athleticOffset = 0f;
            if (workspace != null)
            {
                for (int i = 0; i < workspace.PartCount; i++)
                {
                    if (workspace.CalculatedProsthetics[i] > 0f) prostheticBoost += workspace.CalculatedProsthetics[i];
                    if (workspace.CalculatedHealths[i] < 0f) healthDeficit += workspace.CalculatedHealths[i];
                    athleticOffset += workspace.CalculatedAthletics[i];
                }
            }
            node.ProstheticBoost = prostheticBoost;
            node.HealthDeficit = healthDeficit;
            node.AthleticOffset = athleticOffset;
            node.BaselineCapacity = baselineCapacity;
            node.IsStale = false;

            // Background atomic cache update
            MassSnapshotCache.WriteState(pawn.thingIDNumber, solvedOffset, calculatedMultiplier);

            PerformanceTelemetry.IncrementCacheMisses();

            if (!shouldCompileUIProperties)
            {
                if (workspace != null)
                {
                    WorkspacePool.ReleaseWorkspace(workspace);
                    workspace = null;
                }
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
        /// Prevents redundant eviction searches for uncached combatants and utilizes a state-based debounce on damage bursts.
        /// </summary>
        /// <param name="thingID">The unique identifier of the pawn whose cache entry is being checked for invalidation.</param>
        /// <returns>True if the cache entry should be culled due to invalidation; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldCullInvalidation(int thingID)
        {
            if (!UnityData.IsInMainThread) return false;

            // 1. Uncached Short-Circuit:
            // If the pawn is not in capacityCache, it is guaranteed not to exist in MassSnapshotCache either.
            // Culling immediately eliminates 100% of redundant eviction probe searches during combat bursts.
            if (!capacityCache.TryGetValue(thingID, out CachedMassData cachedNode))
            {
                return true;
            }

            // 2. Already Stale Check (State-Based Debounce):
            // If the node is already flagged dirty, downstream queries will re-solve on demand.
            // This safely catches overlapping burst damage without desynchronizing lazy evaluations.
            if (cachedNode.IsStale)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reactive cache invalidator. Marks the pawn's main-thread cache entry as stale,
        /// evicts the entry from the background snapshot cache, and records the invalidation timestamp.
        /// </summary>
        /// <param name="thingID">The unique identifier of the pawn to invalidate.</param>
        public static void Invalidate(int thingID)
        {
            AssertMainThread("Cache Invalidation");

            bool invalidatedMain = false;
            if (capacityCache.TryGetValue(thingID, out CachedMassData cachedNode))
            {
                cachedNode.LastInvalidatedTick = GetSafeCurrentTick();
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
            AssertMainThread("Cache Eviction");

            bool removedMain = capacityCache.Remove(thingID);
            bool removedGlobal = MassSnapshotCache.Evict(thingID);

            if (removedMain || removedGlobal)
            {
                PerformanceTelemetry.IncrementEvictions();
                OHLog.Performance.RecordPawnEvicted(thingID);
            }
        }

        /// <summary>
        /// Clears all cached pawn data, including main-thread and snapshot caches, scratch buffers, and resets relevant tick counters.
        /// </summary>
        public static void ClearAllCaches()
        {
            capacityCache.Clear();
            staleKeysScratch.Clear();
            cachedActiveCount = -1;
            cachedExpiryBase = SettingsDefaults.DefaultCacheMinFastPathTicks;
            lastCapturedMainThreadTick = 0;
            lastCleanupTick = 0;
            currentEvaluatingPawnId = 0;
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

            // If the current tick is less than the last cleanup tick, or if the interval has elapsed, perform cache cleanup.
            if (currentTick < lastCleanupTick || (currentTick - lastCleanupTick) > interval)
            {
                // Attempt to acquire the cleanup sentinel to ensure only one cleanup operation occurs at a time.
                if (System.Threading.Interlocked.CompareExchange(ref activeCleanupSentinel, 1, 0) == 0)
                {
                    try
                    {
                        // Double-check the cleanup condition after acquiring the sentinel to avoid redundant cleanup operations.
                        if (currentTick < lastCleanupTick || (currentTick - lastCleanupTick) > interval)
                        {
                            AssertMainThread("Registry Cleanup");
                            lastCleanupTick = currentTick;

                            int longTermInactivityThreshold = OverHaulers.settings != null 
                                ? OverHaulers.settings.PawnEvictionTimeframeTicks 
                                : SettingsDefaults.PawnEvictionTimeframeHours * GenDate.TicksPerHour;

                            staleKeysScratch.Clear();

                            // Iterate through the capacity cache to identify stale entries based on the long-term inactivity threshold.
                            foreach (var keyValuePair in capacityCache)
                            {
                                int age = currentTick - keyValuePair.Value.CalculatedTick;
                                if (age > longTermInactivityThreshold)
                                {
                                    staleKeysScratch.Add(keyValuePair.Key);
                                }
                            }
                            // Evict all identified stale entries from the cache.
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
                        // Release the cleanup sentinel to allow future cleanup operations to proceed.
                        System.Threading.Interlocked.Exchange(ref activeCleanupSentinel, 0);
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the dynamic cache expiry duration for the specified pawn ID.
        /// Scales sub-linearly with active cached population using a square-root curve (Floor + K * sqrt(N))
        /// governed by SettingsDefaults invariants, providing sub-second early-game responsiveness
        /// while preventing herd spikes during large raids.
        /// </summary>
        /// <param name="pawnId">The unique identifier of the pawn for which to retrieve the cache expiry duration.</param>
        /// <returns>The calculated cache expiry duration in game ticks.</returns>
        private static int GetCacheExpiryDuration(int pawnId)
        {
            int activeCount = capacityCache.Count;
            int floor = SettingsDefaults.DefaultCacheMinFastPathTicks;
            int scaleK = SettingsDefaults.DynamicCacheExpiryScaleK;

            if (activeCount != cachedActiveCount)
            {
                cachedActiveCount = activeCount;
                // Base TTL = Floor + K * sqrt(activeCount)
                cachedExpiryBase = floor + (int)(scaleK * Mathf.Sqrt(activeCount));
            }

            int expiryJitter = cachedExpiryBase / 4; // 25% staggered jitter window

            return cachedExpiryBase + (expiryJitter > 0 ? (pawnId % expiryJitter) : 0);
        }

        /// <summary>
        /// Retrieves the current game tick in a thread-safe manner, ensuring that the value is updated on the main thread if necessary.
        /// </summary>
        /// <returns>The current game tick, updated in a thread-safe manner.</returns>
        private static int GetSafeCurrentTick()
        {
            // Ensure that the current tick is safely captured and updated on the main thread if necessary.
            if (UnityData.IsInMainThread)
            {
                // Capture the current main thread tick to ensure thread-safe access.
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
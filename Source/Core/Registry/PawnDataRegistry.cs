using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-01] Centralized high-frequency numerical cache and state registry for pawn Caravan Mass Capacity data.
    /// Manages fast-path evaluation gates, jittered population TTL scaling, combat burst debouncing, and background table writes.
    /// 100% decoupled from UI presentation models and view projections.
    /// Resides under Source/Core/Registry/.
    /// </summary>
    public static class PawnDataRegistry
    {
        #region 1. CONSTANTS

        private const int MaxDeferredInvalidations = 4096;

        #endregion

        #region 2. STATIC STATE, LOCKS & BUFFERS

        // Pre-seeded dictionary with an initial capacity of 512 to eliminate rehash allocations during early game ticks
        private static readonly Dictionary<int, CachedMassData> capacityCache = new Dictionary<int, CachedMassData>(512);

        // Scratch list for temporarily buffering stale keys during periodic maintenance passes
        private static readonly List<int> staleKeysScratch = new List<int>(256);

        // Queue of invalidation requests raised off the main thread (e.g. background comps), drained on the next main tick
        private static readonly ConcurrentQueue<int> deferredInvalidations = new ConcurrentQueue<int>();
        private static int deferredInvalidationCount = 0;

        // Tracks the most recent tick captured on the main thread for expiry math and maintenance intervals
        private static volatile int lastCapturedMainThreadTick = 0;

        // Tracks the tick of the last out-of-band cache cleanup pass
        private static volatile int lastCleanupTick = 0;

        // Sentinel flag ensuring only one thread or maintenance routine executes cleanup at a time
        private static int activeCleanupSentinel = 0;

        // Cached population count and base TTL to avoid redundant Mathf.Sqrt calculations when population is stable
        private static int cachedActiveCount = -1;
        private static int cachedExpiryBase = 5;

        // Downward configuration snapshot pushed from Settings (SSoT initialized from SettingsDefaults)
        private static RuntimeConfiguration activeRuntimeConfig = SettingsDefaults.CreateDefaultRuntimeConfiguration();

        // Re-entrancy guard preventing infinite recursion when external stat workers query mass within a mass calculation
        [ThreadStatic]
        private static int currentEvaluatingPawnId;

        #endregion

        #region 3. DOWNWARD CONFIGURATION & EVENTS

        /// <summary>
        /// Updates the active runtime infrastructure configuration snapshot.
        /// Downward entry point invoked by Settings upon load, save, or slider mutation.
        /// </summary>
        /// <param name="newConfig">The new runtime infrastructure configuration struct snapshot.</param>
        public static void UpdateConfiguration(in RuntimeConfiguration newConfig)
        {
            activeRuntimeConfig = newConfig;
            cachedExpiryBase = newConfig.MinFastPathTicks;
            PerformanceTelemetry.Configure(in newConfig);
        }

        /// <summary>Gets a read-only reference to the active runtime configuration.</summary>
        public static ref readonly RuntimeConfiguration ActiveConfiguration => ref activeRuntimeConfig;

        /// <summary>
        /// Retrieves the exact count of active cached pawns residing in the main-thread registry.
        /// </summary>
        public static int CachedPawnCount => capacityCache.Count;

        /// <summary>
        /// Retrieves the most recent game tick captured on the main thread.
        /// </summary>
        public static int CurrentTick => lastCapturedMainThreadTick;

        /// <summary>
        /// [CACHE-01] Downward notification event fired on the main thread when all pawn data caches are cleared.
        /// </summary>
        public static event Action OnCacheCleared;

        /// <summary>
        /// [CACHE-01] Downward notification event fired on the main thread when a specific pawn entity is evicted from Core caches.
        /// </summary>
        public static event Action<int> OnPawnEvicted;

        #endregion

        #region 4. LIFECYCLE & CACHE RESET METHODS

        /// <summary>
        /// Completely flushes all registry entries, background snapshot tables, and pooled workspace buffers,
        /// notifying subscribed presentation layers to clear UI models.
        /// Invoked on save load, world transitions, and developer hot-recompilation.
        /// </summary>
        public static void ClearAllCaches()
        {
            AssertMainThread("ClearAllCaches");

            capacityCache.Clear();
            staleKeysScratch.Clear();

            // Drain any remaining deferred invalidations before clearing the cache.
            while (deferredInvalidations.TryDequeue(out _)) { }

            System.Threading.Interlocked.Exchange(ref deferredInvalidationCount, 0);
            cachedActiveCount = -1;
            cachedExpiryBase = activeRuntimeConfig.MinFastPathTicks;
            lastCapturedMainThreadTick = 0;
            lastCleanupTick = 0;
            currentEvaluatingPawnId = 0;
            System.Threading.Interlocked.Exchange(ref activeCleanupSentinel, 0);

            MassSnapshotCache.Reset();
            WorkspacePool.ClearPools();
            OHLog.Performance.ClearTraceBuffers();

            OnCacheCleared?.Invoke();
        }

        /// <summary>
        /// Completely purges a pawn from both the main-thread registry and the background snapshot table,
        /// notifying subscribed presentation layers to maintain synchronized eviction.
        /// Triggered on pawn death, map despawn, or long-term inactivity eviction.
        /// </summary>
        /// <param name="thingID">The unique ThingIDNumber of the pawn to evict.</param>
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

            OnPawnEvicted?.Invoke(thingID);
        }

        /// <summary>
        /// Marks every cached pawn entry stale across the registry without flushing background snapshots.
        /// Used when global balance settings mutate so values recalculate lazily on demand.
        /// Safely no-ops during startup when the cache is empty.
        /// </summary>
        public static void MarkAllStale()
        {
            // During startup or before any game is loaded, no cached pawn entries exist
            if (capacityCache.Count == 0) return;

            AssertMainThread("Cache Stale Sweep");

            foreach (var keyValuePair in capacityCache)
            {
                keyValuePair.Value.IsStale = true;
            }

            cachedActiveCount = -1;
        }

        #endregion

        #region 5. PUBLIC CANONICAL CALCULATION & QUERY API

        /// <summary>
        /// Retrieves the solved mass capacity offset in kilograms relative to the baseline capacity.
        /// Evaluates off-thread pathfinders via atomic <see cref="MassSnapshotCache"/> lookups;
        /// evaluates main-thread queries via <see cref="capacityCache"/> with on-demand recalculation.
        /// </summary>
        /// <param name="pawn">The pawn entity being evaluated.</param>
        /// <param name="baselineCapacity">The species baseline mass capacity in kilograms.</param>
        /// <returns>The physical capacity offset in kilograms (kg).</returns>
        public static float GetOffset(Pawn pawn, float baselineCapacity)
        {
            if (pawn == null || !CanCarryCaravanMass(pawn)) return 0f;

            if (!UnityData.IsInMainThread)
            {
                return MassSnapshotCache.GetOffsetThreadSafe(pawn.thingIDNumber);
            }

            int pawnId = pawn.thingIDNumber;

            // BREAKPOINT ANCHOR: Exact Pawn Re-entrancy Shield
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
                    SolveAndCacheEntry(pawn, cachedNode, baselineCapacity, currentTick);
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
        /// Retrieves the fully clamped, gameplay-ready Caravan Mass Capacity in kilograms for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn entity being evaluated.</param>
        /// <param name="baselineCapacity">The species baseline mass capacity in kilograms.</param>
        /// <returns>The final clamped capacity in kilograms (kg).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetCapacity(Pawn pawn, float baselineCapacity)
        {
            if (pawn == null || !CanCarryCaravanMass(pawn)) return 0f;

            if (!UnityData.IsInMainThread)
            {
                float snapshotOffset = MassSnapshotCache.GetOffsetThreadSafe(pawn.thingIDNumber);
                return MassCapacitySolver.EnforceSafetyFloorForPawn(baselineCapacity + snapshotOffset, pawn);
            }

            GetOffset(pawn, baselineCapacity); // Ensures cache entry is evaluated and fresh
            return capacityCache.TryGetValue(pawn.thingIDNumber, out CachedMassData node) ? node.FinalCapacity : baselineCapacity;
        }

        /// <summary>
        /// Resolves the clean species baseline mass capacity for a pawn via <see cref="SpeciesBaselineCalibration"/>.
        /// </summary>
        /// <param name="pawn">The pawn entity whose baseline capacity is being resolved.</param>
        /// <returns>The baseline mass capacity in kilograms (kg).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ResolveBaseline(Pawn pawn)
        {
            if (pawn == null) return 0f;
            return SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
        }

        /// <summary>
        /// Retrieves pre-computed fleet breakdown metrics for a pawn directly from cached vectors without allocating UI view models.
        /// </summary>
        /// <param name="pawn">The pawn entity being evaluated.</param>
        /// <param name="baselineCapacity">The species baseline mass capacity in kilograms.</param>
        /// <param name="prostheticBoost">Outputs the solved mass capacity boost from prosthetics in kg.</param>
        /// <param name="healthDeficit">Outputs the solved mass capacity penalty from health deficits in kg.</param>
        /// <param name="athleticOffset">Outputs the solved mass capacity offset from athletic implants in kg.</param>
        /// <returns>True if metrics were successfully retrieved; otherwise, false.</returns>
        public static bool TryGetFleetMetrics(Pawn pawn, float baselineCapacity, out float prostheticBoost, out float healthDeficit, out float athleticOffset)
        {
            prostheticBoost = 0f;
            healthDeficit = 0f;
            athleticOffset = 0f;

            if (pawn == null || !CanCarryCaravanMass(pawn) || !UnityData.IsInMainThread) return false;
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
        /// Reactively invalidates a pawn's cache entry, marking it stale for lazy re-evaluation.
        /// Leaves the background snapshot table intact to serve continuous numbers until the main thread updates.
        /// Calls from background threads are safely enqueued and processed on the next main tick.
        /// </summary>
        /// <param name="thingID">The unique ThingIDNumber of the pawn to invalidate.</param>
        public static void Invalidate(int thingID)
        {
            if (!UnityData.IsInMainThread)
            {
                if (System.Threading.Interlocked.Increment(ref deferredInvalidationCount) <= MaxDeferredInvalidations)
                {
                    deferredInvalidations.Enqueue(thingID);
                }
                else
                {
                    System.Threading.Interlocked.Decrement(ref deferredInvalidationCount);
                }
                return;
            }

            if (capacityCache.TryGetValue(thingID, out CachedMassData cachedNode))
            {
                cachedNode.LastInvalidatedTick = GetSafeCurrentTick();
                if (!cachedNode.IsStale)
                {
                    cachedNode.IsStale = true;
                    PerformanceTelemetry.IncrementInvalidations();
                }
            }
        }

        #endregion

        #region 6. SECONDARY FAST-PATH GATES & QUERIES

        /// <summary>
        /// Attempts to retrieve a fresh, non-stale cached mass capacity offset for the specified pawn ID.
        /// Bypasses species baseline calibration, monitor locks, and Pawn.BodySize evaluations on cache hits.
        /// </summary>
        /// <param name="pawnId">The unique ThingIDNumber of the pawn.</param>
        /// <param name="offset">Outputs the cached capacity offset in kilograms if valid.</param>
        /// <returns>True if a valid, unexpired cached offset was found; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFreshOffset(int pawnId, out float offset)
        {
            if (!UnityData.IsInMainThread || currentEvaluatingPawnId == pawnId)
            {
                offset = 0f;
                return false;
            }

            if (capacityCache.TryGetValue(pawnId, out CachedMassData cachedNode) && !cachedNode.IsStale)
            {
                int currentTick = GetSafeCurrentTick();
                int elapsedTicks = currentTick - cachedNode.CalculatedTick;

                if (elapsedTicks >= 0)
                {
                    if (elapsedTicks < activeRuntimeConfig.MinFastPathTicks ||
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
        /// <param name="pawnId">The unique ThingIDNumber of the pawn.</param>
        /// <param name="finalCapacity">Outputs the cached final clamped capacity in kilograms if valid.</param>
        /// <returns>True if a valid, unexpired cached entry was found; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFreshCapacity(int pawnId, out float finalCapacity)
        {
            if (!UnityData.IsInMainThread || currentEvaluatingPawnId == pawnId)
            {
                finalCapacity = 0f;
                return false;
            }

            if (capacityCache.TryGetValue(pawnId, out CachedMassData cachedNode) && !cachedNode.IsStale)
            {
                int currentTick = GetSafeCurrentTick();
                int elapsedTicks = currentTick - cachedNode.CalculatedTick;

                if (elapsedTicks >= 0)
                {
                    if (elapsedTicks < activeRuntimeConfig.MinFastPathTicks ||
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
        /// Attempts to retrieve the cached baseline Caravan Mass Capacity for a pawn if valid and fresh.
        /// Returns false if the cache entry is marked stale, forcing a native baseline re-evaluation.
        /// </summary>
        /// <param name="thingID">The unique ThingIDNumber of the target pawn.</param>
        /// <param name="baseline">Outputs the cached baseline mass capacity in kilograms if fresh; otherwise, 0.0f.</param>
        /// <returns>True if a valid, fresh baseline was available; otherwise, false.</returns>
        public static bool TryGetCachedBaseline(int thingID, out float baseline)
        {
            if (!UnityData.IsInMainThread) { baseline = 0f; return false; }

            if (capacityCache.TryGetValue(thingID, out CachedMassData cachedNode) && !cachedNode.IsStale)
            {
                PerformanceTelemetry.IncrementCacheHits();
                baseline = cachedNode.BaselineCapacity;
                return true;
            }

            baseline = 0f;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve the cached Caravan Mass Capacity offset for a pawn if valid and fresh.
        /// </summary>
        /// <param name="thingID">The unique ThingIDNumber of the target pawn.</param>
        /// <param name="offset">Outputs the cached capacity offset in kilograms if fresh; otherwise, 0.0f.</param>
        /// <returns>True if a valid, fresh offset was available; otherwise, false.</returns>
        public static bool TryGetCachedOffset(int thingID, out float offset)
        {
            if (!UnityData.IsInMainThread) { offset = 0f; return false; }

            if (capacityCache.TryGetValue(thingID, out CachedMassData cachedNode) && !cachedNode.IsStale)
            {
                PerformanceTelemetry.IncrementCacheHits();
                offset = cachedNode.Offset;
                return true;
            }

            offset = 0f;
            return false;
        }

        /// <summary>
        /// Primary gate determining if a live pawn entity should be processed by the OverHaulers solver.
        /// By default, only caravan-capable pawns are evaluated. When the dev stress-test toggle is active,
        /// all biological fauna are evaluated to benchmark cache scaling.
        /// </summary>
        /// <param name="pawn">The pawn being evaluated.</param>
        /// <returns>True if the pawn is eligible for Caravan Mass Capacity calculation; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanCarryCaravanMass(Pawn pawn)
        {
            if (pawn == null) return false;

            if (activeRuntimeConfig.DevStressTestAllFauna)
            {
                return true;
            }

            return IsCaravanCapable(pawn);
        }

        /// <summary>
        /// Evaluates whether a live pawn entity is officially designated for caravan mass transport.
        /// Strictly requires Humanlike, designated pack animal, or hauling-capable mechanoid.
        /// </summary>
        /// <param name="pawn">The pawn entity being evaluated.</param>
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
        /// Evaluates whether a species <see cref="ThingDef"/> archetype is designated for caravan transport.
        /// Distinct symbol name disambiguating species archetype queries from live pawn instance queries.
        /// </summary>
        /// <param name="raceDef">The race definition representing the species archetype.</param>
        /// <returns>True if the species archetype is designated for caravan transport; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSpeciesCaravanCapable(ThingDef raceDef)
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

        /// <summary>
        /// Evaluates whether an incoming invalidation signal for a pawn should be culled.
        /// Culls uncached combatants in $O(1)$ time and implements state-based debouncing during combat damage bursts.
        /// </summary>
        /// <param name="thingID">The unique ThingIDNumber of the pawn.</param>
        /// <returns>True if the invalidation is redundant and should be culled; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldCullInvalidation(int thingID)
        {
            if (!UnityData.IsInMainThread) return false;

            // 1. Uncached Short-Circuit: If not cached, avoid redundant probe and eviction searches
            if (!capacityCache.TryGetValue(thingID, out CachedMassData cachedNode))
            {
                PerformanceTelemetry.IncrementCulledInvalidations();
                return true;
            }

            // 2. Already Stale Check: Debounce multiple damage signals within the same tick burst
            if (cachedNode.IsStale)
            {
                PerformanceTelemetry.IncrementCulledInvalidations();
                return true;
            }

            return false;
        }

        #endregion

        #region 7. ALGORITHMIC SOLVER PIPELINE & MAINTENANCE STAGES

        /// <summary>
        /// Main-thread tick driver called by the lifecycle manager to process deferred invalidations and out-of-band cleanup.
        /// </summary>
        /// <param name="currentTick">The active game tick from the TickManager.</param>
        public static void UpdateTick(int currentTick)
        {
            if (currentTick != lastCapturedMainThreadTick)
            {
                lastCapturedMainThreadTick = currentTick;
                DrainDeferredInvalidations();
                MaybeCleanupCache(currentTick);
            }
        }

        /// <summary>
        /// Drains and processes invalidation requests that were deferred from off-thread callers.
        /// </summary>
        private static void DrainDeferredInvalidations()
        {
            while (deferredInvalidations.TryDequeue(out int thingID))
            {
                System.Threading.Interlocked.Decrement(ref deferredInvalidationCount);
                Invalidate(thingID);
            }
        }

        /// <summary>
        /// Periodically sweeps the cache for entries unqueried beyond the configured inactivity threshold.
        /// </summary>
        /// <param name="currentTick">The current game tick.</param>
        private static void MaybeCleanupCache(int currentTick)
        {
            int interval = activeRuntimeConfig.CacheCleanupInterval;

            if (currentTick < lastCleanupTick || (currentTick - lastCleanupTick) > interval)
            {
                if (System.Threading.Interlocked.CompareExchange(ref activeCleanupSentinel, 1, 0) == 0)
                {
                    try
                    {
                        if (currentTick < lastCleanupTick || (currentTick - lastCleanupTick) > interval)
                        {
                            AssertMainThread("Registry Cleanup");
                            lastCleanupTick = currentTick;

                            int longTermInactivityThreshold = activeRuntimeConfig.PawnEvictionTimeframeTicks;

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
        /// Solves the mass capacity for the specified pawn and caches the numerical results in the provided node.
        /// Pure Core implementation: zero UI compilation, zero ViewProjection dependencies.
        /// </summary>
        /// <param name="pawn">The pawn whose mass capacity is being solved.</param>
        /// <param name="node">The cache node where solved numerical values will be stored.</param>
        /// <param name="baselineCapacity">The species baseline capacity in kilograms.</param>
        /// <param name="currentTick">The current game tick timestamp.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SolveAndCacheEntry(
            Pawn pawn, 
            CachedMassData node, 
            float baselineCapacity, 
            int currentTick)
        {
            MassCapacityEvaluationResult result = MassCapacitySolver.EvaluateMassCapacityForPawn(
                pawn, baselineCapacity);

            // Commit complete numerical state to the cache entry
            node.Offset = result.SolvedOffset;
            node.FinalCapacity = result.FinalCapacity;
            node.TotalMultiplier = result.TotalMultiplier;
            node.CalculatedTick = currentTick;
            node.ProstheticBoost = result.CalculatedProsthetics;
            node.HealthDeficit = result.CalculatedHealths;
            node.AthleticOffset = result.CalculatedAthletics;
            node.BaselineCapacity = baselineCapacity;
            node.IsStale = false;

            // Publish state atomically to the background snapshot table for pathfinding threads
            MassSnapshotCache.WriteState(pawn.thingIDNumber, result.SolvedOffset);

            PerformanceTelemetry.IncrementCacheMisses();
        }

        /// <summary>
        /// Validates whether a cache entry is within the sub-second fast path and has not mutated its species baseline.
        /// </summary>
        /// <param name="node">The cache node to evaluate.</param>
        /// <param name="baselineCapacity">The currently requested baseline capacity.</param>
        /// <param name="currentTick">The active game tick.</param>
        /// <param name="calculatedTick">The tick when the node was calculated.</param>
        /// <returns>True if the node is valid for immediate return; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCacheNodeValid(CachedMassData node, float baselineCapacity, int currentTick, int calculatedTick)
        {
            if (node == null) return false;

            if (Math.Abs(node.BaselineCapacity - baselineCapacity) >= activeRuntimeConfig.EfficiencyEpsilon)
            {
                node.IsStale = true;
            }

            int age = currentTick - calculatedTick;
            return age >= 0 && age < activeRuntimeConfig.MinFastPathTicks && !node.IsStale;
        }

        #endregion

        #region 8. PRIVATE EXPIRY MATH & THREADING HELPERS

        /// <summary>
        /// Computes the dynamic cache TTL in game ticks for a pawn ID.
        /// Scales sub-linearly with active cached population using a square-root curve:
        /// <c>Floor + K * sqrt(N) + Jitter</c>.
        /// </summary>
        /// <param name="pawnId">The unique ThingIDNumber of the pawn (used for 25% staggered jitter).</param>
        /// <returns>The calculated TTL in game ticks.</returns>
        private static int GetCacheExpiryDuration(int pawnId)
        {
            int activeCount = capacityCache.Count;
            int floor = activeRuntimeConfig.MinFastPathTicks;
            int scaleK = activeRuntimeConfig.DynamicExpiryScaleK;

            if (activeCount != cachedActiveCount)
            {
                cachedActiveCount = activeCount;
                cachedExpiryBase = floor + (int)(scaleK * Math.Sqrt(activeCount));
            }

            int expiryJitter = cachedExpiryBase / 4; // 25% staggered jitter window

            return cachedExpiryBase + (expiryJitter > 0 ? (pawnId % expiryJitter) : 0);
        }

        /// <summary>
        /// Safely retrieves the current game tick on the main thread, updating the internal counter if ticks advanced.
        /// </summary>
        /// <returns>The active game tick.</returns>
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
        /// Enforces that a critical cache mutation executes strictly on Unity's main thread.
        /// Throws an <see cref="InvalidOperationException"/> if invoked from a background thread.
        /// </summary>
        /// <param name="context">The operation description for diagnostic reporting.</param>
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
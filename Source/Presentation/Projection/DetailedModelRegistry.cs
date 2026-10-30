using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-03] PRESENTATION MODEL REGISTRY & INFOCARD CACHE
    /// Dedicated presentation cache managing <see cref="MassCapacityModel"/> lifecycles for InfoCard dialogs.
    /// Implements a bounded, zero-allocation doubly-linked LRU cache with in-session node recycling to completely
    /// isolate presentation tree generation and explanation caching from the high-frequency gameplay Core.
    /// Dynamically scales capacity (32 -> 1024) upon detecting high-velocity UI working-set contention.
    /// Guarantees 100% clean teardown on save unload with zero cross-session memory retention.
    /// Resides under Source/Presentation/Projection/.
    /// </summary>
    public static class DetailedModelRegistry
    {
        #region 1. [VIEW-03-A] CONSTANTS, CACHE DATA STRUCTURES & STATIC STATE

        /// <summary>Default initial capacity for standard colonies and single-pawn inspections.</summary>
        public const int DefaultBaseCapacity = 32;

        /// <summary>Maximum dynamic capacity ceiling permitted for massive horde colonies and modded overview tables.</summary>
        public const int MaxPermittedCapacity = 1024;

        /// <summary>Rolling tick duration (60 ticks = 1 second) used to compute recycle velocity and detect UI thrashing.</summary>
        private const int VelocityWindowTicks = 60;

        /// <summary>
        /// [VIEW-03-A] Intrusive doubly-linked LRU cache node holding pre-compiled visual models and solver validation state.
        /// </summary>
        /// <remarks>
        /// This class is used internally by the <see cref="DetailedModelRegistry"/> to manage the lifecycle of presentation models.
        /// </remarks>
        private sealed class PresentationCacheEntry
        {
            public int PawnId;
            public readonly MassCapacityModel Model = new MassCapacityModel();
            public float SolvedOffset;
            public float BaselineCapacity;
            public int CalculatedTick;
            public bool IsValid;

            public PresentationCacheEntry Prev;
            public PresentationCacheEntry Next;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset(int newPawnId)
            {
                PawnId = newPawnId;
                SolvedOffset = 0f;
                BaselineCapacity = 0f;
                CalculatedTick = 0;
                IsValid = false;
                Prev = null;
                Next = null;
            }
        }

        /// <summary>Lookup table mapping pawn IDs to their corresponding presentation cache entries.</summary>
        private static readonly Dictionary<int, PresentationCacheEntry> presentationLookup =
            new Dictionary<int, PresentationCacheEntry>(DefaultBaseCapacity);

        /// <summary>Head of the LRU linked list representing the most recently used presentation cache entry.</summary>
        private static PresentationCacheEntry lruHead;

        /// <summary>Tail of the LRU linked list representing the least recently used presentation cache entry.</summary>
        private static PresentationCacheEntry lruTail;

        /// <summary>Current active capacity allocated for the presentation cache.</summary>
        private static int activeCapacity = DefaultBaseCapacity;

        /// <summary>Start tick of the current velocity evaluation window for dynamic scaling.</summary>
        private static int velocityWindowStartTick;

        /// <summary>Count of recycle events within the current velocity evaluation window.</summary>
        private static int velocityRecycleCount;

        /// <summary>Pawn ID currently being evaluated in the presentation cache on this thread.</summary>
        [ThreadStatic]
        private static int currentEvaluatingPawnId;

        // Telemetry & Diagnostic Counters
        private static long totalHits;
        private static long totalMisses;
        private static long totalRecycles;
        private static long totalFlushes;

        static DetailedModelRegistry()
        {
            // Subscribe downward to Core invalidation events (Strict Downward Directionality)
            PawnDataRegistry.OnPawnEvicted += Evict;
            PawnDataRegistry.OnCacheCleared += Clear;

            // Wire downward telemetry providers into Core
            PerformanceTelemetry.GetPresentationActiveCount = () => ActiveEntryCount;
            PerformanceTelemetry.GetPresentationCapacity = () => CurrentCapacity;
        }

        #endregion

        #region 2. [VIEW-03-B] TELEMETRY & DIAGNOSTIC PROPERTIES

        /// <summary>Current maximum capacity allocated for the active gameplay session.</summary>
        public static int CurrentCapacity => activeCapacity;

        /// <summary>Count of active presentation models currently resident in the LRU rotation.</summary>
        public static int ActiveEntryCount => presentationLookup.Count;

        /// <summary>Total presentation cache hits since engine startup or last manual reset.</summary>
        public static long TotalHits => totalHits;

        /// <summary>Total presentation cache misses triggering visual model solves.</summary>
        public static long TotalMisses => totalMisses;

        /// <summary>Total times the LRU tail was recycled to make room for a new pawn without memory allocation.</summary>
        public static long TotalRecycles => totalRecycles;

        /// <summary>Total times the presentation registry was completely flushed (e.g. on save unload).</summary>
        public static long TotalFlushes => totalFlushes;

        /// <summary>Formatted one-line diagnostic summary of presentation cache rotation metrics.</summary>
        public static string DiagnosticSummary
        {
            get
            {
                long totalQueries = totalHits + totalMisses;
                float hitRate = totalQueries > 0 ? ((float)totalHits / totalQueries) * 100f : 0f;
                return $"[DetailedModelRegistry] {ActiveEntryCount}/{activeCapacity} in rotation (Ceiling: {MaxPermittedCapacity}) | Hits: {totalHits} ({hitRate:F1}%) | Misses: {totalMisses} | Recycles: {totalRecycles} | Flushes: {totalFlushes}";
            }
        }

        #endregion

        #region 3. [VIEW-03-C] PUBLIC PRESENTATION API

        /// <summary>
        /// Retrieves or rebuilds the compiled <see cref="MassCapacityModel"/> for a pawn's InfoCard dialog.
        /// Main thread only. Automatically validates freshness against Core's numerical solver cache
        /// and promotes hits to the head of the presentation LRU cache.
        /// </summary>
        /// <param name="pawn">The target pawn being inspected.</param>
        /// <param name="baselineCapacity">The pawn's baseline biological capacity in kg.</param>
        /// <returns>The fully compiled visual model, or null if off-thread or invalid.</returns>
        public static MassCapacityModel GetDetailedModel(Pawn pawn, float baselineCapacity)
        {
            if (pawn == null || !PawnDataRegistry.CanCarryCaravanMass(pawn) || !UnityData.IsInMainThread)
            {
                return null;
            }

            int pawnId = pawn.thingIDNumber;

            // Re-entrancy guard: return active model if recursive stat query occurs
            if (currentEvaluatingPawnId == pawnId)
            {
                return presentationLookup.TryGetValue(pawnId, out PresentationCacheEntry reentrantEntry)
                    ? reentrantEntry.Model
                    : null;
            }

            int currentTick = PawnDataRegistry.CurrentTick;

            // Check if Core considers its numerical cache fresh
            bool coreIsFresh = PawnDataRegistry.TryGetFreshOffset(pawnId, out float coreFreshOffset);

            // BREAKPOINT ANCHOR: Presentation LRU Lookup & Validation
            if (presentationLookup.TryGetValue(pawnId, out PresentationCacheEntry entry) && entry.IsValid)
            {
                bool baselineMatches = Math.Abs(entry.BaselineCapacity - baselineCapacity) < PawnDataRegistry.EfficiencyEpsilon;
                bool offsetMatches = Math.Abs(entry.SolvedOffset - coreFreshOffset) < PawnDataRegistry.EfficiencyEpsilon;

                if (coreIsFresh && baselineMatches && offsetMatches)
                {
                    totalHits++;
                    PerformanceTelemetry.RecordPresentationHit();
                    PromoteToHead(entry);
                    return entry.Model;
                }
            }

            totalMisses++;
            PerformanceTelemetry.RecordPresentationMiss();

            if (entry == null)
            {
                entry = ObtainOrRecycleEntry(pawnId, currentTick);
                presentationLookup[pawnId] = entry;
                AttachAtHead(entry);
            }
            else
            {
                // Stale entry found in lookup; promote it to head before re-solving
                PromoteToHead(entry);
            }

            // BREAKPOINT ANCHOR: Model Rebuild & View Projection
            int prevEvaluatingId = currentEvaluatingPawnId;

            try
            {
                currentEvaluatingPawnId = pawnId;

                MassCapacityEvaluationResult result = ViewProjection.InspectAndProjectForPawn(pawn, baselineCapacity, entry.Model);

                entry.SolvedOffset = result.SolvedOffset;
                entry.BaselineCapacity = result.BiologicalBaseline;
                entry.CalculatedTick = currentTick;
                entry.IsValid = true;

                // Synchronize fresh solve to background snapshot cache for pathfinding threads
                MassSnapshotCache.WriteState(pawnId, result.SolvedOffset);

                return entry.Model;
            }
            finally
            {
                currentEvaluatingPawnId = prevEvaluatingId;
            }
        }

        #endregion

        #region 4. [VIEW-03-D] CACHE EVICTION & LIFECYCLE MANAGEMENT

        /// <summary>
        /// Evicts a specific pawn's presentation model from cache (e.g. on death or despawn).
        /// </summary>
        /// <param name="pawnId">The ID of the pawn whose presentation model should be evicted from the cache.</param>
        public static void Evict(int pawnId)
        {
            if (!UnityData.IsInMainThread || pawnId <= 0) return;

            if (presentationLookup.TryGetValue(pawnId, out PresentationCacheEntry entry))
            {
                presentationLookup.Remove(pawnId);
                DetachNode(entry);
            }
        }

        /// <summary>
        /// [VIEW-03-D] Completely flushes the presentation cache on save load or world unload.
        /// Unlinks all active references so old presentation models are garbage collected cleanly,
        /// and snaps active capacity back to baseline (32 slots).
        /// </summary>
        public static void Clear()
        {
            if (!UnityData.IsInMainThread) return;

            int purgedCount = presentationLookup.Count;
            presentationLookup.Clear();
            lruHead = null;
            lruTail = null;

            activeCapacity = DefaultBaseCapacity;
            velocityWindowStartTick = 0;
            velocityRecycleCount = 0;
            currentEvaluatingPawnId = 0;
            totalFlushes++;

            if (purgedCount > 0)
            {
                OHLog.Presentation.CacheFlushed(purgedCount, DefaultBaseCapacity);
            }
        }

        #endregion

        #region 5. [VIEW-03-E] LRU MANAGEMENT & RECYCLING PRIMITIVES

        /// <summary>
        /// Obtains a new entry or recycles the least recently used tail entry.
        /// Dynamically scales capacity up by power-of-two (up to 512) upon detecting high recycle velocity.
        /// </summary>
        /// <param name="pawnId">The ID of the pawn for which to obtain or recycle a presentation cache entry.</param>
        /// <param name="currentTick">The current game tick used for evaluating recycle velocity.</param>
        /// <returns>A presentation cache entry, either newly allocated or recycled from the LRU tail.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PresentationCacheEntry ObtainOrRecycleEntry(int pawnId, int currentTick)
        {
            PresentationCacheEntry entry;

            if (presentationLookup.Count >= activeCapacity && lruTail != null)
            {
                // BREAKPOINT ANCHOR: Recycle Velocity Evaluation
                if (currentTick - velocityWindowStartTick > VelocityWindowTicks)
                {
                    velocityWindowStartTick = currentTick;
                    velocityRecycleCount = 0;
                }

                velocityRecycleCount++;

                // If velocity exceeds half capacity within 60 ticks, scale up to absorb working-set thrashing
                if (activeCapacity < MaxPermittedCapacity && velocityRecycleCount >= (activeCapacity >> 1))
                {
                    int oldCap = activeCapacity;
                    // `activeCapacity << 1` bit shift equivalent to `activeCapacity * 2`
                    activeCapacity = Math.Min(MaxPermittedCapacity, activeCapacity << 1);

                    int burstRecycles = velocityRecycleCount;
                    velocityWindowStartTick = currentTick;
                    velocityRecycleCount = 0;

                    OHLog.Presentation.CacheScaledUp(oldCap, activeCapacity, burstRecycles);

                    // Allocate one new entry to expand into the new capacity bracket
                    entry = new PresentationCacheEntry();
                    entry.PawnId = pawnId;
                    return entry;
                }

                // Normal LRU recycling: detach and reuse the existing tail node
                totalRecycles++;
                PerformanceTelemetry.RecordPresentationRecycle();
                entry = lruTail;
                DetachNode(entry);
                presentationLookup.Remove(entry.PawnId);
                entry.Reset(pawnId);
            }
            else
            {
                entry = new PresentationCacheEntry();
                entry.PawnId = pawnId;
            }

            return entry;
        }

        /// <summary>
        /// Promotes the specified node to the head of the LRU linked list.
        /// </summary>
        /// <param name="node">The cache entry node to promote to the head of the LRU list.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PromoteToHead(PresentationCacheEntry node)
        {
            if (node == lruHead) return;
            DetachNode(node);
            AttachAtHead(node);
        }

        /// <summary>
        /// Detaches the specified node from the LRU linked list.
        /// </summary>
        /// <param name="node">The cache entry node to detach from the LRU list.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DetachNode(PresentationCacheEntry node)
        {
            if (node.Prev != null) node.Prev.Next = node.Next;
            else lruHead = node.Next;

            if (node.Next != null) node.Next.Prev = node.Prev;
            else lruTail = node.Prev;

            node.Prev = null;
            node.Next = null;
        }

        /// <summary>
        /// Attaches the specified node at the head of the LRU linked list.
        /// </summary>
        /// <param name="node">The cache entry node to attach at the head of the LRU list.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AttachAtHead(PresentationCacheEntry node)
        {
            node.Prev = null;
            node.Next = lruHead;

            if (lruHead != null) lruHead.Prev = node;
            lruHead = node;

            if (lruTail == null) lruTail = node;
        }

        #endregion
    }
}
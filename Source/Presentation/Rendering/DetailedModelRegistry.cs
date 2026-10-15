using System;
using System.Collections.Generic;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-04] Dedicated presentation cache managing <see cref="MassCapacityModel"/> lifecycles for InfoCard dialogs.
    /// Completely isolates presentation tree generation and explanation caching from the high-frequency gameplay Core.
    /// Resides under Source/Presentation/Rendering/.
    /// </summary>
    public static class DetailedModelRegistry
    {
        #region 1. CACHE DATA STRUCTURES & STATIC STATE

        private sealed class PresentationCacheEntry
        {
            public readonly MassCapacityModel Model = new MassCapacityModel();
            public float SolvedOffset;
            public float BaselineCapacity;
            public int CalculatedTick;
            public bool IsValid;
        }

        private static readonly Dictionary<int, PresentationCacheEntry> presentationCache =
            new Dictionary<int, PresentationCacheEntry>(32);

        [ThreadStatic]
        private static int currentEvaluatingPawnId;

        static DetailedModelRegistry()
        {
            // Subscribe downward to Core invalidation events (Strict Downward Directionality)
            PawnDataRegistry.OnPawnEvicted += Evict;
            PawnDataRegistry.OnCacheCleared += Clear;
        }

        #endregion

        #region 2. PUBLIC PRESENTATION API

        /// <summary>
        /// Retrieves or rebuilds the compiled <see cref="MassCapacityModel"/> for a pawn's InfoCard dialog.
        /// Main thread only. Automatically validates freshness against Core's numerical solver cache.
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
                return presentationCache.TryGetValue(pawnId, out PresentationCacheEntry reentrantEntry)
                    ? reentrantEntry.Model
                    : null;
            }

            int currentTick = PawnDataRegistry.CurrentTick;

            // Check if Core considers its numerical cache fresh
            bool coreIsFresh = PawnDataRegistry.TryGetFreshOffset(pawnId, out float coreFreshOffset);

            if (presentationCache.TryGetValue(pawnId, out PresentationCacheEntry entry) && entry.IsValid)
            {
                bool baselineMatches = Math.Abs(entry.BaselineCapacity - baselineCapacity) < SettingsDefaults.EfficiencyEpsilon;
                bool offsetMatches = Math.Abs(entry.SolvedOffset - coreFreshOffset) < SettingsDefaults.EfficiencyEpsilon;

                if (coreIsFresh && baselineMatches && offsetMatches)
                {
                    return entry.Model;
                }
            }
            else if (entry == null)
            {
                entry = new PresentationCacheEntry();
                presentationCache[pawnId] = entry;
            }

            // Cache Miss: Rebuild presentation model via Core solver and ViewProjection
            int prevEvaluatingId = currentEvaluatingPawnId;

            try
            {
                currentEvaluatingPawnId = pawnId;

                MassCapacityEvaluationResult result = MassCapacitySolver.InspectMassCapacityModelForPawn(
                    pawn, baselineCapacity, entry.Model);

                entry.SolvedOffset = result.SolvedOffset;
                entry.BaselineCapacity = result.BiologicalBaseline;
                entry.CalculatedTick = currentTick;
                entry.IsValid = true;

                // Synchronize fresh solve to background snapshot cache for pathfinding threads
                MassSnapshotCache.WriteState(pawnId, result.SolvedOffset, result.TotalMultiplier);

                return entry.Model;
            }
            finally
            {
                currentEvaluatingPawnId = prevEvaluatingId;
            }
        }

        #endregion

        #region 3. CACHE EVICTION & LIFECYCLE MANAGEMENT

        /// <summary>
        /// Evicts a specific pawn's presentation model from cache (e.g. on death or despawn).
        /// </summary>
        public static void Evict(int pawnId)
        {
            if (UnityData.IsInMainThread)
            {
                presentationCache.Remove(pawnId);
            }
        }

        /// <summary>
        /// Completely clears the presentation model cache on save load or settings mutation.
        /// </summary>
        public static void Clear()
        {
            if (UnityData.IsInMainThread)
            {
                presentationCache.Clear();
                currentEvaluatingPawnId = 0;
            }
        }

        #endregion
    }
}
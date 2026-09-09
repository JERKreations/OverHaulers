using Verse;

namespace OverHaulers
{
    #region 1. [INT-05] REACTIVE CACHE INVALIDATION HOOKS

    /// <summary>
    /// [INT-05] Reactive invalidation patches monitoring pawn health, apparel, and equipment state changes.
    /// Intercepts native engine events to clear stale cache entries in PawnDataRegistry and MassSnapshotCache.
    /// Resides under Source/Integration/ as part of the integration delivery layer.
    /// </summary>
    public static class CacheInvalidationPatches
    {
        #region 1. PROCESSING GATES

        /// <summary>
        /// Direct main-thread processing gate filtering out duplicate invalidation signals in $O(1)$ time.
        /// </summary>
        private static void ProcessInvalidationGate(Pawn pawn)
        {
            if (pawn == null || pawn.thingIDNumber <= 0) return;

            // BREAKPOINT ANCHOR: Sandbox Pawn Fast Bypass
            // Immediately ignore mock surgeries performed inside the Test Bench
            if (pawn.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId)
            {
                return;
            }

            try
            {
                int pawnId = pawn.thingIDNumber;

                // Check if the pawn's invalidation should be culled to avoid redundant processing.
                if (PawnDataRegistry.ShouldCullInvalidation(pawnId))
                {
                    return;
                }

                PawnDataRegistry.Invalidate(pawnId);
            }
            catch
            {
            }
        }

        #endregion

        #region 2. HARMONY POSTFIX TARGETS

        /// <summary>[INT-05] Postfix hook for Pawn_HealthTracker.Notify_HediffChanged.</summary>
        public static void Notify_HediffChanged_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-05] Postfix hook for Pawn_ApparelTracker.Notify_ApparelAdded.</summary>
        public static void Notify_ApparelAdded_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-05] Postfix hook for Pawn_ApparelTracker.Notify_ApparelRemoved.</summary>
        public static void Notify_ApparelRemoved_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-05] Postfix hook for Pawn_EquipmentTracker.Notify_EquipmentAdded.</summary>
        public static void Notify_EquipmentAdded_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-05] Postfix hook for Pawn_EquipmentTracker.Notify_EquipmentRemoved.</summary>
        public static void Notify_EquipmentRemoved_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>
        /// [INT-05] Postfix hook for Pawn.Kill. Immediately evicts dead pawns from all cache registries.
        /// </summary>
        public static void Notify_PawnKilled_Postfix(Pawn __instance)
        {
            if (__instance == null || __instance.thingIDNumber <= 0) return;

            try
            {
                // BREAKPOINT ANCHOR: Immediate Pawn Eviction Signal
                if (UnityData.IsInMainThread)
                {
                    PawnDataRegistry.EvictCacheEntry(__instance.thingIDNumber);
                }
            }
            catch
            {
            }
        }

        #endregion
    }

    #endregion
}
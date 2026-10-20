using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [INT-06] REACTIVE CACHE INVALIDATION DRIVER
    /// Intercepts pawn health mutations, apparel changes, equipment equipping, and life-cycle events.
    /// Filters signals via O(1) debouncing gates and evicts stale entries from Core caches.
    /// Strictly decoupled from UI and presentation rendering.
    /// Resides under Source/Integration/Patches/.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. [INT-INVAL-01] INVALIDATION REGISTRATION & BINDING

        /// <summary>
        /// Installs cache invalidation patches for pawn lifecycle and equipment events.
        /// Each patch is applied independently so one failure cannot prevent the remaining patches from operating.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the patches.</param>
        private static void InstallInvalidationPatches(Harmony harmony)
        {
            Guarded("Pawn_HealthTracker.Notify_HediffChanged", () => harmony.Patch(
                AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged)),
                postfix: OwnPatch(nameof(Notify_HediffChanged_Postfix))));

            Guarded("Pawn_ApparelTracker.Notify_ApparelAdded", () => harmony.Patch(
                AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded)),
                postfix: OwnPatch(nameof(Notify_ApparelAdded_Postfix))));

            Guarded("Pawn_ApparelTracker.Notify_ApparelRemoved", () => harmony.Patch(
                AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved)),
                postfix: OwnPatch(nameof(Notify_ApparelRemoved_Postfix))));

            Guarded("Pawn_EquipmentTracker.Notify_EquipmentAdded", () => harmony.Patch(
                AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded)),
                postfix: OwnPatch(nameof(Notify_EquipmentAdded_Postfix))));

            Guarded("Pawn_EquipmentTracker.Notify_EquipmentRemoved", () => harmony.Patch(
                AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved)),
                postfix: OwnPatch(nameof(Notify_EquipmentRemoved_Postfix))));

            Guarded("Pawn.Kill", () => harmony.Patch(
                AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)),
                postfix: OwnPatch(nameof(Notify_PawnKilled_Postfix))));

            Guarded("Pawn.DeSpawn", () => harmony.Patch(
                AccessTools.Method(typeof(Pawn), nameof(Pawn.DeSpawn)),
                postfix: OwnPatch(nameof(Notify_PawnDeSpawned_Postfix))));
        }

        #endregion

        #region 2. [INT-INVAL-02] PROCESSING GATES & TELEMETRY

        /// <summary>
        /// Direct main-thread processing gate filtering out duplicate invalidation signals in O(1) time.
        /// Ignores headless sandbox surgeries and non-caravan wildlife before collection lookups.
        /// </summary>
        private static void ProcessInvalidationGate(Pawn pawn)
        {
            if (pawn == null || pawn.thingIDNumber <= 0) return;

            // BREAKPOINT ANCHOR: Headless Pawn Fast Bypass
            // Immediately ignore mock operations and calibrations performed on headless pawns
            if (pawn.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId)
            {
                return;
            }

            // Fast Pre-Filter: Ignore non-caravan wildlife and creatures OverHaulers does not track
            if (!PawnDataRegistry.CanCarryCaravanMass(pawn))
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

        #region 3. [INT-INVAL-03] HARMONY POSTFIX TARGETS

        /// <summary>[INT-INVAL-03] Postfix hook for Pawn_HealthTracker.Notify_HediffChanged.</summary>
        private static void Notify_HediffChanged_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-INVAL-03] Postfix hook for Pawn_ApparelTracker.Notify_ApparelAdded.</summary>
        private static void Notify_ApparelAdded_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-INVAL-03] Postfix hook for Pawn_ApparelTracker.Notify_ApparelRemoved.</summary>
        private static void Notify_ApparelRemoved_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-INVAL-03] Postfix hook for Pawn_EquipmentTracker.Notify_EquipmentAdded.</summary>
        private static void Notify_EquipmentAdded_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-INVAL-03] Postfix hook for Pawn_EquipmentTracker.Notify_EquipmentRemoved.</summary>
        private static void Notify_EquipmentRemoved_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>
        /// [INT-INVAL-03] Postfix hook for Pawn.Kill. Immediately evicts dead pawns from Core cache registries.
        /// Core downward events (PawnDataRegistry.OnPawnEvicted) automatically synchronize presentation cache eviction.
        /// </summary>
        private static void Notify_PawnKilled_Postfix(Pawn __instance)
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

        /// <summary>
        /// [INT-INVAL-03] Postfix hook for Pawn.DeSpawn. Immediately evicts non-colonist pawns (fleeing raiders, departing visitors, wild animals)
        /// from capacity registries to rapidly deflate the cache population and snap dynamic TTL back to baseline after raids.
        /// </summary>
        private static void Notify_PawnDeSpawned_Postfix(Pawn __instance)
        {
            if (__instance == null || __instance.thingIDNumber <= 0) return;

            try
            {
                // BREAKPOINT ANCHOR: Non-Colonist Despawn Eviction Signal
                if (UnityData.IsInMainThread)
                {
                    // Player colonists and caravan members remain cached; all fled raiders and temporary entities are evicted
                    if (__instance.Faction != Faction.OfPlayer)
                    {
                        PawnDataRegistry.EvictCacheEntry(__instance.thingIDNumber);
                    }
                }
            }
            catch
            {
            }
        }

        #endregion
    }
}
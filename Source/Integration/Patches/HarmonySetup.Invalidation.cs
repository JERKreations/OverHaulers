using System;
using UnityEngine;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    public static partial class HarmonySetup
    {
        #region 1. [INT-06] INVALIDATION REGISTRATION & BINDING

        /// <summary>
        /// Installs cache invalidation patches for various pawn-related events, ensuring that cached data is properly updated when changes
        ///  occur. Each patch is installed independently so one failure cannot prevent the remaining patches from being applied.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the cache invalidation patches.</param>
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

            Guarded("Widgets.Label", () => harmony.Patch(
                AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new Type[] { typeof(Rect), typeof(string) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(InfoCardOverlay), nameof(InfoCardOverlay.Prefix)))));

            // Gated InfoCard Intercept: Sets IsRenderingInfoCard = true only while StatsReportUtility is actively rendering
            Guarded("StatsReportUtility.DrawStatsReport", () =>
            {
                var statsReportPrefix = OwnPatch(nameof(StatsReport_Prefix));
                var statsReportFinalizer = OwnPatch(nameof(StatsReport_Finalizer));
                var statsReportMethods = typeof(StatsReportUtility).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                for (int i = 0; i < statsReportMethods.Length; i++)
                {
                    if (statsReportMethods[i].Name == nameof(StatsReportUtility.DrawStatsReport))
                    {
                        harmony.Patch(statsReportMethods[i], prefix: statsReportPrefix, finalizer: statsReportFinalizer);
                    }
                }
            });

            Guarded("Caravan.MassCapacityExplanation", () => harmony.Patch(
                AccessTools.PropertyGetter(typeof(RimWorld.Planet.Caravan), "MassCapacityExplanation"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(CaravanUIIntegration), nameof(CaravanUIIntegration.Caravan_MassCapacityExplanation_Postfix)))));

            Guarded("CaravanUIUtility.DrawCaravanInfo", () => InstallDrawCaravanInfoPatch(harmony));
        }

        /// <summary>
        /// Resolves the <c>CaravanUIUtility.DrawCaravanInfo</c> overload that takes the 'info' structure and patches it.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the patch.</param>
        private static void InstallDrawCaravanInfoPatch(Harmony harmony)
        {
            var drawCaravanInfoPrefix = AccessTools.Method(typeof(CaravanUIIntegration), nameof(CaravanUIIntegration.DrawCaravanInfo_Prefix));
            System.Reflection.MethodInfo drawCaravanInfo = null;
            var drawMethods = typeof(RimWorld.Planet.CaravanUIUtility).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            for (int i = 0; i < drawMethods.Length; i++)
            {
                var m = drawMethods[i];
                if (m.Name == nameof(RimWorld.Planet.CaravanUIUtility.DrawCaravanInfo))
                {
                    var pars = m.GetParameters();
                    for (int p = 0; p < pars.Length; p++)
                    {
                        Type pType = pars[p].ParameterType;
                        if (pars[p].Name == "info" &&
                            (pType == typeof(RimWorld.Planet.CaravanUIUtility.CaravanInfo) ||
                             pType == typeof(RimWorld.Planet.CaravanUIUtility.CaravanInfo).MakeByRefType()))
                        {
                            drawCaravanInfo = m;
                            break;
                        }
                    }
                    if (drawCaravanInfo != null) break;
                }
            }

            if (drawCaravanInfo != null && drawCaravanInfoPrefix != null)
            {
                harmony.Patch(drawCaravanInfo, prefix: new HarmonyMethod(drawCaravanInfoPrefix));
                return;
            }

            string candidates = string.Empty;
            for (int i = 0; i < drawMethods.Length; i++)
            {
                if (drawMethods[i].Name == nameof(RimWorld.Planet.CaravanUIUtility.DrawCaravanInfo))
                {
                    candidates += drawMethods[i].ToString() + "; ";
                }
            }
            OHLog.Integration.Warn("HarmonySetup", null, $"Failed to resolve CaravanUIUtility.DrawCaravanInfo matching 'info' parameter. Candidates: {(string.IsNullOrEmpty(candidates) ? "none" : candidates)}");
        }

        #endregion

        #region 2. [INT-06] PROCESSING GATES & TELEMETRY

        /// <summary>
        /// Direct main-thread processing gate filtering out duplicate invalidation signals in O(1) time.
        /// Ignores mock sandbox surgeries and non-caravan wildlife before collection lookups.
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

        #region 3. [INT-06] HARMONY POSTFIX TARGETS

        /// <summary>[INT-06] Postfix hook for Pawn_HealthTracker.Notify_HediffChanged.</summary>
        private static void Notify_HediffChanged_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-06] Postfix hook for Pawn_ApparelTracker.Notify_ApparelAdded.</summary>
        private static void Notify_ApparelAdded_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-06] Postfix hook for Pawn_ApparelTracker.Notify_ApparelRemoved.</summary>
        private static void Notify_ApparelRemoved_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-06] Postfix hook for Pawn_EquipmentTracker.Notify_EquipmentAdded.</summary>
        private static void Notify_EquipmentAdded_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>[INT-06] Postfix hook for Pawn_EquipmentTracker.Notify_EquipmentRemoved.</summary>
        private static void Notify_EquipmentRemoved_Postfix(Pawn ___pawn) => ProcessInvalidationGate(___pawn);

        /// <summary>
        /// [INT-06] Postfix hook for Pawn.Kill. Immediately evicts dead pawns from all cache registries.
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
        /// [INT-06] Postfix hook for Pawn.DeSpawn. Immediately evicts non-colonist pawns (fleeing raiders, departing visitors, wild animals)
        /// from capacity registries to rapidly deflate the cache population and snap dynamic TTL back to baseline after raids.
        /// </summary>
        private static void Notify_PawnDeSpawned_Postfix(Pawn __instance)
        {
            if (__instance == null || __instance.thingIDNumber <= 0) return;

            try
            {
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

        /// <summary>[INT-06] Sets the active InfoCard rendering context gate when an in-game stat report begins drawing.</summary>
        private static void StatsReport_Prefix() => InfoCardOverlay.PushInfoCardContext();

        /// <summary>[INT-06] Resets the active InfoCard rendering context gate when an in-game stat report finishes drawing.</summary>
        private static void StatsReport_Finalizer() => InfoCardOverlay.PopInfoCardContext();

        #endregion
    }
}

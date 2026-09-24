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
        ///  occur.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the cache invalidation patches.</param>
        private static void InstallInvalidationPatches(Harmony harmony)
        {
            try
            {
                var hediffChanged = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged));
                var hediffPostfix = AccessTools.Method(typeof(HarmonySetup), nameof(Notify_HediffChanged_Postfix));
                if (hediffChanged != null && hediffPostfix != null) harmony.Patch(hediffChanged, postfix: new HarmonyMethod(hediffPostfix));

                var apparelAdded = AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded));
                var apparelAddedPostfix = AccessTools.Method(typeof(HarmonySetup), nameof(Notify_ApparelAdded_Postfix));
                if (apparelAdded != null && apparelAddedPostfix != null) harmony.Patch(apparelAdded, postfix: new HarmonyMethod(apparelAddedPostfix));

                var apparelRemoved = AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved));
                var apparelRemovedPostfix = AccessTools.Method(typeof(HarmonySetup), nameof(Notify_ApparelRemoved_Postfix));
                if (apparelRemoved != null && apparelRemovedPostfix != null) harmony.Patch(apparelRemoved, postfix: new HarmonyMethod(apparelRemovedPostfix));

                var equipAdded = AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded));
                var equipAddedPostfix = AccessTools.Method(typeof(HarmonySetup), nameof(Notify_EquipmentAdded_Postfix));
                if (equipAdded != null && equipAddedPostfix != null) harmony.Patch(equipAdded, postfix: new HarmonyMethod(equipAddedPostfix));

                var equipRemoved = AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved));
                var equipRemovedPostfix = AccessTools.Method(typeof(HarmonySetup), nameof(Notify_EquipmentRemoved_Postfix));
                if (equipRemoved != null && equipRemovedPostfix != null) harmony.Patch(equipRemoved, postfix: new HarmonyMethod(equipRemovedPostfix));

                var pawnKill = AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill));
                var pawnKillPostfix = AccessTools.Method(typeof(HarmonySetup), nameof(Notify_PawnKilled_Postfix));
                if (pawnKill != null && pawnKillPostfix != null) harmony.Patch(pawnKill, postfix: new HarmonyMethod(pawnKillPostfix));

                var pawnDeSpawn = AccessTools.Method(typeof(Pawn), nameof(Pawn.DeSpawn));
                var pawnDeSpawnPostfix = AccessTools.Method(typeof(HarmonySetup), nameof(Notify_PawnDeSpawned_Postfix));
                if (pawnDeSpawn != null && pawnDeSpawnPostfix != null) harmony.Patch(pawnDeSpawn, postfix: new HarmonyMethod(pawnDeSpawnPostfix));

                var widgetsLabel = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new Type[] { typeof(Rect), typeof(string) });
                var labelPrefix = AccessTools.Method(typeof(InfoCardOverlay), nameof(InfoCardOverlay.Prefix));
                if (widgetsLabel != null && labelPrefix != null) harmony.Patch(widgetsLabel, prefix: new HarmonyMethod(labelPrefix));

                // Gated InfoCard Intercept: Sets IsRenderingInfoCard = true only while StatsReportUtility is actively rendering
                var statsReportPrefix = AccessTools.Method(typeof(HarmonySetup), nameof(StatsReport_Prefix));
                var statsReportFinalizer = AccessTools.Method(typeof(HarmonySetup), nameof(StatsReport_Finalizer));
                var statsReportMethods = typeof(StatsReportUtility).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                for (int i = 0; i < statsReportMethods.Length; i++)
                {
                    if (statsReportMethods[i].Name == nameof(StatsReportUtility.DrawStatsReport))
                    {
                        harmony.Patch(statsReportMethods[i], prefix: new HarmonyMethod(statsReportPrefix), finalizer: new HarmonyMethod(statsReportFinalizer));
                    }
                }

                var caravanExplanationGetter = AccessTools.PropertyGetter(typeof(RimWorld.Planet.Caravan), "MassCapacityExplanation");
                var caravanExplanationPostfix = AccessTools.Method(typeof(CaravanUIIntegration), nameof(CaravanUIIntegration.Caravan_MassCapacityExplanation_Postfix));
                if (caravanExplanationGetter != null && caravanExplanationPostfix != null) harmony.Patch(caravanExplanationGetter, postfix: new HarmonyMethod(caravanExplanationPostfix));

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
                }
                else
                {
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
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("HarmonySetup", ex, "Failed to install invalidation patches.");
            }
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
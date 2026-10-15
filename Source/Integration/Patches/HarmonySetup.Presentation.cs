using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [INT-PRES-01] PRESENTATION & UI HARMONY HOOKS
    /// Intercepts InfoCard drawing routines, caravan mass capacity explanations, and caravan info bars.
    /// Resides under Source/Integration/Patches/.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. [INT-PRES-01] PRESENTATION PATCH REGISTRATION

        /// <summary>
        /// Installs presentation patches for InfoCard label overlays, rendering context gates, and caravan UI integrations.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the patches.</param>
        private static void InstallPresentationPatches(Harmony harmony)
        {
            // 1. InfoCard Typography & Icon Stamping Intercept
            Guarded("Widgets.Label", () => harmony.Patch(
                AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new Type[] { typeof(Rect), typeof(string) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(InfoCardOverlay), nameof(InfoCardOverlay.Prefix)))));

            // 2. Gated InfoCard Context Intercept: Tracks IsRenderingInfoCard during StatsReportUtility draws
            Guarded("StatsReportUtility.DrawStatsReport", () =>
            {
                var statsReportPrefix = OwnPatch(nameof(StatsReport_Prefix));
                var statsReportFinalizer = OwnPatch(nameof(StatsReport_Finalizer));
                var statsReportMethods = typeof(StatsReportUtility).GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < statsReportMethods.Length; i++)
                {
                    if (statsReportMethods[i].Name == nameof(StatsReportUtility.DrawStatsReport))
                    {
                        harmony.Patch(statsReportMethods[i], prefix: statsReportPrefix, finalizer: statsReportFinalizer);
                    }
                }
            });

            // 3. Caravan Mass Capacity Explanation Postfix
            Guarded("Caravan.MassCapacityExplanation", () => harmony.Patch(
                AccessTools.PropertyGetter(typeof(Caravan), "MassCapacityExplanation"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(CaravanUIIntegration), nameof(CaravanUIIntegration.Caravan_MassCapacityExplanation_Postfix)))));

            // 4. Caravan UI Info Bar Overload Intercept
            Guarded("CaravanUIUtility.DrawCaravanInfo", () => InstallDrawCaravanInfoPatch(harmony));
        }

        #endregion

        #region 2. [INT-PRES-02] STATS REPORT CONTEXT GATES

        /// <summary>[INT-PRES-02] Sets the active InfoCard rendering context gate when an in-game stat report begins drawing.</summary>
        private static void StatsReport_Prefix() => InfoCardOverlay.PushInfoCardContext();

        /// <summary>[INT-PRES-02] Resets the active InfoCard rendering context gate when an in-game stat report finishes drawing.</summary>
        private static void StatsReport_Finalizer() => InfoCardOverlay.PopInfoCardContext();

        #endregion

        #region 3. [INT-PRES-03] CARAVAN UI REFLECTION RESOLUTION

        /// <summary>
        /// Resolves the <c>CaravanUIUtility.DrawCaravanInfo</c> overload that takes the 'info' structure and patches it.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the patch.</param>
        private static void InstallDrawCaravanInfoPatch(Harmony harmony)
        {
            var drawCaravanInfoPrefix = AccessTools.Method(typeof(CaravanUIIntegration), nameof(CaravanUIIntegration.DrawCaravanInfo_Prefix));
            MethodInfo drawCaravanInfo = null;
            var drawMethods = typeof(CaravanUIUtility).GetMethods(BindingFlags.Public | BindingFlags.Static);

            for (int i = 0; i < drawMethods.Length; i++)
            {
                var m = drawMethods[i];
                if (m.Name == nameof(CaravanUIUtility.DrawCaravanInfo))
                {
                    var pars = m.GetParameters();
                    for (int p = 0; p < pars.Length; p++)
                    {
                        Type pType = pars[p].ParameterType;
                        if (pars[p].Name == "info" &&
                            (pType == typeof(CaravanUIUtility.CaravanInfo) ||
                             pType == typeof(CaravanUIUtility.CaravanInfo).MakeByRefType()))
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
                if (drawMethods[i].Name == nameof(CaravanUIUtility.DrawCaravanInfo))
                {
                    candidates += drawMethods[i].ToString() + "; ";
                }
            }
            OHLog.Integration.Warn("HarmonySetup", null, $"Failed to resolve CaravanUIUtility.DrawCaravanInfo matching 'info' parameter. Candidates: {(string.IsNullOrEmpty(candidates) ? "none" : candidates)}");
        }

        #endregion
    }
}
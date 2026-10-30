using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [INT-07] High-performance Harmony hooks intercepting Caravan Mass Capacity tooltips across:
    /// 1. Active World Caravans (Caravan.MassCapacityExplanation)
    /// 2. Universal Caravan UI Bars (CaravanUIUtility.DrawCaravanInfo for Form Caravan, Pods, Shuttles &amp; Split dialogs)
    /// Employs compiled dynamic IL field accessors to achieve zero reflection overhead during IMGUI rendering passes.
    /// Resides under Source/Presentation/Rendering/.
    /// </summary>
    public static class CaravanUIIntegration
    {
        #region 1. STATIC STATE & COMPILED IL ACCESSORS

        // BREAKPOINT ANCHOR: Compiled IL Fast Field Accessors (0 reflection overhead per frame)
        private static readonly AccessTools.FieldRef<Dialog_LoadTransporters, List<TransferableOneWay>> loadTransportersAccessor;
        private static readonly AccessTools.FieldRef<Dialog_SplitCaravan, List<TransferableOneWay>> splitCaravanAccessor;

        private static string cachedCaravanHeaderPrefix;

        /// <summary>Retrieves the localized caravan header prefix substring preceding format arguments for deduplication detection.</summary>
        private static string CaravanHeaderPrefix
        {
            get
            {
                if (cachedCaravanHeaderPrefix == null)
                {
                    string raw = "OverHaulers_Caravan_Header".Translate().ToString();
                    int splitIdx = raw.IndexOf('{');
                    cachedCaravanHeaderPrefix = splitIdx > 0 ? raw.Substring(0, splitIdx) : raw;
                }
                return cachedCaravanHeaderPrefix;
            }
        }

        #endregion

        #region 2. LIFECYCLE & STATIC CONSTRUCTOR

        /// <summary>
        /// Initializes the compiled IL field accessors for the Caravan UI integration hooks.
        /// </summary>
        static CaravanUIIntegration()
        {
            try
            {
                loadTransportersAccessor = AccessTools.FieldRefAccess<Dialog_LoadTransporters, List<TransferableOneWay>>("transferables");
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("CaravanUIIntegration", ex, "Failed to initialize load transporters accessor.");
            }

            try
            {
                splitCaravanAccessor = AccessTools.FieldRefAccess<Dialog_SplitCaravan, List<TransferableOneWay>>("transferables");
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("CaravanUIIntegration", ex, "Failed to initialize split caravan accessor.");
            }
        }

        #endregion

        #region 3. PUBLIC HARMONY HOOKS (CANONICAL ENTRY POINTS)

        /// <summary>
        /// Postfix hook on Caravan.MassCapacityExplanation.
        /// Compiles and prepends the aggregate Caravan Summary directly onto the world map inspect panel tooltip.
        /// </summary>
        /// <param name="__instance">The caravan instance for which the mass capacity explanation is being generated.</param>
        /// <param name="__result">The resulting mass capacity explanation string, which will be modified to include the caravan summary.</param>
        public static void Caravan_MassCapacityExplanation_Postfix(Caravan __instance, ref string __result)
        {
            if (__instance?.PawnsListForReading == null || __instance.PawnsListForReading.Count == 0) return;

            try
            {
                CaravanSummary summary = CaravanAggregator.CompileFromPawns(__instance.PawnsListForReading);
                if (summary.TotalPawnCount == 0) return;

                string caravanReport = CaravanAggregator.BuildCaravanReport(in summary);
                if (string.IsNullOrEmpty(caravanReport)) return;

                __result = MergeCaravanExplanation(caravanReport, __result);
            }
            catch (Exception ex)
            {
                OHLog.Presentation.Warn("Caravan_MassCapacityExplanation_Postfix", ex, "Failed to compile caravan summary.");
            }
        }

        /// <summary>
        /// Prefix hook on CaravanUIUtility.DrawCaravanInfo.
        /// Intercepts active Form Caravan, Drop Pod, Shuttle, and Split Caravan dialogs to inject caravan breakdowns.
        /// </summary>
        /// <param name="info">The caravan info structure containing the mass capacity explanation to be potentially modified.</param>
        public static void DrawCaravanInfo_Prefix(ref CaravanUIUtility.CaravanInfo info)
        {
            if (string.IsNullOrEmpty(info.massCapacityExplanation)) return;

            // BREAKPOINT ANCHOR: Fast O(1) Header Deduplication Gate
            if (info.massCapacityExplanation.IndexOf(CaravanHeaderPrefix, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            var windowStack = Find.WindowStack;
            if (windowStack == null) return;

            var windows = windowStack.Windows;
            if (windows == null || windows.Count == 0) return;

            // BREAKPOINT ANCHOR: Top-Down Window Stack Evaluation (O(1) active dialog detection)
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window window = windows[i];

                // Case A: Form Caravan Dialog
                if (window is Dialog_FormCaravan formDialog)
                {
                    if (formDialog.transferables != null && formDialog.transferables.Count > 0)
                    {
                        InjectSummaryIntoExplanation(formDialog.transferables, ref info.massCapacityExplanation);
                    }
                    return;
                }

                // Case B: Drop Pod / Shuttle Loading Dialog (Direct compiled IL delegate)
                if (window is Dialog_LoadTransporters loadDialog && loadTransportersAccessor != null)
                {
                    List<TransferableOneWay> transferables = loadTransportersAccessor(loadDialog);
                    if (transferables != null && transferables.Count > 0)
                    {
                        InjectSummaryIntoExplanation(transferables, ref info.massCapacityExplanation);
                    }
                    return;
                }

                // Case C: Split Caravan Dialog (Direct compiled IL delegate)
                if (window is Dialog_SplitCaravan splitDialog && splitCaravanAccessor != null)
                {
                    List<TransferableOneWay> transferables = splitCaravanAccessor(splitDialog);
                    if (transferables != null && transferables.Count > 0)
                    {
                        InjectSummaryIntoExplanation(transferables, ref info.massCapacityExplanation);
                    }
                    return;
                }
            }
        }

        #endregion

        #region 4. ALGORITHMIC SUMMARY INJECTION & STRING MERGING

        /// <summary>
        /// Injects the compiled fleet summary into the provided mass capacity explanation string.
        /// </summary>
        /// <param name="transferables">The list of transferables (pawns and items) involved in the caravan or transport operation.</param>
        /// <param name="explanation">The mass capacity explanation string to be modified with the caravan summary.</param>
        private static void InjectSummaryIntoExplanation(List<TransferableOneWay> transferables, ref string explanation)
        {
            try
            {
                CaravanSummary summary = CaravanAggregator.CompileFromTransferables(transferables);
                if (summary.TotalPawnCount == 0) return;

                string caravanReport = CaravanAggregator.BuildCaravanReport(in summary);
                if (string.IsNullOrEmpty(caravanReport)) return;

                explanation = MergeCaravanExplanation(caravanReport, explanation);
            }
            catch (Exception ex)
            {
                OHLog.Presentation.Warn("InjectSummaryIntoExplanation", ex, "Failed to inject caravan summary into mass capacity explanation.");
            }
        }

        /// <summary>
        /// Merges the OverHaulers caravan summary with the vanilla mass capacity breakdown.
        /// Preserves the vanilla breakdown and all third-party mod entries intact.
        /// </summary>
        /// <param name="caravanReport">The OverHaulers caravan summary explanation string.</param>
        /// <param name="vanillaExplanation">The original vanilla mass capacity explanation string.</param>
        /// <returns>The merged explanation string combining both the caravan summary and the vanilla breakdown.</returns>
        private static string MergeCaravanExplanation(string caravanReport, string vanillaExplanation)
        {
            if (string.IsNullOrEmpty(vanillaExplanation))
            {
                return caravanReport;
            }

            string cleanVanilla = vanillaExplanation.Trim('\r', '\n');

            return caravanReport + "\n" + cleanVanilla;
        }

        #endregion
    }
}
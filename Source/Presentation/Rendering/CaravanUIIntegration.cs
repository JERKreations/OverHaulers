using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-07] CARAVAN UI HARMONY INTERCEPT

    /// <summary>
    /// [INT-07] High-performance Harmony hooks intercepting Caravan Mass Capacity tooltips across:
    /// 1. Active World Caravans (Caravan.MassCapacityExplanation)
    /// 2. Universal Caravan UI Bars (CaravanUIUtility.DrawCaravanInfo for Form Caravan, Pods, Shuttles &amp; Split dialogs)
    /// Employs compiled dynamic IL field accessors to achieve zero reflection overhead during IMGUI rendering passes.
    /// </summary>
    public static class CaravanUIIntegration
    {
        #region FIELDS & COMPILED ACCESSORS

        // BREAKPOINT ANCHOR: Compiled IL Fast Field Accessors (0 reflection overhead per frame)
        private static readonly AccessTools.FieldRef<Dialog_LoadTransporters, List<TransferableOneWay>> loadTransportersAccessor;
        private static readonly AccessTools.FieldRef<Dialog_SplitCaravan, List<TransferableOneWay>> splitCaravanAccessor;

        /// <summary>
        /// Initializes the compiled IL field accessors for the Caravan UI integration hooks.
        /// </summary>
        static CaravanUIIntegration()
        {
            try
            {
                // Initialize the compiled IL field accessor for the load transporters dialog.
                loadTransportersAccessor = AccessTools.FieldRefAccess<Dialog_LoadTransporters, List<TransferableOneWay>>("transferables");
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("CaravanUIIntegration", ex, "Failed to initialize load transporters accessor.");
            }

            try
            {
                // Initialize the compiled IL field accessor for the split caravan dialog.
                splitCaravanAccessor = AccessTools.FieldRefAccess<Dialog_SplitCaravan, List<TransferableOneWay>>("transferables");
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("CaravanUIIntegration", ex, "Failed to initialize split caravan accessor.");
            }
        }

        #endregion

        #region HOOK 1: ACTIVE WORLD MAP CARAVANS

        /// <summary>
        /// Postfix hook on Caravan.MassCapacityExplanation.
        /// Compiles and prepends the aggregate Caravan Fleet Summary directly onto the world map inspect panel tooltip.
        /// </summary>
        /// <param name="__instance">The caravan instance for which the mass capacity explanation is being generated.</param>
        /// <param name="__result">The resulting mass capacity explanation string, which will be modified to include the fleet summary.</param>
        public static void Caravan_MassCapacityExplanation_Postfix(Caravan __instance, ref string __result)
        {
            if (__instance?.PawnsListForReading == null || __instance.PawnsListForReading.Count == 0) return;

            try
            {
                // Compile the caravan fleet summary from the list of pawns in the caravan.
                CaravanFleetSummary summary = CaravanFleetAggregator.CompileFromPawns(__instance.PawnsListForReading);
                if (summary.TotalPawnCount == 0) return;

                string fleetExplanation = CaravanFleetAggregator.BuildFleetExplanation(in summary);
                if (string.IsNullOrEmpty(fleetExplanation)) return;

                // Clean merge that strips the redundant "Mass capacity:" header from the vanilla explanation
                __result = MergeFleetExplanation(fleetExplanation, __result);
            }
            catch (Exception ex)
            {
                OHLog.Presentation.Warn("Caravan_MassCapacityExplanation_Postfix", ex, "Failed to compile caravan fleet summary.");
            }
        }

        #endregion

        #region HOOK 2: UNIVERSAL CARAVAN TOP BAR DRAWER

        /// <summary>
        /// Prefix hook on CaravanUIUtility.DrawCaravanInfo.
        /// Intercepts active Form Caravan, Drop Pod, Shuttle, and Split Caravan dialogs to inject fleet breakdowns.
        /// </summary>
        /// <param name="info">The caravan info structure containing the mass capacity explanation to be potentially modified.</param>
        /// <remarks>
        /// This prefix hook ensures that the fleet breakdown is injected into the mass capacity explanation
        /// before the caravan info is drawn in the top bar UI.
        /// </remarks>
        public static void DrawCaravanInfo_Prefix(ref CaravanUIUtility.CaravanInfo info)
        {
            if (string.IsNullOrEmpty(info.massCapacityExplanation)) return;

            // BREAKPOINT ANCHOR: Fast O(1) Sentinel Deduplication Gate
            if (info.massCapacityExplanation.IndexOf(InfoCardOverlay.TagSentinel, StringComparison.Ordinal) >= 0)
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

        /// <summary>
        /// Injects the compiled fleet summary into the provided mass capacity explanation string.
        /// </summary>
        /// <param name="transferables">The list of transferables (pawns and items) involved in the caravan or transport operation.</param>
        /// <param name="explanation">The mass capacity explanation string to be modified with the fleet summary.</param>
        private static void InjectSummaryIntoExplanation(List<TransferableOneWay> transferables, ref string explanation)
        {
            try
            {
                CaravanFleetSummary summary = CaravanFleetAggregator.CompileFromTransferables(transferables);
                if (summary.TotalPawnCount == 0) return;

                string fleetExplanation = CaravanFleetAggregator.BuildFleetExplanation(in summary);
                if (string.IsNullOrEmpty(fleetExplanation)) return;

                // Clean merge that strips the redundant "Mass capacity:" header from the vanilla explanation
                explanation = MergeFleetExplanation(fleetExplanation, explanation);
            }
            catch (Exception ex)
            {
                OHLog.Presentation.Warn("InjectSummaryIntoExplanation", ex, "Failed to inject fleet summary into mass capacity explanation.");
            }
        }

        /// <summary>
        /// Merges the OverHaulers fleet summary with the vanilla mass capacity breakdown,
        /// cleanly stripping out vanilla's duplicate "Mass capacity:" header line if present.
        /// </summary>
        private static string MergeFleetExplanation(string fleetExplanation, string vanillaExplanation)
        {
            if (string.IsNullOrEmpty(vanillaExplanation))
            {
                return fleetExplanation;
            }

            // Trim only newlines to preserve the pawn list's 2-space indentation
            string cleanVanilla = vanillaExplanation.Trim('\r', '\n');

            return fleetExplanation + "\n\n" + cleanVanilla;
        }

        #endregion
    }

    #endregion
}
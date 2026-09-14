using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-05] High-performance compiler and aggregator for Caravan Mass Capacity across multi-pawn fleets.
    /// Performs single-pass evaluation across transferable pawns using native ToStringMass formatting.
    /// </summary>
    public static class CaravanFleetAggregator
    {
        #region 1. SCRATCH STORAGE (Main-Thread Only)

        private static readonly StringBuilder pooledFleetReportBuilder = new StringBuilder(2048);

        #endregion

        #region 2. SINGLE-PASS FLEET AGGREGATION

        /// <summary>
        /// Compiles a summary of the caravan fleet's mass capacity from a list of transferable pawns.
        /// </summary>
        /// <param name="transferables">The list of transferable pawns to be aggregated.</param>
        /// <returns>A CaravanFleetSummary containing the aggregated mass capacity information for the specified transferables.</returns>
        public static CaravanFleetSummary CompileFromTransferables(List<TransferableOneWay> transferables)
        {
            CaravanFleetSummary summary = default;
            if (transferables == null || transferables.Count == 0) return summary;

            for (int t = 0; t < transferables.Count; t++)
            {
                TransferableOneWay item = transferables[t];
                if (item == null || item.CountToTransfer <= 0) continue;

                List<Thing> things = item.things;
                int transferCount = item.CountToTransfer;

                // Path A: Standard vanilla populated distinct things list
                if (things != null && things.Count > 0)
                {
                    int maxCount = Math.Min(transferCount, things.Count);
                    for (int i = 0; i < maxCount; i++)
                    {
                        if (things[i] is Pawn targetPawn && PawnDataRegistry.CanCarryCaravanMass(targetPawn))
                        {
                            AggregatePawnIntoSummary(targetPawn, ref summary);
                        }
                    }
                }
                // Path B: Modded virtualized / prototype transferables fallback
                else if (item.AnyThing is Pawn prototypePawn && PawnDataRegistry.CanCarryCaravanMass(prototypePawn))
                {
                    // Replicate the prototype pawn across the full transferCount
                    for (int i = 0; i < transferCount; i++)
                    {
                        AggregatePawnIntoSummary(prototypePawn, ref summary);
                    }
                }
            }

            return summary;
        }

        /// <summary>
        /// Compiles a summary of the caravan fleet's mass capacity from a list of pawns.
        /// </summary>
        /// <param name="pawns">The list of pawns to be aggregated.</param>
        /// <returns>A CaravanFleetSummary containing the aggregated mass capacity information for the specified pawns.</returns>
        public static CaravanFleetSummary CompileFromPawns(List<Pawn> pawns)
        {
            CaravanFleetSummary summary = default;
            if (pawns == null || pawns.Count == 0) return summary;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && PawnDataRegistry.CanCarryCaravanMass(pawn))
                {
                    AggregatePawnIntoSummary(pawn, ref summary);
                }
            }

            return summary;
        }

        /// <summary>
        /// Aggregates the mass capacity information of a single pawn into the provided caravan fleet summary.
        /// </summary>
        /// <param name="pawn">The pawn whose mass capacity information is to be aggregated.</param>
        /// <param name="summary">The caravan fleet summary to which the pawn's mass capacity information will be added.</param>
        private static void AggregatePawnIntoSummary(Pawn pawn, ref CaravanFleetSummary summary)
        {
            if (pawn == null) return;

            float speciesBaseline = IntegrationPipeline.ActiveDriver != null 
                ? IntegrationPipeline.ActiveDriver.ResolveOriginalBaseline(pawn) 
                : SpeciesBaselineCalibration.ResolveBaseline(pawn);

            if (speciesBaseline <= 0f) return;

            float offset = PawnDataRegistry.GetOffset(pawn, speciesBaseline);
            float finalCapacity = MassCapacitySolver.EnforceSafetyFloor(speciesBaseline + offset, pawn.LabelShortCap);

            summary.TotalBaseline += speciesBaseline;
            summary.TotalCapacity += finalCapacity;
            summary.TotalPawnCount++;

            if (summary.TopContributor == null || finalCapacity > summary.TopContributorCapacity)
            {
                summary.TopContributor = pawn;
                summary.TopContributorCapacity = finalCapacity;
            }

            MassCapacityModel model = PawnDataRegistry.GetDetailedModel(pawn, speciesBaseline);
            if (model?.EvaluatedParts != null && model.EvaluatedParts.Count > 0)
            {
                float pawnProsthetic = 0f;
                float pawnDeficit = 0f;
                float pawnAthletic = 0f;

                for (int p = 0; p < model.EvaluatedParts.Count; p++)
                {
                    PartViewNode node = model.EvaluatedParts[p];
                    if (node == null) continue;

                    if (node.ProstheticOffset > 0f) pawnProsthetic += node.ProstheticOffset;
                    if (node.HealthOffset < 0f) pawnDeficit += node.HealthOffset;
                    pawnAthletic += node.AthleticOffset;
                }

                summary.TotalProstheticBoost += pawnProsthetic;
                summary.TotalHealthDeficit += pawnDeficit;
                summary.TotalAthleticOffset += pawnAthletic;

                if (pawnProsthetic > SettingsDefaults.EfficiencyEpsilon)
                {
                    summary.BoostedPawnCount++;
                }

                if (pawnDeficit < -SettingsDefaults.EfficiencyEpsilon)
                {
                    summary.ImpairedPawnCount++;

                    if (summary.MostImpaired == null || pawnDeficit < summary.MostImpairedDeficit)
                    {
                        summary.MostImpaired = pawn;
                        summary.MostImpairedDeficit = pawnDeficit;
                    }
                }
            }
        }

        #endregion

        #region 3. FLEET SUMMARY EXPLANATION BUILDER

        /// <summary>
        /// Builds a textual explanation of the caravan fleet's mass capacity summary for display in the InfoCard overlay.
        /// </summary>
        /// <param name="summary">The caravan fleet summary to be explained.</param>
        /// <returns>A string containing the formatted explanation of the caravan fleet's mass capacity summary.</returns>
        public static string BuildFleetExplanation(in CaravanFleetSummary summary)
        {
            if (!UnityData.IsInMainThread || summary.TotalPawnCount == 0) return string.Empty;

            pooledFleetReportBuilder.Clear();
            pooledFleetReportBuilder.Append(InfoCardOverlay.TagSentinel);

            Settings settings = OverHaulers.settings;
            Color boostColor = settings?.colorBoosted ?? SettingsDefaults.ColorBoostedDefault;
            Color critColor = settings?.colorCritical ?? SettingsDefaults.ColorCriticalDefault;

            // 1. Compact Header Banner
            string netOffsetStr = summary.TotalNetOffset.ToStringMassOffset();
            string multStr = summary.CollectiveMultiplier.ToString("F2");
            pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_Header".Translate(multStr, netOffsetStr).ToString().Colorize(Color.cyan));

            // 2. Compact Baseline to Total Progression
            pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_BaselineProgression".Translate(
                summary.TotalBaseline.ToStringMass(), 
                summary.TotalCapacity.ToStringMass()
            ).ToString());

            // 3. Compact Prosthetic, Deficit & Athletic Rows (Inline Icon Stamped)
            if (summary.HasSignificantModifications)
            {
                string pawnLabelBoosted = summary.BoostedPawnCount == 1 
                    ? "OverHaulers_Fleet_PawnSingle".Translate().ToString() 
                    : "OverHaulers_Fleet_PawnPlural".Translate().ToString();

                string pawnLabelImpaired = summary.ImpairedPawnCount == 1 
                    ? "OverHaulers_Fleet_PawnSingle".Translate().ToString() 
                    : "OverHaulers_Fleet_PawnPlural".Translate().ToString();

                if (summary.TotalProstheticBoost > SettingsDefaults.EfficiencyEpsilon)
                {
                    string boostStr = summary.TotalProstheticBoost.ToStringMassOffset().Colorize(boostColor);
                    pooledFleetReportBuilder.AppendLine($"  {InfoCardOverlay.TagProsthetic} {boostStr} [{summary.BoostedPawnCount} {pawnLabelBoosted}]");
                }

                if (summary.TotalHealthDeficit < -SettingsDefaults.EfficiencyEpsilon)
                {
                    string deficitStr = summary.TotalHealthDeficit.ToStringMassOffset().Colorize(critColor);
                    pooledFleetReportBuilder.AppendLine($"  {InfoCardOverlay.TagInjury} {deficitStr} [{summary.ImpairedPawnCount} {pawnLabelImpaired}]");
                }

                if (Math.Abs(summary.TotalAthleticOffset) > SettingsDefaults.EfficiencyEpsilon)
                {
                    Color athColor = summary.TotalAthleticOffset > 0f ? boostColor : critColor;
                    string athleticStr = summary.TotalAthleticOffset.ToStringMassOffset().Colorize(athColor);
                    pooledFleetReportBuilder.AppendLine($"  {InfoCardOverlay.TagAthletic} {athleticStr}");
                }
            }

            // 4. Standout Performers
            if (summary.TopContributor != null)
            {
                pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_TopContributor".Translate(
                    summary.TopContributor.LabelShortCap, 
                    summary.TopContributorCapacity.ToStringMass()
                ).ToString());
            }

            if (summary.MostImpaired != null)
            {
                pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_MostImpaired".Translate(
                    summary.MostImpaired.LabelShortCap, 
                    summary.MostImpairedDeficit.ToStringMassOffset()
                ).ToString().Colorize(critColor));
            }

            string result = pooledFleetReportBuilder.ToString();
            pooledFleetReportBuilder.Clear();
            return result;
        }

        #endregion
    }
}
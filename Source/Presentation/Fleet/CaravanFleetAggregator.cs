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
        /// Evaluates gross capacity alongside net delta offsets for true standout performer tracking.
        /// </summary>
        /// <param name="pawn">The pawn whose mass capacity information is to be aggregated.</param>
        /// <param name="summary">The caravan fleet summary to which the pawn's mass capacity information will be added.</param>
        private static void AggregatePawnIntoSummary(Pawn pawn, ref CaravanFleetSummary summary)
        {
            if (pawn == null) return;

            // Resolve the species baseline capacity for the pawn
            float speciesBaseline = PawnDataRegistry.ResolveBaseline(pawn);

            // Skip pawns with non-positive baseline capacity
            if (speciesBaseline <= 0f) return;

            float finalCapacity = PawnDataRegistry.GetCapacity(pawn, speciesBaseline);
            float netOffset = finalCapacity - speciesBaseline;

            summary.TotalBaseline += speciesBaseline;
            summary.TotalCapacity += finalCapacity;
            summary.TotalPawnCount++;

            // 1. Gross Evaluation: Highest absolute physical capacity (The Heavy Lifter)
            if (summary.TopGrossContributor == null || finalCapacity > summary.TopGrossCapacity)
            {
                summary.TopGrossContributor = pawn;
                summary.TopGrossCapacity = finalCapacity;
            }

            // 2. Net Evaluation: Highest positive physiological offset above baseline (The Biomechanical Champion)
            if (netOffset > SettingsDefaults.EfficiencyEpsilon)
            {
                if (summary.TopNetContributor == null || netOffset > summary.TopNetOffset)
                {
                    summary.TopNetContributor = pawn;
                    summary.TopNetOffset = netOffset;
                    summary.TopNetCapacity = finalCapacity;
                }
            }

            // 3. Impairment Evaluation: Greatest net physiological deficit below baseline
            if (netOffset < -SettingsDefaults.EfficiencyEpsilon)
            {
                if (summary.MostImpaired == null || netOffset < summary.MostImpairedNetOffset)
                {
                    summary.MostImpaired = pawn;
                    summary.MostImpairedNetOffset = netOffset;
                    summary.MostImpairedCapacity = finalCapacity;
                }
            }
        }

        #endregion

        #region 3. FLEET SUMMARY EXPLANATION BUILDER

        /// <summary>
        /// Builds a textual explanation of the caravan fleet's mass capacity summary for display in the InfoCard overlay.
        /// Formats into a vertically stacked presentation with smart Net/Gross performer collapse and symmetrical gear callouts.
        /// </summary>
        /// <param name="summary">The caravan fleet summary to be explained.</param>
        /// <returns>A string containing the formatted explanation of the caravan fleet's mass capacity summary.</returns>
        public static string BuildFleetExplanation(in CaravanFleetSummary summary)
        {
            if (!UnityData.IsInMainThread || summary.TotalPawnCount == 0) return string.Empty;

            pooledFleetReportBuilder.Clear();
            pooledFleetReportBuilder.AppendLine(); // Visual spacer beneath vanilla's secondary "Mass capacity:" section header

            // 1. Header Banner
            string netOffsetStr = summary.TotalNetOffset.ToStringMassOffset();
            string multStr = summary.CollectiveMultiplier.ToString("F2");
            pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_Header".Translate(multStr, netOffsetStr).ToString());

            // 2. Physical Total
            pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_Total".Translate(summary.TotalCapacity.ToStringMass()).ToString());

            // 3. Standout Performers: Net vs Gross with Smart Collapse
            bool hasNet = summary.TopNetContributor != null;
            bool hasGross = summary.TopGrossContributor != null;

            // Case A: Split — Top net gainer is different from top gross carrier (e.g. Augmented Colonist vs Pack Elephant)
            if (hasNet && hasGross && summary.TopNetContributor != summary.TopGrossContributor)
            {
                pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_TopContributorNet".Translate(summary.TopNetContributor.LabelShortCap).ToString());
                AppendPerformerDetail(pooledFleetReportBuilder, summary.TopNetContributor, summary.TopNetCapacity);

                pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_TopContributorGross".Translate(summary.TopGrossContributor.LabelShortCap).ToString());
                AppendPerformerDetail(pooledFleetReportBuilder, summary.TopGrossContributor, summary.TopGrossCapacity);
            }
            // Case B: Collapsed — Single standout asset (same pawn won both, or nobody has net augmentations)
            else if (hasGross)
            {
                Pawn topPawn = hasNet ? summary.TopNetContributor : summary.TopGrossContributor;
                float topCapacity = hasNet ? summary.TopNetCapacity : summary.TopGrossCapacity;

                pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_TopContributor".Translate(topPawn.LabelShortCap).ToString());
                AppendPerformerDetail(pooledFleetReportBuilder, topPawn, topCapacity);
            }

            // 4. Most Impaired (Greatest Net Deficit)
            if (summary.MostImpaired != null)
            {
                pooledFleetReportBuilder.AppendLine("OverHaulers_Fleet_MostImpaired".Translate(summary.MostImpaired.LabelShortCap).ToString());
                AppendPerformerDetail(pooledFleetReportBuilder, summary.MostImpaired, summary.MostImpairedCapacity);
            }

            string result = pooledFleetReportBuilder.ToString();
            pooledFleetReportBuilder.Clear();
            return result;
        }

        /// <summary>
        /// Appends the indented detail line for a standout performer, calculating gear capacity dynamically.
        /// Symmetrically formats both boosted and impaired performers.
        /// </summary>
        private static void AppendPerformerDetail(StringBuilder sb, Pawn pawn, float physicalValue)
        {
            float totalWithGear = MassUtility.Capacity(pawn, null);
            float gearOffset = totalWithGear - physicalValue;

            if (gearOffset > SettingsDefaults.EfficiencyEpsilon)
            {
                sb.AppendLine("OverHaulers_Fleet_PerformerDetail_Gear".Translate(
                    physicalValue.ToStringMassOffset(),
                    gearOffset.ToStringMassOffset()
                ).ToString());
            }
            else
            {
                sb.AppendLine("OverHaulers_Fleet_PerformerDetail".Translate(
                    physicalValue.ToStringMassOffset()
                ).ToString());
            }
        }

        #endregion
    }
}
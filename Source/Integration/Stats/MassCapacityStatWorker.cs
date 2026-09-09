using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-03] STANDALONE STAT WORKER ENGINE

    /// <summary>
    /// [INT-03] Evaluates and renders Caravan Mass Capacity stats on pawns when operating in standalone mode.
    /// Delivers the OverHaulers breakdown via GetExplanationUnfinalized using native ToStringMass formatting.
    /// </summary>
    public class MassCapacityStatWorker : StatWorker
    {
        #region STAT VISIBILITY GATE

        public override bool ShouldShowFor(StatRequest req)
        {
            if (!(IntegrationPipeline.ActiveDriver is VanillaStatDriver))
            {
                return false;
            }

            if (req.HasThing && req.Thing is Pawn pawn)
            {
                return PawnDataRegistry.CanCarryCaravanMass(pawn);
            }
            return base.ShouldShowFor(req);
        }

        #endregion

        #region STAT VALUE EVALUATION

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            if (req.HasThing && req.Thing is Pawn pawn)
            {
                if (!PawnDataRegistry.CanCarryCaravanMass(pawn))
                {
                    return 0f;
                }

                float baseline = IntegrationPipeline.ActiveDriver.ResolveOriginalBaseline(pawn);
                float offset = PawnDataRegistry.GetOffset(pawn, baseline);

                return baseline + offset;
            }
            return base.GetValueUnfinalized(req, applyPostProcess);
        }

        #endregion

        #region INFOCARD EXPLANATION BREAKDOWN

        /// <summary>
        /// Gets the detailed explanation for the pawn's mass capacity stat, including the OverHaulers breakdown.
        /// </summary>
        /// <param name="req">The stat request containing the pawn.</param>
        /// <param name="numberSense">The number sense for formatting the stat value.</param>
        /// <returns>A string containing the detailed explanation of the mass capacity stat.</returns>
        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            if (req.HasThing && req.Thing is Pawn pawn)
            {
                if (!PawnDataRegistry.CanCarryCaravanMass(pawn))
                {
                    return string.Empty;
                }

                float baselineCapacity = IntegrationPipeline.ActiveDriver.ResolveOriginalBaseline(pawn);
                MassCapacityModel model = PawnDataRegistry.GetDetailedModel(pawn, baselineCapacity);

                string baseLine = "StatsReport_BaseValue".Translate() + ": " + baselineCapacity.ToStringMass();
                string explanation = model?.Explanation ?? string.Empty;

                // Strip TagSentinel from the sub-model if present so we can place it cleanly at Index 0
                if (explanation.StartsWith(InfoCardOverlay.TagSentinel, StringComparison.Ordinal))
                {
                    explanation = explanation.Substring(InfoCardOverlay.TagSentinel.Length);
                }

                return InfoCardOverlay.TagSentinel + baseLine + "\n\n" + explanation;
            }
            return base.GetExplanationUnfinalized(req, numberSense);
        }

        #endregion

        #region INFOCARD HYPERLINKS

        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest req)
        {
            return HyperlinkUtility.ResolveHyperlinks(req);
        }

        #endregion
    }

    #endregion
}
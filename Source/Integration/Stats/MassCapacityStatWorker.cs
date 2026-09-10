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

        /// <summary>
        /// Determines whether the mass capacity stat should be shown for the given stat request.
        /// </summary>
        /// <param name="req">The stat request containing the context for which the visibility is being determined.</param>
        /// <returns>True if the mass capacity stat should be shown; otherwise, false.</returns>
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

        /// <summary>
        /// Calculates the unfinalized mass capacity value for the given stat request, taking into account the OverHaulers breakdown.
        /// </summary>
        /// <param name="req">The stat request containing the pawn for which the value is being calculated.</param>
        /// <param name="applyPostProcess">Indicates whether post-processing should be applied to the calculated value.</param>
        /// <returns>The unfinalized mass capacity value for the specified pawn.</returns>
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
                // Ingress Gate: Completely skip non-caravan species during live play
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

        /// <summary>
        /// Retrieves the hyperlinks to the info card for the specified pawn, allowing users to quickly navigate to related information.
        /// </summary>
        /// <param name="req">The stat request containing the pawn for which the info card hyperlinks are being retrieved.</param>
        /// <returns>An enumerable of Dialog_InfoCard.Hyperlink objects related to the specified pawn.</returns>
        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest req)
        {
            return HyperlinkUtility.ResolveHyperlinks(req);
        }

        #endregion
    }

    #endregion
}
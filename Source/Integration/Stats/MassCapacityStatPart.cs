using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-04] VEF / EXTERNAL STATPART INTEGRATION DRIVER

    /// <summary>
    /// [INT-04] Integrated-mode StatPart attached dynamically to third-party caravan mass capacity StatDefs 
    /// (such as Vanilla Expanded Framework's 'VEF_MassCarryCapacity' or custom mod stats registered via MassCapacityDriverDef).
    /// </summary>
    /// <remarks>
    /// ARCHITECTURAL DUAL-PIPELINE CONTEXT:
    /// This class is the Integrated Mode counterpart to MassCapacityStatWorker.
    /// - MassCapacityStatWorker: Used in Standalone Mode when no external mod defines a caravan mass StatDef.
    /// - MassCapacityStatPart: Used in Integrated Mode when a foreign mod already owns the StatDef.
    ///   Instead of replacing the foreign StatWorker, OverHaulers dynamically injects this StatPart into 
    ///   the external stat's 'parts' list, smoothly appending our skeletal offsets and InfoCard explanations
    ///   without overriding third-party logic (e.g. backpack gear bonuses).
    /// </remarks>
    public class MassCapacityStatPart : StatPart
    {
        #region STATPART TRANSFORM & EXPLANATION

        public override void TransformValue(StatRequest statRequest, ref float val)
        {
            // BREAKPOINT ANCHOR: Dummy Evaluation Bypass
            if (SpeciesBaselineCalibration.IsResolvingBaseline) return;

            // INGRESS GATE: Completely skip non-caravan species during live play
            if (statRequest.HasThing && statRequest.Thing is Pawn pawn && PawnDataRegistry.CanCarryCaravanMass(pawn))
            {
                float bioBaseline = IntegrationPipeline.ActiveDriver.ResolveOriginalBaseline(pawn);
                if (bioBaseline <= 0f) return;

                float skeletalOffset = PawnDataRegistry.GetOffset(pawn, bioBaseline);
                val += skeletalOffset;

                // EGRESS CLAMP: Prevent external stat debuffs + our offset from driving capacity negative
                val = MassCapacitySolver.EnforceSafetyFloor(val, pawn.LabelShortCap);
            }
        }

        /// <summary>
        /// Appends the detailed OverHaulers anatomical breakdown into the parent stat's InfoCard explanation.
        /// </summary>
        /// <param name="statRequest">The stat request containing the pawn for which the explanation is being generated.</param>
        /// <returns>A formatted string detailing the mass capacity calculation and anatomical breakdown.</returns>
        public override string ExplanationPart(StatRequest statRequest)
        {
            if (statRequest.HasThing && statRequest.Thing is Pawn pawn)
            {
                float bioBaseline = IntegrationPipeline.ActiveDriver.ResolveOriginalBaseline(pawn);
                MassCapacityModel detailedMassModel = PawnDataRegistry.GetDetailedModel(pawn, bioBaseline);
                
                return detailedMassModel?.Explanation ?? string.Empty;
            }
            return null;
        }

        /// <summary>
        /// Appends clickable InfoCard hyperlinks for installed augmentations into the parent foreign stat.
        /// </summary>
        /// <param name="statRequest">The stat request containing the pawn for which hyperlinks are being retrieved.</param>
        /// <returns>An enumerable of Dialog_InfoCard.Hyperlink objects representing installed augmentations.</returns>
        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest statRequest)
        {
            return HyperlinkUtility.ResolveHyperlinks(statRequest);
        }

        #endregion
    }

    #endregion
}
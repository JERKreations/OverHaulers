using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-04] VEF / EXTERNAL STATPART INTEGRATION DRIVER

    public class MassCapacityStatPart : StatPart
    {
        #region STATPART TRANSFORM & EXPLANATION

        public override void TransformValue(StatRequest statRequest, ref float val)
        {
            // BREAKPOINT ANCHOR: Dummy Evaluation Bypass
            if (ModpackBaselineCalibration.IsResolvingBaseline) return;

            // Ingress Gate: Completely skip non-caravan species during live play
            if (statRequest.HasThing && statRequest.Thing is Pawn pawn && PawnDataRegistry.CanCarryCaravanMass(pawn))
            {
                float bioBaseline = IntegrationPipeline.ActiveDriver.ResolveOriginalBaseline(pawn);
                if (bioBaseline <= 0f) return;

                float skeletalOffset = PawnDataRegistry.GetOffset(pawn, bioBaseline);
                val += skeletalOffset;
            }
        }

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

        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest statRequest)
        {
            return HyperlinkUtility.ResolveHyperlinks(statRequest);
        }

        #endregion
    }

    #endregion
}
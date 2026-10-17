using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-04] FOREIGN STAT ADOPTION HOOK (STATPART)

    /// <summary>
    /// StatPart injected into foreign mass capacity StatDefs (e.g., VEF_MassCarryCapacity).
    /// Responsible for delivering the OverHaulers anatomical capacity offset and injecting the
    /// detailed anatomical breakdown and bionic hyperlinks into the foreign mod's InfoCard dialog.
    /// Resides under Source/Integration/Stats/.
    /// </summary>
    public class MassCapacityStatPart : StatPart
    {
        [ThreadStatic]
        private static bool isTransforming;

        #region 1. ACTIVE VALUE TRANSFORM (ADOPTED STAT INTEGRATION)

        /// <summary>
        /// Transforms the adopted foreign stat value by adding the pawn's biological mass capacity offset.
        /// Integrates Over Haulers directly into the foreign stat's calculation chain alongside gear offsets.
        /// Shielded against baseline calibration recursion, dummy pawn synthesis, and re-entrancy loops.
        /// </summary>
        /// <param name="statRequest">The stat request containing the pawn being evaluated.</param>
        /// <param name="val">The running value of the stat, modified by the anatomical offset.</param>
        public override void TransformValue(StatRequest statRequest, ref float val)
        {
            // EARLY EXIT: If the foreign stat is not adopted, skip the transformation entirely.
            if (!IntegrationPipeline.IsForeignStatAdopted) return;

            // EARLY EXIT: If the species baseline is currently being resolved, skip the transformation to avoid recursion.
            if (SpeciesBaselineCalibration.IsResolvingBaseline) return;

            // EARLY EXIT: If we are already transforming this stat (re-entrancy), skip to prevent double-counting.
            if (isTransforming) return;

            if (statRequest.HasThing && statRequest.Thing is Pawn pawn && PawnDataRegistry.CanCarryCaravanMass(pawn))
            {
                // BREAKPOINT ANCHOR: Headless Dummy Shield
                if (pawn.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId) return;

                isTransforming = true;
                try
                {
                    float bioBaseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
                    float offset = PawnDataRegistry.GetOffset(pawn, bioBaseline);
                    val += offset;
                    val = MassCapacitySolver.EnforceSafetyFloorForPawn(val, pawn);
                }
                finally
                {
                    isTransforming = false;
                }
            }
        }

        #endregion

        #region 2. INFOCARD EXPLANATION BREAKDOWN

        /// <summary>
        /// Appends the detailed OverHaulers anatomical breakdown into the parent foreign stat's InfoCard explanation.
        /// Preceding third-party explanation lines (such as backpack offsets) are preserved.
        /// </summary>
        /// <param name="statRequest">The stat request containing the pawn for which the explanation is being generated.</param>
        /// <returns>A formatted string detailing the mass capacity calculation and anatomical breakdown, or null if inactive.</returns>
        public override string ExplanationPart(StatRequest statRequest)
        {
            // EARLY EXIT: If the foreign stat is not adopted, skip the explanation entirely.
            if (!IntegrationPipeline.IsForeignStatAdopted) return null;

            // EARLY EXIT: If the species baseline is currently being resolved, skip the explanation to avoid recursion.
            if (SpeciesBaselineCalibration.IsResolvingBaseline) return null;

            // If the stat request has a valid pawn and the pawn can carry caravan mass, proceed with the explanation.
            if (statRequest.HasThing && statRequest.Thing is Pawn pawn && PawnDataRegistry.CanCarryCaravanMass(pawn))
            {
                // EARLY EXIT: If the pawn is a headless shell, skip the explanation entirely.
                if (pawn.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId) return null;

                float bioBaseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
                MassCapacityModel detailedMassModel = DetailedModelRegistry.GetDetailedModel(pawn, bioBaseline);
                
                return detailedMassModel?.Explanation ?? string.Empty;
            }
            return null;
        }

        #endregion

        #region 3. INFOCARD HYPERLINK DISPATCH

        /// <summary>
        /// Appends clickable InfoCard hyperlinks for installed augmentations into the parent foreign stat dialog.
        /// </summary>
        /// <param name="statRequest">The stat request containing the pawn for which hyperlinks are being retrieved.</param>
        /// <returns>An enumerable of Dialog_InfoCard.Hyperlink objects representing installed augmentations.</returns>
        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest statRequest)
        {
            // EARLY EXIT: If the species baseline is currently being resolved, skip the hyperlinks to avoid recursion.
            if (SpeciesBaselineCalibration.IsResolvingBaseline) return null;

            // EARLY EXIT: If the foreign stat is not adopted, skip the hyperlinks entirely.
            if (statRequest.HasThing && statRequest.Thing is Pawn pawn &&
                pawn.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId)
            {
                return null;
            }

            return HyperlinkUtility.ResolveHyperlinks(statRequest);
        }

        #endregion
    }

    #endregion
}
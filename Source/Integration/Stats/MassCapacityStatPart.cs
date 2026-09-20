using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-04] FOREIGN STAT ADOPTION HOOK (STATPART)

    /// <summary>
    /// StatPart injected into foreign mass capacity StatDefs (e.g., VEF_MassCarryCapacity).
    /// Purely responsible for injecting the OverHaulers anatomical breakdown and bionic hyperlinks
    /// into the foreign mod's InfoCard dialog without modifying numerical values.
    /// Direct game-engine carrying capacity is decoupled and universally delivered via MassUtility.Capacity.
    /// </summary>
    public class MassCapacityStatPart : StatPart
    {
        #region 1. PASSIVE VALUE TRANSFORM (ZERO-MATH GUARD)

        /// <summary>
        /// Pure presentation adoption: Numerical mass delivery is universally handled via Harmony on MassUtility.Capacity.
        /// Transforming foreign stat values directly is intentionally a no-op to prevent clashing with gear mass,
        /// apparel movement debuffs, or foreign additive offsets (e.g. backpacks).
        /// </summary>
        /// <param name="statRequest">The stat request containing the pawn being evaluated.</param>
        /// <param name="val">The running value of the stat, left strictly unmodified.</param>
        public override void TransformValue(StatRequest statRequest, ref float val)
        {
            // UNIVERSAL EGRESS ARCHITECTURE:
            // Direct numerical offset delivery is handled exclusively via Harmony postfix on MassUtility.Capacity.
            // Leaving foreign stat values unmodified prevents order-of-operation inversion and apparel weight clamping.
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
            // Only deliver explanation when actively adopting a foreign stat card
            if (!IntegrationPipeline.IsForeignStatAdopted) return null;

            if (statRequest.HasThing && statRequest.Thing is Pawn pawn && PawnDataRegistry.CanCarryCaravanMass(pawn))
            {
                float bioBaseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
                MassCapacityModel detailedMassModel = PawnDataRegistry.GetDetailedModel(pawn, bioBaseline);
                
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
            return HyperlinkUtility.ResolveHyperlinks(statRequest);
        }

        #endregion
    }

    #endregion
}
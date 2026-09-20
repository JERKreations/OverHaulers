using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-03] NATIVE CARAVAN STAT WORKER

    /// <summary>
    /// StatWorker for the native OverHaulers_CaravanMassCapacity stat.
    /// Handles stat evaluation, InfoCard explanation rendering, and hyperlinking when operating
    /// in native standalone presentation mode.
    /// </summary>
    public class MassCapacityStatWorker : StatWorker
    {
        #region 1. STAT VISIBILITY & DEDUPLICATION GATE

        /// <summary>
        /// Determines whether the native mass capacity stat should be displayed on the target's InfoCard.
        /// When an external mod's stat is adopted, this is suppressed to eliminate duplicate rows on pawns.
        /// </summary>
        /// <param name="req">The stat request containing the target thing to evaluate.</param>
        /// <returns>True if the stat should be displayed; otherwise, false.</returns>
        public override bool ShouldShowFor(StatRequest req)
        {
            if (IntegrationPipeline.IsForeignStatAdopted)
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

        #region 2. NATIVE VALUE CALCULATION

        /// <summary>
        /// Calculates the unfinalized capacity value directly from the species baseline and PawnDataRegistry.
        /// </summary>
        /// <param name="req">The stat request containing the pawn being evaluated.</param>
        /// <param name="applyPostProcess">Whether post-processing should be applied.</param>
        /// <returns>The calculated unfinalized mass capacity in kilograms.</returns>
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            if (req.HasThing && req.Thing is Pawn pawn)
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
                return PawnDataRegistry.GetCapacity(pawn, baseline);
            }
            return base.GetValueUnfinalized(req, applyPostProcess);
        }

        #endregion

        #region 3. INFOCARD EXPLANATION BREAKDOWN

        /// <summary>
        /// Generates the complete anatomical report breakdown for the pawn's native InfoCard dialog.
        /// </summary>
        /// <param name="req">The stat request containing the pawn for which the explanation is being generated.</param>
        /// <param name="numberSense">The number sense format requested by the game engine.</param>
        /// <returns>A formatted explanation breakdown detailing all biological systems and modifications.</returns>
        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            if (req.HasThing && req.Thing is Pawn pawn)
            {
                float baselineCapacity = SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
                MassCapacityModel detailedMassModel = PawnDataRegistry.GetDetailedModel(pawn, baselineCapacity);
                return detailedMassModel?.Explanation ?? string.Empty;
            }

            return base.GetExplanationUnfinalized(req, numberSense);
        }

        #endregion

        #region 4. INFOCARD HYPERLINK DISPATCH

        /// <summary>
        /// Retrieves clickable InfoCard hyperlinks for installed augmentations on the pawn.
        /// </summary>
        /// <param name="req">The stat request containing the target pawn.</param>
        /// <returns>An enumerable of Dialog_InfoCard.Hyperlink objects for installed augmentations.</returns>
        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest req)
        {
            return HyperlinkUtility.ResolveHyperlinks(req);
        }

        #endregion
    }

    #endregion
}
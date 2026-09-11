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
    /// <remarks>
    /// ARCHITECTURAL DESIGN RATIONALE (WHY THIS CLASS EXISTS &amp; WHY IDE SHOWS "0 REFERENCES"):
    /// 
    /// 1. The Dual-Pipeline Architecture:
    ///    - Integrated Mode (Third-Party / VEF): When external mods like Vanilla Expanded Framework are active,
    ///      OverHaulers attaches MassCapacityStatPart to the foreign mod's existing StatDef (e.g. VEF_MassCarryCapacity).
    ///    - Standalone Mode (Pure Vanilla / Forced): Vanilla RimWorld does NOT have a Caravan Mass Capacity StatDef.
    ///      Vanilla calculates mass purely through a static math helper: MassUtility.Capacity(Pawn). Because it isn't
    ///      a StatDef, vanilla pawns have no inspectable Caravan Mass stat on their InfoCard ('i').
    ///      VanillaStatDriver creates a dynamic StatDef ('OverHaulers_CaravanMassCapacity') at runtime and binds its
    ///      workerClass directly to this MassCapacityStatWorker.
    /// 
    /// 2. The IDE "0 References" Optical Illusion:
    ///    Roslyn / IDE code analysis will report 0 callers for the overridden methods in this file. 
    ///    This is because this class is instantiated via reflection by RimWorld's StatDef.Worker getter
    ///    (Activator.CreateInstance), and its virtual methods (ShouldShowFor, GetValueUnfinalized, 
    ///    GetExplanationUnfinalized, GetInfoCardHyperlinks) are invoked polymorphically by the base game's
    ///    UI engine (StatsReportUtility, Dialog_InfoCard, CharacterCardUtility).
    /// 
    /// 3. Consequences of Removal:
    ///    If this file is deleted or pruned as "unused":
    ///    - Standalone / vanilla games fall back to base StatWorker.
    ///    - The multi-tiered InfoCard breakdown (Total Body Rating, [P]/[A]/[I] icons, and structural groups) ceases to render.
    ///    - Clickable bionic/implant hyperlinks inside the InfoCard disappear.
    ///    - Non-caravan animals will erroneously display mass capacity stats due to losing ShouldShowFor gating.
    /// </remarks>
    public class MassCapacityStatWorker : StatWorker
    {
        #region STAT VISIBILITY GATE

        /// <summary>
        /// Determines whether the standalone mass capacity stat should be shown on the target's InfoCard.
        /// Filters out non-caravan-capable wildlife during standard gameplay.
        /// Invoked by RimWorld's StatsReportUtility via polymorphic dispatch.
        /// </summary>
        /// <param name="req">The stat request containing the context for which visibility is being determined.</param>
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
        /// Calculates the unfinalized total mass capacity value (Baseline + Biological Offset) for the given pawn.
        /// Invoked by RimWorld's StatWorker.GetValue pipeline.
        /// </summary>
        /// <param name="req">The stat request containing the pawn for which the value is being calculated.</param>
        /// <param name="applyPostProcess">Indicates whether post-processing should be applied to the calculated value.</param>
        /// <returns>The unfinalized mass capacity value for the specified pawn in kg.</returns>
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            if (req.HasThing && req.Thing is Pawn pawn)
            {
                // INGRESS GATE: Completely skip non-caravan species during live play
                if (!PawnDataRegistry.CanCarryCaravanMass(pawn))
                {
                    return 0f;
                }

                float baseline = IntegrationPipeline.ActiveDriver.ResolveOriginalBaseline(pawn);
                float offset = PawnDataRegistry.GetOffset(pawn, baseline);

                // EGRESS CLAMP: Guarantee the actual game result obeys the safety floor
                float finalVal = baseline + offset;
                return MassCapacitySolver.EnforceSafetyFloor(finalVal, pawn.LabelShortCap);
            }
            return base.GetValueUnfinalized(req, applyPostProcess);
        }

        #endregion

        #region INFOCARD EXPLANATION BREAKDOWN

        /// <summary>
        /// Compiles and delivers the formatted OverHaulers breakdown (Total Body Rating, Capacities, Athletic Impacts,
        /// and Regional Structural Groups) for rendering inside the pawn's InfoCard dialog.
        /// Prepend TagSentinel so InfoCardOverlay intercepts and stamps inline bionic/athletic/injury icons.
        /// Invoked by RimWorld's StatWorker.GetExplanationFull pipeline.
        /// </summary>
        /// <param name="req">The stat request containing the pawn.</param>
        /// <param name="numberSense">The number sense for formatting the stat value.</param>
        /// <returns>A formatted string detailing the mass capacity calculation and anatomical breakdown.</returns>
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
        /// Resolves clickable hyperlinks for installed prosthetics, bionics, and implants inside the InfoCard.
        /// Invoked by RimWorld's Dialog_InfoCard via polymorphic dispatch.
        /// </summary>
        /// <param name="req">The stat request containing the pawn for which hyperlinks are being retrieved.</param>
        /// <returns>An enumerable of Dialog_InfoCard.Hyperlink objects representing installed augmentations.</returns>
        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest req)
        {
            return HyperlinkUtility.ResolveHyperlinks(req);
        }

        #endregion
    }

    #endregion
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [TEST-06] RECIPE CATALOG ENTRY MODELS

    /// <summary>
    /// [TEST-06] Immutable descriptor representing an indexed surgery recipe or medical procedure.
    /// Encapsulates surgical parameters and athletic capacity metadata for the Test Bench.
    /// </summary>
    public readonly struct MedicalOperationEntry
    {
        public readonly RecipeDef Recipe;
        public readonly HediffDef Hediff;
        public readonly string Label;
        public readonly float Efficiency;
        public readonly bool IsReplacement;
        public readonly bool AffectsAthletics;

        /// <summary>
        /// Initializes a new instance of the <see cref="MedicalOperationEntry"/> struct.
        /// </summary>
        /// <param name="recipe">The recipe definition associated with the operation.</param>
        /// <param name="hediff">The hediff definition resulting from the operation.</param>
        /// <param name="label">The display label for the operation.</param>
        /// <param name="efficiency">The efficiency impact of the operation.</param>
        /// <param name="isReplacement">Indicates whether the operation involves a replacement.</param>
        /// <param name="affectsAthletics">Indicates whether the operation affects athletic capacity.</param>
        public MedicalOperationEntry(
            RecipeDef recipe,
            HediffDef hediff,
            string label,
            float efficiency,
            bool isReplacement,
            bool affectsAthletics)
        {
            Recipe = recipe;
            Hediff = hediff;
            Label = label;
            Efficiency = efficiency;
            IsReplacement = isReplacement;
            AffectsAthletics = affectsAthletics;
        }
    }

    /// <summary>
    /// One pawn-specific recipe target returned by the native RimWorld recipe worker.
    /// This is procedure discoverability only; ingredients, surgeon skill and bill confirmation
    /// must still be checked immediately before a live action is scheduled.
    /// </summary>
    public readonly struct MedicalProcedureTarget
    {
        public readonly RecipeDef Recipe;
        public readonly BodyPartRecord Part;
        public readonly string Label;
        public readonly string UnavailableReason;

        /// <summary>
        /// Initializes a new instance of the <see cref="MedicalProcedureTarget"/> struct.
        /// </summary>
        /// <param name="recipe">The recipe definition associated with the procedure.</param>
        /// <param name="part">The body part targeted by the procedure.</param>
        /// <param name="label">The display label for the procedure.</param>
        /// <param name="unavailableReason">The reason the procedure is unavailable, if any.</param>
        public MedicalProcedureTarget(
            RecipeDef recipe,
            BodyPartRecord part,
            string label,
            string unavailableReason)
        {
            Recipe = recipe;
            Part = part;
            Label = label;
            UnavailableReason = unavailableReason;
        }
    }

    #endregion

    #region 2. [TEST-06] CONDITION METADATA MODELS

    /// <summary>
    /// [TEST-06] Presentation groups for an impactful condition definition.
    /// Medical entries can belong to both target-scope groups when both target kinds are known.
    /// </summary>
    [Flags]
    public enum MedicalConditionCategory : byte
    {
        None = 0,
        Ingestibles = 1 << 0,
        SystemicMedical = 1 << 1,
        LocalizedMedical = 1 << 2,
        MedicalTargetUnknown = 1 << 3,
        Other = 1 << 4
    }

    /// <summary>
    /// Evidence that a condition can be granted by a known game definition source.
    /// </summary>
    [Flags]
    public enum MedicalConditionSource : ushort
    {
        None = 0,
        ConditionDefinition = 1 << 0,
        Ingestible = 1 << 1,
        SpeciesHediffGiver = 1 << 2,
        HediffDefinitionGiver = 1 << 3,
        HediffStageGiver = 1 << 4,
        Other = 1 << 5,
        DiseaseIncident = 1 << 6,
        IncidentPawnHediff = 1 << 7,
        WoundInfection = 1 << 8
    }

    /// <summary>
    /// Describes a target possibility proven by the source definition metadata.
    /// Unknown is intentionally not an eligible target; it records an unresolved target contract.
    /// InfectablePart represents a hypothetical infection on a part that could sustain an infectable wound.
    /// </summary>
    public enum MedicalConditionTargetKind : byte
    {
        Unknown,
        WholeBody,
        SpecificPart,
        AnyLivePart,
        InfectablePart
    }

    /// <summary>
    /// One target alternative for a condition, retaining its source and any resolved body-part constraint.
    /// </summary>
    public readonly struct MedicalConditionTarget
    {
        public readonly MedicalConditionTargetKind Kind;
        public readonly BodyPartDef Part;
        public readonly MedicalConditionSource Source;
        public readonly ThingDef Species;
        public readonly HediffGiverSetDef GiverSet;
        public readonly IncidentDef Incident;

        /// <summary>
        /// Initializes a new instance of the <see cref="MedicalConditionTarget"/> struct.
        /// </summary>
        /// <param name="kind">The kind of target.</param>
        /// <param name="part">The body part associated with the target, if any.</param>
        /// <param name="source">The source of the medical condition.</param>
        /// <param name="species">The species associated with the target, if any.</param>
        /// <param name="giverSet">The hediff giver set associated with the target, if any.</param>
        /// <param name="incident">The incident associated with the target, if any.</param>
        public MedicalConditionTarget(
            MedicalConditionTargetKind kind,
            BodyPartDef part,
            MedicalConditionSource source,
            ThingDef species = null,
            HediffGiverSetDef giverSet = null,
            IncidentDef incident = null)
        {
            Kind = kind;
            Part = part;
            Source = source;
            Species = species;
            GiverSet = giverSet;
            Incident = incident;
        }
    }

    /// <summary>
    /// Immutable condition metadata for catalog queries. Target records are source alternatives,
    /// not pawn-state eligibility results; the caller must filter part targets
    /// constraints against the selected pawn's actual body, and must match non-null Species
    /// against the pawn's race. A non-null GiverSet retains the exact race/giver provenance;
    /// a non-null Incident identifies disease provenance for any further incident eligibility checks.
    /// </summary>
    public sealed class MedicalConditionEntry
    {
        public HediffDef Hediff { get; private set; }
        public string Label { get; private set; }
        /// <summary>Presentation-group membership; medical conditions may have both target-scope flags.</summary>
        public MedicalConditionCategory Categories { get; private set; }
        public MedicalConditionSource Sources { get; private set; }
        public bool HasSystemicStageImpact { get; private set; }
        public bool HasPartLocalImpact { get; private set; }
        public bool IsSolverRelevant { get; private set; }
        public bool IsMedicallyCataloged { get; private set; }
        public IReadOnlyList<MedicalConditionTarget> Targets { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MedicalConditionEntry"/> class.
        /// </summary>
        /// <param name="hediff">The medical condition definition.</param>
        /// <param name="label">The display label for the condition.</param>
        /// <param name="categories">The presentation-group membership categories.</param>
        /// <param name="sources">The sources of the medical condition.</param>
        /// <param name="hasSystemicStageImpact">Indicates if the condition impacts systemic stages.</param>
        /// <param name="hasPartLocalImpact">Indicates if the condition impacts local body parts.</param>
        /// <param name="targets">The list of targets associated with the condition.</param>
        /// <param name="isSolverRelevant">Indicates if the condition is relevant for the solver.</param>
        /// <param name="isMedicallyCataloged">Indicates if the condition is medically cataloged.</param>
        internal MedicalConditionEntry(
            HediffDef hediff,
            string label,
            MedicalConditionCategory categories,
            MedicalConditionSource sources,
            bool hasSystemicStageImpact,
            bool hasPartLocalImpact,
            List<MedicalConditionTarget> targets,
            bool isSolverRelevant = false,
            bool isMedicallyCataloged = false)
        {
            Hediff = hediff;
            Label = label;
            Categories = categories;
            Sources = sources;
            HasSystemicStageImpact = hasSystemicStageImpact;
            HasPartLocalImpact = hasPartLocalImpact;
            IsSolverRelevant = isSolverRelevant;
            IsMedicallyCataloged = isMedicallyCataloged;
            Targets = new ReadOnlyCollection<MedicalConditionTarget>(targets);
        }
    }

    #endregion

}

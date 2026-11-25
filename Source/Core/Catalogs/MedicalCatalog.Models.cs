using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [CORE-CAT-01] RECIPE CATALOG ENTRY MODELS

    /// <summary>
    /// [CORE-CAT-01] Immutable descriptor representing an indexed surgery recipe or medical procedure.
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

    #endregion

    #region 2. [CORE-CAT-03] CONDITION METADATA MODELS

    /// <summary>
    /// [CORE-CAT-03] Presentation groups for an impactful condition definition.
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
        PsychicAndAbilities = 1 << 4,
        Other = 1 << 5
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
        Ability = 1 << 5,
        RandomAbility = 1 << 6,
        Other = 1 << 7,
        DiseaseIncident = 1 << 8,
        IncidentPawnHediff = 1 << 9,
        WoundInfection = 1 << 10
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
    /// Immutable condition metadata for UI enumeration. Target records are source alternatives,
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
        public IReadOnlyList<MedicalConditionTarget> Targets { get; private set; }

        internal MedicalConditionEntry(
            HediffDef hediff,
            string label,
            MedicalConditionCategory categories,
            MedicalConditionSource sources,
            bool hasSystemicStageImpact,
            bool hasPartLocalImpact,
            List<MedicalConditionTarget> targets)
        {
            Hediff = hediff;
            Label = label;
            Categories = categories;
            Sources = sources;
            HasSystemicStageImpact = hasSystemicStageImpact;
            HasPartLocalImpact = hasPartLocalImpact;
            Targets = new ReadOnlyCollection<MedicalConditionTarget>(targets);
        }
    }

    #endregion

}

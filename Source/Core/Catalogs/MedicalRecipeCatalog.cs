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

    #region 1A. [CORE-CAT-03] CONDITION METADATA MODELS

    /// <summary>
    /// [CORE-CAT-03] Stable presentation grouping for an impactful condition definition.
    /// </summary>
    public enum MedicalConditionCategory : byte
    {
        Ingestibles,
        MedicalPhysiological,
        PsychicAndAbilities,
        Other
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
        IncidentPawnHediff = 1 << 9
    }

    /// <summary>
    /// Describes a target possibility proven by the source definition metadata.
    /// Unknown is intentionally not an eligible target; it records an unresolved target contract.
    /// </summary>
    public enum MedicalConditionTargetKind : byte
    {
        Unknown,
        WholeBody,
        SpecificPart,
        AnyLivePart
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
            MedicalConditionSource source)
            : this(kind, part, source, null, null, null)
        {
        }

        public MedicalConditionTarget(
            MedicalConditionTargetKind kind,
            BodyPartDef part,
            MedicalConditionSource source,
            ThingDef species,
            HediffGiverSetDef giverSet,
            IncidentDef incident)
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
    /// not pawn-state eligibility results; the caller must filter specific and any-live-part
    /// constraints against the selected pawn's actual body, and must match non-null Species
    /// against the pawn's race. A non-null GiverSet retains the exact race/giver provenance;
    /// a non-null Incident identifies disease provenance for any further incident eligibility checks.
    /// </summary>
    public sealed class MedicalConditionEntry
    {
        public HediffDef Hediff { get; private set; }
        public string Label { get; private set; }
        public MedicalConditionCategory Category { get; private set; }
        public MedicalConditionSource Sources { get; private set; }
        public bool HasSystemicStageImpact { get; private set; }
        public bool HasPartLocalImpact { get; private set; }
        public IReadOnlyList<MedicalConditionTarget> Targets { get; private set; }

        internal MedicalConditionEntry(
            HediffDef hediff,
            string label,
            MedicalConditionCategory category,
            MedicalConditionSource sources,
            bool hasSystemicStageImpact,
            bool hasPartLocalImpact,
            List<MedicalConditionTarget> targets)
        {
            Hediff = hediff;
            Label = label;
            Category = category;
            Sources = sources;
            HasSystemicStageImpact = hasSystemicStageImpact;
            HasPartLocalImpact = hasPartLocalImpact;
            Targets = new ReadOnlyCollection<MedicalConditionTarget>(targets);
        }
    }

    #endregion

    #endregion

    /// <summary>
    /// [CORE-CAT-01] STATIC MEDICAL RECIPE & AUGMENTATION CATALOG
    /// Pre-indexes surgeries and implants, plus categorized condition definitions with source-derived target alternatives.
    /// Provides indexed query tables for the interactive medical planner and diagnostic census tools.
    /// Resides under Source/Core/Catalogs/.
    /// </summary>
    public static class MedicalRecipeCatalog
    {
        #region 2. STORAGE & LOOKUP TABLES

        private static readonly Dictionary<BodyPartDef, List<MedicalOperationEntry>> replacementRecipesByPart = 
            new Dictionary<BodyPartDef, List<MedicalOperationEntry>>(128);

        private static readonly Dictionary<BodyPartDef, List<MedicalOperationEntry>> implantRecipesByPart = 
            new Dictionary<BodyPartDef, List<MedicalOperationEntry>>(128);

        private static readonly List<HediffDef> systemicDrugsAndStimulants = 
            new List<HediffDef>(64);

        private static ReadOnlyCollection<MedicalConditionEntry> conditionEntries =
            new ReadOnlyCollection<MedicalConditionEntry>(new List<MedicalConditionEntry>());

        private static readonly ReadOnlyCollection<MedicalConditionEntry> emptyConditionEntries =
            new ReadOnlyCollection<MedicalConditionEntry>(new List<MedicalConditionEntry>());

        private static readonly Dictionary<MedicalConditionCategory, ReadOnlyCollection<MedicalConditionEntry>>
            conditionEntriesByCategory =
                new Dictionary<MedicalConditionCategory, ReadOnlyCollection<MedicalConditionEntry>>();

        private static Dictionary<HediffDef, MedicalConditionEntry> conditionEntriesByHediff =
            new Dictionary<HediffDef, MedicalConditionEntry>();

        private static readonly ReadOnlyCollection<MedicalConditionTarget> emptyConditionTargets =
            new ReadOnlyCollection<MedicalConditionTarget>(new List<MedicalConditionTarget>());

        private static readonly ReadOnlyCollection<MedicalConditionCategory> conditionCategories =
            new ReadOnlyCollection<MedicalConditionCategory>(new List<MedicalConditionCategory>
            {
                MedicalConditionCategory.Ingestibles,
                MedicalConditionCategory.MedicalPhysiological,
                MedicalConditionCategory.PsychicAndAbilities,
                MedicalConditionCategory.Other
            });

        private static bool isInitialized = false;
        private static readonly object catalogLock = new object();

        private sealed class MedicalConditionBuilder
        {
            public readonly HediffDef Hediff;
            public readonly List<MedicalConditionTarget> Targets = new List<MedicalConditionTarget>(2);
            public MedicalConditionSource Sources;
            public bool HasSystemicStageImpact;
            public bool HasPartLocalImpact;
            public bool IsMedical;

            public MedicalConditionBuilder(HediffDef hediff)
            {
                Hediff = hediff;
            }
        }

        #endregion

        #region 3. CATALOG BOOTSTRAPPING & PRE-INDEXING

        /// <summary>
        /// [CORE-CAT-01] Ensures the medical surgery and condition catalog has been indexed.
        /// Thread-safe, idempotent on-demand initializer.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (!isInitialized)
            {
                InitializeCatalog();
            }
        }

        /// <summary>
        /// [CORE-CAT-01] Scans DefDatabase and builds surgery indexes plus the canonical condition catalog.
        /// Invoked on startup or first query to ensure all modded defs are resident.
        /// </summary>
        public static void InitializeCatalog()
        {
            lock (catalogLock)
            {
                if (isInitialized) return;

                var watch = System.Diagnostics.Stopwatch.StartNew();

                replacementRecipesByPart.Clear();
                implantRecipesByPart.Clear();

                int totalReplacementRecipes = 0;
                int totalImplantRecipes = 0;
                HashSet<RecipeDef> countedReplacements = new HashSet<RecipeDef>();
                HashSet<RecipeDef> countedImplants = new HashSet<RecipeDef>();

                // =========================================================================
                // TIER 1: INDEX LOCALIZED SURGERY RECIPES & BLACKLIST SURGICAL HEDIFFS
                // =========================================================================
                HashSet<HediffDef> surgicalHediffs = new HashSet<HediffDef>(256);

                List<RecipeDef> allRecipes = DefDatabase<RecipeDef>.AllDefsListForReading;
                if (allRecipes != null)
                {
                    for (int i = 0; i < allRecipes.Count; i++)
                    {
                        RecipeDef recipe = allRecipes[i];
                        if (recipe.addsHediff == null || recipe.appliedOnFixedBodyParts == null || recipe.appliedOnFixedBodyParts.Count == 0)
                        {
                            continue;
                        }

                        HediffDef hediff = recipe.addsHediff;
                        surgicalHediffs.Add(hediff);

                        bool isReplacement = typeof(Hediff_AddedPart).IsAssignableFrom(hediff.hediffClass);
                        bool isImplant = typeof(Hediff_Implant).IsAssignableFrom(hediff.hediffClass) 
                                      || typeof(HediffWithComps).IsAssignableFrom(hediff.hediffClass);

                        if (!isReplacement && !isImplant) continue;

                        float efficiency = hediff.addedPartProps?.partEfficiency ?? 1.0f;
                        ImplantCharacteristics characteristics = MedicalClassifier.EvaluateHediffCapacities(hediff);

                        string label = hediff.LabelCap.ToString();
                        if (string.IsNullOrEmpty(label)) label = recipe.LabelCap.ToString();

                        MedicalOperationEntry entry = new MedicalOperationEntry(
                            recipe, 
                            hediff, 
                            label, 
                            efficiency, 
                            isReplacement, 
                            characteristics.AffectsAthletics
                        );

                        for (int p = 0; p < recipe.appliedOnFixedBodyParts.Count; p++)
                        {
                            BodyPartDef partDef = recipe.appliedOnFixedBodyParts[p];
                            if (partDef == null) continue;

                            if (isReplacement)
                            {
                                if (!replacementRecipesByPart.TryGetValue(partDef, out var list))
                                {
                                    list = new List<MedicalOperationEntry>(8);
                                    replacementRecipesByPart[partDef] = list;
                                }
                                if (!list.Exists(e => e.Recipe == recipe)) list.Add(entry);
                                if (countedReplacements.Add(recipe)) totalReplacementRecipes++;
                            }
                            else
                            {
                                if (!implantRecipesByPart.TryGetValue(partDef, out var list))
                                {
                                    list = new List<MedicalOperationEntry>(8);
                                    implantRecipesByPart[partDef] = list;
                                }
                                if (!list.Exists(e => e.Recipe == recipe)) list.Add(entry);
                                if (countedImplants.Add(recipe)) totalImplantRecipes++;
                            }
                        }
                    }
                }

                // =========================================================================
                // TIER 2: IDENTIFY CHEMICAL TOLERANCES (EXCLUDE PASSIVE BUILDUP COUNTERS)
                // =========================================================================
                HashSet<HediffDef> chemicalTolerances = new HashSet<HediffDef>(64);
                List<ChemicalDef> allChemicals = DefDatabase<ChemicalDef>.AllDefsListForReading;
                if (allChemicals != null)
                {
                    for (int i = 0; i < allChemicals.Count; i++)
                    {
                        ChemicalDef chem = allChemicals[i];
                        if (chem.toleranceHediff != null) chemicalTolerances.Add(chem.toleranceHediff);
                    }
                }

                BuildConditionCatalog(surgicalHediffs, chemicalTolerances);

                watch.Stop();
                isInitialized = true;

                CoreLogEvents.MedicalCatalogInitialized(
                    (float)watch.Elapsed.TotalMilliseconds,
                    totalReplacementRecipes,
                    totalImplantRecipes,
                    conditionEntries.Count
                );
            }
        }

        /// <summary>
        /// [CORE-CAT-03] Builds one condition entry per relevant HediffDef and merges all known grant-source target alternatives.
        /// This is a definition-only scan; it does not inspect maps, pawns, or runtime hediff state.
        /// </summary>
        private static void BuildConditionCatalog(
            HashSet<HediffDef> surgicalHediffs,
            HashSet<HediffDef> chemicalTolerances)
        {
            Dictionary<HediffDef, MedicalConditionBuilder> builders =
                new Dictionary<HediffDef, MedicalConditionBuilder>(128);
            List<HediffDef> allHediffs = DefDatabase<HediffDef>.AllDefsListForReading;

            if (allHediffs != null)
            {
                for (int i = 0; i < allHediffs.Count; i++)
                {
                    HediffDef hediff = allHediffs[i];
                    if (IsExcludedCondition(hediff, surgicalHediffs, chemicalTolerances)) continue;

                    bool hasSystemicStageImpact;
                    bool hasPartLocalImpact;
                    bool isPregnancy;
                    if (!TryGetConditionImpact(hediff, out hasSystemicStageImpact, out hasPartLocalImpact, out isPregnancy))
                    {
                        continue;
                    }

                    MedicalConditionBuilder builder = new MedicalConditionBuilder(hediff);
                    builder.Sources = MedicalConditionSource.ConditionDefinition;
                    builder.HasSystemicStageImpact = hasSystemicStageImpact;
                    builder.HasPartLocalImpact = hasPartLocalImpact;
                    builder.IsMedical = isPregnancy ||
                        hediff.isInfection ||
                        hediff.chronic ||
                        hediff.tendable ||
                        hediff.pregnant ||
                        hediff == HediffDefOf.Anesthetic;

                    if (isPregnancy)
                    {
                        AddConditionTarget(builder, MedicalConditionTargetKind.WholeBody, null, MedicalConditionSource.Other);
                        builder.Sources |= MedicalConditionSource.Other;
                    }

                    builders.Add(hediff, builder);
                }
            }

            // BREAKPOINT ANCHOR: Definition-Only Ingestible And Species Target Discovery
            List<ThingDef> allThings = DefDatabase<ThingDef>.AllDefsListForReading;
            if (allThings != null)
            {
                for (int i = 0; i < allThings.Count; i++)
                {
                    ThingDef thing = allThings[i];
                    var outcomeDoers = thing.ingestible?.outcomeDoers;
                    if (outcomeDoers != null)
                    {
                        for (int d = 0; d < outcomeDoers.Count; d++)
                        {
                            IngestionOutcomeDoer_GiveHediff giveHediff =
                                outcomeDoers[d] as IngestionOutcomeDoer_GiveHediff;
                            if (giveHediff == null || giveHediff.hediffDef == null) continue;

                            MedicalConditionBuilder builder;
                            if (!builders.TryGetValue(giveHediff.hediffDef, out builder)) continue;

                            builder.Sources |= MedicalConditionSource.Ingestible;
                            AddConditionTarget(
                                builder,
                                MedicalConditionTargetKind.WholeBody,
                                null,
                                MedicalConditionSource.Ingestible);
                        }
                    }

                    List<HediffGiverSetDef> giverSets = thing.race?.hediffGiverSets;
                    if (giverSets == null) continue;

                    for (int s = 0; s < giverSets.Count; s++)
                    {
                        HediffGiverSetDef giverSet = giverSets[s];
                        AddHediffGiverTargets(
                            builders,
                            giverSet?.hediffGivers,
                            MedicalConditionSource.SpeciesHediffGiver,
                            thing,
                            giverSet);
                    }
                }
            }

            if (allHediffs != null)
            {
                for (int i = 0; i < allHediffs.Count; i++)
                {
                    HediffDef sourceDef = allHediffs[i];
                    AddHediffGiverTargets(
                        builders,
                        sourceDef.hediffGivers,
                        MedicalConditionSource.HediffDefinitionGiver);

                    if (sourceDef.stages == null) continue;
                    for (int s = 0; s < sourceDef.stages.Count; s++)
                    {
                        HediffStage stage = sourceDef.stages[s];
                        AddHediffGiverTargets(
                            builders,
                            stage?.hediffGivers,
                            MedicalConditionSource.HediffStageGiver);
                    }
                }
            }

            // BREAKPOINT ANCHOR: Explicit Incident Disease Provenance
            List<IncidentDef> allIncidents = DefDatabase<IncidentDef>.AllDefsListForReading;
            if (allIncidents != null)
            {
                for (int i = 0; i < allIncidents.Count; i++)
                {
                    IncidentDef incident = allIncidents[i];
                    if (incident == null) continue;

                    MedicalConditionBuilder diseaseBuilder;
                    if (incident.diseaseIncident != null &&
                        builders.TryGetValue(incident.diseaseIncident, out diseaseBuilder))
                    {
                        diseaseBuilder.Sources |= MedicalConditionSource.DiseaseIncident;
                        diseaseBuilder.IsMedical = true;

                        List<BodyPartDef> diseaseParts = incident.diseasePartsToAffect;
                        if (diseaseParts == null || diseaseParts.Count == 0)
                        {
                            AddConditionTarget(
                                diseaseBuilder,
                                MedicalConditionTargetKind.WholeBody,
                                null,
                                MedicalConditionSource.DiseaseIncident,
                                null,
                                null,
                                incident);
                        }
                        else
                        {
                            bool foundPart = false;
                            for (int p = 0; p < diseaseParts.Count; p++)
                            {
                                BodyPartDef part = diseaseParts[p];
                                if (part == null) continue;

                                AddConditionTarget(
                                    diseaseBuilder,
                                    MedicalConditionTargetKind.SpecificPart,
                                    part,
                                    MedicalConditionSource.DiseaseIncident,
                                    null,
                                    null,
                                    incident);
                                foundPart = true;
                            }

                            if (!foundPart)
                            {
                                AddConditionTarget(
                                    diseaseBuilder,
                                    MedicalConditionTargetKind.Unknown,
                                    null,
                                    MedicalConditionSource.DiseaseIncident,
                                    null,
                                    null,
                                    incident);
                            }
                        }
                    }

                    MedicalConditionBuilder pawnHediffBuilder;
                    if (incident.pawnHediff != null &&
                        builders.TryGetValue(incident.pawnHediff, out pawnHediffBuilder))
                    {
                        pawnHediffBuilder.Sources |= MedicalConditionSource.IncidentPawnHediff;
                        AddConditionTarget(
                            pawnHediffBuilder,
                            MedicalConditionTargetKind.Unknown,
                            null,
                            MedicalConditionSource.IncidentPawnHediff,
                            null,
                            null,
                            incident);
                    }
                }
            }

            // BREAKPOINT ANCHOR: Ability Target Constraint Resolution
            List<AbilityDef> allAbilities = DefDatabase<AbilityDef>.AllDefsListForReading;
            if (allAbilities != null)
            {
                List<BodyPartDef> consciousnessSourceParts = GetConsciousnessSourceParts();
                for (int i = 0; i < allAbilities.Count; i++)
                {
                    AbilityDef ability = allAbilities[i];
                    if (ability.comps == null) continue;

                    for (int c = 0; c < ability.comps.Count; c++)
                    {
                        CompProperties_AbilityGiveHediff giveHediff =
                            ability.comps[c] as CompProperties_AbilityGiveHediff;
                        if (giveHediff != null)
                        {
                            MedicalConditionBuilder builder;
                            if (!builders.TryGetValue(giveHediff.hediffDef, out builder)) continue;

                            builder.Sources |= MedicalConditionSource.Ability;
                            if (giveHediff.onlyBrain)
                            {
                                if (consciousnessSourceParts.Count == 0)
                                {
                                    AddConditionTarget(
                                        builder,
                                        MedicalConditionTargetKind.Unknown,
                                        null,
                                        MedicalConditionSource.Ability);
                                }
                                else
                                {
                                    for (int p = 0; p < consciousnessSourceParts.Count; p++)
                                    {
                                        AddConditionTarget(
                                            builder,
                                            MedicalConditionTargetKind.SpecificPart,
                                            consciousnessSourceParts[p],
                                            MedicalConditionSource.Ability);
                                    }
                                }
                            }
                            else
                            {
                                AddConditionTarget(
                                    builder,
                                    MedicalConditionTargetKind.WholeBody,
                                    null,
                                    MedicalConditionSource.Ability);
                            }

                            continue;
                        }

                        CompProperties_AbilityGiveRandomHediff randomGiveHediff =
                            ability.comps[c] as CompProperties_AbilityGiveRandomHediff;
                        if (randomGiveHediff?.options == null) continue;

                        for (int o = 0; o < randomGiveHediff.options.Count; o++)
                        {
                            HediffOption option = randomGiveHediff.options[o];
                            if (option?.hediffDef == null) continue;

                            MedicalConditionBuilder builder;
                            if (!builders.TryGetValue(option.hediffDef, out builder)) continue;

                            builder.Sources |= MedicalConditionSource.RandomAbility;
                            AddConditionTarget(
                                builder,
                                option.bodyPart == null
                                    ? MedicalConditionTargetKind.Unknown
                                    : MedicalConditionTargetKind.SpecificPart,
                                option.bodyPart,
                                MedicalConditionSource.RandomAbility);
                        }
                    }
                }
            }

            List<MedicalConditionEntry> entries = new List<MedicalConditionEntry>(builders.Count);
            Dictionary<HediffDef, MedicalConditionEntry> entriesByHediff =
                new Dictionary<HediffDef, MedicalConditionEntry>(builders.Count);
            List<MedicalConditionEntry> ingestibles = new List<MedicalConditionEntry>();
            List<MedicalConditionEntry> medical = new List<MedicalConditionEntry>();
            List<MedicalConditionEntry> psychic = new List<MedicalConditionEntry>();
            List<MedicalConditionEntry> other = new List<MedicalConditionEntry>();

            foreach (KeyValuePair<HediffDef, MedicalConditionBuilder> pair in builders)
            {
                MedicalConditionBuilder builder = pair.Value;
                if (builder.Targets.Count == 0)
                {
                    AddConditionTarget(
                        builder,
                        MedicalConditionTargetKind.Unknown,
                        null,
                        MedicalConditionSource.ConditionDefinition);
                }

                MedicalConditionCategory category = GetConditionCategory(builder);
                string label = builder.Hediff.LabelCap.ToString();
                MedicalConditionEntry entry = new MedicalConditionEntry(
                    builder.Hediff,
                    label,
                    category,
                    builder.Sources,
                    builder.HasSystemicStageImpact,
                    builder.HasPartLocalImpact,
                    builder.Targets);

                entries.Add(entry);
                entriesByHediff.Add(builder.Hediff, entry);

                if (category == MedicalConditionCategory.Ingestibles) ingestibles.Add(entry);
                else if (category == MedicalConditionCategory.MedicalPhysiological) medical.Add(entry);
                else if (category == MedicalConditionCategory.PsychicAndAbilities) psychic.Add(entry);
                else other.Add(entry);
            }

            Comparison<MedicalConditionEntry> entryComparer = CompareConditionEntries;
            entries.Sort(entryComparer);
            ingestibles.Sort(entryComparer);
            medical.Sort(entryComparer);
            psychic.Sort(entryComparer);
            other.Sort(entryComparer);

            conditionEntries = new ReadOnlyCollection<MedicalConditionEntry>(entries);
            conditionEntriesByHediff = entriesByHediff;
            conditionEntriesByCategory.Clear();
            conditionEntriesByCategory.Add(
                MedicalConditionCategory.Ingestibles,
                new ReadOnlyCollection<MedicalConditionEntry>(ingestibles));
            conditionEntriesByCategory.Add(
                MedicalConditionCategory.MedicalPhysiological,
                new ReadOnlyCollection<MedicalConditionEntry>(medical));
            conditionEntriesByCategory.Add(
                MedicalConditionCategory.PsychicAndAbilities,
                new ReadOnlyCollection<MedicalConditionEntry>(psychic));
            conditionEntriesByCategory.Add(
                MedicalConditionCategory.Other,
                new ReadOnlyCollection<MedicalConditionEntry>(other));

            // Legacy API projection: retain the existing mutable List<HediffDef> surface without a second scan.
            systemicDrugsAndStimulants.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                systemicDrugsAndStimulants.Add(entries[i].Hediff);
            }
            systemicDrugsAndStimulants.Sort(
                (first, second) => string.Compare(
                    first.LabelCap.ToString(),
                    second.LabelCap.ToString(),
                    StringComparison.OrdinalIgnoreCase));
        }

        private static List<BodyPartDef> GetConsciousnessSourceParts()
        {
            List<BodyPartDef> parts = new List<BodyPartDef>();
            BodyPartTagDef sourceTag = TopologyLayoutCompiler.ConsciousnessSourceTag;
            List<BodyPartDef> allParts = DefDatabase<BodyPartDef>.AllDefsListForReading;
            if (sourceTag == null || allParts == null) return parts;

            for (int i = 0; i < allParts.Count; i++)
            {
                BodyPartDef part = allParts[i];
                if (part?.tags != null && part.tags.Contains(sourceTag))
                {
                    parts.Add(part);
                }
            }

            return parts;
        }

        private static bool IsExcludedCondition(
            HediffDef hediff,
            HashSet<HediffDef> surgicalHediffs,
            HashSet<HediffDef> chemicalTolerances)
        {
            if (hediff == null || hediff.hediffClass == null) return true;
            if (surgicalHediffs.Contains(hediff) || chemicalTolerances.Contains(hediff)) return true;
            if (hediff.CompProps<HediffCompProperties_DrugEffectFactor>() != null) return true;

            Type hediffType = hediff.hediffClass;
            return typeof(Hediff_AddedPart).IsAssignableFrom(hediffType) ||
                   typeof(Hediff_Implant).IsAssignableFrom(hediffType) ||
                   typeof(Hediff_Injury).IsAssignableFrom(hediffType) ||
                   typeof(Hediff_MissingPart).IsAssignableFrom(hediffType);
        }

        private static bool TryGetConditionImpact(
            HediffDef hediff,
            out bool hasSystemicStageImpact,
            out bool hasPartLocalImpact,
            out bool isPregnancy)
        {
            hasSystemicStageImpact = false;
            hasPartLocalImpact = false;
            isPregnancy = typeof(Hediff_Pregnant).IsAssignableFrom(hediff.hediffClass);

            if (hediff.stages != null)
            {
                // BREAKPOINT ANCHOR: Test-Bench-Reproducible Stage Effects
                // Arbitrary StatDef offsets/factors are omitted: the condition simulation consumes body-part
                // efficiency and capacity snapshots, not general stat evaluation. A direct mass-capacity-stat
                // modifier remains unsupported until the Test Bench models that stat path.
                for (int s = 0; s < hediff.stages.Count; s++)
                {
                    HediffStage stage = hediff.stages[s];
                    if (stage == null) continue;

                    if (Math.Abs(stage.painOffset) >= PawnDataRegistry.EfficiencyEpsilon ||
                        Math.Abs(stage.painFactor - 1f) >= PawnDataRegistry.EfficiencyEpsilon ||
                        HasRelevantCapacityModifier(stage.capMods))
                    {
                        hasSystemicStageImpact = true;
                    }

                    if (Math.Abs(stage.partEfficiencyOffset) >= PawnDataRegistry.EfficiencyEpsilon)
                    {
                        hasPartLocalImpact = true;
                    }
                }
            }

            if (hediff == HediffDefOf.Anesthetic)
            {
                hasSystemicStageImpact = true;
            }

            return hasSystemicStageImpact || hasPartLocalImpact || isPregnancy;
        }

        private static bool HasRelevantCapacityModifier(List<PawnCapacityModifier> modifiers)
        {
            if (modifiers == null) return false;

            for (int i = 0; i < modifiers.Count; i++)
            {
                PawnCapacityModifier modifier = modifiers[i];
                if (modifier == null || !IsRelevantConditionCapacity(modifier.capacity)) continue;

                if (Math.Abs(modifier.offset) >= PawnDataRegistry.EfficiencyEpsilon ||
                    Math.Abs(modifier.postFactor - 1f) >= PawnDataRegistry.EfficiencyEpsilon ||
                    Math.Abs(modifier.setMax - 1f) >= PawnDataRegistry.EfficiencyEpsilon)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRelevantConditionCapacity(PawnCapacityDef capacity)
        {
            return MedicalClassifier.IsAthleticCapacity(capacity) ||
                   capacity == PawnCapacityDefOf.BloodFiltration;
        }

        private static void AddHediffGiverTargets(
            Dictionary<HediffDef, MedicalConditionBuilder> builders,
            List<HediffGiver> givers,
            MedicalConditionSource source,
            ThingDef species = null,
            HediffGiverSetDef giverSet = null)
        {
            if (givers == null) return;

            for (int i = 0; i < givers.Count; i++)
            {
                HediffGiver giver = givers[i];
                if (giver?.hediff == null) continue;

                MedicalConditionBuilder builder;
                if (!builders.TryGetValue(giver.hediff, out builder)) continue;

                builder.Sources |= source;
                if (source == MedicalConditionSource.SpeciesHediffGiver ||
                    source == MedicalConditionSource.HediffDefinitionGiver ||
                    source == MedicalConditionSource.HediffStageGiver)
                {
                    builder.IsMedical = true;
                }

                bool hasSpecifiedParts = giver.partsToAffect != null && giver.partsToAffect.Count > 0;
                if (hasSpecifiedParts)
                {
                    for (int p = 0; p < giver.partsToAffect.Count; p++)
                    {
                        BodyPartDef part = giver.partsToAffect[p];
                        if (part != null)
                        {
                            AddConditionTarget(
                                builder,
                                MedicalConditionTargetKind.SpecificPart,
                                part,
                                source,
                                species,
                                giverSet);
                        }
                    }
                }

                if (giver.canAffectAnyLivePart)
                {
                    AddConditionTarget(
                        builder,
                        MedicalConditionTargetKind.AnyLivePart,
                        null,
                        source,
                        species,
                        giverSet);
                }
                else if (!hasSpecifiedParts)
                {
                    AddConditionTarget(
                        builder,
                        builder.HasPartLocalImpact || builder.Hediff.isInfection
                            ? MedicalConditionTargetKind.Unknown
                            : MedicalConditionTargetKind.WholeBody,
                        null,
                        source,
                        species,
                        giverSet);
                }
            }
        }

        private static void AddConditionTarget(
            MedicalConditionBuilder builder,
            MedicalConditionTargetKind kind,
            BodyPartDef part,
            MedicalConditionSource source,
            ThingDef species = null,
            HediffGiverSetDef giverSet = null,
            IncidentDef incident = null)
        {
            for (int i = 0; i < builder.Targets.Count; i++)
            {
                MedicalConditionTarget existing = builder.Targets[i];
                if (existing.Kind == kind &&
                    existing.Part == part &&
                    existing.Source == source &&
                    existing.Species == species &&
                    existing.GiverSet == giverSet &&
                    existing.Incident == incident)
                {
                    return;
                }
            }

            builder.Targets.Add(new MedicalConditionTarget(kind, part, source, species, giverSet, incident));
        }

        private static MedicalConditionCategory GetConditionCategory(MedicalConditionBuilder builder)
        {
            if ((builder.Sources & MedicalConditionSource.Ingestible) != 0)
            {
                return MedicalConditionCategory.Ingestibles;
            }

            if ((builder.Sources & (MedicalConditionSource.Ability | MedicalConditionSource.RandomAbility)) != 0)
            {
                return MedicalConditionCategory.PsychicAndAbilities;
            }

            return builder.IsMedical
                ? MedicalConditionCategory.MedicalPhysiological
                : MedicalConditionCategory.Other;
        }

        private static int CompareConditionEntries(MedicalConditionEntry first, MedicalConditionEntry second)
        {
            int categoryComparison = first.Category.CompareTo(second.Category);
            if (categoryComparison != 0) return categoryComparison;

            return string.Compare(first.Label, second.Label, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// [CORE-CAT-01] Forces a complete wipe and re-index of surgery recipes, implants, and condition metadata.
        /// Flushes internal lookup tables to enable real-time live developer recompilation without requiring game restarts.
        /// </summary>
        public static void ClearAndReinitialize()
        {
            lock (catalogLock)
            {
                // BREAKPOINT ANCHOR: Forced Catalog Re-indexing Gate
                isInitialized = false;
                InitializeCatalog();
            }
        }

        #endregion

        #region 4. STAGE RESOLVER & MATHEMATICAL LETHALITY CLAMPING

        /// <summary>
        /// Calculates the peak impact severity level for a staged condition.
        /// Caps finite lethal thresholds to 99%, skips zero-consciousness fatal stages, and uses
        /// a finite final-stage fallback when maxSeverity is the engine's unbounded sentinel.
        /// This does not guarantee survival when combined with other conditions.
        /// </summary>
        /// <param name="def">The target hediff definition.</param>
        /// <param name="stageSuffix">Outputs a human-readable stage suffix (e.g. "Extreme", "Major", "Withdrawal").</param>
        /// <returns>The initial target severity to assign to the simulated hediff.</returns>
        public static float GetPeakImpactSeverity(HediffDef def, out string stageSuffix)
        {
            stageSuffix = null;
            if (def == null) return 0.5f;

            // BREAKPOINT ANCHOR: Absolute Lethal Ceiling (99% of lethalSeverity)
            bool hasFiniteLethalSeverity = IsFinitePositiveSeverity(def.lethalSeverity);
            bool hasFiniteMaximumSeverity = IsFinitePositiveSeverity(def.maxSeverity);
            float safeLethalCeiling = hasFiniteLethalSeverity ? (def.lethalSeverity * 0.99f) : float.MaxValue;

            if (def.stages == null || def.stages.Count == 0)
            {
                float baseSeverity = IsFinitePositiveSeverity(def.initialSeverity) ? def.initialSeverity : 0.5f;
                return Math.Min(baseSeverity, safeLethalCeiling);
            }

            int bestStageIndex = -1;
            float maxImpactScore = 0f;

            for (int i = 0; i < def.stages.Count; i++)
            {
                HediffStage stage = def.stages[i];
                if (stage == null) continue;

                // 1. Guard against stages that cause immediate zero-consciousness death
                if (StageCausesImmediateZeroConsciousness(stage))
                {
                    continue;
                }

                // 2. Guard against stages starting at or above the lethal boundary
                if (hasFiniteLethalSeverity && stage.minSeverity >= def.lethalSeverity)
                {
                    continue;
                }

                float stageScore = 0f;

                if (stage.capMods != null)
                {
                    for (int c = 0; c < stage.capMods.Count; c++)
                    {
                        PawnCapacityModifier capMod = stage.capMods[c];
                        if (capMod != null &&
                            (MedicalClassifier.IsAthleticCapacity(capMod.capacity) ||
                             capMod.capacity == PawnCapacityDefOf.BloodFiltration))
                        {
                            stageScore += Math.Abs(capMod.offset);
                            if (capMod.postFactor != 1.0f) stageScore += Math.Abs(1.0f - capMod.postFactor);
                            if (capMod.setMax != 1.0f) stageScore += Math.Abs(1.0f - capMod.setMax) * 2f;
                        }
                    }
                }

                if (Math.Abs(stage.partEfficiencyOffset) >= PawnDataRegistry.EfficiencyEpsilon)
                {
                    stageScore += Math.Abs(stage.partEfficiencyOffset);
                }

                if (stage.painOffset > 0f) stageScore += stage.painOffset * 0.5f;
                if (stage.painFactor != 1.0f) stageScore += Math.Abs(1.0f - stage.painFactor) * 0.5f;

                if (stageScore > maxImpactScore)
                {
                    maxImpactScore = stageScore;
                    bestStageIndex = i;
                }
            }

            // If all stages were either zero impact or fatal, fall back to safe baseline
            if (bestStageIndex == -1)
            {
                float baseSeverity = IsFinitePositiveSeverity(def.initialSeverity) ? def.initialSeverity : 0.5f;
                return Math.Min(baseSeverity, safeLethalCeiling);
            }

            HediffStage bestStage = def.stages[bestStageIndex];
            if (!string.IsNullOrEmpty(bestStage.label))
            {
                stageSuffix = bestStage.label.CapitalizeFirst();
            }

            // Calculate target severity within the selected stage
            float targetSeverity;

            if (bestStageIndex + 1 < def.stages.Count)
            {
                // Stage has a defined upper boundary: place at 95% into the stage's range
                float nextMin = def.stages[bestStageIndex + 1].minSeverity;
                targetSeverity = bestStage.minSeverity + ((nextMin - bestStage.minSeverity) * 0.95f);
            }
            else
            {
                // Final stage: place at 99% of lethal severity or 95% of max severity
                if (hasFiniteLethalSeverity)
                {
                    targetSeverity = def.lethalSeverity * 0.99f;
                }
                else if (hasFiniteMaximumSeverity)
                {
                    targetSeverity = bestStage.minSeverity + ((def.maxSeverity - bestStage.minSeverity) * 0.95f);
                }
                else
                {
                    targetSeverity = GetUnboundedFinalStageSeverity(def, bestStageIndex);
                }
            }

            // Final safety clamp: never exceed 99% of lethal threshold
            targetSeverity = Math.Min(targetSeverity, safeLethalCeiling);

            if (hasFiniteMaximumSeverity)
            {
                targetSeverity = Math.Min(targetSeverity, def.maxSeverity);
            }

            if (float.IsNaN(targetSeverity) || float.IsInfinity(targetSeverity))
            {
                targetSeverity = bestStage.minSeverity;
            }

            return Math.Max(targetSeverity, bestStage.minSeverity);
        }

        private static bool IsFinitePositiveSeverity(float severity)
        {
            return severity > 0f && severity < float.MaxValue &&
                   !float.IsNaN(severity) && !float.IsInfinity(severity);
        }

        private static float GetUnboundedFinalStageSeverity(HediffDef def, int stageIndex)
        {
            HediffStage stage = def.stages[stageIndex];
            float increment = 0.1f;

            if (stageIndex > 0)
            {
                HediffStage previousStage = def.stages[stageIndex - 1];
                if (previousStage != null)
                {
                    float previousRange = stage.minSeverity - previousStage.minSeverity;
                    if (previousRange > 0f && !float.IsNaN(previousRange) && !float.IsInfinity(previousRange))
                    {
                        increment = Math.Min(previousRange, 1f);
                    }
                }
            }

            double target = (double)stage.minSeverity + ((double)increment * 0.95d);
            if (double.IsNaN(target) || double.IsInfinity(target) || target >= float.MaxValue)
            {
                return stage.minSeverity;
            }

            return (float)target;
        }

        /// <summary>
        /// Determines whether the specified HediffStage causes immediate zero consciousness.
        /// </summary>
        /// <param name="stage">The HediffStage to evaluate for immediate zero consciousness.</param>
        /// <returns>True if the stage causes immediate zero consciousness; otherwise, false.</returns>
        private static bool StageCausesImmediateZeroConsciousness(HediffStage stage)
        {
            if (stage?.capMods == null) return false;

            for (int c = 0; c < stage.capMods.Count; c++)
            {
                PawnCapacityModifier mod = stage.capMods[c];
                if (mod != null && mod.capacity == PawnCapacityDefOf.Consciousness)
                {
                    if (mod.setMax <= 0.001f || mod.offset <= -1.0f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a formatted display label for the condition dropdown, including stage indicators if impactful.
        /// </summary>
        /// <param name="def">The HediffDef for which to retrieve the formatted condition label.</param>
        /// <returns>A formatted string representing the condition label, including stage indicators if impactful.</returns>
        public static string GetFormattedConditionLabel(HediffDef def)
        {
            if (def == null) return string.Empty;

            GetPeakImpactSeverity(def, out string stageSuffix);
            string baseLabel = def.LabelCap.ToString();

            if (!string.IsNullOrEmpty(stageSuffix) && !baseLabel.ToLowerInvariant().Contains(stageSuffix.ToLowerInvariant()))
            {
                return $"{baseLabel} ({stageSuffix})";
            }

            return baseLabel;
        }

        /// <summary>
        /// [CORE-CAT-03] Returns the engine's label for an existing hediff, which reflects its actual current stage.
        /// Unlike the definition overload, this method does not predict a peak-impact stage.
        /// </summary>
        /// <param name="hediff">The live or simulated hediff to format.</param>
        /// <returns>The current engine label, or an empty string when the hediff is null.</returns>
        public static string GetFormattedConditionLabel(Hediff hediff)
        {
            if (hediff == null) return string.Empty;

            string actualLabel = hediff.LabelCap.ToString();
            return string.IsNullOrEmpty(actualLabel)
                ? GetFormattedConditionLabel(hediff.def)
                : actualLabel;
        }

        #endregion

        #region 5. PUBLIC O(1) QUERY API

        /// <summary>
        /// Returns true if any replacement or implant surgeries are available for this BodyPartDef.
        /// </summary>
        /// <param name="partDef">The BodyPartDef for which to check available operations.</param>
        /// <returns>True if any replacement or implant surgeries are available for the specified BodyPartDef; otherwise, false.</returns>
        public static bool HasAnyOperationsFor(BodyPartDef partDef)
        {
            if (partDef == null) return false;
            if (!isInitialized) InitializeCatalog();

            if (replacementRecipesByPart.TryGetValue(partDef, out var repls) && repls.Count > 0) return true;
            if (implantRecipesByPart.TryGetValue(partDef, out var imps) && imps.Count > 0) return true;

            return false;
        }

        /// <summary>
        /// Retrieves all indexed replacement (prosthetic/bionic) operations available for a specific BodyPartDef.
        /// </summary>
        /// <param name="partDef">The BodyPartDef for which to retrieve replacement operations.</param>
        /// <returns>A list of all indexed replacement operations for the specified BodyPartDef, or null if none are available.</returns>
        public static List<MedicalOperationEntry> GetReplacementsFor(BodyPartDef partDef)
        {
            if (!isInitialized) InitializeCatalog();
            if (partDef != null && replacementRecipesByPart.TryGetValue(partDef, out var list))
            {
                return list;
            }
            return null;
        }

        /// <summary>
        /// Retrieves all indexed localized implant operations available for a specific BodyPartDef.
        /// </summary>
        /// <param name="partDef">The BodyPartDef for which to retrieve implant operations.</param>
        /// <returns>A list of all indexed implant operations for the specified BodyPartDef, or null if none are available.</returns>
        public static List<MedicalOperationEntry> GetImplantsFor(BodyPartDef partDef)
        {
            if (!isInitialized) InitializeCatalog();
            if (partDef != null && implantRecipesByPart.TryGetValue(partDef, out var list))
            {
                return list;
            }
            return null;
        }

        /// <summary>
        /// Legacy mutable projection of condition definitions, retained for callers migrating to the categorized catalog API.
        /// </summary>
        /// <returns>The current condition definitions in alphabetical label order.</returns>
        public static List<HediffDef> GetSystemicDrugs()
        {
            if (!isInitialized) InitializeCatalog();
            return systemicDrugsAndStimulants;
        }

        #endregion

        #region 5A. [CORE-CAT-03] PUBLIC CONDITION QUERY API

        /// <summary>
        /// [CORE-CAT-03] Returns the stable category order used by the condition catalog.
        /// The returned collection is read-only and reused after initialization.
        /// </summary>
        public static IReadOnlyList<MedicalConditionCategory> GetConditionCategories()
        {
            EnsureInitialized();
            return conditionCategories;
        }

        /// <summary>
        /// [CORE-CAT-03] Enumerates all impactful condition definitions in category and label order.
        /// This definition catalog does not apply pawn-state eligibility filtering.
        /// </summary>
        public static IReadOnlyList<MedicalConditionEntry> GetConditionEntries()
        {
            EnsureInitialized();
            return conditionEntries;
        }

        /// <summary>
        /// [CORE-CAT-03] Enumerates the impactful condition definitions assigned to one category.
        /// </summary>
        public static IReadOnlyList<MedicalConditionEntry> GetConditionEntries(MedicalConditionCategory category)
        {
            EnsureInitialized();

            ReadOnlyCollection<MedicalConditionEntry> entries;
            return conditionEntriesByCategory.TryGetValue(category, out entries)
                ? entries
                : emptyConditionEntries;
        }

        /// <summary>
        /// [CORE-CAT-03] Retrieves a condition entry by definition without allocating a new catalog snapshot.
        /// </summary>
        public static bool TryGetConditionEntry(HediffDef hediff, out MedicalConditionEntry entry)
        {
            EnsureInitialized();
            if (hediff != null)
            {
                return conditionEntriesByHediff.TryGetValue(hediff, out entry);
            }

            entry = null;
            return false;
        }

        /// <summary>
        /// [CORE-CAT-03] Returns known target alternatives for a condition. Specific-part and any-live-part
        /// alternatives still require filtering against the selected pawn by the caller.
        /// Unknown records are informational and must not be treated as eligible targets.
        /// </summary>
        public static IReadOnlyList<MedicalConditionTarget> GetConditionTargetConstraints(HediffDef hediff)
        {
            MedicalConditionEntry entry;
            return TryGetConditionEntry(hediff, out entry) ? entry.Targets : emptyConditionTargets;
        }

        #endregion
    }
}
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
    /// not pawn-state eligibility results; the caller must filter specific and any-live-part
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

        /// <summary>Stores all medical condition categories.</summary>
        private static readonly ReadOnlyCollection<MedicalConditionCategory> conditionCategories =
            new ReadOnlyCollection<MedicalConditionCategory>(new List<MedicalConditionCategory>
            {
                MedicalConditionCategory.Ingestibles,
                MedicalConditionCategory.SystemicMedical,
                MedicalConditionCategory.LocalizedMedical,
                MedicalConditionCategory.MedicalTargetUnknown,
                MedicalConditionCategory.PsychicAndAbilities,
                MedicalConditionCategory.Other
            });

        private static readonly ReadOnlyCollection<MedicalConditionEntry> emptyConditionEntries =
            new ReadOnlyCollection<MedicalConditionEntry>(new List<MedicalConditionEntry>());

        private static readonly ReadOnlyCollection<MedicalConditionTarget> emptyConditionTargets =
            new ReadOnlyCollection<MedicalConditionTarget>(new List<MedicalConditionTarget>());

        /// <summary>
        /// The sole publication boundary; snapshots are complete before assignment and never mutated afterward.
        /// Null until the first complete build is ready.
        /// </summary>
        private static volatile CatalogSnapshot currentSnapshot;

        /// <summary>Serializes initial builds and explicit catalog refreshes.</summary>
        private static readonly object catalogLock = new object();

        /// <summary>[CORE-CAT-03] Represents an immutable published set of condition indexes.</summary>
        private sealed class ConditionCatalogSnapshot
        {
            public readonly ReadOnlyCollection<MedicalConditionEntry> AllEntries;
            public readonly ReadOnlyDictionary<MedicalConditionCategory, ReadOnlyCollection<MedicalConditionEntry>> EntriesByCategory;
            public readonly ReadOnlyDictionary<HediffDef, MedicalConditionEntry> EntriesByHediff;
            public readonly ReadOnlyCollection<HediffDef> SystemicDrugs;

            public ConditionCatalogSnapshot(
                List<MedicalConditionEntry> entries,
                Dictionary<MedicalConditionCategory, ReadOnlyCollection<MedicalConditionEntry>> entriesByCategory,
                Dictionary<HediffDef, MedicalConditionEntry> entriesByHediff,
                List<HediffDef> systemicDrugs)
            {
                AllEntries = new ReadOnlyCollection<MedicalConditionEntry>(entries);
                EntriesByCategory = new ReadOnlyDictionary<MedicalConditionCategory, ReadOnlyCollection<MedicalConditionEntry>>(
                    new Dictionary<MedicalConditionCategory, ReadOnlyCollection<MedicalConditionEntry>>(entriesByCategory));
                EntriesByHediff = new ReadOnlyDictionary<HediffDef, MedicalConditionEntry>(
                    new Dictionary<HediffDef, MedicalConditionEntry>(entriesByHediff));
                SystemicDrugs = new ReadOnlyCollection<HediffDef>(systemicDrugs);
            }
        }

        /// <summary>[CORE-CAT-01] One fully built catalog generation, published with a single volatile reference write.</summary>
        private sealed class CatalogSnapshot
        {
            public readonly ReadOnlyDictionary<BodyPartDef, ReadOnlyCollection<MedicalOperationEntry>> ReplacementsByPart;
            public readonly ReadOnlyDictionary<BodyPartDef, ReadOnlyCollection<MedicalOperationEntry>> ImplantsByPart;
            public readonly ConditionCatalogSnapshot Conditions;

            public CatalogSnapshot(
                Dictionary<BodyPartDef, List<MedicalOperationEntry>> replacementsByPart,
                Dictionary<BodyPartDef, List<MedicalOperationEntry>> implantsByPart,
                ConditionCatalogSnapshot conditions)
            {
                ReplacementsByPart = CreateReadOnlyOperationIndex(replacementsByPart);
                ImplantsByPart = CreateReadOnlyOperationIndex(implantsByPart);
                Conditions = conditions;
            }
        }

        /// <summary>
        /// Builder class for constructing medical condition entries in the catalog.
        /// </summary>
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
            GetSnapshot();
        }

        /// <summary>
        /// [CORE-CAT-01] Scans DefDatabase and builds surgery indexes plus the canonical condition catalog.
        /// Invoked on startup or first query to ensure all modded defs are resident.
        /// </summary>
        public static void InitializeCatalog()
        {
            lock (catalogLock)
            {
                if (currentSnapshot != null) return;
                BuildAndPublishCatalogSnapshot();
            }
        }

        /// <summary>
        /// [CORE-CAT-01] Returns one published generation, initializing it on first access.
        /// A concurrent refresh may publish a newer generation, but this reference remains complete and immutable.
        /// </summary>
        private static CatalogSnapshot GetSnapshot()
        {
            // BREAKPOINT ANCHOR: Published Catalog Snapshot Read
            CatalogSnapshot snapshot = currentSnapshot;
            if (snapshot == null)
            {
                InitializeCatalog();
                snapshot = currentSnapshot;
            }

            return snapshot;
        }

        /// <summary>
        /// [CORE-CAT-01] Builds a replacement privately and atomically publishes it after every index is complete.
        /// </summary>
        private static void BuildAndPublishCatalogSnapshot()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Dictionary<BodyPartDef, List<MedicalOperationEntry>> replacementRecipesByPart =
                new Dictionary<BodyPartDef, List<MedicalOperationEntry>>(128);
            Dictionary<BodyPartDef, List<MedicalOperationEntry>> implantRecipesByPart =
                new Dictionary<BodyPartDef, List<MedicalOperationEntry>>(128);

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

            ConditionCatalogSnapshot conditions = BuildConditionCatalog(surgicalHediffs, chemicalTolerances);
            watch.Stop();

            // BREAKPOINT ANCHOR: Atomic Catalog Snapshot Publication
            currentSnapshot = new CatalogSnapshot(
                replacementRecipesByPart,
                implantRecipesByPart,
                conditions);

            CoreLogEvents.MedicalCatalogInitialized(
                (float)watch.Elapsed.TotalMilliseconds,
                totalReplacementRecipes,
                totalImplantRecipes,
                conditions.AllEntries.Count);
        }

        /// <summary>
        /// [CORE-CAT-01] Converts builder-owned operation lists into immutable indexes for publication.
        /// </summary>
        private static ReadOnlyDictionary<BodyPartDef, ReadOnlyCollection<MedicalOperationEntry>> CreateReadOnlyOperationIndex(
            Dictionary<BodyPartDef, List<MedicalOperationEntry>> builders)
        {
            Dictionary<BodyPartDef, ReadOnlyCollection<MedicalOperationEntry>> entries =
                new Dictionary<BodyPartDef, ReadOnlyCollection<MedicalOperationEntry>>(builders.Count);
            foreach (KeyValuePair<BodyPartDef, List<MedicalOperationEntry>> pair in builders)
            {
                entries.Add(pair.Key, new ReadOnlyCollection<MedicalOperationEntry>(pair.Value));
            }

            return new ReadOnlyDictionary<BodyPartDef, ReadOnlyCollection<MedicalOperationEntry>>(entries);
        }

        /// <summary>
        /// [CORE-CAT-03] Builds one condition entry per relevant HediffDef and merges all known grant-source target alternatives.
        /// This is a definition-only scan; it does not inspect maps, pawns, or runtime hediff state.
        /// </summary>
        /// <param name="surgicalHediffs">A set of surgical condition definitions to exclude.</param>
        /// <param name="chemicalTolerances">A set of chemical tolerance condition definitions to exclude.</param>
        /// <remarks>
        /// This method scans all known HediffDefs and builds a catalog of medical conditions,
        /// excluding those that are surgical or chemical tolerance conditions.
        /// </remarks>
        private static ConditionCatalogSnapshot BuildConditionCatalog(
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

                    // Direct HediffDef target metadata
                    if (hediff.defaultInstallPart != null)
                    {
                        AddConditionTarget(builder, MedicalConditionTargetKind.SpecificPart, hediff.defaultInstallPart, MedicalConditionSource.ConditionDefinition);
                    }
                    builders.Add(hediff, builder);
                }
            }

            // BREAKPOINT ANCHOR: Chemical Addictions & Dependency Target Discovery
            List<ChemicalDef> allChemicals = DefDatabase<ChemicalDef>.AllDefsListForReading;
            if (allChemicals != null)
            {
                for (int i = 0; i < allChemicals.Count; i++)
                {
                    ChemicalDef chem = allChemicals[i];
                    if (chem == null) continue;

                    MedicalConditionBuilder addictionBuilder;
                    if (chem.addictionHediff != null && builders.TryGetValue(chem.addictionHediff, out addictionBuilder))
                    {
                        addictionBuilder.Sources |= MedicalConditionSource.Ingestible;
                        AddConditionTarget(addictionBuilder, MedicalConditionTargetKind.WholeBody, null, MedicalConditionSource.Ingestible);
                    }
                }
            }

            // BREAKPOINT ANCHOR: Definition-Only Ingestible, Apparel, and Species Target Discovery
            List<ThingDef> allThings = DefDatabase<ThingDef>.AllDefsListForReading;
            if (allThings != null)
            {
                for (int i = 0; i < allThings.Count; i++)
                {
                    ThingDef thing = allThings[i];
                    if (thing == null) continue;

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

                    // Apparel & AoE Hediff-granting comps
                    if (thing.comps != null && thing.comps.Count > 0)
                    {
                        for (int c = 0; c < thing.comps.Count; c++)
                        {
                            CompProperties compProps = thing.comps[c];
                            if (compProps is CompProperties_CauseHediff_Apparel apparelProps && apparelProps.hediff != null)
                            {
                                if (builders.TryGetValue(apparelProps.hediff, out MedicalConditionBuilder builder))
                                {
                                    builder.Sources |= MedicalConditionSource.Other;
                                    AddConditionTarget(
                                        builder,
                                        apparelProps.part != null ? MedicalConditionTargetKind.SpecificPart : MedicalConditionTargetKind.WholeBody,
                                        apparelProps.part,
                                        MedicalConditionSource.Other);
                                }
                            }
                            else if (compProps is CompProperties_CauseHediff_AoE aoeProps && aoeProps.hediff != null)
                            {
                                if (builders.TryGetValue(aoeProps.hediff, out MedicalConditionBuilder builder))
                                {
                                    builder.Sources |= MedicalConditionSource.Other;
                                    AddConditionTarget(
                                        builder,
                                        aoeProps.part != null ? MedicalConditionTargetKind.SpecificPart : MedicalConditionTargetKind.WholeBody,
                                        aoeProps.part,
                                        MedicalConditionSource.Other);
                                }
                            }
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
                    if (sourceDef == null) continue;

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

            // BREAKPOINT ANCHOR: Damage Def Additional Hediffs Discovery
            List<DamageDef> allDamages = DefDatabase<DamageDef>.AllDefsListForReading;
            if (allDamages != null)
            {
                for (int i = 0; i < allDamages.Count; i++)
                {
                    DamageDef damage = allDamages[i];
                    if (damage == null || !damage.harmsHealth) continue;

                    if (damage.additionalHediffs != null)
                    {
                        for (int h = 0; h < damage.additionalHediffs.Count; h++)
                        {
                            HediffDef extraDef = damage.additionalHediffs[h]?.hediff;
                            if (extraDef != null && builders.TryGetValue(extraDef, out MedicalConditionBuilder builder))
                            {
                                builder.Sources |= MedicalConditionSource.Other;
                                AddConditionTarget(builder, MedicalConditionTargetKind.AnyLivePart, null, MedicalConditionSource.Other);
                            }
                        }
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
                            }
                        }
                    }

                    MedicalConditionBuilder pawnHediffBuilder;
                    if (incident.pawnHediff != null &&
                        builders.TryGetValue(incident.pawnHediff, out pawnHediffBuilder))
                    {
                        pawnHediffBuilder.Sources |= MedicalConditionSource.IncidentPawnHediff;
                        pawnHediffBuilder.IsMedical = true;
                        AddConditionTarget(
                            pawnHediffBuilder,
                            MedicalConditionTargetKind.WholeBody,
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
                    if (ability?.comps == null) continue;

                    for (int c = 0; c < ability.comps.Count; c++)
                    {
                        CompProperties_AbilityGiveHediff giveHediff =
                            ability.comps[c] as CompProperties_AbilityGiveHediff;
                        if (giveHediff != null)
                        {
                            if (giveHediff.hediffDef == null) continue;

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
                                    ? MedicalConditionTargetKind.WholeBody
                                    : MedicalConditionTargetKind.SpecificPart,
                                option.bodyPart,
                                MedicalConditionSource.RandomAbility);
                        }
                    }
                }
            }

            // BREAKPOINT ANCHOR: Default WholeBody Target Assignment For Systemic Conditions
            foreach (KeyValuePair<HediffDef, MedicalConditionBuilder> pair in builders)
            {
                MedicalConditionBuilder builder = pair.Value;
                if (builder.Targets.Count == 0)
                {
                    // Infection status does not prove a whole-body or any-live-part application site.
                    if (builder.Hediff.isInfection)
                    {
                        AddConditionTarget(
                            builder,
                            MedicalConditionTargetKind.Unknown,
                            null,
                            MedicalConditionSource.ConditionDefinition);
                    }
                    else if (builder.HasSystemicStageImpact ||
                        builder.Hediff.pregnant ||
                        builder.Hediff.IsAddiction ||
                        builder.Hediff == HediffDefOf.GeneticDrugNeed)
                    {
                        AddConditionTarget(
                            builder,
                            MedicalConditionTargetKind.WholeBody,
                            null,
                            MedicalConditionSource.ConditionDefinition);
                    }
                    else
                    {
                        AddConditionTarget(
                            builder,
                            MedicalConditionTargetKind.Unknown,
                            null,
                            MedicalConditionSource.ConditionDefinition);
                    }
                }
            }

            // BREAKPOINT ANCHOR: Duplicate Label Disambiguation Pre-Scan
            Dictionary<string, int> labelFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<HediffDef, MedicalConditionBuilder> pair in builders)
            {
                string rawLabel = GetFormattedConditionDefLabel(pair.Key);
                if (labelFrequency.TryGetValue(rawLabel, out int count))
                {
                    labelFrequency[rawLabel] = count + 1;
                }
                else
                {
                    labelFrequency.Add(rawLabel, 1);
                }
            }

            List<MedicalConditionEntry> entries = new List<MedicalConditionEntry>(builders.Count);
            Dictionary<HediffDef, MedicalConditionEntry> entriesByHediff =
                new Dictionary<HediffDef, MedicalConditionEntry>(builders.Count);
            List<MedicalConditionEntry> ingestibles = new List<MedicalConditionEntry>();
            List<MedicalConditionEntry> systemicMedical = new List<MedicalConditionEntry>();
            List<MedicalConditionEntry> localizedMedical = new List<MedicalConditionEntry>();
            List<MedicalConditionEntry> medicalTargetUnknown = new List<MedicalConditionEntry>();
            List<MedicalConditionEntry> psychic = new List<MedicalConditionEntry>();
            List<MedicalConditionEntry> other = new List<MedicalConditionEntry>();

            foreach (KeyValuePair<HediffDef, MedicalConditionBuilder> pair in builders)
            {
                MedicalConditionBuilder builder = pair.Value;
                MedicalConditionCategory categories = GetConditionCategories(builder);

                string baseLabel = GetFormattedConditionDefLabel(builder.Hediff);
                bool isDuplicate = labelFrequency.TryGetValue(baseLabel, out int freq) && freq > 1;
                string label = ResolveDisambiguatedLabel(builder.Hediff, baseLabel, isDuplicate);

                MedicalConditionEntry entry = new MedicalConditionEntry(
                    builder.Hediff,
                    label,
                    categories,
                    builder.Sources,
                    builder.HasSystemicStageImpact,
                    builder.HasPartLocalImpact,
                    builder.Targets);

                entries.Add(entry);
                entriesByHediff.Add(builder.Hediff, entry);

                if ((categories & MedicalConditionCategory.Ingestibles) != 0) ingestibles.Add(entry);
                if ((categories & MedicalConditionCategory.SystemicMedical) != 0) systemicMedical.Add(entry);
                if ((categories & MedicalConditionCategory.LocalizedMedical) != 0) localizedMedical.Add(entry);
                if ((categories & MedicalConditionCategory.MedicalTargetUnknown) != 0) medicalTargetUnknown.Add(entry);
                if ((categories & MedicalConditionCategory.PsychicAndAbilities) != 0) psychic.Add(entry);
                if ((categories & MedicalConditionCategory.Other) != 0) other.Add(entry);
            }

            Comparison<MedicalConditionEntry> entryComparer = CompareConditionEntries;
            entries.Sort(entryComparer);
            ingestibles.Sort(CompareConditionLabels);
            systemicMedical.Sort(CompareConditionLabels);
            localizedMedical.Sort(CompareConditionLabels);
            medicalTargetUnknown.Sort(CompareConditionLabels);
            psychic.Sort(CompareConditionLabels);
            other.Sort(CompareConditionLabels);

            Dictionary<MedicalConditionCategory, ReadOnlyCollection<MedicalConditionEntry>> entriesByCategory =
                new Dictionary<MedicalConditionCategory, ReadOnlyCollection<MedicalConditionEntry>>(6);
            entriesByCategory.Add(
                MedicalConditionCategory.Ingestibles,
                new ReadOnlyCollection<MedicalConditionEntry>(ingestibles));
            entriesByCategory.Add(
                MedicalConditionCategory.SystemicMedical,
                new ReadOnlyCollection<MedicalConditionEntry>(systemicMedical));
            entriesByCategory.Add(
                MedicalConditionCategory.LocalizedMedical,
                new ReadOnlyCollection<MedicalConditionEntry>(localizedMedical));
            entriesByCategory.Add(
                MedicalConditionCategory.MedicalTargetUnknown,
                new ReadOnlyCollection<MedicalConditionEntry>(medicalTargetUnknown));
            entriesByCategory.Add(
                MedicalConditionCategory.PsychicAndAbilities,
                new ReadOnlyCollection<MedicalConditionEntry>(psychic));
            entriesByCategory.Add(
                MedicalConditionCategory.Other,
                new ReadOnlyCollection<MedicalConditionEntry>(other));

            List<HediffDef> systemicDrugsAndStimulants = new List<HediffDef>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                systemicDrugsAndStimulants.Add(entries[i].Hediff);
            }
            systemicDrugsAndStimulants.Sort(
                (first, second) => string.Compare(
                    first.LabelCap.ToString(),
                    second.LabelCap.ToString(),
                    StringComparison.OrdinalIgnoreCase));

            return new ConditionCatalogSnapshot(
                entries,
                entriesByCategory,
                entriesByHediff,
                systemicDrugsAndStimulants);
        }

        /// <summary>
        /// [CORE-CAT-03] Disambiguates condition display labels when multiple definitions share the same localized label.
        /// </summary>
        /// <param name="def">The medical condition definition.</param>
        /// <param name="rawLabel">The original display label of the condition.</param>
        /// <param name="isDuplicate">Indicates whether the label is a duplicate.</param>
        /// <returns>A disambiguated label for the condition.</returns>
        private static string ResolveDisambiguatedLabel(HediffDef def, string rawLabel, bool isDuplicate)
        {
            if (!isDuplicate || def == null) return rawLabel;

            string defName = def.defName;
            string disambiguator;
            if (defName == "PregnantHuman" || defName == "Flu" || defName == "Plague")
            {
                disambiguator = "TargetHuman".Translate().ToString();
            }
            else if (defName == "Pregnant" || defName.StartsWith("Animal_"))
            {
                disambiguator = "TargetAnimal".Translate().ToString();
            }
            else if (defName.EndsWith("Moderate"))
            {
                disambiguator = "OverHaulers_Sandbox_Condition_Disambiguation_Moderate".Translate().ToString();
            }
            else if (defName.EndsWith("Severe"))
            {
                disambiguator = "OverHaulers_Sandbox_Condition_Disambiguation_Severe".Translate().ToString();
            }
            else if (defName.EndsWith("Major"))
            {
                disambiguator = "OverHaulers_Sandbox_Condition_Disambiguation_Major".Translate().ToString();
            }
            else if (defName.EndsWith("Minor"))
            {
                disambiguator = "OverHaulers_Sandbox_Condition_Disambiguation_Minor".Translate().ToString();
            }
            else
            {
                disambiguator = defName;
            }

            return "OverHaulers_Sandbox_Condition_DisambiguatedLabel".Translate(rawLabel, disambiguator).ToString();
        }

        /// <summary>
        /// Retrieves the list of body parts that serve as sources of consciousness.
        /// </summary>
        /// <returns>A list of body part definitions that are consciousness sources.</returns>
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

        /// <summary>
        /// Determines whether a medical condition should be excluded from consideration based on its type and associated properties.
        /// </summary>
        /// <param name="hediff">The medical condition definition to evaluate.</param>
        /// <param name="surgicalHediffs">A set of surgical condition definitions to exclude.</param>
        /// <param name="chemicalTolerances">A set of chemical tolerance condition definitions to exclude.</param>
        /// <returns>True if the condition should be excluded; otherwise, false.</returns>
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

        /// <summary>
        /// Determines the impact of a medical condition on systemic stages, local body parts, and pregnancy status.
        /// </summary>
        /// <param name="hediff">The medical condition definition to evaluate.</param>
        /// <param name="hasSystemicStageImpact">Outputs whether the condition has an impact on systemic stages.</param>
        /// <param name="hasPartLocalImpact">Outputs whether the condition has an impact on local body parts.</param>
        /// <param name="isPregnancy">Outputs whether the condition represents a pregnancy.</param>
        /// <returns>True if the condition has any impact; otherwise, false.</returns>
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

        /// <summary>
        /// Determines whether the specified list of pawn capacity modifiers contains any relevant modifiers for medical conditions.
        /// </summary>
        /// <param name="modifiers">The list of pawn capacity modifiers to check.</param>
        /// <returns>True if any relevant modifiers are found; otherwise, false.</returns>
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

        /// <summary>
        /// Determines whether the specified pawn capacity is relevant for medical conditions.
        /// </summary>
        /// <param name="capacity">The pawn capacity to check.</param>
        /// <returns>True if the capacity is relevant for medical conditions; otherwise, false.</returns>
        private static bool IsRelevantConditionCapacity(PawnCapacityDef capacity)
        {
            return MedicalClassifier.IsAthleticCapacity(capacity) ||
                   capacity == PawnCapacityDefOf.BloodFiltration;
        }

        /// <summary>
        /// Adds hediff giver targets to the corresponding medical condition builders based on the provided givers and source.
        /// </summary>
        /// <param name="builders">The dictionary of medical condition builders keyed by hediff definitions.</param>
        /// <param name="givers">The list of hediff givers to process.</param>
        /// <param name="source">The source of the medical condition.</param>
        /// <param name="species">The species affected by the condition, if applicable.</param>
        /// <param name="giverSet">The hediff giver set associated with the condition, if applicable.</param>
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

        /// <summary>
        /// Adds a condition target to the medical condition builder if it does not already exist.
        /// </summary>
        /// <param name="builder">The medical condition builder to which the target will be added.</param>
        /// <param name="kind">The kind of medical condition target.</param>
        /// <param name="part">The specific body part affected, if applicable.</param>
        /// <param name="source">The source of the medical condition.</param>
        /// <param name="species">The species affected by the condition, if applicable.</param>
        /// <param name="giverSet">The hediff giver set associated with the condition, if applicable.</param>
        /// <param name="incident">The incident associated with the condition, if applicable.</param>
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

        /// <summary>
        /// [CORE-CAT-03] Determines presentation groups from existing condition classifications and known target alternatives.
        /// </summary>
        /// <param name="builder">The medical condition builder containing the hediff and sources.</param>
        /// <returns>One or more categories for the medical condition.</returns>
        private static MedicalConditionCategory GetConditionCategories(MedicalConditionBuilder builder)
        {
            HediffDef def = builder.Hediff;

            // 1. Ingestibles, Chemical Addictions, Need Deficits, and Ingestion Toxins
            if ((builder.Sources & MedicalConditionSource.Ingestible) != 0 ||
                def.IsAddiction ||
                def == HediffDefOf.GeneticDrugNeed ||
                def == HediffDefOf.Hangover ||
                def == HediffDefOf.DrugOverdose ||
                def == HediffDefOf.FoodPoisoning)
            {
                return MedicalConditionCategory.Ingestibles;
            }

            // 2. Psycasts, Psychic Entropy, and Ability Coma/Shock States
            if ((builder.Sources & (MedicalConditionSource.Ability | MedicalConditionSource.RandomAbility)) != 0 ||
                def.hediffClass.Name.IndexOf("Psychic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                def.defName.IndexOf("Psychic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                def == HediffDefOf.PsychicEntropy ||
                def == HediffDefOf.PsychicHangover ||
                def == HediffDefOf.PsychicSuppression ||
                def == HediffDefOf.PsychicLove ||
                def == HediffDefOf.PsychicBond ||
                def == HediffDefOf.PsychicBondTorn ||
                def == HediffDefOf.PsychicShock ||
                def == HediffDefOf.DarkPsychicShock ||
                def.defName == "BrainShock")
            {
                return MedicalConditionCategory.PsychicAndAbilities;
            }

            // 3. Medical target scope
            if (builder.IsMedical ||
                builder.HasSystemicStageImpact ||
                builder.HasPartLocalImpact ||
                (builder.Sources & MedicalConditionSource.DiseaseIncident) != 0)
            {
                MedicalConditionCategory categories = MedicalConditionCategory.None;
                for (int i = 0; i < builder.Targets.Count; i++)
                {
                    MedicalConditionTargetKind kind = builder.Targets[i].Kind;
                    if (kind == MedicalConditionTargetKind.WholeBody)
                    {
                        categories |= MedicalConditionCategory.SystemicMedical;
                    }
                    else if (kind == MedicalConditionTargetKind.SpecificPart ||
                        kind == MedicalConditionTargetKind.AnyLivePart)
                    {
                        categories |= MedicalConditionCategory.LocalizedMedical;
                    }
                }

                return categories != MedicalConditionCategory.None
                    ? categories
                    : MedicalConditionCategory.MedicalTargetUnknown;
            }

            return MedicalConditionCategory.Other;
        }

        /// <summary>
        /// Compares two medical condition entries for sorting purposes.
        /// Primary sort is by category, secondary sort is by label (case-insensitive).
        /// </summary>
        /// <param name="first">The first medical condition entry to compare.</param>
        /// <param name="second">The second medical condition entry to compare.</param>
        /// <returns>An integer indicating the relative order of the entries.</returns>
        private static int CompareConditionEntries(MedicalConditionEntry first, MedicalConditionEntry second)
        {
            int categoryComparison = first.Categories.CompareTo(second.Categories);
            if (categoryComparison != 0) return categoryComparison;

            return CompareConditionLabels(first, second);
        }

        /// <summary>
        /// [CORE-CAT-03] Compares condition labels in case-insensitive alphabetical order.
        /// </summary>
        /// <param name="first">The first medical condition entry.</param>
        /// <param name="second">The second medical condition entry.</param>
        /// <returns>An integer indicating the relative label order.</returns>
        private static int CompareConditionLabels(MedicalConditionEntry first, MedicalConditionEntry second)
        {
            return string.Compare(first.Label, second.Label, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// [CORE-CAT-01] Rebuilds the catalog and atomically publishes the new generation.
        /// Readers continue using the previous complete generation until the replacement is ready.
        /// </summary>
        public static void ClearAndReinitialize()
        {
            lock (catalogLock)
            {
                BuildAndPublishCatalogSnapshot();
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

        /// <summary>
        /// Determines whether the specified severity value is finite and positive.
        /// </summary>
        /// <param name="severity">The severity value to evaluate.</param>
        /// <returns>True if the severity is finite and positive; otherwise, false.</returns>
        private static bool IsFinitePositiveSeverity(float severity)
        {
            return severity > 0f && severity < float.MaxValue &&
                   !float.IsNaN(severity) && !float.IsInfinity(severity);
        }

        /// <summary>
        /// Calculates the unbounded final stage severity for a given HediffDef and stage index.
        /// </summary>
        /// <param name="def">The HediffDef containing the stages.</param>
        /// <param name="stageIndex">The index of the stage to evaluate.</param>
        /// <returns>The calculated unbounded final stage severity.</returns>
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
        /// Returns a formatted display label for a condition definition, including its peak-impact stage suffix if applicable.
        /// </summary>
        /// <param name="def">The HediffDef for which to retrieve the formatted condition label.</param>
        /// <returns>A formatted string representing the condition label, including stage indicators if impactful.</returns>
        public static string GetFormattedConditionDefLabel(HediffDef def)
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
        /// Unlike <see cref="GetFormattedConditionDefLabel"/>, this method does not predict a peak-impact stage.
        /// </summary>
        /// <param name="hediff">The live or simulated hediff to format.</param>
        /// <returns>The current engine label, or an empty string when the hediff is null.</returns>
        public static string GetFormattedConditionLabel(Hediff hediff)
        {
            if (hediff == null) return string.Empty;

            string actualLabel = hediff.LabelCap.ToString();
            return string.IsNullOrEmpty(actualLabel)
                ? GetFormattedConditionDefLabel(hediff.def)
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
            CatalogSnapshot snapshot = GetSnapshot();

            ReadOnlyCollection<MedicalOperationEntry> replacements;
            if (snapshot.ReplacementsByPart.TryGetValue(partDef, out replacements) && replacements.Count > 0) return true;
            ReadOnlyCollection<MedicalOperationEntry> implants;
            if (snapshot.ImplantsByPart.TryGetValue(partDef, out implants) && implants.Count > 0) return true;

            return false;
        }

        /// <summary>
        /// Retrieves all indexed replacement (prosthetic/bionic) operations available for a specific BodyPartDef.
        /// Returned data belongs to a complete published generation and remains stable across refreshes.
        /// </summary>
        /// <param name="partDef">The BodyPartDef for which to retrieve replacement operations.</param>
        /// <returns>A read-only list of indexed replacement operations, or null if none are available.</returns>
        public static IReadOnlyList<MedicalOperationEntry> GetReplacementsFor(BodyPartDef partDef)
        {
            CatalogSnapshot snapshot = GetSnapshot();
            ReadOnlyCollection<MedicalOperationEntry> entries;
            if (partDef != null && snapshot.ReplacementsByPart.TryGetValue(partDef, out entries))
            {
                return entries;
            }
            return null;
        }

        /// <summary>
        /// Retrieves all indexed localized implant operations available for a specific BodyPartDef.
        /// Returned data belongs to a complete published generation and remains stable across refreshes.
        /// </summary>
        /// <param name="partDef">The BodyPartDef for which to retrieve implant operations.</param>
        /// <returns>A read-only list of indexed implant operations, or null if none are available.</returns>
        public static IReadOnlyList<MedicalOperationEntry> GetImplantsFor(BodyPartDef partDef)
        {
            CatalogSnapshot snapshot = GetSnapshot();
            ReadOnlyCollection<MedicalOperationEntry> entries;
            if (partDef != null && snapshot.ImplantsByPart.TryGetValue(partDef, out entries))
            {
                return entries;
            }
            return null;
        }

        /// <summary>
        /// Legacy read-only projection of condition definitions, retained for callers migrating to the categorized catalog API.
        /// </summary>
        /// <returns>The current condition definitions in alphabetical label order.</returns>
        public static IReadOnlyList<HediffDef> GetSystemicDrugs()
        {
            return GetSnapshot().Conditions.SystemicDrugs;
        }

        #endregion

        #region 6. [CORE-CAT-03] PUBLIC CONDITION QUERY API

        /// <summary>
        /// [CORE-CAT-03] Returns the stable category order used by the condition catalog.
        /// The returned collection is read-only and reused after initialization.
        /// </summary>
        public static IReadOnlyList<MedicalConditionCategory> GetConditionCategories()
        {
            GetSnapshot();
            return conditionCategories;
        }

        /// <summary>
        /// [CORE-CAT-03] Enumerates all impactful condition definitions in category-membership and label order.
        /// This definition catalog does not apply pawn-state eligibility filtering.
        /// </summary>
        public static IReadOnlyList<MedicalConditionEntry> GetAllConditionEntries()
        {
            return GetSnapshot().Conditions.AllEntries;
        }

        /// <summary>
        /// [CORE-CAT-03] Enumerates the impactful condition definitions assigned to one category.
        /// </summary>
        /// <param name="category">One category returned by <see cref="GetConditionCategories"/>.</param>
        /// <returns>A read-only list of medical condition entries in the specified category.</returns>
        public static IReadOnlyList<MedicalConditionEntry> GetConditionEntries(MedicalConditionCategory category)
        {
            CatalogSnapshot snapshot = GetSnapshot();

            ReadOnlyCollection<MedicalConditionEntry> entries;
            return snapshot.Conditions.EntriesByCategory.TryGetValue(category, out entries)
                ? entries
                : emptyConditionEntries;
        }

        /// <summary>
        /// [CORE-CAT-03] Retrieves a condition entry by definition without allocating a new catalog snapshot.
        /// </summary>
        /// <param name="hediff">The HediffDef to look up.</param>
        /// <param name="entry">The corresponding medical condition entry if found; otherwise, null.</param>
        /// <returns>True if the entry was found; otherwise, false.</returns>
        public static bool TryGetConditionEntry(HediffDef hediff, out MedicalConditionEntry entry)
        {
            CatalogSnapshot snapshot = GetSnapshot();
            if (hediff != null)
            {
                return snapshot.Conditions.EntriesByHediff.TryGetValue(hediff, out entry);
            }

            entry = null;
            return false;
        }

        /// <summary>
        /// [CORE-CAT-03] Returns known target alternatives for a condition. Specific-part and any-live-part
        /// alternatives still require filtering against the selected pawn by the caller.
        /// Unknown records are informational and must not be treated as eligible targets.
        /// </summary>
        /// <param name="hediff">The HediffDef for which to retrieve target constraints.</param>
        /// <returns>A read-only list of medical condition targets associated with the specified HediffDef.</returns>
        public static IReadOnlyList<MedicalConditionTarget> GetConditionTargetConstraints(HediffDef hediff)
        {
            MedicalConditionEntry entry;
            return TryGetConditionEntry(hediff, out entry) ? entry.Targets : emptyConditionTargets;
        }

        #endregion
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TEST-06] STATIC MEDICAL CATALOG
    /// Pre-indexes surgeries and implants, plus categorized condition definitions with source-derived target alternatives.
    /// Provides indexed query tables for the interactive medical planner and diagnostic census tools.
    /// Resides under Source/Settings/Tooling/TestBench/.
    /// </summary>
    public static class MedicalCatalog
    {
        #region 1. STORAGE & LOOKUP TABLES

        /// <summary>Stores all medical condition categories.</summary>
        private static readonly ReadOnlyCollection<MedicalConditionCategory> conditionCategories =
            new ReadOnlyCollection<MedicalConditionCategory>(new List<MedicalConditionCategory>
            {
                MedicalConditionCategory.Ingestibles,
                MedicalConditionCategory.SystemicMedical,
                MedicalConditionCategory.LocalizedMedical,
                MedicalConditionCategory.MedicalTargetUnknown,
                MedicalConditionCategory.Other
            });

        private static readonly ReadOnlyCollection<MedicalConditionEntry> emptyConditionEntries =
            new ReadOnlyCollection<MedicalConditionEntry>(new List<MedicalConditionEntry>());

        private static readonly ReadOnlyCollection<MedicalConditionTarget> emptyConditionTargets =
            new ReadOnlyCollection<MedicalConditionTarget>(new List<MedicalConditionTarget>());

        private static readonly ReadOnlyCollection<MedicalProcedureTarget> emptyProcedureTargets =
            new ReadOnlyCollection<MedicalProcedureTarget>(new List<MedicalProcedureTarget>());

        /// <summary>
        /// The sole publication boundary; snapshots are complete before assignment and never mutated afterward.
        /// Null until the first complete build is ready.
        /// </summary>
        private static volatile CatalogSnapshot currentSnapshot;

        /// <summary>Serializes initial builds and explicit catalog refreshes.</summary>
        private static readonly object catalogLock = new object();

        /// <summary>[TEST-06] One fully built catalog generation, published with a single volatile reference write.</summary>
        private sealed class CatalogSnapshot
        {
            public readonly ReadOnlyDictionary<BodyPartDef, ReadOnlyCollection<MedicalOperationEntry>> ReplacementsByPart;
            public readonly ReadOnlyDictionary<BodyPartDef, ReadOnlyCollection<MedicalOperationEntry>> ImplantsByPart;
            public readonly MedicalConditionIndexing.ConditionCatalogSnapshot Conditions;

            public CatalogSnapshot(
                Dictionary<BodyPartDef, List<MedicalOperationEntry>> replacementsByPart,
                Dictionary<BodyPartDef, List<MedicalOperationEntry>> implantsByPart,
                MedicalConditionIndexing.ConditionCatalogSnapshot conditions)
            {
                ReplacementsByPart = CreateReadOnlyOperationIndex(replacementsByPart);
                ImplantsByPart = CreateReadOnlyOperationIndex(implantsByPart);
                Conditions = conditions;
            }
        }

        #endregion

        #region 2. CATALOG BOOTSTRAPPING & PRE-INDEXING

        /// <summary>
        /// [TEST-06] Ensures the medical surgery and condition catalog has been indexed.
        /// Idempotent on-demand initializer. The first build must occur on the main thread.
        /// </summary>
        public static void EnsureInitialized()
        {
            GetSnapshot();
        }

        /// <summary>
        /// [TEST-06] Scans DefDatabase and builds surgery indexes plus the canonical condition catalog.
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
        /// [TEST-06] Returns one published generation, initializing it on first access.
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
        /// [TEST-06] Builds a replacement privately and atomically publishes it after every index is complete.
        /// </summary>
        private static void BuildAndPublishCatalogSnapshot()
        {
            if (!UnityData.IsInMainThread)
                throw new InvalidOperationException("Medical catalog indexing must run on the main thread.");

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

            MedicalConditionIndexing.ConditionCatalogSnapshot conditions = MedicalConditionIndexing.Build(surgicalHediffs, chemicalTolerances);
            watch.Stop();

            // BREAKPOINT ANCHOR: Atomic Catalog Snapshot Publication
            currentSnapshot = new CatalogSnapshot(
                replacementRecipesByPart,
                implantRecipesByPart,
                conditions);

            OHLog.Event("OverHaulers_Event_MedicalCatalog_Indexed",
                ((float)watch.Elapsed.TotalMilliseconds).ToString("F1"),
                totalReplacementRecipes,
                totalImplantRecipes,
                conditions.AllEntries.Count);
        }

        /// <summary>
        /// [TEST-06] Converts builder-owned operation lists into immutable indexes for publication.
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
        /// [TEST-06] Rebuilds the catalog and atomically publishes the new generation.
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

        #region 3. [TEST-06] CONDITION SEVERITY & LABEL API

        /// <summary>Returns the initial severity for a simulated condition.</summary>
        public static float GetPeakImpactSeverity(HediffDef def, out string stageSuffix)
        {
            return MedicalConditionSeverity.GetPeakImpactSeverity(def, out stageSuffix);
        }

        /// <summary>Formats a condition definition using its predicted peak-impact stage.</summary>
        public static string GetFormattedConditionDefLabel(HediffDef def)
        {
            return MedicalConditionSeverity.GetFormattedConditionDefLabel(def);
        }

        /// <summary>Formats an existing condition using its current engine label.</summary>
        public static string GetFormattedConditionLabel(Hediff hediff)
        {
            return MedicalConditionSeverity.GetFormattedConditionLabel(hediff);
        }

        #endregion

        #region 4. PUBLIC OPERATION & PROCEDURE QUERY API

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
        /// Retrieves replacement operations whose recipes belong to the selected pawn's ThingDef.
        /// Shared body-part definitions alone are insufficient because animal and human recipes may target the same part.
        /// </summary>
        /// <param name="pawn">The pawn for which to retrieve replacement operations.</param>
        /// <param name="part">The body part record associated with the replacement operation.</param>
        /// <returns>A read-only list of indexed replacement operations applicable to the pawn and body part, or null if none are available.</returns>
        public static IReadOnlyList<MedicalOperationEntry> GetReplacementsFor(Pawn pawn, BodyPartRecord part)
        {
            return GetPawnApplicableOperations(pawn, part, GetReplacementsFor(part?.def));
        }

        /// <summary>
        /// Retrieves implant operations whose recipes belong to the selected pawn's ThingDef.
        /// </summary>
        /// <param name="pawn">The pawn for which to retrieve implant operations.</param>
        /// <param name="part">The body part record associated with the implant operation.</param>
        /// <returns>A read-only list of indexed implant operations applicable to the pawn and body part, or null if none are available.</returns>
        public static IReadOnlyList<MedicalOperationEntry> GetImplantsFor(Pawn pawn, BodyPartRecord part)
        {
            return GetPawnApplicableOperations(pawn, part, GetImplantsFor(part?.def));
        }

        /// <summary>
        /// Returns true when the recipe is owned by the pawn's ThingDef, directly or through inheritance.
        /// </summary>
        /// <param name="pawn">The pawn whose ThingDef ownership is being checked.</param>
        /// <param name="recipe">The recipe to check for availability.</param>
        /// <returns>True if the recipe is available to the pawn; otherwise, false.</returns>
        public static bool IsRecipeAvailableToPawn(Pawn pawn, RecipeDef recipe)
        {
            List<RecipeDef> recipes = pawn?.def?.AllRecipes;
            if (recipes == null || recipe == null) return false;

            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i] == recipe) return true;
            }
            return false;
        }

        /// <summary>
        /// Filters the candidate medical operations for those applicable to the specified pawn and body part.
        /// </summary>
        /// <param name="pawn">The pawn for which to retrieve applicable operations.</param>
        /// <param name="part">The body part record associated with the operation.</param>
        /// <param name="candidates">The list of candidate medical operations to filter.</param>
        /// <returns>A read-only list of applicable medical operations, or null if none are available.</returns>
        private static IReadOnlyList<MedicalOperationEntry> GetPawnApplicableOperations(
            Pawn pawn,
            BodyPartRecord part,
            IReadOnlyList<MedicalOperationEntry> candidates)
        {
            if (pawn == null || part == null || candidates == null || candidates.Count == 0)
                return null;

            List<MedicalOperationEntry> applicable = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                MedicalOperationEntry entry = candidates[i];
                if (!IsRecipeAvailableToPawn(pawn, entry.Recipe)) continue;
                if (!WorkerTargetsPart(pawn, part, entry.Recipe)) continue;

                if (applicable == null) applicable = new List<MedicalOperationEntry>(4);
                applicable.Add(entry);
            }

            return applicable == null
                ? null
                : new ReadOnlyCollection<MedicalOperationEntry>(applicable);
        }

        /// <summary>
        /// Determines whether the specified recipe targets the given body part for the pawn.
        /// </summary>
        /// <param name="pawn">The pawn for which to check the recipe.</param>
        /// <param name="part">The body part record to check against the recipe's targets.</param>
        /// <param name="recipe">The medical recipe to evaluate.</param>
        /// <returns>True if the recipe targets the specified body part for the pawn; otherwise, false.</returns>
        private static bool WorkerTargetsPart(Pawn pawn, BodyPartRecord part, RecipeDef recipe)
        {
            if (recipe?.Worker == null) return false;
            foreach (BodyPartRecord target in recipe.Worker.GetPartsToApplyOn(pawn, recipe))
            {
                if (target == part) return true;
            }
            return false;
        }

        /// <summary>
        /// Enumerates the recipes and concrete targets exposed by the pawn's native medical recipe workers.
        /// The result mirrors health-card procedure discovery without scheduling a bill or applying a recipe.
        /// </summary>
        /// <param name="pawn">The pawn whose race recipes, anatomy and current health state define eligibility.</param>
        /// <returns>A read-only list of native recipe/target alternatives, or an empty list for an invalid pawn.</returns>
        public static IReadOnlyList<MedicalProcedureTarget> GetNativeProcedureTargets(Pawn pawn)
        {
            List<RecipeDef> recipes = pawn?.def?.AllRecipes;
            if (recipes == null || pawn.health?.hediffSet == null)
            {
                return emptyProcedureTargets;
            }

            List<MedicalProcedureTarget> targets = new List<MedicalProcedureTarget>();
            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    RecipeDef recipe = recipes[i];
                    // Main-menu sandbox pawns intentionally bypass research/world availability.
                    // Live plans revalidate recipe.AvailableNow through the recipe worker path.
                    if (recipe == null || recipe.Worker == null) continue;
                    if (Current.Game != null && !recipe.AvailableNow) continue;

                    AcceptanceReport report = Current.Game == null
                        ? AcceptanceReport.WasAccepted
                        : recipe.Worker.AvailableReport(pawn);
                    if (!report.Accepted && report.Reason.NullOrEmpty()) continue;

                    string unavailableReason = report.Accepted ? null : report.Reason;
                    if (recipe.targetsBodyPart)
                    {
                        foreach (BodyPartRecord part in recipe.Worker.GetPartsToApplyOn(pawn, recipe))
                        {
                            if (part != null && recipe.AvailableOnNow(pawn, part))
                            {
                                string label = recipe.Worker.GetLabelWhenUsedOn(pawn, part);
                                targets.Add(new MedicalProcedureTarget(
                                    recipe,
                                    part,
                                    label.NullOrEmpty() ? recipe.LabelCap.ToString() : label,
                                    unavailableReason));
                            }
                        }
                    }
                    else if (!pawn.health.hediffSet.HasHediff(recipe.addsHediff))
                    {
                        string label = recipe.Worker.GetLabelWhenUsedOn(pawn, null);
                        targets.Add(new MedicalProcedureTarget(
                            recipe,
                            null,
                            label.NullOrEmpty() ? recipe.LabelCap.ToString() : label,
                            unavailableReason));
                    }
                }
            }

            return new ReadOnlyCollection<MedicalProcedureTarget>(targets);
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

        #region 5. [TEST-06] PUBLIC CONDITION QUERY API

        /// <summary>
        /// [TEST-06] Returns the stable category order used by the condition catalog.
        /// The returned collection is read-only and reused after initialization.
        /// </summary>
        public static IReadOnlyList<MedicalConditionCategory> GetConditionCategories()
        {
            GetSnapshot();
            return conditionCategories;
        }

        /// <summary>
        /// [TEST-06] Enumerates all impactful condition definitions in category-membership and label order.
        /// This definition catalog does not apply pawn-state eligibility filtering.
        /// </summary>
        public static IReadOnlyList<MedicalConditionEntry> GetAllConditionEntries()
        {
            return GetSnapshot().Conditions.AllEntries;
        }

        /// <summary>
        /// [TEST-06] Enumerates medical condition definitions independently of solver impact.
        /// Target records remain source evidence and do not prove runtime eligibility.
        /// </summary>
        public static IReadOnlyList<MedicalConditionEntry> GetAllMedicalConditionEntries()
        {
            return GetSnapshot().Conditions.MedicalEntries;
        }

        /// <summary>
        /// [TEST-06] Enumerates the impactful condition definitions assigned to one category.
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
        /// [TEST-06] Returns medically cataloged conditions in one category without applying solver relevance.
        /// </summary>
        public static IReadOnlyList<MedicalConditionEntry> GetMedicalConditionEntries(MedicalConditionCategory category)
        {
            CatalogSnapshot snapshot = GetSnapshot();

            ReadOnlyCollection<MedicalConditionEntry> entries;
            return snapshot.Conditions.MedicalEntriesByCategory.TryGetValue(category, out entries)
                ? entries
                : emptyConditionEntries;
        }

        /// <summary>
        /// [TEST-06] Retrieves a condition entry by definition without allocating a new catalog snapshot.
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
        /// [TEST-06] Retrieves a medically cataloged condition independently of solver impact.
        /// </summary>
        /// <param name="hediff">The HediffDef to look up.</param>
        /// <param name="entry">The corresponding medical condition entry if found; otherwise, null.</param>
        /// <returns>True if the entry was found; otherwise, false.</returns>
        public static bool TryGetMedicalConditionEntry(HediffDef hediff, out MedicalConditionEntry entry)
        {
            CatalogSnapshot snapshot = GetSnapshot();
            if (hediff != null)
            {
                return snapshot.Conditions.MedicalEntriesByHediff.TryGetValue(hediff, out entry);
            }

            entry = null;
            return false;
        }

        /// <summary>
        /// Returns true when a source target is valid for the pawn's race.
        /// </summary>
        /// <param name="pawn">The pawn whose race is being checked against the target.</param>
        /// <param name="target">The medical condition target to evaluate.</param>
        public static bool ConditionTargetMatchesPawn(Pawn pawn, MedicalConditionTarget target)
        {
            return pawn != null && (target.Species == null || target.Species == pawn.def);
        }

        /// <summary>
        /// Returns true when the condition has a pawn-compatible whole-body target.
        /// </summary>
        /// <param name="pawn">The pawn whose compatibility is being checked.</param>
        /// <param name="entry">The medical condition entry to evaluate.</param>
        public static bool HasWholeBodyConditionTarget(Pawn pawn, MedicalConditionEntry entry)
        {
            if (pawn == null || entry == null) return false;

            for (int i = 0; i < entry.Targets.Count; i++)
            {
                MedicalConditionTarget target = entry.Targets[i];
                if (target.Kind == MedicalConditionTargetKind.WholeBody &&
                    ConditionTargetMatchesPawn(pawn, target))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves currently present pawn parts that satisfy a condition entry's source-derived target alternatives.
        /// </summary>
        /// <param name="pawn">The pawn whose anatomy and current part state are evaluated.</param>
        /// <param name="entry">The condition metadata to resolve.</param>
        /// <param name="solverRelevantOnly">
        /// True to retain the current mass-capacity part filter; false to return every compatible medical target.
        /// </param>
        public static List<BodyPartRecord> GetConditionTargetParts(
            Pawn pawn,
            MedicalConditionEntry entry,
            bool solverRelevantOnly)
        {
            List<BodyPartRecord> result = new List<BodyPartRecord>();
            List<BodyPartRecord> parts = pawn?.RaceProps?.body?.AllParts;
            if (parts == null || entry == null) return result;

            SpeciesTopologyTemplate template = solverRelevantOnly
                ? TopologyLayoutCompiler.GetOrCreateTopologyTemplate(pawn.RaceProps.body)
                : null;

            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (!CanApplyConditionToPart(pawn, part)) continue;
                if (solverRelevantOnly && !entry.HasSystemicStageImpact && entry.HasPartLocalImpact &&
                    !IsSolverRelevantConditionPart(part, template)) continue;

                for (int t = 0; t < entry.Targets.Count; t++)
                {
                    MedicalConditionTarget target = entry.Targets[t];
                    if (ConditionTargetMatchesPawn(pawn, target) &&
                        ((target.Kind == MedicalConditionTargetKind.SpecificPart && target.Part == part.def) ||
                         (target.Kind == MedicalConditionTargetKind.AnyLivePart && part.coverageAbs > 0f) ||
                         (target.Kind == MedicalConditionTargetKind.InfectablePart &&
                          !part.def.IsSolid(part, pawn.health.hediffSet.hediffs))))
                    {
                        result.Add(part);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns whether a present, natural part can receive a direct condition change.
        /// </summary>
        /// <param name="pawn">The pawn whose body part is being evaluated.</param>
        /// <param name="part">The body part to check for condition applicability.</param>
        public static bool CanApplyConditionToPart(Pawn pawn, BodyPartRecord part)
        {
            HediffSet set = pawn?.health?.hediffSet;
            return set != null && part != null && pawn.RaceProps?.body != null &&
                pawn.RaceProps.body.AllParts.Contains(part) &&
                !set.PartIsMissing(part) &&
                set.GetFirstHediffMatchingPart<Hediff_AddedPart>(part) == null &&
                !set.AncestorHasDirectlyAddedParts(part);
        }

        /// <summary>
        /// Determines whether a localized condition can affect modeled mass capacity.
        /// </summary>
        /// <param name="part">The body part to evaluate.</param>
        /// <param name="template">The species topology template for the pawn.</param>
        private static bool IsSolverRelevantConditionPart(BodyPartRecord part, SpeciesTopologyTemplate template)
        {
            if (MedicalClassifier.IsCapacitySourcePart(part.def)) return true;
            List<BodyPartTagDef> tags = part.def.tags;
            if (tags != null &&
                ((TopologyLayoutCompiler.BloodFiltrationSourceTag != null && tags.Contains(TopologyLayoutCompiler.BloodFiltrationSourceTag)) ||
                 (TopologyLayoutCompiler.BloodFiltrationKidneyTag != null && tags.Contains(TopologyLayoutCompiler.BloodFiltrationKidneyTag)) ||
                 (TopologyLayoutCompiler.BloodFiltrationLiverTag != null && tags.Contains(TopologyLayoutCompiler.BloodFiltrationLiverTag))))
                return true;

            if (template == null) return false;
            PartType type = template.GetPartType(part);
            return type == PartType.CorePart || type == PartType.ManipulationPart ||
                type == PartType.MovingPart || type == PartType.DualLimb;
        }

        /// <summary>
        /// [TEST-06] Returns known target alternatives for a condition. Part
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

        /// <summary>
        /// [TEST-06] Returns source-derived targets from the broader medical catalog without requiring solver impact.
        /// </summary>
        /// <param name="hediff">The HediffDef for which to retrieve target constraints.</param>
        /// <returns>A read-only list of medical condition targets associated with the specified HediffDef.</returns>
        public static IReadOnlyList<MedicalConditionTarget> GetMedicalConditionTargetConstraints(HediffDef hediff)
        {
            MedicalConditionEntry entry;
            return TryGetMedicalConditionEntry(hediff, out entry) ? entry.Targets : emptyConditionTargets;
        }

        #endregion
    }
}
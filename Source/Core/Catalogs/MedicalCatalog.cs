using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [CORE-CAT-01] STATIC MEDICAL CATALOG
    /// Pre-indexes surgeries and implants, plus categorized condition definitions with source-derived target alternatives.
    /// Provides indexed query tables for the interactive medical planner and diagnostic census tools.
    /// Resides under Source/Core/Catalogs/.
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

        /// <summary>[CORE-CAT-01] One fully built catalog generation, published with a single volatile reference write.</summary>
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
        /// [CORE-CAT-01] Ensures the medical surgery and condition catalog has been indexed.
        /// Idempotent on-demand initializer. The first build must occur on the main thread.
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

        #region 3. [CORE-CAT-03] CONDITION SEVERITY & LABEL API

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

        #region 4. PUBLIC O(1) QUERY API

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

        #region 5. [CORE-CAT-03] PUBLIC CONDITION QUERY API

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
        /// [CORE-CAT-03] Returns known target alternatives for a condition. Part
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
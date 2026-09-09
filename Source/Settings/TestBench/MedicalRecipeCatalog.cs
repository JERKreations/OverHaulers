using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [TEST-04] RECIPE CATALOG ENTRY MODELS

    /// <summary>
    /// [TEST-04] Immutable descriptor representing an indexed surgery recipe or medical procedure.
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

    /// <summary>
    /// [TEST-04] STATIC MEDICAL RECIPE & AUGMENTATION CATALOG
    /// Pre-indexes all loaded vanilla and modded surgery recipes, artificial parts, implants, combat stimulants,
    /// and whole-body staged conditions (pregnancies, food poisoning, hangovers, withdrawals, mechanites).
    /// Provides zero-allocation $O(1)$ query tables for the interactive medical planner and diagnostic census tools.
    /// Resides under Source/Settings/TestBench/.
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

        private static bool isInitialized = false;
        private static readonly object catalogLock = new object();

        #endregion

        #region 3. CATALOG BOOTSTRAPPING & PRE-INDEXING

        /// <summary>
        /// [TEST-04] Scans DefDatabase and pre-indexes all medical surgery recipes, artificial parts, drugs, stimulants, and staged conditions.
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
                systemicDrugsAndStimulants.Clear();

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

                // =========================================================================
                // TIER 3: DISCOVER INGESTIBLE DRUG HIGHS & CONSUMABLES
                // =========================================================================
                HashSet<HediffDef> ingestibleDrugHediffs = new HashSet<HediffDef>(64);
                List<ThingDef> allThings = DefDatabase<ThingDef>.AllDefsListForReading;
                if (allThings != null)
                {
                    for (int i = 0; i < allThings.Count; i++)
                    {
                        ThingDef thing = allThings[i];
                        if (thing.ingestible?.outcomeDoers != null)
                        {
                            for (int d = 0; d < thing.ingestible.outcomeDoers.Count; d++)
                            {
                                if (thing.ingestible.outcomeDoers[d] is IngestionOutcomeDoer_GiveHediff giveHediff && giveHediff.hediffDef != null)
                                {
                                    ingestibleDrugHediffs.Add(giveHediff.hediffDef);
                                }
                            }
                        }
                    }
                }

                foreach (HediffDef drugHediff in ingestibleDrugHediffs)
                {
                    if (chemicalTolerances.Contains(drugHediff)) continue;
                    if (surgicalHediffs.Contains(drugHediff)) continue;

                    if (!systemicDrugsAndStimulants.Contains(drugHediff))
                    {
                        systemicDrugsAndStimulants.Add(drugHediff);
                    }
                }

                // =========================================================================
                // TIER 4: DISCOVER WHOLE-BODY CONDITIONS, STAGED WITHDRAWALS & MECHANITES
                // =========================================================================
                List<HediffDef> allHediffs = DefDatabase<HediffDef>.AllDefsListForReading;
                if (allHediffs != null)
                {
                    for (int i = 0; i < allHediffs.Count; i++)
                    {
                        HediffDef h = allHediffs[i];
                        if (surgicalHediffs.Contains(h)) continue;
                        if (chemicalTolerances.Contains(h)) continue;
                        if (ingestibleDrugHediffs.Contains(h)) continue;

                        // Reject localized replacements, implants, physical injuries, and missing parts
                        if (typeof(Hediff_AddedPart).IsAssignableFrom(h.hediffClass) ||
                            typeof(Hediff_Implant).IsAssignableFrom(h.hediffClass) ||
                            typeof(Hediff_Injury).IsAssignableFrom(h.hediffClass) ||
                            typeof(Hediff_MissingPart).IsAssignableFrom(h.hediffClass))
                        {
                            continue;
                        }

                        // Filter out passive tolerance counters
                        if (h.defName.IndexOf("Tolerance", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        // Evaluate whether this whole-body condition has any stage modifying athletic capacities
                        bool affectsAthletics = MedicalClassifier.EvaluateHediffCapacities(h).AffectsAthletics;
                        if (affectsAthletics || h == HediffDefOf.Anesthetic || typeof(Hediff_Pregnant).IsAssignableFrom(h.hediffClass))
                        {
                            if (!systemicDrugsAndStimulants.Contains(h))
                            {
                                systemicDrugsAndStimulants.Add(h);
                            }
                        }
                    }
                }

                // Alphabetical sort for clean UI presentation
                systemicDrugsAndStimulants.Sort((a, b) => string.Compare(GetFormattedConditionLabel(a), GetFormattedConditionLabel(b), StringComparison.OrdinalIgnoreCase));

                watch.Stop();
                isInitialized = true;

                OHLog.Integration.MedicalCatalogInitialized(
                    (float)watch.Elapsed.TotalMilliseconds,
                    totalReplacementRecipes,
                    totalImplantRecipes,
                    systemicDrugsAndStimulants.Count
                );
            }
        }

        /// <summary>
        /// [TEST-04] Forces a complete wipe and re-index of all surgery recipes, artificial parts, implants, and systemic conditions.
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
        /// Strictly caps severity to 99% of lethal thresholds (def.lethalSeverity * 0.99) and skips
        /// zero-consciousness fatal stages to prevent triggering pawn death in the sandbox.
        /// </summary>
        /// <param name="def">The target hediff definition.</param>
        /// <param name="stageSuffix">Outputs a human-readable stage suffix (e.g. "Extreme", "Major", "Withdrawal").</param>
        /// <returns>The safe target severity float value to assign to the simulated hediff.</returns>
        public static float GetPeakImpactSeverity(HediffDef def, out string stageSuffix)
        {
            stageSuffix = null;
            if (def == null) return 0.5f;

            // BREAKPOINT ANCHOR: Absolute Lethal Ceiling (99% of lethalSeverity)
            float safeLethalCeiling = def.lethalSeverity > 0f ? (def.lethalSeverity * 0.99f) : float.MaxValue;

            if (def.stages == null || def.stages.Count == 0)
            {
                float baseSeverity = def.initialSeverity > 0f ? def.initialSeverity : 0.5f;
                return Mathf.Min(baseSeverity, safeLethalCeiling);
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
                if (def.lethalSeverity > 0f && stage.minSeverity >= def.lethalSeverity)
                {
                    continue;
                }

                float stageScore = 0f;

                if (stage.capMods != null)
                {
                    for (int c = 0; c < stage.capMods.Count; c++)
                    {
                        PawnCapacityModifier capMod = stage.capMods[c];
                        if (capMod != null && MedicalClassifier.IsAthleticCapacity(capMod.capacity))
                        {
                            stageScore += Math.Abs(capMod.offset);
                            if (capMod.postFactor != 1.0f) stageScore += Math.Abs(1.0f - capMod.postFactor);
                            if (capMod.setMax < 1.0f) stageScore += Math.Abs(1.0f - capMod.setMax) * 2f;
                        }
                    }
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
                float baseSeverity = def.initialSeverity > 0f ? def.initialSeverity : 0.5f;
                return Mathf.Min(baseSeverity, safeLethalCeiling);
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
                if (def.lethalSeverity > 0f)
                {
                    targetSeverity = def.lethalSeverity * 0.99f;
                }
                else if (def.maxSeverity > 0f)
                {
                    targetSeverity = bestStage.minSeverity + ((def.maxSeverity - bestStage.minSeverity) * 0.95f);
                }
                else
                {
                    targetSeverity = bestStage.minSeverity + 0.10f;
                }
            }

            // Final safety clamp: never exceed 99% of lethal threshold
            targetSeverity = Mathf.Min(targetSeverity, safeLethalCeiling);

            if (def.maxSeverity > 0f)
            {
                targetSeverity = Mathf.Min(targetSeverity, def.maxSeverity);
            }

            return Mathf.Max(targetSeverity, bestStage.minSeverity);
        }

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

        #endregion

        #region 5. PUBLIC O(1) QUERY API

        /// <summary>
        /// Returns true if any replacement or implant surgeries are available for this BodyPartDef.
        /// </summary>
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
        /// Retrieves the list of all verified systemic drugs, stimulants, depressants, sedatives, and whole-body conditions.
        /// </summary>
        public static List<HediffDef> GetSystemicDrugs()
        {
            if (!isInitialized) InitializeCatalog();
            return systemicDrugsAndStimulants;
        }

        #endregion
    }
}
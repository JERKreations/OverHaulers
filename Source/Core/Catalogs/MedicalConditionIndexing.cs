using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>[CORE-CAT-03] Builds condition indexes from loaded definition metadata.</summary>
    internal static partial class MedicalConditionIndexing
    {
        #region 1. CONDITION INDEX MODELS

        /// <summary>[CORE-CAT-03] Represents an immutable published set of condition indexes.</summary>
        internal sealed class ConditionCatalogSnapshot
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

        #region 2. [CORE-CAT-03] CONDITION INDEXING

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
        internal static ConditionCatalogSnapshot Build(
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

                    if (hediff == HediffDefOf.WoundInfection || hediff.defName == "ScariaInfection")
                    {
                        builder.Sources |= MedicalConditionSource.WoundInfection;
                        AddConditionTarget(builder, MedicalConditionTargetKind.InfectablePart, null, MedicalConditionSource.WoundInfection);
                    }
                    builders.Add(hediff, builder);
                }
            }

            DiscoverConditionTargets(builders, allHediffs);

            // BREAKPOINT ANCHOR: Duplicate Label Disambiguation Pre-Scan
            Dictionary<string, int> labelFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<HediffDef, MedicalConditionBuilder> pair in builders)
            {
                string rawLabel = MedicalConditionSeverity.GetFormattedConditionDefLabel(pair.Key);
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

                string baseLabel = MedicalConditionSeverity.GetFormattedConditionDefLabel(builder.Hediff);
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
                        kind == MedicalConditionTargetKind.AnyLivePart ||
                        kind == MedicalConditionTargetKind.InfectablePart)
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

        #endregion
    }
}

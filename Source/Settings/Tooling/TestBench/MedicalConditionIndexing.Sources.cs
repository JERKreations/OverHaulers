using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>[TEST-06] Discovers source-derived condition target alternatives.</summary>
    internal static partial class MedicalConditionIndexing
    {
        #region 1. [TEST-06] SOURCE DISCOVERY

        /// <summary>
        /// Discovers condition targets for all relevant definitions, including chemicals, ingestibles, apparel, and species-specific sources.
        /// </summary>
        /// <param name="builders">A dictionary mapping HediffDefs to their corresponding medical condition builders.</param>
        /// <param name="allHediffs">A list of all HediffDefs to consider.</param>
        private static void DiscoverConditionTargets(
            Dictionary<HediffDef, MedicalConditionBuilder> builders,
            List<HediffDef> allHediffs)
        {
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

        #endregion
    }
}

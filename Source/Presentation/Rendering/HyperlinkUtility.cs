using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-04] Shared utility resolving clickable InfoCard hyperlinks for artificial body parts and implants.
    /// Single source of truth shared between StatWorker and StatPart implementations.
    /// </summary>
    public static class HyperlinkUtility
    {
        /// <summary>
        /// Pooled deduplicator used to avoid generating duplicate hyperlinks for the same ThingDef within a single resolution pass.
        /// </summary>
        private static readonly HashSet<ThingDef> pooledDeduplicator = new HashSet<ThingDef>(256);

        /// <summary>
        /// Resolves an allocation-free sequence of InfoCard hyperlinks for installed bionics and implants on the specified pawn, avoiding duplicate
        ///  hyperlinks within a single resolution pass.
        /// </summary>
        /// <param name="req">The StatRequest containing the pawn for which to resolve hyperlinks.</param>
        /// <returns>An enumerable sequence of InfoCard hyperlinks corresponding to the installed bionics and implants on the specified pawn.</returns>
        /// <remarks>
        /// This method ensures that each ThingDef corresponding to an installed bionic or implant is only represented once in the returned sequence
        ///  of hyperlinks.
        /// </remarks>
        public static IEnumerable<Dialog_InfoCard.Hyperlink> ResolveHyperlinks(StatRequest req)
        {
            if (!req.HasThing || !(req.Thing is Pawn pawn) || !PawnDataRegistry.CanCarryCaravanMass(pawn) || pawn.health?.hediffSet == null)
            {
                return Array.Empty<Dialog_InfoCard.Hyperlink>();
            }

            float baselineCapacity = PawnDataRegistry.ResolveBaseline(pawn);
            MassCapacityModel detailedMassModel = PawnDataRegistry.GetDetailedModel(pawn, baselineCapacity);
            if (detailedMassModel == null)
            {
                return Array.Empty<Dialog_InfoCard.Hyperlink>();
            }

            List<Hediff> activeHediffs = pawn.health.hediffSet.hediffs;
            List<Dialog_InfoCard.Hyperlink> hyperlinks = new List<Dialog_InfoCard.Hyperlink>();

            SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(pawn.RaceProps.body);

            pooledDeduplicator.Clear();
            try
            {
                for (int i = 0; i < activeHediffs.Count; i++)
                {
                    Hediff hediff = activeHediffs[i];
                    if (hediff != null && hediff.Part != null && (hediff is Hediff_AddedPart || hediff is Hediff_Implant))
                    {
                        ThingDef spawnDef = hediff.def?.spawnThingOnRemoved;
                        if (spawnDef != null && template != null)
                        {
                            int partIndex = template.GetPartIndex(hediff.Part);
                            PartViewNode partModel = partIndex != -1 ? detailedMassModel.GetNodeForPartIndex(partIndex) : null;
                            if (partModel != null && (partModel.IsOrgan || Math.Abs(partModel.TotalOffset) >= SettingsDefaults.EfficiencyEpsilon))
                            {
                                if (pooledDeduplicator.Add(spawnDef))
                                {
                                    hyperlinks.Add(new Dialog_InfoCard.Hyperlink(spawnDef));
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                pooledDeduplicator.Clear();
            }

            return hyperlinks;
        }
    }
}
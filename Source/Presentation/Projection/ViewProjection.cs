using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [VIEW-01 to VIEW-04] VIEW PROJECTION COMPILER
    /// Stateless view projection compiler translating mathematical workspace vectors into passive visual node hierarchies.
    /// Operates on IAnatomicalDataSource with zero transient heap allocations on the main thread via O(1) direct array access and pooled view nodes.
    /// </summary>
    public static class ViewProjection
    {
        #region 1. STATIC SCRATCH POOLS & PRESENTATION ORDER (Main Thread Only)

        [ThreadStatic]
        private static bool isProjectingView;

        private static readonly PartType[] PresentationGroupOrder = new PartType[5]
        {
            PartType.HeadPart,
            PartType.CorePart,
            PartType.ManipulationPart,
            PartType.MovingPart,
            PartType.DualLimb
        };

        private static readonly List<PawnCapacityUtility.CapacityImpactor> pooledConsciousnessImpactors = 
            new List<PawnCapacityUtility.CapacityImpactor>(16);

        private static readonly List<BodyPartRecord>[] pooledCategorizedParts = new List<BodyPartRecord>[6]
        {
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32)
        };

        private static readonly List<PartViewNode>[] pooledGroupSubModels = new List<PartViewNode>[6]
        {
            new List<PartViewNode>(32),
            new List<PartViewNode>(32),
            new List<PartViewNode>(32),
            new List<PartViewNode>(32),
            new List<PartViewNode>(32),
            new List<PartViewNode>(32)
        };

        private static readonly Dictionary<BodyPartRecord, PartViewNode> pooledRecordToModel = 
            new Dictionary<BodyPartRecord, PartViewNode>(64);

        private static readonly List<PartViewNode> pooledRootModels = 
            new List<PartViewNode>(64);

        #endregion

        #region 2. PRIMARY PROJECTION ORCHESTRATORS

        /// <summary>
        /// Projects the compiled anatomical workspace into a passive visual node hierarchy for UI rendering.
        /// </summary>
        public static void RebuildDetailedModel_Internal(
            IAnatomicalDataSource source,
            MassCapacityModel massModel,
            float solvedOffset,
            float biologicalBaseline,
            AnatomicalWorkspace workspace)
        {
            if (!UnityData.IsInMainThread || massModel == null) return;
            if (source != null && !source.IsValidBiologicalState) return;

            if (isProjectingView)
            {
                return;
            }

            try
            {
                isProjectingView = true;

                massModel.Offset = solvedOffset;

                BodyDef bodyDef = source?.BodyDef ?? workspace?.GetPartRecord(0)?.body ?? BodyDefOf.Human;

                SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(bodyDef);
                if (template == null) return;

                // Snapshot Biological Capacities
                BiologicalCapacitySnapshot capacitySnapshot;
                if (source != null)
                {
                    capacitySnapshot.Breathing = source.GetCapacityLevel(PawnCapacityDefOf.Breathing);
                    capacitySnapshot.BloodPumping = source.GetCapacityLevel(PawnCapacityDefOf.BloodPumping);
                    capacitySnapshot.Moving = source.GetCapacityLevel(PawnCapacityDefOf.Moving);
                    capacitySnapshot.Manipulation = source.GetCapacityLevel(PawnCapacityDefOf.Manipulation);
                    capacitySnapshot.Consciousness = source.GetCapacityLevel(PawnCapacityDefOf.Consciousness);
                }
                else
                {
                    capacitySnapshot.Breathing = massModel.CapacityLevels[0] > 0f ? massModel.CapacityLevels[0] : 1.0f;
                    capacitySnapshot.BloodPumping = massModel.CapacityLevels[1] > 0f ? massModel.CapacityLevels[1] : 1.0f;
                    capacitySnapshot.Moving = massModel.CapacityLevels[2] > 0f ? massModel.CapacityLevels[2] : 1.0f;
                    capacitySnapshot.Manipulation = massModel.CapacityLevels[3] > 0f ? massModel.CapacityLevels[3] : 1.0f;
                    capacitySnapshot.Consciousness = massModel.CapacityLevels[4] > 0f ? massModel.CapacityLevels[4] : 1.0f;
                }

                PartCounts partCounts = template.Counts;
                SystemicEvaluationContext context = SystemicEvaluationContext.CreateFromSnapshot(
                    biologicalBaseline, partCounts, OverHaulers.settings, capacitySnapshot);

                massModel.CapacityLevels[0] = capacitySnapshot.Breathing;
                massModel.CapacityLevels[1] = capacitySnapshot.BloodPumping;
                massModel.CapacityLevels[2] = capacitySnapshot.Moving;
                massModel.CapacityLevels[3] = capacitySnapshot.Manipulation;
                massModel.CapacityLevels[4] = capacitySnapshot.Consciousness;

                for (int i = 0; i < 6; i++)
                {
                    pooledCategorizedParts[i].Clear();
                    pooledGroupSubModels[i].Clear();
                }

                CategorizePartsFast(template, pooledCategorizedParts);

                massModel.ClearAilments();
                massModel.EvaluatedParts.Clear();

                if (workspace != null)
                {
                    massModel.AddAilmentsRangeUnique(workspace.SystemicAilments);
                }

                // [VIEW-04.A] Build Structural Sub-Models in Top-Down Anatomical Order
                for (int g = 0; g < PresentationGroupOrder.Length; g++)
                {
                    int typeIndex = (int)PresentationGroupOrder[g];
                    var partList = pooledCategorizedParts[typeIndex];

                    for (int i = 0; i < partList.Count; i++)
                    {
                        BodyPartRecord part = partList[i];
                        if (TopologyLayoutCompiler.TryGetWeightBudget(part, bodyDef, context, out float budget))
                        {
                            PartViewNode partModel = CreatePartViewNode(part, budget, part.LabelCap.ToString(), false, workspace, massModel);
                            pooledGroupSubModels[typeIndex].Add(partModel);
                        }
                    }
                }

                // [VIEW-04.B] Attach Metabolic Organs and Virtual Anchors to Ancestral Bones
                var organList = pooledCategorizedParts[(int)PartType.None];
                for (int i = 0; i < organList.Count; i++)
                {
                    BodyPartRecord part = organList[i];
                    bool isOrgan = MedicalClassifier.IsMetabolicOrgan(part.def);
                    PartViewNode nonStructuralModel = CreatePartViewNode(part, 0f, part.LabelCap.ToString(), isOrgan, workspace, massModel);

                    int partIndex = template.GetPartIndex(part);
                    int ancestorIndex = partIndex != -1 ? template.NearestStructuralAncestor[partIndex] : -1;
                    PartViewNode ancestorModel = ancestorIndex != -1 ? massModel.GetNodeForPartIndex(ancestorIndex) : null;

                    if (ancestorModel != null)
                    {
                        ancestorModel.AddSubPart(nonStructuralModel);
                    }
                    else
                    {
                        massModel.EvaluatedParts.Add(nonStructuralModel);
                    }
                }

                // [VIEW-04.C] Consolidate & Nest Visual Group Wrappers
                for (int g = 0; g < PresentationGroupOrder.Length; g++)
                {
                    int typeIndex = (int)PresentationGroupOrder[g];
                    ProcessAndAddGroupWrapper(pooledGroupSubModels[typeIndex], (PartType)typeIndex, context, massModel, template);
                }

                massModel.TotalMultiplier = biologicalBaseline > 0f ? ((biologicalBaseline + massModel.Offset) / biologicalBaseline) : 0f;
                if (massModel.TotalMultiplier < 0f) massModel.TotalMultiplier = 0f;

                bool verbose = OverHaulers.settings?.verboseBreakdown ?? false;
                massModel.Explanation = ReportFormatter.BuildExplanation(massModel, verbose, biologicalBaseline);
            }
            catch (Exception ex)
            {
                string targetName = source?.EntityLabel ?? "Unknown";
                OHLog.Presentation.WarnException($"ViewProjection:{targetName}", ex);
            }
            finally
            {
                ClearScratchBuffers();
                isProjectingView = false;
            }
        }

        /// <summary>
        /// Convenience overload projecting view models directly for a live RimWorld Pawn.
        /// </summary>
        public static void RebuildDetailedModel_Internal(
            Pawn pawn,
            MassCapacityModel massModel,
            float solvedOffset,
            float biologicalBaseline,
            AnatomicalWorkspace workspace)
        {
            if (pawn == null) return;
            RebuildDetailedModel_Internal(new PawnAnatomicalSource(pawn), massModel, solvedOffset, biologicalBaseline, workspace);
        }

        #endregion

        #region 3. GROUP CONSOLIDATION & NESTING HELPERS

        private static void ProcessAndAddGroupWrapper(
            List<PartViewNode> subModels,
            PartType type,
            in SystemicEvaluationContext context,
            MassCapacityModel massModel,
            SpeciesTopologyTemplate template)
        {
            PartViewNode wrapper = ConsolidateGroup(subModels, type, context, massModel, template);
            if (wrapper != null)
            {
                NestModelsInPlace(wrapper.SubParts, template);
                massModel.EvaluatedParts.Add(wrapper);
            }
        }

        private static string GetGroupLabelCap(PartType type, SpeciesTopologyTemplate template)
        {
            switch (type)
            {
                case PartType.HeadPart:
                    return template?.HeadPart != null 
                        ? template.HeadPart.LabelCap.ToString() 
                        : "OverHaulers_GroupFallbackHead".Translate().ToString();

                case PartType.CorePart:
                    return template?.CorePart != null 
                        ? template.CorePart.LabelCap.ToString() 
                        : "OverHaulers_GroupFallbackTorso".Translate().ToString();

                case PartType.ManipulationPart:
                    return template?.PrimaryManipulationDef != null 
                        ? template.PrimaryManipulationDef.LabelCap.ToString() 
                        : PawnCapacityDefOf.Manipulation.LabelCap.ToString();

                case PartType.MovingPart:
                    return template?.PrimaryMovingDef != null 
                        ? template.PrimaryMovingDef.LabelCap.ToString() 
                        : PawnCapacityDefOf.Moving.LabelCap.ToString();

                case PartType.DualLimb:
                    return template?.PrimaryDualDef != null 
                        ? template.PrimaryDualDef.LabelCap.ToString() 
                        : "OverHaulers_GroupDualLimbs".Translate().ToString();

                default:
                    return string.Empty;
            }
        }

        private static PartViewNode ConsolidateGroup(
            List<PartViewNode> subModels,
            PartType type,
            in SystemicEvaluationContext context,
            MassCapacityModel massModel,
            SpeciesTopologyTemplate template)
        {
            if (subModels == null || subModels.Count == 0) return null;

            float totalProstheticOffset = 0f;
            float totalAthleticOffset = 0f;
            float totalHealthOffset = 0f;

            float cumulativeWeightedEfficiency = 0f;
            float totalRelativeWeight = 0f;
            bool containsProstheticComponent = false;
            float highestProstheticEfficiency = 0f;
            string dominantProstheticName = string.Empty;
            Color dominantProstheticColor = Color.white;

            float healthSum = 0f;
            int healthCount = 0;

            for (int i = 0; i < subModels.Count; i++)
            {
                var subModel = subModels[i];
                float subPartWeight = subModel.ProportionalWeight;

                // Offsets are summed (they will safely be 0f for neutralized parts anyway)
                totalProstheticOffset += subModel.ProstheticOffset;
                totalAthleticOffset += subModel.AthleticOffset;
                totalHealthOffset += subModel.HealthOffset;

                // Explicitly ignore neutralized anchor bones from dragging down group averages
                if (subModel.IsNeutralized)
                {
                    continue;
                }

                float effectiveEfficiency;
                if (subModel.IsMissing)
                {
                    effectiveEfficiency = 0f;
                }
                else if (subModel.HasProsthetic)
                {
                    effectiveEfficiency = subModel.EfficiencyRating;
                    containsProstheticComponent = true;
                    
                    if (subModel.EfficiencyRating > highestProstheticEfficiency)
                    {
                        highestProstheticEfficiency = subModel.EfficiencyRating;
                        dominantProstheticName = subModel.ProstheticName;
                        dominantProstheticColor = subModel.ProstheticColor;
                    }
                }
                else
                {
                    float healthLoss = 1.0f - subModel.HealthFraction;
                    float athleticDifference = context.AthleticMultiplier - 1.0f;
                    effectiveEfficiency = 1.0f + athleticDifference - healthLoss;
                }

                cumulativeWeightedEfficiency += effectiveEfficiency * subPartWeight;
                totalRelativeWeight += subPartWeight;
                
                healthSum += subModel.HealthFraction;
                healthCount++;
            }

            float consolidatedEfficiency = totalRelativeWeight > 0f ? (cumulativeWeightedEfficiency / totalRelativeWeight) : 1.0f;
            float averagePartHealth = healthCount > 0 ? (healthSum / healthCount) : 1.0f;

            string groupName = GetGroupLabelCap(type, template);
            string label = "OverHaulers_GroupDefName".Translate(groupName).ToString();

            PartViewNode consolidatedWrapper = massModel.AcquireNode();
            consolidatedWrapper.Label = label;
            consolidatedWrapper.IsOrgan = false;
            consolidatedWrapper.IsMissing = false;
            consolidatedWrapper.HealthFraction = averagePartHealth;
            consolidatedWrapper.HasProsthetic = containsProstheticComponent;
            consolidatedWrapper.ProstheticName = dominantProstheticName;
            consolidatedWrapper.EfficiencyRating = consolidatedEfficiency;
            consolidatedWrapper.ProstheticColor = dominantProstheticColor;
            consolidatedWrapper.ProstheticOffset = totalProstheticOffset;
            consolidatedWrapper.AthleticOffset = totalAthleticOffset;
            consolidatedWrapper.HealthOffset = totalHealthOffset;
            consolidatedWrapper.TotalOffset = totalProstheticOffset + totalAthleticOffset + totalHealthOffset;

            consolidatedWrapper.SubParts.Clear();
            for (int i = 0; i < subModels.Count; i++)
            {
                consolidatedWrapper.AddSubPart(subModels[i]);
            }

            return consolidatedWrapper;
        }

        private static void NestModelsInPlace(List<PartViewNode> flatModelList, SpeciesTopologyTemplate template)
        {
            if (flatModelList == null || flatModelList.Count == 0 || template == null) return;

            pooledRecordToModel.Clear();
            pooledRootModels.Clear();

            try
            {
                for (int i = 0; i < flatModelList.Count; i++)
                {
                    var model = flatModelList[i];
                    if (model.Record != null)
                    {
                        pooledRecordToModel[model.Record] = model;
                    }
                }

                for (int i = 0; i < flatModelList.Count; i++)
                {
                    var model = flatModelList[i];
                    
                    int index = template.GetPartIndex(model.Record);
                    if (index == -1)
                    {
                        pooledRootModels.Add(model);
                        continue;
                    }

                    PartType type = template.PartTypes[index];
                    
                    // BREAKPOINT ANCHOR: Flat 1D Stride Ancestral Jump Lookup
                    int candidateIndex = template.GetNearestAncestor(index, type);
                    bool hasBeenNested = false;

                    while (candidateIndex != -1)
                    {
                        BodyPartRecord candidateRecord = template.IndexedParts[candidateIndex];
                        if (pooledRecordToModel.TryGetValue(candidateRecord, out PartViewNode parentModel))
                        {
                            parentModel.AddSubPart(model);
                            hasBeenNested = true;
                            break;
                        }
                        candidateIndex = template.GetNearestAncestor(candidateIndex, type);
                    }

                    if (!hasBeenNested)
                    {
                        pooledRootModels.Add(model);
                    }
                }

                flatModelList.Clear();
                flatModelList.AddRange(pooledRootModels);
            }
            finally
            {
                pooledRecordToModel.Clear();
                pooledRootModels.Clear();
            }
        }

        #endregion

        #region 4. LOW-LEVEL FACTORIES & CATEGORIZERS

        private static PartViewNode CreatePartViewNode(
            BodyPartRecord part, 
            float proportionalWeight, 
            string partLabel, 
            bool isOrganSegment, 
            AnatomicalWorkspace workspace,
            MassCapacityModel massModel)
        {
            PartViewNode partViewNode = massModel.AcquireNode();
            partViewNode.Record = part;
            partViewNode.Label = partLabel;
            partViewNode.IsOrgan = isOrganSegment;
            partViewNode.ProportionalWeight = proportionalWeight;

            int index = workspace != null ? workspace.GetPartIndex(part) : -1;
            if (index != -1)
            {
                massModel.SetNodeForPartIndex(index, partViewNode);

                ref PartStateCold coldState = ref workspace.PartStatesColdArray[index];
                
                partViewNode.IsMissing = workspace.HasFlag(index, PartFlags.IsMissing);
                partViewNode.IsNeutralized = workspace.HasFlag(index, PartFlags.IsNeutralized);
                partViewNode.HealthFraction = workspace.HealthFractions[index];
                partViewNode.LocalAilmentName = coldState.LocalAilmentName;
                partViewNode.LocalAilmentColor = coldState.LocalAilmentColor;

                if (workspace.HasFlag(index, PartFlags.HasAddedPart))
                {
                    partViewNode.HasProsthetic = true;
                    partViewNode.ProstheticName = coldState.ProstheticName;
                    partViewNode.EfficiencyRating = workspace.EfficiencyRatings[index];
                    partViewNode.ProstheticColor = coldState.ProstheticColor;
                    partViewNode.IsInherited = workspace.HasFlag(index, PartFlags.ProstheticIsInherited);
                }
                else
                {
                    partViewNode.EfficiencyRating = workspace.HealthFractions[index];
                }

                if (workspace.HasFlag(index, PartFlags.HasAthleticImplant))
                {
                    partViewNode.AthleticImplantName = coldState.AthleticImplantName;
                    partViewNode.AthleticImplantColor = coldState.AthleticImplantColor;
                }

                partViewNode.ProstheticOffset = workspace.CalculatedProsthetics[index];
                partViewNode.AthleticOffset = workspace.CalculatedAthletics[index];
                partViewNode.HealthOffset = workspace.CalculatedHealths[index];
                partViewNode.TotalOffset = workspace.CalculatedTotals[index];
            }
            else
            {
                partViewNode.HealthFraction = 1.0f;
                partViewNode.EfficiencyRating = 1.0f;
                partViewNode.ProstheticOffset = 0f;
                partViewNode.AthleticOffset = 0f;
                partViewNode.HealthOffset = 0f;
                partViewNode.TotalOffset = 0f;
            }

            return partViewNode;
        }

        private static void CategorizePartsFast(
            SpeciesTopologyTemplate template, 
            List<BodyPartRecord>[] categorizedParts)
        {
            if (template == null) return;

            int partCount = template.PartCount;
            BodyPartRecord[] indexedParts = template.IndexedParts;
            PartType[] partTypes = template.PartTypes;

            for (int i = 0; i < partCount; i++)
            {
                BodyPartRecord bodyPart = indexedParts[i];
                if (bodyPart?.def == null) continue;

                PartType type = partTypes[i];
                if (type != PartType.None)
                {
                    categorizedParts[(int)type].Add(bodyPart);
                }
                else
                {
                    // Captures both metabolic organs and virtual anchors (e.g. Waist) for full visual tree fidelity
                    categorizedParts[(int)PartType.None].Add(bodyPart);
                }
            }
        }

        private static void ClearScratchBuffers()
        {
            for (int i = 0; i < 6; i++)
            {
                pooledCategorizedParts[i].Clear();
                pooledGroupSubModels[i].Clear();
            }
            pooledConsciousnessImpactors.Clear();
            pooledRecordToModel.Clear();
            pooledRootModels.Clear();
        }

        #endregion
    }
}
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

        /// <summary>
        /// Defines the order in which body parts are presented in the view projection.
        /// This order determines the sequence in which parts are visually arranged in the compiled view model.
        /// </summary>
        private static readonly PartType[] PresentationGroupOrder = new PartType[5]
        {
            PartType.HeadPart,
            PartType.CorePart,
            PartType.ManipulationPart,
            PartType.MovingPart,
            PartType.DualLimb
        };

        /// <summary>
        /// Pooled list of consciousness impactors used during view projection to minimize heap allocations.
        /// </summary>
        private static readonly List<PawnCapacityUtility.CapacityImpactor> pooledConsciousnessImpactors = 
            new List<PawnCapacityUtility.CapacityImpactor>(16);

        /// <summary>
        /// Pooled categorized body parts used during view projection to minimize heap allocations.
        /// </summary>
        private static readonly List<BodyPartRecord>[] pooledCategorizedParts = new List<BodyPartRecord>[6]
        {
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32),
            new List<BodyPartRecord>(32)
        };

        /// <summary>
        /// Pooled group sub-models used during view projection to minimize heap allocations.
        /// </summary>
        private static readonly List<PartViewNode>[] pooledGroupSubModels = new List<PartViewNode>[6]
        {
            new List<PartViewNode>(32),
            new List<PartViewNode>(32),
            new List<PartViewNode>(32),
            new List<PartViewNode>(32),
            new List<PartViewNode>(32),
            new List<PartViewNode>(32)
        };

        /// <summary>
        /// Pooled mapping from body part records to their corresponding view nodes used during view projection to minimize heap allocations.
        /// </summary>
        private static readonly Dictionary<BodyPartRecord, PartViewNode> pooledRecordToModel = 
            new Dictionary<BodyPartRecord, PartViewNode>(64);

        /// <summary>
        /// Pooled list of root view nodes used during view projection to minimize heap allocations.
        /// </summary>
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
                // Begin the view projection process by marking the system as actively projecting.
                isProjectingView = true;

                massModel.Offset = solvedOffset;

                BodyDef bodyDef = source?.BodyDef ?? workspace?.GetPartRecord(0)?.body ?? BodyDefOf.Human;

                SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(bodyDef);
                if (template == null) return;

                // Snapshot Biological Capacities
                // This captures the current state of the pawn's biological capacities for use in the view projection.
                BiologicalCapacitySnapshot capacitySnapshot;

                // Initialize the biological capacity snapshot with default values.
                if (source != null)
                {
                    capacitySnapshot.Breathing = source.GetCapacityLevel(PawnCapacityDefOf.Breathing);
                    capacitySnapshot.BloodPumping = source.GetCapacityLevel(PawnCapacityDefOf.BloodPumping);
                    capacitySnapshot.Moving = source.GetCapacityLevel(PawnCapacityDefOf.Moving);
                    capacitySnapshot.Manipulation = source.GetCapacityLevel(PawnCapacityDefOf.Manipulation);
                    capacitySnapshot.Consciousness = source.GetCapacityLevel(PawnCapacityDefOf.Consciousness);
                }
                // If no source is available, initialize the biological capacity snapshot with values derived from the mass model.
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

                // Update the mass model's capacity levels with the captured biological capacity snapshot.
                for (int i = 0; i < 6; i++)
                {
                    pooledCategorizedParts[i].Clear();
                    pooledGroupSubModels[i].Clear();
                }

                CategorizePartsFast(template, pooledCategorizedParts);

                // Clear any previously recorded ailments and evaluated parts from the mass model.
                massModel.ClearAilments();
                massModel.EvaluatedParts.Clear();

                // Add systemic ailments from the workspace to the mass model, ensuring uniqueness.
                if (workspace != null)
                {
                    massModel.AddAilmentsRangeUnique(workspace.SystemicAilments);
                }

                // [VIEW-04.A] Build Structural Sub-Models in Top-Down Anatomical Order
                for (int g = 0; g < PresentationGroupOrder.Length; g++)
                {
                    int typeIndex = (int)PresentationGroupOrder[g];
                    var partList = pooledCategorizedParts[typeIndex];

                    // Iterate through each part in the current category and create corresponding view nodes if applicable.
                    for (int i = 0; i < partList.Count; i++)
                    {
                        BodyPartRecord part = partList[i];
                        // Attempt to retrieve the weight budget for the current part. If successful, create a corresponding view node.
                        if (TopologyLayoutCompiler.TryGetWeightBudget(part, bodyDef, context, out float budget))
                        {
                            PartViewNode partModel = CreatePartViewNode(part, budget, part.LabelCap.ToString(), false, workspace, massModel);
                            pooledGroupSubModels[typeIndex].Add(partModel);
                        }
                    }
                }

                // [VIEW-04.B] Attach Metabolic Organs and Virtual Anchors to Ancestral Bones
                // Retrieve the list of non-structural parts, typically metabolic organs and virtual anchors.
                // For each non-structural part, create a corresponding view node and attach it to its nearest structural ancestor if available.
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
                // Iterate through each presentation group, consolidate its sub-models into a single wrapper, and nest them appropriately.
                for (int g = 0; g < PresentationGroupOrder.Length; g++)
                {
                    int typeIndex = (int)PresentationGroupOrder[g];
                    ProcessAndAddGroupWrapper(pooledGroupSubModels[typeIndex], (PartType)typeIndex, context, massModel, template);
                }

                // Calculate the total multiplier for the mass model based on the biological baseline and any offsets.
                massModel.TotalMultiplier = biologicalBaseline > 0f ? ((biologicalBaseline + massModel.Offset) / biologicalBaseline) : 0f;
                if (massModel.TotalMultiplier < 0f) massModel.TotalMultiplier = 0f;

                // Build a detailed explanation of the mass model, optionally including verbose breakdowns.
                bool verbose = OverHaulers.settings?.verboseBreakdown ?? false;
                massModel.Explanation = ReportFormatter.BuildExplanation(massModel, verbose, biologicalBaseline);
            }
            catch (Exception ex)
            {
                string targetName = source?.EntityLabel ?? "Unknown";
                OHLog.Presentation.Warn("InfoCard", ex, $"Failed to project view for pawn {targetName}.");
            }
            finally
            {
                // Conclude the view projection process by clearing temporary buffers and resetting the projection flag.
                ClearScratchBuffers();
                isProjectingView = false;
            }
        }

        /// <summary>
        /// Rebuilds the detailed mass model for the specified pawn, using the provided solved offset, biological baseline, and anatomical workspace.
        /// </summary>
        /// <param name="pawn">The pawn for whom the mass model is being rebuilt.</param>
        /// <param name="massModel">The mass model to be rebuilt.</param>
        /// <param name="solvedOffset">The solved offset to apply to the mass model.</param>
        /// <param name="biologicalBaseline">The biological baseline for the pawn's mass.</param>
        /// <param name="workspace">The anatomical workspace containing relevant structural information.</param>
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

        /// <summary>
        /// Processes a group of sub-models, consolidates them into a single wrapper node, nests the sub-models in place, and adds the wrapper
        ///  to the mass model.
        /// </summary>
        /// <param name="subModels">The list of sub-models to be processed and added.</param>
        /// <param name="type">The type of the part group being processed.</param>
        /// <param name="context">The systemic evaluation context for the consolidation process.</param>
        /// <param name="massModel">The mass model to which the consolidated group will be added.</param>
        /// <param name="template">The species topology template providing structural information.</param>
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

        /// <summary>
        /// Retrieves the capitalized label for a given part group type based on the species topology template.
        /// </summary>
        /// <param name="type">The type of the part group for which the label is being retrieved.</param>
        /// <param name="template">The species topology template providing structural information.</param>
        /// <returns>The capitalized label for the specified part group type, or an empty string if not available.</returns>
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

        /// <summary>
        /// Consolidates a group of sub-models into a single wrapper node, calculating cumulative offsets, efficiencies, and health metrics,
        ///  while ignoring neutralized parts.
        /// </summary>
        /// <param name="subModels">The list of sub-models to be consolidated.</param>
        /// <param name="type">The type of the part group being consolidated.</param>
        /// <param name="context">The systemic evaluation context for the consolidation process.</param>
        /// <param name="massModel">The mass model to which the consolidated group will be added.</param>
        /// <param name="template">The species topology template providing structural information.</param>
        /// <returns>The consolidated part view node representing the group, or null if no valid sub-models are present.</returns>
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

            // Iterate through each sub-model to accumulate offsets, efficiencies, and health metrics.
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

                // Calculate the effective efficiency for the current sub-model based on its status and attributes.
                float effectiveEfficiency;
                if (subModel.IsMissing)
                {
                    effectiveEfficiency = 0f;
                }
                // If the sub-model is neither missing nor has a prosthetic, it is considered a natural part and its efficiency is
                //  calculated accordingly.
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
                // If the sub-model has a prosthetic, its efficiency is taken directly from its rating and it may influence the dominant
                //  prosthetic tracking.
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
                // Add each sub-model to the consolidated wrapper's sub-parts list.
                consolidatedWrapper.AddSubPart(subModels[i]);
            }

            return consolidatedWrapper;
        }

        /// <summary>
        /// Nests the flat list of part view nodes into a hierarchical structure based on the species topology template.
        /// </summary>
        /// <param name="flatModelList">The flat list of part view nodes to be nested.</param>
        /// <param name="template">The species topology template used to determine the hierarchical structure.</param>
        private static void NestModelsInPlace(List<PartViewNode> flatModelList, SpeciesTopologyTemplate template)
        {
            if (flatModelList == null || flatModelList.Count == 0 || template == null) return;

            pooledRecordToModel.Clear();
            pooledRootModels.Clear();

            try
            {
                // Populate the pooled record-to-model dictionary for quick lookup during nesting.
                for (int i = 0; i < flatModelList.Count; i++)
                {
                    var model = flatModelList[i];
                    if (model.Record != null)
                    {
                        pooledRecordToModel[model.Record] = model;
                    }
                }

                // Iterate through each model in the flat list to attempt nesting it under its nearest ancestor.
                for (int i = 0; i < flatModelList.Count; i++)
                {
                    var model = flatModelList[i];
                    // Determine the index of the current model's part in the species topology template.
                    int index = template.GetPartIndex(model.Record);
                    if (index == -1)
                    {
                        // If the part is not found in the template, consider it a root model.
                        pooledRootModels.Add(model);
                        continue;
                    }

                    // Retrieve the type of the current part from the template.
                    PartType type = template.PartTypes[index];
                    
                    // BREAKPOINT ANCHOR: Flat 1D Stride Ancestral Jump Lookup
                    // Attempt to find the nearest ancestor in the template that has a corresponding model in the pooled dictionary.
                    int candidateIndex = template.GetNearestAncestor(index, type);
                    bool hasBeenNested = false;

                    while (candidateIndex != -1)
                    {
                        // Retrieve the candidate record from the template's indexed parts.
                        BodyPartRecord candidateRecord = template.IndexedParts[candidateIndex];
                        if (pooledRecordToModel.TryGetValue(candidateRecord, out PartViewNode parentModel))
                        {
                            parentModel.AddSubPart(model);
                            hasBeenNested = true;
                            break;
                        }
                        candidateIndex = template.GetNearestAncestor(candidateIndex, type);
                    }

                    // If the model could not be nested under any ancestor, consider it a root model.
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

        /// <summary>
        /// Creates a new PartViewNode for the specified body part, initializing it with the given parameters and workspace state.
        /// </summary>
        /// <param name="part">The body part record for which to create the view node.</param>
        /// <param name="proportionalWeight">The proportional weight of the part relative to the whole body.</param>
        /// <param name="partLabel">The label to display for the part.</param>
        /// <param name="isOrganSegment">Indicates whether the part is an organ segment.</param>
        /// <param name="workspace">The anatomical workspace containing the part's state.</param>
        /// <param name="massModel">The mass capacity model used to acquire and manage the node.</param>
        /// <returns>A newly created and initialized PartViewNode for the specified body part.</returns>
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

            // If a workspace is provided, attempt to retrieve the part's index and associated state.
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

                // Populate the part view node with the retrieved cold state and workspace flags.
                if (workspace.HasFlag(index, PartFlags.HasAddedPart))
                {
                    partViewNode.HasProsthetic = true;
                    partViewNode.ProstheticName = coldState.ProstheticName;
                    partViewNode.EfficiencyRating = workspace.EfficiencyRatings[index];
                    partViewNode.ProstheticColor = coldState.ProstheticColor;
                    partViewNode.IsInherited = workspace.HasFlag(index, PartFlags.ProstheticIsInherited);
                }
                // If the part does not have an added part, set its efficiency rating based on its health fraction.
                else
                {
                    partViewNode.EfficiencyRating = workspace.HealthFractions[index];
                }

                // If the part has an athletic implant, populate the corresponding fields in the part view node.
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

        /// <summary>
        /// Categorizes the body parts of the given species topology template into the provided categorized parts array.
        /// </summary>
        /// <param name="template">The species topology template containing the body parts to categorize.</param>
        /// <param name="categorizedParts">An array of lists where each list will be populated with body parts of the corresponding type.</param>
        private static void CategorizePartsFast(
            SpeciesTopologyTemplate template, 
            List<BodyPartRecord>[] categorizedParts)
        {
            if (template == null) return;

            int partCount = template.PartCount;
            BodyPartRecord[] indexedParts = template.IndexedParts;
            PartType[] partTypes = template.PartTypes;

            // Iterate through each body part in the template and categorize it based on its type.
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

        /// <summary>
        /// Clears the scratch buffers used for temporary storage during body part categorization and projection calculations.
        /// </summary>
        /// <remarks>
        /// This method should be called after each projection calculation to ensure that temporary data does not persist between calculations.
        /// </remarks>
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
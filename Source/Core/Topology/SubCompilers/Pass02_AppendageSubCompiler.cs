using System;
using System.Collections.Generic;
using Verse;

namespace OverHaulers
{
    #region 1. [TOPO-SUB-02] TRANSIENT GRAPH COMPILATION STRUCT

    public struct PartTopologyInfo
    {
        public PartType Type;
        public int ParentIndex;
        public int RootPartIndex;
        public int SiblingCount;
        public int Depth;
        public float UnnormalizedLimbWeight;
        public float StaticWeightFactor;

        public float SafeSiblingDivisor => SiblingCount <= 1 ? 1.0f : (float)SiblingCount;
    }

    #endregion

    /// <summary>
    /// [TOPO-SUB-02] APPENDAGE TOPOLOGY SUB-COMPILER
    /// Stateless sub-compiler managing limb classification, dual-purpose appendages, and connective anchor reclamation.
    /// Resides under Source/Core/Topology/SubCompilers/.
    /// </summary>
    public static class Pass02_AppendageSubCompiler
    {
        #region 2. [TOPO-SUB-02] PASS 2: ANATOMICAL CLASSIFICATION & ROOT DISCOVERY

        /// <summary>
        /// Compiles the anatomical classifications for each body part, determining limb types, dual-purpose appendages, and other structural roles.
        /// </summary>
        /// <param name="bodyDef">The body definition containing all parts of the entity.</param>
        /// <param name="template">The species topology template to be populated with appendage information.</param>
        /// <param name="parentIndicesArr">Array of parent indices for each body part.</param>
        /// <param name="headAncestry">Array indicating whether each part is part of the head ancestry.</param>
        /// <param name="resolvedTypes">Output array of resolved part types for each body part.</param>
        /// <param name="counts">Output structure containing counts of various part types.</param>
        /// <returns>Array of PartTopologyInfo structures containing detailed topology information for each body part.</returns>
        public static PartTopologyInfo[] CompilePartClassifications(
            BodyDef bodyDef,
            SpeciesTopologyTemplate template,
            int[] parentIndicesArr,
            bool[] headAncestry,
            out PartType[] resolvedTypes,
            out PartCounts counts)
        {
            int partCount = bodyDef.AllParts.Count;
            BodyPartRecord corePart = bodyDef.corePart;
            resolvedTypes = new PartType[partCount];
            counts = default;

            #region 2A. Base Identification & Limb Tag Checks

            // Iterates through all body parts to perform initial classification and tagging based on coverage, mod extensions, and anatomical features.
            for (int i = 0; i < partCount; i++)
            {
                BodyPartRecord part = bodyDef.AllParts[i];

                // BREAKPOINT ANCHOR: Virtual / Conceptual Apparel Anchor Gate (e.g. Waist)
                // Non-core parts with 0 coverage have no physical hit volume and carry no structural weight
                if (part.coverageAbs <= 0f && part != corePart)
                {
                    resolvedTypes[i] = PartType.None;
                    continue;
                }

                var ext = part.def?.GetCachedModExtension();
                if (ext != null)
                {
                    if (ext.isOrgan) { resolvedTypes[i] = PartType.None; template.IsOrgan[i] = true; continue; }
                    if (ext.partType != PartType.None) { resolvedTypes[i] = ext.partType; continue; }
                }

                if (MedicalClassifier.IsMetabolicOrgan(part.def))
                {
                    resolvedTypes[i] = PartType.None;
                    template.IsOrgan[i] = true;
                }
                else if (headAncestry[i])
                {
                    resolvedTypes[i] = PartType.HeadPart;
                }
                else if (part == corePart || TopologyLayoutCompiler.IsTrunkDef(part.def) || TopologyLayoutCompiler.IsTorsoGroupPart(part))
                {
                    resolvedTypes[i] = PartType.CorePart;
                }
                else
                {
                    bool hasArmTag = TopologyLayoutCompiler.HasAnyTag(part.def, TopologyLayoutCompiler.armTags);
                    bool hasLegTag = TopologyLayoutCompiler.HasAnyTag(part.def, TopologyLayoutCompiler.legTags);

                    if (hasArmTag && hasLegTag) resolvedTypes[i] = PartType.DualLimb;
                    else if (hasArmTag) resolvedTypes[i] = PartType.ManipulationPart;
                    else if (hasLegTag) resolvedTypes[i] = PartType.MovingPart;
                    else resolvedTypes[i] = PartType.None;
                }
            }

            #endregion

            #region 2B. Bidirectional Branch Propagation

            // ANATOMICAL RATIONALE (THE UNTAGGED MODDED BONE PROBLEM):
            // In vanilla and many modded xenotypes, intermediate bones (like Forearm or Humerus) often lack 
            // explicit ManipulationLimb tags; only the hands/digits carry tags.
            // 
            // The bottom-up sweep propagates limb identity upward from tagged digits to the shoulder;
            // the top-down sweep ensures parallel sub-branches inherit the correct classification.
            for (int d = partCount - 1; d >= 0; d--)
            {
                int i = template.DepthSortedIndices[d];
                PartType childType = resolvedTypes[i];

                if (childType == PartType.ManipulationPart || childType == PartType.MovingPart || childType == PartType.DualLimb)
                {
                    int parentIdx = parentIndicesArr[i];
                    while (parentIdx != -1 && resolvedTypes[parentIdx] == PartType.None)
                    {
                        BodyPartRecord parentPart = bodyDef.AllParts[parentIdx];
                        if (parentPart == corePart || TopologyLayoutCompiler.IsTrunkDef(parentPart.def) || headAncestry[parentIdx] || (parentPart.coverageAbs <= 0f && parentPart != corePart))
                            break;

                        resolvedTypes[parentIdx] = childType;
                        parentIdx = parentIndicesArr[parentIdx];
                    }
                }
            }

            // Ensures that any remaining unclassified parts are assigned a type based on their parent's classification, maintaining structural
            // consistency.
            for (int d = 0; d < partCount; d++)
            {
                int i = template.DepthSortedIndices[d];
                if (resolvedTypes[i] != PartType.None) continue;
                if (MedicalClassifier.IsMetabolicOrgan(bodyDef.AllParts[i].def) || headAncestry[i] || (bodyDef.AllParts[i].coverageAbs <= 0f && bodyDef.AllParts[i] != corePart)) continue;

                int parentIdx = parentIndicesArr[i];
                if (parentIdx != -1)
                {
                    PartType parentType = resolvedTypes[parentIdx];
                    if (parentType == PartType.ManipulationPart || parentType == PartType.MovingPart || parentType == PartType.DualLimb)
                    {
                        resolvedTypes[i] = parentType;
                    }
                }
            }

            #endregion

            #region 2C. Connective Anchor-Bridge Reclassification

            // Reclassifies parts that serve as connective anchors or bridges between major structural components, ensuring accurate representation of load-bearing elements.
            for (int i = 0; i < partCount; i++)
            {
                BodyPartRecord part = bodyDef.AllParts[i];
                if (MedicalClassifier.IsMetabolicOrgan(part.def) || headAncestry[i] || (part.coverageAbs <= 0f && part != corePart)) continue;

                bool wasLimbTyped = resolvedTypes[i] == PartType.ManipulationPart || resolvedTypes[i] == PartType.MovingPart || resolvedTypes[i] == PartType.DualLimb;

                if (ShouldReclaimAsCore(part, i, parentIndicesArr, resolvedTypes))
                {
                    if (wasLimbTyped) template.WasReclaimedAsAnchor[i] = true;
                    resolvedTypes[i] = PartType.CorePart;
                }
                else if (resolvedTypes[i] == PartType.None)
                {
                    resolvedTypes[i] = PartType.CorePart;
                }
            }

            #endregion

            #region 2D. Root Counts & Primary Limb Capture

            // Tallies the number of root parts for each major category and captures the primary definitions for limbs, ensuring accurate
            // representation of the entity's appendage structure.
            for (int i = 0; i < partCount; i++)
            {
                BodyPartRecord part = bodyDef.AllParts[i];
                PartType type = resolvedTypes[i];

                switch (type)
                {
                    case PartType.CorePart: counts.CoreParts++; break;
                    case PartType.HeadPart: counts.HeadParts++; break;
                    case PartType.DualLimb:
                    case PartType.ManipulationPart:
                    case PartType.MovingPart:
                        int parentIdx = parentIndicesArr[i];
                        bool isRoot = parentIdx == -1 || resolvedTypes[parentIdx] != type;
                        if (isRoot)
                        {
                            if (type == PartType.DualLimb)
                            {
                                counts.DualLimbs++;
                                if (template.PrimaryDualDef == null) template.PrimaryDualDef = part.def;
                            }
                            else if (type == PartType.ManipulationPart)
                            {
                                counts.ManipulationParts++;
                                if (template.PrimaryManipulationDef == null) template.PrimaryManipulationDef = part.def;
                            }
                            else
                            {
                                counts.MovingParts++;
                                if (template.PrimaryMovingDef == null) template.PrimaryMovingDef = part.def;
                            }
                        }
                        break;
                }
            }

            if (counts.CoreParts == 0 && corePart != null) counts.CoreParts = 1;

            #endregion

            #region 2E. Branch Depths & Sibling Mapping

            // Constructs temporary topology information for each part, capturing depth, root indices, and initial sibling counts.
            // This information will later be used to determine sibling relationships and branch depths for appendage parts.
            PartTopologyInfo[] tempPartTopologies = new PartTopologyInfo[partCount];
            for (int i = 0; i < partCount; i++)
            {
                PartType type = resolvedTypes[i];
                int parentIndex = parentIndicesArr[i];

                PartTopologyInfo topologyInfo = new PartTopologyInfo
                {
                    Type = type, 
                    ParentIndex = parentIndex, 
                    RootPartIndex = -1, 
                    Depth = 0, 
                    SiblingCount = 1, 
                    StaticWeightFactor = 0f
                };

                // Track limb branch root index and chain depth for structural appendages (arms, legs, dual limbs).
                // Non-limb parts retain default RootPartIndex = -1 and Depth = 0.
                if (type == PartType.ManipulationPart || type == PartType.MovingPart || type == PartType.DualLimb)
                {
                    bool continuesParentChain = parentIndex != -1 && resolvedTypes[parentIndex] == type;
                    if (continuesParentChain)
                    {
                        topologyInfo.RootPartIndex = tempPartTopologies[parentIndex].RootPartIndex;
                        topologyInfo.Depth = tempPartTopologies[parentIndex].Depth + 1;
                    }
                    else
                    {
                        topologyInfo.RootPartIndex = i;
                        topologyInfo.Depth = 0;
                    }
                }
                tempPartTopologies[i] = topologyInfo;
            }

            // Computes sibling counts for each part based on type, root part index, and depth, ensuring accurate representation of parallel appendages.
            Dictionary<long, int> siblingCountsMap = new Dictionary<long, int>(partCount);
            for (int i = 0; i < partCount; i++)
            {
                PartTopologyInfo other = tempPartTopologies[i];
                if (other.Type != PartType.None)
                {
                    long key = ((long)other.Type << 32) | ((long)(other.RootPartIndex + 1) << 16) | (long)other.Depth;
                    siblingCountsMap[key] = siblingCountsMap.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }

            // Updates each part's sibling count based on the previously computed map, ensuring that each part is aware of its parallel counterparts.
            for (int i = 0; i < partCount; i++)
            {
                ref PartTopologyInfo topology = ref tempPartTopologies[i];
                if (topology.Type != PartType.None)
                {
                    long key = ((long)topology.Type << 32) | ((long)(topology.RootPartIndex + 1) << 16) | (long)topology.Depth;
                    topology.SiblingCount = Math.Max(1, siblingCountsMap[key]);
                }

                template.Depth[i] = topology.Depth;
                template.SiblingCount[i] = topology.SiblingCount;
                template.RootPartIndex[i] = topology.RootPartIndex;
            }

            // At this point, all part topologies have been finalized and the template has been updated accordingly.
            #endregion

            return tempPartTopologies;
        }

        /// <summary>
        /// Determines whether a given body part should be reclassified as a core part based on its own characteristics and its position within the hierarchy.
        /// </summary>
        /// <param name="part">The body part record being evaluated.</param>
        /// <param name="index">The index of the body part within the overall part array.</param>
        /// <param name="parentIndicesArr">An array mapping each part to its parent index.</param>
        /// <param name="resolvedTypes">An array of resolved part types corresponding to each part index.</param>
        /// <returns>True if the part should be reclassified as a core part; otherwise, false.</returns>
        /// <remarks>
        /// THE "CLAVICLE" PROBLEM:
        /// In standard RimWorld humanoid anatomy, the Clavicle (collarbone) is grouped under 'Arms'. 
        /// However, structurally, the collarbone is a stationary anchor bridge transferring arm loads 
        /// to the core/torso. ShouldReclaimAsCore detects untagged bones that connect limbs to the torso 
        /// and reclaims them as CorePart, preventing lost collarbones from being misclassified as severed limbs.
        /// </remarks>
        private static bool ShouldReclaimAsCore(BodyPartRecord part, int index, int[] parentIndicesArr, PartType[] resolvedTypes)
        {
            if (part == null) return false;

            bool hasOwnLimbTag = TopologyLayoutCompiler.HasAnyTag(part.def, TopologyLayoutCompiler.armTags) ||
                                  TopologyLayoutCompiler.HasAnyTag(part.def, TopologyLayoutCompiler.legTags);
            if (hasOwnLimbTag)
            {
                return false;
            }

            if (HasLimbDescendant(part))
            {
                return false;
            }

            int parentIdx = parentIndicesArr[index];
            if (parentIdx == -1) return true;

            PartType parentType = resolvedTypes[parentIdx];

            if (parentType == PartType.CorePart || parentType == PartType.None)
            {
                return true;
            }

            if (parentType == PartType.ManipulationPart || parentType == PartType.MovingPart || parentType == PartType.DualLimb)
            {
                int grandParentIdx = parentIndicesArr[parentIdx];
                bool parentIsLimbRoot = grandParentIdx == -1 || 
                    (resolvedTypes[grandParentIdx] != PartType.ManipulationPart && 
                     resolvedTypes[grandParentIdx] != PartType.MovingPart && 
                     resolvedTypes[grandParentIdx] != PartType.DualLimb);

                if (parentIsLimbRoot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether a given body part has any descendant that is classified as a limb (arm or leg). 
        /// </summary>
        /// <param name="part">The body part record to check for limb descendants.</param>
        /// <returns>True if the part has any limb descendants; otherwise, false.</returns>
        private static bool HasLimbDescendant(BodyPartRecord part)
        {
            if (part?.parts == null || part.parts.Count == 0) return false;

            for (int i = 0; i < part.parts.Count; i++)
            {
                BodyPartRecord child = part.parts[i];
                if (child?.def == null) continue;

                if (TopologyLayoutCompiler.HasAnyTag(child.def, TopologyLayoutCompiler.armTags) ||
                    TopologyLayoutCompiler.HasAnyTag(child.def, TopologyLayoutCompiler.legTags) ||
                    TopologyLayoutCompiler.IsLimbGroupPart(child))
                {
                    return true;
                }

                if (HasLimbDescendant(child))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
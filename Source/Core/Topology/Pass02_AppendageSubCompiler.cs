using System;
using System.Collections.Generic;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TOPO-SUB-02] APPENDAGE TOPOLOGY SUB-COMPILER
    /// Stateless sub-compiler managing limb classification, dual-purpose appendages, and connective anchor reclamation.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public static class Pass02_AppendageSubCompiler
    {
        #region 1. [TOPO-SUB-02] PASS 2: ANATOMICAL CLASSIFICATION & ROOT DISCOVERY

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

            #region 1A. Base Identification & Limb Tag Checks

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

            #region 1B. Bidirectional Branch Propagation

            // ANATOMICAL RATIONALE (THE UNTAGGED MODDED BONE & DUAL-LIMB MERGE):
            // In vanilla and modded xenotypes, intermediate bones often lack tags while digits carry them.
            // Furthermore, quadrupeds with claws often have MovingPart legs parented to DualLimb/Manipulation claws.
            // 
            // The bottom-up sweep propagates limb identity upward and merges multi-capability branches into DualLimb;
            // the top-down sweep ensures all child segments inherit the unified limb classification.
            for (int d = partCount - 1; d >= 0; d--)
            {
                int i = template.DepthSortedIndices[d];
                PartType childType = resolvedTypes[i];

                if (IsLimbType(childType))
                {
                    int parentIdx = parentIndicesArr[i];
                    while (parentIdx != -1)
                    {
                        BodyPartRecord parentPart = bodyDef.AllParts[parentIdx];
                        if (parentPart == corePart || TopologyLayoutCompiler.IsTrunkDef(parentPart.def) || headAncestry[parentIdx] || (parentPart.coverageAbs <= 0f && parentPart != corePart))
                            break;

                        PartType currentParentType = resolvedTypes[parentIdx];

                        if (currentParentType == PartType.None)
                        {
                            resolvedTypes[parentIdx] = childType;
                        }
                        else if (IsLimbType(currentParentType))
                        {
                            if (currentParentType != childType)
                            {
                                resolvedTypes[parentIdx] = PartType.DualLimb;
                            }
                        }
                        else
                        {
                            break;
                        }

                        parentIdx = parentIndicesArr[parentIdx];
                    }
                }
            }

            // Top-down sweep: ensure all child segments within a DualLimb branch inherit DualLimb classification
            for (int d = 0; d < partCount; d++)
            {
                int i = template.DepthSortedIndices[d];
                if (MedicalClassifier.IsMetabolicOrgan(bodyDef.AllParts[i].def) || headAncestry[i] || (bodyDef.AllParts[i].coverageAbs <= 0f && bodyDef.AllParts[i] != corePart)) continue;

                int parentIdx = parentIndicesArr[i];
                if (parentIdx != -1)
                {
                    PartType parentType = resolvedTypes[parentIdx];
                    if (IsLimbType(parentType))
                    {
                        if (parentType == PartType.DualLimb)
                        {
                            resolvedTypes[i] = PartType.DualLimb;
                        }
                        else if (resolvedTypes[i] == PartType.None)
                        {
                            resolvedTypes[i] = parentType;
                        }
                    }
                }
            }

            #endregion

            #region 1C. Connective Anchor-Bridge Reclassification

            // Reclassifies parts that serve as connective anchors or bridges between major structural components, ensuring accurate representation of load-bearing elements.
            for (int i = 0; i < partCount; i++)
            {
                BodyPartRecord part = bodyDef.AllParts[i];
                if (MedicalClassifier.IsMetabolicOrgan(part.def) || headAncestry[i] || (part.coverageAbs <= 0f && part != corePart)) continue;

                bool wasLimbTyped = IsLimbType(resolvedTypes[i]);

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

            #region 1D. Root Counts & Primary Limb Capture

            // Tallies the number of genuine root parts: child segments attached to existing limbs are NEVER roots.
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
                        bool isRoot = parentIdx == -1 || !IsLimbType(resolvedTypes[parentIdx]);
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

            #region 1E. Branch Depths & Sibling Mapping

            PartTopologyInfo[] tempPartTopologies = new PartTopologyInfo[partCount];
            int manipRootCounter = 0;
            int movingRootCounter = 0;

            for (int d = 0; d < partCount; d++)
            {
                int i = template.DepthSortedIndices[d];
                PartType type = resolvedTypes[i];
                int parentIndex = parentIndicesArr[i];

                PartTopologyInfo topologyInfo = new PartTopologyInfo
                {
                    Type = type,
                    ParentIndex = parentIndex,
                    RootPartIndex = -1,
                    Depth = 0,
                    SiblingCount = 1,
                    StaticWeightFactor = 0f,
                    ManipPairIndex = 0,
                    MovingPairIndex = 0
                };

                // Track limb branch root index, chain depth, and channel pair indices
                if (IsLimbType(type))
                {
                    bool continuesParentChain = parentIndex != -1 && IsLimbType(resolvedTypes[parentIndex]);
                    if (continuesParentChain)
                    {
                        topologyInfo.RootPartIndex = tempPartTopologies[parentIndex].RootPartIndex;
                        topologyInfo.Depth = tempPartTopologies[parentIndex].Depth + 1;
                        topologyInfo.ManipPairIndex = tempPartTopologies[parentIndex].ManipPairIndex;
                        topologyInfo.MovingPairIndex = tempPartTopologies[parentIndex].MovingPairIndex;
                    }
                    else
                    {
                        topologyInfo.RootPartIndex = i;
                        topologyInfo.Depth = 0;

                        if (type == PartType.ManipulationPart || type == PartType.DualLimb)
                        {
                            topologyInfo.ManipPairIndex = manipRootCounter / 2;
                            manipRootCounter++;
                        }

                        if (type == PartType.MovingPart || type == PartType.DualLimb)
                        {
                            topologyInfo.MovingPairIndex = movingRootCounter / 2;
                            movingRootCounter++;
                        }
                    }
                }
                tempPartTopologies[i] = topologyInfo;
            }

            // Computes direct-parent child counts for limb chains so forked digits divide siblings correctly
            int[] directLimbChildCounts = new int[partCount];
            for (int i = 0; i < partCount; i++)
            {
                PartTopologyInfo other = tempPartTopologies[i];
                if (IsLimbType(other.Type) &&
                    other.ParentIndex != -1 &&
                    IsLimbType(tempPartTopologies[other.ParentIndex].Type))
                {
                    directLimbChildCounts[other.ParentIndex]++;
                }
            }

            for (int i = 0; i < partCount; i++)
            {
                ref PartTopologyInfo topology = ref tempPartTopologies[i];
                bool continuesChain =
                    IsLimbType(topology.Type) &&
                    topology.ParentIndex != -1 &&
                    IsLimbType(tempPartTopologies[topology.ParentIndex].Type);

                topology.SiblingCount = continuesChain ? Math.Max(1, directLimbChildCounts[topology.ParentIndex]) : 1;

                template.Depth[i] = topology.Depth;
                template.ManipPairIndex[i] = topology.ManipPairIndex;
                template.MovingPairIndex[i] = topology.MovingPairIndex;
                template.SiblingCount[i] = topology.SiblingCount;
                template.RootPartIndex[i] = topology.RootPartIndex;
            }

            return tempPartTopologies;

            #endregion
        }

        /// <summary>
        /// Returns true if the part classification represents a structural limb (arm, leg, or dual limb).
        /// </summary>
        /// <param name="type">The part type to check.</param>
        /// <returns>True if the part type is a structural limb; otherwise, false.</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsLimbType(PartType type)
        {
            return type == PartType.ManipulationPart || type == PartType.MovingPart || type == PartType.DualLimb;
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

            if (IsLimbType(parentType))
            {
                int grandParentIdx = parentIndicesArr[parentIdx];
                bool parentIsLimbRoot = grandParentIdx == -1 || 
                    !IsLimbType(resolvedTypes[grandParentIdx]);

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
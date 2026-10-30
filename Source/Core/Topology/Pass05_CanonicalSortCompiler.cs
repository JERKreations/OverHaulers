using System;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TOPO-SUB-05] CANONICAL SORT & FLAT JUMP-TABLE COMPILER
    /// Stateless sub-compiler building execution sweep indices, 1D stride ancestral jump matrices,
    /// flat child buffer spans, and the natural top-down CanonicalDisplayIndices permutation array.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public static class Pass05_CanonicalSortCompiler
    {
        #region 1. [TOPO-SUB-05] PASS 5: JUMP MATRICES & CANONICAL DISPLAY PERMUTATION

        /// <summary>
        /// Compiles 1D stride ancestor jump matrices, flat child buffers, depth-sorted execution arrays,
        /// and natural top-down canonical display permutations for the species template.
        /// </summary>
        /// <param name="bodyDef">The body definition containing all parts.</param>
        /// <param name="template">The species topology template to populate.</param>
        /// <param name="resolvedTypes">Array of resolved part types.</param>
        /// <param name="parentIndicesArr">Array of parent indices for each part.</param>
        public static void CompileTopologyIndices(
            BodyDef bodyDef,
            SpeciesTopologyTemplate template,
            PartType[] resolvedTypes,
            int[] parentIndicesArr)
        {
            int partCount = bodyDef.AllParts.Count;

            #region 1A. 1D Stride Ancestral Jump Matrix (PartCount * 6)

            // Initialize nearest ancestor and structural ancestor matrices.
            int[] nearestAncestorMatrix = template.NearestAncestorMatrix ?? new int[partCount * SpeciesTopologyTemplate.PartTypeStride];
            int[] nearestStructuralAncestor = template.NearestStructuralAncestor ?? new int[partCount];

            // Populate the nearest ancestor and structural ancestor matrices for each part.
            for (int i = 0; i < partCount; i++)
            {
                int parentIdx = parentIndicesArr[i];
                int rowOffset = i * SpeciesTopologyTemplate.PartTypeStride;

                // PartType.None slot (0) is always -1
                nearestAncestorMatrix[rowOffset + 0] = -1;

                // Populate the nearest ancestor matrix for each part type slot.
                for (int t = 1; t <= 5; t++)
                {
                    // If the parent index is -1, there is no ancestor of this type.
                    if (parentIdx == -1)
                    {
                        nearestAncestorMatrix[rowOffset + t] = -1;
                    }
                    // If the parent is of the current type, it is the nearest ancestor of this type.
                    else if ((int)resolvedTypes[parentIdx] == t)
                    {
                        nearestAncestorMatrix[rowOffset + t] = parentIdx;
                    }
                    // Otherwise, inherit the nearest ancestor of this type from the parent.
                    else
                    {
                        int parentRowOffset = parentIdx * SpeciesTopologyTemplate.PartTypeStride;
                        nearestAncestorMatrix[rowOffset + t] = nearestAncestorMatrix[parentRowOffset + t];
                    }
                }

                // Structural Ancestor Resolution (nearest non-organ ancestor)
                if (parentIdx == -1)
                {
                    nearestStructuralAncestor[i] = -1;
                }
                // If the parent is a structural part, it is the nearest structural ancestor.
                else if (resolvedTypes[parentIdx] != PartType.None)
                {
                    nearestStructuralAncestor[i] = parentIdx;
                }
                // Otherwise, inherit the nearest structural ancestor from the parent.
                else
                {
                    nearestStructuralAncestor[i] = nearestStructuralAncestor[parentIdx];
                }
            }

            template.NearestAncestorMatrix = nearestAncestorMatrix;
            template.NearestStructuralAncestor = nearestStructuralAncestor;

            // Nearest ancestor and structural ancestor matrices populated.
            #endregion

            #region 1B. Flat Contiguous Child Buffer (0 Jagged Allocations)

            // Initialize child counts and offsets for each part.
            int[] childCounts = template.ChildCounts ?? new int[partCount];
            int[] childOffsets = template.ChildOffsets ?? new int[partCount];
            Array.Clear(childCounts, 0, partCount);

            int totalChildren = 0;
            // Count the number of children for each part.
            for (int i = 0; i < partCount; i++)
            {
                BodyPartRecord part = bodyDef.AllParts[i];
                // Only consider parts that have a parent.
                if (part.parent != null)
                {
                    // Get the index of the parent part.
                    int parentIdx = template.GetPartIndex(part.parent);

                    // If the parent index is valid, increment the child count for the parent.
                    if (parentIdx != -1)
                    {
                        childCounts[parentIdx]++;
                        totalChildren++;
                    }
                }
            }

            // Calculate the starting offset for each part's children in the flat child buffer.
            int currentOffset = 0;
            for (int i = 0; i < partCount; i++)
            {
                childOffsets[i] = currentOffset;
                currentOffset += childCounts[i];
            }

            // Populate the flat child buffer with the indices of each part's children.
            int[] childBuffer = new int[totalChildren];
            int[] fillCursors = new int[partCount];

            for (int i = 0; i < partCount; i++)
            {
                BodyPartRecord part = bodyDef.AllParts[i];

                // Only consider parts that have a parent.
                if (part.parent != null)
                {
                    int parentIdx = template.GetPartIndex(part.parent);

                    // If the parent index is valid, insert this part into the parent's child buffer.
                    if (parentIdx != -1)
                    {
                        int insertIndex = childOffsets[parentIdx] + fillCursors[parentIdx]++;
                        childBuffer[insertIndex] = i;
                    }
                }
            }

            template.ChildBuffer = childBuffer;
            template.ChildOffsets = childOffsets;
            template.ChildCounts = childCounts;

            // Child buffer, offsets, and counts populated.
            #endregion

            #region 1C. CanonicalDisplayIndices (Zero-Allocation Natural Anatomical Sort)

            // Skeletal depths are computed in Pass01 during cranial traversal.
            // Build canonical display indices using a 64-bit packed composite key to eliminate lambda closures.
            int[] skeletalDepths = new int[partCount];
            for (int i = 0; i < partCount; i++)
            {
                int parentIdx = parentIndicesArr[i];
                skeletalDepths[i] = parentIdx != -1 ? skeletalDepths[parentIdx] + 1 : 0;
            }

            BuildCanonicalDisplayIndices(template, resolvedTypes, skeletalDepths);

            #endregion
        }

        /// <summary>
        /// Builds canonical display indices using a 64-bit composite key sort.
        /// Zero heap allocations: eliminates compiler-generated delegate/closure classes.
        /// Key layout: (Priority << 32) | (Depth << 16) | SlotIndex
        /// </summary>
        /// <param name="template">The species topology template to populate canonical display indices for.</param>
        /// <param name="resolvedTypes">Array of resolved part types corresponding to each part index.</param>
        /// <param name="skeletalDepths">Array of skeletal depths for each part index.</param>
        private static void BuildCanonicalDisplayIndices(
            SpeciesTopologyTemplate template, 
            PartType[] resolvedTypes, 
            int[] skeletalDepths)
        {
            int partCount = template.PartCount;
            int[] canonical = new int[partCount];
            long[] sortKeys = new long[partCount];

            for (int i = 0; i < partCount; i++)
            {
                canonical[i] = i;
                long priority = (long)GetAnatomicalSortPriority(resolvedTypes[i]);
                long depth = (long)skeletalDepths[i];
                sortKeys[i] = (priority << 32) | (depth << 16) | (long)i;
            }

            // Zero-allocation primitive sort via Mono's native quicksort
            Array.Sort(sortKeys, canonical);

            template.CanonicalDisplayIndices = canonical;
        }

        /// <summary>
        /// Gets the anatomical sort priority for the given part type.
        /// </summary>
        /// <param name="type">The part type.</param>
        /// <returns>The sort priority for the part type.</returns>
        private static int GetAnatomicalSortPriority(PartType type)
        {
            switch (type)
            {
                case PartType.HeadPart: return 1;          // Head at top
                case PartType.CorePart: return 2;          // Neck / Torso / Spine / Pelvis
                case PartType.None: return 3;              // Internal Metabolic Organs
                case PartType.ManipulationPart: return 4;  // Arms / Wings / Blades
                case PartType.MovingPart: return 5;        // Legs / Feet / Hooves
                case PartType.DualLimb: return 6;          // Dual-purpose limbs
                default: return 7;
            }
        }

        #endregion
    }
}
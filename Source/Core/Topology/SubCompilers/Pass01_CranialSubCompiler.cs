using System;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TOPO-SUB-01] CRANIAL TOPOLOGY SUB-COMPILER
    /// Stateless sub-compiler identifying head roots and cranial descendant subtrees across all species.
    /// Pre-sorts topological depth indices in Pass 1 for downstream limb and axial compilers.
    /// 100% agnostic with ZERO string matching.
    /// Resides under Source/Core/Topology/SubCompilers/.
    /// </summary>
    public static class Pass01_CranialSubCompiler
    {
        #region 1. [TOPO-SUB-01] PASS 1: SKELETAL DEPTH & CRANIAL INGRESS

        /// <summary>
        /// Compiles the cranial ancestry for the given body definition, populating parent indices, head ancestry flags, and maximum skeletal depth.
        /// </summary>
        /// <param name="bodyDef">The body definition containing all parts of the entity.</param>
        /// <param name="template">The species topology template to be populated with cranial ancestry information.</param>
        /// <param name="parentIndicesArr">Output array of parent indices for each body part.</param>
        /// <param name="headAncestry">Output array indicating whether each part is part of the head ancestry.</param>
        /// <param name="maxDepthInBody">Output maximum skeletal depth found within the body.</param>
        public static void CompileCranialAncestry(
            BodyDef bodyDef,
            SpeciesTopologyTemplate template,
            out int[] parentIndicesArr,
            out bool[] headAncestry,
            out int maxDepthInBody)
        {
            int partCount = bodyDef.AllParts.Count;
            parentIndicesArr = new int[partCount];
            int[] skeletalDepths = new int[partCount];
            headAncestry = new bool[partCount];
            maxDepthInBody = 0;

            // Single-pass skeletal traversal: populates parent indices, computes skeletal depths,
            //  identifies cranial root bones, and propagates cranial ancestry down the tree.
            for (int i = 0; i < partCount; i++)
            {
                BodyPartRecord part = bodyDef.AllParts[i];
                int parentIdx = part.parent != null ? template.GetPartIndex(part.parent) : -1;
                parentIndicesArr[i] = parentIdx;

                int depth = parentIdx != -1 ? skeletalDepths[parentIdx] + 1 : 0;
                skeletalDepths[i] = depth;
                if (depth > maxDepthInBody) maxDepthInBody = depth;

                bool isHeadRoot = IsHeadRoot(part, bodyDef);
                if (isHeadRoot && template.HeadPart == null)
                {
                    template.HeadPart = part;
                }

                headAncestry[i] = isHeadRoot || (parentIdx != -1 && headAncestry[parentIdx]);
            }

            // BREAKPOINT ANCHOR: Pre-populate DepthSortedIndices in Pass 1 for downstream sweeps
            for (int i = 0; i < partCount; i++)
            {
                template.DepthSortedIndices[i] = i;
            }

            Array.Sort(template.DepthSortedIndices, (a, b) => skeletalDepths[a].CompareTo(skeletalDepths[b]));
        }

        #endregion

        #region 2. AGNOSTIC HEAD PREDICATES

        /// <summary>
        /// Determines whether the specified body part is considered the root of the head based on various criteria.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <param name="bodyDef">The body definition containing the part.</param>
        /// <returns>True if the part is considered the root of the head; otherwise, false.</returns>
        public static bool IsHeadRoot(BodyPartRecord part, BodyDef bodyDef)
        {
            if (part?.def == null || bodyDef == null) return false;

            // Core root can never be a head root
            if (part == bodyDef.corePart) return false;
            if (TopologyLayoutCompiler.IsTrunkDef(part.def)) return false;

            // 1. Explicit ModExtension Override
            var ext = part.def.GetCachedModExtension();
            if (ext != null)
            {
                if (ext.partType == PartType.HeadPart) return true;
                if (ext.partType != PartType.None) return false;
            }

            // 2. Direct Head Group Membership (FullHead, UpperHead for Humanoids; HeadAttackTool for Animals/Insects)
            if (part.groups != null && part.groups.Count > 0)
            {
                for (int i = 0; i < part.groups.Count; i++)
                {
                    var g = part.groups[i];
                    if (g == null) continue;
                    if (g == TopologyLayoutCompiler.FullHeadGroup || 
                        g == TopologyLayoutCompiler.UpperHeadGroup || 
                        g == TopologyLayoutCompiler.HeadAttackToolGroup)
                    {
                        return true;
                    }
                }
            }

            // 3. Cranial Organ Container (Direct child parts contain Brain, Eyes, or Ears)
            if (part.parts != null && part.parts.Count > 0)
            {
                for (int i = 0; i < part.parts.Count; i++)
                {
                    var childDef = part.parts[i]?.def;
                    if (childDef?.tags == null || childDef.tags.Count == 0) continue;

                    if ((TopologyLayoutCompiler.ConsciousnessSourceTag != null && childDef.tags.Contains(TopologyLayoutCompiler.ConsciousnessSourceTag)) ||
                        (TopologyLayoutCompiler.SightSourceTag != null && childDef.tags.Contains(TopologyLayoutCompiler.SightSourceTag)) ||
                        (TopologyLayoutCompiler.HearingSourceTag != null && childDef.tags.Contains(TopologyLayoutCompiler.HearingSourceTag)))
                    {
                        return true;
                    }
                }
            }

            // 4. Standalone Consciousness Source (for headless entities with an exposed brain)
            if (part.def.tags != null && TopologyLayoutCompiler.ConsciousnessSourceTag != null && part.def.tags.Contains(TopologyLayoutCompiler.ConsciousnessSourceTag))
            {
                return true;
            }

            return false;
        }

        #endregion
    }
}
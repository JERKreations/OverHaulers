using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TOPO-SUB-04] APPENDAGE WEIGHT SUB-COMPILER
    /// Stateless sub-compiler managing long serial chain detection, geometric limb depth decay,
    /// branch weight summation, and 3-channel (Torso, Manipulation, Moving) budget normalization.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public static class Pass04_AppendageWeightSubCompiler
    {
        #region 1. [TOPO-SUB-04] PASS 4: GEOMETRIC LIMB DEPTH DECAY & WEIGHT NORMALIZATION

        private const int LongSerialChainDepthThreshold = 2;

        /// <summary>
        /// Compiles the final limb and torso weights for a given body definition, taking into account geometric limb depth decay and torso
        /// weight normalization.
        /// </summary>
        /// <param name="bodyDef">The body definition containing all body parts.</param>
        /// <param name="template">The species topology template used for the compilation.</param>
        /// <param name="counts">The counts of various body part types.</param>
        /// <param name="tempPartTopologies">An array of temporary part topology information for each body part.</param>
        /// <param name="torsoRawWeights">An array to store the raw weights of torso parts.</param>
        /// <param name="torsoRelativeWeightSum">The sum of relative weights for the torso parts.</param>
        public static void CompileLimbAndFinalWeights(
            BodyDef bodyDef,
            SpeciesTopologyTemplate template,
            PartCounts counts,
            PartTopologyInfo[] tempPartTopologies,
            float[] torsoRawWeights,
            float torsoRelativeWeightSum)
        {
            int partCount = bodyDef.AllParts.Count;
            ref readonly SolverConfiguration config = ref MassCapacitySolver.ActiveConfiguration;
            float decayFactor = config.LimbDepthDecayFactor;
            float hpSensitivity = config.TorsoHpSensitivity;

            #region 1A. Long Serial Chain vs Geometric Decay Selection

            // Initialize dictionaries to track the maximum depth and sibling count for each limb branch.
            Dictionary<long, int> branchMaxDepth = new Dictionary<long, int>(partCount);
            Dictionary<long, int> branchMaxSibling = new Dictionary<long, int>(partCount);
            bool[] isDecayExempt = new bool[partCount];

            // Iterate through all parts to populate the branch depth and sibling count dictionaries.
            for (int i = 0; i < partCount; i++)
            {
                PartTopologyInfo topology = tempPartTopologies[i];
                if (topology.Type == PartType.ManipulationPart || topology.Type == PartType.MovingPart || topology.Type == PartType.DualLimb)
                {
                    long branchKey = ((long)topology.Type << 32) | (long)(topology.RootPartIndex + 1);
                    if (!branchMaxDepth.TryGetValue(branchKey, out int curDepth) || topology.Depth > curDepth) branchMaxDepth[branchKey] = topology.Depth;
                    if (!branchMaxSibling.TryGetValue(branchKey, out int curSib) || topology.SiblingCount > curSib) branchMaxSibling[branchKey] = topology.SiblingCount;
                }
            }

            // Traverse in parent-before-child DepthSortedIndices order so forked limb segments and decay-exempt
            // subtrees inherit strictly from their immediate same-type parent.
            int[] depthSorted = template.DepthSortedIndices;
            for (int d = 0; d < partCount; d++)
            {
                int i = depthSorted != null ? depthSorted[d] : d;
                ref PartTopologyInfo topology = ref tempPartTopologies[i];
                if (topology.Type == PartType.ManipulationPart || topology.Type == PartType.MovingPart || topology.Type == PartType.DualLimb)
                {
                    var ext = bodyDef.AllParts[i].def?.GetCachedModExtension();
                    bool continuesChain = topology.ParentIndex != -1 && tempPartTopologies[topology.ParentIndex].Type == topology.Type;
                    bool directExempt = ext != null && ext.ignoreLimbDepthDecay;
                    bool parentExempt = continuesChain && isDecayExempt[topology.ParentIndex];

                    // Check for explicit mod extension overrides for limb weight.
                    if (ext != null && ext.relativeLimbWeight >= 0f)
                    {
                        isDecayExempt[i] = directExempt || parentExempt;
                        template.WeightModels[i] = LimbWeightModel.ExplicitOverride;
                        topology.UnnormalizedLimbWeight = ext.relativeLimbWeight / topology.SafeSiblingDivisor;
                    }
                    // Direct or inherited exemption from geometric limb depth decay (Decay = 1.0 across subtree).
                    else if (directExempt)
                    {
                        isDecayExempt[i] = true;
                        template.WeightModels[i] = LimbWeightModel.ExplicitOverride;
                        topology.UnnormalizedLimbWeight = 1.0f / topology.SafeSiblingDivisor;
                    }
                    // If the parent is exempt, but the current part is not directly exempt, inherit the exemption.
                    else if (parentExempt)
                    {
                        isDecayExempt[i] = true;
                        template.WeightModels[i] = LimbWeightModel.ExplicitOverride;
                        topology.UnnormalizedLimbWeight = tempPartTopologies[topology.ParentIndex].UnnormalizedLimbWeight / topology.SafeSiblingDivisor;
                    }
                    // If no explicit or inherited overrides apply, fall back to serial HP ratio or geometric depth decay.
                    else
                    {
                        long branchKey = ((long)topology.Type << 32) | (long)(topology.RootPartIndex + 1);
                        bool isLongSerialChain = branchMaxSibling.TryGetValue(branchKey, out int maxSib) && maxSib <= 1
                            && branchMaxDepth.TryGetValue(branchKey, out int maxDepth) && maxDepth >= LongSerialChainDepthThreshold;

                        if (isLongSerialChain)
                        {
                            BodyPartRecord part = bodyDef.AllParts[i];
                            BodyPartRecord rootPart = bodyDef.AllParts[topology.RootPartIndex];
                            float rootHp = (rootPart.def != null && rootPart.def.hitPoints > 0) ? (float)rootPart.def.hitPoints : 10f;
                            float partHp = (part.def != null && part.def.hitPoints > 0) ? (float)part.def.hitPoints : 10f;

                            // For long serial chains, use the HP ratio between the part and its root as the unnormalized weight.
                            template.WeightModels[i] = LimbWeightModel.HpRatio;
                            topology.UnnormalizedLimbWeight = Pass03_AxialTrunkSubCompiler.FastPow(partHp / rootHp, hpSensitivity) / topology.SafeSiblingDivisor;
                        }
                        // If not a long serial chain, fall back to geometric decay based on depth and sibling divisor.
                        else
                        {
                            float unnormalizedWeight;

                            if (continuesChain && template.WeightModels[topology.ParentIndex] == LimbWeightModel.GeometricDecay)
                            {
                                unnormalizedWeight = (tempPartTopologies[topology.ParentIndex].UnnormalizedLimbWeight * decayFactor) / topology.SafeSiblingDivisor;
                            }
                            else
                            {
                                unnormalizedWeight = FastIntPow(decayFactor, topology.Depth) / topology.SafeSiblingDivisor;
                            }

                            template.WeightModels[i] = LimbWeightModel.GeometricDecay;
                            topology.UnnormalizedLimbWeight = unnormalizedWeight;
                        }
                    }
                }
            }

            // At this point, all limb parts have their unnormalized weights calculated based on depth and branch characteristics.
            // The next step involves summing the unnormalized weights for each branch and normalizing them across the three channels:
            //  core, manipulation, and moving parts.
            #endregion

            #region 1B. Branch Summation & 3-Channel Normalization

            // Initialize a dictionary to store the sum of unnormalized limb weights for each branch.
            Dictionary<long, float> branchSumsMap = new Dictionary<long, float>(partCount);

            // Iterate through all parts to accumulate the unnormalized limb weights for each branch.
            for (int i = 0; i < partCount; i++)
            {
                PartTopologyInfo topology = tempPartTopologies[i];
                if (topology.Type == PartType.ManipulationPart || topology.Type == PartType.MovingPart || topology.Type == PartType.DualLimb)
                {
                    long branchKey = ((long)topology.Type << 32) | (long)(topology.RootPartIndex + 1);
                    branchSumsMap[branchKey] = branchSumsMap.TryGetValue(branchKey, out float currentSum)
                        ? currentSum + topology.UnnormalizedLimbWeight
                        : topology.UnnormalizedLimbWeight;
                }
            }

            // EQUAL UNDERLYING LIMB WEIGHTS:
            // Every limb branch within the same channel receives an identical 1/N channel share so natural athletics,
            // physical deficits, and sub-par prosthetics weight all limbs equally and sum cleanly to 1.000.
            // Pair diminishing returns are applied dynamically at solve time by replacement efficiency rank.
            int totalManipRoots = counts.TotalManipulationRoots;
            float manipChannelDivisor = totalManipRoots <= 1 ? 1.0f : (float)totalManipRoots;

            int totalMovingRoots = counts.TotalMovingRoots;
            float moveChannelDivisor = totalMovingRoots <= 1 ? 1.0f : (float)totalMovingRoots;

            SystemicEvaluationContext.ResolveNormalizedBudgets(
                in counts,
                in config,
                out float normTorso,
                out float normManip,
                out float normMove);

            // Iterate through all parts and assign normalized weights based on their type and branch characteristics.
            for (int i = 0; i < partCount; i++)
            {
                ref PartTopologyInfo topology = ref tempPartTopologies[i];

                if (topology.Type == PartType.CorePart)
                {
                    float sumWeights = torsoRelativeWeightSum > 0f ? torsoRelativeWeightSum : 1.0f;
                    template.WeightCore[i] = torsoRawWeights[i] / sumWeights;
                    template.WeightManipulation[i] = 0f;
                    template.WeightMoving[i] = 0f;

                    template.StaticWeightFactors[i] = template.WeightCore[i] * normTorso;
                }
                else if (topology.Type == PartType.ManipulationPart || topology.Type == PartType.MovingPart || topology.Type == PartType.DualLimb)
                {
                    long branchKey = ((long)topology.Type << 32) | (long)(topology.RootPartIndex + 1);
                    float branchSum = branchSumsMap.TryGetValue(branchKey, out float bSum) && bSum > 0f ? bSum : 1.0f;
                    float normalizedBranchFraction = topology.UnnormalizedLimbWeight / branchSum;

                    float manipFactor = (topology.Type == PartType.ManipulationPart || topology.Type == PartType.DualLimb)
                        ? (normalizedBranchFraction / manipChannelDivisor)
                        : 0f;

                    float moveFactor = (topology.Type == PartType.MovingPart || topology.Type == PartType.DualLimb)
                        ? (normalizedBranchFraction / moveChannelDivisor)
                        : 0f;

                    template.WeightCore[i] = 0f;
                    template.WeightManipulation[i] = manipFactor;
                    template.WeightMoving[i] = moveFactor;

                    template.StaticWeightFactors[i] = (template.WeightManipulation[i] * normManip) + (template.WeightMoving[i] * normMove);
                }
                else
                {
                    template.WeightCore[i] = 0f;
                    template.WeightManipulation[i] = 0f;
                    template.WeightMoving[i] = 0f;
                    template.StaticWeightFactors[i] = 0f;
                }

                template.PartTypes[i] = tempPartTopologies[i].Type;
                template.ParentIndices[i] = tempPartTopologies[i].ParentIndex;
            }

            #endregion
        }

        #endregion

        #region 2. MATHEMATICAL FAST INTEGER POW HELPER

        /// <summary>
        /// Computes the power of a base value raised to an integer exponent using a fast method for small exponents and exponentiation by squaring
        /// for larger exponents.
        /// </summary>
        /// <param name="baseVal">The base value to be raised to a power.</param>
        /// <param name="exp">The integer exponent.</param>
        /// <returns>The result of raising <paramref name="baseVal"/> to the power of <paramref name="exp"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastIntPow(float baseVal, int exp)
        {
            switch (exp)
            {
                case 0: return 1.0f;
                case 1: return baseVal;
                case 2: return baseVal * baseVal;
                case 3: return baseVal * baseVal * baseVal;
                case 4: return baseVal * baseVal * baseVal * baseVal;
                default:
                    if (exp < 0) return (float)Math.Pow(baseVal, exp);
                    float result = 1.0f;
                    float current = baseVal;
                    while (exp > 0)
                    {
                        if ((exp & 1) == 1) result *= current;
                        current *= current;
                        exp >>= 1;
                    }
                    return result;
            }
        }

        #endregion
    }
}
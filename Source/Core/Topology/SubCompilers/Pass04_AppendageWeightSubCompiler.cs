using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TOPO-SUB-04] APPENDAGE WEIGHT SUB-COMPILER
    /// Stateless sub-compiler managing long serial chain detection, geometric limb depth decay,
    /// branch weight summation, and 3-channel (Torso, Manipulation, Moving) budget normalization.
    /// Resides under Source/Core/Topology/SubCompilers/.
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
            float decayFactor = OverHaulers.settings?.limbDepthDecayFactor ?? SettingsDefaults.LimbDepthDecayFactor;
            float hpSensitivity = OverHaulers.settings?.torsoHpSensitivity ?? SettingsDefaults.TorsoHpSensitivity;

            #region 1A. Long Serial Chain vs Geometric Decay Selection

            // Initialize dictionaries to track the maximum depth and sibling count for each limb branch.
            Dictionary<long, int> branchMaxDepth = new Dictionary<long, int>(partCount);
            Dictionary<long, int> branchMaxSibling = new Dictionary<long, int>(partCount);

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

            // At this point, branchMaxDepth and branchMaxSibling contain the maximum depth and sibling count for each limb branch.
            for (int i = 0; i < partCount; i++)
            {
                ref PartTopologyInfo topology = ref tempPartTopologies[i];
                if (topology.Type == PartType.ManipulationPart || topology.Type == PartType.MovingPart || topology.Type == PartType.DualLimb)
                {
                    var ext = bodyDef.AllParts[i].def?.GetCachedModExtension();
                    float rawDepthWeight;

                    // Determine the raw depth weight for the current limb part based on various conditions and extensions.
                    if (ext != null && ext.relativeLimbWeight >= 0f)
                    {
                        rawDepthWeight = ext.relativeLimbWeight;
                        template.WeightModels[i] = LimbWeightModel.ExplicitOverride;
                    }
                    // Check if the limb should ignore depth decay based on the extension.
                    else if (ext != null && ext.ignoreLimbDepthDecay)
                    {
                        rawDepthWeight = 1.0f;
                        template.WeightModels[i] = LimbWeightModel.ExplicitOverride;
                    }
                    // If no explicit weight or ignore flag is set, calculate the weight based on branch characteristics.
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

                            // Calculate the raw depth weight based on the hit point ratio between the part and its root part.
                            rawDepthWeight = Pass03_AxialTrunkSubCompiler.FastPow(partHp / rootHp, hpSensitivity);
                            template.WeightModels[i] = LimbWeightModel.HpRatio;
                        }
                        // If the limb is not part of a long serial chain, use the geometric decay model.
                        else
                        {
                            rawDepthWeight = FastIntPow(decayFactor, topology.Depth);
                            template.WeightModels[i] = LimbWeightModel.GeometricDecay;
                        }
                    }

                    topology.UnnormalizedLimbWeight = rawDepthWeight / topology.SafeSiblingDivisor;
                }
            }

            // At this point, all limb parts have their unnormalized weights calculated based on depth and branch characteristics.
            // Proceed to branch summation and normalization to ensure the limb weights are properly scaled within each branch.
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

            int totalManipRoots = counts.TotalManipulationRoots;
            int totalMovingRoots = counts.TotalMovingRoots;

            float manipRootFactor = 1.0f / (totalManipRoots <= 1 ? 1.0f : (float)totalManipRoots);
            float movingRootFactor = 1.0f / (totalMovingRoots <= 1 ? 1.0f : (float)totalMovingRoots);

            float budgetTorso = OverHaulers.settings?.anatomyWeightTorso ?? SettingsDefaults.AnatomyWeightTorso;
            float budgetManip = OverHaulers.settings?.anatomyWeightArm ?? SettingsDefaults.AnatomyWeightArm;
            float budgetMove = OverHaulers.settings?.anatomyWeightLeg ?? SettingsDefaults.AnatomyWeightLeg;
            float totalBudget = budgetTorso + budgetManip + budgetMove;

            // Ensure the total budget is positive to avoid division by zero during normalization.
            if (totalBudget <= 0f) totalBudget = 1.0f;

            // Calculate the normalized budget fractions for torso, manipulation, and moving parts.
            float normTorso = budgetTorso / totalBudget;
            float normManip = budgetManip / totalBudget;
            float normMove = budgetMove / totalBudget;

            // Iterate through all parts and assign normalized weights based on their type and branch characteristics.
            for (int i = 0; i < partCount; i++)
            {
                ref PartTopologyInfo topology = ref tempPartTopologies[i];

                // Determine the type of the current part and assign weights accordingly. Core parts receive torso weights, while manipulation and moving parts are normalized within their respective branches.
                if (topology.Type == PartType.CorePart)
                {
                    float sumWeights = torsoRelativeWeightSum > 0f ? torsoRelativeWeightSum : 1.0f;
                    template.WeightCore[i] = torsoRawWeights[i] / sumWeights;
                    template.WeightManipulation[i] = 0f;
                    template.WeightMoving[i] = 0f;
                    
                    template.StaticWeightFactors[i] = template.WeightCore[i] * normTorso;
                }

                // For manipulation, moving, and dual-limb parts, calculate their normalized weights within their respective branches.
                else if (topology.Type == PartType.ManipulationPart || topology.Type == PartType.MovingPart || topology.Type == PartType.DualLimb)
                {
                    long branchKey = ((long)topology.Type << 32) | (long)(topology.RootPartIndex + 1);
                    float branchSum = branchSumsMap.TryGetValue(branchKey, out float bSum) && bSum > 0f ? bSum : 1.0f;
                    float normalizedBranchFraction = topology.UnnormalizedLimbWeight / branchSum;

                    template.WeightCore[i] = 0f;
                    template.WeightManipulation[i] = (topology.Type == PartType.ManipulationPart || topology.Type == PartType.DualLimb) 
                        ? (manipRootFactor * normalizedBranchFraction) : 0f;

                    template.WeightMoving[i] = (topology.Type == PartType.MovingPart || topology.Type == PartType.DualLimb) 
                        ? (movingRootFactor * normalizedBranchFraction) : 0f;

                    template.StaticWeightFactors[i] = (template.WeightManipulation[i] * normManip) + (template.WeightMoving[i] * normMove);
                }
                // For any other part types not explicitly handled, set all weights to zero.
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

            // At this point, all parts have been processed and their weights have been assigned based on their type and branch characteristics.
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
                    if (exp < 0) return Mathf.Pow(baseVal, exp);
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
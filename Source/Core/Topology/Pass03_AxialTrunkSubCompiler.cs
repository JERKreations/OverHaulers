using System;
using System.Runtime.CompilerServices;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TOPO-SUB-03] AXIAL TRUNK TOPOLOGY SUB-COMPILER
    /// Stateless sub-compiler distributing structural load budgets across spinal columns, necks, pelves, and chest carapaces.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public static class Pass03_AxialTrunkSubCompiler
    {
        #region 1. [TOPO-SUB-03] PASS 3: AXIAL TORSO ROLES & HP DENSITY COMPILER

        /// <summary>
        /// [PASS 3] Distributes the Torso structural budget across spinal, pelvic, neck, and rib segments.
        /// Normalizes each active <see cref="TorsoRole"/> bucket so <see cref="SolverConfiguration.TorsoAxialBias"/> acts as a true
        /// regional budget share across arbitrary morphologies, while weighting parts within each bucket by:
        /// <code>RawScore = FastPow(PartHP / CoreHP, TorsoHpSensitivity) / SiblingDivisor</code>
        /// </summary>
        public static void CompileTorsoRolesAndWeights(
            BodyDef bodyDef,
            SpeciesTopologyTemplate template,
            PartType[] resolvedTypes,
            PartTopologyInfo[] tempPartTopologies,
            out float[] torsoRawWeights,
            out float torsoRelativeWeightSum)
        {
            int partCount = bodyDef.AllParts.Count;
            BodyPartRecord corePart = bodyDef.corePart;
            TorsoRole[] torsoRoles = new TorsoRole[partCount];

            #region 1A. Axial Column Traversal & Anchor Bridges

            // Assign the core part as the initial axial column if it exists. This ensures that the central structural element of the torso is
            // correctly identified.
            if (corePart != null)
            {
                int coreIdx = template.GetPartIndex(corePart);
                if (coreIdx != -1 && resolvedTypes[coreIdx] == PartType.CorePart)
                {
                    torsoRoles[coreIdx] = TorsoRole.AxialColumn;
                }
            }

            // Traverse all parts to ensure that any core parts that are part of the trunk are correctly marked as axial columns. This helps in
            // establishing a continuous axial structure for the torso.
            for (int i = 0; i < partCount; i++)
            {
                if (resolvedTypes[i] == PartType.CorePart && TopologyLayoutCompiler.IsTrunkDef(bodyDef.AllParts[i].def))
                {
                    torsoRoles[i] = TorsoRole.AxialColumn;
                }
            }

            // Iterate through all parts to identify and mark anchor bridges for limb attachment points. This ensures that limbs have a stable
            // connection to the axial structure of the torso.
            for (int i = 0; i < partCount; i++)
            {
                PartType type = resolvedTypes[i];
                if (type == PartType.ManipulationPart || type == PartType.MovingPart || type == PartType.DualLimb)
                {
                    BodyPartRecord part = bodyDef.AllParts[i];
                    bool isLimbRoot = part.parent == null || resolvedTypes[template.GetPartIndex(part.parent)] != type;

                    if (isLimbRoot)
                    {
                        BodyPartRecord curr = part.parent;
                        while (curr != null)
                        {
                            int idx = template.GetPartIndex(curr);
                            if (idx != -1 && resolvedTypes[idx] == PartType.CorePart)
                            {
                                if (torsoRoles[idx] != TorsoRole.AxialColumn)
                                {
                                    torsoRoles[idx] = TorsoRole.AnchorBridge;
                                }
                            }
                            curr = curr.parent;
                        }
                    }
                }
            }

            #endregion

            #region 1B. ModExtension XML Overrides & Fallback

            // Apply any explicit ModExtension XML overrides to the torso roles (including demoting anchor bridges to Peripheral).
            for (int i = 0; i < partCount; i++)
            {
                if (resolvedTypes[i] == PartType.CorePart)
                {
                    var ext = bodyDef.AllParts[i].def?.GetCachedModExtension();
                    if (ext != null && ext.torsoRole != TorsoRole.Auto)
                    {
                        torsoRoles[i] = ext.torsoRole;
                    }
                }
            }

            // Ensure that there is at least one axial column in the torso when possible. If none exists, promote anchor bridges.
            bool hasAxialPath = false;
            for (int i = 0; i < partCount; i++)
            {
                if (torsoRoles[i] == TorsoRole.AxialColumn) { hasAxialPath = true; break; }
            }

            if (!hasAxialPath)
            {
                for (int i = 0; i < partCount; i++)
                {
                    if (resolvedTypes[i] == PartType.CorePart && torsoRoles[i] == TorsoRole.AnchorBridge)
                    {
                        torsoRoles[i] = TorsoRole.AxialColumn;
                    }
                }
            }

            #endregion

            #region 1C. Biomechanical HP Density & Role-Bucket Normalization

            ref readonly SolverConfiguration config = ref MassCapacitySolver.ActiveConfiguration;
            float axialBias = config.TorsoAxialBias;
            float hpSensitivity = config.TorsoHpSensitivity;

            float coreHp = (corePart != null && corePart.def != null && corePart.def.hitPoints > 0) ? (float)corePart.def.hitPoints : 100f;

            torsoRawWeights = new float[partCount];
            bool[] isExplicitOverride = null;

            float axialScoreSum = 0f;
            float anchorScoreSum = 0f;
            float peripheralScoreSum = 0f;

            // Step 1: Write per-part HP density scores directly into torsoRawWeights (or capture explicit overrides) and sum by bucket.
            for (int i = 0; i < partCount; i++)
            {
                if (resolvedTypes[i] != PartType.CorePart) continue;

                BodyPartRecord part = bodyDef.AllParts[i];

                // Zero-coverage virtual parts carry exactly 0.0f raw weight
                if (part.coverageAbs <= 0f && part != corePart)
                {
                    torsoRawWeights[i] = 0f;
                    continue;
                }

                var ext = part.def?.GetCachedModExtension();
                if (ext != null && ext.relativeTorsoWeight >= 0f)
                {
                    if (isExplicitOverride == null) isExplicitOverride = new bool[partCount];
                    isExplicitOverride[i] = true;
                    torsoRawWeights[i] = ext.relativeTorsoWeight / tempPartTopologies[i].SafeSiblingDivisor;
                    continue;
                }

                float partHp = (part.def != null && part.def.hitPoints > 0) ? (float)part.def.hitPoints : 10f;
                float hpRatio = partHp / coreHp;
                float score = FastPow(hpRatio, hpSensitivity) / tempPartTopologies[i].SafeSiblingDivisor;
                torsoRawWeights[i] = score;

                TorsoRole role = torsoRoles[i];
                if (role == TorsoRole.AxialColumn) axialScoreSum += score;
                else if (role == TorsoRole.AnchorBridge) anchorScoreSum += score;
                else peripheralScoreSum += score;
            }

            // Step 2: Assign exact active bucket targets so TorsoAxialBias stays faithful regardless of peripheral part counts.
            bool hasAxial = axialScoreSum > 1e-6f;
            bool hasAnchor = anchorScoreSum > 1e-6f;
            bool hasPeripheral = peripheralScoreSum > 1e-6f;

            float axialTarget = hasAxial ? axialBias : 0f;
            float nonAxialBudget = hasAxial ? (1.0f - axialBias) : 1.0f;
            float anchorTarget = hasAnchor ? (hasPeripheral ? nonAxialBudget * 0.60f : nonAxialBudget) : 0f;
            float peripheralTarget = hasPeripheral ? (hasAnchor ? nonAxialBudget * 0.40f : nonAxialBudget) : 0f;

            if (hasAxial && !hasAnchor && !hasPeripheral)
            {
                axialTarget = 1.0f;
            }

            float torsoRawWeightSum = 0f;
            for (int i = 0; i < partCount; i++)
            {
                if (resolvedTypes[i] != PartType.CorePart) continue;

                bool isOverride = isExplicitOverride != null && isExplicitOverride[i];
                if (!isOverride && torsoRawWeights[i] > 0f)
                {
                    TorsoRole role = torsoRoles[i];
                    if (role == TorsoRole.AxialColumn && hasAxial)
                    {
                        torsoRawWeights[i] = (torsoRawWeights[i] / axialScoreSum) * axialTarget;
                    }
                    else if (role == TorsoRole.AnchorBridge && hasAnchor)
                    {
                        torsoRawWeights[i] = (torsoRawWeights[i] / anchorScoreSum) * anchorTarget;
                    }
                    else if (hasPeripheral)
                    {
                        torsoRawWeights[i] = (torsoRawWeights[i] / peripheralScoreSum) * peripheralTarget;
                    }
                }

                torsoRawWeightSum += torsoRawWeights[i];
            }

            torsoRelativeWeightSum = torsoRawWeightSum > 1e-6f ? torsoRawWeightSum : 1.0f;

            #endregion
        }

        #endregion

        #region 2. MATHEMATICAL FAST-POW HELPER

        /// <summary>
        /// Fast approximation of raising a base value to a given exponent.
        /// </summary>
        /// <param name="baseVal">The base value.</param>
        /// <param name="exp">The exponent.</param>
        /// <returns>The result of baseVal raised to the power of exp.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastPow(float baseVal, float exp)
        {
            if (exp == 1.0f) return baseVal;
            if (exp == 0.0f) return 1.0f;
            if (exp == 2.0f) return baseVal * baseVal;
            if (exp == 0.5f) return (float)Math.Sqrt(baseVal);
            return (float)Math.Pow(baseVal, exp);
        }

        #endregion
    }
}
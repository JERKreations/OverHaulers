using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TOPO-SUB-03] AXIAL TRUNK TOPOLOGY SUB-COMPILER
    /// Stateless sub-compiler distributing structural load budgets across spinal columns, necks, pelves, and chest carapaces.
    /// Resides under Source/Core/Topology/SubCompilers/.
    /// </summary>
    public static class Pass03_AxialTrunkSubCompiler
    {
        #region 1. [TOPO-SUB-03] PASS 3: AXIAL TORSO ROLES & HP DENSITY COMPILER

        /// <summary>
        /// [PASS 3] Distributes the Torso structural budget across spinal, pelvic, neck, and rib segments.
        /// Applies the biomechanical HitPoint density power curve:
        /// <code>RawWeight = RoleWeight * FastPow(PartHP / CoreHP, TorsoHpSensitivity) / SiblingDivisor</code>
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
            //  correctly identified.
            if (corePart != null)
            {
                int coreIdx = template.GetPartIndex(corePart);
                if (coreIdx != -1 && resolvedTypes[coreIdx] == PartType.CorePart)
                {
                    torsoRoles[coreIdx] = TorsoRole.AxialColumn;
                }
            }

            // Traverse all parts to ensure that any core parts that are part of the trunk are correctly marked as axial columns. This helps in
            //  establishing a continuous axial structure for the torso.
            for (int i = 0; i < partCount; i++)
            {
                if (resolvedTypes[i] == PartType.CorePart && TopologyLayoutCompiler.IsTrunkDef(bodyDef.AllParts[i].def))
                {
                    torsoRoles[i] = TorsoRole.AxialColumn;
                }
            }

            bool hasAnchorBridges = false;
            // Iterate through all parts to identify and mark anchor bridges for limb attachment points. This ensures that limbs have a stable
            //  connection to the axial structure of the torso.
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
                                    hasAnchorBridges = true;
                                }
                            }
                            curr = curr.parent;
                        }
                    }
                }
            }

            // At this point, all anchor bridges have been identified and marked, ensuring that limbs have a stable connection to the axial structure
            //  of the torso.
            #endregion

            #region 1B. ModExtension XML Overrides & Fallback

            // Apply any ModExtension XML overrides to the torso roles, ensuring that custom definitions are respected.
            // If no overrides are present, fallback to the previously determined roles based on axial columns and anchor bridges.
            for (int i = 0; i < partCount; i++)
            {
                if (resolvedTypes[i] == PartType.CorePart)
                {
                    var ext = bodyDef.AllParts[i].def?.GetCachedModExtension();
                    if (ext != null && ext.torsoRole != TorsoRole.Peripheral)
                    {
                        torsoRoles[i] = ext.torsoRole;
                    }
                }
            }

            // Ensure that there is at least one axial column in the torso. If none exists, promote an anchor bridge to an axial column.
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

            // At this point, all torso roles have been finalized, with ModExtension overrides applied and fallback logic ensuring a valid
            //  axial structure.
            #endregion

            #region 1C. Biomechanical HP Density Weight Calculation

            float axialBias = OverHaulers.settings?.torsoAxialBias ?? SettingsDefaults.TorsoAxialBias;
            float hpSensitivity = OverHaulers.settings?.torsoHpSensitivity ?? SettingsDefaults.TorsoHpSensitivity;

            float remainingBudget = 1.0f - axialBias;
            float anchorWeightRole = hasAnchorBridges ? (remainingBudget * 0.60f) : 0f;
            float peripheralWeightRole = hasAnchorBridges ? (remainingBudget * 0.40f) : remainingBudget;

            float coreHp = (corePart != null && corePart.def != null && corePart.def.hitPoints > 0) ? (float)corePart.def.hitPoints : 100f;

            float torsoRawWeightSum = 0f;
            torsoRawWeights = new float[partCount];

            // Iterate through all torso parts to calculate their raw weights based on role, hit points, and ModExtension overrides.
            for (int i = 0; i < partCount; i++)
            {
                if (resolvedTypes[i] == PartType.CorePart)
                {
                    BodyPartRecord part = bodyDef.AllParts[i];

                    // Zero-coverage virtual parts carry exactly 0.0f raw weight
                    if (part.coverageAbs <= 0f && part != corePart)
                    {
                        torsoRawWeights[i] = 0f;
                        continue;
                    }

                    var ext = part.def?.GetCachedModExtension();

                    // Determine the raw weight for the torso part, considering ModExtension overrides and role-based calculations.
                    float rawWeight;
                    if (ext != null && ext.relativeTorsoWeight >= 0f)
                    {
                        rawWeight = ext.relativeTorsoWeight / tempPartTopologies[i].SafeSiblingDivisor;
                    }
                    else
                    {
                        float roleWeight = axialBias;
                        if (torsoRoles[i] == TorsoRole.AnchorBridge) roleWeight = anchorWeightRole;
                        else if (torsoRoles[i] == TorsoRole.Peripheral) roleWeight = peripheralWeightRole;

                        float partHp = (part.def != null && part.def.hitPoints > 0) ? (float)part.def.hitPoints : 10f;
                        float hpRatio = partHp / coreHp;
                        float hpFactor = FastPow(hpRatio, hpSensitivity);

                        rawWeight = roleWeight * hpFactor / tempPartTopologies[i].SafeSiblingDivisor;
                    }

                    torsoRawWeights[i] = rawWeight;
                    torsoRawWeightSum += rawWeight;
                }
            }

            torsoRelativeWeightSum = Math.Max(0.1f, torsoRawWeightSum);

            // Finalize the torso weight calculations, ensuring that the relative weight sum is at least 0.1f.
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
            if (exp == 0.5f) return Mathf.Sqrt(baseVal);
            return Mathf.Pow(baseVal, exp);
        }

        #endregion
    }
}
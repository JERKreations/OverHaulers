using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [PASS-01] UNIFIED INGRESS TRANSLATION & TOPOLOGICAL SWEEP SOLVER
    /// Single source of truth translating Pawn HediffSets onto contiguous SoA workspace arrays.
    /// Resides under Source/Core/Solvers/.
    /// </summary>
    public static class AnatomicalIngressSolver
    {
        // Detailed-model ingress only runs on the main thread (MassCapacitySolver -> PawnDataRegistry cache-miss path).
        private static readonly List<PawnCapacityUtility.CapacityImpactor> pooledImpactors =
            new List<PawnCapacityUtility.CapacityImpactor>(16);

        #region 1. PUBLIC INGRESS ENTRY POINT

        /// <summary>
        /// [PASS-01] Translates a Pawn's live hediff set into the workspace and executes unified graph sweeps.
        /// </summary>
        /// <param name="pawn">The pawn whose hediff set is being translated.</param>
        /// <param name="safeHediffs">The list of hediffs considered safe for translation.</param>
        /// <param name="workspace">The anatomical workspace to be updated.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI properties should be compiled during translation.</param>
        /// <remarks>
        /// This method is intended to be called on the main thread and assumes that the provided hediffs are safe for translation.
        /// </remarks>
        public static void IngressLivePawnPathology(
            Pawn pawn,
            List<Hediff> safeHediffs,
            AnatomicalWorkspace workspace,
            bool shouldCompileUIProperties)
        {
            if (pawn == null || safeHediffs == null || workspace == null) return;

            // Stage 1: Hediff Pathology Stamping & Systemic Ailment Extraction
            StampLiveHediffs(pawn, safeHediffs, workspace, shouldCompileUIProperties);

            // Stage 2: Unified Topological Graph Sweeps
            FinalizeTopologicalSweeps(pawn, workspace, shouldCompileUIProperties);
        }

        #endregion

        #region 2. STAGE 1: HEDIFF TRANSLATION

        /// <summary>
        /// Translates a pawn's live hediff set into the anatomical workspace, updating part states, efficiencies, and systemic ailments.
        /// </summary>
        /// <param name="pawn">The pawn whose hediff set is being translated.</param>
        /// <param name="safeHediffs">The list of hediffs considered safe for translation.</param>
        /// <param name="workspace">The anatomical workspace to be updated.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI properties should be compiled during translation.</param>
        /// <remarks>
        /// This method is intended to be called on the main thread and assumes that the provided hediffs are safe for translation.
        /// </remarks>
        private static void StampLiveHediffs(
            Pawn pawn,
            List<Hediff> safeHediffs,
            AnatomicalWorkspace workspace,
            bool shouldCompileUIProperties)
        {
            int partCount = workspace.PartCount;
            float[] injuryScratchBuffer = workspace.FloatScratchBuffer;
            Array.Clear(injuryScratchBuffer, 0, partCount);

            // Scratch buffers for workspace data
            var flags = workspace.Flags;
            var healths = workspace.HealthFractions;
            var effs = workspace.EfficiencyRatings;
            var athEffs = workspace.AthleticEfficiencies;
            var coldStates = workspace.PartStatesColdArray;

            float consciousness = MedicalClassifier.GetCapacityLevel(pawn, PawnCapacityDefOf.Consciousness);
            bool consciousnessIsDropped = consciousness < 1.0f - SettingsDefaults.EfficiencyEpsilon;

            // Ingress Native Consciousness Impactors at the translation boundary
            if (shouldCompileUIProperties && consciousnessIsDropped && pawn.health?.hediffSet != null)
            {
                pooledImpactors.Clear();
                try
                {
                    PawnCapacityUtility.CalculateCapacityLevel(
                        pawn.health.hediffSet, 
                        PawnCapacityDefOf.Consciousness, 
                        pooledImpactors
                    );

                    // Ingress each native consciousness impactor
                    for (int j = 0; j < pooledImpactors.Count; j++)
                    {
                        workspace.AddSystemicAilmentUnique(pooledImpactors[j].Readable(pawn));
                    }
                }
                catch (Exception ex)
                {
                    // Avoid pawn.LabelShortCap here: it can itself throw from third-party label patches while already handling an exception.
                    string pawnLabel = pawn.def?.label ?? "Unknown";
                    OHLog.Solver.Warn("StampLiveHediffs", ex, $"An error occurred while extracting systemic ailment impactors for {pawnLabel}");
                }
            }

            // Ingress each safe hediff
            for (int i = 0; i < safeHediffs.Count; i++)
            {
                Hediff hediff = safeHediffs[i];
                if (hediff?.def == null) continue;

                // =========================================================================
                // Branch A: Whole-Body Systemic Hediffs (Part == null)
                // =========================================================================
                if (hediff.Part == null)
                {
                    if (shouldCompileUIProperties && MedicalClassifier.IsActivePathologyOrSubstance(hediff, consciousnessIsDropped))
                    {
                        workspace.AddSystemicAilmentUnique(hediff.LabelCap);
                    }
                    continue;
                }

                // =========================================================================
                // Branch B: Part-Specific Hediffs (Part != null)
                // =========================================================================
                int partIndex = workspace.GetPartIndex(hediff.Part);
                if (partIndex < 0 || partIndex >= partCount) continue;

                ref PartStateCold coldState = ref coldStates[partIndex];

                // 1. Missing Part
                if (hediff is Hediff_MissingPart)
                {
                    workspace.SetFlag(partIndex, PartFlags.IsMissing, true);
                    healths[partIndex] = 0f;
                }
                // 2. Added Artificial Part (Bionic / Prosthetic / Utility Limb) - Unconditional Ingress
                else if (hediff is Hediff_AddedPart addedPart)
                {
                    float efficiency = addedPart.def?.addedPartProps?.partEfficiency ?? 1.0f;
                    ImplantCharacteristics characteristics = MedicalClassifier.EvaluateHediffCapacities(addedPart.def);

                    workspace.SetFlag(partIndex, PartFlags.HasAddedPart, true);
                    effs[partIndex] = efficiency;

                    // Ingress each added part
                    if (characteristics.AffectsAthletics)
                    {
                        workspace.SetFlag(partIndex, PartFlags.HasAthleticImplant, true);
                        athEffs[partIndex] = characteristics.EfficiencyModifier;
                    }

                    // Compile UI properties for the added part
                    if (shouldCompileUIProperties)
                    {
                        coldState.ProstheticName = addedPart.LabelCap;
                        coldState.ProstheticColor = addedPart.def?.defaultLabelColor ?? Color.white;

                        if (characteristics.AffectsAthletics)
                        {
                            coldState.AthleticImplantName = addedPart.LabelCap;
                            coldState.AthleticImplantColor = addedPart.def?.defaultLabelColor ?? Color.white;
                        }
                    }
                }
                // 3. Organ / Tissue Implant
                else if (hediff is Hediff_Implant implant)
                {
                    ImplantCharacteristics characteristics = MedicalClassifier.EvaluateHediffCapacities(implant.def);

                    // Ingress each organ/tissue implant
                    if (characteristics.AffectsAthletics || Math.Abs(characteristics.EfficiencyModifier - 1.0f) >= SettingsDefaults.EfficiencyEpsilon)
                    {
                        workspace.SetFlag(partIndex, PartFlags.HasAthleticImplant, true);
                        athEffs[partIndex] = characteristics.EfficiencyModifier;

                        // Compile UI properties for the implant
                        if (shouldCompileUIProperties)
                        {
                            coldState.AthleticImplantName = implant.LabelCap;
                            coldState.AthleticImplantColor = implant.def?.defaultLabelColor ?? Color.white;
                        }
                    }
                }
                // 4. Physical Injury / Direct Wound
                else if (hediff is Hediff_Injury injury)
                {
                    workspace.SetFlag(partIndex, PartFlags.HasDirectDamage, true);
                    injuryScratchBuffer[partIndex] += injury.Severity;
                }
                // 5. Localized Medical Pathology / Infection on a specific part
                else if (MedicalClassifier.IsActivePathologyOrSubstance(hediff, consciousnessIsDropped))
                {
                    workspace.SetFlag(partIndex, PartFlags.HasDirectDamage, true);

                    // Compile UI properties for the localized ailment
                    if (shouldCompileUIProperties)
                    {
                        coldState.LocalAilmentName = hediff.LabelCap;
                        coldState.LocalAilmentColor = hediff.def?.defaultLabelColor ?? Color.white;
                    }
                }
            }
        }

        #endregion

        #region 3. STAGE 2: TOPOLOGICAL GRAPH SWEEPS

        /// <summary>
        /// Finalizes the topological sweeps of the anatomical workspace, updating part states and efficiencies based on the pawn's current condition.
        /// </summary>
        /// <param name="pawn">The pawn whose anatomical workspace is being finalized.</param>
        /// <param name="workspace">The anatomical workspace containing part states and efficiencies.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI properties should be compiled during the finalization process.</param>
        private static void FinalizeTopologicalSweeps(
            Pawn pawn,
            AnatomicalWorkspace workspace,
            bool shouldCompileUIProperties)
        {
            int partCount = workspace.PartCount;
            var healths = workspace.HealthFractions;
            var effs = workspace.EfficiencyRatings;
            var coldStates = workspace.PartStatesColdArray;
            int[] parentIndices = workspace.ParentIndices;
            int[] depthSorted = workspace.DepthSortedIndices;

            // =========================================================================
            // Sweep 1: Injury Health Fraction Normalization
            // =========================================================================
            if (pawn != null)
            {
                float[] injuryScratchBuffer = workspace.FloatScratchBuffer;

                // Ingress each injury
                for (int i = 0; i < partCount; i++)
                {
                    if (workspace.HasFlag(i, PartFlags.HasDirectDamage))
                    {
                        BodyPartRecord part = workspace.GetPartRecord(i);

                        // Normalize injury health fraction
                        if (part != null)
                        {
                            float maxHp = part.def.GetMaxHealth(pawn);

                            // Calculate current health based on lost HP
                            if (maxHp > 0f)
                            {
                                float lostHp = injuryScratchBuffer[i];
                                float currentHp = Math.Max(0f, maxHp - lostHp);
                                healths[i] = Mathf.Clamp01(currentHp / maxHp);
                            }
                            // Fallback for parts with no defined max health
                            else
                            {
                                healths[i] = 1.0f;
                            }
                        }

                        if (workspace.HasFlag(i, PartFlags.HasAddedPart))
                        {
                            effs[i] *= healths[i]; // Adjust efficiency based on normalized health fraction
                        }
                    }
                }
            }

            // =========================================================================
            // Sweep 2: Unified Top-Down Graph State Propagation
            // =========================================================================
            for (int d = 0; d < partCount; d++)
            {
                int i = depthSorted != null ? depthSorted[d] : d;
                int parentIdx = parentIndices[i];

                // Propagate state from parent to child
                if (parentIdx >= 0 && parentIdx < partCount)
                {
                    bool parentAdded = workspace.HasFlag(parentIdx, PartFlags.HasAddedPart);
                    bool parentMissing = workspace.HasFlag(parentIdx, PartFlags.IsMissing);

                    // Case A: Parent is replaced by a Prosthetic
                    if (parentAdded)
                    {
                        PartType type = workspace.PartTypes[i];
                        bool isLimb = (type == PartType.ManipulationPart || type == PartType.MovingPart || type == PartType.DualLimb);

                        // Case A-i: Parent is a prosthetic and the current part is a limb
                        if (isLimb)
                        {
                            if (!workspace.HasFlag(i, PartFlags.HasAddedPart))
                            {
                                workspace.SetFlag(i, PartFlags.HasAddedPart, true);
                                workspace.SetFlag(i, PartFlags.ProstheticIsInherited, true);
                                effs[i] = effs[parentIdx];
                                healths[i] = 1.0f;
                                workspace.SetFlag(i, PartFlags.IsMissing, false);
                                workspace.SetFlag(i, PartFlags.HasDirectDamage, false);
                                workspace.SetFlag(i, PartFlags.IsNeutralized, false);

                                // Compile UI properties for the added limb
                                if (shouldCompileUIProperties)
                                {
                                    coldStates[i].ProstheticName = coldStates[parentIdx].ProstheticName;
                                    coldStates[i].ProstheticColor = coldStates[parentIdx].ProstheticColor;
                                    coldStates[i].LocalAilmentName = null;
                                }
                            }
                        }
                        // Case A-ii: Parent is a prosthetic but the current part is not a limb
                        else
                        {
                            if (!workspace.HasFlag(i, PartFlags.HasAddedPart))
                            {
                                workspace.SetFlag(i, PartFlags.IsMissing, true);
                                workspace.SetFlag(i, PartFlags.IsNeutralized, true);
                                healths[i] = 0f;
                            }
                        }
                    }
                    // Case B: Parent is genuinely Missing (Amputated)
                    else if (parentMissing)
                    {
                        if (!workspace.HasFlag(i, PartFlags.HasAddedPart))
                        {
                            workspace.SetFlag(i, PartFlags.IsMissing, true);
                            healths[i] = 0f;
                        }
                    }
                }
            }
        }

        #endregion
    }
}
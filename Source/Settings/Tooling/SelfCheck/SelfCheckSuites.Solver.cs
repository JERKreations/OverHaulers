using System;
using RimWorld;
using Verse;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. SOLVER CHECKS

        /// <summary>
        /// Builds a sandbox harness bound to a subject, optionally with the representative wound set applied.
        /// </summary>
        /// <param name="subject">The test subject to mirror.</param>
        /// <param name="wounded">True to apply the representative wounds.</param>
        /// <returns>A valid, bound harness; the caller must dispose it.</returns>
        private static SandboxPawnHarness CreateHarness(TestSubjectEntry subject, bool wounded)
        {
            SandboxPawnHarness harness = SandboxPawnHarness.CreateForCurrentProgramState();

            harness.BindSubject(subject);
            if (!harness.IsValid)
            {
                harness.Dispose();
                throw new SelfCheckFailedException("could not build a sandbox pawn for " + subject.Label);
            }

            if (wounded)
            {
                harness.SimulateRepresentativeWounds();
            }

            return harness;
        }

        /// <summary>
        /// Solves a sandbox pawn once and verifies the result is finite.
        /// Workspace lease lifecycle is managed and released internally by the solver.
        /// </summary>
        /// <param name="pawn">The sandbox pawn to solve.</param>
        /// <param name="baseline">The species baseline mass capacity in kg.</param>
        /// <returns>The solved caravan mass capacity offset in kg.</returns>
        private static float SolveOnce(Pawn pawn, float baseline)
        {
            MassCapacityEvaluationResult result = MassCapacitySolver.EvaluateMassCapacityForPawn(
                pawn, baseline);

            Expect.True(!float.IsNaN(result.SolvedOffset) && !float.IsInfinity(result.SolvedOffset), "the solver produced a non-finite offset");
            return result.SolvedOffset;
        }

        /// <summary>
        /// Verifies that solving the same unchanged pawn repeatedly returns identical results.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckSolverDeterminism(SelfCheckContext ctx)
        {
            TestSubjectEntry subject = TestSubjectRegistry.GetDefaultSubject();
            if (subject?.BodyDef == null)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoSubject".Translate().ToString());
                return;
            }

            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: false))
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
                float first = SolveOnce(harness.SandboxPawn, baseline);

                for (int i = 0; i < 3; i++)
                {
                    Expect.Equal(first, SolveOnce(harness.SandboxPawn, baseline), "solve " + (i + 2) + " vs solve 1");
                }

                ctx.Note("OverHaulers_SelfCheck_Note_SolverDeterminism".Translate(subject.Label, baseline.ToString("F3"), first.ToString("F3")).ToString());
            }
        }

        /// <summary>
        /// Verifies that applying the representative wounds never increases the solved caravan mass capacity offset.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckWoundsNeverIncrease(SelfCheckContext ctx)
        {
            TestSubjectEntry subject = TestSubjectRegistry.GetDefaultSubject();
            if (subject?.BodyDef == null)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoSubject".Translate().ToString());
                return;
            }

            float pristine;
            float wounded;

            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: false))
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
                pristine = SolveOnce(harness.SandboxPawn, baseline);
            }

            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: true))
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
                wounded = SolveOnce(harness.SandboxPawn, baseline);
            }

            Expect.True(wounded <= pristine + PawnDataRegistry.EfficiencyEpsilon, "wounded offset " + wounded.ToString("R") + " kg exceeds pristine offset " + pristine.ToString("R") + " kg (tolerance " + PawnDataRegistry.EfficiencyEpsilon.ToString("R") + " kg)");
            ctx.Note("OverHaulers_SelfCheck_Note_WoundsNeverIncrease".Translate(subject.Label, pristine.ToString("F3"), wounded.ToString("F3")).ToString());
        }

        /// <summary>
        /// Builds a self-contained synthetic 5-part topology (1 Torso + 4 DualLimbs) for zero-side-effect solver checks.
        /// Never registers into TopologyLayoutCompiler caches.
        /// </summary>
        private static SpeciesTopologyTemplate BuildSyntheticDualLimbTemplate()
        {
            BodyDef syntheticBody = new BodyDef { defName = "OH_SelfCheck_DualLimbFixture" };
            BodyPartRecord torso = new BodyPartRecord { def = BodyPartDefOf.Torso, body = syntheticBody, coverageAbs = 0.50f };
            syntheticBody.corePart = torso;
            syntheticBody.AllParts.Add(torso);

            for (int i = 0; i < 4; i++)
            {
                BodyPartRecord limb = new BodyPartRecord
                {
                    def = BodyPartDefOf.Leg,
                    body = syntheticBody,
                    parent = torso,
                    coverageAbs = 0.125f
                };
                torso.parts.Add(limb);
                syntheticBody.AllParts.Add(limb);
            }

            SpeciesTopologyTemplate template = new SpeciesTopologyTemplate(syntheticBody);
            template.Counts = new PartCounts { CoreParts = 1, DualLimbs = 4 };

            template.PartTypes[0] = PartType.CorePart;
            template.ParentIndices[0] = -1;
            template.DepthSortedIndices[0] = 0;
            template.WeightCore[0] = 1.0f;
            template.StaticWeightFactors[0] = 0.50f;

            for (int i = 1; i <= 4; i++)
            {
                template.PartTypes[i] = PartType.DualLimb;
                template.ParentIndices[i] = 0;
                template.DepthSortedIndices[i] = i;
                template.WeightManipulation[i] = 0.25f;
                template.WeightMoving[i] = 0.25f;
                // Default 15% Manip + 35% Moving = 50% total across 4 DualLimbs -> 12.5% each
                template.StaticWeightFactors[i] = 0.125f;
            }

            return template;
        }

        /// <summary>
        /// Verifies that amputating all dual limbs under asymmetric regional budgets (15% / 35%) deducts
        /// the full 50% limb playable budget rather than prematurely clamping at the manipulation ceiling.
        /// </summary>
        private static void CheckDualLimbBudgetConservation(SelfCheckContext ctx)
        {
            SpeciesTopologyTemplate template = BuildSyntheticDualLimbTemplate();
            SolverConfiguration config = CoreDefaults.CreateDefaultSolverConfiguration();

            BiologicalCapacitySnapshot neutralSnapshot = new BiologicalCapacitySnapshot
            {
                Breathing = 1.0f,
                BloodPumping = 1.0f,
                Moving = 1.0f,
                Manipulation = 1.0f,
                Consciousness = 1.0f
            };

            SystemicEvaluationContext evalContext = SystemicEvaluationContext.CreateFromSnapshot(
                100.0f, template.Counts, in config, in neutralSnapshot);

            using (WorkspacePool.WorkspaceLease lease = WorkspacePool.LeaseWorkspace())
            {
                AnatomicalWorkspace workspace = lease.Workspace;
                workspace.InitializeForPawn(template);
                workspace.EnsureArraySizes(template.PartCount);
                workspace.ResetAllSlots(template.PartCount);

                for (int i = 1; i <= 4; i++)
                {
                    workspace.SetFlag(i, PartFlags.IsMissing, true);
                    workspace.HealthFractions[i] = 0f;
                }

                MassCapacityEvaluationResult result = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_DualLimb");

                // 100 kg baseline, 20% floor (20 kg) => 80 kg playable budget; 50% limbs = -40 kg
                Expect.True(Math.Abs(result.SolvedOffset - (-40.0f)) < 0.005f,
                    "full dual-limb loss must deduct full 50% limb budget (-40 kg), got " + result.SolvedOffset.ToString("F4"));

                float summedParts = 0f;
                for (int i = 0; i < workspace.PartCount; i++)
                {
                    summedParts += workspace.CalculatedTotals[i];
                }

                Expect.True(Math.Abs(summedParts - result.SolvedOffset) < 0.005f,
                    "per-part totals must equal net biological offset (" + summedParts.ToString("F4") + " vs " + result.SolvedOffset.ToString("F4") + ")");
            }
        }

        /// <summary>
        /// Verifies that damaged prosthetics scale operational performance inside ProstheticOffset,
        /// remain vulnerable to physical damage even when prosthetic scaling is disabled,
        /// and receive zero athletic coupling in either boost or deficit directions.
        /// </summary>
        private static void CheckProstheticDamageAndAthletics(SelfCheckContext ctx)
        {
            SpeciesTopologyTemplate template = BuildSyntheticDualLimbTemplate();
            template.Counts = new PartCounts { CoreParts = 1, ManipulationParts = 1, MovingParts = 3 };
            template.PartTypes[1] = PartType.ManipulationPart;
            template.WeightManipulation[1] = 1.0f;
            template.WeightMoving[1] = 0.0f;
            template.StaticWeightFactors[1] = 0.15f;

            for (int i = 2; i <= 4; i++)
            {
                template.PartTypes[i] = PartType.MovingPart;
                template.WeightManipulation[i] = 0.0f;
                template.WeightMoving[i] = 1.0f / 3.0f;
                template.StaticWeightFactors[i] = 0.35f / 3.0f;
            }

            SolverConfiguration enabledProsthetics = CoreDefaults.CreateDefaultSolverConfiguration();
            SolverConfiguration disabledProsthetics = new SolverConfiguration(
                enabledProsthetics.ConfigurablePercentileFloor,
                false, // enableProsthetics = false
                enabledProsthetics.EnableAthletics,
                enabledProsthetics.EnablePartHealth,
                enabledProsthetics.EnableHardCompatibilitySafetyFloor,
                enabledProsthetics.AthleticScaling,
                enabledProsthetics.AthleticImpactConstant,
                enabledProsthetics.ProstheticScaling,
                enabledProsthetics.ProstheticImpactConstant,
                enabledProsthetics.TorsoAxialBias,
                enabledProsthetics.TorsoHpSensitivity,
                enabledProsthetics.LimbDepthDecayFactor,
                enabledProsthetics.TorsoPositiveBreathing, enabledProsthetics.TorsoPositiveBlood, enabledProsthetics.TorsoPositiveMoving, enabledProsthetics.TorsoPositiveManipulation,
                enabledProsthetics.ArmPositiveBreathing,   enabledProsthetics.ArmPositiveBlood,   enabledProsthetics.ArmPositiveMoving,   enabledProsthetics.ArmPositiveManipulation,
                enabledProsthetics.LegPositiveBreathing,   enabledProsthetics.LegPositiveBlood,   enabledProsthetics.LegPositiveMoving,   enabledProsthetics.LegPositiveManipulation,
                enabledProsthetics.TorsoDeficitBreathing,  enabledProsthetics.TorsoDeficitBlood,  enabledProsthetics.TorsoDeficitMoving,  enabledProsthetics.TorsoDeficitManipulation,
                enabledProsthetics.ArmDeficitBreathing,    enabledProsthetics.ArmDeficitBlood,    enabledProsthetics.ArmDeficitMoving,    enabledProsthetics.ArmDeficitManipulation,
                enabledProsthetics.LegDeficitBreathing,    enabledProsthetics.LegDeficitBlood,    enabledProsthetics.LegDeficitMoving,    enabledProsthetics.LegDeficitManipulation,
                enabledProsthetics.BudgetCorePart,         enabledProsthetics.BudgetManipulationPart, enabledProsthetics.BudgetMovingPart);

            BiologicalCapacitySnapshot boostedSnapshot = new BiologicalCapacitySnapshot
            {
                Breathing = 2.0f,
                BloodPumping = 1.0f,
                Moving = 1.0f,
                Manipulation = 1.0f,
                Consciousness = 1.0f
            };

            BiologicalCapacitySnapshot impairedSnapshot = new BiologicalCapacitySnapshot
            {
                Breathing = 0.5f,
                BloodPumping = 1.0f,
                Moving = 1.0f,
                Manipulation = 1.0f,
                Consciousness = 1.0f
            };

            using (WorkspacePool.WorkspaceLease lease = WorkspacePool.LeaseWorkspace())
            {
                AnatomicalWorkspace workspace = lease.Workspace;
                workspace.InitializeForPawn(template);
                workspace.EnsureArraySizes(template.PartCount);

                // 1. Damaged prosthetic under boosted athletics: negative prosthetic offset and zero athletic offset
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.HealthFractions[1] = 0.40f;
                workspace.EfficiencyRatings[1] = 1.50f * 0.40f; // 0.60 rated * health

                SystemicEvaluationContext ctxBoosted = SystemicEvaluationContext.CreateFromSnapshot(
                    100.0f, template.Counts, in enabledProsthetics, in boostedSnapshot);
                MassCapacitySolver.SolveInternal(workspace, in ctxBoosted, 100.0f, "SelfCheck_ProstheticBoosted");

                Expect.True(workspace.CalculatedProsthetics[1] < 0f,
                    "a 150% prosthetic damaged to 40% HP (60% effective) must produce a negative prosthetic offset");
                Expect.Equal(0f, workspace.CalculatedHealths[1],
                    "prosthetic damage must remain isolated in ProstheticOffset rather than HealthOffset");
                Expect.Equal(0f, workspace.CalculatedAthletics[1],
                    "non-natural parts must receive zero positive athletic coupling");

                // 2. Damaged prosthetic under impaired athletics: zero negative athletic coupling
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.HealthFractions[1] = 0.40f;
                workspace.EfficiencyRatings[1] = 1.50f * 0.40f;

                SystemicEvaluationContext ctxImpaired = SystemicEvaluationContext.CreateFromSnapshot(
                    100.0f, template.Counts, in enabledProsthetics, in impairedSnapshot);
                MassCapacitySolver.SolveInternal(workspace, in ctxImpaired, 100.0f, "SelfCheck_ProstheticImpaired");

                Expect.Equal(0f, workspace.CalculatedAthletics[1],
                    "non-natural parts must receive zero negative athletic coupling");

                // 3. Disabled Prosthetic Scaling + Enabled Physical Deficits: damaged prosthetic must still lose capacity
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.HealthFractions[1] = 0.40f;
                workspace.EfficiencyRatings[1] = 1.50f * 0.40f;

                SystemicEvaluationContext ctxNoProsthetics = SystemicEvaluationContext.CreateFromSnapshot(
                    100.0f, template.Counts, in disabledProsthetics, in impairedSnapshot);
                MassCapacitySolver.SolveInternal(workspace, in ctxNoProsthetics, 100.0f, "SelfCheck_ProstheticDisabled");

                float expectedDisabledPenalty = -(80.0f * 0.15f) * (1.0f - 0.40f);
                Expect.True(Math.Abs(workspace.CalculatedProsthetics[1] - expectedDisabledPenalty) < 0.005f,
                    "disabling prosthetic scaling must not grant wound immunity; expected " + expectedDisabledPenalty.ToString("F3") + " got " + workspace.CalculatedProsthetics[1].ToString("F3"));
            }
        }

        #endregion
    }
}

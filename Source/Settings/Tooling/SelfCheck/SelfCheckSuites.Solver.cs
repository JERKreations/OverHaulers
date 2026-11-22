using System;
using System.Collections.Generic;
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
            SandboxPawnHarness harness = new SandboxPawnHarness();

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

            using (HeadlessPawnShell.EnterSafetyFallbackScope())
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

            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            {
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
                template.RootPartIndex[i] = i;
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
        /// Verifies via the real topology pass pipeline that losing only the primary pair of a 4-legged creature
        /// does not saturate 100% of the Moving deficit ceiling and preserves additive losses across the secondary pair.
        /// </summary>
        private static void CheckQuadrupedLimbDeficitDistribution()
        {
            BodyDef quadBody = new BodyDef { defName = "OH_SelfCheck_QuadrupedFixture" };
            BodyPartRecord torso = new BodyPartRecord { def = BodyPartDefOf.Torso, body = quadBody, coverageAbs = 0.50f };
            quadBody.corePart = torso;
            quadBody.AllParts.Add(torso);

            for (int i = 0; i < 4; i++)
            {
                BodyPartRecord leg = new BodyPartRecord
                {
                    def = BodyPartDefOf.Leg,
                    body = quadBody,
                    parent = torso,
                    coverageAbs = 0.125f
                };
                torso.parts.Add(leg);
                quadBody.AllParts.Add(leg);
            }

            SpeciesTopologyTemplate template = new SpeciesTopologyTemplate(quadBody);
            Pass01_CranialSubCompiler.CompileCranialAncestry(
                quadBody, template, out int[] parentIndices, out bool[] headAncestry, out _);
            PartTopologyInfo[] partTopologies = Pass02_AppendageSubCompiler.CompilePartClassifications(
                quadBody, template, parentIndices, headAncestry, out PartType[] resolvedTypes, out PartCounts counts);
            Pass03_AxialTrunkSubCompiler.CompileTorsoRolesAndWeights(
                quadBody, template, resolvedTypes, partTopologies, out float[] torsoRawWeights, out float torsoRawWeightSum);
            Pass04_AppendageWeightSubCompiler.CompileLimbAndFinalWeights(
                quadBody, template, counts, partTopologies, torsoRawWeights, torsoRawWeightSum);
            template.Counts = counts;

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
            float expectedFullMovingLoss = -80.0f * evalContext.GetNormalizedBudget(PartType.MovingPart);
            float expectedPair0Loss = expectedFullMovingLoss * (1.0f / (1.0f + config.LimbPairDecayFactor));

            using (WorkspacePool.WorkspaceLease lease = WorkspacePool.LeaseWorkspace())
            {
                AnatomicalWorkspace workspace = lease.Workspace;
                workspace.InitializeForPawn(template);
                workspace.EnsureArraySizes(template.PartCount);

                // 1. Amputate only Pair 0 (indices 1 & 2)
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.IsMissing, true);
                workspace.SetFlag(2, PartFlags.IsMissing, true);
                workspace.HealthFractions[1] = 0f;
                workspace.HealthFractions[2] = 0f;

                MassCapacityEvaluationResult pair0Result = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadPair0");
                Expect.True(Math.Abs(pair0Result.SolvedOffset - expectedPair0Loss) < 0.01f,
                    "quadruped front-pair loss must leave secondary pair headroom; expected " + expectedPair0Loss.ToString("F3") + " got " + pair0Result.SolvedOffset.ToString("F3"));

                // 2. Amputate all 4 legs (indices 1..4)
                workspace.SetFlag(3, PartFlags.IsMissing, true);
                workspace.SetFlag(4, PartFlags.IsMissing, true);
                workspace.HealthFractions[3] = 0f;
                workspace.HealthFractions[4] = 0f;

                MassCapacityEvaluationResult allLegsResult = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadAllLegs");
                Expect.True(Math.Abs(allLegsResult.SolvedOffset - expectedFullMovingLoss) < 0.01f,
                    "quadruped total leg loss must reach full moving deficit budget; expected " + expectedFullMovingLoss.ToString("F3") + " got " + allLegsResult.SolvedOffset.ToString("F3"));
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
                // [SEC-01]
                false, // enableProsthetics = false
                enabledProsthetics.EnableAthletics,
                enabledProsthetics.EnablePartHealth,
                enabledProsthetics.ProstheticScaling,
                enabledProsthetics.AthleticScaling,
                enabledProsthetics.ConfigurablePercentileFloor,
                enabledProsthetics.EnableHardCompatibilitySafetyFloor,

                // [SEC-02]
                enabledProsthetics.BudgetCorePart,
                enabledProsthetics.BudgetManipulationPart,
                enabledProsthetics.BudgetMovingPart,
                enabledProsthetics.MissingArmsTorsoShare,
                enabledProsthetics.MissingLegsTorsoShare,

                // [SEC-03]
                enabledProsthetics.TorsoPositiveBreathing, enabledProsthetics.TorsoPositiveBlood, enabledProsthetics.TorsoPositiveMoving, enabledProsthetics.TorsoPositiveManipulation,
                enabledProsthetics.ArmPositiveBreathing,   enabledProsthetics.ArmPositiveBlood,   enabledProsthetics.ArmPositiveMoving,   enabledProsthetics.ArmPositiveManipulation,
                enabledProsthetics.LegPositiveBreathing,   enabledProsthetics.LegPositiveBlood,   enabledProsthetics.LegPositiveMoving,   enabledProsthetics.LegPositiveManipulation,

                // [SEC-04]
                enabledProsthetics.TorsoDeficitBreathing,  enabledProsthetics.TorsoDeficitBlood,  enabledProsthetics.TorsoDeficitMoving,  enabledProsthetics.TorsoDeficitManipulation,
                enabledProsthetics.ArmDeficitBreathing,    enabledProsthetics.ArmDeficitBlood,    enabledProsthetics.ArmDeficitMoving,    enabledProsthetics.ArmDeficitManipulation,
                enabledProsthetics.LegDeficitBreathing,    enabledProsthetics.LegDeficitBlood,    enabledProsthetics.LegDeficitMoving,    enabledProsthetics.LegDeficitManipulation,

                // [SEC-05]
                enabledProsthetics.TorsoAxialBias,
                enabledProsthetics.TorsoHpSensitivity,

                // [SEC-06]
                enabledProsthetics.LimbDepthDecayFactor,
                enabledProsthetics.LimbPairDecayFactor,

                // [SEC-07]
                enabledProsthetics.ProstheticImpactConstant,
                enabledProsthetics.AthleticImpactConstant);

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

        /// <summary>
        /// Verifies via the canonical uncached compiler pipeline that multi-limbed species share equal underlying
        /// weights across limbs of the same type, enforce order-independent 112.5% + 112.5% = 125% symmetry across the
        /// first two boosted branches, and apply diminishing returns solely to 3rd+ boosted branches.
        /// </summary>
        private static void CheckQuadrupedSymmetryAndDeficits(SelfCheckContext ctx)
        {
            BodyDef quadBody = new BodyDef { defName = "OH_SelfCheck_QuadrupedFixture" };
            BodyPartRecord torso = new BodyPartRecord { def = BodyPartDefOf.Torso, body = quadBody, coverageAbs = 0.50f };
            quadBody.corePart = torso;
            quadBody.AllParts.Add(torso);

            BodyPartDef distalLegDef = new BodyPartDef
            {
                defName = "OH_SelfCheck_DistalFoot",
                label = "foot",
                hitPoints = 10,
                tags = new List<BodyPartTagDef>(TopologyLayoutCompiler.legTags)
            };

            // 4 Leg -> Hoof multi-segment branches (indices: Legs 1,3,5,7; Hooves 2,4,6,8)
            for (int i = 0; i < 4; i++)
            {
                BodyPartRecord leg = new BodyPartRecord
                {
                    def = BodyPartDefOf.Leg,
                    body = quadBody,
                    parent = torso,
                    coverageAbs = 0.10f
                };
                torso.parts.Add(leg);
                quadBody.AllParts.Add(leg);

                BodyPartRecord hoof = new BodyPartRecord
                {
                    def = distalLegDef,
                    body = quadBody,
                    parent = leg,
                    coverageAbs = 0.025f
                };
                leg.parts.Add(hoof);
                quadBody.AllParts.Add(hoof);
            }

            SpeciesTopologyTemplate template = TopologyLayoutCompiler.CompileTemplateUncached(quadBody);
            Expect.True(template != null, "failed to compile synthetic quadruped template");

            // Issue #3: Every leg branch must have identical underlying channel weight (0.25 each)
            float branch0Weight = template.WeightMoving[1] + template.WeightMoving[2];
            float branch1Weight = template.WeightMoving[3] + template.WeightMoving[4];
            float branch2Weight = template.WeightMoving[5] + template.WeightMoving[6];
            float branch3Weight = template.WeightMoving[7] + template.WeightMoving[8];
            Expect.True(Math.Abs(branch0Weight - 0.25f) < 0.001f &&
                        Math.Abs(branch1Weight - 0.25f) < 0.001f &&
                        Math.Abs(branch2Weight - 0.25f) < 0.001f &&
                        Math.Abs(branch3Weight - 0.25f) < 0.001f,
                "all quadruped legs must share identical 0.25 underlying moving weights");

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
            float expectedFullMovingLoss = -80.0f * evalContext.GetNormalizedBudget(PartType.MovingPart);
            float expectedHalfMovingLoss = expectedFullMovingLoss * 0.50f;

            using (WorkspacePool.WorkspaceLease lease = WorkspacePool.LeaseWorkspace())
            {
                AnatomicalWorkspace workspace = lease.Workspace;
                workspace.InitializeForPawn(template);
                workspace.EnsureArraySizes(template.PartCount);

                // 1A. Amputating Front Pair (slots 1..4) must equal -50% Moving budget
                workspace.ResetAllSlots(template.PartCount);
                for (int i = 1; i <= 4; i++)
                {
                    workspace.SetFlag(i, PartFlags.IsMissing, true);
                    workspace.HealthFractions[i] = 0f;
                }
                MassCapacityEvaluationResult frontMissing = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadFrontMissing");
                Expect.True(Math.Abs(frontMissing.SolvedOffset - expectedHalfMovingLoss) < 0.01f,
                    "front-pair loss must equal 50% moving deficit; expected " + expectedHalfMovingLoss.ToString("F3") + " got " + frontMissing.SolvedOffset.ToString("F3"));

                // 1B. Amputating Rear Pair (slots 5..8) must match Front Pair exactly
                workspace.ResetAllSlots(template.PartCount);
                for (int i = 5; i <= 8; i++)
                {
                    workspace.SetFlag(i, PartFlags.IsMissing, true);
                    workspace.HealthFractions[i] = 0f;
                }
                MassCapacityEvaluationResult rearMissing = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadRearMissing");
                Expect.True(Math.Abs(rearMissing.SolvedOffset - frontMissing.SolvedOffset) < 0.01f,
                    "rear-pair loss must equal front-pair loss (" + rearMissing.SolvedOffset.ToString("F3") + " vs " + frontMissing.SolvedOffset.ToString("F3") + ")");

                // 1C. Amputating all 4 legs reaches 100% Moving budget
                for (int i = 1; i <= 4; i++)
                {
                    workspace.SetFlag(i, PartFlags.IsMissing, true);
                    workspace.HealthFractions[i] = 0f;
                }
                MassCapacityEvaluationResult allMissing = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadAllMissing");
                Expect.True(Math.Abs(allMissing.SolvedOffset - expectedFullMovingLoss) < 0.01f,
                    "full leg loss must reach 100% moving deficit; expected " + expectedFullMovingLoss.ToString("F3") + " got " + allMissing.SolvedOffset.ToString("F3"));

                // 2. Issues #1 & #2: First two 125% bionic replacements must yield 2x a single replacement regardless of limb indices
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.SetFlag(2, PartFlags.HasAddedPart, true);
                workspace.EfficiencyRatings[1] = 1.25f;
                workspace.EfficiencyRatings[2] = 1.25f;
                float singleBionic = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadSingleBionic").SolvedOffset;

                // Front-Left + Front-Right (branches 0 & 1: slots 1..4)
                workspace.SetFlag(3, PartFlags.HasAddedPart, true);
                workspace.SetFlag(4, PartFlags.HasAddedPart, true);
                workspace.EfficiencyRatings[3] = 1.25f;
                workspace.EfficiencyRatings[4] = 1.25f;
                float frontPairBionic = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadFrontBionic").SolvedOffset;
                Expect.True(Math.Abs(frontPairBionic - (2.0f * singleBionic)) < 0.01f,
                    "first two replacements must double a single replacement (112.5% + 112.5% = 125%)");

                // Front-Left + Rear-Right diagonal (branches 0 & 3: slots 1..2, 7..8)
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.SetFlag(2, PartFlags.HasAddedPart, true);
                workspace.SetFlag(7, PartFlags.HasAddedPart, true);
                workspace.SetFlag(8, PartFlags.HasAddedPart, true);
                workspace.EfficiencyRatings[1] = 1.25f;
                workspace.EfficiencyRatings[2] = 1.25f;
                workspace.EfficiencyRatings[7] = 1.25f;
                workspace.EfficiencyRatings[8] = 1.25f;
                float diagonalPairBionic = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadDiagonalBionic").SolvedOffset;
                Expect.True(Math.Abs(diagonalPairBionic - frontPairBionic) < 0.01f,
                    "diagonal replacements (Front-Left + Rear-Right) must equal Front-Left + Front-Right (" + diagonalPairBionic.ToString("F3") + " vs " + frontPairBionic.ToString("F3") + ")");

                // Rear-Left + Rear-Right (branches 2 & 3: slots 5..8)
                workspace.ResetAllSlots(template.PartCount);
                for (int i = 5; i <= 8; i++)
                {
                    workspace.SetFlag(i, PartFlags.HasAddedPart, true);
                    workspace.EfficiencyRatings[i] = 1.25f;
                }
                float rearPairBionic = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadRearBionic").SolvedOffset;
                Expect.True(Math.Abs(rearPairBionic - frontPairBionic) < 0.01f,
                    "rear replacements (Rear-Left + Rear-Right) must equal Front-Left + Front-Right (" + rearPairBionic.ToString("F3") + " vs " + frontPairBionic.ToString("F3") + ")");

                // All 4 bionic: Pair 0 (1.0) + Pair 1 (LimbPairDecayFactor)
                for (int i = 1; i <= 4; i++)
                {
                    workspace.SetFlag(i, PartFlags.HasAddedPart, true);
                    workspace.EfficiencyRatings[i] = 1.25f;
                }
                float allFourBionic = MassCapacitySolver.SolveInternal(
                    workspace, in evalContext, 100.0f, "SelfCheck_QuadAllBionic").SolvedOffset;
                float expectedAllFour = frontPairBionic * (1.0f + config.LimbPairDecayFactor);
                Expect.True(Math.Abs(allFourBionic - expectedAllFour) < 0.01f,
                    "4-leg bionic boost must apply LimbPairDecayFactor to the 3rd & 4th legs (" + allFourBionic.ToString("F3") + " vs " + expectedAllFour.ToString("F3") + ")");
            }
        }

        /// <summary>
        /// Verifies that losing a distal limb segment (Hand) beneath an intact opposite root (Shoulder)
        /// proportionally scales down bilateral bionic arm synergy rather than treating the stump as fully intact.
        /// </summary>
        private static void CheckSubRootBilateralSymmetry(SelfCheckContext ctx)
        {
            SolverConfiguration config = CoreDefaults.CreateDefaultSolverConfiguration();
            BiologicalCapacitySnapshot snapshot = new BiologicalCapacitySnapshot
            {
                Breathing = 2.0f,
                BloodPumping = 1.0f,
                Moving = 1.0f,
                Manipulation = 1.0f,
                Consciousness = 1.0f
            };

            BodyDef bipedHands = new BodyDef { defName = "OH_SelfCheck_SubRootSymmetry" };
            BodyPartRecord torso = new BodyPartRecord { def = BodyPartDefOf.Torso, body = bipedHands, coverageAbs = 0.50f };
            BodyPartRecord leftShoulder = new BodyPartRecord { def = BodyPartDefOf.Shoulder, body = bipedHands, parent = torso, coverageAbs = 0.10f };
            BodyPartRecord rightShoulder = new BodyPartRecord { def = BodyPartDefOf.Shoulder, body = bipedHands, parent = torso, coverageAbs = 0.10f };
            BodyPartRecord rightHand = new BodyPartRecord { def = BodyPartDefOf.Hand, body = bipedHands, parent = rightShoulder, coverageAbs = 0.05f };

            torso.parts.Add(leftShoulder);
            torso.parts.Add(rightShoulder);
            rightShoulder.parts.Add(rightHand);
            bipedHands.corePart = torso;
            bipedHands.AllParts.Add(torso);
            bipedHands.AllParts.Add(leftShoulder);
            bipedHands.AllParts.Add(rightShoulder);
            bipedHands.AllParts.Add(rightHand);

            SpeciesTopologyTemplate template = new SpeciesTopologyTemplate(bipedHands);
            template.Counts = new PartCounts { CoreParts = 1, ManipulationParts = 2 };
            template.PartTypes[0] = PartType.CorePart;
            template.ParentIndices[0] = -1;
            template.DepthSortedIndices[0] = 0;
            template.WeightCore[0] = 1.0f;
            template.StaticWeightFactors[0] = 0.50f;

            // Left branch (idx 1): single manipulation root worth 0.50
            template.PartTypes[1] = PartType.ManipulationPart;
            template.ParentIndices[1] = 0;
            template.DepthSortedIndices[1] = 1;
            template.RootPartIndex[1] = 1;
            template.WeightManipulation[1] = 0.50f;
            template.StaticWeightFactors[1] = 0.50f * 0.15f;

            // Right branch (idx 2 = root 0.30, idx 3 = distal hand 0.20)
            template.PartTypes[2] = PartType.ManipulationPart;
            template.ParentIndices[2] = 0;
            template.DepthSortedIndices[2] = 2;
            template.RootPartIndex[2] = 2;
            template.WeightManipulation[2] = 0.30f;
            template.StaticWeightFactors[2] = 0.30f * 0.15f;

            template.PartTypes[3] = PartType.ManipulationPart;
            template.ParentIndices[3] = 2;
            template.DepthSortedIndices[3] = 3;
            template.RootPartIndex[3] = 2;
            template.WeightManipulation[3] = 0.20f;
            template.StaticWeightFactors[3] = 0.20f * 0.15f;

            SystemicEvaluationContext evalContext = SystemicEvaluationContext.CreateFromSnapshot(
                100.0f, template.Counts, in config, in snapshot);

            using (WorkspacePool.WorkspaceLease lease = WorkspacePool.LeaseWorkspace())
            {
                AnatomicalWorkspace workspace = lease.Workspace;
                workspace.InitializeForPawn(template);
                workspace.EnsureArraySizes(template.PartCount);

                // Baseline: Left 150% bionic arm, both arms intact
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.EfficiencyRatings[1] = 1.50f;
                MassCapacitySolver.SolveInternal(workspace, in evalContext, 100.0f, "SelfCheck_BilateralIntact");
                float intactBoost = workspace.CalculatedProsthetics[1];

                // Amputate distal Right Hand (idx 3) while Right Shoulder (idx 2) remains intact
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.EfficiencyRatings[1] = 1.50f;
                workspace.SetFlag(3, PartFlags.IsMissing, true);
                workspace.HealthFractions[3] = 0f;
                MassCapacitySolver.SolveInternal(workspace, in evalContext, 100.0f, "SelfCheck_BilateralDistalLoss");
                float distalLossBoost = workspace.CalculatedProsthetics[1];

                Expect.True(distalLossBoost < intactBoost - 0.01f,
                    "missing opposite distal segment must attenuate bilateral prosthetic boost (" + distalLossBoost.ToString("F3") + " vs " + intactBoost.ToString("F3") + ")");
            }
        }

        /// <summary>
        /// Verifies that losing a distal limb segment (Hand) beneath an intact opposite root (Shoulder)
        /// proportionally scales down bilateral bionic arm synergy rather than treating the stump as fully intact.
        /// </summary>
        private static void CheckSubRootBilateralSymmetry(
            in SolverConfiguration config,
            in BiologicalCapacitySnapshot snapshot)
        {
            BodyDef bipedHands = new BodyDef { defName = "OH_SelfCheck_SubRootSymmetry" };
            BodyPartRecord torso = new BodyPartRecord { def = BodyPartDefOf.Torso, body = bipedHands, coverageAbs = 0.50f };
            BodyPartRecord leftShoulder = new BodyPartRecord { def = BodyPartDefOf.Shoulder, body = bipedHands, parent = torso, coverageAbs = 0.10f };
            BodyPartRecord rightShoulder = new BodyPartRecord { def = BodyPartDefOf.Shoulder, body = bipedHands, parent = torso, coverageAbs = 0.10f };
            BodyPartRecord rightHand = new BodyPartRecord { def = BodyPartDefOf.Hand, body = bipedHands, parent = rightShoulder, coverageAbs = 0.05f };

            torso.parts.Add(leftShoulder);
            torso.parts.Add(rightShoulder);
            rightShoulder.parts.Add(rightHand);
            bipedHands.corePart = torso;
            bipedHands.AllParts.Add(torso);
            bipedHands.AllParts.Add(leftShoulder);
            bipedHands.AllParts.Add(rightShoulder);
            bipedHands.AllParts.Add(rightHand);

            SpeciesTopologyTemplate template = new SpeciesTopologyTemplate(bipedHands);
            template.Counts = new PartCounts { CoreParts = 1, ManipulationParts = 2 };
            template.PartTypes[0] = PartType.CorePart;
            template.ParentIndices[0] = -1;
            template.DepthSortedIndices[0] = 0;
            template.WeightCore[0] = 1.0f;
            template.StaticWeightFactors[0] = 0.50f;

            // Left branch (idx 1): single manipulation root worth 0.50
            template.PartTypes[1] = PartType.ManipulationPart;
            template.ParentIndices[1] = 0;
            template.DepthSortedIndices[1] = 1;
            template.WeightManipulation[1] = 0.50f;
            template.StaticWeightFactors[1] = 0.50f * 0.15f;

            // Right branch (idx 2 = root 0.30, idx 3 = distal hand 0.20)
            template.PartTypes[2] = PartType.ManipulationPart;
            template.ParentIndices[2] = 0;
            template.DepthSortedIndices[2] = 2;
            template.WeightManipulation[2] = 0.30f;
            template.StaticWeightFactors[2] = 0.30f * 0.15f;

            template.PartTypes[3] = PartType.ManipulationPart;
            template.ParentIndices[3] = 2;
            template.DepthSortedIndices[3] = 3;
            template.WeightManipulation[3] = 0.20f;
            template.StaticWeightFactors[3] = 0.20f * 0.15f;

            SystemicEvaluationContext evalContext = SystemicEvaluationContext.CreateFromSnapshot(
                100.0f, template.Counts, in config, in snapshot);

            using (WorkspacePool.WorkspaceLease lease = WorkspacePool.LeaseWorkspace())
            {
                AnatomicalWorkspace workspace = lease.Workspace;
                workspace.InitializeForPawn(template);
                workspace.EnsureArraySizes(template.PartCount);

                // Baseline: Left 150% bionic arm, both arms intact
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.EfficiencyRatings[1] = 1.50f;
                MassCapacitySolver.SolveInternal(workspace, in evalContext, 100.0f, "SelfCheck_BilateralIntact");
                float intactBoost = workspace.CalculatedProsthetics[1];

                // Amputate distal Right Hand (idx 3) while Right Shoulder (idx 2) remains intact
                workspace.ResetAllSlots(template.PartCount);
                workspace.SetFlag(1, PartFlags.HasAddedPart, true);
                workspace.EfficiencyRatings[1] = 1.50f;
                workspace.SetFlag(3, PartFlags.IsMissing, true);
                workspace.HealthFractions[3] = 0f;
                MassCapacitySolver.SolveInternal(workspace, in evalContext, 100.0f, "SelfCheck_BilateralDistalLoss");
                float distalLossBoost = workspace.CalculatedProsthetics[1];

                Expect.True(distalLossBoost < intactBoost - 0.01f,
                    "missing opposite distal segment must attenuate bilateral prosthetic boost (" + distalLossBoost.ToString("F3") + " vs " + intactBoost.ToString("F3") + ")");
            }
        }

        #endregion
    }
}
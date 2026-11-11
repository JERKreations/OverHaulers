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

        #endregion
    }
}

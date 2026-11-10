using RimWorld;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. CONFIGURATION CHECKS

        /// <summary>Verifies one tunable's range is well-formed and its default lies inside it.</summary>
        private static void ExpectDefaultInRange(string name, float value, float min, float max)
        {
            Expect.True(min < max, name + " range is inverted or empty (" + min + " to " + max + ")");
            Expect.True(value >= min && value <= max, name + " default " + value + " lies outside its range " + min + " to " + max);
        }

        /// <summary>
        /// Verifies that every <see cref="CoreDefaults"/> tunable default lies inside its own Min/Max range,
        /// so a factory reset or an unconfigured baseline can never produce a value the sanitiser would clamp.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckCoreDefaultsInRange(SelfCheckContext ctx)
        {
            ExpectDefaultInRange("ProstheticScaling", CoreDefaults.ProstheticScaling, CoreDefaults.ProstheticScalingMin, CoreDefaults.ProstheticScalingMax);
            ExpectDefaultInRange("AthleticScaling", CoreDefaults.AthleticScaling, CoreDefaults.AthleticScalingMin, CoreDefaults.AthleticScalingMax);
            ExpectDefaultInRange("ConfigurablePercentileFloor", CoreDefaults.ConfigurablePercentileFloor, CoreDefaults.ConfigurablePercentileFloorMin, CoreDefaults.ConfigurablePercentileFloorMax);
            ExpectDefaultInRange("TorsoAxialBias", CoreDefaults.TorsoAxialBias, CoreDefaults.TorsoAxialBiasMin, CoreDefaults.TorsoAxialBiasMax);
            ExpectDefaultInRange("TorsoHpSensitivity", CoreDefaults.TorsoHpSensitivity, CoreDefaults.TorsoHpSensitivityMin, CoreDefaults.TorsoHpSensitivityMax);
            ExpectDefaultInRange("LimbDepthDecayFactor", CoreDefaults.LimbDepthDecayFactor, CoreDefaults.LimbDepthDecayFactorMin, CoreDefaults.LimbDepthDecayFactorMax);
            ExpectDefaultInRange("ProstheticImpactConstant", CoreDefaults.ProstheticImpactConstant, CoreDefaults.ProstheticImpactConstantMin, CoreDefaults.ProstheticImpactConstantMax);
            ExpectDefaultInRange("AthleticImpactConstant", CoreDefaults.AthleticImpactConstant, CoreDefaults.AthleticImpactConstantMin, CoreDefaults.AthleticImpactConstantMax);
            ExpectDefaultInRange("ReportMetricsIntervalHours", CoreDefaults.ReportMetricsIntervalHours, CoreDefaults.ReportMetricsIntervalHoursMin, CoreDefaults.ReportMetricsIntervalHoursMax);
            ExpectDefaultInRange("PawnEvictionTimeframeHours", CoreDefaults.PawnEvictionTimeframeHours, CoreDefaults.PawnEvictionTimeframeHoursMin, CoreDefaults.PawnEvictionTimeframeHoursMax);

            float pMin = CoreDefaults.PercentageMin;
            float pMax = CoreDefaults.PercentageMax;
            ExpectDefaultInRange("AnatomyWeightTorso", CoreDefaults.AnatomyWeightTorso, pMin, pMax);
            ExpectDefaultInRange("AnatomyWeightArm", CoreDefaults.AnatomyWeightArm, pMin, pMax);
            ExpectDefaultInRange("AnatomyWeightLeg", CoreDefaults.AnatomyWeightLeg, pMin, pMax);

            ExpectDefaultInRange("TorsoPositiveBreathing", CoreDefaults.TorsoPositiveBreathing, pMin, pMax);
            ExpectDefaultInRange("TorsoPositiveBlood", CoreDefaults.TorsoPositiveBlood, pMin, pMax);
            ExpectDefaultInRange("TorsoPositiveMoving", CoreDefaults.TorsoPositiveMoving, pMin, pMax);
            ExpectDefaultInRange("TorsoPositiveManipulation", CoreDefaults.TorsoPositiveManipulation, pMin, pMax);
            ExpectDefaultInRange("ArmPositiveBreathing", CoreDefaults.ArmPositiveBreathing, pMin, pMax);
            ExpectDefaultInRange("ArmPositiveBlood", CoreDefaults.ArmPositiveBlood, pMin, pMax);
            ExpectDefaultInRange("ArmPositiveMoving", CoreDefaults.ArmPositiveMoving, pMin, pMax);
            ExpectDefaultInRange("ArmPositiveManipulation", CoreDefaults.ArmPositiveManipulation, pMin, pMax);
            ExpectDefaultInRange("LegPositiveBreathing", CoreDefaults.LegPositiveBreathing, pMin, pMax);
            ExpectDefaultInRange("LegPositiveBlood", CoreDefaults.LegPositiveBlood, pMin, pMax);
            ExpectDefaultInRange("LegPositiveMoving", CoreDefaults.LegPositiveMoving, pMin, pMax);
            ExpectDefaultInRange("LegPositiveManipulation", CoreDefaults.LegPositiveManipulation, pMin, pMax);

            ExpectDefaultInRange("TorsoDeficitBreathing", CoreDefaults.TorsoDeficitBreathing, pMin, pMax);
            ExpectDefaultInRange("TorsoDeficitBlood", CoreDefaults.TorsoDeficitBlood, pMin, pMax);
            ExpectDefaultInRange("TorsoDeficitMoving", CoreDefaults.TorsoDeficitMoving, pMin, pMax);
            ExpectDefaultInRange("TorsoDeficitManipulation", CoreDefaults.TorsoDeficitManipulation, pMin, pMax);
            ExpectDefaultInRange("ArmDeficitBreathing", CoreDefaults.ArmDeficitBreathing, pMin, pMax);
            ExpectDefaultInRange("ArmDeficitBlood", CoreDefaults.ArmDeficitBlood, pMin, pMax);
            ExpectDefaultInRange("ArmDeficitMoving", CoreDefaults.ArmDeficitMoving, pMin, pMax);
            ExpectDefaultInRange("ArmDeficitManipulation", CoreDefaults.ArmDeficitManipulation, pMin, pMax);
            ExpectDefaultInRange("LegDeficitBreathing", CoreDefaults.LegDeficitBreathing, pMin, pMax);
            ExpectDefaultInRange("LegDeficitBlood", CoreDefaults.LegDeficitBlood, pMin, pMax);
            ExpectDefaultInRange("LegDeficitMoving", CoreDefaults.LegDeficitMoving, pMin, pMax);
        }

        /// <summary>
        /// Verifies the Core sanitiser: default snapshots pass untouched, infinities clamp to the bound they exceed, NaN falls back to the default,
        /// and out-of-range snapshots are repaired and reported as modified.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckConfigSanitisation(SelfCheckContext ctx)
        {
            bool changed = false;
            SolverConfiguration solverDefaults = CoreDefaults.CreateDefaultSolverConfiguration();
            solverDefaults.Sanitized(out changed);
            Expect.True(!changed, "Default solver configuration was modified by the sanitiser");

            RuntimeConfiguration runtimeDefaults = CoreDefaults.CreateDefaultRuntimeConfiguration();
            runtimeDefaults.Sanitized(out changed);
            Expect.True(!changed, "Default runtime configuration was modified by the sanitiser");

            changed = false;
            Expect.True(ConfigBounds.ClampFloat(float.PositiveInfinity, 0.5f, 0.1f, 5f, ref changed) == 5f, "+Infinity did not clamp to max");
            Expect.True(ConfigBounds.ClampFloat(float.NegativeInfinity, 0.5f, 0.1f, 5f, ref changed) == 0.1f, "-Infinity did not clamp to min");
            Expect.True(ConfigBounds.ClampFloat(float.NaN, 0.5f, 0.1f, 5f, ref changed) == 0.5f, "NaN did not fall back to the default");
            Expect.True(ConfigBounds.ClampFloat(99f, 0.5f, 0.1f, 5f, ref changed) == 5f, "High value did not clamp to max");
            Expect.True(ConfigBounds.ClampFloat(-99f, 0.5f, 0.1f, 5f, ref changed) == 0.1f, "Low value did not clamp to min");
            Expect.True(changed, "Clamping reported no modification");

            changed = false;
            Expect.True(ConfigBounds.ClampFloat(2f, 0.5f, 0.1f, 5f, ref changed) == 2f && !changed, "In-range value was altered or reported as modified");

            SolverConfiguration bad = new SolverConfiguration(
                float.NaN, true, true, true, true,
                float.PositiveInfinity, CoreDefaults.AthleticImpactConstant, float.NegativeInfinity, CoreDefaults.ProstheticImpactConstant,
                CoreDefaults.TorsoAxialBias, 99f, CoreDefaults.LimbDepthDecayFactor,
                -5f, 7f, CoreDefaults.TorsoPositiveMoving, CoreDefaults.TorsoPositiveManipulation,
                CoreDefaults.ArmPositiveBreathing, CoreDefaults.ArmPositiveBlood, CoreDefaults.ArmPositiveMoving, CoreDefaults.ArmPositiveManipulation,
                CoreDefaults.LegPositiveBreathing, CoreDefaults.LegPositiveBlood, CoreDefaults.LegPositiveMoving, CoreDefaults.LegPositiveManipulation,
                CoreDefaults.TorsoDeficitBreathing, CoreDefaults.TorsoDeficitBlood, CoreDefaults.TorsoDeficitMoving, CoreDefaults.TorsoDeficitManipulation,
                CoreDefaults.ArmDeficitBreathing, CoreDefaults.ArmDeficitBlood, CoreDefaults.ArmDeficitMoving, CoreDefaults.ArmDeficitManipulation,
                CoreDefaults.LegDeficitBreathing, CoreDefaults.LegDeficitBlood, CoreDefaults.LegDeficitMoving, float.NaN,
                float.PositiveInfinity, CoreDefaults.AnatomyWeightArm, CoreDefaults.AnatomyWeightLeg);

            SolverConfiguration fixedSolver = bad.Sanitized(out changed);
            Expect.True(changed, "Invalid solver configuration was not reported as modified");
            Expect.True(fixedSolver.ConfigurablePercentileFloor == CoreDefaults.ConfigurablePercentileFloor, "NaN percentile floor did not fall back to its default");
            Expect.True(fixedSolver.AthleticScaling == CoreDefaults.AthleticScalingMax, "+Infinity athletic scaling did not clamp to max");
            Expect.True(fixedSolver.ProstheticScaling == CoreDefaults.ProstheticScalingMin, "-Infinity prosthetic scaling did not clamp to min");
            Expect.True(fixedSolver.TorsoHpSensitivity == CoreDefaults.TorsoHpSensitivityMax, "Oversized torso sensitivity did not clamp to max");
            Expect.True(fixedSolver.TorsoPositiveBreathing == CoreDefaults.PercentageMin, "Negative weighting did not clamp to min");
            Expect.True(fixedSolver.TorsoPositiveBlood == CoreDefaults.PercentageMax, "Oversized weighting did not clamp to max");
            Expect.True(fixedSolver.LegDeficitManipulation == CoreDefaults.LegDeficitManipulation, "NaN weighting did not fall back to its default");
            Expect.True(fixedSolver.BudgetCorePart == CoreDefaults.PercentageMax, "+Infinity budget did not clamp to max");

            fixedSolver.Sanitized(out changed);
            Expect.True(!changed, "Sanitising is not idempotent");

            RuntimeConfiguration badRuntime = new RuntimeConfiguration(
                int.MaxValue, false, -3,
                true, true, true, true, true, true, true, true);
            RuntimeConfiguration fixedRuntime = badRuntime.Sanitized(out changed);
            Expect.True(changed, "Invalid runtime configuration was not reported as modified");
            Expect.True(fixedRuntime.PawnEvictionTimeframeTicks == CoreDefaults.PawnEvictionTimeframeHoursMax * GenDate.TicksPerHour, "Oversized eviction timeframe did not clamp to max");
            Expect.True(fixedRuntime.ReportMetricsIntervalHours == CoreDefaults.ReportMetricsIntervalHoursMin, "Negative report interval did not clamp to min");
        }

        #endregion
    }
}

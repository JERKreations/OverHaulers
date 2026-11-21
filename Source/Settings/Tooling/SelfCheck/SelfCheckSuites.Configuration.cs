using System.Globalization;
using System.Threading;
using RimWorld;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. CONFIGURATION CHECKS

        /// <summary>Checks synthetic display fixtures independently of the active game language.</summary>
        private static void CheckDisplayPrecision(SelfCheckContext ctx)
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Expect.Equal("100%", ReportFormatter.FormatPercentage(1f), "whole gameplay percentage");
                Expect.Equal("87.5%", ReportFormatter.FormatPercentage(0.875f), "trimmed gameplay percentage");
                Expect.Equal("87.543%", ReportFormatter.FormatPercentage(0.8754321f), "gameplay percentage precision");
                Expect.Equal("87.54321%", ReportFormatter.FormatPercentage(0.8754321f, true), "diagnostic percentage precision");
                Expect.Equal("-12.346%", ReportFormatter.FormatPercentage(-0.123456f), "negative gameplay percentage");
                Expect.Equal("0", ReportFormatter.FormatAdaptive(0.000004f), "small gameplay value");
                Expect.Equal("0.00001", ReportFormatter.FormatAdaptive(0.000006f, true), "small diagnostic value");
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Expect.Equal("1,25", ReportFormatter.FormatAdaptive(1.25f), "localized decimal separator");
                Expect.Equal("-12,346%", ReportFormatter.FormatPercentage(-0.123456f), "localized negative percentage");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        /// <summary>Verifies one tunable's range is well-formed and its default lies inside it.</summary>
        private static void ExpectDefaultInRange(string name, float value, float min, float max)
        {
            Expect.True(min < max, name + " range is inverted or empty (" + min + " to " + max + ")");
            Expect.True(value >= min && value <= max, name + " default " + value + " lies outside its range " + min + " to " + max);
        }

        /// <summary>
        /// Verifies that every <see cref="CoreDefaults"/> tunable default lies inside its own Min/Max range,
        /// so a factory reset or an unconfigured baseline can never produce a value the sanitiser would clamp.
        /// Ordered strictly by [SEC-01] through [SEC-07] (Solver) and [SEC-09] (Runtime).
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckCoreDefaultsInRange(SelfCheckContext ctx)
        {
            // [SEC-01] Global Modifiers & Scaling Intensity
            ExpectDefaultInRange("ProstheticScaling", CoreDefaults.ProstheticScaling, CoreDefaults.ProstheticScalingMin, CoreDefaults.ProstheticScalingMax);
            ExpectDefaultInRange("AthleticScaling", CoreDefaults.AthleticScaling, CoreDefaults.AthleticScalingMin, CoreDefaults.AthleticScalingMax);
            ExpectDefaultInRange("ConfigurablePercentileFloor", CoreDefaults.ConfigurablePercentileFloor, CoreDefaults.ConfigurablePercentileFloorMin, CoreDefaults.ConfigurablePercentileFloorMax);

            float pMin = CoreDefaults.PercentageMin;
            float pMax = CoreDefaults.PercentageMax;

            // [SEC-02] Core Body Group Budgets & Redistribution
            ExpectDefaultInRange("AnatomyWeightTorso", CoreDefaults.AnatomyWeightTorso, pMin, pMax);
            ExpectDefaultInRange("AnatomyWeightArm", CoreDefaults.AnatomyWeightArm, pMin, pMax);
            ExpectDefaultInRange("AnatomyWeightLeg", CoreDefaults.AnatomyWeightLeg, pMin, pMax);
            ExpectDefaultInRange("MissingArmsTorsoShare", CoreDefaults.MissingArmsTorsoShare, pMin, pMax);
            ExpectDefaultInRange("MissingLegsTorsoShare", CoreDefaults.MissingLegsTorsoShare, pMin, pMax);

            // [SEC-03] Positive Systemic Coupling Weights
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

            // [SEC-04] Negative Deficit Coupling Weights
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
            ExpectDefaultInRange("LegDeficitManipulation", CoreDefaults.LegDeficitManipulation, pMin, pMax);

            // [SEC-05] Torso Macro Topology
            ExpectDefaultInRange("TorsoAxialBias", CoreDefaults.TorsoAxialBias, CoreDefaults.TorsoAxialBiasMin, CoreDefaults.TorsoAxialBiasMax);
            ExpectDefaultInRange("TorsoHpSensitivity", CoreDefaults.TorsoHpSensitivity, CoreDefaults.TorsoHpSensitivityMin, CoreDefaults.TorsoHpSensitivityMax);

            // [SEC-06] Limb Branch Topology & Pair Scaling
            ExpectDefaultInRange("LimbDepthDecayFactor", CoreDefaults.LimbDepthDecayFactor, CoreDefaults.LimbDepthDecayFactorMin, CoreDefaults.LimbDepthDecayFactorMax);
            ExpectDefaultInRange("LimbPairDecayFactor", CoreDefaults.LimbPairDecayFactor, CoreDefaults.LimbPairDecayFactorMin, CoreDefaults.LimbPairDecayFactorMax);

            // [SEC-07] Structural Scaling Constants
            ExpectDefaultInRange("ProstheticImpactConstant", CoreDefaults.ProstheticImpactConstant, CoreDefaults.ProstheticImpactConstantMin, CoreDefaults.ProstheticImpactConstantMax);
            ExpectDefaultInRange("AthleticImpactConstant", CoreDefaults.AthleticImpactConstant, CoreDefaults.AthleticImpactConstantMin, CoreDefaults.AthleticImpactConstantMax);

            // [SEC-09] Diagnostics & Telemetry (Runtime)
            ExpectDefaultInRange("ReportMetricsIntervalHours", CoreDefaults.ReportMetricsIntervalHours, CoreDefaults.ReportMetricsIntervalHoursMin, CoreDefaults.ReportMetricsIntervalHoursMax);
            ExpectDefaultInRange("PawnEvictionTimeframeHours", CoreDefaults.PawnEvictionTimeframeHours, CoreDefaults.PawnEvictionTimeframeHoursMin, CoreDefaults.PawnEvictionTimeframeHoursMax);
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
                // [SEC-01] Global Modifiers & Scaling Intensity
                true, true, true,
                float.NegativeInfinity, float.PositiveInfinity,
                float.NaN, true,

                // [SEC-02] Core Body Group Budgets & Redistribution
                float.PositiveInfinity, CoreDefaults.AnatomyWeightArm, CoreDefaults.AnatomyWeightLeg,
                -5f, 5f,

                // [SEC-03] Positive Systemic Coupling Weights
                -5f, 7f, CoreDefaults.TorsoPositiveMoving, CoreDefaults.TorsoPositiveManipulation,
                CoreDefaults.ArmPositiveBreathing, CoreDefaults.ArmPositiveBlood, CoreDefaults.ArmPositiveMoving, CoreDefaults.ArmPositiveManipulation,
                CoreDefaults.LegPositiveBreathing, CoreDefaults.LegPositiveBlood, CoreDefaults.LegPositiveMoving, CoreDefaults.LegPositiveManipulation,

                // [SEC-04] Negative Deficit Coupling Weights
                CoreDefaults.TorsoDeficitBreathing, CoreDefaults.TorsoDeficitBlood, CoreDefaults.TorsoDeficitMoving, CoreDefaults.TorsoDeficitManipulation,
                CoreDefaults.ArmDeficitBreathing, CoreDefaults.ArmDeficitBlood, CoreDefaults.ArmDeficitMoving, CoreDefaults.ArmDeficitManipulation,
                CoreDefaults.LegDeficitBreathing, CoreDefaults.LegDeficitBlood, CoreDefaults.LegDeficitMoving, float.NaN,

                // [SEC-05] Torso Macro Topology
                CoreDefaults.TorsoAxialBias, 99f,

                // [SEC-06] Limb Branch Topology & Pair Scaling
                CoreDefaults.LimbDepthDecayFactor, -10f,

                // [SEC-07] Structural Scaling Constants
                100f, -10f);

            SolverConfiguration fixedSolver = bad.Sanitized(out changed);
            Expect.True(changed, "Invalid solver configuration was not reported as modified");

            // [SEC-01] Verification
            Expect.True(fixedSolver.ConfigurablePercentileFloor == CoreDefaults.ConfigurablePercentileFloor, "NaN percentile floor did not fall back to its default");
            Expect.True(fixedSolver.ProstheticScaling == CoreDefaults.ProstheticScalingMin, "-Infinity prosthetic scaling did not clamp to min");
            Expect.True(fixedSolver.AthleticScaling == CoreDefaults.AthleticScalingMax, "+Infinity athletic scaling did not clamp to max");

            // [SEC-02] Verification
            Expect.True(fixedSolver.BudgetCorePart == CoreDefaults.PercentageMax, "+Infinity budget did not clamp to max");
            Expect.True(fixedSolver.MissingArmsTorsoShare == CoreDefaults.PercentageMin, "Negative missing arms share did not clamp to min");
            Expect.True(fixedSolver.MissingLegsTorsoShare == CoreDefaults.PercentageMax, "Oversized missing legs share did not clamp to max");

            // [SEC-03] Verification
            Expect.True(fixedSolver.TorsoPositiveBreathing == CoreDefaults.PercentageMin, "Negative weighting did not clamp to min");
            Expect.True(fixedSolver.TorsoPositiveBlood == CoreDefaults.PercentageMax, "Oversized weighting did not clamp to max");

            // [SEC-04] Verification
            Expect.True(fixedSolver.LegDeficitManipulation == CoreDefaults.LegDeficitManipulation, "NaN weighting did not fall back to its default");

            // [SEC-05] Verification
            Expect.True(fixedSolver.TorsoHpSensitivity == CoreDefaults.TorsoHpSensitivityMax, "Oversized torso sensitivity did not clamp to max");

            // [SEC-06] Verification
            Expect.True(fixedSolver.LimbPairDecayFactor == CoreDefaults.LimbPairDecayFactorMin, "Negative limb pair decay did not clamp to min");

            // [SEC-07] Verification
            Expect.True(fixedSolver.ProstheticImpactConstant == CoreDefaults.ProstheticImpactConstantMax, "Oversized prosthetic impact constant did not clamp to max");
            Expect.True(fixedSolver.AthleticImpactConstant == CoreDefaults.AthleticImpactConstantMin, "Negative athletic impact constant did not clamp to min");

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
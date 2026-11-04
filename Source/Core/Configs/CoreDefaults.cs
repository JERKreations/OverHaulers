using RimWorld;

namespace OverHaulers
{
    /// <summary>
    /// [CORE-CONF-03] CORE FACTORY DEFAULTS &amp; BASELINE CONFIGURATION FACTORIES
    /// Single source of truth for every default value that feeds <see cref="SolverConfiguration"/> and
    /// <see cref="RuntimeConfiguration"/>, together with each tunable's valid Min/Max range. Resides under Source/Core/Configs/
    /// and has zero Settings or UI dependencies.
    /// The Settings layer reads these values downward for its persisted fields and UI sliders; it owns only presentation-side values and palettes.
    /// </summary>
    public static class CoreDefaults
    {
        #region 1. [SEC-01] GLOBAL MODIFIERS & SCALING INTENSITY

        public const bool EnableProsthetics = true;
        public const bool EnableAthletics = true;
        public const bool EnablePartHealth = true;

        public const float ProstheticScaling = 1.0f;
        public const float ProstheticScalingMin = 0.1f;
        public const float ProstheticScalingMax = 5.0f;

        public const float AthleticScaling = 1.0f;
        public const float AthleticScalingMin = 0.1f;
        public const float AthleticScalingMax = 5.0f;

        public const float ConfigurablePercentileFloor = 0.20f;
        public const float ConfigurablePercentileFloorMin = 0.05f;
        public const float ConfigurablePercentileFloorMax = 1.00f;

        public const bool EnableHardCompatibilitySafetyFloor = true;

        #endregion

        #region 2. [SEC-02] CORE BODY GROUP BUDGETS

        public const float PercentageMin = 0.0f;
        public const float PercentageMax = 1.0f;

        public const float AnatomyWeightTorso = 0.50f;
        public const float AnatomyWeightArm = 0.15f;
        public const float AnatomyWeightLeg = 0.35f;

        #endregion

        #region 3. [SEC-03] POSITIVE SYSTEMIC COUPLING WEIGHTS

        public const float TorsoPositiveBreathing = 1.00f;
        public const float TorsoPositiveBlood = 1.00f;
        public const float TorsoPositiveMoving = 1.00f;
        public const float TorsoPositiveManipulation = 1.00f;

        public const float ArmPositiveBreathing = 1.00f;
        public const float ArmPositiveBlood = 1.00f;
        public const float ArmPositiveMoving = 0.33f;
        public const float ArmPositiveManipulation = 1.00f;

        public const float LegPositiveBreathing = 1.00f;
        public const float LegPositiveBlood = 1.00f;
        public const float LegPositiveMoving = 1.00f;
        public const float LegPositiveManipulation = 0.00f;

        #endregion

        #region 4. [SEC-04] NEGATIVE SYSTEMIC COUPLING WEIGHTS

        public const float TorsoDeficitBreathing = 1.00f;
        public const float TorsoDeficitBlood = 1.00f;
        public const float TorsoDeficitMoving = 1.00f;
        public const float TorsoDeficitManipulation = 1.00f;

        public const float ArmDeficitBreathing = 1.00f;
        public const float ArmDeficitBlood = 1.00f;
        public const float ArmDeficitMoving = 0.33f;
        public const float ArmDeficitManipulation = 1.00f;

        public const float LegDeficitBreathing = 1.00f;
        public const float LegDeficitBlood = 1.00f;
        public const float LegDeficitMoving = 1.00f;
        public const float LegDeficitManipulation = 0.00f;

        #endregion

        #region 5. [SEC-05] TORSO MACRO TOPOLOGY

        public const float TorsoAxialBias = 0.70f;
        public const float TorsoAxialBiasMin = 0.20f;
        public const float TorsoAxialBiasMax = 0.95f;

        public const float TorsoHpSensitivity = 1.00f;
        public const float TorsoHpSensitivityMin = 0.00f;
        public const float TorsoHpSensitivityMax = 2.50f;

        #endregion

        #region 6. [SEC-06] LIMB DEPTH DECAY

        public const float LimbDepthDecayFactor = 0.50f;
        public const float LimbDepthDecayFactorMin = 0.20f;
        public const float LimbDepthDecayFactorMax = 0.80f;

        #endregion

        #region 7. [SEC-07] STRUCTURAL SCALING CONSTANTS

        public const float ProstheticImpactConstant = 10.0f;
        public const float ProstheticImpactConstantMin = 1.0f;
        public const float ProstheticImpactConstantMax = 50.0f;

        public const float AthleticImpactConstant = 3.0f;
        public const float AthleticImpactConstantMin = 1.0f;
        public const float AthleticImpactConstantMax = 15.0f;

        #endregion

        #region 8. [SEC-09] DIAGNOSTICS & TELEMETRY DEFAULTS

        /// <summary>
        /// High-load population stress-test benchmark toggle forcing 100% of biological fauna
        /// (livestock, wild animals, vermin, predators) through the solver and dynamic cache.
        /// </summary>
        public const bool DevStressTestAllFauna = false;

        // Report metrics interval in hours. This defines how frequently diagnostic metrics are reported.
        public const int ReportMetricsIntervalHours = 0;
        public const int ReportMetricsIntervalHoursMin = 0;
        public const int ReportMetricsIntervalHoursMax = 48;

        // Flags to enable or disable logging of various diagnostic metrics.
        public const bool LogQueryMetrics = true;
        public const bool LogCacheMetrics = true;
        public const bool LogSnapshotTableMetrics = true;
        public const bool LogPresentationMetrics = true;
        public const bool LogLifecycleMetrics = true;
        public const bool LogWorkspaceMetrics = true;
        public const bool LogSafetyFloorClamps = true;
        public const bool LogPawnEvictions = true;

        // Timeframe in hours for considering pawn evictions. This defines the window within which pawn evictions are evaluated.
        public const int PawnEvictionTimeframeHours = 24;
        public const int PawnEvictionTimeframeHoursMin = 1;
        public const int PawnEvictionTimeframeHoursMax = 72;

        #endregion

        #region 9. BASELINE CONFIGURATION FACTORIES

        /// <summary>
        /// Creates a baseline <see cref="SolverConfiguration"/> snapshot using default constant values.
        /// Acts as the single source of truth for unconfigured or reset solver baselines.
        /// </summary>
        public static SolverConfiguration CreateDefaultSolverConfiguration()
        {
            return new SolverConfiguration(
                ConfigurablePercentileFloor,
                EnableProsthetics,
                EnableAthletics,
                EnablePartHealth,
                EnableHardCompatibilitySafetyFloor,
                AthleticScaling,
                AthleticImpactConstant,
                ProstheticScaling,
                ProstheticImpactConstant,
                TorsoAxialBias,
                TorsoHpSensitivity,
                LimbDepthDecayFactor,
                TorsoPositiveBreathing, TorsoPositiveBlood, TorsoPositiveMoving, TorsoPositiveManipulation,
                ArmPositiveBreathing,   ArmPositiveBlood,   ArmPositiveMoving,   ArmPositiveManipulation,
                LegPositiveBreathing,   LegPositiveBlood,   LegPositiveMoving,   LegPositiveManipulation,
                TorsoDeficitBreathing,  TorsoDeficitBlood,  TorsoDeficitMoving,  TorsoDeficitManipulation,
                ArmDeficitBreathing,    ArmDeficitBlood,    ArmDeficitMoving,    ArmDeficitManipulation,
                LegDeficitBreathing,    LegDeficitBlood,    LegDeficitMoving,    LegDeficitManipulation,
                AnatomyWeightTorso,     AnatomyWeightArm,   AnatomyWeightLeg
            );
        }

        /// <summary>
        /// Creates a baseline <see cref="RuntimeConfiguration"/> snapshot using tuned default constant values.
        /// Acts as the single source of truth for unconfigured or reset infrastructure baselines.
        /// </summary>
        public static RuntimeConfiguration CreateDefaultRuntimeConfiguration()
        {
            return new RuntimeConfiguration(
                pawnEvictionTimeframeTicks: PawnEvictionTimeframeHours * GenDate.TicksPerHour,
                devStressTestAllFauna: DevStressTestAllFauna,
                reportMetricsIntervalHours: ReportMetricsIntervalHours,
                logCache: LogCacheMetrics,
                logQuery: LogQueryMetrics,
                logSnapshot: LogSnapshotTableMetrics,
                logPresentation: LogPresentationMetrics,
                logLifecycle: LogLifecycleMetrics,
                logWorkspace: LogWorkspaceMetrics,
                logSafetyFloorClamps: LogSafetyFloorClamps,
                logPawnEvictions: LogPawnEvictions
            );
        }

        #endregion
    }
}

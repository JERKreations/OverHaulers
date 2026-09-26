using UnityEngine;

namespace OverHaulers
{
    public enum PalettePreset
    {
        Default = 0,
        Deuteranopia = 1,
        Tritanopia = 2,
        HighContrast = 3,
        Custom = 4
    }

    /// <summary>
    /// Centralized database of hardcoded factory defaults, bounding constraints, and mathematical constants.
    /// Acts as the single source of truth for all balancing configurations and UI slider limits.
    /// Annotated with Canonical Semantic Tags [SEC-01] through [SEC-10] for instant cross-file search.
    /// </summary>
    public static class SettingsDefaults
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

        public const float MassCapacityFloor = 0.20f;
        public const float MassCapacityFloorMin = 0.05f;
        public const float MassCapacityFloorMax = 1.00f;

        public const bool CompatibilitySafetyFloor = true;
        public const bool DevAllowNonPackSpeciesInLivePlay = false;

        #endregion

        #region 2. [SEC-02] CORE BODY GROUP BUDGETS

        public const float PercentageMin = 0.0f;
        public const float PercentageMax = 1.0f;

        public const float AnatomyWeightTorso = 0.50f;
        public const float AnatomyWeightArm = 0.15f;
        public const float AnatomyWeightLeg = 0.35f;

        #endregion

        #region 3. [SEC-03] POSITIVE SYSTEMIC COUPLING WEIGHTS

        public const bool LinkSystemicWeights = true;

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

        #region 8. [SEC-08] COMPATIBILITY & PIPELINE DRIVER DEFAULTS

        public const string DefaultSelectedPresentationKey = "AUTO";

        #endregion

        #region 9. [SEC-09] DIAGNOSTICS & TELEMETRY DEFAULTS

        // Verbose breakdown flag for diagnostics. When true, instances of InfoCard are forced to display all groupings details regardless of
        // part states and health conditions.
        public const bool VerboseBreakdown = false;
        
        // Report metrics interval in hours. This defines how frequently diagnostic metrics are reported.
        public const int ReportMetricsIntervalHours = 0;
        public const int ReportMetricsIntervalHoursMin = 0;
        public const int ReportMetricsIntervalHoursMax = 48;

        // Flags to enable or disable logging of various diagnostic metrics.
        public const bool LogQueryMetrics = true;
        public const bool LogCacheMetrics = true;
        public const bool LogSnapshotTableMetrics = true;
        public const bool LogLifecycleMetrics = true;
        public const bool LogWorkspaceMetrics = true;
        public const bool LogSafetyFloorClamps = false;
        public const bool LogPawnEvictions = false;
        
        // Timeframe in hours for considering pawn evictions. This defines the window within which pawn evictions are evaluated.
        public const int PawnEvictionTimeframeHours = 24;
        public const int PawnEvictionTimeframeHoursMin = 1;
        public const int PawnEvictionTimeframeHoursMax = 72;

        #endregion

        #region 10. [SEC-10] ACCESSIBILITY & COLOR PALETTES

        public const float IconScalePercent = 0.70f;
        public const float IconScalePercentMin = 0.50f;
        public const float IconScalePercentMax = 1.20f;

        // Default color palette.
        public static readonly Color ColorHealthyDefault = new Color(0.90f, 0.90f, 0.90f);
        public static readonly Color ColorCriticalDefault = new Color(0.90f, 0.33f, 0.33f);
        public static readonly Color ColorBoostedDefault = new Color(0.40f, 0.82f, 1.00f);

        // Deuteranopia color palette.
        public static readonly Color ColorHealthyDeuteranopia = new Color(0.35f, 0.70f, 0.90f);
        public static readonly Color ColorCriticalDeuteranopia = new Color(0.95f, 0.65f, 0.15f);
        public static readonly Color ColorBoostedDeuteranopia = new Color(0.85f, 0.40f, 0.85f);

        // Tritanopia color palette.
        public static readonly Color ColorHealthyTritanopia = new Color(0.20f, 0.75f, 0.65f);
        public static readonly Color ColorCriticalTritanopia = new Color(0.90f, 0.25f, 0.40f);
        public static readonly Color ColorBoostedTritanopia = new Color(0.65f, 0.40f, 0.90f);

        // High contrast color palette.
        public static readonly Color ColorHealthyHighContrast = new Color(1.00f, 1.00f, 1.00f);
        public static readonly Color ColorCriticalHighContrast = new Color(0.45f, 0.45f, 0.45f);
        public static readonly Color ColorBoostedHighContrast = new Color(1.00f, 0.85f, 0.20f);

        public const PalettePreset DefaultPalettePreset = PalettePreset.Default;

        #endregion

        #region 11. ENGINE & PERFORMANCE INVARIANTS

        // Minimum fast path ticks and dynamic cache expiry floor (5 ticks = ~0.08s at 1x speed).
        public const int DefaultCacheMinFastPathTicks = 5;

        // Dynamic square-root scaling factor (K) for population-based cache expiry.
        public const int DynamicCacheExpiryScaleK = 5;

        // Minimum tick interval between reactive cache invalidations for a single pawn during combat damage bursts (15 ticks = ~0.25s).
        public const int InvalidationDebounceTicks = 15;

        // Cache cleanup interval. This is the interval at which expired cache entries are cleaned up.
        public const int CacheCleanupInterval = 600;

        // Default workspace capacity. This defines the initial capacity of the workspace used in performance-critical operations.
        public const int DefaultWorkspaceCapacity = 64;

        // Efficiency epsilon for performance calculations. This small value is used to avoid division by zero and to maintain numerical stability.
        public const float EfficiencyEpsilon = 0.005f;

        // World load calibration delay in ticks. This is the delay before performing calibration after the world is loaded.
        public const int WorldLoadCalibrationDelayTicks = 120;

        #endregion
    }
}
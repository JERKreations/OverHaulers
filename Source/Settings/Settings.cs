using UnityEngine;
using Verse;

namespace OverHaulers
{
    #region 1. [SEC-00] NAVIGATION ENUMS

    /// <summary>
    /// [SEC-00] Top-level navigation tabs for the OverHaulers settings interface.
    /// </summary>
    public enum SettingsTab
    {
        Tuning = 0,
        TestBench = 1,
        Integration = 2,
        Accessibility = 3
    }

    #endregion

    /// <summary>
    /// Persisted user settings. The class is split per domain: this primary file owns navigation state, the
    /// serialization orchestrators, sanitisation helpers, the projections into Core configuration snapshots and the
    /// global reset; each <c>Settings.*.cs</c> partial owns the fields, resets and Scribe/sanitise helpers of one [SEC-xx] domain.
    /// </summary>
    public partial class Settings : ModSettings
    {
        #region 2. [SEC-00] NAVIGATION & FIRST-LAUNCH STATE

        public SettingsTab activeTab = SettingsTab.Tuning;
        public bool hasOpenedSettingsBefore = false;

        #endregion

        #region 3. PROJECTION & CORE INGRESS

        /// <summary>
        /// Projects the active mutable settings into an immutable <see cref="SolverConfiguration"/> value struct.
        /// </summary>
        public SolverConfiguration ToSolverConfiguration()
        {
            return new SolverConfiguration(
                configurablePercentileFloor,
                enableProsthetics,
                enableAthletics,
                enablePartHealth,
                enableHardCompatibilitySafetyFloor,
                athleticScaling,
                athleticImpactConstant,
                prostheticScaling,
                prostheticImpactConstant,
                torsoAxialBias,
                torsoHpSensitivity,
                limbDepthDecayFactor,
                torsoPositiveBreathing, torsoPositiveBlood, torsoPositiveMoving, torsoPositiveManipulation,
                armPositiveBreathing,   armPositiveBlood,   armPositiveMoving,   armPositiveManipulation,
                legPositiveBreathing,   legPositiveBlood,   legPositiveMoving,   legPositiveManipulation,
                torsoDeficitBreathing,  torsoDeficitBlood,  torsoDeficitMoving,  torsoDeficitManipulation,
                armDeficitBreathing,    armDeficitBlood,    armDeficitMoving,    armDeficitManipulation,
                legDeficitBreathing,    legDeficitBlood,    legDeficitMoving,    legDeficitManipulation,
                anatomyWeightTorso,     anatomyWeightArm,   anatomyWeightLeg
            );
        }

        /// <summary>
        /// Projects active infrastructure, eviction timeframe, and telemetry toggles
        /// into an immutable <see cref="RuntimeConfiguration"/> value struct.
        /// </summary>
        public RuntimeConfiguration ToRuntimeConfiguration()
        {
            return new RuntimeConfiguration(
                pawnEvictionTimeframeTicks: PawnEvictionTimeframeTicks,
                devStressTestAllFauna: devStressTestAllFauna,
                reportMetricsIntervalHours: reportMetricsIntervalHours,
                logCache: logCacheMetrics,
                logQuery: logQueryMetrics,
                logSnapshot: logSnapshotTableMetrics,
                logPresentation: logPresentationMetrics,
                logLifecycle: logLifecycleMetrics,
                logWorkspace: logWorkspaceMetrics,
                logSafetyFloorClamps: logSafetyFloorClamps,
                logPawnEvictions: logPawnEvictions
            );
        }

        /// <summary>
        /// Atomically pushes both solver balancing and runtime infrastructure configurations to the computational Core engine,
        /// and pushes active color palettes and icon scaling downward to the Presentation layer.
        /// Thread-safe: can be invoked from any thread (including async mod initialization and Scribe loading).
        /// </summary>
        public void PushConfigurationToCore()
        {
            MassCapacitySolver.UpdateConfiguration(ToSolverConfiguration());
            PawnDataRegistry.UpdateConfiguration(ToRuntimeConfiguration());
            ReportFormatter.SetPalette(colorHealthy, colorCritical, colorBoosted, verboseBreakdown);
            InfoCardOverlay.SetIconScale(iconScalePercent);
        }

        #endregion

        #region 4. GLOBAL RESET

        /// <summary>
        /// Resets all settings to their default values.
        /// </summary>
        public void ResetAllToDefaults()
        {
            ResetGlobals();
            ResetAccessibility();
            ResetBodyParts();
            ResetSystemicWeightings();
            ResetTorsoParts();
            ResetLimbDecay();
            ResetScalingConstants();
            ResetCompatibility();
            ResetDiagnostics();
            SettingsViewUtilities.ClearInputBuffers();
            SettingsViewUtilities.OnSettingMutated();
        }

        #endregion

        #region 5. DATA SERIALIZATION (SCRIBE)

        /// <summary>
        /// Exposes the settings data for serialization.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref activeTab, "activeTab", SettingsTab.Tuning);
            Scribe_Values.Look(ref hasOpenedSettingsBefore, "hasOpenedSettingsBefore", false);

            ExposeBasic();
            ExposeAccessibility();
            ExposeWeightings();
            ExposeScaling();
            ExposeIntegration();
            ExposeDiagnostics();

            // Values are fully populated at this point when loading; guard against hand-edited or corrupted settings files and ensure
            // the configuration is pushed to the core systems.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                SanitizeLoadedValues();
                PushConfigurationToCore();
            }
        }

        /// <summary>
        /// Clamps every numeric setting to its supported range and replaces NaN/infinite values with defaults,
        /// so a hand-edited or corrupted settings file can never feed invalid numbers into the solver or the UI.
        /// </summary>
        private void SanitizeLoadedValues()
        {
            if (!System.Enum.IsDefined(typeof(SettingsTab), activeTab)) activeTab = SettingsTab.Tuning;

            SanitizeBasic();
            SanitizeAccessibility();
            SanitizeWeightings();
            SanitizeScaling();
            SanitizeIntegration();
            SanitizeDiagnostics();
        }

        #endregion

        #region 6. SANITISATION PRIMITIVES

        /// <summary>
        /// Returns a finite value clamped to a range, or the fallback when the value is NaN or infinite.
        /// </summary>
        /// <param name="value">The loaded value.</param>
        /// <param name="fallback">The default used when the value is not finite.</param>
        /// <param name="min">Lower bound.</param>
        /// <param name="max">Upper bound.</param>
        /// <returns>A finite value within the range.</returns>
        private static float Sane(float value, float fallback, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return value < min ? min : (value > max ? max : value);
        }

        /// <summary>
        /// Sanitises a 0 to 1 percentage-style setting.
        /// </summary>
        /// <param name="value">The loaded value.</param>
        /// <param name="fallback">The default used when the value is not finite.</param>
        /// <returns>A finite value between 0 and 1.</returns>
        private static float SanePercent(float value, float fallback)
        {
            return Sane(value, fallback, CoreDefaults.PercentageMin, CoreDefaults.PercentageMax);
        }

        /// <summary>
        /// Sanitises a colour setting channel by channel, falling back per channel when a value is not finite.
        /// </summary>
        /// <param name="value">The loaded colour.</param>
        /// <param name="fallback">The default colour.</param>
        /// <returns>A colour with every channel finite and within 0 to 1.</returns>
        private static Color SaneColor(Color value, Color fallback)
        {
            return new Color(
                Sane(value.r, fallback.r, 0f, 1f),
                Sane(value.g, fallback.g, 0f, 1f),
                Sane(value.b, fallback.b, 0f, 1f),
                Sane(value.a, 1f, 0f, 1f));
        }

        #endregion
    }
}

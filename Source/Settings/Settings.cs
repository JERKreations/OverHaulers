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
        #region 2. [SEC-00] CHANGE NOTIFICATION

        /// <summary>Raised after settings values are reset programmatically, so views can discard stale text-input buffers.</summary>
        public static event System.Action ValuesReset;

        /// <summary>Raised after a programmatic reset that must also push configuration and invalidate dependent runtime caches.</summary>
        public static event System.Action ValuesMutated;

        private static void NotifyValuesReset() => ValuesReset?.Invoke();

        private static void NotifyValuesMutated() => ValuesMutated?.Invoke();

        #endregion

        #region 3. [SEC-00] NAVIGATION & FIRST-LAUNCH STATE

        public SettingsTab activeTab = SettingsTab.Tuning;
        public bool hasOpenedSettingsBefore = false;

        #endregion

        #region 4. PROJECTION & CORE INGRESS

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
            ReportFormatter.SetProstheticPalette(prostheticColorOverride,
                customProstheticPalette ? prostheticColorCritical : colorCritical,
                customProstheticPalette ? prostheticColorHealthy : colorHealthy,
                customProstheticPalette ? prostheticColorBoosted : colorBoosted);
            InfoCardOverlay.SetIconScale(iconScalePercent);
            IntegrationPipeline.SetPreferredPresentationKey(selectedPresentationKey);
        }

        #endregion

        #region 5. GLOBAL RESET

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
            NotifyValuesReset();
            NotifyValuesMutated();
        }

        #endregion

        #region 6. DATA SERIALIZATION (SCRIBE)

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
        /// Replaces every numeric setting with its Core-sanitised counterpart and clamps presentation-owned values,
        /// so a hand-edited or corrupted settings file can never feed invalid numbers into the solver or the UI.
        /// Range logic for Core-owned tunables lives solely in <see cref="SolverConfiguration.Sanitized"/> and <see cref="RuntimeConfiguration.Sanitized"/>.
        /// </summary>
        private void SanitizeLoadedValues()
        {
            if (!System.Enum.IsDefined(typeof(SettingsTab), activeTab)) activeTab = SettingsTab.Tuning;

            SolverConfiguration solver = ToSolverConfiguration().Sanitized(out _);
            RuntimeConfiguration runtime = ToRuntimeConfiguration().Sanitized(out _);

            SanitizeBasic(in solver);
            SanitizeAccessibility();
            SanitizeWeightings(in solver);
            SanitizeScaling(in solver);
            SanitizeIntegration();
            SanitizeDiagnostics(in runtime);
        }

        #endregion

        #region 7. SANITISATION PRIMITIVES

        /// <summary>
        /// Sanitises a colour setting channel by channel, falling back per channel when a value is not a number.
        /// </summary>
        /// <param name="value">The loaded colour.</param>
        /// <param name="fallback">The default colour.</param>
        /// <returns>A colour with every channel within 0 to 1.</returns>
        private static Color SaneColor(Color value, Color fallback)
        {
            return new Color(
                ConfigBounds.ClampFloatUntracked(value.r, fallback.r, 0f, 1f),
                ConfigBounds.ClampFloatUntracked(value.g, fallback.g, 0f, 1f),
                ConfigBounds.ClampFloatUntracked(value.b, fallback.b, 0f, 1f),
                ConfigBounds.ClampFloatUntracked(value.a, 1f, 0f, 1f));
        }

        #endregion
    }
}

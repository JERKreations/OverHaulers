using System.Collections.Generic;
using UnityEngine;
using RimWorld;
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

    public class Settings : ModSettings
    {
        #region 2. [SEC-00] NAVIGATION & FIRST-LAUNCH STATE

        public SettingsTab activeTab = SettingsTab.Tuning;
        public bool hasOpenedSettingsBefore = false;

        #endregion

        #region 3. [SEC-01] GLOBAL MODIFIERS & SCALING FIELDS

        public bool enableProsthetics = SettingsDefaults.EnableProsthetics;
        public bool enableAthletics = SettingsDefaults.EnableAthletics;
        public bool enablePartHealth = SettingsDefaults.EnablePartHealth;

        public float prostheticScaling = SettingsDefaults.ProstheticScaling;
        public float athleticScaling = SettingsDefaults.AthleticScaling;
        public float massCapacityFloor = SettingsDefaults.MassCapacityFloor;

        public bool compatibilitySafetyFloor = SettingsDefaults.CompatibilitySafetyFloor;
        public bool devAllowNonPackSpeciesInLivePlay = SettingsDefaults.DevAllowNonPackSpeciesInLivePlay;

        #endregion

        #region 4. [SEC-02] CORE BODY GROUP BUDGET FIELDS

        public float anatomyWeightTorso = SettingsDefaults.AnatomyWeightTorso;
        public float anatomyWeightArm = SettingsDefaults.AnatomyWeightArm;
        public float anatomyWeightLeg = SettingsDefaults.AnatomyWeightLeg;

        #endregion

        #region 5. [SEC-03] POSITIVE SYSTEMIC COUPLING FIELDS

        public bool linkSystemicWeights = SettingsDefaults.LinkSystemicWeights;

        public float torsoPositiveBreathing = SettingsDefaults.TorsoPositiveBreathing;
        public float torsoPositiveBlood = SettingsDefaults.TorsoPositiveBlood;
        public float torsoPositiveMoving = SettingsDefaults.TorsoPositiveMoving;
        public float torsoPositiveManipulation = SettingsDefaults.TorsoPositiveManipulation;

        public float armPositiveBreathing = SettingsDefaults.ArmPositiveBreathing;
        public float armPositiveBlood = SettingsDefaults.ArmPositiveBlood;
        public float armPositiveMoving = SettingsDefaults.ArmPositiveMoving;
        public float armPositiveManipulation = SettingsDefaults.ArmPositiveManipulation;

        public float legPositiveBreathing = SettingsDefaults.LegPositiveBreathing;
        public float legPositiveBlood = SettingsDefaults.LegPositiveBlood;
        public float legPositiveMoving = SettingsDefaults.LegPositiveMoving;
        public float legPositiveManipulation = SettingsDefaults.LegPositiveManipulation;

        #endregion

        #region 6. [SEC-04] NEGATIVE SYSTEMIC COUPLING FIELDS

        public float torsoDeficitBreathing = SettingsDefaults.TorsoDeficitBreathing;
        public float torsoDeficitBlood = SettingsDefaults.TorsoDeficitBlood;
        public float torsoDeficitMoving = SettingsDefaults.TorsoDeficitMoving;
        public float torsoDeficitManipulation = SettingsDefaults.TorsoDeficitManipulation;

        public float armDeficitBreathing = SettingsDefaults.ArmDeficitBreathing;
        public float armDeficitBlood = SettingsDefaults.ArmDeficitBlood;
        public float armDeficitMoving = SettingsDefaults.ArmDeficitMoving;
        public float armDeficitManipulation = SettingsDefaults.ArmDeficitManipulation;

        public float legDeficitBreathing = SettingsDefaults.LegDeficitBreathing;
        public float legDeficitBlood = SettingsDefaults.LegDeficitBlood;
        public float legDeficitMoving = SettingsDefaults.LegDeficitMoving;
        public float legDeficitManipulation = SettingsDefaults.LegDeficitManipulation;

        #endregion

        #region 7. [SEC-05] TORSO MACRO TOPOLOGY FIELDS

        public float torsoAxialBias = SettingsDefaults.TorsoAxialBias;
        public float torsoHpSensitivity = SettingsDefaults.TorsoHpSensitivity;

        #endregion

        #region 8. [SEC-06] LIMB DEPTH DECAY FIELDS

        public float limbDepthDecayFactor = SettingsDefaults.LimbDepthDecayFactor;

        #endregion

        #region 9. [SEC-07] STRUCTURAL SCALING CONSTANTS

        public float prostheticImpactConstant = SettingsDefaults.ProstheticImpactConstant;
        public float athleticImpactConstant = SettingsDefaults.AthleticImpactConstant;

        #endregion

        #region 10. [SEC-08] COMPATIBILITY & PIPELINE DRIVER FIELDS

        public string selectedDriverKey = IntegrationPipeline.DriverKeyAuto;

        public void ResetCompatibility()
        {
            selectedDriverKey = IntegrationPipeline.DriverKeyAuto;
            SettingsViewUtilities.ClearInputBuffers();
        }

        #endregion

        #region 11. [SEC-09] DIAGNOSTICS & DEVELOPER FIELDS

        public bool verboseBreakdown = SettingsDefaults.VerboseBreakdown;
        public int reportMetricsIntervalHours = SettingsDefaults.ReportMetricsIntervalHours;
        public bool logQueryMetrics = SettingsDefaults.LogQueryMetrics;
        public bool logCacheMetrics = SettingsDefaults.LogCacheMetrics;
        public bool logLifecycleMetrics = SettingsDefaults.LogLifecycleMetrics;
        public bool logWorkspaceMetrics = SettingsDefaults.LogWorkspaceMetrics;
        public bool logSafetyFloorClamps = SettingsDefaults.LogSafetyFloorClamps;
        public bool logPawnEvictions = SettingsDefaults.LogPawnEvictions;
        public int pawnEvictionTimeframeHours = SettingsDefaults.PawnEvictionTimeframeHours;

        public int ReportMetricsIntervalTicks => reportMetricsIntervalHours * GenDate.TicksPerHour;
        public int PawnEvictionTimeframeTicks => pawnEvictionTimeframeHours * GenDate.TicksPerHour;

        #endregion

        #region 12. [SEC-10] ACCESSIBILITY & COLOR FIELDS

        public float iconScalePercent = SettingsDefaults.IconScalePercent;
        public PalettePreset activePalettePreset = SettingsDefaults.DefaultPalettePreset;

        public Color colorHealthy = SettingsDefaults.ColorHealthyDefault;
        public Color colorCritical = SettingsDefaults.ColorCriticalDefault;
        public Color colorBoosted = SettingsDefaults.ColorBoostedDefault;

        public void ApplyPreset(PalettePreset preset)
        {
            activePalettePreset = preset;
            switch (preset)
            {
                case PalettePreset.Default:
                    colorHealthy = SettingsDefaults.ColorHealthyDefault;
                    colorCritical = SettingsDefaults.ColorCriticalDefault;
                    colorBoosted = SettingsDefaults.ColorBoostedDefault;
                    break;
                case PalettePreset.Deuteranopia:
                    colorHealthy = SettingsDefaults.ColorHealthyDeuteranopia;
                    colorCritical = SettingsDefaults.ColorCriticalDeuteranopia;
                    colorBoosted = SettingsDefaults.ColorBoostedDeuteranopia;
                    break;
                case PalettePreset.Tritanopia:
                    colorHealthy = SettingsDefaults.ColorHealthyTritanopia;
                    colorCritical = SettingsDefaults.ColorCriticalTritanopia;
                    colorBoosted = SettingsDefaults.ColorBoostedTritanopia;
                    break;
                case PalettePreset.HighContrast:
                    colorHealthy = SettingsDefaults.ColorHealthyHighContrast;
                    colorCritical = SettingsDefaults.ColorCriticalHighContrast;
                    colorBoosted = SettingsDefaults.ColorBoostedHighContrast;
                    break;
                case PalettePreset.Custom:
                    break;
            }

            SettingsViewUtilities.ClearInputBuffers();
            SettingsViewUtilities.OnSettingMutated();
        }

        public void ResetAccessibility()
        {
            iconScalePercent = SettingsDefaults.IconScalePercent;
            ApplyPreset(SettingsDefaults.DefaultPalettePreset);
        }

        #endregion

        #region 13. DYNAMIC READERS & HELPERS

        public float GetBudget(PartType type)
        {
            switch (type)
            {
                case PartType.CorePart: return anatomyWeightTorso;
                case PartType.ManipulationPart: return anatomyWeightArm;
                case PartType.MovingPart: return anatomyWeightLeg;
                default: return 0f;
            }
        }

        #endregion

        #region 14. TAGGED SECTION RESET ROUTINES

        public void ResetGlobals()
        {
            prostheticScaling = SettingsDefaults.ProstheticScaling;
            athleticScaling = SettingsDefaults.AthleticScaling;
            massCapacityFloor = SettingsDefaults.MassCapacityFloor;
            compatibilitySafetyFloor = SettingsDefaults.CompatibilitySafetyFloor;
            devAllowNonPackSpeciesInLivePlay = SettingsDefaults.DevAllowNonPackSpeciesInLivePlay;
            enableProsthetics = SettingsDefaults.EnableProsthetics;
            enableAthletics = SettingsDefaults.EnableAthletics;
            enablePartHealth = SettingsDefaults.EnablePartHealth;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetBodyParts()
        {
            anatomyWeightTorso = SettingsDefaults.AnatomyWeightTorso;
            anatomyWeightArm = SettingsDefaults.AnatomyWeightArm;
            anatomyWeightLeg = SettingsDefaults.AnatomyWeightLeg;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetTorsoPositives()
        {
            torsoPositiveBreathing = SettingsDefaults.TorsoPositiveBreathing;
            torsoPositiveBlood = SettingsDefaults.TorsoPositiveBlood;
            torsoPositiveMoving = SettingsDefaults.TorsoPositiveMoving;
            torsoPositiveManipulation = SettingsDefaults.TorsoPositiveManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetArmPositives()
        {
            armPositiveBreathing = SettingsDefaults.ArmPositiveBreathing;
            armPositiveBlood = SettingsDefaults.ArmPositiveBlood;
            armPositiveMoving = SettingsDefaults.ArmPositiveMoving;
            armPositiveManipulation = SettingsDefaults.ArmPositiveManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetLegPositives()
        {
            legPositiveBreathing = SettingsDefaults.LegPositiveBreathing;
            legPositiveBlood = SettingsDefaults.LegPositiveBlood;
            legPositiveMoving = SettingsDefaults.LegPositiveMoving;
            legPositiveManipulation = SettingsDefaults.LegPositiveManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetTorsoDeficits()
        {
            torsoDeficitBreathing = SettingsDefaults.TorsoDeficitBreathing;
            torsoDeficitBlood = SettingsDefaults.TorsoDeficitBlood;
            torsoDeficitMoving = SettingsDefaults.TorsoDeficitMoving;
            torsoDeficitManipulation = SettingsDefaults.TorsoDeficitManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetArmDeficits()
        {
            armDeficitBreathing = SettingsDefaults.ArmDeficitBreathing;
            armDeficitBlood = SettingsDefaults.ArmDeficitBlood;
            armDeficitMoving = SettingsDefaults.ArmDeficitMoving;
            armDeficitManipulation = SettingsDefaults.ArmDeficitManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetLegDeficits()
        {
            legDeficitBreathing = SettingsDefaults.LegDeficitBreathing;
            legDeficitBlood = SettingsDefaults.LegDeficitBlood;
            legDeficitMoving = SettingsDefaults.LegDeficitMoving;
            legDeficitManipulation = SettingsDefaults.LegDeficitManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetTorsoSystemic()
        {
            ResetTorsoPositives();
            ResetTorsoDeficits();
        }

        public void ResetArmSystemic()
        {
            ResetArmPositives();
            ResetArmDeficits();
        }

        public void ResetLegSystemic()
        {
            ResetLegPositives();
            ResetLegDeficits();
        }

        public void ResetSystemicWeightings()
        {
            linkSystemicWeights = SettingsDefaults.LinkSystemicWeights;
            ResetTorsoSystemic();
            ResetArmSystemic();
            ResetLegSystemic();
        }

        public void ResetTorsoParts()
        {
            torsoAxialBias = SettingsDefaults.TorsoAxialBias;
            torsoHpSensitivity = SettingsDefaults.TorsoHpSensitivity;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetLimbDecay()
        {
            limbDepthDecayFactor = SettingsDefaults.LimbDepthDecayFactor;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetScalingConstants()
        {
            prostheticImpactConstant = SettingsDefaults.ProstheticImpactConstant;
            athleticImpactConstant = SettingsDefaults.AthleticImpactConstant;
            SettingsViewUtilities.ClearInputBuffers();
        }

        public void ResetDiagnostics()
        {
            verboseBreakdown = SettingsDefaults.VerboseBreakdown;
            reportMetricsIntervalHours = SettingsDefaults.ReportMetricsIntervalHours;
            logQueryMetrics = SettingsDefaults.LogQueryMetrics;
            logCacheMetrics = SettingsDefaults.LogCacheMetrics;
            logLifecycleMetrics = SettingsDefaults.LogLifecycleMetrics;
            logWorkspaceMetrics = SettingsDefaults.LogWorkspaceMetrics;
            logSafetyFloorClamps = SettingsDefaults.LogSafetyFloorClamps;
            logPawnEvictions = SettingsDefaults.LogPawnEvictions;
            pawnEvictionTimeframeHours = SettingsDefaults.PawnEvictionTimeframeHours;
            SettingsViewUtilities.ClearInputBuffers();
        }

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

        #region 15. DATA SERIALIZATION (SCRIBE)

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Values.Look(ref activeTab, "activeTab", SettingsTab.Tuning);
            Scribe_Values.Look(ref hasOpenedSettingsBefore, "hasOpenedSettingsBefore", false);

            Scribe_Values.Look(ref enableProsthetics, "enableProsthetics", SettingsDefaults.EnableProsthetics);
            Scribe_Values.Look(ref enableAthletics, "enableAthletics", SettingsDefaults.EnableAthletics);
            Scribe_Values.Look(ref enablePartHealth, "enablePartHealth", SettingsDefaults.EnablePartHealth);
            Scribe_Values.Look(ref prostheticScaling, "prostheticScaling", SettingsDefaults.ProstheticScaling);
            Scribe_Values.Look(ref athleticScaling, "athleticScaling", SettingsDefaults.AthleticScaling);
            Scribe_Values.Look(ref massCapacityFloor, "massCapacityFloor", SettingsDefaults.MassCapacityFloor);
            Scribe_Values.Look(ref compatibilitySafetyFloor, "compatibilitySafetyFloor", SettingsDefaults.CompatibilitySafetyFloor);
            Scribe_Values.Look(ref devAllowNonPackSpeciesInLivePlay, "restrictNonCaravanAnimals", SettingsDefaults.DevAllowNonPackSpeciesInLivePlay);

            Scribe_Values.Look(ref iconScalePercent, "iconScalePercent", SettingsDefaults.IconScalePercent);
            Scribe_Values.Look(ref activePalettePreset, "activePalettePreset", SettingsDefaults.DefaultPalettePreset);
            Scribe_Values.Look(ref colorHealthy, "colorHealthy", SettingsDefaults.ColorHealthyDefault);
            Scribe_Values.Look(ref colorCritical, "colorCritical", SettingsDefaults.ColorCriticalDefault);
            Scribe_Values.Look(ref colorBoosted, "colorBoosted", SettingsDefaults.ColorBoostedDefault);

            Scribe_Values.Look(ref anatomyWeightTorso, "anatomyWeightTorso", SettingsDefaults.AnatomyWeightTorso);
            Scribe_Values.Look(ref anatomyWeightArm, "anatomyWeightArm", SettingsDefaults.AnatomyWeightArm);
            Scribe_Values.Look(ref anatomyWeightLeg, "anatomyWeightLeg", SettingsDefaults.AnatomyWeightLeg);

            Scribe_Values.Look(ref torsoPositiveBreathing, "torsoPositiveBreathing", SettingsDefaults.TorsoPositiveBreathing);
            Scribe_Values.Look(ref torsoPositiveBlood, "torsoPositiveBlood", SettingsDefaults.TorsoPositiveBlood);
            Scribe_Values.Look(ref torsoPositiveMoving, "torsoPositiveMoving", SettingsDefaults.TorsoPositiveMoving);
            Scribe_Values.Look(ref torsoPositiveManipulation, "torsoPositiveManipulation", SettingsDefaults.TorsoPositiveManipulation);
            Scribe_Values.Look(ref armPositiveBreathing, "armPositiveBreathing", SettingsDefaults.ArmPositiveBreathing);
            Scribe_Values.Look(ref armPositiveBlood, "armPositiveBlood", SettingsDefaults.ArmPositiveBlood);
            Scribe_Values.Look(ref armPositiveMoving, "armPositiveMoving", SettingsDefaults.ArmPositiveMoving);
            Scribe_Values.Look(ref armPositiveManipulation, "armPositiveManipulation", SettingsDefaults.ArmPositiveManipulation);
            Scribe_Values.Look(ref legPositiveBreathing, "legPositiveBreathing", SettingsDefaults.LegPositiveBreathing);
            Scribe_Values.Look(ref legPositiveBlood, "legPositiveBlood", SettingsDefaults.LegPositiveBlood);
            Scribe_Values.Look(ref legPositiveMoving, "legPositiveMoving", SettingsDefaults.LegPositiveMoving);
            Scribe_Values.Look(ref legPositiveManipulation, "legPositiveManipulation", SettingsDefaults.LegPositiveManipulation);

            Scribe_Values.Look(ref torsoDeficitBreathing, "torsoDeficitBreathing", SettingsDefaults.TorsoDeficitBreathing);
            Scribe_Values.Look(ref torsoDeficitBlood, "torsoDeficitBlood", SettingsDefaults.TorsoDeficitBlood);
            Scribe_Values.Look(ref torsoDeficitMoving, "torsoDeficitMoving", SettingsDefaults.TorsoDeficitMoving);
            Scribe_Values.Look(ref torsoDeficitManipulation, "torsoDeficitManipulation", SettingsDefaults.TorsoDeficitManipulation);
            Scribe_Values.Look(ref armDeficitBreathing, "armDeficitBreathing", SettingsDefaults.ArmDeficitBreathing);
            Scribe_Values.Look(ref armDeficitBlood, "armDeficitBlood", SettingsDefaults.ArmDeficitBlood);
            Scribe_Values.Look(ref armDeficitMoving, "armDeficitMoving", SettingsDefaults.ArmDeficitMoving);
            Scribe_Values.Look(ref armDeficitManipulation, "armDeficitManipulation", SettingsDefaults.ArmDeficitManipulation);
            Scribe_Values.Look(ref legDeficitBreathing, "legDeficitBreathing", SettingsDefaults.LegDeficitBreathing);
            Scribe_Values.Look(ref legDeficitBlood, "legDeficitBlood", SettingsDefaults.LegDeficitBlood);
            Scribe_Values.Look(ref legDeficitMoving, "legDeficitMoving", SettingsDefaults.LegDeficitMoving);
            Scribe_Values.Look(ref legDeficitManipulation, "legDeficitManipulation", SettingsDefaults.LegDeficitManipulation);

            Scribe_Values.Look(ref torsoAxialBias, "torsoAxialBias", SettingsDefaults.TorsoAxialBias);
            Scribe_Values.Look(ref torsoHpSensitivity, "torsoHpSensitivity", SettingsDefaults.TorsoHpSensitivity);
            Scribe_Values.Look(ref limbDepthDecayFactor, "limbDepthDecayFactor", SettingsDefaults.LimbDepthDecayFactor);

            Scribe_Values.Look(ref prostheticImpactConstant, "prostheticImpactConstant", SettingsDefaults.ProstheticImpactConstant);
            Scribe_Values.Look(ref athleticImpactConstant, "athleticImpactConstant", SettingsDefaults.AthleticImpactConstant);

            Scribe_Values.Look(ref selectedDriverKey, "selectedDriverKey", IntegrationPipeline.DriverKeyAuto);

            Scribe_Values.Look(ref verboseBreakdown, "verboseBreakdown", SettingsDefaults.VerboseBreakdown);
            Scribe_Values.Look(ref reportMetricsIntervalHours, "reportMetricsIntervalHours", SettingsDefaults.ReportMetricsIntervalHours);
            Scribe_Values.Look(ref logQueryMetrics, "logQueryMetrics", SettingsDefaults.LogQueryMetrics);
            Scribe_Values.Look(ref logCacheMetrics, "logCacheMetrics", SettingsDefaults.LogCacheMetrics);
            Scribe_Values.Look(ref logLifecycleMetrics, "logLifecycleMetrics", SettingsDefaults.LogLifecycleMetrics);
            Scribe_Values.Look(ref logWorkspaceMetrics, "logWorkspaceMetrics", SettingsDefaults.LogWorkspaceMetrics);
            Scribe_Values.Look(ref logSafetyFloorClamps, "logSafetyFloorClamps", SettingsDefaults.LogSafetyFloorClamps);
            Scribe_Values.Look(ref logPawnEvictions, "logPawnEvictions", SettingsDefaults.LogPawnEvictions);
            Scribe_Values.Look(ref pawnEvictionTimeframeHours, "pawnEvictionTimeframeHours", SettingsDefaults.PawnEvictionTimeframeHours);
        }

        #endregion
    }
}
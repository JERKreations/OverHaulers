using System;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-01 to SEC-07] Linear, zero-allocation presentation layout for Tab 1 (Tuning &amp; Anatomy).
    /// Encompasses global scalar modifiers, safety floor clamping, core body group weights,
    /// systemic capacity coupling, skeletal topology parameters, and structural scaling constants.
    /// Resides under Source/Settings/Views/.
    /// </summary>
    public static partial class SettingsView
    {
        #region 1. CANONICAL TAB ENTRY POINT

        public static void DrawTuningTabContents(ref SettingsLayout ui, Settings settings)
        {
            // 1. [SEC-01] Main Modifiers & Safety Floors
            DrawBasicModifiersSection(ref ui, settings);
            ui.Divider();

            // 2. [SEC-02] Core Body Group Weights
            DrawCoreBodyWeightsSection(ref ui, settings);
            ui.Divider();

            // 3. [SEC-03 & SEC-04] Systemic Health Weightings
            DrawSystemicHealthWeightsSection(ref ui, settings);
            ui.Divider();

            // 4. [SEC-05 & SEC-06] Torso Macro Topology & Limb Depth Decay
            DrawTorsoAndLimbDepthsSection(ref ui, settings);
            ui.Divider();

            // 5. [SEC-07] Structural Scaling Constants
            DrawScalingConstantsSection(ref ui, settings);
            ui.Gap(16f);
        }

        #endregion

        #region 2. [SEC-01] BASIC MODIFIERS & SAFETY FLOORS

        private static void DrawBasicModifiersSection(ref SettingsLayout ui, Settings settings)
        {
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_GlobalModifiersHeader", settings.ResetGlobals);

            // Row 1: Prosthetics Scaling
            ui.ToggleSlider(
                SettingsViewUtilities.CachedText("OverHaulers_ProstheticScaling_Label"),
                ref settings.enableProsthetics,
                ref settings.prostheticScaling,
                CoreDefaults.ProstheticScalingMin,
                CoreDefaults.ProstheticScalingMax,
                step: 0.01f,
                customLabel: string.Format("{0:P0}", settings.prostheticScaling)
            );
            ui.DescriptionKey("OverHaulers_ProstheticScaling_Desc");
            ui.Gap(4f);

            // Row 2: Athletics Scaling
            ui.ToggleSlider(
                SettingsViewUtilities.CachedText("OverHaulers_AthleticScaling_Label"),
                ref settings.enableAthletics,
                ref settings.athleticScaling,
                CoreDefaults.AthleticScalingMin,
                CoreDefaults.AthleticScalingMax,
                step: 0.01f,
                customLabel: string.Format("{0:P0}", settings.athleticScaling)
            );
            ui.DescriptionKey("OverHaulers_AthleticScaling_Desc");
            ui.Gap(4f);

            // Row 3: Physical Deficits Floor
            string floorReadout = "OverHaulers_DeficitsFloorReadout".Translate(settings.configurablePercentileFloor.ToStringPercent()).ToString();
            ui.ToggleSlider(
                SettingsViewUtilities.CachedText("OverHaulers_Deficits_Label"),
                ref settings.enablePartHealth,
                ref settings.configurablePercentileFloor,
                CoreDefaults.ConfigurablePercentileFloorMin,
                CoreDefaults.ConfigurablePercentileFloorMax,
                step: 0.01f,
                customLabel: floorReadout
            );
            ui.DescriptionKey("OverHaulers_Deficits_Desc");
            ui.Gap(6f);

            // Row 4: Global Compatibility Floor (0.01kg)
            ui.ToggleKey(
                "OverHaulers_EnableGlobalSafetyFloor",
                ref settings.enableHardCompatibilitySafetyFloor,
                "OverHaulers_EnableGlobalSafetyFloor_Tooltip"
            );
        }

        #endregion

        #region 3. [SEC-02] CORE BODY GROUP WEIGHTS

        private static void DrawCoreBodyWeightsSection(ref SettingsLayout ui, Settings settings)
        {
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_BodyPartWeightsHeader", settings.ResetBodyParts);
            ui.DescriptionKey("OverHaulers_BodyPartWeights_Desc");

            float sumBodyParts = settings.anatomyWeightTorso + settings.anatomyWeightArm + settings.anatomyWeightLeg;
            float pctTorso = sumBodyParts > 0f ? (settings.anatomyWeightTorso / sumBodyParts) : 0f;
            float pctArm = sumBodyParts > 0f ? (settings.anatomyWeightArm / sumBodyParts) : 0f;
            float pctLeg = sumBodyParts > 0f ? (settings.anatomyWeightLeg / sumBodyParts) : 0f;

            string torsoLabel = "OverHaulers_TorsoImpact".Translate(pctTorso.ToStringPercent()).ToString();
            ui.Slider(
                torsoLabel,
                ref settings.anatomyWeightTorso,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                customLabel: string.Format("{0:P0}", settings.anatomyWeightTorso),
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_TorsoImpact_Tooltip")
            );

            string armLabel = "OverHaulers_ArmsImpact".Translate(pctArm.ToStringPercent()).ToString();
            ui.Slider(
                armLabel,
                ref settings.anatomyWeightArm,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                customLabel: string.Format("{0:P0}", settings.anatomyWeightArm),
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_ArmsImpact_Tooltip")
            );

            string legLabel = "OverHaulers_LegsImpact".Translate(pctLeg.ToStringPercent()).ToString();
            ui.Slider(
                legLabel,
                ref settings.anatomyWeightLeg,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                customLabel: string.Format("{0:P0}", settings.anatomyWeightLeg),
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_LegsImpact_Tooltip")
            );
        }

        #endregion

        #region 4. [SEC-03 & SEC-04] SYSTEMIC HEALTH WEIGHTINGS

        private static void DrawSystemicHealthWeightsSection(ref SettingsLayout ui, Settings settings)
        {
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_SystemicWeightingsHeader", settings.ResetSystemicWeightings);
            ui.DescriptionKey("OverHaulers_SystemicWeightings_Desc");

            bool prevLink = settings.linkSystemicWeights;
            ui.ToggleKey(
                "OverHaulers_LinkSystemicWeights",
                ref settings.linkSystemicWeights,
                "OverHaulers_LinkSystemicWeights_Tooltip"
            );

            if (!prevLink && settings.linkSystemicWeights)
            {
                SynchronizeLinkedSystemicWeights(settings);
            }

            ui.Gap(6f);

            // 1. Torso Region Card
            ui.SubHeader("OverHaulers_TorsoSystemicHeader".Translate().ToString());
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Breathing".Translate().ToString(), ref settings.torsoPositiveBreathing, ref settings.torsoDeficitBreathing, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_BloodPumping".Translate().ToString(), ref settings.torsoPositiveBlood, ref settings.torsoDeficitBlood, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Moving".Translate().ToString(), ref settings.torsoPositiveMoving, ref settings.torsoDeficitMoving, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Manipulation".Translate().ToString(), ref settings.torsoPositiveManipulation, ref settings.torsoDeficitManipulation, settings.linkSystemicWeights);
            ui.Gap(6f);

            // 2. Manipulation Limbs Card
            ui.SubHeader("OverHaulers_ArmSystemicHeader".Translate().ToString());
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Breathing".Translate().ToString(), ref settings.armPositiveBreathing, ref settings.armDeficitBreathing, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_BloodPumping".Translate().ToString(), ref settings.armPositiveBlood, ref settings.armDeficitBlood, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Moving".Translate().ToString(), ref settings.armPositiveMoving, ref settings.armDeficitMoving, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Manipulation".Translate().ToString(), ref settings.armPositiveManipulation, ref settings.armDeficitManipulation, settings.linkSystemicWeights);
            ui.Gap(6f);

            // 3. Moving Limbs Card
            ui.SubHeader("OverHaulers_LegSystemicHeader".Translate().ToString());
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Breathing".Translate().ToString(), ref settings.legPositiveBreathing, ref settings.legDeficitBreathing, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_BloodPumping".Translate().ToString(), ref settings.legPositiveBlood, ref settings.legDeficitBlood, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Moving".Translate().ToString(), ref settings.legPositiveMoving, ref settings.legDeficitMoving, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Manipulation".Translate().ToString(), ref settings.legPositiveManipulation, ref settings.legDeficitManipulation, settings.linkSystemicWeights);
        }

        private static void DrawDualCapacityRow(
            ref SettingsLayout ui,
            string prefix,
            ref float posValue,
            ref float defValue,
            bool isLinked)
        {
            Rect rowRect = new Rect(ui.X, ui.Y, ui.Width, SettingsLayout.DefaultRowHeight);

            float gap = 10f;
            float minLabelWidth = 120f;

            // Anchor dual 200px slider units to the right margin
            float availableForUnits = ui.Width - minLabelWidth - gap;
            float unitWidth = Math.Min(286f, (availableForUnits - gap) / 2f); // 24px tag + 8px gap + 200px slider + 8px gap + 46px input
            float totalDualWidth = (unitWidth * 2f) + gap;

            float boostX = ui.X + ui.Width - totalDualWidth;
            float defX = boostX + unitWidth + gap;
            float labelWidth = boostX - ui.X - gap;

            // 1. Left: Capacity Label
            Widgets.Label(new Rect(ui.X, rowRect.y + 2f, labelWidth, 22f), prefix);

            // 2. Middle: Boost Unit [+]
            Rect boostRect = new Rect(boostX, rowRect.y, unitWidth, 26f);
            float oldPos = posValue;
            bool boostChanged = ui.DrawSliderInputUnit(boostRect, "[+]", ref posValue, CoreDefaults.PercentageMin, CoreDefaults.PercentageMax, step: 0.01f);

            // 3. Right: Deficit Unit [-] (Flush to right margin)
            Rect defRect = new Rect(defX, rowRect.y, unitWidth, 26f);
            float oldDef = defValue;
            bool defChanged = ui.DrawSliderInputUnit(defRect, "[-]", ref defValue, CoreDefaults.PercentageMin, CoreDefaults.PercentageMax, step: 0.01f);

            if (boostChanged && isLinked)
            {
                defValue = posValue;
                SettingsViewUtilities.OnSettingMutated();
            }
            else if (defChanged && isLinked)
            {
                posValue = defValue;
                SettingsViewUtilities.OnSettingMutated();
            }

            ui.Y += 28f;
        }

        private static void SynchronizeLinkedSystemicWeights(Settings settings)
        {
            settings.torsoDeficitBreathing = settings.torsoPositiveBreathing;
            settings.torsoDeficitBlood = settings.torsoPositiveBlood;
            settings.torsoDeficitMoving = settings.torsoPositiveMoving;
            settings.torsoDeficitManipulation = settings.torsoPositiveManipulation;

            settings.armDeficitBreathing = settings.armPositiveBreathing;
            settings.armDeficitBlood = settings.armPositiveBlood;
            settings.armDeficitMoving = settings.armPositiveMoving;
            settings.armDeficitManipulation = settings.armPositiveManipulation;

            settings.legDeficitBreathing = settings.legPositiveBreathing;
            settings.legDeficitBlood = settings.legPositiveBlood;
            settings.legDeficitMoving = settings.legPositiveMoving;
            settings.legDeficitManipulation = settings.legPositiveManipulation;

            SettingsViewUtilities.OnSettingMutated();
        }

        #endregion

        #region 5. [SEC-05 & SEC-06] TORSO MACRO TOPOLOGY & LIMB DEPTH DECAY

        private static void DrawTorsoAndLimbDepthsSection(ref SettingsLayout ui, Settings settings)
        {
            // Part A: Torso Macro Topology
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_CoreBonesHeader", settings.ResetTorsoParts);
            ui.DescriptionKey("OverHaulers_CoreBones_Desc");

            string axialLabel = "OverHaulers_TorsoAxialBias".Translate(settings.torsoAxialBias.ToStringPercent()).ToString();
            ui.Slider(
                axialLabel,
                ref settings.torsoAxialBias,
                CoreDefaults.TorsoAxialBiasMin,
                CoreDefaults.TorsoAxialBiasMax,
                step: 0.01f
            );
            ui.DescriptionKey("OverHaulers_TorsoAxialBias_Desc");
            ui.Gap(4f);

            string hpSensLabel = "OverHaulers_TorsoHpSensitivity".Translate(settings.torsoHpSensitivity.ToString("F1")).ToString();
            ui.Slider(
                hpSensLabel,
                ref settings.torsoHpSensitivity,
                CoreDefaults.TorsoHpSensitivityMin,
                CoreDefaults.TorsoHpSensitivityMax,
                step: 0.1f
            );
            ui.DescriptionKey("OverHaulers_TorsoHpSensitivity_Desc");
            ui.Gap(6f);

            // Part B: Geometric Limb Depth Decay
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_LimbDepthImpactsHeader", settings.ResetLimbDecay);
            ui.DescriptionKey("OverHaulers_LimbDepth_Desc");

            string decayPctLabel = "OverHaulers_LimbDepthDecayFactor".Translate(settings.limbDepthDecayFactor.ToStringPercent()).ToString();
            ui.Slider(
                decayPctLabel,
                ref settings.limbDepthDecayFactor,
                CoreDefaults.LimbDepthDecayFactorMin,
                CoreDefaults.LimbDepthDecayFactorMax,
                step: 0.01f
            );
            ui.DescriptionKey("OverHaulers_LimbDepthDecayFactor_Desc");
        }

        #endregion

        #region 6. [SEC-07] STRUCTURAL SCALING CONSTANTS

        private static void DrawScalingConstantsSection(ref SettingsLayout ui, Settings settings)
        {
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_ScalingConstantsHeader", settings.ResetScalingConstants);
            ui.DescriptionKey("OverHaulers_ScalingConstants_Desc");

            string prostheticImpactLabel = "OverHaulers_ProstheticImpactConstant".Translate(settings.prostheticImpactConstant.ToString("F1")).ToString();
            ui.Slider(
                prostheticImpactLabel,
                ref settings.prostheticImpactConstant,
                CoreDefaults.ProstheticImpactConstantMin,
                CoreDefaults.ProstheticImpactConstantMax,
                step: 0.1f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_ProstheticImpactConstant_Tooltip")
            );

            string athleticImpactLabel = "OverHaulers_AthleticImpactConstant".Translate(settings.athleticImpactConstant.ToString("F1")).ToString();
            ui.Slider(
                athleticImpactLabel,
                ref settings.athleticImpactConstant,
                CoreDefaults.AthleticImpactConstantMin,
                CoreDefaults.AthleticImpactConstantMax,
                step: 0.1f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_AthleticImpactConstant_Tooltip")
            );
        }

        #endregion

        #region 7. PRIVATE HELPERS

        private static void DrawSectionHeaderWithReset(ref SettingsLayout ui, string headerKey, Action resetAction)
        {
            Rect headerRect = new Rect(ui.X, ui.Y, ui.Width, 30f);

            Text.Font = GameFont.Medium;
            string headerText = SettingsViewUtilities.CachedText(headerKey);
            Widgets.Label(new Rect(headerRect.x, headerRect.y, headerRect.width - 80f, 30f), headerText);
            Text.Font = GameFont.Small;

            if (resetAction != null)
            {
                Rect resetBtnRect = new Rect(headerRect.xMax - 75f, headerRect.y + 2f, 75f, 24f);
                if (Widgets.ButtonText(resetBtnRect, SettingsViewUtilities.CachedText("OverHaulers_Reset")))
                {
                    resetAction();
                    SettingsLayout.ClearInputBuffers();
                    SettingsViewUtilities.OnSettingMutated();
                }
            }

            ui.Y += 34f;
        }

        #endregion
    }
}
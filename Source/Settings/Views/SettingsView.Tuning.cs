using System;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-01 to SEC-07] Linear presentation layout for Tab 1 (Tuning &amp; Anatomy).
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
            ui.Gap(SettingsViewUtilities.GroupGap);
        }

        #endregion

        #region 2. [SEC-01] BASIC MODIFIERS & SAFETY FLOORS

        private static void DrawBasicModifiersSection(ref SettingsLayout ui, Settings settings)
        {
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_GlobalModifiersHeader", settings.ResetGlobals);

            // Row 1: Prosthetics Scaling
            ui.ToggleSlider(
                "OverHaulers_ProstheticScaling_Label".Translate(settings.prostheticScaling.ToString("P0")).ToString(),
                ref settings.enableProsthetics,
                ref settings.prostheticScaling,
                CoreDefaults.ProstheticScalingMin,
                CoreDefaults.ProstheticScalingMax,
                step: 0.01f
            );
            ui.DescriptionKey("OverHaulers_ProstheticScaling_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

            // Row 2: Athletics Scaling
            ui.ToggleSlider(
                "OverHaulers_AthleticScaling_Label".Translate(settings.athleticScaling.ToString("P0")).ToString(),
                ref settings.enableAthletics,
                ref settings.athleticScaling,
                CoreDefaults.AthleticScalingMin,
                CoreDefaults.AthleticScalingMax,
                step: 0.01f
            );
            ui.DescriptionKey("OverHaulers_AthleticScaling_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

            // Row 3: Physical Deficits Floor
            ui.ToggleSlider(
                "OverHaulers_Deficits_Label".Translate(settings.configurablePercentileFloor.ToString("P0")).ToString(),
                ref settings.enablePartHealth,
                ref settings.configurablePercentileFloor,
                CoreDefaults.ConfigurablePercentileFloorMin,
                CoreDefaults.ConfigurablePercentileFloorMax,
                step: 0.01f
            );
            ui.DescriptionKey("OverHaulers_Deficits_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

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

            string torsoLabel = "OverHaulers_TorsoImpact".Translate(pctTorso.ToString("P0"), settings.anatomyWeightTorso.ToString("P0")).ToString();
            ui.Slider(
                torsoLabel,
                ref settings.anatomyWeightTorso,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_TorsoImpact_Tooltip")
            );

            string armLabel = "OverHaulers_ArmsImpact".Translate(pctArm.ToString("P0"), settings.anatomyWeightArm.ToString("P0")).ToString();
            ui.Slider(
                armLabel,
                ref settings.anatomyWeightArm,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_ArmsImpact_Tooltip")
            );

            string legLabel = "OverHaulers_LegsImpact".Translate(pctLeg.ToString("P0"), settings.anatomyWeightLeg.ToString("P0")).ToString();
            ui.Slider(
                legLabel,
                ref settings.anatomyWeightLeg,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
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

            ui.Gap(SettingsViewUtilities.ControlGap);

            // 1. Torso Region Card
            ui.SubHeader("OverHaulers_TorsoSystemicHeader".Translate().ToString());
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Breathing".Translate().ToString(), ref settings.torsoPositiveBreathing, ref settings.torsoDeficitBreathing, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_BloodPumping".Translate().ToString(), ref settings.torsoPositiveBlood, ref settings.torsoDeficitBlood, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Moving".Translate().ToString(), ref settings.torsoPositiveMoving, ref settings.torsoDeficitMoving, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Manipulation".Translate().ToString(), ref settings.torsoPositiveManipulation, ref settings.torsoDeficitManipulation, settings.linkSystemicWeights);
            ui.Gap(SettingsViewUtilities.ControlGap);

            // 2. Manipulation Limbs Card
            ui.SubHeader("OverHaulers_ArmSystemicHeader".Translate().ToString());
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Breathing".Translate().ToString(), ref settings.armPositiveBreathing, ref settings.armDeficitBreathing, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_BloodPumping".Translate().ToString(), ref settings.armPositiveBlood, ref settings.armDeficitBlood, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Moving".Translate().ToString(), ref settings.armPositiveMoving, ref settings.armDeficitMoving, settings.linkSystemicWeights);
            DrawDualCapacityRow(ref ui, "OverHaulers_Prefix_Manipulation".Translate().ToString(), ref settings.armPositiveManipulation, ref settings.armDeficitManipulation, settings.linkSystemicWeights);
            ui.Gap(SettingsViewUtilities.ControlGap);

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

            float gap = SettingsViewUtilities.ControlGap;
            float minLabelWidth = 120f;
            const float maxSliderWidth = 200f;

            // Anchor dual 200px slider units to the right margin
            float availableForUnits = ui.Width - minLabelWidth - gap;
            float maxUnitWidth = SettingsLayout.SliderTagWidth + SettingsLayout.ControlGap * 2f + maxSliderWidth + SettingsLayout.FixedInputWidth;
            float unitWidth = Math.Min(maxUnitWidth, (availableForUnits - gap) / 2f);
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

            string axialLabel = "OverHaulers_TorsoAxialBias".Translate(settings.torsoAxialBias.ToString("P0")).ToString();
            ui.Slider(
                axialLabel,
                ref settings.torsoAxialBias,
                CoreDefaults.TorsoAxialBiasMin,
                CoreDefaults.TorsoAxialBiasMax,
                step: 0.01f
            );
            ui.DescriptionKey("OverHaulers_TorsoAxialBias_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

            string hpSensLabel = "OverHaulers_TorsoHpSensitivity".Translate(settings.torsoHpSensitivity.ToString("F2")).ToString();
            ui.Slider(
                hpSensLabel,
                ref settings.torsoHpSensitivity,
                CoreDefaults.TorsoHpSensitivityMin,
                CoreDefaults.TorsoHpSensitivityMax,
                step: 0.01f
            );
            ui.DescriptionKey("OverHaulers_TorsoHpSensitivity_Desc");
            ui.Gap(SettingsViewUtilities.ControlGap);

            // Part B: Geometric Limb Depth Decay
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_LimbDepthImpactsHeader", settings.ResetLimbDecay);
            ui.DescriptionKey("OverHaulers_LimbDepth_Desc");

            string decayPctLabel = "OverHaulers_LimbDepthDecayFactor".Translate(settings.limbDepthDecayFactor.ToString("P0")).ToString();
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

            string prostheticImpactLabel = "OverHaulers_ProstheticImpactConstant".Translate(settings.prostheticImpactConstant.ToString("F2")).ToString();
            ui.Slider(
                prostheticImpactLabel,
                ref settings.prostheticImpactConstant,
                CoreDefaults.ProstheticImpactConstantMin,
                CoreDefaults.ProstheticImpactConstantMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_ProstheticImpactConstant_Tooltip")
            );

            string athleticImpactLabel = "OverHaulers_AthleticImpactConstant".Translate(settings.athleticImpactConstant.ToString("F2")).ToString();
            ui.Slider(
                athleticImpactLabel,
                ref settings.athleticImpactConstant,
                CoreDefaults.AthleticImpactConstantMin,
                CoreDefaults.AthleticImpactConstantMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_AthleticImpactConstant_Tooltip")
            );
        }

        #endregion

        #region 7. PRIVATE HELPERS

        private static void DrawSectionHeaderWithReset(ref SettingsLayout ui, string headerKey, Action resetAction)
        {
            string headerText = SettingsViewUtilities.CachedText(headerKey);
            string resetLabel = SettingsViewUtilities.CachedText("OverHaulers_Reset");
            Vector2 resetSize = resetAction != null
                ? SettingsViewUtilities.ButtonSize(resetLabel, ui.Width, minWidth: 75f, minHeight: 24f)
                : Vector2.zero;
            Text.Font = GameFont.Medium;
            bool separateReset = resetAction != null &&
                SettingsViewUtilities.MeasuredTextWidth(headerText) + resetSize.x + 5f > ui.Width;
            float headerWidth = resetAction != null && !separateReset ? ui.Width - resetSize.x - 5f : ui.Width;
            float headerHeight = Math.Max(30f, SettingsViewUtilities.MeasuredTextHeight(headerText, headerWidth));
            Widgets.Label(new Rect(ui.X, ui.Y, headerWidth, headerHeight), headerText);
            Text.Font = GameFont.Small;

            float bottom = ui.Y + headerHeight;
            if (resetAction != null)
            {
                float resetY = separateReset ? bottom + 4f : ui.Y + 2f;
                Rect resetBtnRect = new Rect(ui.X + ui.Width - resetSize.x, resetY, resetSize.x, resetSize.y);
                if (SettingsViewUtilities.DrawButton(resetBtnRect, resetLabel))
                {
                    resetAction();
                    SettingsLayout.ClearInputBuffers();
                    SettingsViewUtilities.OnSettingMutated();
                }
                bottom = Math.Max(bottom, resetBtnRect.yMax);
            }

            ui.Y = bottom + 4f;
        }

        #endregion
    }
}
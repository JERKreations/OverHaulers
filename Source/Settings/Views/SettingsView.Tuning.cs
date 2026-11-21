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

        /// <summary>
        /// Draws the contents of the Tuning tab in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the tab contents in.</param>
        /// <param name="settings">The current settings.</param>
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

        /// <summary>
        /// Draws the basic modifiers and safety floors section in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the section in.</param>
        /// <param name="settings">The current settings.</param>
        private static void DrawBasicModifiersSection(ref SettingsLayout ui, Settings settings)
        {
            ui.HeaderWithResetKey("OverHaulers_GlobalModifiersHeader", settings.ResetGlobals, SettingMutationScope.Solver);

            // Row 1: Prosthetics Scaling
            ui.ToggleSlider(
                "OverHaulers_ProstheticScaling_Label".Translate(settings.prostheticScaling.ToString("P0")).ToString(),
                ref settings.enableProsthetics,
                ref settings.prostheticScaling,
                CoreDefaults.ProstheticScalingMin,
                CoreDefaults.ProstheticScalingMax,
                step: 0.01f,
                mutationScope: SettingMutationScope.Solver
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
                step: 0.01f,
                mutationScope: SettingMutationScope.Solver
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
                step: 0.01f,
                mutationScope: SettingMutationScope.Solver
            );
            ui.DescriptionKey("OverHaulers_Deficits_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

            // Row 4: Global Compatibility Floor (0.01kg)
            ui.ToggleKey(
                "OverHaulers_EnableGlobalSafetyFloor",
                ref settings.enableHardCompatibilitySafetyFloor,
                "OverHaulers_EnableGlobalSafetyFloor_Tooltip",
                mutationScope: SettingMutationScope.Solver
            );
        }

        #endregion

        #region 3. [SEC-02] CORE BODY GROUP WEIGHTS

        /// <summary>
        /// Draws the coupled normalized core body group weights and the missing-limb redistribution sub-section.
        /// </summary>
        /// <param name="ui">The settings layout to draw the section in.</param>
        /// <param name="settings">The current settings.</param>
        private static void DrawCoreBodyWeightsSection(ref SettingsLayout ui, Settings settings)
        {
            ui.HeaderWithResetKey("OverHaulers_BodyPartWeightsHeader", settings.ResetBodyParts, SettingMutationScope.Topology);
            ui.DescriptionKey("OverHaulers_BodyPartWeights_Desc");

            string torsoLabel = "OverHaulers_TorsoImpact".Translate(settings.anatomyWeightTorso.ToString("P0")).ToString();
            bool torsoChanged = ui.Slider(
                torsoLabel,
                ref settings.anatomyWeightTorso,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_TorsoImpact_Tooltip"),
                mutationScope: SettingMutationScope.Topology,
                notifyOnMutate: false
            );

            string armLabel = "OverHaulers_ArmsImpact".Translate(settings.anatomyWeightArm.ToString("P0")).ToString();
            bool armChanged = ui.Slider(
                armLabel,
                ref settings.anatomyWeightArm,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_ArmsImpact_Tooltip"),
                mutationScope: SettingMutationScope.Topology,
                notifyOnMutate: false
            );

            string legLabel = "OverHaulers_LegsImpact".Translate(settings.anatomyWeightLeg.ToString("P0")).ToString();
            bool legChanged = ui.Slider(
                legLabel,
                ref settings.anatomyWeightLeg,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_LegsImpact_Tooltip"),
                mutationScope: SettingMutationScope.Topology,
                notifyOnMutate: false
            );

            if (torsoChanged)
            {
                NormalizeCoupledTriplet(
                    ref settings.anatomyWeightTorso,
                    ref settings.anatomyWeightArm,
                    ref settings.anatomyWeightLeg,
                    CoreDefaults.AnatomyWeightArm,
                    CoreDefaults.AnatomyWeightLeg);
                SettingsViewUtilities.OnSettingMutated(SettingMutationScope.Topology);
            }
            else if (armChanged)
            {
                NormalizeCoupledTriplet(
                    ref settings.anatomyWeightArm,
                    ref settings.anatomyWeightTorso,
                    ref settings.anatomyWeightLeg,
                    CoreDefaults.AnatomyWeightTorso,
                    CoreDefaults.AnatomyWeightLeg);
                SettingsViewUtilities.OnSettingMutated(SettingMutationScope.Topology);
            }
            else if (legChanged)
            {
                NormalizeCoupledTriplet(
                    ref settings.anatomyWeightLeg,
                    ref settings.anatomyWeightTorso,
                    ref settings.anatomyWeightArm,
                    CoreDefaults.AnatomyWeightTorso,
                    CoreDefaults.AnatomyWeightArm);
                SettingsViewUtilities.OnSettingMutated(SettingMutationScope.Topology);
            }

            // Sub-section: Conditional Missing-Limb Redistribution Splits
            ui.Gap(SettingsViewUtilities.CompactGap);
            ui.SubHeader(SettingsViewUtilities.CachedText("OverHaulers_RedistributionSubHeader"));
            ui.DescriptionKey("OverHaulers_Redistribution_Desc");

            float missingArmsLegShare = 1.0f - settings.missingArmsTorsoShare;
            string missingArmsLabel = "OverHaulers_MissingArmsSplit".Translate(
                settings.missingArmsTorsoShare.ToString("P0"),
                missingArmsLegShare.ToString("P0")
            ).ToString();
            ui.Slider(
                missingArmsLabel,
                ref settings.missingArmsTorsoShare,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_MissingArmsSplit_Tooltip"),
                mutationScope: SettingMutationScope.Topology
            );

            float missingLegsArmShare = 1.0f - settings.missingLegsTorsoShare;
            string missingLegsLabel = "OverHaulers_MissingLegsSplit".Translate(
                settings.missingLegsTorsoShare.ToString("P0"),
                missingLegsArmShare.ToString("P0")
            ).ToString();
            ui.Slider(
                missingLegsLabel,
                ref settings.missingLegsTorsoShare,
                CoreDefaults.PercentageMin,
                CoreDefaults.PercentageMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_MissingLegsSplit_Tooltip"),
                mutationScope: SettingMutationScope.Topology
            );
        }

        /// <summary>
        /// Proportionally scales the two sibling weights so the coupled triplet always sums exactly to 1.00.
        /// Falls back to factory ratios when both siblings were previously at 0.00.
        /// </summary>
        /// <param name="changed">The value that was changed and needs normalization.</param>
        /// <param name="siblingA">The first sibling weight to be adjusted.</param>
        /// <param name="siblingB">The second sibling weight to be adjusted.</param>
        /// <param name="defaultA">The default value for the first sibling if both siblings are zero.</param>
        /// <param name="defaultB">The default value for the second sibling if both siblings are zero.</param>
        /// <remarks>
        /// This method ensures that the sum of the changed value and its two siblings always equals 1.00.
        /// If both siblings are initially zero, it falls back to the provided default ratios.
        /// </remarks>
        private static void NormalizeCoupledTriplet(
            ref float changed,
            ref float siblingA,
            ref float siblingB,
            float defaultA,
            float defaultB)
        {
            changed = (float)Math.Round(Math.Min(1.0f, Math.Max(0.0f, changed)), 2, MidpointRounding.ToEven);
            float remainder = (float)Math.Round(1.0f - changed, 2, MidpointRounding.ToEven);

            if (remainder <= 0f)
            {
                siblingA = 0f;
                siblingB = 0f;
                return;
            }

            float siblingSum = siblingA + siblingB;
            float ratioA;
            if (siblingSum > 0.0001f)
            {
                ratioA = siblingA / siblingSum;
            }
            else
            {
                float defaultSum = defaultA + defaultB;
                ratioA = defaultSum > 0.0001f ? (defaultA / defaultSum) : 0.5f;
            }

            siblingA = (float)Math.Round(remainder * ratioA, 2, MidpointRounding.ToEven);
            siblingA = Math.Min(remainder, Math.Max(0.0f, siblingA));
            siblingB = (float)Math.Round(remainder - siblingA, 2, MidpointRounding.ToEven);
            siblingB = Math.Min(remainder, Math.Max(0.0f, siblingB));
        }

        #endregion

        #region 4. [SEC-03 & SEC-04] SYSTEMIC HEALTH WEIGHTINGS

        /// <summary>
        /// Draws the systemic health weights section in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the section in.</param>
        /// <param name="settings">The current settings.</param>
        private static void DrawSystemicHealthWeightsSection(ref SettingsLayout ui, Settings settings)
        {
            ui.HeaderWithResetKey("OverHaulers_SystemicWeightingsHeader", settings.ResetSystemicWeightings, SettingMutationScope.Solver);
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

        /// <summary>
        /// Draws a row for dual capacity settings (positive and deficit) with an optional link between them.
        /// </summary>
        /// <param name="ui">The settings layout to draw the row in.</param>
        /// <param name="prefix">The prefix text for the row label.</param>
        /// <param name="posValue">The positive capacity value.</param>
        /// <param name="defValue">The deficit capacity value.</param>
        /// <param name="isLinked">Whether the positive and deficit values are linked.</param>
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
            bool boostChanged = ui.DrawSliderInputUnit(boostRect, "[+]", ref posValue,
                CoreDefaults.PercentageMin, CoreDefaults.PercentageMax, step: 0.01f, notifyOnMutate: false);

            // 3. Right: Deficit Unit [-] (Flush to right margin)
            Rect defRect = new Rect(defX, rowRect.y, unitWidth, 26f);
            bool defChanged = ui.DrawSliderInputUnit(defRect, "[-]", ref defValue,
                CoreDefaults.PercentageMin, CoreDefaults.PercentageMax, step: 0.01f, notifyOnMutate: false);

            if (boostChanged || defChanged)
            {
                if (isLinked)
                {
                    if (boostChanged) defValue = posValue;
                    else posValue = defValue;
                }

                SettingsViewUtilities.OnSettingMutated(SettingMutationScope.Solver);
            }

            ui.Y += 28f;
        }

        /// <summary>
        /// Synchronizes the linked systemic weights for torso, arm, and leg deficits based on their positive counterparts.
        /// </summary>
        /// <param name="settings">The current settings.</param>
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

            SettingsViewUtilities.OnSettingMutated(SettingMutationScope.Solver);
        }

        #endregion

        #region 5. [SEC-05 & SEC-06] TORSO MACRO TOPOLOGY & LIMB DEPTH DECAY

        /// <summary>
        /// Draws the torso macro topology and limb depth/pair scaling section in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the section in.</param>
        /// <param name="settings">The current settings.</param>
        private static void DrawTorsoAndLimbDepthsSection(ref SettingsLayout ui, Settings settings)
        {
            // Part A: Torso Macro Topology
            ui.HeaderWithResetKey("OverHaulers_CoreBonesHeader", settings.ResetTorsoParts, SettingMutationScope.Topology);
            ui.DescriptionKey("OverHaulers_CoreBones_Desc");

            string axialLabel = "OverHaulers_TorsoAxialBias".Translate(settings.torsoAxialBias.ToString("P0")).ToString();
            ui.Slider(
                axialLabel,
                ref settings.torsoAxialBias,
                CoreDefaults.TorsoAxialBiasMin,
                CoreDefaults.TorsoAxialBiasMax,
                step: 0.01f,
                mutationScope: SettingMutationScope.Topology
            );
            ui.DescriptionKey("OverHaulers_TorsoAxialBias_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

            string hpSensLabel = "OverHaulers_TorsoHpSensitivity".Translate(settings.torsoHpSensitivity.ToString("F2")).ToString();
            ui.Slider(
                hpSensLabel,
                ref settings.torsoHpSensitivity,
                CoreDefaults.TorsoHpSensitivityMin,
                CoreDefaults.TorsoHpSensitivityMax,
                step: 0.01f,
                mutationScope: SettingMutationScope.Topology
            );
            ui.DescriptionKey("OverHaulers_TorsoHpSensitivity_Desc");
            ui.Gap(SettingsViewUtilities.ControlGap);

            // Part B: Geometric Limb Depth Decay & Auxiliary Pair Scaling
            ui.HeaderWithResetKey("OverHaulers_LimbDepthImpactsHeader", settings.ResetLimbDecay, SettingMutationScope.Topology);
            ui.DescriptionKey("OverHaulers_LimbDepth_Desc");

            string decayPctLabel = "OverHaulers_LimbDepthDecayFactor".Translate(settings.limbDepthDecayFactor.ToString("P0")).ToString();
            ui.Slider(
                decayPctLabel,
                ref settings.limbDepthDecayFactor,
                CoreDefaults.LimbDepthDecayFactorMin,
                CoreDefaults.LimbDepthDecayFactorMax,
                step: 0.01f,
                mutationScope: SettingMutationScope.Topology
            );
            ui.DescriptionKey("OverHaulers_LimbDepthDecayFactor_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

            string pairDecayPctLabel = "OverHaulers_LimbPairDecayFactor".Translate(settings.limbPairDecayFactor.ToString("P0")).ToString();
            ui.Slider(
                pairDecayPctLabel,
                ref settings.limbPairDecayFactor,
                CoreDefaults.LimbPairDecayFactorMin,
                CoreDefaults.LimbPairDecayFactorMax,
                step: 0.01f,
                mutationScope: SettingMutationScope.Topology
            );
            ui.DescriptionKey("OverHaulers_LimbPairDecayFactor_Desc");

            DrawLimbPairCurveLivePreview(ref ui, settings);
        }

        /// <summary>
        /// Renders the real-time mathematical progression readout for auxiliary limb pair decay.
        /// </summary>
        /// <param name="ui">The settings layout to draw the progression in.</param>
        /// <param name="settings">The current settings.</param>
        private static void DrawLimbPairCurveLivePreview(ref SettingsLayout ui, Settings settings)
        {
            float lambda = settings.limbPairDecayFactor;
            float p1 = 1.0f;
            float p2 = p1 * lambda;
            float p3 = p2 * lambda;
            float p4 = p3 * lambda;
            float total4 = p1 + p2 + p3 + p4;

            string progressionText = "OverHaulers_LimbPair_Progression".Translate(
                p1.ToString("P0"),
                p2.ToString("P0"),
                p3.ToString("P0"),
                p4.ToString("P0"),
                total4.ToString("F2")
            ).ToString();

            Text.Font = GameFont.Tiny;
            Color origColor = GUI.color;
            try
            {
                GUI.color = SettingsLayout.MutedTextColor;
                float labelH = Math.Max(18f, SettingsViewUtilities.CalcTextHeight(progressionText, ui.Width, GameFont.Tiny));
                Widgets.Label(new Rect(ui.X, ui.Y, ui.Width, labelH), progressionText);
                ui.Y += labelH + SettingsViewUtilities.CompactGap;
            }
            finally
            {
                GUI.color = origColor;
                Text.Font = GameFont.Small;
            }
        }

        #endregion

        #region 6. [SEC-07] STRUCTURAL SCALING CONSTANTS

        /// <summary>
        /// Draws the scaling constants section in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the section in.</param>
        /// <param name="settings">The current settings.</param>
        private static void DrawScalingConstantsSection(ref SettingsLayout ui, Settings settings)
        {
            ui.HeaderWithResetKey("OverHaulers_ScalingConstantsHeader", settings.ResetScalingConstants, SettingMutationScope.Solver);
            ui.DescriptionKey("OverHaulers_ScalingConstants_Desc");

            string prostheticImpactLabel = "OverHaulers_ProstheticImpactConstant".Translate(settings.prostheticImpactConstant.ToString("F2")).ToString();
            ui.Slider(
                prostheticImpactLabel,
                ref settings.prostheticImpactConstant,
                CoreDefaults.ProstheticImpactConstantMin,
                CoreDefaults.ProstheticImpactConstantMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_ProstheticImpactConstant_Tooltip"),
                mutationScope: SettingMutationScope.Solver
            );

            string athleticImpactLabel = "OverHaulers_AthleticImpactConstant".Translate(settings.athleticImpactConstant.ToString("F2")).ToString();
            ui.Slider(
                athleticImpactLabel,
                ref settings.athleticImpactConstant,
                CoreDefaults.AthleticImpactConstantMin,
                CoreDefaults.AthleticImpactConstantMax,
                step: 0.01f,
                tooltip: SettingsViewUtilities.CachedText("OverHaulers_AthleticImpactConstant_Tooltip"),
                mutationScope: SettingMutationScope.Solver
            );
        }

        #endregion
    }
}
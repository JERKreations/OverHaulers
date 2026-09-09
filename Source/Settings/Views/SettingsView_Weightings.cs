using System;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    public static partial class SettingsView
    {
        #region 1. [SEC-02] CORE BODY GROUP WEIGHTS DRAWER

        public static void DrawCoreBodyGroupWeights(Rect viewRect, ref float currentY, Settings settings, Action resetAnatomy)
        {
            Rect inner = SettingsViewUtilities.BeginSectionBoxDirect(viewRect, "SEC_CoreBodyWeights", ref currentY, fallbackEstimate: 180f);
            float startY = inner.y;
            float localY = inner.y;

            try
            {
                bool isResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(inner, "OverHaulers_BodyPartWeightsHeader", resetAnatomy, localY, out localY);

                float totalWidth = inner.width;
                float gap = 16f;
                float leftWidth = (totalWidth * 0.46f) - (gap / 2f);
                float rightWidth = totalWidth - leftWidth - gap;

                float colStartY = localY;

                // LEFT COLUMN: Description & Anatomical Rationale
                string leftDesc = "OverHaulers_BodyPartImpactsLabel".Translate().ToString();
                float leftY = SettingsViewUtilities.DrawSectionDescriptionDirect(new Rect(inner.x, colStartY, leftWidth, 0f), leftDesc, colStartY);

                // RIGHT COLUMN: 3 Normalized Impact Sliders
                float rightX = inner.x + leftWidth + gap;
                float rightY = colStartY;
                Rect rightColRect = new Rect(rightX, rightY, rightWidth, 0f);

                float sumBodyParts = settings.anatomyWeightTorso + settings.anatomyWeightArm + settings.anatomyWeightLeg;
                float pctTorso = sumBodyParts > 0f ? (settings.anatomyWeightTorso / sumBodyParts) : 0f;
                float pctArm = sumBodyParts > 0f ? (settings.anatomyWeightArm / sumBodyParts) : 0f;
                float pctLeg = sumBodyParts > 0f ? (settings.anatomyWeightLeg / sumBodyParts) : 0f;

                rightY = SettingsViewUtilities.DrawSettingRowDirect(
                    rightColRect,
                    "OverHaulers_TorsoImpact".Translate(pctTorso.ToStringPercent()).ToString(),
                    ref settings.anatomyWeightTorso,
                    SettingsDefaults.PercentageMin, SettingsDefaults.PercentageMax,
                    rightY,
                    "OverHaulers_TorsoImpact_Tooltip".Translate().ToString(),
                    highlight: isResetHovered
                );

                rightY = SettingsViewUtilities.DrawSettingRowDirect(
                    rightColRect,
                    "OverHaulers_ArmsImpact".Translate(pctArm.ToStringPercent()).ToString(),
                    ref settings.anatomyWeightArm,
                    SettingsDefaults.PercentageMin, SettingsDefaults.PercentageMax,
                    rightY,
                    "OverHaulers_ArmsImpact_Tooltip".Translate().ToString(),
                    highlight: isResetHovered
                );

                rightY = SettingsViewUtilities.DrawSettingRowDirect(
                    rightColRect,
                    "OverHaulers_LegsImpact".Translate(pctLeg.ToStringPercent()).ToString(),
                    ref settings.anatomyWeightLeg,
                    SettingsDefaults.PercentageMin, SettingsDefaults.PercentageMax,
                    rightY,
                    "OverHaulers_LegsImpact_Tooltip".Translate().ToString(),
                    highlight: isResetHovered
                );

                localY = Mathf.Max(leftY, rightY) + 4f;
            }
            finally
            {
                SettingsViewUtilities.EndSectionBoxDirect("SEC_CoreBodyWeights", startY, localY, ref currentY);
            }
        }

        #endregion

        #region 2. [SEC-03 & SEC-04] SYSTEMIC HEALTH WEIGHTINGS DRAWER

        public static void DrawSystemicHealthWeights(
            Rect viewRect, 
            ref float currentY, 
            Settings settings, 
            Action resetTorso, 
            Action resetArm, 
            Action resetLeg,
            Action resetAll)
        {
            Rect inner = SettingsViewUtilities.BeginSectionBoxDirect(viewRect, "SEC_SystemicWeightings", ref currentY, fallbackEstimate: 460f);
            float startY = inner.y;
            float localY = inner.y;

            try
            {
                bool isMainResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(inner, "OverHaulers_SystemicWeightingsHeader", resetAll, localY, out localY);

                string mainDesc = "OverHaulers_SystemicWeightings_Desc".Translate().ToString();
                localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, mainDesc, localY);

                // Symmetry Switch (Toggles real-time dual-slider mirroring)
                bool prevLink = settings.linkSystemicWeights;
                localY = SettingsViewUtilities.DrawCheckboxRowDirect(
                    inner,
                    "OverHaulers_LinkSystemicWeights".Translate().ToString(),
                    ref settings.linkSystemicWeights,
                    localY,
                    "OverHaulers_LinkSystemicWeights_Tooltip".Translate().ToString(),
                    isMainResetHovered
                );

                if (!prevLink && settings.linkSystemicWeights)
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

                // 1. TORSO REGION CARD
                localY = DrawRegionCard(
                    inner,
                    "OverHaulers_TorsoSystemicHeader",
                    ref settings.torsoPositiveBreathing, ref settings.torsoDeficitBreathing,
                    ref settings.torsoPositiveBlood, ref settings.torsoDeficitBlood,
                    ref settings.torsoPositiveMoving, ref settings.torsoDeficitMoving,
                    ref settings.torsoPositiveManipulation, ref settings.torsoDeficitManipulation,
                    settings.linkSystemicWeights,
                    resetTorso,
                    localY,
                    isMainResetHovered
                );

                // 2. MANIPULATION LIMBS CARD
                localY = DrawRegionCard(
                    inner,
                    "OverHaulers_ArmSystemicHeader",
                    ref settings.armPositiveBreathing, ref settings.armDeficitBreathing,
                    ref settings.armPositiveBlood, ref settings.armDeficitBlood,
                    ref settings.armPositiveMoving, ref settings.armDeficitMoving,
                    ref settings.armPositiveManipulation, ref settings.armDeficitManipulation,
                    settings.linkSystemicWeights,
                    resetArm,
                    localY,
                    isMainResetHovered
                );

                // 3. MOVING LIMBS CARD
                localY = DrawRegionCard(
                    inner,
                    "OverHaulers_LegSystemicHeader",
                    ref settings.legPositiveBreathing, ref settings.legDeficitBreathing,
                    ref settings.legPositiveBlood, ref settings.legDeficitBlood,
                    ref settings.legPositiveMoving, ref settings.legDeficitMoving,
                    ref settings.legPositiveManipulation, ref settings.legDeficitManipulation,
                    settings.linkSystemicWeights,
                    resetLeg,
                    localY,
                    isMainResetHovered
                );
            }
            finally
            {
                SettingsViewUtilities.EndSectionBoxDirect("SEC_SystemicWeightings", startY, localY, ref currentY);
            }
        }

        #endregion

        #region 3. [SEC-05 & SEC-06] TORSO MACRO TOPOLOGY & LIMB DEPTH DECAY DRAWER (50/50 Split)

        public static void DrawTorsoAndLimbDepths(Rect viewRect, ref float currentY, Settings settings, Action resetTorso, Action resetLimbDepths)
        {
            float cachedHeight = SettingsViewUtilities.GetCachedSectionHeight("SEC_TorsoLimbDepths", 260f);
            Rect rowRect = new Rect(0f, currentY, viewRect.width, cachedHeight);

            float halfWidth = (rowRect.width / 2f) - 6f;
            Rect leftBox = new Rect(rowRect.x, rowRect.y, halfWidth, rowRect.height);
            Rect rightBox = new Rect(rowRect.x + halfWidth + 12f, rowRect.y, halfWidth, rowRect.height);

            Widgets.DrawMenuSection(leftBox);
            Widgets.DrawMenuSection(rightBox);

            Rect leftInner = leftBox.ContractedBy(10f);
            Rect rightInner = rightBox.ContractedBy(10f);

            string leftLabel = "OverHaulers_CoreBonesLabel".Translate().ToString();
            string rightLabel = "OverHaulers_LimbDepthImpactsLabel".Translate().ToString();

            string axialLabel = "OverHaulers_TorsoAxialBias".Translate(settings.torsoAxialBias.ToStringPercent()).ToString();
            string axialDesc = "OverHaulers_TorsoAxialBias_Desc".Translate().ToString();

            string hpSensLabel = "OverHaulers_TorsoHpSensitivity".Translate(settings.torsoHpSensitivity.ToString("F1")).ToString();
            string hpSensDesc = "OverHaulers_TorsoHpSensitivity_Desc".Translate().ToString();

            string decayPctLabel = "OverHaulers_LimbDepthDecayFactor".Translate(settings.limbDepthDecayFactor.ToStringPercent()).ToString();
            string decayDesc = "OverHaulers_LimbDepthDecayFactor_Desc".Translate().ToString();

            // Column A: Torso Macro Topology Controls [SEC-05]
            float leftY = leftInner.y;
            bool isTorsoResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(leftInner, "OverHaulers_CoreBonesHeader", resetTorso, leftY, out leftY);
            leftY = SettingsViewUtilities.DrawSectionDescriptionDirect(leftInner, leftLabel, leftY);

            leftY = SettingsViewUtilities.DrawSettingRowWithDescriptionDirect(
                leftInner,
                axialLabel,
                ref settings.torsoAxialBias,
                SettingsDefaults.TorsoAxialBiasMin, SettingsDefaults.TorsoAxialBiasMax,
                axialDesc,
                leftY,
                highlight: isTorsoResetHovered
            );

            leftY = SettingsViewUtilities.DrawSettingRowWithDescriptionDirect(
                leftInner,
                hpSensLabel,
                ref settings.torsoHpSensitivity,
                SettingsDefaults.TorsoHpSensitivityMin, SettingsDefaults.TorsoHpSensitivityMax,
                hpSensDesc,
                leftY,
                highlight: isTorsoResetHovered
            );

            // Column B: Geometric Limb Depth Decay Controls [SEC-06]
            float rightY = rightInner.y;
            bool isLimbResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(rightInner, "OverHaulers_LimbDepthImpactsHeader", resetLimbDepths, rightY, out rightY);
            rightY = SettingsViewUtilities.DrawSectionDescriptionDirect(rightInner, rightLabel, rightY);

            rightY = SettingsViewUtilities.DrawSettingRowWithDescriptionDirect(
                rightInner,
                decayPctLabel,
                ref settings.limbDepthDecayFactor,
                SettingsDefaults.LimbDepthDecayFactorMin, SettingsDefaults.LimbDepthDecayFactorMax,
                decayDesc,
                rightY,
                highlight: isLimbResetHovered
            );

            float leftHeightDrawn = leftY - leftInner.y;
            float rightHeightDrawn = rightY - rightInner.y;
            float actualMaxHeight = Mathf.Max(leftHeightDrawn, rightHeightDrawn) + 20f;

            SettingsViewUtilities.RecordSectionHeight("SEC_TorsoLimbDepths", actualMaxHeight);
            currentY += actualMaxHeight + 15f;
        }

        #endregion

        #region 4. MODULAR REGIONAL CARD DRAWERS

        private static float DrawRegionCard(
            Rect inner,
            string headerKey,
            ref float posBreathing, ref float defBreathing,
            ref float posBlood, ref float defBlood,
            ref float posMoving, ref float defMoving,
            ref float posManip, ref float defManip,
            bool isLinked,
            Action resetAction,
            float localY,
            bool isMainResetHovered)
        {
            bool isResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(inner, headerKey, resetAction, localY, out localY);
            bool highlight = isMainResetHovered || isResetHovered;

            localY = DrawSingleLineDualCapacityRow(inner, "OverHaulers_Prefix_Breathing".Translate().ToString(), ref posBreathing, ref defBreathing, isLinked, localY, highlight);
            localY = DrawSingleLineDualCapacityRow(inner, "OverHaulers_Prefix_BloodPumping".Translate().ToString(), ref posBlood, ref defBlood, isLinked, localY, highlight);
            localY = DrawSingleLineDualCapacityRow(inner, "OverHaulers_Prefix_Moving".Translate().ToString(), ref posMoving, ref defMoving, isLinked, localY, highlight);
            localY = DrawSingleLineDualCapacityRow(inner, "OverHaulers_Prefix_Manipulation".Translate().ToString(), ref posManip, ref defManip, isLinked, localY, highlight);

            return localY + SettingsViewUtilities.GroupGap;
        }

        private static float DrawSingleLineDualCapacityRow(
            Rect inner,
            string capacityPrefix,
            ref float posValue,
            ref float defValue,
            bool isLinked,
            float currentY,
            bool highlight)
        {
            Rect rowRect = new Rect(inner.x, currentY, inner.width, 24f);

            if (highlight)
            {
                Widgets.DrawBoxSolid(rowRect.ExpandedBy(2f, 1f), SettingsViewUtilities.DarkTargetHighlightColor);
            }

            float prefixWidth = 110f;
            float gap = 10f;
            float remainingWidth = rowRect.width - prefixWidth - gap;
            float unitWidth = (remainingWidth - gap) / 2f;

            Rect prefixRect = new Rect(rowRect.x, rowRect.y + 2f, prefixWidth, 20f);
            Rect boostRect = new Rect(rowRect.x + prefixWidth + gap, rowRect.y, unitWidth, rowRect.height);
            Rect deficitRect = new Rect(boostRect.x + unitWidth + gap, rowRect.y, unitWidth, rowRect.height);

            Text.Font = GameFont.Small;
            Widgets.Label(prefixRect, capacityPrefix);

            float oldPos = posValue;
            float oldDef = defValue;

            string posLabel = "OverHaulers_BoostShort".Translate(posValue.ToStringPercent()).ToString();
            SettingsViewUtilities.DrawSliderAndInputRowDirect(boostRect, posLabel, ref posValue, SettingsDefaults.PercentageMin, SettingsDefaults.PercentageMax, isInteger: false);

            string defLabel = "OverHaulers_DeficitShort".Translate(defValue.ToStringPercent()).ToString();
            SettingsViewUtilities.DrawSliderAndInputRowDirect(deficitRect, defLabel, ref defValue, SettingsDefaults.PercentageMin, SettingsDefaults.PercentageMax, isInteger: false);

            if (isLinked)
            {
                if (Mathf.Abs(posValue - oldPos) > 0.0001f)
                {
                    defValue = posValue;
                }
                else if (Mathf.Abs(defValue - oldDef) > 0.0001f)
                {
                    posValue = defValue;
                }
            }

            return currentY + 28f;
        }

        #endregion
    }
}
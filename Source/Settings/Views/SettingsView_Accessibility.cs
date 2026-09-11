using System;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Provides the user interface for accessibility settings, including color palette customization and icon scale adjustments.
    /// </summary>
    public static partial class SettingsView
    {
        #region [SEC-10] ACCESSIBILITY & MEDICAL COLOR PALETTE DRAWER

        /// <summary>
        /// Draws the accessibility section of the settings view, including icon scale and color palette options.
        /// </summary>
        /// <param name="viewRect">The rectangle defining the area in which to draw the section.</param>
        /// <param name="currentY">The current vertical position within the view, updated as elements are drawn.</param>
        /// <param name="settings">The settings object containing accessibility options.</param>
        /// <param name="resetAction">The action to invoke when the reset button is clicked.</param>
        public static void DrawAccessibilitySection(Rect viewRect, ref float currentY, Settings settings, Action resetAction)
        {
            Rect inner = SettingsViewUtilities.BeginSectionBoxDirect(viewRect, "SEC_Accessibility", ref currentY, fallbackEstimate: 360f);
            float startY = inner.y;
            float localY = inner.y;

            try
            {
                bool isResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(inner, "OverHaulers_AccessibilityHeader", resetAction, localY, out localY);

                // 1. Breakdown Icon Scale Setting
                string iconScaleLabel = "OverHaulers_IconScaleLabel".Translate((settings.iconScalePercent * 100f).ToString("F0")).ToString();
                string iconScaleDesc = "OverHaulers_IconScale_Desc".Translate().ToString();

                localY = SettingsViewUtilities.DrawSettingRowWithDescriptionDirect(
                    inner, iconScaleLabel, ref settings.iconScalePercent,
                    SettingsDefaults.IconScalePercentMin, SettingsDefaults.IconScalePercentMax,
                    iconScaleDesc, localY
                );
                localY += 6f;

                // 2. Inline Sub-Header
                Rect colorHeaderRect = new Rect(inner.x, localY, inner.width, 22f);
                Text.Font = GameFont.Small;
                Widgets.Label(colorHeaderRect, "OverHaulers_ColorPaletteHeader".Translate().ToString());
                localY += 26f;

                string sectionDesc = "OverHaulers_Accessibility_Desc".Translate().ToString();
                localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, sectionDesc, localY);

                // 3. Preset Radio Toggles
                float halfWidth = (inner.width / 2f) - 8f;
                Rect row1Rect = new Rect(inner.x, localY, inner.width, 24f);
                
                if (Widgets.RadioButtonLabeled(new Rect(row1Rect.x, row1Rect.y, halfWidth, row1Rect.height), "OverHaulers_PresetDefault".Translate().ToString(), settings.activePalettePreset == PalettePreset.Default)) settings.ApplyPreset(PalettePreset.Default);
                if (Widgets.RadioButtonLabeled(new Rect(row1Rect.x + halfWidth + 16f, row1Rect.y, halfWidth, row1Rect.height), "OverHaulers_PresetDeuteranopia".Translate().ToString(), settings.activePalettePreset == PalettePreset.Deuteranopia)) settings.ApplyPreset(PalettePreset.Deuteranopia);
                localY += 26f;

                Rect row2Rect = new Rect(inner.x, localY, inner.width, 24f);
                if (Widgets.RadioButtonLabeled(new Rect(row2Rect.x, row2Rect.y, halfWidth, row2Rect.height), "OverHaulers_PresetTritanopia".Translate().ToString(), settings.activePalettePreset == PalettePreset.Tritanopia)) settings.ApplyPreset(PalettePreset.Tritanopia);
                if (Widgets.RadioButtonLabeled(new Rect(row2Rect.x + halfWidth + 16f, row2Rect.y, halfWidth, row2Rect.height), "OverHaulers_PresetHighContrast".Translate().ToString(), settings.activePalettePreset == PalettePreset.HighContrast)) settings.ApplyPreset(PalettePreset.HighContrast);
                localY += 26f;

                GUI.enabled = false;
                Widgets.RadioButtonLabeled(new Rect(inner.x, localY, inner.width, 22f), "OverHaulers_PresetCustom".Translate().ToString(), settings.activePalettePreset == PalettePreset.Custom);
                GUI.enabled = true;
                localY += 28f;

                // 4. Live 3-Stop Gradient Preview Bar
                localY = DrawGradientPreviewDirect(inner, settings.colorCritical, settings.colorHealthy, settings.colorBoosted, localY);

                // 5. Interactive RGB Slider Rows
                Action onSliderChanged = () => 
                { 
                    settings.activePalettePreset = PalettePreset.Custom; 
                    SettingsViewUtilities.ClearInputBuffers(); 
                    SettingsViewUtilities.OnSettingMutated(); 
                };

                localY = DrawColorSliderRowDirect(inner, "OverHaulers_ColorCriticalLabel".Translate().ToString(), ref settings.colorCritical, localY, onSliderChanged);
                localY = DrawColorSliderRowDirect(inner, "OverHaulers_ColorHealthyLabel".Translate().ToString(), ref settings.colorHealthy, localY, onSliderChanged);
                localY = DrawColorSliderRowDirect(inner, "OverHaulers_ColorBoostedLabel".Translate().ToString(), ref settings.colorBoosted, localY, onSliderChanged);
            }
            finally
            {
                SettingsViewUtilities.EndSectionBoxDirect("SEC_Accessibility", startY, localY, ref currentY);
            }
        }

        #endregion

        #region PRIVATE ACCESSIBILITY IMGUI HELPERS

        /// <summary>
        /// Draws a live gradient preview bar representing the transition from critical to healthy to boosted colors.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area in which to draw the gradient preview.</param>
        /// <param name="critical">The color representing the critical state.</param>
        /// <param name="healthy">The color representing the healthy state.</param>
        /// <param name="boosted">The color representing the boosted state.</param>
        /// <param name="currentY">The current vertical position within the container, updated as elements are drawn.</param>
        /// <returns>The updated vertical position after drawing the gradient preview.</returns>
        private static float DrawGradientPreviewDirect(Rect containerRect, Color critical, Color healthy, Color boosted, float currentY)
        {
            Rect labelRect = new Rect(containerRect.x, currentY, containerRect.width, 20f);
            Text.Font = GameFont.Tiny;
            Color origColor = GUI.color;
            GUI.color = SettingsViewUtilities.DescriptionTextColor;
            Widgets.Label(labelRect, "OverHaulers_GradientPreviewHeader".Translate().ToString());
            GUI.color = origColor;
            Text.Font = GameFont.Small;
            currentY += 22f;

            float barWidth = containerRect.width;
            float barHeight = 16f;
            Rect barRect = new Rect(containerRect.x, currentY, barWidth, barHeight);

            Widgets.DrawBoxSolid(barRect.ExpandedBy(1f), Color.black);

            int segments = 32;
            float segmentWidth = barWidth / segments;
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / (segments - 1);
                Color segColor = t <= 0.5f ? Color.Lerp(critical, healthy, t / 0.5f) : Color.Lerp(healthy, boosted, (t - 0.5f) / 0.5f);
                Widgets.DrawBoxSolid(new Rect(barRect.x + (i * segmentWidth), barRect.y, segmentWidth + 0.5f, barHeight), segColor);
            }

            currentY += barHeight + 2f;
            Rect tickRect = new Rect(containerRect.x, currentY, containerRect.width, 16f);
            Text.Font = GameFont.Tiny;
            GUI.color = SettingsViewUtilities.DescriptionTextColor;
            Widgets.Label(tickRect.LeftPart(0.20f), "0%");
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(tickRect, "100%");
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(tickRect.RightPart(0.20f), "200%+");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = origColor;
            Text.Font = GameFont.Small;

            return currentY + 18f;
        }

        /// <summary>
        /// Draws a color slider row for adjusting the red, green, and blue components of a color.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area in which to draw the color slider row.</param>
        /// <param name="labelText">The label text to display next to the color swatch.</param>
        /// <param name="color">The color to be adjusted.</param>
        /// <param name="currentY">The current vertical position within the container, updated as elements are drawn.</param>
        /// <param name="onColorChanged">The action to invoke when the color is changed.</param>
        /// <returns>The updated vertical position after drawing the color slider row.</returns>
        private static float DrawColorSliderRowDirect(Rect containerRect, string labelText, ref Color color, float currentY, Action onColorChanged)
        {
            Rect labelRect = new Rect(containerRect.x, currentY, containerRect.width - 26f, 20f);
            Rect swatchRect = new Rect(containerRect.x + containerRect.width - 20f, currentY, 20f, 20f);

            Widgets.Label(labelRect, labelText);
            Widgets.DrawBoxSolid(swatchRect.ExpandedBy(1f), Color.black);
            Widgets.DrawBoxSolid(swatchRect, color);

            currentY += 22f;

            float sliderWidth = (containerRect.width / 3f) - 6f;
            Color oldColor = color;

            color.r = Widgets.HorizontalSlider(new Rect(containerRect.x, currentY, sliderWidth, 22f), color.r, 0f, 1f, false, label: $"R: {color.r:F2}");
            color.g = Widgets.HorizontalSlider(new Rect(containerRect.x + sliderWidth + 9f, currentY, sliderWidth, 22f), color.g, 0f, 1f, false, label: $"G: {color.g:F2}");
            color.b = Widgets.HorizontalSlider(new Rect(containerRect.x + (sliderWidth * 2f) + 18f, currentY, sliderWidth, 22f), color.b, 0f, 1f, false, label: $"B: {color.b:F2}");

            if (Mathf.Abs(color.r - oldColor.r) > 0.001f || Mathf.Abs(color.g - oldColor.g) > 0.001f || Mathf.Abs(color.b - oldColor.b) > 0.001f)
            {
                onColorChanged?.Invoke();
            }

            return currentY + 28f;
        }

        #endregion
    }
}
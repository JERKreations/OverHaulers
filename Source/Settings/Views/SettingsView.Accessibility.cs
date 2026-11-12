using System;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-10] Linear presentation layout for Tab 3 (Accessibility &amp; UI).
    /// Provides controls for Breakdown Icon Scaling, medical health status color presets,
    /// live 3-stop gradient previewing, and interactive RGB channel customization for medical and prosthetic colours.
    /// Resides under Source/Settings/Views/.
    /// </summary>
    public static partial class SettingsView
    {
        #region 1. CANONICAL TAB ENTRY POINT

        /// <summary>
        /// Renders the contents of the Accessibility tab, including icon scale, color palette presets, live gradient preview, and RGB
        ///  color customization sliders.
        /// </summary>
        /// <param name="ui">The settings layout context for positioning and rendering UI elements.</param>
        /// <param name="settings">The settings object containing the current accessibility and UI preferences.</param>
        public static void DrawAccessibilityTabContents(ref SettingsLayout ui, Settings settings)
        {
            // 1. Header with Reset Action
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_AccessibilityHeader", () =>
            {
                settings.ResetAccessibility();
                SettingsLayout.ClearInputBuffers();
                SettingsViewUtilities.OnSettingMutated();
            });

            // 2. Icon Scale Slider
            string scaleReadout = settings.iconScalePercent.ToString("P0");
            string scaleLabel = "OverHaulers_IconScaleLabel".Translate(scaleReadout).ToString();
            ui.Slider(
                scaleLabel,
                ref settings.iconScalePercent,
                PresentationDefaults.IconScalePercentMin,
                PresentationDefaults.IconScalePercentMax,
                step: 0.01f
            );
            ui.DescriptionKey("OverHaulers_IconScale_Desc");
            ui.Divider();

            // 3. Medical Palette Presets
            ui.SubHeader("OverHaulers_ColorPaletteHeader".Translate().ToString());
            ui.DescriptionKey("OverHaulers_Accessibility_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

            DrawPresetRadioMatrix(ref ui, settings);
            ui.Gap(SettingsViewUtilities.ControlGap);

            // 4. Live Health Gradient Preview Bar
            DrawLiveGradientPreview(ref ui, settings.colorCritical, settings.colorHealthy, settings.colorBoosted);
            ui.Divider();

            // 5. Interactive RGB Color Channel Sliders
            DrawColorCustomizerRow(ref ui, "OverHaulers_ColorCriticalLabel".Translate().ToString(), ref settings.colorCritical, settings);
            ui.Gap(SettingsViewUtilities.CompactGap);
            DrawColorCustomizerRow(ref ui, "OverHaulers_ColorHealthyLabel".Translate().ToString(), ref settings.colorHealthy, settings);
            ui.Gap(SettingsViewUtilities.CompactGap);
            DrawColorCustomizerRow(ref ui, "OverHaulers_ColorBoostedLabel".Translate().ToString(), ref settings.colorBoosted, settings);
            ui.Gap(SettingsViewUtilities.GroupGap);

            ui.Divider();
            ui.SubHeader("OverHaulers_ProstheticPaletteHeader".Translate().ToString());
            ui.DescriptionKey("OverHaulers_ProstheticPalette_Desc");
            ui.ToggleKey("OverHaulers_ProstheticPalette_Enable", ref settings.prostheticColorOverride);

            bool guiEnabled = GUI.enabled;
            try
            {
                GUI.enabled = guiEnabled && settings.prostheticColorOverride;
                ui.ToggleKey("OverHaulers_ProstheticPalette_Custom", ref settings.customProstheticPalette);
            }
            finally
            {
                GUI.enabled = guiEnabled;
            }

            Color critical = settings.customProstheticPalette ? settings.prostheticColorCritical : settings.colorCritical;
            Color healthy = settings.customProstheticPalette ? settings.prostheticColorHealthy : settings.colorHealthy;
            Color boosted = settings.customProstheticPalette ? settings.prostheticColorBoosted : settings.colorBoosted;
            DrawLiveGradientPreview(ref ui, critical, healthy, boosted, prosthetic: true);

            ui.Divider();
            ui.SubHeader("OverHaulers_ProstheticPalette_CustomHeader".Translate().ToString());
            try
            {
                GUI.enabled = guiEnabled && settings.prostheticColorOverride && settings.customProstheticPalette;
                if (ui.Button(SettingsViewUtilities.CachedText("OverHaulers_ProstheticPalette_ResetColors")))
                {
                    settings.ResetProstheticColorsToHealthPalette();
                }

                DrawColorCustomizerRow(ref ui, "OverHaulers_ProstheticColorCriticalLabel".Translate().ToString(),
                    ref settings.prostheticColorCritical, settings, medicalPalette: false);
                ui.Gap(SettingsViewUtilities.CompactGap);
                DrawColorCustomizerRow(ref ui, "OverHaulers_ProstheticColorHealthyLabel".Translate().ToString(),
                    ref settings.prostheticColorHealthy, settings, medicalPalette: false);
                ui.Gap(SettingsViewUtilities.CompactGap);
                DrawColorCustomizerRow(ref ui, "OverHaulers_ProstheticColorBoostedLabel".Translate().ToString(),
                    ref settings.prostheticColorBoosted, settings, medicalPalette: false);
            }
            finally
            {
                GUI.enabled = guiEnabled;
            }
            ui.Gap(SettingsViewUtilities.GroupGap);
        }

        #endregion

        #region 2. PRESET RADIO MATRIX

        /// <summary>
        /// Draws a matrix of radio buttons for selecting a color palette preset.
        /// </summary>
        /// <param name="ui">The settings layout context for positioning and rendering UI elements.</param>
        /// <param name="settings">The settings object containing the active palette preset.</param>
        private static void DrawPresetRadioMatrix(ref SettingsLayout ui, Settings settings)
        {
            float halfWidth = (ui.Width - SettingsViewUtilities.ControlGap) / 2f;

            // Row 1: Default & Deuteranopia
            Rect r1Col1 = new Rect(ui.X, ui.Y, halfWidth, SettingsLayout.DefaultToggleHeight);
            Rect r1Col2 = new Rect(ui.X + halfWidth + SettingsViewUtilities.ControlGap, ui.Y, halfWidth, SettingsLayout.DefaultToggleHeight);

            if (Widgets.RadioButtonLabeled(r1Col1, "OverHaulers_PresetDefault".Translate().ToString(), settings.activePalettePreset == PalettePreset.Default))
            {
                settings.ApplyPreset(PalettePreset.Default);
                SettingsLayout.ClearInputBuffers();
            }

            if (Widgets.RadioButtonLabeled(r1Col2, "OverHaulers_PresetDeuteranopia".Translate().ToString(), settings.activePalettePreset == PalettePreset.Deuteranopia))
            {
                settings.ApplyPreset(PalettePreset.Deuteranopia);
                SettingsLayout.ClearInputBuffers();
            }
            ui.Y += SettingsLayout.DefaultToggleHeight + SettingsViewUtilities.CompactGap;

            // Row 2: Tritanopia & High Contrast
            Rect r2Col1 = new Rect(ui.X, ui.Y, halfWidth, SettingsLayout.DefaultToggleHeight);
            Rect r2Col2 = new Rect(ui.X + halfWidth + SettingsViewUtilities.ControlGap, ui.Y, halfWidth, SettingsLayout.DefaultToggleHeight);

            if (Widgets.RadioButtonLabeled(r2Col1, "OverHaulers_PresetTritanopia".Translate().ToString(), settings.activePalettePreset == PalettePreset.Tritanopia))
            {
                settings.ApplyPreset(PalettePreset.Tritanopia);
                SettingsLayout.ClearInputBuffers();
            }

            if (Widgets.RadioButtonLabeled(r2Col2, "OverHaulers_PresetHighContrast".Translate().ToString(), settings.activePalettePreset == PalettePreset.HighContrast))
            {
                settings.ApplyPreset(PalettePreset.HighContrast);
                SettingsLayout.ClearInputBuffers();
            }
            ui.Y += SettingsLayout.DefaultToggleHeight + SettingsViewUtilities.CompactGap;

            // Row 3: Custom Palette Indicator
            Rect r3Col1 = new Rect(ui.X, ui.Y, halfWidth, SettingsLayout.DefaultToggleHeight);
            if (Widgets.RadioButtonLabeled(r3Col1, "OverHaulers_PresetCustom".Translate().ToString(), settings.activePalettePreset == PalettePreset.Custom))
            {
                settings.activePalettePreset = PalettePreset.Custom;
            }
            ui.Y += 30f;
        }

        #endregion

        #region 3. LIVE HEALTH STATUS GRADIENT PREVIEW

        /// <summary>
        /// Draws a live preview of the health status gradient, showing the transition from critical to healthy to boosted states.
        /// </summary>
        /// <param name="ui">The settings layout context for positioning and rendering UI elements.</param>
        /// <param name="critical">The color representing the critical health state.</param>
        /// <param name="healthy">The color representing the healthy state.</param>
        /// <param name="boosted">The color representing the boosted health state.</param>
        /// <param name="prosthetic">Use the prosthetic efficiency labels instead of the health labels.</param>
        private static void DrawLiveGradientPreview(ref SettingsLayout ui, Color critical, Color healthy, Color boosted, bool prosthetic = false)
        {
            // Header
            Text.Font = GameFont.Tiny;
            Color origColor = GUI.color;
            GUI.color = SettingsLayout.MutedTextColor;
            string headerKey = prosthetic ? "OverHaulers_ProstheticGradientPreviewHeader" : "OverHaulers_GradientPreviewHeader";
            Widgets.Label(new Rect(ui.X, ui.Y, ui.Width, 20f), headerKey.Translate().ToString());
            GUI.color = origColor;
            Text.Font = GameFont.Small;
            ui.Y += 22f;

            // 32-Segment LERP Gradient Bar
            float barHeight = 16f;
            Rect barRect = new Rect(ui.X, ui.Y, ui.Width, barHeight);

            Widgets.DrawBoxSolid(barRect.ExpandedBy(1f), Color.black);

            const int segments = 32;
            float segmentWidth = ui.Width / segments;
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / (segments - 1);
                Color segColor = t <= 0.5f
                    ? Color.Lerp(critical, healthy, t / 0.5f)
                    : Color.Lerp(healthy, boosted, (t - 0.5f) / 0.5f);

                Widgets.DrawBoxSolid(new Rect(barRect.x + (i * segmentWidth), barRect.y, segmentWidth + 0.5f, barHeight), segColor);
            }
            ui.Y += barHeight + 3f;

            // Percentage Legend Ticks
            Rect tickRect = new Rect(ui.X, ui.Y, ui.Width, 16f);
            Text.Font = GameFont.Tiny;
            GUI.color = SettingsLayout.MutedTextColor;

            Widgets.Label(tickRect.LeftPart(0.20f), prosthetic ? 0.5f.ToString("P0") : "0%");
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(tickRect, "100%");
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(tickRect.RightPart(0.20f), prosthetic ? 1.5f.ToString("P0") + "+" : "200%+");
            Text.Anchor = TextAnchor.UpperLeft;

            GUI.color = origColor;
            Text.Font = GameFont.Small;
            ui.Y += 20f;
        }

        #endregion

        #region 4. RGB COLOR CHANNEL CUSTOMIZATION ROWS

        /// <summary>
        /// Draws a row for customizing an RGB color with live preview and individual channel sliders.
        /// </summary>
        /// <param name="ui">The settings layout context for positioning and rendering UI elements.</param>
        /// <param name="label">The label describing the color being customized.</param>
        /// <param name="color">The color value to be customized.</param>
        /// <param name="settings">The settings object containing the active palette preset.</param>
        /// <param name="medicalPalette">Whether editing this colour selects the custom medical preset.</param>
        private static void DrawColorCustomizerRow(ref SettingsLayout ui, string label, ref Color color, Settings settings, bool medicalPalette = true)
        {
            // 1. Label and Live Color Swatch Box
            Rect labelRect = new Rect(ui.X, ui.Y, ui.Width - 36f, 22f);
            Rect swatchRect = new Rect(ui.X + ui.Width - 30f, ui.Y, 30f, 20f);

            Widgets.Label(labelRect, label);
            Widgets.DrawBoxSolid(swatchRect.ExpandedBy(1f), Color.black);
            Widgets.DrawBoxSolid(swatchRect, color);
            ui.Y += 24f;

            // 2. 3-Column RGB Sliders with Manual Entry Fields
            float gap = SettingsViewUtilities.ControlGap;
            float colWidth = (ui.Width - (gap * 2f)) / 3f;

            Rect rRect = new Rect(ui.X, ui.Y, colWidth, 26f);
            Rect gRect = new Rect(ui.X + colWidth + gap, ui.Y, colWidth, 26f);
            Rect bRect = new Rect(ui.X + (colWidth * 2f) + (gap * 2f), ui.Y, colWidth, 26f);

            bool rChanged = ui.DrawSliderInputUnit(rRect, "R:", ref color.r, 0f, 1f, step: 0.01f);
            bool gChanged = ui.DrawSliderInputUnit(gRect, "G:", ref color.g, 0f, 1f, step: 0.01f);
            bool bChanged = ui.DrawSliderInputUnit(bRect, "B:", ref color.b, 0f, 1f, step: 0.01f);

            if (rChanged || gChanged || bChanged)
            {
                if (medicalPalette) settings.activePalettePreset = PalettePreset.Custom;
                SettingsViewUtilities.OnSettingMutated();
            }

            ui.Y += 30f;
        }

        #endregion
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Stateless GUI utility drawer providing specialized direct IMGUI layout widgets, auto-height measurement engines,
    /// focus-aware text buffers, accessibility color controls, and menu section drawers for the Mod Settings interface.
    /// Operates on direct Rect cursor math with 0 Listing_Standard overhead and a Zero-Allocation Control ID Cache.
    /// Resides under Source/Presentation/.
    /// </summary>
    public static class SettingsViewUtilities
    {
        #region 1. LAYOUT METRIC CONSTANTS & CACHES

        public const float SectionPadding = 20f;
        public const float HeaderHeight = 38f;
        public const float ControlRowHeight = 28f;
        public const float GroupGap = 16f;

        public static readonly Color DescriptionTextColor = new Color(0.63f, 0.65f, 0.67f);
        public static readonly Color DarkTargetHighlightColor = new Color(0f, 0f, 0f, 0.40f);

        private static readonly Dictionary<string, string> activeInputBuffers = new Dictionary<string, string>(32);
        private static readonly Dictionary<string, float> sectionHeightCache = new Dictionary<string, float>(16);

        // BREAKPOINT ANCHOR: Deterministic Zero-Allocation Control ID Cache
        private static int currentControlIndex = 0;
        private static readonly List<string> cachedControlNames = new List<string>(128);

        public static void ResetControlIndex()
        {
            currentControlIndex = 0;
        }

        private static string GetNextControlName()
        {
            if (currentControlIndex >= cachedControlNames.Count)
            {
                cachedControlNames.Add("OH_Input_" + currentControlIndex);
            }
            return cachedControlNames[currentControlIndex++];
        }

        public static void ClearInputBuffers()
        {
            activeInputBuffers.Clear();
        }

        public static void ClearHeightCache()
        {
            sectionHeightCache.Clear();
        }

        public static float GetCachedSectionHeight(string key, float fallback = 120f)
        {
            if (sectionHeightCache.TryGetValue(key, out float h) && h > 20f)
            {
                return h;
            }
            return fallback;
        }

        public static void RecordSectionHeight(string key, float height)
        {
            sectionHeightCache[key] = height;
        }

        #endregion

        #region 2. CENTRALIZED CACHE INVALIDATION & MUTATION SINK

        /// <summary>
        /// Single centralized endpoint invoked whenever any slider, input box, or toggle mutates in settings.
        /// Flushes the TestBench dirty cache, clears precompiled topology layouts, and purges runtime pawn caches.
        /// </summary>
        public static void OnSettingMutated()
        {
            TestBench.MarkDirty();
            TopologyLayoutCompiler.InvalidateAllTopologies();
            PawnDataRegistry.ClearAllCaches();
        }

        #endregion

        #region 3. DYNAMIC TEXT & DIRECT LAYOUT HELPERS

        public static float CalcTextHeight(string text, float width, GameFont font = GameFont.Tiny)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f) return 0f;

            GameFont originalFont = Text.Font;
            Text.Font = font;
            try
            {
                return Text.CalcHeight(text, width);
            }
            finally
            {
                Text.Font = originalFont;
            }
        }

        public static float DrawSectionDescriptionDirect(Rect containerRect, string descriptionText, float currentY)
        {
            if (string.IsNullOrEmpty(descriptionText)) return currentY;

            float descH = CalcTextHeight(descriptionText, containerRect.width, GameFont.Tiny);
            Rect descRect = new Rect(containerRect.x, currentY, containerRect.width, descH);

            Text.Font = GameFont.Tiny;
            Color originalColor = GUI.color;
            try
            {
                GUI.color = DescriptionTextColor;
                Widgets.Label(descRect, descriptionText);
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = GameFont.Small;
            }

            return currentY + descH + 8f;
        }

        #endregion

        #region 4. DIRECT SECTION BOX ENGINE

        public static Rect BeginSectionBoxDirect(Rect viewRect, string sectionKey, ref float currentY, float fallbackEstimate = 120f)
        {
            float boxHeight = GetCachedSectionHeight(sectionKey, fallbackEstimate);
            Rect boxRect = new Rect(0f, currentY, viewRect.width, boxHeight);

            Widgets.DrawMenuSection(boxRect);

            return boxRect.ContractedBy(10f);
        }

        public static void EndSectionBoxDirect(string sectionKey, float startY, float endY, ref float currentY, float bottomGap = 15f)
        {
            float actualHeight = (endY - startY) + 20f; 
            RecordSectionHeight(sectionKey, actualHeight);
            currentY += actualHeight + bottomGap;
        }

        #endregion

        #region 5. DIRECT HEADER & GRID BUILDERS

        public static bool DrawHeaderWithResetDirect(
            Rect containerRect,
            string headerLabelKey,
            Action resetAction,
            float currentY,
            out float nextY)
        {
            Rect rect = new Rect(containerRect.x, currentY, containerRect.width, 30f);
            Rect headerRect = rect.LeftPart(0.74f);
            Rect resetRect = rect.RightPart(0.22f);

            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect, headerLabelKey.Translate().ToString());
            Text.Font = GameFont.Small;

            bool isHovered = Mouse.IsOver(resetRect);

            if (Widgets.ButtonText(resetRect, "OverHaulers_Reset".Translate().ToString()))
            {
                resetAction?.Invoke();
                ClearInputBuffers();
                ClearHeightCache();
                OnSettingMutated();
            }

            nextY = currentY + 38f;
            return isHovered;
        }

        public static float DrawCheckboxRowDirect(
            Rect containerRect, 
            string label, 
            ref bool value, 
            float currentY, 
            string tooltip = null, 
            bool highlight = false)
        {
            Rect rowRect = new Rect(containerRect.x, currentY, containerRect.width, 24f);

            if (highlight)
            {
                Widgets.DrawBoxSolid(rowRect.ExpandedBy(2f, 1f), DarkTargetHighlightColor);
            }

            bool oldVal = value;
            Widgets.CheckboxLabeled(rowRect, label, ref value);

            if (oldVal != value)
            {
                OnSettingMutated();
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rowRect, tooltip);
            }

            return currentY + 28f;
        }

        public static float Draw2x2CheckboxGridRowDirect(
            Rect containerRect,
            string leftLabel, ref bool leftValue, string leftTooltip,
            string rightLabel, ref bool rightValue, string rightTooltip,
            float currentY,
            bool highlight = false)
        {
            Rect rowRect = new Rect(containerRect.x, currentY, containerRect.width, 24f);

            if (highlight)
            {
                Widgets.DrawBoxSolid(rowRect.ExpandedBy(2f, 1f), DarkTargetHighlightColor);
            }

            float halfWidth = (rowRect.width / 2f) - 8f;
            Rect leftRect = new Rect(rowRect.x, rowRect.y, halfWidth, rowRect.height);
            Rect rightRect = new Rect(rowRect.x + halfWidth + 16f, rowRect.y, halfWidth, rowRect.height);

            bool oldLeft = leftValue;
            Widgets.CheckboxLabeled(leftRect, leftLabel, ref leftValue);
            if (oldLeft != leftValue) OnSettingMutated();
            if (!string.IsNullOrEmpty(leftTooltip)) TooltipHandler.TipRegion(leftRect, leftTooltip);

            bool oldRight = rightValue;
            Widgets.CheckboxLabeled(rightRect, rightLabel, ref rightValue);
            if (oldRight != rightValue) OnSettingMutated();
            if (!string.IsNullOrEmpty(rightTooltip)) TooltipHandler.TipRegion(rightRect, rightTooltip);

            return currentY + 28f;
        }

        #endregion

        #region 6. ROW LAYOUT ENGINE

        private static bool CalculateRowLayout(
            Rect rowRect, 
            string labelText, 
            out Rect labelRect, 
            out Rect sliderRect, 
            out Rect textRect,
            out bool isStacked)
        {
            float inputWidth = 44f;
            float gap = 6f;
            float rightPadding = 2f;

            float effectiveWidth = rowRect.width - rightPadding;
            float sliderWidth = effectiveWidth > 500f ? 200f : 100f;
            float availableLabelWidth = effectiveWidth - inputWidth - sliderWidth - (gap * 2f);

            float requiredTextWidth = Text.CalcSize(labelText).x + 4f;
            isStacked = requiredTextWidth > availableLabelWidth;

            if (isStacked)
            {
                labelRect = new Rect(rowRect.x, rowRect.y, effectiveWidth, 20f);
                sliderRect = new Rect(rowRect.x, rowRect.y + 22f, effectiveWidth - inputWidth - gap, 22f);
                textRect = new Rect(rowRect.x + effectiveWidth - inputWidth, rowRect.y + 22f, inputWidth, 22f);
                return true;
            }

            textRect = new Rect(rowRect.x + effectiveWidth - inputWidth, rowRect.y, inputWidth, 22f);
            sliderRect = new Rect(textRect.x - gap - sliderWidth, rowRect.y, sliderWidth, 22f);
            labelRect = new Rect(rowRect.x, rowRect.y, sliderRect.x - rowRect.x - gap, 22f);
            return false;
        }

        #endregion

        #region 7. [CORE ATOMIC SLIDER ENGINE] - SINGLE SOURCE OF TRUTH

        /// <summary>
        /// The single underlying atomic slider and text-input engine for the entire mod settings interface.
        /// Handles slider dragging, focus-aware text buffering, text parsing, bounds clamping, and centralized cache invalidation.
        /// </summary>
        private static bool DrawCoreSliderWithInputDirect(
            Rect sliderRect, 
            Rect textRect, 
            ref float value, 
            float min, 
            float max, 
            bool isInteger)
        {
            float originalValue = value;
            bool valueChanged = false;

            // 1. Horizontal Slider
            float oldVal = value;
            float sliderVal = Widgets.HorizontalSlider(sliderRect, value, min, max, true);
            if (Mathf.Abs(sliderVal - oldVal) > 0.0001f)
            {
                value = isInteger ? Mathf.Round(sliderVal) : (float)Math.Round(sliderVal, 2);
                valueChanged = true;
            }

            // 2. Focus-Aware Text Input Sync
            string controlName = GetNextControlName();
            GUI.SetNextControlName(controlName);
            bool isFocused = GUI.GetNameOfFocusedControl() == controlName;

            if (!activeInputBuffers.TryGetValue(controlName, out string currentBuffer) || !isFocused)
            {
                currentBuffer = isInteger ? Mathf.RoundToInt(value).ToString() : value.ToString("F2");
                activeInputBuffers[controlName] = currentBuffer;
            }

            string newBuffer = Widgets.TextField(textRect, currentBuffer);

            if (newBuffer != currentBuffer)
            {
                activeInputBuffers[controlName] = newBuffer;

                if (float.TryParse(newBuffer, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    float clamped = Mathf.Clamp(parsed, min, max);
                    float parsedValue = isInteger ? Mathf.Round(clamped) : (float)Math.Round(clamped, 2);
                    if (Mathf.Abs(parsedValue - value) > 0.0001f)
                    {
                        value = parsedValue;
                        valueChanged = true;
                    }
                }
            }

            // 3. Centralized Invalidation Hook
            if (valueChanged && Mathf.Abs(originalValue - value) > 0.0001f)
            {
                OnSettingMutated();
                return true;
            }

            return false;
        }

        #endregion

        #region 8. PUBLIC SLIDER ROW DRAWERS

        public static float DrawSettingRowDirect(
            Rect containerRect, 
            string label, 
            ref float value, 
            float min, 
            float max, 
            float currentY, 
            string tooltip = null, 
            bool highlight = false)
        {
            Rect baseRect = new Rect(containerRect.x, currentY, containerRect.width, 24f);

            if (highlight)
            {
                Widgets.DrawBoxSolid(baseRect.ExpandedBy(2f, 1f), DarkTargetHighlightColor);
            }

            DrawSliderAndInputRowDirect(baseRect, label, ref value, min, max, isInteger: false, tooltip: tooltip);
            return currentY + 28f;
        }

        public static float DrawSettingRowWithDescriptionDirect(
            Rect containerRect, 
            string label, 
            ref float value, 
            float min, 
            float max, 
            string description, 
            float currentY, 
            string tooltip = null, 
            bool highlight = false)
        {
            currentY = DrawSettingRowDirect(containerRect, label, ref value, min, max, currentY, tooltip, highlight);

            float descH = CalcTextHeight(description, containerRect.width, GameFont.Tiny);
            Rect descRect = new Rect(containerRect.x, currentY, containerRect.width, descH);

            Text.Font = GameFont.Tiny;
            Color originalColor = GUI.color;
            try
            {
                GUI.color = DescriptionTextColor;
                Widgets.Label(descRect, description);
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = GameFont.Small;
            }

            return currentY + descH + 6f;
        }

        public static float DrawToggleSettingRowWithDescriptionDirect(
            Rect containerRect,
            ref bool toggleValue,
            string headingLabel,
            ref float sliderValue,
            float min,
            float max,
            string description,
            float currentY,
            string tooltip = null,
            bool highlight = false,
            string customValueLabel = null)
        {
            Rect baseRect = new Rect(containerRect.x, currentY, containerRect.width, 24f);

            if (highlight)
            {
                Widgets.DrawBoxSolid(baseRect.ExpandedBy(2f, 1f), DarkTargetHighlightColor);
            }

            string fullCheckboxLabel = customValueLabel != null 
                ? $"{headingLabel} ({customValueLabel})"
                : $"{headingLabel} ({sliderValue * 100f:F0}%)";

            CalculateRowLayout(baseRect, fullCheckboxLabel, out Rect labelRect, out Rect sliderRect, out Rect textRect, out bool isStacked);

            if (isStacked)
            {
                baseRect = new Rect(containerRect.x, currentY, containerRect.width, 44f);
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(baseRect, tooltip);
            }

            bool oldToggle = toggleValue;
            Widgets.CheckboxLabeled(labelRect, fullCheckboxLabel, ref toggleValue);
            if (oldToggle != toggleValue)
            {
                OnSettingMutated();
            }

            bool originalEnabledState = GUI.enabled;
            try
            {
                GUI.enabled = originalEnabledState && toggleValue;

                // Funneled into atomic core slider engine
                DrawCoreSliderWithInputDirect(sliderRect, textRect, ref sliderValue, min, max, isInteger: false);
            }
            finally
            {
                GUI.enabled = originalEnabledState;
            }

            currentY += isStacked ? 48f : 28f;

            float descH = CalcTextHeight(description, containerRect.width, GameFont.Tiny);
            Rect descRect = new Rect(containerRect.x, currentY, containerRect.width, descH);

            Text.Font = GameFont.Tiny;
            Color originalColor = GUI.color;
            try
            {
                GUI.color = DescriptionTextColor;
                Widgets.Label(descRect, description);
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = GameFont.Small;
            }

            return currentY + descH + 8f;
        }

        public static void DrawSliderAndInputRowDirect(
            Rect baseRect, 
            string label, 
            ref int value, 
            int min, 
            int max, 
            string tooltip = null)
        {
            float floatVal = value;
            DrawSliderAndInputRowDirect(baseRect, label, ref floatVal, min, max, isInteger: true, tooltip: tooltip);
            value = Mathf.RoundToInt(floatVal);
        }

        public static void DrawSliderAndInputRowDirect(
            Rect baseRect, 
            string label, 
            ref float value, 
            float min, 
            float max, 
            bool isInteger,
            string tooltip = null)
        {
            CalculateRowLayout(baseRect, label, out Rect labelRect, out Rect sliderRect, out Rect textRect, out _);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(baseRect, tooltip);
            }

            Widgets.Label(labelRect, label);

            // Funneled into atomic core slider engine
            DrawCoreSliderWithInputDirect(sliderRect, textRect, ref value, min, max, isInteger);
        }

        #endregion
    }
}
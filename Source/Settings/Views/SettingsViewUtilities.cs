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

        /// <summary>
        /// Resets the internal control index, preparing the zero-allocation control ID cache for a new GUI frame.
        /// </summary>
        public static void ResetControlIndex()
        {
            currentControlIndex = 0;
        }

        /// <summary>
        /// Retrieves the next unique control name from the zero-allocation control ID cache.
        /// </summary>
        /// <returns>The next unique control name as a string.</returns>
        private static string GetNextControlName()
        {
            if (currentControlIndex >= cachedControlNames.Count)
            {
                cachedControlNames.Add("OH_Input_" + currentControlIndex);
            }
            return cachedControlNames[currentControlIndex++];
        }

        /// <summary>
        /// Clears all active input buffers, effectively resetting any text input states.
        /// </summary>
        public static void ClearInputBuffers()
        {
            activeInputBuffers.Clear();
        }

        /// <summary>
        /// Clears the cached section heights, forcing recalculation on the next layout pass.
        /// </summary>
        public static void ClearHeightCache()
        {
            sectionHeightCache.Clear();
        }

        /// <summary>
        /// Retrieves the cached height for a given section, or returns a fallback value if not available or invalid.
        /// </summary>
        /// <param name="key">The unique key identifying the section.</param>
        /// <param name="fallback">The fallback height to use if no valid cached height exists.</param>
        /// <returns>The cached section height or the fallback value.</returns>
        public static float GetCachedSectionHeight(string key, float fallback = 120f)
        {
            if (sectionHeightCache.TryGetValue(key, out float h) && h > 20f)
            {
                return h;
            }
            return fallback;
        }

        /// <summary>
        /// Records the height of a section in the cache.
        /// </summary>
        /// <param name="key">The unique key identifying the section.</param>
        /// <param name="height">The height to record for the section.</param>
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
        /// <remarks>
        /// This method should be called whenever any setting is changed to ensure that all dependent systems are updated accordingly.
        /// </remarks>
        public static void OnSettingMutated()
        {
            TestBench.MarkDirty();
            TopologyLayoutCompiler.InvalidateAllTopologies();
            PawnDataRegistry.ClearAllCaches();
            PerformanceTelemetry.SyncSettingsState();
        }

        #endregion

        #region 3. DYNAMIC TEXT & DIRECT LAYOUT HELPERS

        /// <summary>
        /// Calculates the height required to render the specified text within the given width and font.
        /// </summary>
        /// <param name="text">The text to measure.</param>
        /// <param name="width">The available width for rendering the text.</param>
        /// <param name="font">The font to use for rendering the text.</param>
        /// <returns>The height required to render the text within the specified width and font.</returns>
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

        /// <summary>
        /// Draws a section description directly within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area to draw the description within.</param>
        /// <param name="descriptionText">The description text to render.</param>
        /// <param name="currentY">The current Y position within the container, updated as the description is drawn.</param>
        /// <returns>The updated Y position after drawing the description.</returns>
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

        /// <summary>
        /// Begins a section box directly within the specified view rectangle, using a cached height if available.
        /// </summary>
        /// <param name="viewRect">The rectangle defining the area of the view.</param>
        /// <param name="sectionKey">The unique key identifying the section.</param>
        /// <param name="currentY">The current Y position within the view, updated as the section is drawn.</param>
        /// <param name="fallbackEstimate">The fallback height estimate to use if no cached height is available.</param>
        /// <returns>The rectangle representing the section box, contracted by a margin for content placement.</returns>
        public static Rect BeginSectionBoxDirect(Rect viewRect, string sectionKey, ref float currentY, float fallbackEstimate = 120f)
        {
            float boxHeight = GetCachedSectionHeight(sectionKey, fallbackEstimate);
            Rect boxRect = new Rect(0f, currentY, viewRect.width, boxHeight);

            Widgets.DrawMenuSection(boxRect);

            return boxRect.ContractedBy(10f);
        }

        /// <summary>
        /// Ends a section box that was begun with BeginSectionBoxDirect, recording its actual height and updating the current Y position.
        /// </summary>
        /// <param name="sectionKey">The unique key identifying the section.</param>
        /// <param name="startY">The starting Y position of the section box.</param>
        /// <param name="endY">The ending Y position of the section box.</param>
        /// <param name="currentY">The current Y position within the view, updated as the section is closed.</param>
        /// <param name="bottomGap">The gap to add below the section box.</param>
        public static void EndSectionBoxDirect(string sectionKey, float startY, float endY, ref float currentY, float bottomGap = 15f)
        {
            float actualHeight = (endY - startY) + 20f; 
            RecordSectionHeight(sectionKey, actualHeight);
            currentY += actualHeight + bottomGap;
        }

        #endregion

        #region 5. DIRECT HEADER & GRID BUILDERS

        /// <summary>
        /// Draws a header with an associated reset button directly within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area of the container.</param>
        /// <param name="headerLabelKey">The translation key for the header label.</param>
        /// <param name="resetAction">The action to invoke when the reset button is clicked.</param>
        /// <param name="currentY">The current Y position within the container.</param>
        /// <param name="nextY">The updated Y position after drawing the header and reset button.</param>
        /// <returns>Indicates whether the reset button is currently hovered.</returns>
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

        /// <summary>
        /// Draws a single row containing a checkbox with an optional tooltip and highlighting.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area of the container.</param>
        /// <param name="label">The label for the checkbox.</param>
        /// <param name="value">The current value of the checkbox.</param>
        /// <param name="currentY">The current Y position within the container.</param>
        /// <param name="tooltip">The tooltip text to display when hovering over the row.</param>
        /// <param name="highlight">Indicates whether the row should be highlighted.</param>
        /// <returns>The updated Y position after drawing the row.</returns>
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

        /// <summary>
        /// Draws a single row containing two checkboxes side by side, each with an optional tooltip and highlighting.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area of the container.</param>
        /// <param name="leftLabel">The label for the left checkbox.</param>
        /// <param name="leftValue">The current value of the left checkbox.</param>
        /// <param name="leftTooltip">The tooltip text for the left checkbox.</param>
        /// <param name="rightLabel">The label for the right checkbox.</param>
        /// <param name="rightValue">The current value of the right checkbox.</param>
        /// <param name="rightTooltip">The tooltip text for the right checkbox.</param>
        /// <param name="currentY">The current Y position within the container.</param>
        /// <param name="highlight">Indicates whether the row should be highlighted.</param>
        /// <returns>The updated Y position after drawing the row.</returns>
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

        /// <summary>
        /// Draws a single row containing a checkbox with an in-line description below it and optional highlighting.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area of the container.</param>
        /// <param name="label">The label for the checkbox.</param>
        /// <param name="value">The current value of the checkbox.</param>
        /// <param name="description">The in-line description text rendered beneath the checkbox.</param>
        /// <param name="currentY">The current Y position within the container.</param>
        /// <param name="tooltip">An optional tooltip for the checkbox row.</param>
        /// <param name="highlight">Indicates whether the row should be highlighted.</param>
        /// <returns>The updated Y position after drawing the row and description.</returns>
        public static float DrawCheckboxRowWithDescriptionDirect(
            Rect containerRect,
            string label,
            ref bool value,
            string description,
            float currentY,
            string tooltip = null,
            bool highlight = false)
        {
            currentY = DrawCheckboxRowDirect(containerRect, label, ref value, currentY, tooltip, highlight);

            if (string.IsNullOrEmpty(description)) return currentY;

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

        #endregion

        #region 6. ROW LAYOUT ENGINE

        /// <summary>
        /// Calculates the layout for a single row containing a label, a slider, and a text input field.
        /// </summary>
        /// <param name="rowRect">The rectangle defining the area of the row.</param>
        /// <param name="labelText">The text for the label.</param>
        /// <param name="labelRect">The calculated rectangle for the label.</param>
        /// <param name="sliderRect">The calculated rectangle for the slider.</param>
        /// <param name="textRect">The calculated rectangle for the text input field.</param>
        /// <param name="isStacked">Indicates whether the row layout is stacked vertically.</param>
        /// <returns>True if the row is stacked, false otherwise.</returns>
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
        /// <param name="sliderRect">The rectangle defining the area of the slider.</param>
        /// <param name="textRect">The rectangle defining the area of the text input field.</param>
        /// <param name="value">The current value of the slider and text input.</param>
        /// <param name="min">The minimum allowed value.</param>
        /// <param name="max">The maximum allowed value.</param>
        /// <param name="isInteger">Indicates whether the value should be treated as an integer.</param>
        /// <returns>True if the value was changed, false otherwise.</returns>
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

        /// <summary>
        /// Draws a single setting row with a slider and an optional text input field.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area of the container.</param>
        /// <param name="label">The label text for the setting row.</param>
        /// <param name="value">The current value of the setting.</param>
        /// <param name="min">The minimum allowed value.</param>
        /// <param name="max">The maximum allowed value.</param>
        /// <param name="currentY">The current Y position within the container.</param>
        /// <param name="tooltip">An optional tooltip for the setting row.</param>
        /// <param name="highlight">Indicates whether the row should be highlighted.</param>
        /// <returns>The updated Y position after drawing the row.</returns>
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

        /// <summary>
        /// Draws a single setting row with a slider, an optional text input field, and a description below it.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area of the container.</param>
        /// <param name="label">The label text for the setting row.</param>
        /// <param name="value">The current value of the setting.</param>
        /// <param name="min">The minimum allowed value.</param>
        /// <param name="max">The maximum allowed value.</param>
        /// <param name="description">The description text to display below the setting row.</param>
        /// <param name="currentY">The current Y position within the container.</param>
        /// <param name="tooltip">An optional tooltip for the setting row.</param>
        /// <param name="highlight">Indicates whether the row should be highlighted.</param>
        /// <returns>The updated Y position after drawing the row and its description.</returns>
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

        /// <summary>
        /// Draws a single setting row with a toggle, a slider, an optional text input field, and a description below it.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the area of the container.</param>
        /// <param name="toggleValue">The current value of the toggle.</param>
        /// <param name="headingLabel">The label text for the setting row.</param>
        /// <param name="sliderValue">The current value of the slider.</param>
        /// <param name="min">The minimum allowed value for the slider.</param>
        /// <param name="max">The maximum allowed value for the slider.</param>
        /// <param name="description">The description text to display below the setting row.</param>
        /// <param name="currentY">The current Y position within the container.</param>
        /// <param name="tooltip">An optional tooltip for the setting row.</param>
        /// <param name="highlight">Indicates whether the row should be highlighted.</param>
        /// <param name="customValueLabel">An optional custom label for the slider value.</param>
        /// <returns>The updated Y position after drawing the row and its description.</returns>
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

        /// <summary>
        /// Draws a single setting row with a slider and an optional text input field.
        /// </summary>
        /// <param name="baseRect">The rectangle defining the area of the row.</param>
        /// <param name="label">The label text for the slider row.</param>
        /// <param name="value">The current value of the slider.</param>
        /// <param name="min">The minimum allowed value for the slider.</param>
        /// <param name="max">The maximum allowed value for the slider.</param>
        /// <param name="tooltip">An optional tooltip for the slider row.</param>
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

        /// <summary>
        /// Draws a single setting row with a slider and an optional text input field.
        /// </summary>
        /// <param name="baseRect">The rectangle defining the area of the row.</param>
        /// <param name="label">The label text for the slider row.</param>
        /// <param name="value">The current value of the slider.</param>
        /// <param name="min">The minimum allowed value for the slider.</param>
        /// <param name="max">The maximum allowed value for the slider.</param>
        /// <param name="isInteger">Whether the slider should only allow integer values.</param>
        /// <param name="tooltip">An optional tooltip for the slider row.</param>
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
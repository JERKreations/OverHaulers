using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-00] Immediate mode layout cursor for Mod Settings, using value-type geometry and reusable input buffers.
    /// Operates on top-down linear Rect progression with automatic text measurement, width-dependent sliders,
    /// focus-aware manual entry text fields, and zero Listing_Standard overhead.
    /// Resides under Source/Settings/Views/.
    /// </summary>
    public struct SettingsLayout
    {
        #region 1. CONSTANTS & STYLES

        public const float DefaultRowHeight = 26f;
        public const float DefaultToggleHeight = 24f;
        public const float DefaultButtonHeight = 28f;
        public const float DefaultGap = 12f;

        public const float FixedSliderWidth = 300f;
        public const float FixedInputWidth = 46f;
        public const float SliderTagWidth = 24f;
        public const float ControlGap = SettingsViewUtilities.ControlGap;

        public static readonly Color MutedTextColor = new Color(0.63f, 0.65f, 0.67f);
        public static readonly Color DividerLineColor = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color CardOutlineColor = new Color(1f, 1f, 1f, 0.08f);

        #endregion

        #region 2. STATIC CONTROL ID & TEXT BUFFER CACHE

        private const string DefaultScopeKey = "Settings";

        private sealed class InputScopeState
        {
            public int ControlIndex;
            public readonly List<string> CachedControlNames = new List<string>(64);
            public readonly Dictionary<string, string> ActiveInputBuffers = new Dictionary<string, string>(64);
            private readonly string prefix;

            public InputScopeState(string prefix)
            {
                this.prefix = prefix;
            }

            public string GetNextControlName()
            {
                if (ControlIndex >= CachedControlNames.Count)
                {
                    CachedControlNames.Add(prefix + ControlIndex);
                }
                return CachedControlNames[ControlIndex++];
            }
        }

        private static readonly Dictionary<string, InputScopeState> inputScopes = new Dictionary<string, InputScopeState>(4);
        private static InputScopeState activeScope = GetOrCreateScope(DefaultScopeKey);

        private static InputScopeState GetOrCreateScope(string scopeKey)
        {
            string key = string.IsNullOrEmpty(scopeKey) ? DefaultScopeKey : scopeKey;
            if (!inputScopes.TryGetValue(key, out InputScopeState scope))
            {
                scope = new InputScopeState("OH_" + key + "_Input_");
                inputScopes.Add(key, scope);
            }
            return scope;
        }

        /// <summary>
        /// Resets the control index at the start of each frame for the specified UI window scope,
        /// isolating IMGUI control names and input buffers when multiple dialogs render concurrently.
        /// </summary>
        /// <param name="scopeKey">Optional window scope identifier; defaults to <see cref="DefaultScopeKey"/>.</param>
        public static void ResetControlIndex(string scopeKey = DefaultScopeKey)
        {
            activeScope = GetOrCreateScope(scopeKey);
            activeScope.ControlIndex = 0;
        }

        /// <summary>
        /// Clears cached manual input text buffers (e.g. on settings reset or tab switch).
        /// </summary>
        /// <param name="scopeKey">Optional scope to clear; when null, clears all scopes.</param>
        public static void ClearInputBuffers(string scopeKey = null)
        {
            if (string.IsNullOrEmpty(scopeKey))
            {
                foreach (var kvp in inputScopes)
                {
                    kvp.Value.ActiveInputBuffers.Clear();
                }
            }
            else if (inputScopes.TryGetValue(scopeKey, out InputScopeState scope))
            {
                scope.ActiveInputBuffers.Clear();
            }
        }

        #endregion

        #region 3. FIELDS & CONSTRUCTOR

        public float X;
        public float Y;
        public float Width;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsLayout"/> struct with the specified bounds.
        /// </summary>
        /// <param name="bounds">The rectangle defining the layout bounds.</param>
        public SettingsLayout(Rect bounds)
        {
            X = bounds.x + 2f;
            Y = bounds.y;
            Width = bounds.width - 6f;
        }

        #endregion

        #region 4. HEADERS & TYPOGRAPHY

        /// <summary>
        /// Renders a header row with the specified text.
        /// </summary>
        /// <param name="label">The header text to render.</param>
        public void Header(string label)
        {
            float height = Math.Max(30f, SettingsViewUtilities.CalcTextHeight(label, Width, GameFont.Medium));
            Text.Font = GameFont.Medium;
            Rect rect = new Rect(X, Y, Width, height);
            Widgets.Label(rect, label);
            Text.Font = GameFont.Small;
            Y += height + 4f;
        }

        /// <summary>
        /// Renders a header row using a translation key to fetch the localized text.
        /// </summary>
        /// <param name="translationKey">The translation key for the localized header text.</param>
        public void HeaderKey(string translationKey)
        {
            Header(SettingsViewUtilities.CachedText(translationKey));
        }

        /// <summary>
        /// Renders a medium section header with an optional right-aligned reset button that clears
        /// the specified input scope and triggers downstream mutation propagation.
        /// </summary>
        public void HeaderWithResetKey(
            string headerKey,
            Action resetAction,
            SettingMutationScope? mutationScope = null,
            string inputScopeKey = DefaultScopeKey)
        {
            string headerText = SettingsViewUtilities.CachedText(headerKey);
            string resetLabel = SettingsViewUtilities.CachedText("OverHaulers_Reset");
            Vector2 resetSize = resetAction != null
                ? SettingsViewUtilities.ButtonSize(resetLabel, Width, minWidth: 75f, minHeight: 24f)
                : Vector2.zero;

            Text.Font = GameFont.Medium;
            bool separateReset = resetAction != null &&
                SettingsViewUtilities.MeasuredTextWidth(headerText) + resetSize.x + 5f > Width;
            float headerWidth = resetAction != null && !separateReset ? Width - resetSize.x - 5f : Width;
            float headerHeight = Math.Max(30f, SettingsViewUtilities.MeasuredTextHeight(headerText, headerWidth));
            Widgets.Label(new Rect(X, Y, headerWidth, headerHeight), headerText);
            Text.Font = GameFont.Small;

            float bottom = Y + headerHeight;
            if (resetAction != null)
            {
                float resetY = separateReset ? bottom + 4f : Y + 2f;
                Rect resetBtnRect = new Rect(X + Width - resetSize.x, resetY, resetSize.x, resetSize.y);
                if (SettingsViewUtilities.DrawButton(resetBtnRect, resetLabel))
                {
                    resetAction();
                    ClearInputBuffers(inputScopeKey);
                    SettingsViewUtilities.OnSettingMutated(mutationScope);
                }
                bottom = Math.Max(bottom, resetBtnRect.yMax);
            }

            Y = bottom + 4f;
        }

        /// <summary>
        /// Renders a subheader row with the specified text.
        /// </summary>
        /// <param name="label">The subheader text to render.</param>
        public void SubHeader(string label)
        {
            float height = Math.Max(22f, SettingsViewUtilities.CalcTextHeight(label, Width, GameFont.Small));
            Text.Font = GameFont.Small;
            Rect rect = new Rect(X, Y, Width, height);
            Widgets.Label(rect, label);
            Y += height + 2f;
        }

        /// <summary>
        /// Renders a description row with the specified text.
        /// </summary>
        /// <param name="description">The description text to render.</param>
        public void Description(string description)
        {
            if (string.IsNullOrEmpty(description)) return;

            float textHeight = SettingsViewUtilities.CalcTextHeight(description, Width, GameFont.Tiny);
            if (textHeight <= 0f) return;

            Rect rect = new Rect(X, Y, Width, textHeight);
            Text.Font = GameFont.Tiny;
            Color origColor = GUI.color;
            try
            {
                GUI.color = MutedTextColor;
                Widgets.Label(rect, description);
            }
            finally
            {
                GUI.color = origColor;
                Text.Font = GameFont.Small;
            }

            Y += textHeight + 6f;
        }

        /// <summary>
        /// Renders a description row using a translation key to fetch the localized text.
        /// </summary>
        /// <param name="translationKey">The translation key for the localized description text.</param>
        public void DescriptionKey(string translationKey)
        {
            Description(SettingsViewUtilities.CachedText(translationKey));
        }

        #endregion

        #region 5. DIVIDERS & SPACING

        /// <summary>
        /// Draws a horizontal divider line with optional spacing before and after.
        /// </summary>
        /// <param name="gapBefore">The vertical space before the divider.</param>
        /// <param name="gapAfter">The vertical space after the divider.</param>
        public void Divider(float gapBefore = 8f, float gapAfter = 12f)
        {
            Y += gapBefore;
            Color origColor = GUI.color;
            try
            {
                GUI.color = DividerLineColor;
                Widgets.DrawLineHorizontal(X, Y, Width);
            }
            finally
            {
                GUI.color = origColor;
            }
            Y += gapAfter;
        }

        /// <summary>
        /// Adds vertical spacing between UI elements.
        /// </summary>
        /// <param name="pixels">The number of pixels to add as vertical space.</param>
        public void Gap(float pixels = DefaultGap)
        {
            Y += pixels;
        }

        #endregion

        #region 6. TOGGLES & CHECKBOXES

        /// <summary>
        /// Renders a toggle row with a label and checkbox.
        /// </summary>
        /// <param name="label">The label for the toggle row.</param>
        /// <param name="value">The current state of the toggle checkbox.</param>
        /// <param name="tooltip">Optional tooltip for the row.</param>
        /// <param name="mutationScope">The scope of the setting mutation.</param>
        /// <returns>True if the toggle value changed, false otherwise.</returns>
        public bool Toggle(
            string label,
            ref bool value,
            string tooltip = null,
            SettingMutationScope? mutationScope = null)
        {
            string displayLabel = SettingsViewUtilities.LabelWithTooltip(label, tooltip);
            float labelWidth = Math.Max(1f, Width - 30f);
            float rowHeight = Math.Max(DefaultToggleHeight, SettingsViewUtilities.CalcTextHeight(displayLabel, labelWidth, GameFont.Small));

            Rect rowRect = new Rect(X, Y, Width, rowHeight);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rowRect, tooltip);
            }

            bool prev = value;
            Widgets.CheckboxLabeled(rowRect, displayLabel, ref value);

            Y += rowHeight + SettingsViewUtilities.CompactGap;

            if (prev != value)
            {
                SettingsViewUtilities.OnSettingMutated(mutationScope);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Renders a toggle row using localized text keys for the label and tooltip.
        /// </summary>
        /// <param name="labelKey">The localization key for the label.</param>
        /// <param name="value">The current state of the toggle checkbox.</param>
        /// <param name="tooltipKey">The localization key for the tooltip.</param>
        /// <param name="mutationScope">The scope of the setting mutation.</param>
        /// <returns>True if the toggle value changed, false otherwise.</returns>
        public bool ToggleKey(
            string labelKey,
            ref bool value,
            string tooltipKey = null,
            SettingMutationScope? mutationScope = null)
        {
            string label = SettingsViewUtilities.CachedText(labelKey);
            string tooltip = !string.IsNullOrEmpty(tooltipKey) ? SettingsViewUtilities.CachedText(tooltipKey) : null;
            return Toggle(label, ref value, tooltip, mutationScope);
        }

        #endregion

        #region 7. SLIDERS WITH FIXED BARS & MANUAL ENTRY FIELDS

        /// <summary>
        /// Renders a slider row with a width-dependent slider bar and a manual entry text box on the right.
        /// Defaults to fine 1% (0.01f) tuning increments.
        /// </summary>
        /// <param name="label">The label for the slider row.</param>
        /// <param name="value">The current value of the slider.</param>
        /// <param name="min">The minimum allowed value for the slider.</param>
        /// <param name="max">The maximum allowed value for the slider.</param>
        /// <param name="step">The increment step for the slider.</param>
        /// <param name="tooltip">Optional tooltip for the row.</param>
        /// <param name="isInteger">Whether the slider should only allow integer values.</param>
        /// <param name="mutationScope">The scope of the setting mutation.</param>
        /// <returns>True if the slider value changed, false otherwise.</returns>
        public bool Slider(
            string label,
            ref float value,
            float min,
            float max,
            float step = 0.01f,
            string tooltip = null,
            bool isInteger = false,
            SettingMutationScope? mutationScope = null,
            bool notifyOnMutate = true)
        {
            string displayLabel = SettingsViewUtilities.LabelWithTooltip(label, tooltip);
            float labelWidth = GetSliderLabelWidth(Width);
            float rowHeight = Math.Max(DefaultRowHeight, SettingsViewUtilities.CalcTextHeight(displayLabel, Math.Max(1f, labelWidth), GameFont.Small));

            Rect rowRect = new Rect(X, Y, Width, rowHeight);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rowRect, tooltip);
            }

            GetSliderRowRects(rowRect, out Rect labelRect, out Rect sliderRect, out Rect textRect);

            // 1. Left Label
            Widgets.Label(labelRect, displayLabel);

            bool changed = DrawAtomicSliderWithInput(sliderRect, textRect, ref value, min, max, step, isInteger, notifyOnMutate, mutationScope);

            Y += rowHeight + SettingsViewUtilities.CompactGap;
            return changed;
        }

        /// <summary>
        /// Renders an integer-quantized slider with a width-dependent bar and text field.
        /// </summary>
        /// <param name="label">The label for the integer slider row.</param>
        /// <param name="value">The current integer value of the slider.</param>
        /// <param name="min">The minimum allowed integer value.</param>
        /// <param name="max">The maximum allowed integer value.</param>
        /// <param name="tooltip">Optional tooltip for the row.</param>
        /// <param name="mutationScope">The scope of the setting mutation.</param>
        /// <returns>True if the slider value changed, false otherwise.</returns>
        public bool IntSlider(
            string label,
            ref int value,
            int min,
            int max,
            string tooltip = null,
            SettingMutationScope? mutationScope = null)
        {
            float floatVal = value;
            bool changed = Slider(label, ref floatVal, min, max, step: 1f, tooltip: tooltip, isInteger: true, mutationScope: mutationScope);
            if (changed)
            {
                value = (int)Math.Round(floatVal, MidpointRounding.ToEven);
            }
            return changed;
        }

        /// <summary>
        /// Renders a composite row featuring a toggle checkbox, width-dependent slider, and text field.
        /// When the toggle is unchecked, the slider and text box are greyed out.
        /// </summary>
        /// <param name="label">The label for the toggle slider row.</param>
        /// <param name="enabled">The current state of the toggle checkbox.</param>
        /// <param name="value">The current value of the slider and text input.</param>
        /// <param name="min">The minimum allowed value for the slider.</param>
        /// <param name="max">The maximum allowed value for the slider.</param>
        /// <param name="step">The increment step for the slider.</param>
        /// <param name="tooltip">Optional tooltip for the row.</param>
        /// <param name="mutationScope">The scope of the setting mutation.</param>
        /// <returns>True if either the toggle or the slider value changed, false otherwise.</returns>
        public bool ToggleSlider(
            string label,
            ref bool enabled,
            ref float value,
            float min,
            float max,
            float step = 0.01f,
            string tooltip = null,
            SettingMutationScope? mutationScope = null)
        {
            string displayLabel = SettingsViewUtilities.LabelWithTooltip(label, tooltip);
            float labelWidth = GetSliderLabelWidth(Width);
            float rowHeight = Math.Max(DefaultRowHeight, SettingsViewUtilities.CalcTextHeight(displayLabel, Math.Max(1f, labelWidth - 30f), GameFont.Small));

            Rect rowRect = new Rect(X, Y, Width, rowHeight);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rowRect, tooltip);
            }

            GetSliderRowRects(rowRect, out Rect labelRect, out Rect sliderRect, out Rect textRect);

            // 1. Left Checkbox & Label
            bool prevEnabled = enabled;
            Widgets.CheckboxLabeled(labelRect, displayLabel, ref enabled);

            // 2. Middle Slider & Right Text Input
            bool prevGuiState = GUI.enabled;
            if (!enabled) GUI.enabled = false;

            bool valueChanged = DrawAtomicSliderWithInput(sliderRect, textRect, ref value, min, max, step, isInteger: false, notifyOnMutate: true, mutationScope);

            GUI.enabled = prevGuiState;
            Y += rowHeight + SettingsViewUtilities.CompactGap;

            if (prevEnabled != enabled)
            {
                SettingsViewUtilities.OnSettingMutated(mutationScope);
                return true;
            }

            return valueChanged;
        }

        /// <summary>
        /// Computes the width of the label area for a slider row based on the total row width.
        /// </summary>
        /// <param name="rowWidth">The total width of the slider row.</param>
        /// <returns>The computed width of the label area.</returns>
        private static float GetSliderLabelWidth(float rowWidth)
        {
            float sliderWidth = rowWidth > 500f ? FixedSliderWidth : 120f;
            return Math.Max(1f, rowWidth - sliderWidth - FixedInputWidth - (ControlGap * 2f));
        }

        /// <summary>
        /// Computes the layout rectangles for a slider row, including the label, slider, and text input areas.
        /// </summary>
        /// <param name="row">The rectangle defining the entire row's position and size.</param>
        /// <param name="label">The computed rectangle for the label area.</param>
        /// <param name="slider">The computed rectangle for the slider area.</param>
        /// <param name="input">The computed rectangle for the text input area.</param>
        private static void GetSliderRowRects(Rect row, out Rect label, out Rect slider, out Rect input)
        {
            float sliderWidth = row.width > 500f ? FixedSliderWidth : 120f;
            float labelWidth = row.width - sliderWidth - FixedInputWidth - (ControlGap * 2f);
            float controlY = row.y + Math.Max(1f, (row.height - 24f) * 0.5f);
            label = new Rect(row.x, row.y + 1f, labelWidth, Math.Max(24f, row.height - 1f));
            slider = new Rect(row.x + labelWidth + ControlGap, controlY, sliderWidth, 24f);
            input = new Rect(slider.xMax + ControlGap, controlY, FixedInputWidth, 22f);
        }

        /// <summary>
        /// Renders a compact side-by-side slider and text input unit within an externally positioned Rect.
        /// Computes slider bar width dynamically from unitRect to support both dual systemic rows and RGB columns.
        /// </summary>
        /// <param name="unitRect">The rectangle defining the unit's position and size.</param>
        /// <param name="tag">The label or tag for the slider unit.</param>
        /// <param name="value">The current value of the slider and text input.</param>
        /// <param name="min">The minimum allowed value.</param>
        /// <param name="max">The maximum allowed value.</param>
        /// <param name="step">The increment step for the slider.</param>
        /// <param name="notifyOnMutate">Whether to notify on value change.</param>
        /// <param name="mutationScope">The scope of the setting mutation.</param>
        /// <returns>True if the value was changed, false otherwise.</returns>
        public bool DrawSliderInputUnit(
            Rect unitRect,
            string tag,
            ref float value,
            float min,
            float max,
            float step = 0.01f,
            bool notifyOnMutate = true,
            SettingMutationScope? mutationScope = null)
        {
            float tagWidth = SliderTagWidth;
            float inputWidth = FixedInputWidth;
            float sliderWidth = Math.Max(60f, unitRect.width - tagWidth - inputWidth - (ControlGap * 2f));

            Rect tagRect = new Rect(unitRect.x, unitRect.y + 2f, tagWidth, 22f);
            Widgets.Label(tagRect, tag);

            Rect sliderRect = new Rect(tagRect.xMax + ControlGap, unitRect.y + 1f, sliderWidth, 24f);
            Rect textRect = new Rect(sliderRect.xMax + ControlGap, unitRect.y + 1f, inputWidth, 22f);

            return DrawAtomicSliderWithInput(sliderRect, textRect, ref value, min, max, step, isInteger: false, notifyOnMutate, mutationScope);
        }

        /// <summary>
        /// Draws a slider with an accompanying text input field, handling both user interactions and value rounding.
        /// </summary>
        /// <param name="sliderRect">The rectangle defining the slider's position and size.</param>
        /// <param name="textRect">The rectangle defining the text input's position and size.</param>
        /// <param name="value">The current value of the slider and text input.</param>
        /// <param name="min">The minimum allowed value.</param>
        /// <param name="max">The maximum allowed value.</param>
        /// <param name="step">The increment step for the slider.</param>
        /// <param name="isInteger">Whether the value should be treated as an integer.</param>
        /// <param name="notifyOnMutate">Whether to notify on value change.</param>
        /// <param name="mutationScope">The scope of the setting mutation.</param>
        /// <returns>True if the value was changed, false otherwise.</returns>
        private static bool DrawAtomicSliderWithInput(
            Rect sliderRect,
            Rect textRect,
            ref float value,
            float min,
            float max,
            float step,
            bool isInteger,
            bool notifyOnMutate,
            SettingMutationScope? mutationScope)
        {
            float originalValue = value;
            bool valueChanged = false;

            // 1. Horizontal Slider
            float oldVal = value;
            float sliderVal = Widgets.HorizontalSlider(sliderRect, value, min, max, true);

            if (Math.Abs(sliderVal - oldVal) > 0.0001f)
            {
                if (step > 0.0001f)
                {
                    sliderVal = (float)Math.Round(sliderVal / step, MidpointRounding.ToEven) * step;
                }
                value = isInteger
                    ? (float)Math.Round(sliderVal, MidpointRounding.ToEven)
                    : (float)Math.Round(sliderVal, 2, MidpointRounding.ToEven);
                valueChanged = true;
            }

            // 2. Focus-Aware Text Input
            InputScopeState scope = activeScope;
            string controlName = scope.GetNextControlName();
            GUI.SetNextControlName(controlName);
            bool isFocused = (GUI.GetNameOfFocusedControl() == controlName);

            // Desktop UX: Enter / Return commits value and releases focus
            if (isFocused && Event.current.type == EventType.KeyDown && 
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                GUIUtility.keyboardControl = 0;
                Event.current.Use();
                isFocused = false;
            }

            string currentBuffer;
            if (!scope.ActiveInputBuffers.TryGetValue(controlName, out currentBuffer) || !isFocused)
            {
                currentBuffer = isInteger
                    ? ((int)Math.Round(value, MidpointRounding.ToEven)).ToString()
                    : value.ToString("F2");
                scope.ActiveInputBuffers[controlName] = currentBuffer;
            }

            string newBuffer = Widgets.TextField(textRect, currentBuffer);

            if (newBuffer != currentBuffer)
            {
                scope.ActiveInputBuffers[controlName] = newBuffer;

                // Support both '.' and ',' decimal separators across all localized keyboards
                string normalizedInput = newBuffer.Replace(',', '.');

                if (float.TryParse(normalizedInput, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    float clamped = Math.Min(max, Math.Max(min, parsed));
                    if (step > 0.0001f)
                    {
                        clamped = (float)Math.Round(clamped / step, MidpointRounding.ToEven) * step;
                    }

                    float parsedValue = isInteger
                        ? (float)Math.Round(clamped, MidpointRounding.ToEven)
                        : (float)Math.Round(clamped, 2, MidpointRounding.ToEven);
                    if (Math.Abs(parsedValue - value) > 0.0001f)
                    {
                        value = parsedValue;
                        valueChanged = true;
                    }
                }
            }

            if (valueChanged && Math.Abs(originalValue - value) > 0.0001f)
            {
                if (notifyOnMutate)
                {
                    SettingsViewUtilities.OnSettingMutated(mutationScope);
                }
                return true;
            }

            return false;
        }

        #endregion

        #region 8. BUTTONS & ACTIONS

        /// <summary>
        /// Draws a button with the specified label and layout constraints.
        /// </summary>
        /// <param name="label">The text label of the button.</param>
        /// <param name="minWidth">The minimum width of the button.</param>
        /// <param name="minHeight">The minimum height of the button.</param>
        /// <param name="maxWidth">The maximum width of the button.</param>
        /// <param name="fixedWidth">The fixed width of the button, if specified.</param>
        /// <param name="fixedHeight">The fixed height of the button, if specified.</param>
        /// <param name="maxHeight">The maximum height of the button.</param>
        /// <returns>True if the button was clicked; otherwise, false.</returns>
        public bool Button(string label, float minWidth = 0f, float minHeight = DefaultButtonHeight,
            float? maxWidth = null, float? fixedWidth = null, float? fixedHeight = null, float? maxHeight = null)
        {
            Vector2 size = SettingsViewUtilities.ButtonSize(label, Width, minWidth, minHeight, maxWidth, fixedWidth, fixedHeight, maxHeight);
            Rect btnRect = new Rect(X, Y, size.x, size.y);
            Y += size.y + SettingsViewUtilities.ControlGap;
            return SettingsViewUtilities.DrawButton(btnRect, label);
        }

        /// <summary>
        /// Draws a button using a localized label key. The label is retrieved from the cache.
        /// </summary>
        /// <param name="labelKey">The key for the localized label text.</param>
        /// <param name="minWidth">The minimum width of the button.</param>
        /// <param name="minHeight">The minimum height of the button.</param>
        /// <param name="maxWidth">The maximum width of the button.</param>
        /// <param name="fixedWidth">The fixed width of the button, if specified.</param>
        /// <param name="fixedHeight">The fixed height of the button, if specified.</param>
        /// <param name="maxHeight">The maximum height of the button.</param>
        /// <returns>True if the button was clicked; otherwise, false.</returns>
        public bool ButtonKey(string labelKey, float minWidth = 0f, float minHeight = DefaultButtonHeight,
            float? maxWidth = null, float? fixedWidth = null, float? fixedHeight = null, float? maxHeight = null)
        {
            return Button(SettingsViewUtilities.CachedText(labelKey), minWidth, minHeight, maxWidth, fixedWidth, fixedHeight, maxHeight);
        }

        #endregion
    }
}
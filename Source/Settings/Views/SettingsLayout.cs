using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-00] High-performance, zero-allocation immediate mode layout cursor for Mod Settings.
    /// Operates on top-down linear Rect progression with automatic text measurement, fixed 200px sliders,
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
        public const float ControlGap = 8f;

        public static readonly Color MutedTextColor = new Color(0.63f, 0.65f, 0.67f);
        public static readonly Color DividerLineColor = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color CardOutlineColor = new Color(1f, 1f, 1f, 0.08f);

        #endregion

        #region 2. STATIC CONTROL ID & TEXT BUFFER CACHE

        private static int currentControlIndex = 0;
        private static readonly List<string> cachedControlNames = new List<string>(64);
        private static readonly Dictionary<string, string> activeInputBuffers = new Dictionary<string, string>(64);

        /// <summary>
        /// Resets the control index at the start of each frame to maintain stable control IDs without GC allocation.
        /// </summary>
        public static void ResetControlIndex()
        {
            currentControlIndex = 0;
        }

        /// <summary>
        /// Clears all cached manual input text buffers (e.g. on settings reset).
        /// </summary>
        public static void ClearInputBuffers()
        {
            activeInputBuffers.Clear();
        }

        private static string GetNextControlName()
        {
            if (currentControlIndex >= cachedControlNames.Count)
            {
                cachedControlNames.Add("OH_Layout_Input_" + currentControlIndex);
            }
            return cachedControlNames[currentControlIndex++];
        }

        #endregion

        #region 3. FIELDS & CONSTRUCTOR

        public float X;
        public float Y;
        public float Width;

        public SettingsLayout(Rect bounds)
        {
            X = bounds.x + 2f;
            Y = bounds.y;
            Width = bounds.width - 6f;
        }

        #endregion

        #region 4. HEADERS & TYPOGRAPHY

        public void Header(string label)
        {
            Text.Font = GameFont.Medium;
            Rect rect = new Rect(X, Y, Width, 30f);
            Widgets.Label(rect, label);
            Text.Font = GameFont.Small;
            Y += 34f;
        }

        public void HeaderKey(string translationKey)
        {
            Header(SettingsViewUtilities.CachedText(translationKey));
        }

        public void SubHeader(string label)
        {
            Text.Font = GameFont.Small;
            Rect rect = new Rect(X, Y, Width, 22f);
            Widgets.Label(rect, label);
            Y += 24f;
        }

        public void Description(string description)
        {
            if (string.IsNullOrEmpty(description)) return;

            Text.Font = GameFont.Tiny;
            float textHeight = Text.CalcHeight(description, Width);
            Rect rect = new Rect(X, Y, Width, textHeight);

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

        public void DescriptionKey(string translationKey)
        {
            Description(SettingsViewUtilities.CachedText(translationKey));
        }

        #endregion

        #region 5. DIVIDERS & SPACING

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

        public void Gap(float pixels = DefaultGap)
        {
            Y += pixels;
        }

        #endregion

        #region 6. TOGGLES & CHECKBOXES

        public bool Toggle(string label, ref bool value, string tooltip = null)
        {
            Rect rowRect = new Rect(X, Y, Width, DefaultToggleHeight);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rowRect, tooltip);
            }

            bool prev = value;
            Widgets.CheckboxLabeled(rowRect, label, ref value);

            Y += 28f;

            if (prev != value)
            {
                SettingsViewUtilities.OnSettingMutated();
                return true;
            }

            return false;
        }

        public bool ToggleKey(string labelKey, ref bool value, string tooltipKey = null)
        {
            string label = SettingsViewUtilities.CachedText(labelKey);
            string tooltip = !string.IsNullOrEmpty(tooltipKey) ? SettingsViewUtilities.CachedText(tooltipKey) : null;
            return Toggle(label, ref value, tooltip);
        }

        #endregion

        #region 7. SLIDERS WITH FIXED BARS & MANUAL ENTRY FIELDS

        /// <summary>
        /// Renders a slider row with a fixed 200px slider bar and a manual entry text box on the right.
        /// Defaults to fine 1% (0.01f) tuning increments.
        /// </summary>
        public bool Slider(
            string label,
            ref float value,
            float min,
            float max,
            float step = 0.01f,
            string customLabel = null,
            string tooltip = null)
        {
            Rect rowRect = new Rect(X, Y, Width, DefaultRowHeight);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rowRect, tooltip);
            }

            float sliderWidth = Width > 500f ? FixedSliderWidth : 120f;
            float inputWidth = FixedInputWidth;
            float labelWidth = Width - sliderWidth - inputWidth - (ControlGap * 2f);

            // 1. Left Label with dynamic value formatting
            string displayLabel = !string.IsNullOrEmpty(customLabel) ? string.Format("{0} ({1})", label, customLabel) : label;
            Rect labelRect = new Rect(X, Y + 1f, labelWidth, 24f);
            Widgets.Label(labelRect, displayLabel);

            // 2. Middle Fixed-Width Slider
            Rect sliderRect = new Rect(X + labelWidth + ControlGap, Y + 1f, sliderWidth, 24f);

            // 3. Right Manual Entry Text Field
            Rect textRect = new Rect(sliderRect.xMax + ControlGap, Y + 1f, inputWidth, 22f);

            bool changed = DrawAtomicSliderWithInput(sliderRect, textRect, ref value, min, max, step, isInteger: false);

            Y += 30f;
            return changed;
        }

        /// <summary>
        /// Renders an integer-quantized slider with fixed 200px bar and text field.
        /// </summary>
        public bool IntSlider(
            string label,
            ref int value,
            int min,
            int max,
            string customLabel = null,
            string tooltip = null)
        {
            float floatVal = value;
            bool changed = Slider(label, ref floatVal, min, max, step: 1f, customLabel: customLabel, tooltip: tooltip);
            if (changed)
            {
                value = (int)Math.Round(floatVal, MidpointRounding.ToEven);
            }
            return changed;
        }

        /// <summary>
        /// Renders a composite row featuring a toggle checkbox, fixed 200px slider, and text field.
        /// When the toggle is unchecked, the slider and text box are greyed out.
        /// </summary>
        public bool ToggleSlider(
            string label,
            ref bool enabled,
            ref float value,
            float min,
            float max,
            float step = 0.01f,
            string customLabel = null,
            string tooltip = null)
        {
            Rect rowRect = new Rect(X, Y, Width, DefaultRowHeight);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rowRect, tooltip);
            }

            float sliderWidth = Width > 500f ? FixedSliderWidth : 120f;
            float inputWidth = FixedInputWidth;
            float labelWidth = Width - sliderWidth - inputWidth - (ControlGap * 2f);

            // 1. Left Checkbox & Label
            string displayLabel = !string.IsNullOrEmpty(customLabel) ? string.Format("{0} ({1})", label, customLabel) : label;
            Rect labelRect = new Rect(X, Y + 1f, labelWidth, 24f);
            bool prevEnabled = enabled;
            Widgets.CheckboxLabeled(labelRect, displayLabel, ref enabled);

            // 2. Middle Slider & Right Text Input
            Rect sliderRect = new Rect(X + labelWidth + ControlGap, Y + 1f, sliderWidth, 24f);
            Rect textRect = new Rect(sliderRect.xMax + ControlGap, Y + 1f, inputWidth, 22f);

            bool prevGuiState = GUI.enabled;
            if (!enabled) GUI.enabled = false;

            bool valueChanged = DrawAtomicSliderWithInput(sliderRect, textRect, ref value, min, max, step, isInteger: false);

            GUI.enabled = prevGuiState;
            Y += 30f;

            if (prevEnabled != enabled)
            {
                SettingsViewUtilities.OnSettingMutated();
                return true;
            }

            return valueChanged;
        }

        /// <summary>
        /// Renders a compact side-by-side slider and text input unit within an externally positioned Rect.
        /// Computes slider bar width dynamically from unitRect to support both dual systemic rows and RGB columns.
        /// </summary>
        public bool DrawSliderInputUnit(
            Rect unitRect,
            string tag,
            ref float value,
            float min,
            float max,
            float step = 0.01f)
        {
            float tagWidth = 24f;
            float inputWidth = FixedInputWidth;
            float sliderWidth = Math.Max(60f, unitRect.width - tagWidth - inputWidth - (ControlGap * 2f));

            Rect tagRect = new Rect(unitRect.x, unitRect.y + 2f, tagWidth, 22f);
            Widgets.Label(tagRect, tag);

            Rect sliderRect = new Rect(tagRect.xMax + ControlGap, unitRect.y + 1f, sliderWidth, 24f);
            Rect textRect = new Rect(sliderRect.xMax + ControlGap, unitRect.y + 1f, inputWidth, 22f);

            return DrawAtomicSliderWithInput(sliderRect, textRect, ref value, min, max, step, isInteger: false);
        }

        private static bool DrawAtomicSliderWithInput(
            Rect sliderRect,
            Rect textRect,
            ref float value,
            float min,
            float max,
            float step,
            bool isInteger)
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
            string controlName = GetNextControlName();
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
            if (!activeInputBuffers.TryGetValue(controlName, out currentBuffer) || !isFocused)
            {
                currentBuffer = isInteger
                    ? ((int)Math.Round(value, MidpointRounding.ToEven)).ToString()
                    : value.ToString("F2");
                activeInputBuffers[controlName] = currentBuffer;
            }

            string newBuffer = Widgets.TextField(textRect, currentBuffer);

            if (newBuffer != currentBuffer)
            {
                activeInputBuffers[controlName] = newBuffer;

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
                SettingsViewUtilities.OnSettingMutated();
                return true;
            }

            return false;
        }

        #endregion

        #region 8. BUTTONS & ACTIONS

        public bool Button(string label, float buttonWidth = 180f, float buttonHeight = DefaultButtonHeight)
        {
            Rect btnRect = new Rect(X, Y, buttonWidth, buttonHeight);
            Y += buttonHeight + 6f;
            return Widgets.ButtonText(btnRect, label);
        }

        public bool ButtonKey(string labelKey, float buttonWidth = 180f, float buttonHeight = DefaultButtonHeight)
        {
            return Button(SettingsViewUtilities.CachedText(labelKey), buttonWidth, buttonHeight);
        }

        #endregion
    }
}
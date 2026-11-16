using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-00] Identifies the downstream synchronization and invalidation tier required when a setting changes.
    /// </summary>
    public enum SettingMutationScope
    {
        ConfigOnly = 0,
        Presentation = 1,
        Solver = 2,
        Topology = 3
    }

    /// <summary>
    /// [SEC-00] High-performance presentation utilities, cached translations and text measurements,
    /// accessibility palette adapters, and centralized settings mutation sink.
    /// Resides under Source/Settings/Views/.
    /// </summary>
    public static class SettingsViewUtilities
    {
        #region 1. CONSTANTS & SHARED COLORS

        /// <summary>Minimal gap for compact layouts.</summary>
        public const float CompactGap = 4f;

        /// <summary>Standard gap between controls.</summary>
        public const float ControlGap = 8f;

        /// <summary>Gap between grouped elements.</summary>
        public const float GroupGap = 16f;

        /// <summary>Gap between columns.</summary>
        public const float ColumnGap = 16f;

        /// <summary>Padding inside panels.</summary>
        public const float PanelPadding = 8f;

        /// <summary>Space reserved for scrollbars.</summary>
        public const float ScrollbarGutter = 24f;

        /// <summary>Horizontal padding for button text.</summary>
        public const float ButtonTextPadding = 24f;

        /// <summary>Vertical padding for button text.</summary>
        private const float ButtonVerticalTextPadding = 8f;

        /// <summary>
        /// Represents a cached text measurement for high-performance layout calculations.
        /// </summary>
        private struct TextMeasurement
        {
            public string Text;
            public GameFont Font;
            public GUIStyle Style;
            public Font FontAsset;
            public int FontSize;
            public FontStyle FontStyle;
            public GUISkin Skin;
            public Font SkinFont;
            public bool WordWrap;
            public float Width;
            public float Result;
        }

        /// <summary>Default color for description text.</summary>
        public static readonly Color DescriptionTextColor = new Color(0.63f, 0.65f, 0.67f);

        /// <summary>Color used for dark target highlights.</summary>
        public static readonly Color DarkTargetHighlightColor = new Color(0f, 0f, 0f, 0.40f);

        /// <summary>The player-selected "healthy / good" colour from the active accessibility palette.</summary>
        public static Color HealthyColor
        {
            get { return OverHaulers.settings?.colorHealthy ?? PresentationDefaults.ColorHealthyDefault; }
        }

        /// <summary>The player-selected "critical / bad" colour from the active accessibility palette.</summary>
        public static Color CriticalColor
        {
            get { return OverHaulers.settings?.colorCritical ?? PresentationDefaults.ColorCriticalDefault; }
        }

        #endregion

        #region 2. CENTRALIZED MUTATION SINK

        private static bool isBoundToSettings;

        /// <summary>
        /// Subscribes the view layer to Settings change notifications.
        /// Idempotent; invoked from the mod constructor.
        /// </summary>
        public static void BindToSettings()
        {
            if (isBoundToSettings) return;
            isBoundToSettings = true;

            Settings.ValuesReset += SettingsLayout.ClearInputBuffers;
            Settings.ValuesMutated += OnAllSettingsMutated;
            Settings.PresentationMutated += OnPresentationSettingsMutated;
        }

        private static void OnAllSettingsMutated()
        {
            OnSettingMutated(SettingMutationScope.Topology);
        }

        private static void OnPresentationSettingsMutated()
        {
            OnSettingMutated(SettingMutationScope.Presentation);
        }

        /// <summary>
        /// Centralized notification sink triggered whenever a slider, toggle, or setting mutates.
        /// A <c>null</c> scope performs no Core push or cache invalidation; non-null scopes push configuration
        /// and invalidate strictly up to the declared tier.
        /// </summary>
        public static void OnSettingMutated(SettingMutationScope? scope = null)
        {
            if (!scope.HasValue) return;

            OverHaulers.settings?.PushConfigurationToCore();

            if (scope.Value == SettingMutationScope.ConfigOnly) return;

            TestBench.MarkDirty();

            if (scope.Value >= SettingMutationScope.Topology)
            {
                TopologyLayoutCompiler.InvalidateAllTopologies();
            }

            if (scope.Value >= SettingMutationScope.Solver)
            {
                PawnDataRegistry.MarkAllStale();
            }

            DetailedModelRegistry.Clear();
        }

        #endregion

        #region 3. DYNAMIC TEXT MEASUREMENT HELPERS

        /// <summary>Array of cached text measurements for high-performance layout calculations.</summary>
        private static readonly TextMeasurement[] textMeasurements = new TextMeasurement[128];

        /// <summary>Current count of cached text measurements.</summary>
        private static int textMeasurementCount;

        /// <summary>Current epoch for text measurement caching.</summary>
        private static int textMeasurementEpoch = -1;

        /// <summary>
        /// Calculates the height required to render text within the specified width and font.
        /// </summary>
        /// <param name="text">The text to measure.</param>
        /// <param name="width">The width constraint for the text.</param>
        /// <param name="font">The font to use for measurement.</param>
        /// <returns>The height required to render the text within the specified width.</returns>
        public static float CalcTextHeight(string text, float width, GameFont font = GameFont.Tiny)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f) return 0f;

            GameFont originalFont = Text.Font;
            Text.Font = font;
            try
            {
                return MeasuredTextHeight(text, width);
            }
            finally
            {
                Text.Font = originalFont;
            }
        }

        /// <summary>
        /// Measures text in the current font without a width constraint.
        /// </summary>
        /// <param name="text">The text to measure.</param>
        /// <returns>The width required to render the text without wrapping.</returns>
        public static float MeasuredTextWidth(string text)
        {
            return MeasureText(text, 0f);
        }

        /// <summary>
        /// Measures wrapped text in the current font, restoring the previous wrapping mode.
        /// </summary>
        /// <param name="text">The text to measure.</param>
        /// <param name="width">The width constraint for wrapping.</param>
        /// <returns>The height required to render the text within the specified width.</returns>
        public static float MeasuredTextHeight(string text, float width)
        {
            bool originalWrap = Text.WordWrap;
            Text.WordWrap = true;
            try
            {
                return MeasureText(text, Math.Max(1f, width));
            }
            finally
            {
                Text.WordWrap = originalWrap;
            }
        }

        /// <summary>
        /// Sizes a button to its label. Fixed dimensions replace the adaptive size; maximum dimensions and
        /// available width remain hard limits.
        /// </summary>
        /// <param name="label">The text label of the button.</param>
        /// <param name="availableWidth">The available width for the button.</param>
        /// <param name="minWidth">The minimum width of the button.</param>
        /// <param name="minHeight">The minimum height of the button.</param>
        /// <param name="maxWidth">The maximum width of the button.</param>
        /// <param name="fixedWidth">The fixed width of the button, if specified.</param>
        /// <param name="fixedHeight">The fixed height of the button, if specified.</param>
        /// <param name="maxHeight">The maximum height of the button.</param>
        /// <returns>The size of the button as a Vector2.</returns>
        public static Vector2 ButtonSize(string label, float availableWidth, float minWidth = 0f,
            float minHeight = SettingsLayout.DefaultButtonHeight, float? maxWidth = null,
            float? fixedWidth = null, float? fixedHeight = null, float? maxHeight = null)
        {
            float limit = Math.Max(1f, availableWidth);
            if (maxWidth.HasValue) limit = Math.Min(limit, Math.Max(1f, maxWidth.Value));
            float preferred = fixedWidth ?? Math.Max(minWidth, MeasuredTextWidth(label) + ButtonTextPadding);
            float width = Math.Min(limit, Math.Max(1f, preferred));
            float height = fixedHeight ?? Math.Max(minHeight, MeasuredTextHeight(label, width - ButtonTextPadding) + ButtonVerticalTextPadding);
            if (maxHeight.HasValue) height = Math.Min(height, Math.Max(1f, maxHeight.Value));
            return new Vector2(width, height);
        }

        /// <summary>
        /// Places a button in a wrapping row and advances the row cursor.
        /// </summary>
        /// <param name="bounds">The bounding rectangle for the row of buttons.</param>
        /// <param name="x">The current x-coordinate of the row cursor.</param>
        /// <param name="y">The current y-coordinate of the row cursor.</param>
        /// <param name="rowHeight">The current height of the row.</param>
        /// <param name="label">The text label of the button.</param>
        /// <param name="gap">The gap between buttons in the row.</param>
        /// <param name="minWidth">The minimum width of the button.</param>
        /// <param name="minHeight">The minimum height of the button.</param>
        /// <param name="maxWidth">The maximum width of the button.</param>
        /// <param name="fixedWidth">The fixed width of the button, if specified.</param>
        /// <param name="fixedHeight">The fixed height of the button, if specified.</param>
        /// <param name="maxHeight">The maximum height of the button.</param>
        /// <returns>The rectangle defining the position and size of the placed button.</returns>
        public static Rect PlaceButton(Rect bounds, ref float x, ref float y, ref float rowHeight,
            string label, float gap, float minWidth = 0f, float minHeight = SettingsLayout.DefaultButtonHeight,
            float? maxWidth = null, float? fixedWidth = null, float? fixedHeight = null, float? maxHeight = null)
        {
            Vector2 size = ButtonSize(label, bounds.width, minWidth, minHeight, maxWidth, fixedWidth, fixedHeight, maxHeight);
            if (x > bounds.x && x + size.x > bounds.xMax)
            {
                x = bounds.x;
                y += rowHeight + gap;
                rowHeight = 0f;
            }
            Rect result = new Rect(x, y, size.x, size.y);
            x += size.x + gap;
            rowHeight = Math.Max(rowHeight, size.y);
            return result;
        }

        /// <summary>
        /// Draws a button with its descriptive tooltip, or the full label when constrained.
        /// </summary>
        /// <param name="rect">The rectangle defining the position and size of the button.</param>
        /// <param name="label">The text label of the button.</param>
        /// <param name="tooltip">The tooltip text to display when hovering over the button.</param>
        /// <returns>True if the button was clicked; otherwise, false.</returns>
        public static bool DrawButton(Rect rect, string label, string tooltip = null)
        {
            bool clicked = Widgets.ButtonText(rect, label);
            if (!string.IsNullOrEmpty(tooltip))
                TooltipHandler.TipRegion(rect, tooltip);
            else if (rect.width < MeasuredTextWidth(label) + ButtonTextPadding ||
                rect.height < MeasuredTextHeight(label, rect.width - ButtonTextPadding) + ButtonVerticalTextPadding)
                TooltipHandler.TipRegion(rect, label);
            return clicked;
        }

        /// <summary>
        /// Draws a search field with a localized placeholder and clear button.
        /// </summary>
        /// <param name="rect">The rectangle defining the position and size of the search field.</param>
        /// <param name="text">The current text in the search field.</param>
        /// <param name="placeholderKey">The localization key for the placeholder text.</param>
        /// <param name="placeholderFont">The font to use for the placeholder text.</param>
        /// <returns>The updated text after user input.</returns>
        public static string DrawSearchField(Rect rect, string text, string placeholderKey, GameFont placeholderFont)
        {
            const float clearWidth = 24f;
            const float placeholderInset = 6f;
            Rect inputRect = new Rect(rect.x, rect.y, rect.width - clearWidth - CompactGap, rect.height);
            Rect clearRect = new Rect(rect.x + rect.width - clearWidth, rect.y, clearWidth, rect.height);
            text = Widgets.TextField(inputRect, text);

            if (string.IsNullOrEmpty(text) && Event.current.type == EventType.Repaint)
            {
                Color originalColor = GUI.color;
                GameFont originalFont = Text.Font;
                try
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = placeholderFont;
                    Widgets.Label(new Rect(inputRect.x + placeholderInset, inputRect.y + 4f, inputRect.width - placeholderInset * 2f, inputRect.height), CachedText(placeholderKey));
                }
                finally
                {
                    GUI.color = originalColor;
                    Text.Font = originalFont;
                }
            }

            return Widgets.ButtonText(clearRect, "✕") ? string.Empty : text;
        }

        /// <summary>Measures the width or height of the specified text with caching for performance.</summary>
        /// <param name="text">The text to measure.</param>
        /// <param name="width">The width constraint for the text. If zero, measures the width; otherwise, measures the height.</param>
        /// <returns>The measured width or height of the text.</returns>
        private static float MeasureText(string text, float width)
        {
            int epoch = TranslationEpoch;
            if (epoch != textMeasurementEpoch)
            {
                textMeasurementCount = 0;
                textMeasurementEpoch = epoch;
            }

            GameFont gameFont = Text.Font;
            GUIStyle style = Text.CurFontStyle;
            Font font = style.font;
            int fontSize = style.fontSize;
            FontStyle fontStyle = style.fontStyle;
            GUISkin skin = GUI.skin;
            Font skinFont = skin.font;
            bool wordWrap = Text.WordWrap;
            for (int i = 0; i < textMeasurementCount; i++)
            {
                TextMeasurement item = textMeasurements[i];
                if (item.Font == gameFont && item.Style == style && item.FontAsset == font && item.FontSize == fontSize &&
                    item.FontStyle == fontStyle && item.Skin == skin && item.SkinFont == skinFont &&
                    item.WordWrap == wordWrap && item.Width == width && item.Text == text) return item.Result;
            }

            float result = width == 0f ? Text.CalcSize(text).x : Text.CalcHeight(text, width);
            if (textMeasurementCount == textMeasurements.Length) textMeasurementCount = 0;
            textMeasurements[textMeasurementCount++] = new TextMeasurement
            {
                Text = text, Font = gameFont, Style = style, FontAsset = font, FontSize = fontSize,
                FontStyle = fontStyle, Skin = skin, SkinFont = skinFont, WordWrap = wordWrap, Width = width, Result = result
            };
            return result;
        }

        /// <summary>
        /// Draws a section description directly within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the container area for the section description.</param>
        /// <param name="descriptionText">The description text to draw.</param>
        /// <param name="currentY">The current Y position within the container.</param>
        /// <returns>The updated Y position after drawing the section description.</returns>
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

            return currentY + descH + ControlGap;
        }

        #endregion

        #region 4. CACHED TRANSLATION LOOKUPS

        /// <summary>Cache for localized translation strings to minimize GC allocations.</summary>
        private static readonly Dictionary<string, string> translationCache = new Dictionary<string, string>(128);

        /// <summary>Cache for decorated tooltip labels to minimize GC allocations.</summary>
        private static readonly Dictionary<string, string> tooltipLabelCache = new Dictionary<string, string>(128);

        /// <summary>The language currently used by the translation cache.</summary>
        private static LoadedLanguage translationCacheLanguage;

        /// <summary>The current translation epoch, incremented when the language changes.</summary>
        private static int translationEpoch;

        /// <summary>
        /// Incremented every time the active language changes and the translation cache is flushed.
        /// Used by Benchmark and Self-Check views to determine when to rebuild string tables.
        /// </summary>
        public static int TranslationEpoch
        {
            get
            {
                FlushTranslationCacheIfLanguageChanged();
                return translationEpoch;
            }
        }

        /// <summary>
        /// Returns cached localized text for a Keyed translation key; a cache miss translates and stores the result.
        /// </summary>
        public static string CachedText(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            FlushTranslationCacheIfLanguageChanged();

            string text;
            if (!translationCache.TryGetValue(key, out text))
            {
                text = key.Translate().ToString();
                translationCache[key] = text;
            }

            return text;
        }

        /// <summary>Marks labels with a non-empty tooltip, caching decorated text between frames.</summary>
        public static string LabelWithTooltip(string label, string tooltip)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(tooltip)) return label;

            FlushTranslationCacheIfLanguageChanged();
            if (!tooltipLabelCache.TryGetValue(label, out string decorated))
            {
                if (tooltipLabelCache.Count >= 256) tooltipLabelCache.Clear();
                decorated = string.Concat(label, " ⓘ");
                tooltipLabelCache.Add(label, decorated);
            }
            return decorated;
        }

        /// <summary>Flushes the translation cache if the active language has changed.</summary>
        /// <remarks>This ensures that cached translations are up-to-date with the current language.</remarks>
        private static void FlushTranslationCacheIfLanguageChanged()
        {
            LoadedLanguage active = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(active, translationCacheLanguage))
            {
                translationCache.Clear();
                tooltipLabelCache.Clear();
                translationCacheLanguage = active;
                translationEpoch++;
            }
        }

        #endregion
    }
}
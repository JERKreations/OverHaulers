using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-00] High-performance presentation utilities, zero-allocation translation cache,
    /// accessibility palette adapters, and centralized settings mutation sink.
    /// Resides under Source/Settings/Views/.
    /// </summary>
    public static class SettingsViewUtilities
    {
        #region 1. CONSTANTS & SHARED COLORS

        public const float SectionPadding = 20f;
        public const float HeaderHeight = 38f;
        public const float ControlRowHeight = 28f;
        public const float GroupGap = 16f;

        public static readonly Color DescriptionTextColor = new Color(0.63f, 0.65f, 0.67f);
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
            Settings.ValuesMutated += OnSettingMutated;
        }

        /// <summary>
        /// Centralized notification sink triggered whenever any slider, toggle, or setting mutates.
        /// Synchronizes core configurations, invalidates biological mass caches, and marks presentation registries dirty.
        /// </summary>
        public static void OnSettingMutated()
        {
            OverHaulers.settings?.PushConfigurationToCore();
            TestBench.MarkDirty();
            TopologyLayoutCompiler.InvalidateAllTopologies();
            PawnDataRegistry.MarkAllStale();
            DetailedModelRegistry.Clear();
        }

        #endregion

        #region 3. DYNAMIC TEXT MEASUREMENT HELPERS

        /// <summary>
        /// Calculates the height required to render text within the specified width and font.
        /// </summary>
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

        #region 4. ACCESSIBILITY PALETTE ADAPTERS

        /// <summary>
        /// Returns the currently active accessibility color preset type.
        /// </summary>
        public static PalettePreset ActivePreset
        {
            get
            {
                return OverHaulers.settings != null
                    ? OverHaulers.settings.activePalettePreset
                    : PresentationDefaults.DefaultPalettePreset;
            }
        }

        /// <summary>
        /// Updates the active RGB status colors from the current settings profile.
        /// </summary>
        public static void SetCurrentColors(out Color critical, out Color healthy, out Color boosted)
        {
            if (OverHaulers.settings != null)
            {
                critical = OverHaulers.settings.colorCritical;
                healthy = OverHaulers.settings.colorHealthy;
                boosted = OverHaulers.settings.colorBoosted;
            }
            else
            {
                critical = PresentationDefaults.ColorCriticalDefault;
                healthy = PresentationDefaults.ColorHealthyDefault;
                boosted = PresentationDefaults.ColorBoostedDefault;
            }
        }

        #endregion

        #region 5. ZERO-ALLOCATION CACHED TRANSLATION LOOKUPS

        private static readonly Dictionary<string, string> translationCache = new Dictionary<string, string>(128);
        private static LoadedLanguage translationCacheLanguage;
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
        /// Returns the localized text for a Keyed translation key, cached with zero GC allocation.
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

        public static void ClearLocalizationCache()
        {
            translationCache.Clear();
            translationEpoch++;
        }

        private static void FlushTranslationCacheIfLanguageChanged()
        {
            LoadedLanguage active = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(active, translationCacheLanguage))
            {
                translationCache.Clear();
                translationCacheLanguage = active;
                translationEpoch++;
            }
        }

        #endregion
    }
}
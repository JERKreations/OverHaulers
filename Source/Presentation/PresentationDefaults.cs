using UnityEngine;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-10] Colour palette presets selectable in the accessibility settings.
    /// </summary>
    public enum PalettePreset
    {
        Default = 0,
        Deuteranopia = 1,
        Tritanopia = 2,
        HighContrast = 3,
        Custom = 4
    }

    /// <summary>
    /// [SEC-10] PRESENTATION-OWNED DEFAULTS
    /// Single source of truth for the default colour palettes, icon scale and report verbosity consumed by the
    /// Presentation layer. Resides under Source/Presentation/ and has zero Settings dependencies; the Settings layer
    /// reads these values downward for its persisted fields.
    /// </summary>
    public static class PresentationDefaults
    {
        #region 1. [SEC-10] ICON SCALE & REPORT VERBOSITY

        public const float IconScalePercent = 0.70f;
        public const float IconScalePercentMin = 0.50f;
        public const float IconScalePercentMax = 1.20f;

        // When true, InfoCard instances are forced to display all grouping details regardless of part states and health conditions.
        public const bool VerboseBreakdown = false;
        public const bool ProstheticColorOverride = false;
        public const bool CustomProstheticPalette = false;

        #endregion

        #region 2. [SEC-10] COLOUR PALETTES

        public const PalettePreset DefaultPalettePreset = PalettePreset.Default;

        // Default colour palette.
        public static readonly Color ColorHealthyDefault = new Color(0.90f, 0.90f, 0.90f);
        public static readonly Color ColorCriticalDefault = new Color(0.90f, 0.33f, 0.33f);
        public static readonly Color ColorBoostedDefault = new Color(0.40f, 0.82f, 1.00f);

        // Deuteranopia colour palette.
        public static readonly Color ColorHealthyDeuteranopia = new Color(0.35f, 0.70f, 0.90f);
        public static readonly Color ColorCriticalDeuteranopia = new Color(0.95f, 0.65f, 0.15f);
        public static readonly Color ColorBoostedDeuteranopia = new Color(0.85f, 0.40f, 0.85f);

        // Tritanopia colour palette.
        public static readonly Color ColorHealthyTritanopia = new Color(0.20f, 0.75f, 0.65f);
        public static readonly Color ColorCriticalTritanopia = new Color(0.90f, 0.25f, 0.40f);
        public static readonly Color ColorBoostedTritanopia = new Color(0.65f, 0.40f, 0.90f);

        // High contrast colour palette.
        public static readonly Color ColorHealthyHighContrast = new Color(1.00f, 1.00f, 1.00f);
        public static readonly Color ColorCriticalHighContrast = new Color(0.45f, 0.45f, 0.45f);
        public static readonly Color ColorBoostedHighContrast = new Color(1.00f, 0.85f, 0.20f);

        #endregion
    }
}

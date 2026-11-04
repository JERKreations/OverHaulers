using UnityEngine;
using Verse;

namespace OverHaulers
{
    public partial class Settings
    {
        #region 1. [SEC-10] ACCESSIBILITY & COLOR FIELDS

        public float iconScalePercent = PresentationDefaults.IconScalePercent;
        public PalettePreset activePalettePreset = PresentationDefaults.DefaultPalettePreset;

        public Color colorHealthy = PresentationDefaults.ColorHealthyDefault;
        public Color colorCritical = PresentationDefaults.ColorCriticalDefault;
        public Color colorBoosted = PresentationDefaults.ColorBoostedDefault;

        #endregion

        #region 2. [SEC-10] PALETTE APPLICATION & RESET

        /// <summary>
        /// Applies the specified color palette preset to the accessibility and color settings.
        /// </summary>
        /// <param name="preset">The color palette preset to apply.</param>
        public void ApplyPreset(PalettePreset preset)
        {
            activePalettePreset = preset;
            switch (preset)
            {
                case PalettePreset.Default:
                    colorHealthy = PresentationDefaults.ColorHealthyDefault;
                    colorCritical = PresentationDefaults.ColorCriticalDefault;
                    colorBoosted = PresentationDefaults.ColorBoostedDefault;
                    break;
                case PalettePreset.Deuteranopia:
                    colorHealthy = PresentationDefaults.ColorHealthyDeuteranopia;
                    colorCritical = PresentationDefaults.ColorCriticalDeuteranopia;
                    colorBoosted = PresentationDefaults.ColorBoostedDeuteranopia;
                    break;
                case PalettePreset.Tritanopia:
                    colorHealthy = PresentationDefaults.ColorHealthyTritanopia;
                    colorCritical = PresentationDefaults.ColorCriticalTritanopia;
                    colorBoosted = PresentationDefaults.ColorBoostedTritanopia;
                    break;
                case PalettePreset.HighContrast:
                    colorHealthy = PresentationDefaults.ColorHealthyHighContrast;
                    colorCritical = PresentationDefaults.ColorCriticalHighContrast;
                    colorBoosted = PresentationDefaults.ColorBoostedHighContrast;
                    break;
                case PalettePreset.Custom:
                    break;
            }

            NotifyValuesReset();
            NotifyValuesMutated();
        }

        /// <summary>
        /// Resets the accessibility settings to their default values, including the icon scale and color palette.
        /// </summary>
        public void ResetAccessibility()
        {
            iconScalePercent = PresentationDefaults.IconScalePercent;
            ApplyPreset(PresentationDefaults.DefaultPalettePreset);
        }

        #endregion

        #region 3. [SEC-10] SERIALIZATION & SANITISATION

        private void ExposeAccessibility()
        {
            Scribe_Values.Look(ref iconScalePercent, "iconScalePercent", PresentationDefaults.IconScalePercent);
            Scribe_Values.Look(ref activePalettePreset, "activePalettePreset", PresentationDefaults.DefaultPalettePreset);
            Scribe_Values.Look(ref colorHealthy, "colorHealthy", PresentationDefaults.ColorHealthyDefault);
            Scribe_Values.Look(ref colorCritical, "colorCritical", PresentationDefaults.ColorCriticalDefault);
            Scribe_Values.Look(ref colorBoosted, "colorBoosted", PresentationDefaults.ColorBoostedDefault);
        }

        private void SanitizeAccessibility()
        {
            if (!System.Enum.IsDefined(typeof(PalettePreset), activePalettePreset)) activePalettePreset = PresentationDefaults.DefaultPalettePreset;

            iconScalePercent = ConfigBounds.ClampFloatUntracked(iconScalePercent, PresentationDefaults.IconScalePercent, PresentationDefaults.IconScalePercentMin, PresentationDefaults.IconScalePercentMax);
            colorHealthy = SaneColor(colorHealthy, PresentationDefaults.ColorHealthyDefault);
            colorCritical = SaneColor(colorCritical, PresentationDefaults.ColorCriticalDefault);
            colorBoosted = SaneColor(colorBoosted, PresentationDefaults.ColorBoostedDefault);
        }

        #endregion
    }
}

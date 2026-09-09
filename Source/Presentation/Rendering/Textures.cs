using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Static texture registry holding custom bundled UI icons and vanilla fallback handles.
    /// Employs dual-version (1.5 / 1.6) safe texture resolution to prevent missing icon handles across game builds.
    /// Target: Caravan Mass Capacity InfoCard breakdown icons ([P], [A], [I]).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Textures
    {
        #region 1. ICON HANDLES & LAZY RESOLUTION

        private static Texture2D prostheticIcon;
        private static Texture2D athleticIcon;
        private static Texture2D bandageIcon;

        /// <summary>UI icon for prosthetics/bionics ([P]).</summary>
        public static Texture2D ProstheticIcon => prostheticIcon ?? (prostheticIcon = ResolveProstheticIcon());

        /// <summary>UI icon for athletics/stamina ([A]).</summary>
        public static Texture2D AthleticIcon => athleticIcon ?? (athleticIcon = ResolveAthleticIcon());

        /// <summary>UI icon for injuries/deficits ([I]).</summary>
        public static Texture2D BandageIcon => bandageIcon ?? (bandageIcon = ResolveBandageIcon());

        private static Texture2D ResolveProstheticIcon()
        {
            try
            {
                // 1. Try bundled mod texture
                Texture2D tex = ContentFinder<Texture2D>.Get("UI/Icons/Prosthetic", false);
                if (tex != null) return tex;

                // 2. Try 1.6 / 1.5 vanilla medical prosthetic icon
                tex = ContentFinder<Texture2D>.Get("UI/Icons/Medical/Prosthetics", false);
                if (tex != null) return tex;

                // 3. Fallback to industrial component icon
                ThingDef componentDef = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentIndustrial");
                if (componentDef?.uiIcon != null) return componentDef.uiIcon;
            }
            catch { }

            return BaseContent.WhiteTex;
        }

        private static Texture2D ResolveAthleticIcon()
        {
            try
            {
                // 1. Try bundled mod texture
                Texture2D tex = ContentFinder<Texture2D>.Get("UI/Icons/Athletics", false);
                if (tex != null) return tex;

                // 2. Try vanilla hostility flee boot icon (Works in both 1.5 and 1.6)
                tex = ContentFinder<Texture2D>.Get("UI/Icons/HostilityResponse/Flee", false);
                if (tex != null) return tex;
            }
            catch { }

            return BaseContent.WhiteTex;
        }

        private static Texture2D ResolveBandageIcon()
        {
            try
            {
                // 1. Try bundled mod texture
                Texture2D tex = ContentFinder<Texture2D>.Get("UI/Icons/Injury", false);
                if (tex != null) return tex;

                // 2. Try vanilla medical bandage icons
                tex = ContentFinder<Texture2D>.Get("UI/Icons/Medical/BandageWell", false);
                if (tex != null) return tex;

                tex = ContentFinder<Texture2D>.Get("UI/Icons/Medical/Bleeding", false);
                if (tex != null) return tex;
            }
            catch { }

            return BaseContent.WhiteTex;
        }

        #endregion
    }
}
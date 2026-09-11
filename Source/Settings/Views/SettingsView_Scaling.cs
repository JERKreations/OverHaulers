using System;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Provides the structural scaling constants settings view for the OverHaulers mod, including prosthetic and athletic impact constants.
    /// </summary>
    public static partial class SettingsView
    {
        #region [SEC-07] STRUCTURAL SCALING CONSTANTS DRAWER

        /// <summary>
        /// Draws the structural scaling constants settings section, including prosthetic and athletic impact constants.
        /// </summary>
        /// <param name="viewRect">The rectangle defining the area in which to draw the scaling constants section.</param>
        /// <param name="currentY">The current vertical position within the view, updated as elements are drawn.</param>
        /// <param name="settings">The settings object containing the structural scaling constants configuration.</param>
        /// <param name="resetAction">The action to invoke when the reset button is clicked.</param>
        public static void DrawScalingConstants(Rect viewRect, ref float currentY, Settings settings, Action resetAction)
        {
            Rect inner = SettingsViewUtilities.BeginSectionBoxDirect(viewRect, "SEC_ScalingConstants", ref currentY, fallbackEstimate: 140f);
            float startY = inner.y;
            float localY = inner.y;

            try
            {
                string scalingDesc = "OverHaulers_ScalingConstants_Desc".Translate().ToString();

                bool isResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(inner, "OverHaulers_ScalingConstantsHeader", resetAction, localY, out localY);
                localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, scalingDesc, localY);

                // Row 1: Bionic Upgrade Constant
                localY = SettingsViewUtilities.DrawSettingRowDirect(
                    inner,
                    "OverHaulers_ProstheticImpactConstant".Translate(settings.prostheticImpactConstant.ToString("F1")).ToString(),
                    ref settings.prostheticImpactConstant,
                    SettingsDefaults.ProstheticImpactConstantMin, SettingsDefaults.ProstheticImpactConstantMax,
                    localY,
                    "OverHaulers_ProstheticImpactConstant_Tooltip".Translate().ToString(),
                    highlight: isResetHovered
                );

                // Row 2: Athletic Synergy Constant
                localY = SettingsViewUtilities.DrawSettingRowDirect(
                    inner,
                    "OverHaulers_AthleticImpactConstant".Translate(settings.athleticImpactConstant.ToString("F1")).ToString(),
                    ref settings.athleticImpactConstant,
                    SettingsDefaults.AthleticImpactConstantMin, SettingsDefaults.AthleticImpactConstantMax,
                    localY,
                    "OverHaulers_AthleticImpactConstant_Tooltip".Translate().ToString(),
                    highlight: isResetHovered
                );
            }
            finally
            {
                SettingsViewUtilities.EndSectionBoxDirect("SEC_ScalingConstants", startY, localY, ref currentY);
            }
        }

        #endregion
    }
}
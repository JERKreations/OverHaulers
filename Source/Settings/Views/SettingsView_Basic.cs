using System;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    public static partial class SettingsView
    {
        #region [SEC-01] MAIN SCALAR SETTINGS & SAFETY FLOORS

        /// <summary>
        /// [SEC-01] Draws the main scalar modifiers for prosthetics and athletics, alongside the unified
        /// Physical Deficits & Maximum Impact Floor composite row.
        /// </summary>
        public static void DrawBasicModifiers(Rect viewRect, ref float currentY, Settings settings, Action resetAction)
        {
            Rect inner = SettingsViewUtilities.BeginSectionBoxDirect(viewRect, "SEC_Globals", ref currentY, fallbackEstimate: 260f);
            float startY = inner.y;
            float localY = inner.y;

            try
            {
                bool isResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(inner, "OverHaulers_GlobalModifiersHeader", resetAction, localY, out localY);

                // Row 1: Prosthetics (Hardware Boosts)
                localY = SettingsViewUtilities.DrawToggleSettingRowWithDescriptionDirect(
                    inner,
                    ref settings.enableProsthetics,
                    "OverHaulers_ProstheticTarget".Translate().ToString(),
                    ref settings.prostheticScaling,
                    SettingsDefaults.ProstheticScalingMin, SettingsDefaults.ProstheticScalingMax,
                    "OverHaulers_ProstheticScaling_Desc".Translate().ToString(),
                    localY,
                    highlight: isResetHovered
                );

                // Row 2: Athletics (Systemic Stamina Boosts)
                localY = SettingsViewUtilities.DrawToggleSettingRowWithDescriptionDirect(
                    inner,
                    ref settings.enableAthletics,
                    "OverHaulers_AthleticTarget".Translate().ToString(),
                    ref settings.athleticScaling,
                    SettingsDefaults.AthleticScalingMin, SettingsDefaults.AthleticScalingMax,
                    "OverHaulers_AthleticScaling_Desc".Translate().ToString(),
                    localY,
                    highlight: isResetHovered
                );

                // Row 3: Physical Deficits & Maximum Impact Floor (Unified Composite Row)
                bool prevInjuryToggle = settings.enablePartHealth;
                bool floorIsAtMax = settings.massCapacityFloor >= 1.00f - SettingsDefaults.EfficiencyEpsilon;

                string floorReadout = "OverHaulers_DeficitsFloorReadout".Translate(settings.massCapacityFloor.ToStringPercent()).ToString();

                localY = SettingsViewUtilities.DrawToggleSettingRowWithDescriptionDirect(
                    inner,
                    ref settings.enablePartHealth,
                    "OverHaulers_DeficitsTarget".Translate().ToString(),
                    ref settings.massCapacityFloor,
                    SettingsDefaults.MassCapacityFloorMin, SettingsDefaults.MassCapacityFloorMax,
                    "OverHaulers_DeficitsDesc".Translate().ToString(),
                    localY,
                    highlight: isResetHovered,
                    customValueLabel: floorReadout
                );

                // Safety Floor synchronizations
                if (prevInjuryToggle && !settings.enablePartHealth)
                {
                    settings.compatibilitySafetyFloor = false;
                }

                if (!prevInjuryToggle && settings.enablePartHealth && floorIsAtMax)
                {
                    settings.massCapacityFloor = 0.95f;
                }

                if (settings.massCapacityFloor >= 1.00f - SettingsDefaults.EfficiencyEpsilon && settings.enablePartHealth)
                {
                    settings.enablePartHealth = false;
                    settings.compatibilitySafetyFloor = false;
                }

                // Row 4: Global 0.01kg Safety Floor Checkbox
                localY = SettingsViewUtilities.DrawCheckboxRowDirect(
                    inner,
                    "OverHaulers_EnableGlobalSafetyFloor".Translate().ToString(),
                    ref settings.compatibilitySafetyFloor,
                    localY,
                    "OverHaulers_EnableGlobalSafetyFloor_Tooltip".Translate().ToString(),
                    isResetHovered
                );
            }
            finally
            {
                SettingsViewUtilities.EndSectionBoxDirect("SEC_Globals", startY, localY, ref currentY);
            }
        }

        #endregion
    }
}
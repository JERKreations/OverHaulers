using System;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    public static partial class SettingsView
    {
        #region [SEC-09] DIAGNOSTICS & TELEMETRY DRAWER

        public static void DrawDiagnosticsSection(Rect viewRect, ref float currentY, Settings settings, Action resetAction)
        {
            Rect inner = SettingsViewUtilities.BeginSectionBoxDirect(viewRect, "SEC_Diagnostics", ref currentY, fallbackEstimate: 260f);
            float startY = inner.y;
            float localY = inner.y;

            try
            {
                bool isResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(inner, "OverHaulers_DiagnosticsHeader", resetAction, localY, out localY);

                // Row 1: Verbose Breakdown
                localY = SettingsViewUtilities.DrawCheckboxRowDirect(
                    inner,
                    "OverHaulers_VerboseBreakdown".Translate().ToString(),
                    ref settings.verboseBreakdown,
                    localY,
                    "OverHaulers_VerboseBreakdown_Tooltip".Translate().ToString(),
                    isResetHovered
                );

                // Row 2: Sliders
                Rect row2Rect = new Rect(inner.x, localY, inner.width, 24f);
                if (isResetHovered) Widgets.DrawBoxSolid(row2Rect.ExpandedBy(2f, 1f), SettingsViewUtilities.DarkTargetHighlightColor);

                float halfWidth = (row2Rect.width / 2f) - 8f;
                Rect left2Rect = new Rect(row2Rect.x, row2Rect.y, halfWidth, row2Rect.height);
                Rect right2Rect = new Rect(row2Rect.x + halfWidth + 16f, row2Rect.y, halfWidth, row2Rect.height);

                string reportIntervalLabel = settings.reportMetricsIntervalHours > 0 
                    ? "OverHaulers_ReportMetricsInterval".Translate(settings.reportMetricsIntervalHours).ToString() 
                    : "OverHaulers_ReportMetricsIntervalDisabled".Translate().ToString();

                SettingsViewUtilities.DrawSliderAndInputRowDirect(
                    left2Rect,
                    reportIntervalLabel,
                    ref settings.reportMetricsIntervalHours,
                    SettingsDefaults.ReportMetricsIntervalHoursMin, SettingsDefaults.ReportMetricsIntervalHoursMax,
                    tooltip: "OverHaulers_ReportMetricsInterval_Tooltip".Translate().ToString()
                );

                string evictionTooltip = "OverHaulers_CacheInactivityEvictionHours_Tooltip".Translate(
                    GenDate.TicksPerHour, 
                    GenDate.HoursPerDay
                ).ToString();

                SettingsViewUtilities.DrawSliderAndInputRowDirect(
                    right2Rect,
                    "OverHaulers_CacheInactivityEvictionHours".Translate(settings.pawnEvictionTimeframeHours).ToString(),
                    ref settings.pawnEvictionTimeframeHours,
                    SettingsDefaults.PawnEvictionTimeframeHoursMin, SettingsDefaults.PawnEvictionTimeframeHoursMax,
                    evictionTooltip
                );
                localY += 28f;

                // Sub-Section Toggles
                bool metricsEnabled = settings.reportMetricsIntervalHours > 0;
                bool originalEnabledState = GUI.enabled;

                try
                {
                    GUI.enabled = originalEnabledState && metricsEnabled;

                    Rect subHeaderRect = new Rect(inner.x, localY, inner.width, 20f);
                    Text.Font = GameFont.Tiny;
                    GUI.color = metricsEnabled ? SettingsViewUtilities.DescriptionTextColor : SettingsViewUtilities.DescriptionTextColor * 0.5f;
                    Widgets.Label(subHeaderRect, "OverHaulers_ReportContentHeader".Translate().ToString());
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    localY += 24f;

                    // 2-Column Checkbox Grid
                    localY = SettingsViewUtilities.Draw2x2CheckboxGridRowDirect(
                        inner,
                        "OverHaulers_LogQueryMetrics".Translate().ToString(), ref settings.logQueryMetrics, "OverHaulers_LogQueryMetrics_Tooltip".Translate().ToString(),
                        "OverHaulers_LogCacheMetrics".Translate().ToString(), ref settings.logCacheMetrics, "OverHaulers_LogCacheMetrics_Tooltip".Translate().ToString(),
                        localY, isResetHovered
                    );

                    localY = SettingsViewUtilities.Draw2x2CheckboxGridRowDirect(
                        inner,
                        "OverHaulers_LogLifecycleMetrics".Translate().ToString(), ref settings.logLifecycleMetrics, "OverHaulers_LogLifecycleMetrics_Tooltip".Translate().ToString(),
                        "OverHaulers_LogWorkspaceMetrics".Translate().ToString(), ref settings.logWorkspaceMetrics, "OverHaulers_LogWorkspaceMetrics_Tooltip".Translate().ToString(),
                        localY, isResetHovered
                    );

                    localY = SettingsViewUtilities.Draw2x2CheckboxGridRowDirect(
                        inner,
                        "OverHaulers_LogSafetyFloorClamps".Translate().ToString(), ref settings.logSafetyFloorClamps, "OverHaulers_LogSafetyFloorClamps_Tooltip".Translate().ToString(),
                        "OverHaulers_LogPawnEvictions".Translate().ToString(), ref settings.logPawnEvictions, "OverHaulers_LogPawnEvictions_Tooltip".Translate().ToString(),
                        localY, isResetHovered
                    );
                }
                finally
                {
                    GUI.enabled = originalEnabledState;
                }
            }
            finally
            {
                SettingsViewUtilities.EndSectionBoxDirect("SEC_Diagnostics", startY, localY, ref currentY);
            }
        }

        #endregion
    }
}
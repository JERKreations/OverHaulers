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
            Rect inner = SettingsViewUtilities.BeginSectionBoxDirect(viewRect, "SEC_Diagnostics", ref currentY, fallbackEstimate: 600f);
            float startY = inner.y;
            float localY = inner.y;

            try
            {
                bool isResetHovered = SettingsViewUtilities.DrawHeaderWithResetDirect(inner, "OverHaulers_DiagnosticsHeader", resetAction, localY, out localY);

                // Row 1: Verbose Breakdown
                localY = SettingsViewUtilities.DrawCheckboxRowWithDescriptionDirect(
                    inner,
                    "OverHaulers_VerboseBreakdown".Translate().ToString(),
                    ref settings.verboseBreakdown,
                    "OverHaulers_VerboseBreakdown_Desc".Translate().ToString(),
                    localY,
                    highlight: isResetHovered
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
                localY += 34f;

                // =========================================================================
                // SUB-SECTION HEADER & DESCRIPTION
                // =========================================================================
                bool metricsEnabled = settings.reportMetricsIntervalHours > 0;

                Rect subHeaderRect = new Rect(inner.x, localY, inner.width, 22f);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                Widgets.Label(subHeaderRect, "OverHaulers_ReportContentHeader".Translate().ToString());
                localY += 24f;

                // Explanatory in-line description explaining the gating behavior
                localY = SettingsViewUtilities.DrawSectionDescriptionDirect(
                    inner,
                    "OverHaulers_ReportContent_Desc".Translate().ToString(),
                    localY
                );
                localY += 4f;

                // =========================================================================
                // GATED CONTENT TOGGLES (Greyed out when Interval is 0, active when > 0)
                // =========================================================================
                bool originalEnabledState = GUI.enabled;

                try
                {
                    GUI.enabled = originalEnabledState && metricsEnabled;

                    // 1. Total Registry Queries
                    localY = SettingsViewUtilities.DrawCheckboxRowWithDescriptionDirect(
                        inner,
                        "OverHaulers_LogQueryMetrics".Translate().ToString(),
                        ref settings.logQueryMetrics,
                        "OverHaulers_LogQueryMetrics_Desc".Translate().ToString(),
                        localY, highlight: isResetHovered
                    );

                    // 2. Cache Hit Efficiency
                    localY = SettingsViewUtilities.DrawCheckboxRowWithDescriptionDirect(
                        inner,
                        "OverHaulers_LogCacheMetrics".Translate().ToString(),
                        ref settings.logCacheMetrics,
                        "OverHaulers_LogCacheMetrics_Desc".Translate().ToString(),
                        localY, highlight: isResetHovered
                    );

                    // 3. Snapshot Table & Probing Health
                    localY = SettingsViewUtilities.DrawCheckboxRowWithDescriptionDirect(
                        inner,
                        "OverHaulers_LogSnapshotTableMetrics".Translate().ToString(),
                        ref settings.logSnapshotTableMetrics,
                        "OverHaulers_LogSnapshotTableMetrics_Desc".Translate().ToString(),
                        localY, highlight: isResetHovered
                    );

                    // 4. Lifecycle & Debounce Events
                    localY = SettingsViewUtilities.DrawCheckboxRowWithDescriptionDirect(
                        inner,
                        "OverHaulers_LogLifecycleMetrics".Translate().ToString(),
                        ref settings.logLifecycleMetrics,
                        "OverHaulers_LogLifecycleMetrics_Desc".Translate().ToString(),
                        localY, highlight: isResetHovered
                    );

                    // 5. Pooled Workspaces
                    localY = SettingsViewUtilities.DrawCheckboxRowWithDescriptionDirect(
                        inner,
                        "OverHaulers_LogWorkspaceMetrics".Translate().ToString(),
                        ref settings.logWorkspaceMetrics,
                        "OverHaulers_LogWorkspaceMetrics_Desc".Translate().ToString(),
                        localY, highlight: isResetHovered
                    );

                    // 6. Safety Floor Clamps
                    localY = SettingsViewUtilities.DrawCheckboxRowWithDescriptionDirect(
                        inner,
                        "OverHaulers_LogSafetyFloorClamps".Translate().ToString(),
                        ref settings.logSafetyFloorClamps,
                        "OverHaulers_LogSafetyFloorClamps_Desc".Translate().ToString(),
                        localY, highlight: isResetHovered
                    );

                    // 7. Evicted Pawn IDs
                    localY = SettingsViewUtilities.DrawCheckboxRowWithDescriptionDirect(
                        inner,
                        "OverHaulers_LogPawnEvictions".Translate().ToString(),
                        ref settings.logPawnEvictions,
                        "OverHaulers_LogPawnEvictions_Desc".Translate().ToString(),
                        localY, highlight: isResetHovered
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
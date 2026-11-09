using System;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TOOL-00] High-level floating workspace dialog hosting developer tooling, interactive
    /// anatomical simulations (Test Bench), synthetic performance benchmarks, automated regression suites,
    /// and file export utilities. Decoupled from the primary player settings menu.
    /// Resides under Source/Settings/Tooling/.
    /// </summary>
    public partial class ToolingDialog : Window
    {
        #region 1. ENUMS & CONSTANTS

        public enum DevTab : byte
        {
            TestBench = 0,
            BenchmarkAndSelfCheck = 1,
            DumpsAndTelemetry = 2
        }

        private const float HeaderHeight = 36f;
        private const float TabBarHeight = 32f;
        private const float TabSpacing = 8f;
        private const float TabButtonWidth = 220f;
        private const float BottomMargin = 10f;

        private static readonly Vector2 DefaultWindowSize = new Vector2(1024f, 768f); // Vector2(1280f, 720f);
        private static readonly Color TabActiveColor = new Color(0.92f, 0.94f, 0.96f);
        private static readonly Color TabInactiveColor = new Color(0.65f, 0.67f, 0.70f);
        private static readonly Color DevAccentColor = new Color(0.70f, 0.85f, 1.0f);

        #endregion

        #region 2. STATIC BUFFERS & STATE

        private static DevTab activeTab = DevTab.TestBench;
        private static Vector2 testBenchScrollPos = Vector2.zero;
        private static Vector2 benchmarkScrollPos = Vector2.zero;
        private static Vector2 dumpsScrollPos = Vector2.zero;

        // Dynamic scroll content heights
        private static float lastTestBenchHeight = 1000f;
        private static float lastBenchmarkHeight = 1400f;
        private static float lastDumpsHeight = 800f;

        #endregion

        #region 3. INSTANCE FIELDS & PROPERTIES

        private readonly Settings settings;

        public override Vector2 InitialSize
        {
            get { return DefaultWindowSize; }
        }

        #endregion

        #region 4. LIFECYCLE & CONSTRUCTORS

        public ToolingDialog(Settings settings)
        {
            this.settings = settings;

            // Window configuration
            forcePause = true;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();

            // Ensure test subjects, medical catalogs, and species baselines are primed
            TestBench.EnsureInitialized();
        }

        public override void PostClose()
        {
            base.PostClose();

            // Flush temporary sandbox allocations and mark test bench dirty
            TestBench.MarkDirty();
            DetailedModelRegistry.Clear();
        }

        #endregion

        #region 5. WINDOW CONTENTS (CANONICAL ENTRY POINT)

        public override void DoWindowContents(Rect inRect)
        {
            SettingsLayout.ResetControlIndex();

            float curY = inRect.y;

            // 1. Window Header
            DrawHeader(inRect, ref curY);

            // 2. Navigation Tab Bar
            DrawTabBar(inRect, ref curY);

            // 3. Tab Canvas Area
            float canvasHeight = inRect.yMax - curY - BottomMargin;
            Rect canvasRect = new Rect(inRect.x, curY, inRect.width, canvasHeight);

            switch (activeTab)
            {
                case DevTab.TestBench:
                    DrawTestBenchTab(canvasRect);
                    break;
                case DevTab.BenchmarkAndSelfCheck:
                    DrawBenchmarkAndSelfCheckTab(canvasRect);
                    break;
                case DevTab.DumpsAndTelemetry:
                    DrawDumpsAndTelemetryTab(canvasRect);
                    break;
            }
        }

        #endregion

        #region 6. SUB-ROUTINES: HEADER & NAVIGATION

        private void DrawHeader(Rect inRect, ref float curY)
        {
            Rect headerRect = new Rect(inRect.x, curY, inRect.width, HeaderHeight);

            // Left: Title
            Text.Font = GameFont.Medium;
            GUI.color = DevAccentColor;
            string titleText = "OverHaulers_ToolingDialog_WindowTitle".Translate().ToString();
            Widgets.Label(new Rect(headerRect.x, headerRect.y, 400f, 32f), titleText);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Right: Global Recompile Button
            float buttonWidth = 140f;
            Rect recompileRect = new Rect(headerRect.xMax - buttonWidth - 40f, headerRect.y, buttonWidth, 28f);
            if (Widgets.ButtonText(recompileRect, "OverHaulers_ToolingDialog_RecompileData".Translate().ToString()))
            {
                TestBench.RecompileAll();
            }
            TooltipHandler.TipRegion(recompileRect, "OverHaulers_ToolingDialog_RecompileData_Tooltip".Translate().ToString());

            // Right: Open Dumps Directory Button
            Rect folderRect = new Rect(recompileRect.x - buttonWidth - 8f, headerRect.y, buttonWidth, 28f);
            if (Widgets.ButtonText(folderRect, "OverHaulers_ToolingDialog_OpenDumpsFolder".Translate().ToString()))
            {
                DiagnosticExportUtility.OpenExportDirectory();
            }
            TooltipHandler.TipRegion(folderRect, "OverHaulers_ToolingDialog_OpenDumpsFolder_Tooltip".Translate().ToString());

            curY += HeaderHeight + 4f;

            // Subtle divider rule under title row
            GUI.color = SettingsLayout.DividerLineColor;
            Widgets.DrawLineHorizontal(inRect.x, curY, inRect.width);
            GUI.color = Color.white;
            curY += 8f;
        }

        private void DrawTabBar(Rect inRect, ref float curY)
        {
            Rect barRect = new Rect(inRect.x, curY, inRect.width, TabBarHeight);
            float tabX = barRect.x;

            DrawSingleTab(ref tabX, barRect.y, DevTab.TestBench, "OverHaulers_ToolingDialog_Tab_TestBench".Translate().ToString());
            DrawSingleTab(ref tabX, barRect.y, DevTab.BenchmarkAndSelfCheck, "OverHaulers_ToolingDialog_Tab_Benchmark".Translate().ToString());
            DrawSingleTab(ref tabX, barRect.y, DevTab.DumpsAndTelemetry, "OverHaulers_ToolingDialog_Tab_Telemetry".Translate().ToString());

            curY += TabBarHeight + 10f;
        }

        private void DrawSingleTab(ref float tabX, float curY, DevTab tab, string label)
        {
            Rect btnRect = new Rect(tabX, curY, TabButtonWidth, TabBarHeight - 2f);
            bool isActive = (activeTab == tab);

            Color origColor = GUI.color;
            try
            {
                GUI.color = isActive ? TabActiveColor : TabInactiveColor;
                if (Widgets.ButtonText(btnRect, label))
                {
                    activeTab = tab;
                }
            }
            finally
            {
                GUI.color = origColor;
            }

            tabX += TabButtonWidth + TabSpacing;
        }

        #endregion

        #region 7. TAB CANVAS IMPLEMENTATIONS

        private void DrawTestBenchTab(Rect canvasRect)
        {
            // Pinned Top Toolbar: Verbose Breakdown Toggle
            Rect topBarRect = new Rect(canvasRect.x, canvasRect.y, canvasRect.width, 24f);
            bool prevVerbose = settings.verboseBreakdown;
            Widgets.CheckboxLabeled(topBarRect, "OverHaulers_ToolingDialog_VerboseBreakdown".Translate().ToString(), ref settings.verboseBreakdown);
            TooltipHandler.TipRegion(topBarRect, "OverHaulers_ToolingDialog_VerboseBreakdown_Tooltip".Translate().ToString());

            if (prevVerbose != settings.verboseBreakdown)
            {
                DetailedModelRegistry.Clear();
                TestBench.MarkDirty();
                SettingsViewUtilities.OnSettingMutated();
            }

            float scrollY = canvasRect.y + 28f;
            Rect scrollCanvas = new Rect(canvasRect.x, scrollY, canvasRect.width, canvasRect.height - 28f);

            // Dynamic height measurement prevents spillage when expanding surgery accordions
            float targetHeight = Math.Max(lastTestBenchHeight, scrollCanvas.height);
            Rect viewRect = new Rect(0f, 0f, scrollCanvas.width - 24f, targetHeight);
            Widgets.BeginScrollView(scrollCanvas, ref testBenchScrollPos, viewRect);

            float currentY = 0f;
            DrawTestBenchSection(viewRect, ref currentY, settings, () => TestBench.ResetTestBench());

            Widgets.EndScrollView();
            lastTestBenchHeight = currentY + 40f;
        }

        private void DrawBenchmarkAndSelfCheckTab(Rect canvasRect)
        {
            float targetHeight = Math.Max(lastBenchmarkHeight, canvasRect.height);
            Rect viewRect = new Rect(0f, 0f, canvasRect.width - 24f, targetHeight);
            Widgets.BeginScrollView(canvasRect, ref benchmarkScrollPos, viewRect);

            float localY = 0f;
            localY = DrawBenchmarkBlock(viewRect, localY);
            localY = DrawSelfCheckBlock(viewRect, localY + 16f);

            Widgets.EndScrollView();
            lastBenchmarkHeight = localY + 40f;
        }

        private void DrawDumpsAndTelemetryTab(Rect canvasRect)
        {
            float targetHeight = Math.Max(lastDumpsHeight, canvasRect.height);
            Rect viewRect = new Rect(0f, 0f, canvasRect.width - 24f, targetHeight);
            Widgets.BeginScrollView(canvasRect, ref dumpsScrollPos, viewRect);

            SettingsLayout ui = new SettingsLayout(viewRect);

            // Section 1: Periodic Telemetry Logging
            ui.HeaderKey("OverHaulers_ToolingDialog_Telemetry_SectionHeader");
            ui.DescriptionKey("OverHaulers_ToolingDialog_Telemetry_Desc");

            float interval = settings.reportMetricsIntervalHours;
            string intervalReadout = interval <= 0.001f
                ? "OverHaulers_ToolingDialog_Telemetry_ReportIntervalDisabled".Translate().ToString()
                : string.Format("{0:0}h", interval);
            string intervalLabel = interval <= 0.001f
                ? intervalReadout
                : "OverHaulers_ToolingDialog_Telemetry_ReportInterval".Translate(intervalReadout).ToString();
            if (ui.Slider(intervalLabel, ref interval, 0f, 24f, step: 1f))
            {
                settings.reportMetricsIntervalHours = (int)interval;
            }

            float eviction = settings.pawnEvictionTimeframeHours;
            string evictionReadout = string.Format("{0:0}h", eviction);
            string evictionLabel = "OverHaulers_ToolingDialog_Telemetry_CacheInactivityEvictionHours".Translate(evictionReadout).ToString();
            if (ui.Slider(evictionLabel, ref eviction, 1f, 72f, step: 1f))
            {
                settings.pawnEvictionTimeframeHours = (int)eviction;
            }

            ui.Gap(6f);
            ui.SubHeader(SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_Telemetry_ActiveMetricsHeader"));

            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogQueryMetrics", ref settings.logQueryMetrics, "OverHaulers_ToolingDialog_Telemetry_LogQueryMetrics_Desc");
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogCacheMetrics", ref settings.logCacheMetrics, "OverHaulers_ToolingDialog_Telemetry_LogCacheMetrics_Desc");
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogSnapshotTableMetrics", ref settings.logSnapshotTableMetrics, "OverHaulers_ToolingDialog_Telemetry_LogSnapshotTableMetrics_Desc");
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogPresentationMetrics", ref settings.logPresentationMetrics, "OverHaulers_ToolingDialog_Telemetry_LogPresentationMetrics_Desc");
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogLifecycleMetrics", ref settings.logLifecycleMetrics, "OverHaulers_ToolingDialog_Telemetry_LogLifecycleMetrics_Desc");
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogWorkspaceMetrics", ref settings.logWorkspaceMetrics, "OverHaulers_ToolingDialog_Telemetry_LogWorkspaceMetrics_Desc");
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogSafetyFloorClamps", ref settings.logSafetyFloorClamps, "OverHaulers_ToolingDialog_Telemetry_LogSafetyFloorClamps_Desc");
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogPawnEvictions", ref settings.logPawnEvictions, "OverHaulers_ToolingDialog_Telemetry_LogPawnEvictions_Desc");

            ui.Divider();

            // Section 2: High-Load Population Stress Testing
            ui.SubHeader(SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_Telemetry_StressTestingHeader"));
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_StressTestAllFauna", ref settings.devStressTestAllFauna, "OverHaulers_ToolingDialog_Telemetry_StressTestAllFauna_Desc");

            Widgets.EndScrollView();
            lastDumpsHeight = ui.Y + 40f;
        }

        #endregion
    }
}
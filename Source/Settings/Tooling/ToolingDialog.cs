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
            TelemetryAndMetrics = 2
        }

        private const float HeaderHeight = 36f;
        private const float TabBarHeight = 32f;
        private const float TabSpacing = SettingsViewUtilities.ControlGap;
        private const float TabButtonWidth = 220f;
        private const float BottomMargin = 10f;
        private const float ScrollContentBottomPadding = 40f;

        private static readonly Vector2 DefaultWindowSize = new Vector2(1024f, 768f);
        private static readonly Color TabActiveColor = new Color(0.92f, 0.94f, 0.96f);
        private static readonly Color TabInactiveColor = new Color(0.65f, 0.67f, 0.70f);
        private static readonly Color DevAccentColor = new Color(0.70f, 0.85f, 1.0f);

        #endregion

        #region 2. STATIC BUFFERS & STATE

        private static DevTab activeTab = DevTab.TestBench;
        private static Vector2 testBenchScrollPos = Vector2.zero;
        private static Vector2 benchmarkScrollPos = Vector2.zero;
        private static Vector2 telemetryScrollPos = Vector2.zero;

        // Dynamic scroll content heights
        private static float lastTestBenchHeight = 1000f;
        private static float lastBenchmarkHeight = 1400f;
        private static float lastTelemetryHeight = 800f;

        #endregion

        #region 3. INSTANCE FIELDS & PROPERTIES

        private readonly Settings settings;

        public override Vector2 InitialSize
        {
            get { return DefaultWindowSize; }
        }

        #endregion

        #region 4. LIFECYCLE & CONSTRUCTORS

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolingDialog"/> class with the specified settings.
        /// </summary>
        /// <param name="settings">The settings object used by the tooling dialog.</param>
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

        /// <summary>
        /// Called before the tooling dialog window is opened, allowing for any necessary setup.
        /// </summary>
        public override void PreOpen()
        {
            base.PreOpen();

            // Ensure test subjects, medical catalogs, and species baselines are primed
            TestBench.EnsureInitialized();
        }

        /// <summary>
        /// Called before the tooling dialog window is opened, ensuring necessary initializations.
        /// </summary>
        public override void PostClose()
        {
            base.PostClose();

            // Flush temporary sandbox allocations and mark test bench dirty
            SettingsLayout.ClearInputBuffers("Tooling");
            TestBench.MarkDirty();
        }

        #endregion

        #region 5. WINDOW CONTENTS (CANONICAL ENTRY POINT)

        /// <summary>
        /// Draws the contents of the tooling dialog window, including the header, navigation tab bar, and the active tab's content.
        /// </summary>
        /// <param name="inRect">The rectangle defining the drawing area for the window contents.</param>
        public override void DoWindowContents(Rect inRect)
        {
            SettingsLayout.ResetControlIndex("Tooling");

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
                case DevTab.TelemetryAndMetrics:
                    DrawTelemetryAndMetricsTab(canvasRect);
                    break;
            }
        }

        #endregion

        #region 6. SUB-ROUTINES: HEADER & NAVIGATION

        /// <summary>
        /// Draws the header section of the tooling dialog, including the title and action buttons.
        /// </summary>
        /// <param name="inRect">The rectangle defining the drawing area for the header.</param>
        /// <param name="curY">The current Y position for drawing within the header.</param>
        private void DrawHeader(Rect inRect, ref float curY)
        {
            float availableWidth = Math.Max(1f, inRect.width - 40f);
            float rightEdge = inRect.x + availableWidth;
            string titleText = SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_WindowTitle");
            string recompileText = SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_RecompileButton");
            string folderText = SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_DiagnosticsFolder");

            Text.Font = GameFont.Medium;
            float titleWidth = Math.Min(availableWidth, SettingsViewUtilities.MeasuredTextWidth(titleText));
            float titleHeight = Math.Max(32f, SettingsViewUtilities.MeasuredTextHeight(titleText, titleWidth));
            Text.Font = GameFont.Small;

            Vector2 folderSize = SettingsViewUtilities.ButtonSize(folderText, availableWidth, minWidth: 140f, minHeight: 28f);
            Vector2 recompileSize = SettingsViewUtilities.ButtonSize(recompileText, availableWidth, minWidth: 140f, minHeight: 28f);
            float folderWidth = folderSize.x;
            float recompileWidth = recompileSize.x;
            bool separateTitle = titleWidth + SettingsViewUtilities.ControlGap + folderWidth + SettingsViewUtilities.ControlGap + recompileWidth > availableWidth;
            float actionY = separateTitle ? curY + titleHeight + SettingsViewUtilities.CompactGap : curY;
            bool separateButtons = folderWidth + SettingsViewUtilities.ControlGap + recompileWidth > availableWidth;
            float folderHeight = folderSize.y;
            float recompileHeight = recompileSize.y;
            float recompileY = separateButtons ? actionY + folderHeight + SettingsViewUtilities.CompactGap : actionY;

            Text.Font = GameFont.Medium;
            GUI.color = DevAccentColor;
            Widgets.Label(new Rect(inRect.x, curY, titleWidth, titleHeight), titleText);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect recompileRect = new Rect(rightEdge - recompileWidth, recompileY, recompileWidth, recompileHeight);
            if (SettingsViewUtilities.DrawButton(recompileRect, recompileText,
                SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_RecompileButton_Tooltip")))
            {
                TestBench.RecompileAll();
            }

            Rect folderRect = new Rect(separateButtons ? rightEdge - folderWidth : recompileRect.x - SettingsViewUtilities.ControlGap - folderWidth, actionY, folderWidth, folderHeight);
            if (SettingsViewUtilities.DrawButton(folderRect, folderText,
                SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_DiagnosticsFolder_Tooltip")))
            {
                ExportUtilities.OpenExportDirectory();
            }

            curY = Math.Max(curY + Math.Max(HeaderHeight, titleHeight), Math.Max(folderRect.yMax, recompileRect.yMax)) + SettingsViewUtilities.CompactGap;

            // Subtle divider rule under title row
            GUI.color = SettingsLayout.DividerLineColor;
            Widgets.DrawLineHorizontal(inRect.x, curY, inRect.width);
            GUI.color = Color.white;
            curY += SettingsViewUtilities.ControlGap;
        }

        /// <summary>
        /// Draws the test bench tab, including the test bench section and its controls.
        /// </summary>
        /// <param name="inRect">The rectangle defining the drawing area for the tab.</param>
        /// <param name="curY">The current Y position for drawing within the tab.</param>
        private void DrawTabBar(Rect inRect, ref float curY)
        {
            float tabX = inRect.x;
            float tabY = curY;
            float rowHeight = 0f;

            DrawSingleTab(inRect, ref tabX, ref tabY, ref rowHeight, DevTab.TestBench, SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_Tab_TestBench"));
            DrawSingleTab(inRect, ref tabX, ref tabY, ref rowHeight, DevTab.BenchmarkAndSelfCheck, SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_Tab_Benchmark"));
            DrawSingleTab(inRect, ref tabX, ref tabY, ref rowHeight, DevTab.TelemetryAndMetrics, SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_Tab_Telemetry"));

            curY = tabY + rowHeight + 12f;
        }

        /// <summary>
        /// Draws the benchmark and self-check tab, including the benchmark block and the self-check block.
        /// </summary>
        /// <param name="canvasRect">The rectangle defining the drawing area for the tab.</param>
        private void DrawSingleTab(Rect bounds, ref float tabX, ref float tabY, ref float rowHeight, DevTab tab, string label)
        {
            Rect btnRect = SettingsViewUtilities.PlaceButton(bounds, ref tabX, ref tabY, ref rowHeight, label,
                TabSpacing, minWidth: TabButtonWidth, minHeight: TabBarHeight - 2f);
            bool isActive = (activeTab == tab);

            Color origColor = GUI.color;
            try
            {
                GUI.color = isActive ? TabActiveColor : TabInactiveColor;
                if (SettingsViewUtilities.DrawButton(btnRect, label))
                {
                    if (activeTab != tab)
                    {
                        activeTab = tab;
                        SettingsLayout.ClearInputBuffers("Tooling");
                        SettingsLayout.ResetControlIndex("Tooling");
                    }
                }
            }
            finally
            {
                GUI.color = origColor;
            }
        }

        #endregion

        #region 7. TAB CANVAS IMPLEMENTATIONS

        /// <summary>
        /// Draws the test bench tab, including the test bench section and its controls.
        /// </summary>
        /// <param name="canvasRect">The rectangle defining the drawing area for the tab.</param>
        private void DrawTestBenchTab(Rect canvasRect)
        {
            // Dynamic height measurement prevents spillage when expanding surgery accordions
            float targetHeight = Math.Max(lastTestBenchHeight, canvasRect.height);
            Rect viewRect = new Rect(0f, 0f, canvasRect.width - SettingsViewUtilities.ScrollbarGutter, targetHeight);
            Widgets.BeginScrollView(canvasRect, ref testBenchScrollPos, viewRect);

            float currentY = 0f;
            DrawTestBenchSection(viewRect, ref currentY, settings, () => TestBench.ResetTestBench());

            Widgets.EndScrollView();
            lastTestBenchHeight = currentY + ScrollContentBottomPadding;
        }

        /// <summary>
        /// Draws the benchmark and self-check tab, including the benchmark block and the self-check block.
        /// </summary>
        /// <param name="canvasRect">The rectangle defining the drawing area for the tab.</param>
        private void DrawBenchmarkAndSelfCheckTab(Rect canvasRect)
        {
            float targetHeight = Math.Max(lastBenchmarkHeight, canvasRect.height);
            Rect viewRect = new Rect(0f, 0f, canvasRect.width - SettingsViewUtilities.ScrollbarGutter, targetHeight);
            Widgets.BeginScrollView(canvasRect, ref benchmarkScrollPos, viewRect);

            float localY = 0f;
            localY = DrawBenchmarkBlock(viewRect, localY);
            localY = DrawSelfCheckBlock(viewRect, localY + SettingsViewUtilities.GroupGap);

            Widgets.EndScrollView();
            lastBenchmarkHeight = localY + ScrollContentBottomPadding;
        }

        /// <summary>
        /// Draws the telemetry and metrics tab, including periodic telemetry logging settings and cache eviction settings.
        /// </summary>
        /// <param name="canvasRect">The rectangle defining the drawing area for the tab.</param>
        private void DrawTelemetryAndMetricsTab(Rect canvasRect)
        {
            float targetHeight = Math.Max(lastTelemetryHeight, canvasRect.height);
            Rect viewRect = new Rect(0f, 0f, canvasRect.width - SettingsViewUtilities.ScrollbarGutter, targetHeight);
            Widgets.BeginScrollView(canvasRect, ref telemetryScrollPos, viewRect);

            SettingsLayout ui = new SettingsLayout(viewRect);

            // Section 1: Periodic Telemetry Logging
            ui.HeaderWithResetKey(
                "OverHaulers_ToolingDialog_Telemetry_SectionHeader",
                settings.ResetTelemetrySettings,
                SettingMutationScope.Solver,
                "Tooling");
            ui.DescriptionKey("OverHaulers_ToolingDialog_Telemetry_Desc");

            string intervalReadout = settings.reportMetricsIntervalHours <= 0
                ? "OverHaulers_ToolingDialog_Telemetry_ReportIntervalDisabled".Translate().ToString()
                : settings.reportMetricsIntervalHours.ToString();
            string intervalLabel = settings.reportMetricsIntervalHours <= 0
                ? intervalReadout
                : "OverHaulers_ToolingDialog_Telemetry_ReportInterval".Translate(intervalReadout).ToString();
            ui.IntSlider(
                intervalLabel,
                ref settings.reportMetricsIntervalHours,
                CoreDefaults.ReportMetricsIntervalHoursMin,
                CoreDefaults.ReportMetricsIntervalHoursMax,
                mutationScope: SettingMutationScope.ConfigOnly);

            string evictionLabel = "OverHaulers_ToolingDialog_Telemetry_CacheInactivityEvictionHours"
                .Translate(settings.pawnEvictionTimeframeHours.ToString()).ToString();
            ui.IntSlider(
                evictionLabel,
                ref settings.pawnEvictionTimeframeHours,
                CoreDefaults.PawnEvictionTimeframeHoursMin,
                CoreDefaults.PawnEvictionTimeframeHoursMax,
                mutationScope: SettingMutationScope.ConfigOnly);

            ui.Gap(SettingsViewUtilities.ControlGap);
            ui.HeaderKey("OverHaulers_ToolingDialog_Telemetry_ActiveMetricsHeader");

            float columnGap = SettingsViewUtilities.ColumnGap;
            float columnWidth = (ui.Width - columnGap) * 0.5f;
            SettingsLayout left = ui;
            SettingsLayout right = ui;
            left.Width = columnWidth;
            right.X = ui.X + columnWidth + columnGap;
            right.Width = columnWidth;

            left.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogQueryMetrics", ref settings.logQueryMetrics,
                "OverHaulers_ToolingDialog_Telemetry_LogQueryMetrics_Desc", SettingMutationScope.ConfigOnly);
            left.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogCacheMetrics", ref settings.logCacheMetrics,
                "OverHaulers_ToolingDialog_Telemetry_LogCacheMetrics_Desc", SettingMutationScope.ConfigOnly);
            left.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogSnapshotTableMetrics", ref settings.logSnapshotTableMetrics,
                "OverHaulers_ToolingDialog_Telemetry_LogSnapshotTableMetrics_Desc", SettingMutationScope.ConfigOnly);
            left.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogPresentationMetrics", ref settings.logPresentationMetrics,
                "OverHaulers_ToolingDialog_Telemetry_LogPresentationMetrics_Desc", SettingMutationScope.ConfigOnly);
            right.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogLifecycleMetrics", ref settings.logLifecycleMetrics,
                "OverHaulers_ToolingDialog_Telemetry_LogLifecycleMetrics_Desc", SettingMutationScope.ConfigOnly);
            right.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogWorkspaceMetrics", ref settings.logWorkspaceMetrics,
                "OverHaulers_ToolingDialog_Telemetry_LogWorkspaceMetrics_Desc", SettingMutationScope.ConfigOnly);
            right.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogSafetyFloorClamps", ref settings.logSafetyFloorClamps,
                "OverHaulers_ToolingDialog_Telemetry_LogSafetyFloorClamps_Desc", SettingMutationScope.ConfigOnly);
            right.ToggleKey("OverHaulers_ToolingDialog_Telemetry_LogPawnEvictions", ref settings.logPawnEvictions,
                "OverHaulers_ToolingDialog_Telemetry_LogPawnEvictions_Desc", SettingMutationScope.ConfigOnly);
            ui.Y = Math.Max(left.Y, right.Y);

            ui.Divider();

            // Section 2: High-Load Population Stress Testing
            ui.HeaderKey("OverHaulers_ToolingDialog_Telemetry_StressTestingHeader");
            ui.DescriptionKey("OverHaulers_ToolingDialog_Telemetry_StressTestAllFauna_Desc");
            ui.ToggleKey("OverHaulers_ToolingDialog_Telemetry_StressTestAllFauna", ref settings.devStressTestAllFauna,
                mutationScope: SettingMutationScope.Solver);

            Widgets.EndScrollView();
            lastTelemetryHeight = ui.Y + ScrollContentBottomPadding;
        }

        #endregion
    }
}
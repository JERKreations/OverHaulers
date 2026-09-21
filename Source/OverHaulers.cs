using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    public class OverHaulers : Mod
    {
        #region 1. FIELDS & CONSTRUCTOR

        public static Settings settings;
        public static ModContentPack ContentPack { get; private set; }

        // Isolated scroll positions per tab to prevent jarring jumps when switching views
        private static Vector2 scrollPositionTuning = Vector2.zero;
        private static Vector2 scrollPositionTestBench = Vector2.zero;
        private static Vector2 scrollPositionIntegration = Vector2.zero;
        private static Vector2 scrollPositionAccessibility = Vector2.zero;

        /// <summary>
        /// Initializes a new instance of the OverHaulers mod with the specified content pack.
        /// </summary>
        /// <param name="content">The content pack associated with this mod instance.</param>
        public OverHaulers(ModContentPack content) : base(content)
        {
            ContentPack = content;
            settings = GetSettings<Settings>();
            PerformanceTelemetry.SyncSettingsState();
        }

        #endregion

        #region 2. SETTINGS LIFECYCLE & WINDOW DRAWING

        /// <summary>
        /// Writes the current settings to persistent storage and clears relevant caches.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            
            PawnDataRegistry.ClearAllCaches();
            MedicalClassifier.ClearStaticCaches();
            PerformanceTelemetry.SyncSettingsState();
        }

        /// <summary>
        /// Draws the contents of the settings window within the specified rectangle.
        /// </summary>
        /// <param name="inRect">The rectangle within which to draw the settings window contents.</param>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect outRect = inRect.ContractedBy(4f);

            SettingsViewUtilities.ResetControlIndex();

            // BREAKPOINT ANCHOR: First-Time Launch Router
            // If the user has never opened the settings window, route them to Tuning
            if (!settings.hasOpenedSettingsBefore)
            {
                settings.activeTab = SettingsTab.Tuning;
                settings.hasOpenedSettingsBefore = true;
            }

            float currentY = 0f;

            // 1. Top Bar: Category Label & Global Reset Action
            Rect topBarRect = new Rect(0f, currentY, outRect.width, 26f);
            Rect titleRect = topBarRect.LeftPart(0.60f);
            Rect globalResetRect = topBarRect.RightPart(0.30f);

            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "OverHaulers_SettingsCategory".Translate().ToString());
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(globalResetRect, "OverHaulers_ResetDefaults".Translate().ToString()))
            {
                SettingsViewUtilities.ClearInputBuffers();
                SettingsViewUtilities.ClearHeightCache();
                settings.ResetAllToDefaults();
                TestBench.ResetTestBench();
            }
            currentY += 34f;

            // 2. Persistent Horizontal Tab Navigation Strip
            Rect tabStripRect = new Rect(0f, currentY, outRect.width, 28f);
            DrawTabNavigationStrip(tabStripRect);
            currentY += 36f;

            // 3. Tab-Isolated Content Scroll View
            Rect contentContainerRect = new Rect(0f, currentY, outRect.width, outRect.height - currentY);
            DrawActiveTabContent(contentContainerRect);

            base.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// Returns the name of the settings category for this mod.
        /// </summary>
        /// <returns>The translated name of the settings category.</returns>
        public override string SettingsCategory()
        {
            return "OverHaulers_SettingsCategory".Translate().ToString();
        }

        #endregion

        #region 3. TAB NAVIGATION BAR DRAWER

        /// <summary>
        /// Draws the horizontal tab navigation strip within the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle within which to draw the tab navigation strip.</param>
        private static void DrawTabNavigationStrip(Rect rect)
        {
            float gap = 4f;
            int tabCount = 4;
            float tabWidth = (rect.width - (gap * (tabCount - 1))) / (float)tabCount;

            DrawSingleTabButton(new Rect(rect.x, rect.y, tabWidth, rect.height), "OverHaulers_Tab_Tuning".Translate().ToString(), SettingsTab.Tuning);
            DrawSingleTabButton(new Rect(rect.x + (tabWidth + gap), rect.y, tabWidth, rect.height), "OverHaulers_Tab_TestBench".Translate().ToString(), SettingsTab.TestBench);
            DrawSingleTabButton(new Rect(rect.x + (tabWidth + gap) * 2f, rect.y, tabWidth, rect.height), "OverHaulers_Tab_Integration".Translate().ToString(), SettingsTab.Integration);
            DrawSingleTabButton(new Rect(rect.x + (tabWidth + gap) * 3f, rect.y, tabWidth, rect.height), "OverHaulers_Tab_Accessibility".Translate().ToString(), SettingsTab.Accessibility);
        }

        /// <summary>
        /// Draws a single tab button within the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle within which to draw the tab button.</param>
        /// <param name="label">The label to display on the tab button.</param>
        /// <param name="targetTab">The tab that this button corresponds to.</param>
        /// <remarks>
        /// This method handles the visual representation and interaction logic for a single tab button.
        /// </remarks>
        private static void DrawSingleTabButton(Rect rect, string label, SettingsTab targetTab)
        {
            bool isSelected = settings.activeTab == targetTab;

            if (isSelected)
            {
                Widgets.DrawBoxSolid(rect, SettingsViewUtilities.DarkTargetHighlightColor);
            }

            Color origColor = GUI.color;
            if (isSelected)
            {
                GUI.color = Color.cyan;
            }

            if (Widgets.ButtonText(rect, label))
            {
                if (settings.activeTab != targetTab)
                {
                    settings.activeTab = targetTab;
                    SettingsViewUtilities.ClearInputBuffers();
                    SettingsViewUtilities.ResetControlIndex();
                }
            }

            GUI.color = origColor;
        }

        #endregion

        #region 4. ACTIVE TAB CONTENT ROUTING

        /// <summary>
        /// Draws the content of the currently active settings tab within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The rectangle within which to draw the active tab content.</param>
        private static void DrawActiveTabContent(Rect containerRect)
        {
            switch (settings.activeTab)
            {
                case SettingsTab.Tuning:
                    DrawTuningTab(containerRect);
                    break;

                case SettingsTab.TestBench:
                    DrawTestBenchTab(containerRect);
                    break;

                case SettingsTab.Integration:
                    DrawIntegrationTab(containerRect);
                    break;

                case SettingsTab.Accessibility:
                    DrawAccessibilityTab(containerRect);
                    break;
            }
        }

        /// <summary>
        /// Draws the content of the Tuning tab within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The rectangle within which to draw the Tuning tab content.</param>
        private static void DrawTuningTab(Rect containerRect)
        {
            float systemicHeight = settings.linkSystemicWeights ? 340f : 520f;
            float totalEstimatedHeight = 
                SettingsViewUtilities.GetCachedSectionHeight("SEC_Globals", 280f) + 15f
                + SettingsViewUtilities.GetCachedSectionHeight("SEC_CoreBodyWeights", 180f) + 15f
                + SettingsViewUtilities.GetCachedSectionHeight("SEC_SystemicWeightings", systemicHeight) + 15f
                + SettingsViewUtilities.GetCachedSectionHeight("SEC_TorsoLimbDepths", 260f) + 15f
                + SettingsViewUtilities.GetCachedSectionHeight("SEC_ScalingConstants", 180f) + 30f;

            float canvasHeight = Mathf.Max(containerRect.height, totalEstimatedHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - 18f, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionTuning, viewRect);
            float currentY = 0f;

            try
            {
                // 1. [SEC-01] Main Scalar Settings & Safety Floor
                SettingsView.DrawBasicModifiers(viewRect, ref currentY, settings, settings.ResetGlobals);

                // 2. [SEC-02] Core Body Group Weights Section
                SettingsView.DrawCoreBodyGroupWeights(viewRect, ref currentY, settings, settings.ResetBodyParts);

                // 3. [SEC-03 & SEC-04] Unified Systemic Health Weightings Section
                SettingsView.DrawSystemicHealthWeights(
                    viewRect, 
                    ref currentY, 
                    settings, 
                    settings.ResetTorsoSystemic, 
                    settings.ResetArmSystemic, 
                    settings.ResetLegSystemic,
                    settings.ResetSystemicWeightings
                );

                // 4. [SEC-05] Torso Macro Topology & Limb Depths Section (50/50 Split)
                SettingsView.DrawTorsoAndLimbDepths(viewRect, ref currentY, settings, settings.ResetTorsoParts, settings.ResetLimbDecay);

                // 5. [SEC-06] Scaling Constants Section
                SettingsView.DrawScalingConstants(viewRect, ref currentY, settings, settings.ResetScalingConstants);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// Draws the content of the Test Bench tab within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The rectangle within which to draw the Test Bench tab content.</param>
        private static void DrawTestBenchTab(Rect containerRect)
        {
            float estimatedHeight = SettingsViewUtilities.GetCachedSectionHeight("SEC_TestBench", 560f) + 60f;
            float canvasHeight = Mathf.Max(containerRect.height, estimatedHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - 18f, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionTestBench, viewRect);
            float currentY = 0f;

            try
            {
                // Standalone Developer Testing Override Box (Placed above Test Bench)
                SettingsView.DrawDevTestingOverrideSection(viewRect, ref currentY, settings);

                // Main Interactive Anatomical Test Bench Section
                SettingsView.DrawTestBenchSection(viewRect, ref currentY, settings, TestBench.ResetTestBench);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// Draws the content of the Integration tab within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The rectangle within which to draw the Integration tab content.</param>
        private static void DrawIntegrationTab(Rect containerRect)
        {
            float totalEstimatedHeight = 
                SettingsViewUtilities.GetCachedSectionHeight("SEC_Compatibility", 220f) + 15f
                + SettingsViewUtilities.GetCachedSectionHeight("SEC_Diagnostics", 260f) + 30f;

            float canvasHeight = Mathf.Max(containerRect.height, totalEstimatedHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - 18f, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionIntegration, viewRect);
            float currentY = 0f;

            try
            {
                // 1. [SEC-08] Harmony Patch Inspector & Driver Overrides
                SettingsView.DrawIntegrationSection(viewRect, ref currentY, settings, settings.ResetCompatibility);

                // 2. [SEC-09] Engine Metrics & Performance Reporting
                SettingsView.DrawDiagnosticsSection(viewRect, ref currentY, settings, settings.ResetDiagnostics);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// Draws the content of the Accessibility tab within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The rectangle within which to draw the Accessibility tab content.</param>
        private static void DrawAccessibilityTab(Rect containerRect)
        {
            float estimatedHeight = SettingsViewUtilities.GetCachedSectionHeight("SEC_Accessibility", 360f) + 20f;
            float canvasHeight = Mathf.Max(containerRect.height, estimatedHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - 18f, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionAccessibility, viewRect);
            float currentY = 0f;

            try
            {
                SettingsView.DrawAccessibilitySection(viewRect, ref currentY, settings, settings.ResetAccessibility);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        #endregion
    }
}
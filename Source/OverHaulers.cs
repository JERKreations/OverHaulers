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

        /// <summary>
        /// Initializes a new instance of the OverHaulers mod with the specified content pack.
        /// </summary>
        /// <param name="content">The content pack associated with this mod instance.</param>
        public OverHaulers(ModContentPack content) : base(content)
        {
            ContentPack = content;
            settings = GetSettings<Settings>();
            settings?.PushConfigurationToCore();
        }

        #endregion

        #region 2. SETTINGS LIFECYCLE & WINDOW DRAWING

        /// <summary>
        /// Writes the current settings to persistent storage and marks runtime caches stale.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            
            BenchmarkRunner.Cancel();
            SelfCheckRunner.Cancel();
            settings?.PushConfigurationToCore();
            PawnDataRegistry.MarkAllStale();
            MedicalClassifier.ClearStaticCaches();
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
        /// Delegates active settings tab rendering to <see cref="SettingsView"/>.
        /// </summary>
        /// <param name="containerRect">The bounding rectangle for tab content.</param>
        private static void DrawActiveTabContent(Rect containerRect)
        {
            SettingsView.DrawActiveTabContent(containerRect, settings);
        }

        #endregion
    }
}
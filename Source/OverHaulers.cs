using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [LIFE-05] Mod composition root: the only place where the Settings layer is bound to Core, Presentation and
    /// Integration through pushes and events. Resides under Source/.
    /// </summary>
    public class OverHaulers : Mod
    {
        #region 1. [LIFE-05] FIELDS & CONSTRUCTOR

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
            IntegrationPipeline.PreferredPresentationKeyReset += key => { if (settings != null) settings.selectedPresentationKey = key; };
            ToolingLifecycleBinding.Bind();
            SettingsViewUtilities.BindToSettings();
        }

        #endregion

        #region 2. SETTINGS LIFECYCLE & WINDOW DRAWING

        /// <summary>
        /// Writes the current settings to persistent storage and marks runtime caches stale.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            
            LifecycleHooks.RaiseRuntimeResetting();
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

            SettingsLayout.ResetControlIndex();

            // BREAKPOINT ANCHOR: First-Time Launch Router & Legacy Migration
            // Route first-time players or legacy TestBench tab users to Tuning
            if (!settings.hasOpenedSettingsBefore || settings.activeTab == SettingsTab.TestBench)
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
                SettingsLayout.ClearInputBuffers();
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
        /// Renders player configuration tabs on the left and a floating Dev Workspace launcher on the right.
        /// </summary>
        /// <param name="rect">The rectangle within which to draw the tab navigation strip.</param>
        private static void DrawTabNavigationStrip(Rect rect)
        {
            float gap = 6f;
            float devButtonWidth = 140f;

            // 1. Left: 3 Core Configuration Tabs
            float playerTabsAreaWidth = rect.width - devButtonWidth - gap;
            int playerTabCount = 3;
            float tabWidth = (playerTabsAreaWidth - (gap * (playerTabCount - 1))) / (float)playerTabCount;

            DrawSingleTabButton(new Rect(rect.x, rect.y, tabWidth, rect.height), "OverHaulers_Tab_Tuning".Translate().ToString(), SettingsTab.Tuning);
            DrawSingleTabButton(new Rect(rect.x + (tabWidth + gap), rect.y, tabWidth, rect.height), "OverHaulers_Tab_Integration".Translate().ToString(), SettingsTab.Integration);
            DrawSingleTabButton(new Rect(rect.x + (tabWidth + gap) * 2f, rect.y, tabWidth, rect.height), "OverHaulers_Tab_Accessibility".Translate().ToString(), SettingsTab.Accessibility);

            // 2. Right: Floating Dev Workspace Launcher Button
            Rect devBtnRect = new Rect(rect.xMax - devButtonWidth, rect.y, devButtonWidth, rect.height);
            Color origColor = GUI.color;
            try
            {
                GUI.color = new Color(0.85f, 0.88f, 1.0f);
                if (Widgets.ButtonText(devBtnRect, "OverHaulers_ToolingDialog_LaunchButton".Translate().ToString()))
                {
                    Find.WindowStack.Add(new ToolingDialog(settings));
                }
            }
            finally
            {
                GUI.color = origColor;
            }
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
                    SettingsLayout.ClearInputBuffers();
                    SettingsLayout.ResetControlIndex();
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
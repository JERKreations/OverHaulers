using System;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-00] Central GUI presentation orchestrator for the OverHaulers settings interface.
    /// Manages top-level tab navigation, per-tab scroll state, and delegates section rendering
    /// to modular domain partials (<see cref="SettingsView"/>.*.cs).
    /// Uses cached heights and direct Rect layouts to reduce per-frame layout overhead.
    /// Resides under Source/Settings/Views/.
    /// </summary>
    public static partial class SettingsView
    {
        #region 1. TAB SCROLL STATE & METRICS

        // Isolated scroll positions per tab to prevent jarring jumps when switching views
        private static Vector2 scrollPositionTuning = Vector2.zero;
        private static Vector2 scrollPositionIntegration = Vector2.zero;
        private static Vector2 scrollPositionAccessibility = Vector2.zero;

        private static float lastTuningHeight = 850f;
        private static float lastIntegrationHeight = 650f;
        private static float lastAccessibilityHeight = 520f;

        #endregion

        #region 2. TOP-LEVEL TAB CONTENT ROUTING

        /// <summary>
        /// Renders the content of the currently active settings tab within the specified container rectangle.
        /// </summary>
        /// <param name="containerRect">The bounding rectangle for tab content.</param>
        /// <param name="settings">The active settings instance.</param>
        public static void DrawActiveTabContent(Rect containerRect, Settings settings)
        {
            if (settings == null) return;

            switch (settings.activeTab)
            {
                case SettingsTab.Tuning:
                    DrawTuningTab(containerRect, settings);
                    break;

                case SettingsTab.Integration:
                    DrawIntegrationTab(containerRect, settings);
                    break;

                case SettingsTab.Accessibility:
                    DrawAccessibilityTab(containerRect, settings);
                    break;
            }
        }

        #endregion

        #region 3. TAB-SPECIFIC SCROLL WRAPPERS

        /// <summary>
        /// [SEC-01 to SEC-07] Draws the Tuning &amp; Anatomy tab content within an isolated linear scroll view.
        /// </summary>
        private static void DrawTuningTab(Rect containerRect, Settings settings)
        {
            float canvasHeight = Math.Max(containerRect.height, lastTuningHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - SettingsViewUtilities.ScrollbarGutter, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionTuning, viewRect);

            SettingsLayout ui = new SettingsLayout(viewRect);
            DrawTuningTabContents(ref ui, settings);

            Widgets.EndScrollView();
            lastTuningHeight = ui.Y;
        }

        /// <summary>
        /// [SEC-08] Draws the Compatibility &amp; Mod Integration tab content within an isolated linear scroll view.
        /// </summary>
        private static void DrawIntegrationTab(Rect containerRect, Settings settings)
        {
            float canvasHeight = Math.Max(containerRect.height, lastIntegrationHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - SettingsViewUtilities.ScrollbarGutter, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionIntegration, viewRect);

            SettingsLayout ui = new SettingsLayout(viewRect);
            DrawIntegrationTabContents(ref ui, settings);

            Widgets.EndScrollView();
            lastIntegrationHeight = ui.Y;
        }

        /// <summary>
        /// [SEC-10] Draws the Accessibility &amp; UI tab content within an isolated linear scroll view.
        /// </summary>
        private static void DrawAccessibilityTab(Rect containerRect, Settings settings)
        {
            float canvasHeight = Math.Max(containerRect.height, lastAccessibilityHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - SettingsViewUtilities.ScrollbarGutter, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionAccessibility, viewRect);

            SettingsLayout ui = new SettingsLayout(viewRect);
            DrawAccessibilityTabContents(ref ui, settings);

            Widgets.EndScrollView();
            lastAccessibilityHeight = ui.Y;
        }

        #endregion
    }
}
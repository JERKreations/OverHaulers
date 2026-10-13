using UnityEngine;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-00] Central GUI presentation orchestrator for the OverHaulers settings interface.
    /// Manages top-level tab navigation, per-tab scroll state, and delegates section rendering
    /// to modular domain partials (<see cref="SettingsView"/>.*.cs).
    /// Operates with zero GC allocations per frame via cached heights and direct Rect layouts.
    /// Resides under Source/Settings/Views/.
    /// </summary>
    public static partial class SettingsView
    {
        #region 1. TAB SCROLL STATE & METRICS

        // Isolated scroll positions per tab to prevent jarring jumps when switching views
        private static Vector2 scrollPositionTuning = Vector2.zero;
        private static Vector2 scrollPositionTestBench = Vector2.zero;
        private static Vector2 scrollPositionIntegration = Vector2.zero;
        private static Vector2 scrollPositionAccessibility = Vector2.zero;

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

                case SettingsTab.TestBench:
                    DrawTestBenchTab(containerRect, settings);
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
        /// [SEC-01 to SEC-07] Draws the Tuning &amp; Anatomy tab content within an isolated scroll view.
        /// </summary>
        private static void DrawTuningTab(Rect containerRect, Settings settings)
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
                DrawBasicModifiers(viewRect, ref currentY, settings, settings.ResetGlobals);

                // 2. [SEC-02] Core Body Group Weights Section
                DrawCoreBodyGroupWeights(viewRect, ref currentY, settings, settings.ResetBodyParts);

                // 3. [SEC-03 & SEC-04] Unified Systemic Health Weightings Section
                DrawSystemicHealthWeights(
                    viewRect, 
                    ref currentY, 
                    settings, 
                    settings.ResetTorsoSystemic, 
                    settings.ResetArmSystemic, 
                    settings.ResetLegSystemic,
                    settings.ResetSystemicWeightings
                );

                // 4. [SEC-05 & SEC-06] Torso Macro Topology & Limb Depths Section (50/50 Split)
                DrawTorsoAndLimbDepths(viewRect, ref currentY, settings, settings.ResetTorsoParts, settings.ResetLimbDecay);

                // 5. [SEC-07] Scaling Constants Section
                DrawScalingConstants(viewRect, ref currentY, settings, settings.ResetScalingConstants);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// [SEC-11] Draws the Test Bench tab content within an isolated scroll view.
        /// </summary>
        private static void DrawTestBenchTab(Rect containerRect, Settings settings)
        {
            float estimatedHeight = SettingsViewUtilities.GetCachedSectionHeight("SEC_TestBench", 560f) + 20f;
            float canvasHeight = Mathf.Max(containerRect.height, estimatedHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - 18f, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionTestBench, viewRect);
            float currentY = 0f;

            try
            {
                DrawTestBenchSection(viewRect, ref currentY, settings, TestBench.ResetTestBench);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// [SEC-08 &amp; SEC-09] Draws the Integration &amp; Diagnostics tab content within an isolated scroll view.
        /// </summary>
        private static void DrawIntegrationTab(Rect containerRect, Settings settings)
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
                DrawIntegrationSection(viewRect, ref currentY, settings, settings.ResetCompatibility);

                // 2. [SEC-09] Engine Metrics & Performance Reporting
                DrawDiagnosticsSection(viewRect, ref currentY, settings, settings.ResetDiagnostics);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// [SEC-10] Draws the Accessibility &amp; UI tab content within an isolated scroll view.
        /// </summary>
        private static void DrawAccessibilityTab(Rect containerRect, Settings settings)
        {
            float estimatedHeight = SettingsViewUtilities.GetCachedSectionHeight("SEC_Accessibility", 360f) + 20f;
            float canvasHeight = Mathf.Max(containerRect.height, estimatedHeight);
            Rect viewRect = new Rect(0f, 0f, containerRect.width - 18f, canvasHeight);

            Widgets.BeginScrollView(containerRect, ref scrollPositionAccessibility, viewRect);
            float currentY = 0f;

            try
            {
                DrawAccessibilitySection(viewRect, ref currentY, settings, settings.ResetAccessibility);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        #endregion
    }
}
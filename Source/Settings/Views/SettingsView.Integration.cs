using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-08] Linear presentation layout for Tab 2 (Integration &amp; Compatibility).
    /// Controls InfoCard stat adoption, deduplication against third-party mods, and bytecode patch discovery.
    /// Resides under Source/Settings/Views/.
    /// </summary>
    public static partial class SettingsView
    {
        #region 1. CANONICAL TAB ENTRY POINT

        /// <summary>
        /// Draws the contents of the Integration tab in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the tab contents in.</param>
        /// <param name="settings">The current settings.</param>
        public static void DrawIntegrationTabContents(ref SettingsLayout ui, Settings settings)
        {
            // 0. Section Header & Reset Action
            ui.HeaderWithResetKey("OverHaulers_CompatibilityHeader", () =>
            {
                settings.ResetCompatibility();
                expandedAssemblyStatOwners.Clear();
                IntegrationPipeline.SetCustomCilNeutralizedFactors(null);
                IntegrationPipeline.SetCilAdditiveBonusMode(false);
                IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
            });

            ui.DescriptionKey("OverHaulers_Compatibility_Desc");
            ui.Gap(SettingsViewUtilities.CompactGap);

            // 1. Active Stat Readout Banner
            DrawActiveStatBanner(ref ui, settings);

            // 2. Dynamic In-Game Verification & What-To-Expect Card
            VerificationGuide guide = BuildVerificationGuide(settings);
            DrawVerificationGuideCard(ref ui, in guide);
            ui.Gap(SettingsViewUtilities.ControlGap);

            // 3. Top-Level Mode Selection (Auto vs Standalone)
            DrawTopLevelModeSelection(ref ui, settings);
            ui.Divider();

            // 4. Declarative XML Profiles (Built-In & Third-Party)
            DrawXmlProfilesSection(ref ui, settings);
            ui.Divider();

            // 5. Dynamic Bytecode Discoveries (Lazy CIL Scanning)
            DrawCilScanningSection(ref ui, settings);
            ui.Gap(SettingsViewUtilities.GroupGap);
        }

        #endregion

        #region 2. ACTIVE STAT BANNER & VERIFICATION ENGINE

        /// <summary>
        /// Draws the active stat readout banner in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the banner in.</param>
        /// <param name="settings">The current settings.</param>
        private static void DrawActiveStatBanner(ref SettingsLayout ui, Settings settings)
        {
            string activeStatName = IntegrationPipeline.ActiveStatOwner;
            string activeBadgeType = ResolveStatPresentationBadge();
            string activeLabel = "OverHaulers_ActiveStatBanner".Translate(activeStatName, activeBadgeType).ToString();

            Rect bannerRect = new Rect(ui.X, ui.Y, ui.Width, 26f);
            Widgets.DrawBoxSolid(bannerRect, new Color(0.12f, 0.15f, 0.18f, 0.85f));
            Widgets.DrawBox(bannerRect);

            GameFont origFont = Text.Font;
            Color origColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = settings.colorBoosted;
                Widgets.Label(new Rect(bannerRect.x + 8f, bannerRect.y + 5f, bannerRect.width - 16f, 18f), activeLabel);
            }
            finally
            {
                GUI.color = origColor;
                Text.Font = origFont;
            }

            ui.Y += 32f;
        }

        /// <summary>
        /// Draws the verification guide card in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the card in.</param>
        /// <param name="guide">The verification guide containing the information to display.</param>
        private struct VerificationGuide
        {
            public string StatusTitle;
            public Color StatusColor;
            public string DisplayLine;
            public string GearLine;
            public string ConfirmLine;
        }

        /// <summary>
        /// Builds a verification guide based on the current integration settings and active stat.
        /// </summary>
        /// <param name="settings">The current settings.</param>
        /// <returns>A VerificationGuide containing the appropriate messages and colors.</returns>
        private static VerificationGuide BuildVerificationGuide(Settings settings)
        {
            StatDef stat = IntegrationPipeline.ActiveMassCapacityStat;

            string statLabel = (stat != null && !string.IsNullOrEmpty(stat.label))
                ? stat.LabelCap.ToString()
                : "OverHaulers_Verify_FallbackStat".Translate().ToString();

            string categoryLabel = (stat != null && stat.category != null && !string.IsNullOrEmpty(stat.category.label))
                ? stat.category.LabelCap.ToString()
                : "OverHaulers_Verify_FallbackCategory".Translate().ToString();

            string key = settings.selectedPresentationKey ?? IntegrationPipeline.PresentationKeyAuto;
            bool isForcedStandalone = (key == IntegrationPipeline.PresentationKeyNative);
            bool isCilAdopted = IntegrationPipeline.IsForeignStatAdopted &&
                IntegrationPipeline.ActiveAdoptionSource == StatAdoptionSource.CilDiscovered;
            bool hasExternalStatCards = IntegrationPipeline.HasAnyActiveXmlProfile || HasDiscoveredCandidateStats();

            // Case 4: Adopted via dynamic CIL discovery
            if (isCilAdopted)
            {
                return new VerificationGuide
                {
                    StatusTitle = "OverHaulers_Verify_Cil_Title".Translate(statLabel).ToString(),
                    StatusColor = new Color(0.40f, 0.82f, 1.00f),
                    DisplayLine = "OverHaulers_Verify_Cil_Display".Translate(statLabel, categoryLabel).ToString(),
                    GearLine = "OverHaulers_Verify_Cil_Gear".Translate().ToString(),
                    ConfirmLine = "OverHaulers_Verify_Cil_Confirm".Translate(statLabel).ToString()
                };
            }

            // Case 2: Adoption (XML profile or automatic adoption) -> single unified card
            if (IntegrationPipeline.IsForeignStatAdopted)
            {
                return new VerificationGuide
                {
                    StatusTitle = "OverHaulers_Verify_Unified_Title".Translate().ToString(),
                    StatusColor = settings.colorBoosted,
                    DisplayLine = "OverHaulers_Verify_Unified_Display".Translate(statLabel, categoryLabel).ToString(),
                    GearLine = "OverHaulers_Verify_Unified_Gear".Translate().ToString(),
                    ConfirmLine = "OverHaulers_Verify_Unified_Confirm".Translate(statLabel).ToString()
                };
            }

            // Case 3: Third-party stat mod active while running un-adopted native card (Forced Standalone or unscanned/AUTO unlisted stat) -> multiple cards
            if (hasExternalStatCards && (isForcedStandalone || !IntegrationPipeline.IsForeignStatAdopted))
            {
                string confirmKey = isForcedStandalone
                    ? "OverHaulers_Verify_SideBySide_Confirm"
                    : "OverHaulers_Verify_SideBySide_ConfirmCil";

                return new VerificationGuide
                {
                    StatusTitle = "OverHaulers_Verify_SideBySide_Title".Translate().ToString(),
                    StatusColor = Color.yellow,
                    DisplayLine = "OverHaulers_Verify_SideBySide_Display".Translate(statLabel, categoryLabel).ToString(),
                    GearLine = "OverHaulers_Verify_SideBySide_Gear".ToString(),
                    ConfirmLine = confirmKey.Translate().ToString()
                };
            }

            // Case 1: Default / Pure Standalone (no competing external stat card active) -> single native card
            return new VerificationGuide
            {
                StatusTitle = "OverHaulers_Verify_Standalone_Title".Translate().ToString(),
                StatusColor = settings.colorHealthy,
                DisplayLine = "OverHaulers_Verify_Standalone_Display".Translate(statLabel, categoryLabel).ToString(),
                GearLine = "OverHaulers_Verify_Standalone_Gear".ToString(),
                ConfirmLine = "OverHaulers_Verify_Standalone_Confirm".Translate(statLabel).ToString()
            };
        }

        /// <summary>
        /// Draws a verification guide card in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout to draw the card in.</param>
        /// <param name="guide">The verification guide containing the information to display.</param>
        private static void DrawVerificationGuideCard(ref SettingsLayout ui, in VerificationGuide guide)
        {
            float padding = SettingsViewUtilities.PanelPadding;
            float textWidth = ui.Width - (padding * 2f);
            float prefixWidth = 110f;
            float valueWidth = textWidth - prefixWidth;

            GameFont origFont = Text.Font;
            Color origColor = GUI.color;
            Text.Font = GameFont.Tiny;

            float hDisplay;
            float hGear;
            float hConfirm;

            Text.Font = GameFont.Tiny;
            try
            {
                hDisplay = Math.Max(18f, Text.CalcHeight(guide.DisplayLine, valueWidth));
                hGear = Math.Max(18f, Text.CalcHeight(guide.GearLine, valueWidth));
                hConfirm = Math.Max(18f, Text.CalcHeight(guide.ConfirmLine, valueWidth));
            }
            finally
            {
                Text.Font = origFont;
            }

            float cardHeight = 24f + hDisplay + hGear + hConfirm + (padding * 2f) + 6f;
            Rect cardRect = new Rect(ui.X, ui.Y, ui.Width, cardHeight);

            Widgets.DrawBoxSolid(cardRect, new Color(0.10f, 0.12f, 0.14f, 0.85f));
            Widgets.DrawHighlightIfMouseover(cardRect);

            float curY = cardRect.y + padding;

            Text.Font = GameFont.Tiny;
            try
            {
                // Title Line
                Rect titleRect = new Rect(cardRect.x + padding, curY, textWidth, 20f);
                GUI.color = guide.StatusColor;
                Widgets.Label(titleRect, string.Format("● {0}", guide.StatusTitle.ToUpperInvariant()));
                GUI.color = Color.white;
                curY += 22f;

                // 3 Verification Lines
                DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hDisplay,
                    "OverHaulers_Verify_Field_Display".Translate().ToString(), guide.DisplayLine);
                DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hGear,
                    "OverHaulers_Verify_Field_Gear".Translate().ToString(), guide.GearLine);
                DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hConfirm,
                    "OverHaulers_Verify_Field_Confirm".Translate().ToString(), guide.ConfirmLine);
            }
            finally
            {
                Text.Font = origFont;
                GUI.color = origColor;
            }

            ui.Y += cardHeight + 10f;
        }

        /// <summary>
        /// Draws a single verification line in the settings UI, including the prefix and the corresponding text.
        /// </summary>
        /// <param name="startX">The starting X coordinate for the line.</param>
        /// <param name="curY">The current Y coordinate, passed by reference and updated after drawing.</param>
        /// <param name="prefixWidth">The width allocated for the prefix label.</param>
        /// <param name="valueWidth">The width allocated for the value text.</param>
        /// <param name="lineHeight">The height of the line.</param>
        /// <param name="prefix">The prefix text to display.</param>
        /// <param name="text">The value text to display.</param>
        private static void DrawVerificationLine(
            float startX, ref float curY,
            float prefixWidth, float valueWidth, float lineHeight,
            string prefix, string text)
        {
            Rect prefixRect = new Rect(startX, curY, prefixWidth, lineHeight);
            Rect textRect = new Rect(startX + prefixWidth, curY, valueWidth, lineHeight);

            GUI.color = SettingsLayout.MutedTextColor;
            Widgets.Label(prefixRect, string.Format("• {0}", prefix));
            GUI.color = Color.white;
            Widgets.Label(textRect, text);

            curY += lineHeight + 2f;
        }

        #endregion

        #region 3. TOP-LEVEL MODE SELECTION (AUTO VS STANDALONE)

        /// <summary>
        /// Draws the top-level mode selection section in the settings UI, allowing the user to choose between automatic and standalone modes.
        /// </summary>
        /// <param name="ui">The settings layout reference.</param>
        /// <param name="settings">The current settings object.</param>
        private static void DrawTopLevelModeSelection(ref SettingsLayout ui, Settings settings)
        {
            string currentKey = settings.selectedPresentationKey ?? IntegrationPipeline.PresentationKeyAuto;
            bool isAutoSelected = (currentKey == IntegrationPipeline.PresentationKeyAuto);
            bool isStandaloneSelected = (currentKey == IntegrationPipeline.PresentationKeyNative);

            // Option A: Automatic Stat Adoption (Recommended)
            Rect autoRowRect = new Rect(ui.X, ui.Y, ui.Width, SettingsLayout.DefaultToggleHeight);
            string autoTooltip = SettingsViewUtilities.CachedText("OverHaulers_PresentationMode_Auto_Tooltip");
            string autoLabel = SettingsViewUtilities.LabelWithTooltip(SettingsViewUtilities.CachedText("OverHaulers_PresentationMode_Auto"), autoTooltip);
            if (DrawCustomRadioRow(autoRowRect, autoLabel, isAutoSelected))
            {
                settings.selectedPresentationKey = IntegrationPipeline.PresentationKeyAuto;
                settings.ResetCustomCilNeutralization();
                IntegrationPipeline.SetCustomCilNeutralizedFactors(null);
                IntegrationPipeline.SetCilAdditiveBonusMode(false);
                IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
            }
            if (!string.IsNullOrEmpty(autoTooltip)) TooltipHandler.TipRegion(autoRowRect, autoTooltip);
            ui.Y += SettingsLayout.DefaultToggleHeight + SettingsViewUtilities.CompactGap;

            // Option B: Force Native Standalone Card
            Rect standaloneRowRect = new Rect(ui.X, ui.Y, ui.Width, SettingsLayout.DefaultToggleHeight);
            string standaloneTooltip = SettingsViewUtilities.CachedText("OverHaulers_PresentationMode_Native_Tooltip");
            string standaloneLabel = SettingsViewUtilities.LabelWithTooltip(SettingsViewUtilities.CachedText("OverHaulers_PresentationMode_Native"), standaloneTooltip);
            if (DrawCustomRadioRow(standaloneRowRect, standaloneLabel, isStandaloneSelected))
            {
                settings.selectedPresentationKey = IntegrationPipeline.PresentationKeyNative;
                settings.ResetCustomCilNeutralization();
                IntegrationPipeline.SetCustomCilNeutralizedFactors(null);
                IntegrationPipeline.SetCilAdditiveBonusMode(false);
                IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
            }
            if (!string.IsNullOrEmpty(standaloneTooltip)) TooltipHandler.TipRegion(standaloneRowRect, standaloneTooltip);
            ui.Y += SettingsLayout.DefaultToggleHeight + SettingsViewUtilities.ControlGap;
        }

        #endregion

        #region 4. DECLARATIVE XML PROFILES

        private static readonly List<MassCapacityStatProfileDef> pooledBuiltInProfiles = new List<MassCapacityStatProfileDef>(8);
        private static readonly List<MassCapacityStatProfileDef> pooledThirdPartyProfiles = new List<MassCapacityStatProfileDef>(8);

        /// <summary>
        /// Draws the XML profiles section in the settings UI, including built-in and third-party profiles.
        /// </summary>
        /// <param name="ui">The settings layout reference.</param>
        /// <param name="settings">The current settings object.</param>
        private static void DrawXmlProfilesSection(ref SettingsLayout ui, Settings settings)
        {
            string currentKey = settings.selectedPresentationKey ?? IntegrationPipeline.PresentationKeyAuto;
            List<MassCapacityStatProfileDef> allXmlProfiles = DefDatabase<MassCapacityStatProfileDef>.AllDefsListForReading;

            pooledBuiltInProfiles.Clear();
            pooledThirdPartyProfiles.Clear();

            try
            {
                if (allXmlProfiles != null)
                {
                    for (int i = 0; i < allXmlProfiles.Count; i++)
                    {
                        MassCapacityStatProfileDef def = allXmlProfiles[i];
                        if (def.modContentPack == OverHaulers.ContentPack)
                        {
                            pooledBuiltInProfiles.Add(def);
                        }
                        else
                        {
                            pooledThirdPartyProfiles.Add(def);
                        }
                    }
                }

                ui.SubHeader("OverHaulers_ProfileGroup_BuiltIn".Translate().ToString());

                if (pooledBuiltInProfiles.Count == 0)
                {
                    DrawEmptyGroupNote(ref ui, "OverHaulers_ProfileGroup_None".Translate().ToString());
                }
                else
                {
                    for (int i = 0; i < pooledBuiltInProfiles.Count; i++)
                    {
                        DrawXmlProfileEntry(ref ui, pooledBuiltInProfiles[i], currentKey, settings);
                    }
                }

                if (pooledThirdPartyProfiles.Count > 0)
                {
                    ui.Gap(SettingsViewUtilities.CompactGap);
                    ui.SubHeader("OverHaulers_ProfileGroup_ThirdParty".Translate().ToString());
                    for (int i = 0; i < pooledThirdPartyProfiles.Count; i++)
                    {
                        DrawXmlProfileEntry(ref ui, pooledThirdPartyProfiles[i], currentKey, settings);
                    }
                }

                ui.Gap(SettingsViewUtilities.ControlGap);
            }
            finally
            {
                pooledBuiltInProfiles.Clear();
                pooledThirdPartyProfiles.Clear();
            }
        }

        /// <summary>
        /// Draws the UI entry for a single XML profile, including its selection radio button and status badge.
        /// </summary>
        /// <param name="ui">The settings layout reference.</param>
        /// <param name="def">The XML profile definition.</param>
        /// <param name="currentKey">The currently selected presentation key.</param>
        /// <param name="settings">The current settings object.</param>
        private static void DrawXmlProfileEntry(ref SettingsLayout ui, MassCapacityStatProfileDef def, string currentKey, Settings settings)
        {
            bool isActive = def.IsValidAndActive(out _);
            string expectedKey = IntegrationPipeline.PresentationKeyXmlPrefix + def.defName;
            bool isSelected = (currentKey == expectedKey);

            Rect rowRect = new Rect(ui.X + 8f, ui.Y, ui.Width - 8f, 24f);

            if (isSelected)
            {
                Widgets.DrawBoxSolid(rowRect.ExpandedBy(2f, 1f), new Color(1f, 1f, 1f, 0.05f));
            }

            float badgeWidth = 130f;
            Rect radioRect = new Rect(rowRect.x, rowRect.y, rowRect.width - badgeWidth - 4f, rowRect.height);
            Rect badgeRect = new Rect(rowRect.xMax - badgeWidth, rowRect.y + 2f, badgeWidth, rowRect.height);

            string profileName = def.label ?? def.defName;
            string profileLabel = "OverHaulers_ProfileLabel_StatFormat".Translate(profileName, def.statDefName).ToString();

            if (isActive)
            {
                if (DrawCustomRadioRow(radioRect, profileLabel, isSelected))
                {
                    settings.selectedPresentationKey = expectedKey;
                    settings.ResetCustomCilNeutralization();
                    IntegrationPipeline.SetCustomCilNeutralizedFactors(null);
                    IntegrationPipeline.SetCilAdditiveBonusMode(false);
                    IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
                }

                GameFont origFont = Text.Font;
                Color origColor = GUI.color;
                try
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = isSelected ? settings.colorBoosted : settings.colorHealthy;
                    Widgets.Label(badgeRect, "OverHaulers_ModActive".Translate().ToString());
                }
                finally
                {
                    GUI.color = origColor;
                    Text.Font = origFont;
                }
            }
            else
            {
                bool prevGui = GUI.enabled;
                GameFont origFont = Text.Font;
                Color origColor = GUI.color;
                try
                {
                    GUI.enabled = false;
                    DrawCustomRadioRow(radioRect, profileLabel, false);

                    Text.Font = GameFont.Tiny;
                    GUI.color = SettingsLayout.MutedTextColor;
                    Widgets.Label(badgeRect, "OverHaulers_ModNotLoaded".Translate().ToString());
                }
                finally
                {
                    GUI.color = origColor;
                    Text.Font = origFont;
                    GUI.enabled = prevGui;
                }
            }

            ui.Y += 26f;
        }

        #endregion

        #region 5. DYNAMIC BYTECODE DISCOVERIES (CIL SCANNER)

        /// <summary>Pooled list of ascertained capacity factors discovered during CIL scanning.</summary>
        private static readonly List<AscertainedCapacityFactor> pooledCilFactors = new List<AscertainedCapacityFactor>(8);

        /// <summary>Pooled string builder for summarizing CIL-discovered capacity factors.</summary>
        private static readonly System.Text.StringBuilder pooledCilFactorSummary = new System.Text.StringBuilder(128);

        /// <summary>Tracks which assembly stat owners have their extra stats expanded in the UI.</summary>
        private static readonly HashSet<string> expandedAssemblyStatOwners = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Pooled list of extra assembly stats for the currently expanded stat owner.</summary>
        private static readonly List<StatDef> pooledExtraAssemblyStats = new List<StatDef>(16);

        /// <summary>
        /// Draws the CIL scanning section in the settings UI, including scan status and discovered patches.
        /// </summary>
        /// <param name="ui">The settings layout reference.</param>
        /// <param name="settings">The current settings object.</param>
        private static void DrawCilScanningSection(ref SettingsLayout ui, Settings settings)
        {
            string currentKey = settings.selectedPresentationKey ?? IntegrationPipeline.PresentationKeyAuto;

            ui.SubHeader("OverHaulers_ProfileGroup_Cil".Translate().ToString());

            if (!IntegrationPipeline.HasScannedThisSession)
            {
                ui.DescriptionKey("OverHaulers_CilScanIdleDesc");
                ui.Gap(SettingsViewUtilities.CompactGap);

                if (ui.Button("OverHaulers_ScanPatchesBtn".Translate().ToString(), 280f))
                {
                    expandedAssemblyStatOwners.Clear();
                    IntegrationPipeline.TriggerManualScan();
                }
            }
            else
            {
                // Scan Completed Indicator
                string scanDoneLabel = "OverHaulers_ScanPatchesDone".Translate(IntegrationPipeline.DiscoveredPatches.Count).ToString();
                Vector2 scanDoneSize = SettingsViewUtilities.ButtonSize(scanDoneLabel, ui.Width, minWidth: 320f, minHeight: 26f);
                Rect scanDoneRect = new Rect(ui.X, ui.Y, scanDoneSize.x, scanDoneSize.y);
                bool prevGui = GUI.enabled;
                try
                {
                    GUI.enabled = false;
                    SettingsViewUtilities.DrawButton(scanDoneRect, scanDoneLabel);
                }
                finally
                {
                    GUI.enabled = prevGui;
                }
                ui.Y += scanDoneSize.y + 4f;

                if (IntegrationPipeline.DiscoveredPatches.Count == 0)
                {
                    DrawEmptyGroupNote(ref ui, "OverHaulers_NoExternalPatchesDiscovered".Translate().ToString());
                }
                else
                {
                    for (int p = 0; p < IntegrationPipeline.DiscoveredPatches.Count; p++)
                    {
                        DrawCilDiscoveredPatch(ref ui, IntegrationPipeline.DiscoveredPatches[p], currentKey, settings);
                    }
                }
            }
        }

        /// <summary>
        /// Draws the UI for a discovered CIL patch, including its candidate stats and selection options.
        /// </summary>
        /// <param name="ui">The settings layout reference.</param>
        /// <param name="patch">The discovered patch information.</param>
        /// <param name="currentKey">The currently selected presentation key.</param>
        /// <param name="settings">The current settings object.</param>
        private static void DrawCilDiscoveredPatch(ref SettingsLayout ui, DiscoveredPatchInfo patch, string currentKey, Settings settings)
        {
            Rect patchHeaderRect = new Rect(ui.X + 8f, ui.Y, ui.Width - 8f, 22f);
            GameFont origFont = Text.Font;
            Color origColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = SettingsLayout.MutedTextColor;
                Widgets.Label(patchHeaderRect, "OverHaulers_PatchHeader_OwnerFormat".Translate(patch.Owner, patch.PrimaryType).ToString());
            }
            finally
            {
                GUI.color = origColor;
                Text.Font = origFont;
            }
            ui.Y += 22f;

            if (patch.CandidateStats != null && patch.CandidateStats.Count > 0)
            {
                for (int s = 0; s < patch.CandidateStats.Count; s++)
                {
                    DrawCilStatOption(ref ui, patch.Owner, patch.CandidateStats[s], isSuggested: (s == 0), currentKey, settings);
                }
            }
            else
            {
                Rect emptyRect = new Rect(ui.X + 24f, ui.Y, ui.Width - 24f, 18f);
                GameFont origFont2 = Text.Font;
                Color origColor2 = GUI.color;
                try
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = SettingsLayout.MutedTextColor;
                    Widgets.Label(emptyRect, "OverHaulers_NoCandidateStatsInPatch".Translate().ToString());
                }
                finally
                {
                    GUI.color = origColor2;
                    Text.Font = origFont2;
                }
                ui.Y += 20f;
            }

            PopulateExtraAssemblyStats(patch, pooledExtraAssemblyStats);
            try
            {
                if (pooledExtraAssemblyStats.Count > 0)
                {
                    bool isExpanded = expandedAssemblyStatOwners.Contains(patch.Owner);
                    string toggleKey = isExpanded ? "OverHaulers_CilHideAllAssemblyStats" : "OverHaulers_CilShowAllAssemblyStats";
                    string toggleLabel = toggleKey.Translate(pooledExtraAssemblyStats.Count).ToString();

                    GameFont origToggleFont = Text.Font;
                    Text.Font = GameFont.Tiny;
                    try
                    {
                        Vector2 toggleSize = SettingsViewUtilities.ButtonSize(toggleLabel, ui.Width - 24f, minWidth: 180f, minHeight: 20f);
                        Rect toggleRect = new Rect(ui.X + 24f, ui.Y, toggleSize.x, toggleSize.y);
                        if (SettingsViewUtilities.DrawButton(toggleRect, toggleLabel))
                        {
                            if (isExpanded) expandedAssemblyStatOwners.Remove(patch.Owner);
                            else expandedAssemblyStatOwners.Add(patch.Owner);
                            isExpanded = !isExpanded;
                        }
                        ui.Y += toggleSize.y + 4f;
                    }
                    finally
                    {
                        Text.Font = origToggleFont;
                    }

                    if (isExpanded)
                    {
                        for (int e = 0; e < pooledExtraAssemblyStats.Count; e++)
                        {
                            DrawCilStatOption(ref ui, patch.Owner, pooledExtraAssemblyStats[e], isSuggested: false, currentKey, settings);
                        }
                    }
                }
            }
            finally
            {
                pooledExtraAssemblyStats.Clear();
            }

            ui.Y += 6f;
        }

        /// <summary>
        /// Draws a single CIL stat option row with its associated controls.
        /// </summary>
        /// <param name="ui">The layout context for drawing UI elements.</param>
        /// <param name="owner">The owner of the patch.</param>
        /// <param name="stat">The stat definition to display.</param>
        /// <param name="isSuggested">Indicates if this stat is suggested by the system.</param>
        /// <param name="currentKey">The currently selected presentation key.</param>
        /// <param name="settings">The current settings object.</param>
        private static void DrawCilStatOption(
            ref SettingsLayout ui,
            string owner,
            StatDef stat,
            bool isSuggested,
            string currentKey,
            Settings settings)
        {
            string targetKey = string.Format("{0}{1}:{2}", IntegrationPipeline.PresentationKeyCilPrefix, owner, stat.defName);
            bool isSelected = (currentKey == targetKey);

            string suggested = isSuggested ? (" - " + "OverHaulers_SuggestedStat".Translate().ToString()).Colorize(settings.colorBoosted) : "";
            string statLabel = string.Format("{0} ({1}){2}", stat.defName, stat.LabelCap, suggested);

            Rect statRadioRect = new Rect(ui.X + 24f, ui.Y, ui.Width - 24f, 24f);
            if (DrawCustomRadioRow(statRadioRect, statLabel, isSelected))
            {
                if (!isSelected)
                {
                    settings.selectedPresentationKey = targetKey;
                    settings.ResetCustomCilNeutralization();
                    IntegrationPipeline.SetCustomCilNeutralizedFactors(null);
                    IntegrationPipeline.SetCilAdditiveBonusMode(false);
                    IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
                }
            }
            ui.Y += 24f;

            DrawCilAdditiveBonusToggle(ref ui, isSelected, settings);
            DrawCilCandidateFactors(ref ui, stat, isSelected, settings);
        }

        /// <summary>
        /// Populates the list of extra assembly stats that are not already included as candidate stats.
        /// </summary>
        /// <param name="patch">The discovered patch containing assembly stats.</param>
        /// <param name="results">The list to populate with extra stats.</param>
        private static void PopulateExtraAssemblyStats(DiscoveredPatchInfo patch, List<StatDef> results)
        {
            results.Clear();
            if (patch.AllAssemblyStats == null || patch.AllAssemblyStats.Count == 0) return;

            for (int i = 0; i < patch.AllAssemblyStats.Count; i++)
            {
                StatDef stat = patch.AllAssemblyStats[i];
                if (stat == null || stat == StatDefOf.CarryingCapacity || stat == StatDefOf.Mass) continue;
                if (patch.CandidateStats != null && patch.CandidateStats.Contains(stat)) continue;
                results.Add(stat);
            }
        }

        /// <summary>
        /// Draws the additive-bonus mode checkbox for the currently selected CIL-discovered candidate stat.
        /// </summary>
        /// <param name="ui">The layout context for drawing UI elements.</param>
        /// <param name="isSelected">Indicates if the current stat is selected.</param>
        /// <param name="settings">The current settings object.</param>
        private static void DrawCilAdditiveBonusToggle(ref SettingsLayout ui, bool isSelected, Settings settings)
        {
            if (!isSelected) return;

            string label = SettingsViewUtilities.CachedText("OverHaulers_CilAdditiveBonus_Label");
            string tooltip = SettingsViewUtilities.CachedText("OverHaulers_CilAdditiveBonus_Tooltip");
            float rowWidth = ui.Width - 42f;
            float rowH = Math.Max(22f, SettingsViewUtilities.CalcTextHeight(label, Math.Max(1f, rowWidth - 30f), GameFont.Tiny));
            Rect checkRect = new Rect(ui.X + 42f, ui.Y, rowWidth, rowH);

            bool nextAdditive = settings.treatCilStatAsAdditive;
            GameFont origFont = Text.Font;
            try
            {
                Text.Font = GameFont.Tiny;
                Widgets.CheckboxLabeled(checkRect, label, ref nextAdditive);
            }
            finally
            {
                Text.Font = origFont;
            }
            if (!string.IsNullOrEmpty(tooltip)) TooltipHandler.TipRegion(checkRect, tooltip);
            ui.Y += rowH + 4f;

            if (nextAdditive != settings.treatCilStatAsAdditive)
            {
                settings.treatCilStatAsAdditive = nextAdditive;
                IntegrationPipeline.SetCilAdditiveBonusMode(nextAdditive);
                IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
            }
        }

        /// <summary>
        /// Draws the CIL candidate factors for the specified stat, highlighting those that overlap with Over Haulers' simulation.
        /// </summary>
        /// <param name="ui">The UI layout context.</param>
        /// <param name="stat">The stat for which to draw candidate factors.</param>
        /// <param name="isSelected">Indicates whether the stat is currently selected.</param>
        /// <param name="settings">The current settings instance.</param>
        private static void DrawCilCandidateFactors(ref SettingsLayout ui, StatDef stat, bool isSelected, Settings settings)
        {
            pooledCilFactors.Clear();
            GameFont origFont = Text.Font;
            Color origColor = GUI.color;
            try
            {
                IntegrationPipeline.PopulateAscertainedCapacityFactors(stat, pooledCilFactors);

                if (pooledCilFactors.Count == 0)
                {
                    if (isSelected)
                    {
                        Rect noneRect = new Rect(ui.X + 42f, ui.Y, ui.Width - 42f, 18f);
                        Text.Font = GameFont.Tiny;
                        GUI.color = SettingsLayout.MutedTextColor;
                        Widgets.Label(noneRect, SettingsViewUtilities.CachedText("OverHaulers_CilFactors_None"));
                        ui.Y += 20f;
                    }
                    return;
                }

                if (!isSelected)
                {
                    pooledCilFactorSummary.Clear();
                    for (int i = 0; i < pooledCilFactors.Count; i++)
                    {
                        if (i > 0) pooledCilFactorSummary.Append(", ");
                        AscertainedCapacityFactor f = pooledCilFactors[i];
                        pooledCilFactorSummary.Append(f.OverlapsOverHaulers
                            ? "OverHaulers_CilFactors_AutoTag".Translate(f.Label).ToString()
                            : f.Label);
                    }

                    string summaryText = "OverHaulers_CilFactors_Summary".Translate(pooledCilFactorSummary.ToString()).ToString();
                    float h = Math.Max(18f, SettingsViewUtilities.CalcTextHeight(summaryText, ui.Width - 42f, GameFont.Tiny));
                    Rect summaryRect = new Rect(ui.X + 42f, ui.Y, ui.Width - 42f, h);
                    Text.Font = GameFont.Tiny;
                    GUI.color = SettingsLayout.MutedTextColor;
                    Widgets.Label(summaryRect, summaryText);
                    ui.Y += h + 2f;
                    return;
                }

                // Selected CIL candidate: render interactive factor neutralization checkboxes
                Rect headerRect = new Rect(ui.X + 42f, ui.Y, ui.Width - 42f, 18f);
                Text.Font = GameFont.Tiny;
                GUI.color = SettingsLayout.MutedTextColor;
                Widgets.Label(headerRect, SettingsViewUtilities.CachedText("OverHaulers_CilFactors_Header"));
                GUI.color = origColor;
                ui.Y += 20f;

                bool listModified = false;
                for (int i = 0; i < pooledCilFactors.Count; i++)
                {
                    AscertainedCapacityFactor f = pooledCilFactors[i];
                    bool defaultAutoNeutralize = !settings.treatCilStatAsAdditive && f.OverlapsOverHaulers;
                    bool isChecked = settings.useCustomCilNeutralization
                        ? ContainsIgnoreCase(settings.cilNeutralizedFactors, f.DefName)
                        : defaultAutoNeutralize;

                    string weightText = f.Weight.ToString("0.##");
                    string rowLabel = (f.OverlapsOverHaulers
                        ? "OverHaulers_CilFactors_RowOverlap".Translate(f.Label, weightText)
                        : "OverHaulers_CilFactors_Row".Translate(f.Label, weightText)).ToString();

                    float rowWidth = ui.Width - 52f;
                    float rowH = Math.Max(22f, SettingsViewUtilities.CalcTextHeight(rowLabel, Math.Max(1f, rowWidth - 30f), GameFont.Tiny));
                    Rect checkRect = new Rect(ui.X + 52f, ui.Y, rowWidth, rowH);

                    bool nextChecked = isChecked;
                    Text.Font = GameFont.Tiny;
                    Widgets.CheckboxLabeled(checkRect, rowLabel, ref nextChecked);
                    ui.Y += rowH + 2f;

                    if (nextChecked != isChecked)
                    {
                        if (!settings.useCustomCilNeutralization)
                        {
                            // Materialize current effective auto-detected defaults before toggling one factor
                            settings.cilNeutralizedFactors.Clear();
                            if (!settings.treatCilStatAsAdditive)
                            {
                                for (int d = 0; d < pooledCilFactors.Count; d++)
                                {
                                    if (pooledCilFactors[d].OverlapsOverHaulers)
                                    {
                                        settings.cilNeutralizedFactors.Add(pooledCilFactors[d].DefName);
                                    }
                                }
                            }
                            settings.useCustomCilNeutralization = true;
                        }

                        if (nextChecked)
                        {
                            if (!ContainsIgnoreCase(settings.cilNeutralizedFactors, f.DefName))
                            {
                                settings.cilNeutralizedFactors.Add(f.DefName);
                            }
                        }
                        else
                        {
                            settings.cilNeutralizedFactors.RemoveAll(s => string.Equals(s, f.DefName, StringComparison.OrdinalIgnoreCase));
                        }

                        listModified = true;
                    }
                }

                if (listModified)
                {
                    IntegrationPipeline.SetCustomCilNeutralizedFactors(settings.cilNeutralizedFactors);
                    IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
                }
            }
            finally
            {
                GUI.color = origColor;
                Text.Font = origFont;
                pooledCilFactors.Clear();
                pooledCilFactorSummary.Clear();
            }
        }

        /// <summary>
        /// Determines whether the specified list contains the given value, ignoring case.
        /// </summary>
        /// <param name="list">The list of strings to search.</param>
        /// <param name="value">The value to look for.</param>
        /// <returns>True if the list contains the value, ignoring case; otherwise, false.</returns>
        private static bool ContainsIgnoreCase(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        #endregion

        #region 6. PRIVATE UI HELPERS

        /// <summary>
        /// Returns true if CIL discovery has identified at least one external candidate StatDef.
        /// </summary>
        private static bool HasDiscoveredCandidateStats()
        {
            var patches = IntegrationPipeline.DiscoveredPatches;
            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i].CandidateStats != null && patches[i].CandidateStats.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Draws an empty group note in the settings UI.
        /// </summary>
        /// <param name="ui">The settings layout reference.</param>
        /// <param name="note">The note text to display.</param>
        private static void DrawEmptyGroupNote(ref SettingsLayout ui, string note)
        {
            Rect rect = new Rect(ui.X + 12f, ui.Y, ui.Width - 12f, 20f);
            GameFont origFont = Text.Font;
            Color origColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = SettingsLayout.MutedTextColor;
                Widgets.Label(rect, string.Format("- {0}", note));
            }
            finally
            {
                GUI.color = origColor;
                Text.Font = origFont;
            }
            ui.Y += 22f;
        }

        /// <summary>
        /// Resolves the badge text for the current stat presentation based on its active adoption source.
        /// </summary>
        /// <returns>The resolved badge text for the current stat presentation.</returns>
        private static string ResolveStatPresentationBadge()
        {
            if (!IntegrationPipeline.IsForeignStatAdopted)
            {
                return "OverHaulers_Badge_NativeCard".Translate().ToString();
            }

            switch (IntegrationPipeline.ActiveAdoptionSource)
            {
                case StatAdoptionSource.XmlProfile:
                    return "OverHaulers_Badge_AdoptedXml".Translate().ToString();
                case StatAdoptionSource.CilDiscovered:
                    return "OverHaulers_Badge_AdoptedBytecode".Translate().ToString();
                default:
                    return "OverHaulers_Badge_AdoptedStat".Translate().ToString();
            }
        }

        /// <summary>
        /// Draws a custom radio button row with a label.
        /// </summary>
        /// <param name="rect">The rectangle area for the row.</param>
        /// <param name="label">The label text for the row.</param>
        /// <param name="chosen">Whether the radio button is selected.</param>
        /// <returns>True if the radio button or row was clicked, otherwise false.</returns>
        private static bool DrawCustomRadioRow(Rect rect, string label, bool chosen)
        {
            float radioSize = 20f;
            Rect circleRect = new Rect(rect.x, rect.y + (rect.height - radioSize) / 2f, radioSize, radioSize);
            Rect labelRect = new Rect(rect.x + radioSize + 6f, rect.y, rect.width - radioSize - 6f, rect.height);

            bool radioClicked = Widgets.RadioButton(circleRect.x, circleRect.y, chosen);
            bool rowClicked = Widgets.ButtonInvisible(rect);

            Text.Font = GameFont.Small;
            Widgets.Label(labelRect, label);

            return radioClicked || rowClicked;
        }

        #endregion
    }
}
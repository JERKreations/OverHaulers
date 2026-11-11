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

        public static void DrawIntegrationTabContents(ref SettingsLayout ui, Settings settings)
        {
            // 0. Section Header & Reset Action
            DrawSectionHeaderWithReset(ref ui, "OverHaulers_CompatibilityHeader", () =>
            {
                settings.ResetCompatibility();
                IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
                SettingsLayout.ClearInputBuffers();
                SettingsViewUtilities.OnSettingMutated();
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

        private static void DrawActiveStatBanner(ref SettingsLayout ui, Settings settings)
        {
            string activeStatName = IntegrationPipeline.ActiveStatOwner;
            string activeBadgeType = ResolveStatPresentationBadge();
            string activeLabel = "OverHaulers_ActiveStatBanner".Translate(activeStatName, activeBadgeType).ToString();

            Rect bannerRect = new Rect(ui.X, ui.Y, ui.Width, 26f);
            Widgets.DrawBoxSolid(bannerRect, new Color(0.12f, 0.15f, 0.18f, 0.85f));
            Widgets.DrawBox(bannerRect);

            Text.Font = GameFont.Tiny;
            Color origColor = GUI.color;
            GUI.color = settings.colorBoosted;
            Widgets.Label(new Rect(bannerRect.x + 8f, bannerRect.y + 5f, bannerRect.width - 16f, 18f), activeLabel);
            GUI.color = origColor;
            Text.Font = GameFont.Small;

            ui.Y += 32f;
        }

        private struct VerificationGuide
        {
            public string StatusTitle;
            public Color StatusColor;
            public string DisplayLine;
            public string GearLine;
            public string ConfirmLine;
        }

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
            bool isCilLocked = key.StartsWith(IntegrationPipeline.PresentationKeyCilPrefix);
            bool hasExternalPatches = IntegrationPipeline.DiscoveredPatches.Count > 0;

            // Scenario C: Side-by-Side (Forced Standalone with external patches active)
            if (isForcedStandalone && hasExternalPatches)
            {
                return new VerificationGuide
                {
                    StatusTitle = "OverHaulers_Verify_SideBySide_Title".Translate().ToString(),
                    StatusColor = Color.yellow,
                    DisplayLine = "OverHaulers_Verify_SideBySide_Display".Translate(statLabel).ToString(),
                    GearLine = "OverHaulers_Verify_SideBySide_Gear".Translate().ToString(),
                    ConfirmLine = "OverHaulers_Verify_SideBySide_Confirm".Translate().ToString()
                };
            }

            // Scenario D: Custom Dynamic CIL Hook
            if (isCilLocked)
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

            // Scenario A: Optimal Unified Integration (Adopted Foreign Stat Card)
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

            // Scenario B: Native Standalone Mode (Vanilla Game)
            return new VerificationGuide
            {
                StatusTitle = "OverHaulers_Verify_Standalone_Title".Translate().ToString(),
                StatusColor = settings.colorHealthy,
                DisplayLine = "OverHaulers_Verify_Standalone_Display".Translate(statLabel, categoryLabel).ToString(),
                GearLine = "OverHaulers_Verify_Standalone_Gear".Translate().ToString(),
                ConfirmLine = "OverHaulers_Verify_Standalone_Confirm".Translate(statLabel).ToString()
            };
        }

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

            // Title Line
            Rect titleRect = new Rect(cardRect.x + padding, curY, textWidth, 20f);
            Text.Font = GameFont.Tiny;
            GUI.color = guide.StatusColor;
            Widgets.Label(titleRect, string.Format("● {0}", guide.StatusTitle.ToUpperInvariant()));
            GUI.color = Color.white;
            curY += 22f;

            // 3 Verification Lines
            DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hDisplay, "OverHaulers_Verify_Field_Display".Translate().ToString(), guide.DisplayLine);
            DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hGear, "OverHaulers_Verify_Field_Gear".Translate().ToString(), guide.GearLine);
            DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hConfirm, "OverHaulers_Verify_Field_Confirm".Translate().ToString(), guide.ConfirmLine);

            Text.Font = GameFont.Small;
            GUI.color = origColor;

            ui.Y += cardHeight + 10f;
        }

        private static void DrawVerificationLine(float startX, ref float curY, float prefixWidth, float valueWidth, float lineHeight, string prefix, string text)
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
                IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
            }
            if (!string.IsNullOrEmpty(standaloneTooltip)) TooltipHandler.TipRegion(standaloneRowRect, standaloneTooltip);
            ui.Y += SettingsLayout.DefaultToggleHeight + SettingsViewUtilities.ControlGap;
        }

        #endregion

        #region 4. DECLARATIVE XML PROFILES

        private static void DrawXmlProfilesSection(ref SettingsLayout ui, Settings settings)
        {
            string currentKey = settings.selectedPresentationKey ?? IntegrationPipeline.PresentationKeyAuto;
            List<MassCapacityStatProfileDef> allXmlProfiles = DefDatabase<MassCapacityStatProfileDef>.AllDefsListForReading;

            List<MassCapacityStatProfileDef> builtInProfiles = new List<MassCapacityStatProfileDef>();
            List<MassCapacityStatProfileDef> thirdPartyProfiles = new List<MassCapacityStatProfileDef>();

            if (allXmlProfiles != null)
            {
                for (int i = 0; i < allXmlProfiles.Count; i++)
                {
                    MassCapacityStatProfileDef def = allXmlProfiles[i];
                    if (def.modContentPack == OverHaulers.ContentPack)
                    {
                        builtInProfiles.Add(def);
                    }
                    else
                    {
                        thirdPartyProfiles.Add(def);
                    }
                }
            }

            ui.SubHeader("OverHaulers_ProfileGroup_BuiltIn".Translate().ToString());

            if (builtInProfiles.Count == 0)
            {
                DrawEmptyGroupNote(ref ui, "OverHaulers_ProfileGroup_None".Translate().ToString());
            }
            else
            {
                for (int i = 0; i < builtInProfiles.Count; i++)
                {
                    DrawXmlProfileEntry(ref ui, builtInProfiles[i], currentKey, settings);
                }
            }

            if (thirdPartyProfiles.Count > 0)
            {
                ui.Gap(SettingsViewUtilities.CompactGap);
                ui.SubHeader("OverHaulers_ProfileGroup_ThirdParty".Translate().ToString());
                for (int i = 0; i < thirdPartyProfiles.Count; i++)
                {
                    DrawXmlProfileEntry(ref ui, thirdPartyProfiles[i], currentKey, settings);
                }
            }

            ui.Gap(SettingsViewUtilities.ControlGap);
        }

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
                    IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
                }

                Text.Font = GameFont.Tiny;
                Color origColor = GUI.color;
                GUI.color = isSelected ? settings.colorBoosted : settings.colorHealthy;
                Widgets.Label(badgeRect, "OverHaulers_ModActive".Translate().ToString());
                GUI.color = origColor;
                Text.Font = GameFont.Small;
            }
            else
            {
                bool prevGui = GUI.enabled;
                GUI.enabled = false;
                DrawCustomRadioRow(radioRect, profileLabel, false);

                Text.Font = GameFont.Tiny;
                Color origColor = GUI.color;
                GUI.color = SettingsLayout.MutedTextColor;
                Widgets.Label(badgeRect, "OverHaulers_ModNotLoaded".Translate().ToString());
                GUI.color = origColor;
                Text.Font = GameFont.Small;
                GUI.enabled = prevGui;
            }

            ui.Y += 26f;
        }

        #endregion

        #region 5. DYNAMIC BYTECODE DISCOVERIES (CIL SCANNER)

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
                GUI.enabled = false;
                SettingsViewUtilities.DrawButton(scanDoneRect, scanDoneLabel);
                GUI.enabled = prevGui;
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

        private static void DrawCilDiscoveredPatch(ref SettingsLayout ui, DiscoveredPatchInfo patch, string currentKey, Settings settings)
        {
            Rect patchHeaderRect = new Rect(ui.X + 8f, ui.Y, ui.Width - 8f, 22f);
            Text.Font = GameFont.Tiny;
            Color origColor = GUI.color;
            GUI.color = SettingsLayout.MutedTextColor;
            Widgets.Label(patchHeaderRect, "OverHaulers_PatchHeader_OwnerFormat".Translate(patch.Owner, patch.PrimaryType).ToString());
            GUI.color = origColor;
            Text.Font = GameFont.Small;
            ui.Y += 22f;

            if (patch.CandidateStats != null && patch.CandidateStats.Count > 0)
            {
                for (int s = 0; s < patch.CandidateStats.Count; s++)
                {
                    StatDef stat = patch.CandidateStats[s];
                    string targetKey = string.Format("{0}{1}:{2}", IntegrationPipeline.PresentationKeyCilPrefix, patch.Owner, stat.defName);
                    bool isSelected = (currentKey == targetKey);

                    string suggested = (s == 0) ? (" - " + "OverHaulers_SuggestedStat".Translate().ToString()).Colorize(settings.colorBoosted) : "";
                    string statLabel = string.Format("{0} ({1}){2}", stat.defName, stat.LabelCap, suggested);

                    Rect statRadioRect = new Rect(ui.X + 24f, ui.Y, ui.Width - 24f, 24f);
                    if (DrawCustomRadioRow(statRadioRect, statLabel, isSelected))
                    {
                        settings.selectedPresentationKey = targetKey;
                        IntegrationPipeline.RebindPresentation(settings.selectedPresentationKey);
                    }
                    ui.Y += 24f;
                }
            }
            else
            {
                Rect emptyRect = new Rect(ui.X + 24f, ui.Y, ui.Width - 24f, 18f);
                Text.Font = GameFont.Tiny;
                Color origColor2 = GUI.color;
                GUI.color = SettingsLayout.MutedTextColor;
                Widgets.Label(emptyRect, "OverHaulers_NoCandidateStatsInPatch".Translate().ToString());
                GUI.color = origColor2;
                Text.Font = GameFont.Small;
                ui.Y += 20f;
            }

            ui.Y += 6f;
        }

        #endregion

        #region 6. PRIVATE UI HELPERS

        private static void DrawEmptyGroupNote(ref SettingsLayout ui, string note)
        {
            Rect rect = new Rect(ui.X + 12f, ui.Y, ui.Width - 12f, 20f);
            Text.Font = GameFont.Tiny;
            Color origColor = GUI.color;
            GUI.color = SettingsLayout.MutedTextColor;
            Widgets.Label(rect, string.Format("— {0}", note));
            GUI.color = origColor;
            Text.Font = GameFont.Small;
            ui.Y += 22f;
        }

        private static string ResolveStatPresentationBadge()
        {
            if (!IntegrationPipeline.IsForeignStatAdopted)
            {
                return "OverHaulers_Badge_NativeCard".Translate().ToString();
            }

            string key = OverHaulers.settings != null ? OverHaulers.settings.selectedPresentationKey : "";
            if (!string.IsNullOrEmpty(key) && key.StartsWith(IntegrationPipeline.PresentationKeyCilPrefix))
            {
                return "OverHaulers_Badge_AdoptedBytecode".Translate().ToString();
            }

            List<MassCapacityStatProfileDef> xmlProfiles = DefDatabase<MassCapacityStatProfileDef>.AllDefsListForReading;
            if (xmlProfiles != null)
            {
                for (int i = 0; i < xmlProfiles.Count; i++)
                {
                    string id = xmlProfiles[i].label ?? xmlProfiles[i].defName;
                    if (string.Equals(IntegrationPipeline.ActiveStatOwner, id, StringComparison.OrdinalIgnoreCase))
                    {
                        return "OverHaulers_Badge_AdoptedXml".Translate().ToString();
                    }
                }
            }

            return "OverHaulers_Badge_AdoptedStat".Translate().ToString();
        }

        private static bool DrawCustomRadioRow(Rect rect, string label, bool chosen)
        {
            float radioSize = 20f;
            Rect circleRect = new Rect(rect.x, rect.y + (rect.height - radioSize) / 2f, radioSize, radioSize);
            Rect labelRect = new Rect(rect.x + radioSize + 6f, rect.y, rect.width - radioSize - 6f, rect.height);

            bool clicked = Widgets.RadioButton(circleRect.x, circleRect.y, chosen) || Widgets.ButtonInvisible(rect);

            Text.Font = GameFont.Small;
            Widgets.Label(labelRect, label);

            return clicked;
        }

        #endregion
    }
}
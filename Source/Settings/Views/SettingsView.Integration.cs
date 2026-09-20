using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    public static partial class SettingsView
    {
        #region [SEC-08] COMPATIBILITY & HARMONY OVERRIDES DRAWER

        /// <summary>
        /// Renders the complete Compatibility and External Mod Integration settings panel.
        /// Executes a deterministic order-of-operations pass to ensure zero IMGUI font leaks,
        /// dynamic height adaptation, and real-time in-game verification guidance.
        /// </summary>
        public static void DrawIntegrationSection(Rect viewRect, ref float currentY, Settings settings, Action resetAction)
        {
            Rect inner = SettingsViewUtilities.BeginSectionBoxDirect(viewRect, "SEC_Compatibility", ref currentY, fallbackEstimate: 460f);
            float startY = inner.y;
            float localY = inner.y;

            try
            {
                // ---------------------------------------------------------------------
                // 0. SECTION HEADER & RESET ACTION
                // ---------------------------------------------------------------------
                Action customResetAction = () =>
                {
                    resetAction?.Invoke();
                    IntegrationPipeline.RebindDriver();
                };

                SettingsViewUtilities.DrawHeaderWithResetDirect(inner, "OverHaulers_CompatibilityHeader", customResetAction, localY, out localY);

                string sectionDesc = "OverHaulers_Compatibility_Desc".Translate().ToString();
                localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, sectionDesc, localY);

                // ---------------------------------------------------------------------
                // 1. ACTIVE DRIVER STATUS READOUT BANNER
                // ---------------------------------------------------------------------
                // string currentKey = settings.selectedDriverKey ?? IntegrationPipeline.DriverKeyAuto;
                string currentKey = settings.selectedDriverKey ?? SettingsDefaults.DefaultSelectedDriverKey; // TEMP PATCH
                string activeDriverName = IntegrationPipeline.ActiveDriver?.DriverIdentifier ?? "Standalone";
                string activeDriverType = ResolveDriverTypeBadge(IntegrationPipeline.ActiveDriver);

                Rect activeBannerRect = new Rect(inner.x, localY, inner.width, 24f);
                Widgets.DrawBoxSolid(activeBannerRect, SettingsViewUtilities.DarkTargetHighlightColor);

                string activeLabel = "OverHaulers_ActiveDriverBanner".Translate(activeDriverName, activeDriverType).ToString();
                
                Text.Font = GameFont.Tiny;
                GUI.color = settings.colorBoosted;
                Widgets.Label(new Rect(activeBannerRect.x + 8f, activeBannerRect.y + 4f, activeBannerRect.width - 16f, 18f), activeLabel);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                localY += 28f;

                // ---------------------------------------------------------------------
                // 1B. DYNAMIC IN-GAME VERIFICATION & WHAT-TO-EXPECT CARD
                // ---------------------------------------------------------------------
                VerificationGuide guide = BuildVerificationGuide(settings, IntegrationPipeline.ActiveDriver);
                DrawVerificationGuideCard(inner, guide, ref localY);

                // ---------------------------------------------------------------------
                // 2. TOP-LEVEL MODE SELECTION (AUTO VS STANDALONE)
                // ---------------------------------------------------------------------
                bool isAutoSelected = currentKey == IntegrationPipeline.DriverKeyAuto;
                bool isStandaloneSelected = currentKey == IntegrationPipeline.DriverKeyStandalone;

                // Option A: Automatic Selection (Recommended)
                Rect autoRowRect = new Rect(inner.x, localY, inner.width, 24f);
                if (DrawCustomRadioRow(autoRowRect, "OverHaulers_DriverMode_Auto".Translate().ToString(), isAutoSelected))
                {
                    settings.selectedDriverKey = IntegrationPipeline.DriverKeyAuto;
                    IntegrationPipeline.RebindDriver();
                }
                TooltipHandler.TipRegion(autoRowRect, "OverHaulers_DriverMode_Auto_Tooltip".Translate().ToString());
                localY += 26f;

                // Option B: Force Standalone Direct Mode
                Rect standaloneRowRect = new Rect(inner.x, localY, inner.width, 24f);
                if (DrawCustomRadioRow(standaloneRowRect, "OverHaulers_DriverMode_Standalone".Translate().ToString(), isStandaloneSelected))
                {
                    settings.selectedDriverKey = IntegrationPipeline.DriverKeyStandalone;
                    IntegrationPipeline.RebindDriver();
                }
                TooltipHandler.TipRegion(standaloneRowRect, "OverHaulers_DriverMode_Standalone_Tooltip".Translate().ToString());
                localY += 32f;

                // ---------------------------------------------------------------------
                // 3. DECLARATIVE XML PROFILES (BUILT-IN & THIRD-PARTY)
                // ---------------------------------------------------------------------
                List<MassCapacityDriverDef> allXmlDrivers = DefDatabase<MassCapacityDriverDef>.AllDefsListForReading;

                List<MassCapacityDriverDef> builtInDrivers = new List<MassCapacityDriverDef>();
                List<MassCapacityDriverDef> thirdPartyDrivers = new List<MassCapacityDriverDef>();

                if (allXmlDrivers != null)
                {
                    for (int i = 0; i < allXmlDrivers.Count; i++)
                    {
                        var def = allXmlDrivers[i];
                        if (def.modContentPack == OverHaulers.ContentPack) builtInDrivers.Add(def);
                        else thirdPartyDrivers.Add(def);
                    }
                }

                DrawGroupSubHeader(inner, "OverHaulers_DriverGroup_BuiltIn".Translate().ToString(), ref localY);

                if (builtInDrivers.Count == 0)
                {
                    DrawEmptyGroupNote(inner, "OverHaulers_DriverGroup_None".Translate().ToString(), ref localY);
                }
                else
                {
                    for (int i = 0; i < builtInDrivers.Count; i++)
                    {
                        DrawXmlDriverEntry(inner, builtInDrivers[i], currentKey, settings, ref localY);
                    }
                }

                if (thirdPartyDrivers.Count > 0)
                {
                    localY += 6f;
                    DrawGroupSubHeader(inner, "OverHaulers_DriverGroup_ThirdParty".Translate().ToString(), ref localY);
                    for (int i = 0; i < thirdPartyDrivers.Count; i++)
                    {
                        DrawXmlDriverEntry(inner, thirdPartyDrivers[i], currentKey, settings, ref localY);
                    }
                }

                localY += 10f;

                // ---------------------------------------------------------------------
                // 4. EXPLICIT C# REGISTRATIONS (MOD API)
                // ---------------------------------------------------------------------
                DrawGroupSubHeader(inner, "OverHaulers_DriverGroup_CSharp".Translate().ToString(), ref localY);
                DrawEmptyGroupNote(inner, "OverHaulers_DriverGroup_CSharp_None".Translate().ToString(), ref localY);
                localY += 10f;

                // ---------------------------------------------------------------------
                // 5. DYNAMIC BYTECODE DISCOVERIES (LAZY CIL SCANNING)
                // ---------------------------------------------------------------------
                DrawGroupSubHeader(inner, "OverHaulers_DriverGroup_Cil".Translate().ToString(), ref localY);

                if (!IntegrationPipeline.HasScannedThisSession)
                {
                    // Scan is Idle: Instructions + Trigger Button
                    localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, "OverHaulers_CilScanIdleDesc".Translate().ToString(), localY);

                    Rect scanBtnRect = new Rect(inner.x + 4f, localY, Mathf.Min(320f, inner.width - 8f), 26f);
                    if (Widgets.ButtonText(scanBtnRect, "OverHaulers_ScanPatchesBtn".Translate().ToString()))
                    {
                        IntegrationPipeline.TriggerManualScan();
                    }
                    localY += 32f;
                }
                else
                {
                    // Scan Ran: Session-Locked Indicator
                    Rect scanDoneRect = new Rect(inner.x + 4f, localY, Mathf.Min(320f, inner.width - 8f), 24f);
                    GUI.enabled = false;
                    Widgets.ButtonText(scanDoneRect, "OverHaulers_ScanPatchesDone".Translate(IntegrationPipeline.DiscoveredPatches.Count).ToString());
                    GUI.enabled = true;
                    localY += 28f;

                    if (IntegrationPipeline.DiscoveredPatches.Count == 0)
                    {
                        DrawEmptyGroupNote(inner, "OverHaulers_NoExternalPatchesDiscovered".Translate().ToString(), ref localY);
                    }
                    else
                    {
                        for (int p = 0; p < IntegrationPipeline.DiscoveredPatches.Count; p++)
                        {
                            DrawCilDiscoveredPatch(inner, IntegrationPipeline.DiscoveredPatches[p], currentKey, settings, ref localY);
                        }
                    }
                }

                // Generous bottom margin to guarantee zero box-border clipping
                localY += 8f;
            }
            finally
            {
                SettingsViewUtilities.EndSectionBoxDirect("SEC_Compatibility", startY, localY, ref currentY);
            }
        }

        #endregion

        #region IN-GAME VERIFICATION & WHAT-TO-EXPECT ENGINE

        /// <summary>
        /// Represents the verification guide containing status and guidance lines for in-game verification.
        /// This struct encapsulates the status title, status color, and the various guidance lines to be displayed in the settings view.
        /// </summary>
        private struct VerificationGuide
        {
            public string StatusTitle;
            public Color StatusColor;
            public string DisplayLine;
            public string GearLine;
            public string ConfirmLine;
        }

        /// <summary>
        /// Builds a verification guide based on the current settings and active pipeline driver, providing status and guidance for in-game verification.
        /// </summary>
        /// <param name="settings">The current settings object containing user preferences and selected driver key.</param>
        /// <param name="activeDriver">The active pipeline driver providing context for the verification guide.</param>
        /// <returns>A VerificationGuide struct populated with status and guidance lines for in-game verification.</returns>
        private static VerificationGuide BuildVerificationGuide(Settings settings, IPipelineDriver activeDriver)
        {
            // Dynamically resolve stat label and category header, falling back to agnostic terminology
            StatDef stat = activeDriver?.ActiveMassCapacityStat;

            string statLabel = (stat != null && !string.IsNullOrEmpty(stat.label))
                ? stat.LabelCap.ToString()
                : "OverHaulers_Verify_FallbackStat".Translate().ToString();

            string categoryLabel = (stat?.category != null && !string.IsNullOrEmpty(stat.category.label))
                ? stat.category.LabelCap.ToString()
                : "OverHaulers_Verify_FallbackCategory".Translate().ToString();

            // string key = settings?.selectedDriverKey ?? IntegrationPipeline.DriverKeyAuto;
            string key = settings?.selectedDriverKey ?? SettingsDefaults.DefaultSelectedDriverKey; // TEMP PATCH
            bool isForcedStandalone = key == IntegrationPipeline.DriverKeyStandalone;
            bool isCilLocked = key.StartsWith(IntegrationPipeline.DriverKeyCilPrefix);
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
            if (isCilLocked || (activeDriver is GenericStatDriver && key.StartsWith(IntegrationPipeline.DriverKeyCilPrefix)))
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

            // Scenario A: Optimal Unified Integration (XML or C# Driver)
            if (activeDriver is GenericStatDriver || (activeDriver != null && !(activeDriver is VanillaStatDriver)))
            {
                return new VerificationGuide
                {
                    StatusTitle = "OverHaulers_Verify_Unified_Title".Translate().ToString(),
                    StatusColor = settings?.colorBoosted ?? SettingsDefaults.ColorBoostedDefault,
                    DisplayLine = "OverHaulers_Verify_Unified_Display".Translate(statLabel, categoryLabel).ToString(),
                    GearLine = "OverHaulers_Verify_Unified_Gear".Translate().ToString(),
                    ConfirmLine = "OverHaulers_Verify_Unified_Confirm".Translate(statLabel).ToString()
                };
            }

            // Scenario B: Native Standalone Mode (Vanilla Game)
            return new VerificationGuide
            {
                StatusTitle = "OverHaulers_Verify_Standalone_Title".Translate().ToString(),
                StatusColor = settings?.colorHealthy ?? SettingsDefaults.ColorHealthyDefault,
                DisplayLine = "OverHaulers_Verify_Standalone_Display".Translate(statLabel, categoryLabel).ToString(),
                GearLine = "OverHaulers_Verify_Standalone_Gear".Translate().ToString(),
                ConfirmLine = "OverHaulers_Verify_Standalone_Confirm".Translate(statLabel).ToString()
            };
        }

        /// <summary>
        /// Draws the verification guide card within the settings view, displaying the status, display line, gear line, and confirmation line.
        /// </summary>
        /// <param name="inner">The inner rectangle defining the drawing area for the verification guide card.</param>
        /// <param name="guide">The verification guide containing the lines and status to be displayed.</param>
        /// <param name="localY">The local Y-coordinate for positioning the card, passed by reference to allow vertical stacking.</param>
        private static void DrawVerificationGuideCard(Rect inner, in VerificationGuide guide, ref float localY)
        {
            float padding = 8f;
            float textWidth = inner.width - (padding * 2f);
            float prefixWidth = 100f;
            float valueWidth = textWidth - prefixWidth;

            // Safe Order-of-Operations: Font must be explicitly Tiny during height measurement
            GameFont origFont = Text.Font;
            Color origColor = GUI.color;
            Text.Font = GameFont.Tiny;

            float hDisplay;
            float hGear;
            float hConfirm;

            try
            {
                hDisplay = Mathf.Max(18f, Text.CalcHeight(guide.DisplayLine, valueWidth));
                hGear = Mathf.Max(18f, Text.CalcHeight(guide.GearLine, valueWidth));
                hConfirm = Mathf.Max(18f, Text.CalcHeight(guide.ConfirmLine, valueWidth));
            }
            finally
            {
                Text.Font = origFont;
            }

            float cardHeight = 26f + hDisplay + hGear + hConfirm + (padding * 2f) + 6f;

            Rect cardRect = new Rect(inner.x, localY, inner.width, cardHeight);
            Widgets.DrawBoxSolid(cardRect, new Color(0.10f, 0.12f, 0.14f, 0.85f));
            Widgets.DrawHighlightIfMouseover(cardRect);

            float curY = cardRect.y + padding;

            // Title Line
            Rect titleRect = new Rect(cardRect.x + padding, curY, textWidth, 20f);
            Text.Font = GameFont.Tiny;
            GUI.color = guide.StatusColor;
            Widgets.Label(titleRect, $"● {guide.StatusTitle.ToUpperInvariant()}");
            GUI.color = Color.white;
            curY += 22f;

            // Diagnostic Lines
            DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hDisplay, 
                "OverHaulers_Verify_Field_Display".Translate().ToString(), guide.DisplayLine);

            DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hGear, 
                "OverHaulers_Verify_Field_Gear".Translate().ToString(), guide.GearLine);

            DrawVerificationLine(cardRect.x + padding, ref curY, prefixWidth, valueWidth, hConfirm, 
                "OverHaulers_Verify_Field_Confirm".Translate().ToString(), guide.ConfirmLine);

            Text.Font = GameFont.Small;
            GUI.color = origColor;

            localY += cardHeight + 12f;
        }

        /// <summary>
        /// Draws a single verification line within the settings view, consisting of a prefix and corresponding text value.
        /// </summary>
        /// <param name="startX">The starting X-coordinate for the line.</param>
        /// <param name="curY">The current Y-coordinate for the line, passed by reference to allow vertical stacking.</param>
        /// <param name="prefixWidth">The width allocated for the prefix label.</param>
        /// <param name="valueWidth">The width allocated for the text value.</param>
        /// <param name="lineHeight">The height of the line.</param>
        /// <param name="prefix">The prefix text to be displayed before the value.</param>
        /// <param name="text">The text value corresponding to the prefix.</param>
        private static void DrawVerificationLine(float startX, ref float curY, float prefixWidth, float valueWidth, float lineHeight, string prefix, string text)
        {
            Rect prefixRect = new Rect(startX, curY, prefixWidth, lineHeight);
            Rect textRect = new Rect(startX + prefixWidth, curY, valueWidth, lineHeight);

            GUI.color = SettingsViewUtilities.DescriptionTextColor;
            Widgets.Label(prefixRect, $"• {prefix}");
            GUI.color = Color.white;
            Widgets.Label(textRect, text);

            curY += lineHeight + 2f;
        }

        #endregion

        #region PRIVATE UI DRAW HELPERS

        /// <summary>
        /// Draws a sub-header for a group within the settings view.
        /// </summary>
        /// <param name="inner">The inner rectangle defining the drawing area for the sub-header.</param>
        /// <param name="text">The text of the sub-header to be displayed.</param>
        /// <param name="localY">The local Y-coordinate for positioning the sub-header, passed by reference to allow vertical stacking.</param>
        private static void DrawGroupSubHeader(Rect inner, string text, ref float localY)
        {
            Rect rect = new Rect(inner.x, localY, inner.width, 20f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 0.8f, 0.3f);
            Widgets.Label(rect, text.ToUpperInvariant());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            localY += 22f;
        }

        /// <summary>
        /// Draws a note indicating that a group is empty within the settings view.
        /// </summary>
        /// <param name="inner">The inner rectangle defining the drawing area for the note.</param>
        /// <param name="note">The text of the note to be displayed.</param>
        /// <param name="localY">The local Y-coordinate for positioning the note, passed by reference to allow vertical stacking.</param>
        private static void DrawEmptyGroupNote(Rect inner, string note, ref float localY)
        {
            Rect rect = new Rect(inner.x + 12f, localY, inner.width - 12f, 20f);
            Text.Font = GameFont.Tiny;
            GUI.color = SettingsViewUtilities.DescriptionTextColor;
            Widgets.Label(rect, $"— {note}");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            localY += 22f;
        }

        /// <summary>
        /// Draws the UI elements for an XML driver entry within the settings view.
        /// </summary>
        /// <param name="inner">The inner rectangle defining the drawing area for the driver entry UI elements.</param>
        /// <param name="def">The XML driver definition to be displayed.</param>
        /// <param name="currentKey">The currently selected driver key for comparison.</param>
        /// <param name="settings">The current settings object containing user preferences and selections.</param>
        /// <param name="localY">The local Y-coordinate for positioning the UI elements, passed by reference to allow vertical stacking.</param>
        private static void DrawXmlDriverEntry(Rect inner, MassCapacityDriverDef def, string currentKey, Settings settings, ref float localY)
        {
            bool isActive = def.IsValidAndActive(out _);
            string expectedKey = IntegrationPipeline.DriverKeyXmlPrefix + def.defName;
            bool isSelected = currentKey == expectedKey;

            Rect rowRect = new Rect(inner.x + 8f, localY, inner.width - 8f, 24f);

            if (isSelected)
            {
                Widgets.DrawBoxSolid(rowRect.ExpandedBy(2f, 1f), SettingsViewUtilities.DarkTargetHighlightColor);
            }

            float badgeWidth = 130f;
            Rect radioRect = new Rect(rowRect.x, rowRect.y, rowRect.width - badgeWidth - 4f, rowRect.height);
            Rect badgeRect = new Rect(rowRect.x + rowRect.width - badgeWidth, rowRect.y + 2f, badgeWidth, rowRect.height);

            string driverName = def.label ?? def.defName;
            string driverLabel = "OverHaulers_DriverLabel_StatFormat".Translate(driverName, def.statDefName).ToString();

            if (isActive)
            {
                if (DrawCustomRadioRow(radioRect, driverLabel, isSelected))
                {
                    settings.selectedDriverKey = expectedKey;
                    IntegrationPipeline.RebindDriver();
                }

                Text.Font = GameFont.Tiny;
                GUI.color = isSelected ? settings.colorBoosted : settings.colorHealthy;
                Widgets.Label(badgeRect, "OverHaulers_ModActive".Translate().ToString());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            else
            {
                GUI.enabled = false;
                DrawCustomRadioRow(radioRect, driverLabel, false);

                Text.Font = GameFont.Tiny;
                GUI.color = SettingsViewUtilities.DescriptionTextColor;
                Widgets.Label(badgeRect, "OverHaulers_ModNotLoaded".Translate().ToString());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                GUI.enabled = true;
            }

            localY += 26f;
        }

        /// <summary>
        /// Draws the UI elements for a discovered CIL patch within the settings view.
        /// </summary>
        /// <param name="inner">The inner rectangle defining the drawing area for the patch UI elements.</param>
        /// <param name="patch">The discovered CIL patch information to be displayed.</param>
        /// <param name="currentKey">The currently selected driver key for comparison.</param>
        /// <param name="settings">The current settings object containing user preferences and selections.</param>
        /// <param name="localY">The local Y-coordinate for positioning the UI elements, passed by reference to allow vertical stacking.</param>
        private static void DrawCilDiscoveredPatch(Rect inner, DiscoveredPatchInfo patch, string currentKey, Settings settings, ref float localY)
        {
            Rect patchHeaderRect = new Rect(inner.x + 8f, localY, inner.width - 8f, 22f);
            Text.Font = GameFont.Tiny;
            GUI.color = SettingsViewUtilities.DescriptionTextColor;
            Widgets.Label(patchHeaderRect, "OverHaulers_PatchHeader_OwnerFormat".Translate(patch.Owner, patch.PrimaryType).ToString());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            localY += 22f;

            if (patch.CandidateStats != null && patch.CandidateStats.Count > 0)
            {
                for (int s = 0; s < patch.CandidateStats.Count; s++)
                {
                    StatDef stat = patch.CandidateStats[s];
                    string targetKey = $"{IntegrationPipeline.DriverKeyCilPrefix}{patch.Owner}:{stat.defName}";
                    bool isSelected = currentKey == targetKey;

                    string suggested = (s == 0) ? (" - " + "OverHaulers_SuggestedStat".Translate().ToString()).Colorize(settings.colorBoosted) : "";
                    string statLabel = $"{stat.defName} ({stat.LabelCap}){suggested}";

                    Rect statRadioRect = new Rect(inner.x + 24f, localY, inner.width - 24f, 24f);
                    if (DrawCustomRadioRow(statRadioRect, statLabel, isSelected))
                    {
                        settings.selectedDriverKey = targetKey;
                        IntegrationPipeline.RebindDriver();
                    }
                    localY += 24f;
                }
            }
            else
            {
                // Clean empty-state note for mods that patch without creating caravan stats (e.g. Genetics Rim)
                Rect emptyRect = new Rect(inner.x + 24f, localY, inner.width - 24f, 18f);
                Text.Font = GameFont.Tiny;
                GUI.color = SettingsViewUtilities.DescriptionTextColor;
                Widgets.Label(emptyRect, "OverHaulers_NoCandidateStatsInPatch".Translate().ToString());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                localY += 20f;
            }

            localY += 6f; // Inter-patch separation gap
        }

        /// <summary>
        /// Resolves the appropriate badge label for a given pipeline driver based on its type and context.
        /// </summary>
        /// <param name="driver">The pipeline driver for which to resolve the badge label.</param>
        /// <returns>The localized badge label string corresponding to the driver's type and context.</returns>
        private static string ResolveDriverTypeBadge(IPipelineDriver driver)
        {
            if (driver is VanillaStatDriver) return "OverHaulers_Badge_StandalonePostfix".Translate().ToString();
            if (driver is GenericStatDriver g)
            {
                // Dynamic resolution: check if active driver matches any loaded XML definition
                List<MassCapacityDriverDef> xmlDrivers = DefDatabase<MassCapacityDriverDef>.AllDefsListForReading;
                if (xmlDrivers != null)
                {
                    for (int i = 0; i < xmlDrivers.Count; i++)
                    {
                        string id = xmlDrivers[i].label ?? xmlDrivers[i].defName;
                        if (string.Equals(g.DriverIdentifier, id, StringComparison.OrdinalIgnoreCase))
                        {
                            return "OverHaulers_Badge_DeclarativeXml".Translate().ToString();
                        }
                    }
                }

                string key = OverHaulers.settings?.selectedDriverKey ?? "";
                if (key.StartsWith(IntegrationPipeline.DriverKeyCilPrefix)) return "OverHaulers_Badge_DynamicBytecode".Translate().ToString();
                return "OverHaulers_Badge_StatPartDriver".Translate().ToString();
            }
            return "OverHaulers_Badge_CSharpDriver".Translate().ToString();
        }

        /// <summary>
        /// Renders an accessible left-aligned radio button with label and full font descender clearance.
        /// </summary>
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
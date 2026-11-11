using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Provides the rendering logic for the interactive Test Bench section of the OverHaulers settings view, including controls for selecting
    /// test subjects, configuring test parameters, and previewing results.
    /// </summary>
    /// <remarks>
    /// Provides the interactive Test Bench settings view for the OverHaulers mod, allowing developers to experiment with anatomical configurations
    ///  and preview results.
    /// </remarks>
    public partial class ToolingDialog
    {
        #region 1. CONSTANTS & ACCORDION STATES

        private static string sandboxSearchText = string.Empty;

        // Expanded group labels
        private static readonly HashSet<string> expandedGroupLabels = new HashSet<string>();
        private static readonly Func<Settings, DumpScope, DumpFormat, TestSubjectEntry, string> groupingExport = GroupingDumpExporter.ExportGroupingDump;
        private static readonly Func<Settings, DumpScope, DumpFormat, TestSubjectEntry, string> topologyExport = TopologyDumpExporter.ExportTopologyDump;

        /// <summary>
        /// Clears the accordion expansion states and search filter in the medical planner.
        /// Invoked when changing test subjects or resetting the Test Bench to baseline.
        /// </summary>
        public static void ResetPlannerViewState()
        {
            expandedGroupLabels.Clear();
            sandboxSearchText = string.Empty;
        }

        #endregion

        #region 2. [SEC-11] INTERACTIVE ANATOMICAL TEST BENCH ORCHESTRATOR

        /// <summary>
        /// [SEC-11] Draws the interactive Test Bench section of the OverHaulers settings view, which includes controls for selecting test subjects,
        /// configuring test parameters, and previewing results.
        /// </summary>
        /// <param name="viewRect">The rectangle that contains the entire Test Bench section.</param>
        /// <param name="currentY">The current Y position within the Test Bench section.</param>
        /// <param name="settings">The OverHaulers settings instance.</param>
        /// <param name="resetAction">The action to invoke when the reset button is clicked.</param>
        public static void DrawTestBenchSection(Rect viewRect, ref float currentY, Settings settings, Action resetAction)
        {
            TestBench.EnsureInitialized();

            float localY = currentY;
            float totalWidth = viewRect.width;
            float gap = SettingsViewUtilities.ColumnGap;
            float leftWidth = (totalWidth * 0.44f) - (gap / 2f);
            float rightWidth = totalWidth - leftWidth - gap;

            bool isResetHovered = DrawTestBenchHeaderDirect(viewRect, "OverHaulers_TestBench_Header", settings, resetAction, localY, out localY, leftWidth, rightWidth, gap);

            float columnStartY = localY;

            // LEFT COLUMN: Subject Picker, Topology Badge & Universal Sandbox Medical Planner (Flattened)
            float leftY = DrawLeftControlColumn(viewRect, columnStartY, leftWidth, isResetHovered, settings);

            // RIGHT COLUMN: Top Action Toolbar (View Modes / Dumps) & Live Delta Preview
            float rightY = DrawRightPreviewColumn(viewRect, columnStartY, leftWidth + gap, rightWidth, leftY - columnStartY, settings);

            currentY = Math.Max(leftY, rightY) + 10f;
        }

        #endregion

        #region 3. TOP ACTION TOOLBAR & EXPORT MENUS

        /// <summary>
        /// Draws the Test Bench title, verbose toggle, and reset control aligned with the columns.
        /// </summary>
        private static bool DrawTestBenchHeaderDirect(
            Rect containerRect,
            string headerLabelKey,
            Settings settings,
            Action resetAction,
            float currentY,
            out float nextY,
            float leftWidth,
            float rightWidth,
            float columnGap)
        {
            float rightX = containerRect.x + leftWidth + columnGap;
            float buttonGap = SettingsViewUtilities.ControlGap;
            string title = SettingsViewUtilities.CachedText(headerLabelKey);
            string resetLabel = SettingsViewUtilities.CachedText("OverHaulers_Reset");
            string verboseTooltip = SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_VerboseBreakdown_Tooltip");
            string verboseLabel = SettingsViewUtilities.LabelWithTooltip(SettingsViewUtilities.CachedText("OverHaulers_ToolingDialog_VerboseBreakdown"), verboseTooltip);

            Text.Font = GameFont.Medium;
            float titleHeight = Math.Max(30f, SettingsViewUtilities.MeasuredTextHeight(title, leftWidth));
            Widgets.Label(new Rect(containerRect.x, currentY, leftWidth, titleHeight), title);
            Text.Font = GameFont.Small;

            Vector2 resetSize = SettingsViewUtilities.ButtonSize(resetLabel, rightWidth,
                minWidth: (rightWidth - buttonGap * 2f) / 3f, minHeight: 30f);
            float buttonWidth = resetSize.x;
            float verboseWidth = rightWidth - buttonWidth - buttonGap;
            bool separateControls = verboseWidth < SettingsViewUtilities.MeasuredTextWidth(verboseLabel) + 30f;
            if (separateControls) verboseWidth = rightWidth;
            float verboseHeight = Math.Max(30f, SettingsViewUtilities.MeasuredTextHeight(verboseLabel, Math.Max(1f, verboseWidth - 30f)));
            float resetHeight = resetSize.y;
            Rect verboseRect = new Rect(rightX, currentY, verboseWidth, verboseHeight);
            Rect resetRect = new Rect(rightX + rightWidth - buttonWidth, separateControls ? verboseRect.yMax + buttonGap : currentY, buttonWidth, resetHeight);

            bool prevVerbose = settings.verboseBreakdown;
            Widgets.CheckboxLabeled(verboseRect, verboseLabel, ref settings.verboseBreakdown);
            if (!string.IsNullOrEmpty(verboseTooltip)) TooltipHandler.TipRegion(verboseRect, verboseTooltip);
            if (prevVerbose != settings.verboseBreakdown)
            {
                DetailedModelRegistry.Clear();
                TestBench.MarkDirty();
                SettingsViewUtilities.OnSettingMutated();
            }

            // [Reset] Button - Resets Test Bench parameters to baseline
            bool isResetHovered = Mouse.IsOver(resetRect);
            if (SettingsViewUtilities.DrawButton(resetRect, resetLabel))
            {
                resetAction?.Invoke();
                ResetPlannerViewState();
                SettingsViewUtilities.OnSettingMutated();
            }

            nextY = Math.Max(currentY + titleHeight, Math.Max(verboseRect.yMax, resetRect.yMax)) + SettingsViewUtilities.ControlGap;
            return isResetHovered;
        }

        /// <summary>
        /// Draws the top action toolbar of the right preview column, which includes radio buttons for selecting the view mode
        /// (InfoCard or TopologyXRay) and corresponding dump menus.
        /// </summary>
        private static float DrawTopActionBar(
            Rect inner, 
            float localY, 
            float rightX, 
            float rightWidth, 
            Settings settings)
        {
            string infoLabel = SettingsViewUtilities.CachedText("OverHaulers_ViewMode_InfoCard");
            string xRayLabel = SettingsViewUtilities.CachedText("OverHaulers_ViewMode_TopologyXRay");
            string exportLabel = SettingsViewUtilities.CachedText("OverHaulers_TestBench_ExportButton");
            float exportWidth = SettingsViewUtilities.ButtonSize(exportLabel, rightWidth, minWidth: 80f, minHeight: 24f).x;
            float halfRightWidth = (rightWidth - SettingsViewUtilities.ControlGap) / 2f;
            float requiredRadioWidth = Math.Max(SettingsViewUtilities.MeasuredTextWidth(infoLabel), SettingsViewUtilities.MeasuredTextWidth(xRayLabel)) + 30f;
            bool separateRows = requiredRadioWidth + SettingsViewUtilities.CompactGap + exportWidth > halfRightWidth;
            TestSubjectEntry activeSubject = TestBench.ActiveSubject;
            string subjectLabel = activeSubject?.Label ?? "Subject";
            string bodyDefName = activeSubject?.BodyDef?.defName ?? "Body";
            float blockWidth = separateRows ? rightWidth : halfRightWidth;
            float blockX = inner.x + rightX;

            float infoHeight = DrawExportViewControl(blockX, localY, blockWidth, infoLabel, exportLabel, exportWidth,
                TestBenchViewMode.InfoCard, "OverHaulers_TestBench_ExportGrouping_Tooltip",
                settings, subjectLabel, bodyDefName, activeSubject, groupingExport);
            float xRayY = separateRows ? localY + infoHeight + SettingsViewUtilities.ControlGap : localY;
            float xRayX = separateRows ? blockX : blockX + halfRightWidth + SettingsViewUtilities.ControlGap;
            float xRayHeight = DrawExportViewControl(xRayX, xRayY, blockWidth, xRayLabel, exportLabel, exportWidth,
                TestBenchViewMode.TopologyXRay, "OverHaulers_TestBench_ExportTopology_Tooltip",
                settings, subjectLabel, bodyDefName, activeSubject, topologyExport);

            return (separateRows ? xRayY + xRayHeight : localY + Math.Max(infoHeight, xRayHeight)) + 10f;
        }

        private static float DrawExportViewControl(
            float x, float y, float width, string viewLabel, string exportLabel, float desiredExportWidth,
            TestBenchViewMode mode, string tooltipKey, Settings settings, string subjectLabel, string bodyDefName,
            TestSubjectEntry activeSubject, Func<Settings, DumpScope, DumpFormat, TestSubjectEntry, string> exportAction)
        {
            float exportWidth = Math.Min(width, desiredExportWidth);
            float radioWidth = width - exportWidth - SettingsViewUtilities.CompactGap;
            bool separateControls = radioWidth < SettingsViewUtilities.MeasuredTextWidth(viewLabel) + 30f;
            if (separateControls) radioWidth = width;
            float radioHeight = Math.Max(24f, SettingsViewUtilities.MeasuredTextHeight(viewLabel, Math.Max(1f, radioWidth - 30f)));
            float exportHeight = SettingsViewUtilities.ButtonSize(exportLabel, exportWidth, minHeight: 24f, fixedWidth: exportWidth).y;
            Rect radioRect = new Rect(x, y, radioWidth, radioHeight);
            Rect exportRect = new Rect(x + width - exportWidth, separateControls ? radioRect.yMax + SettingsViewUtilities.CompactGap : y, exportWidth, exportHeight);

            if (Widgets.RadioButtonLabeled(radioRect, viewLabel, TestBench.activeViewMode == mode))
            {
                TestBench.activeViewMode = mode;
                TestBench.MarkDirty();
            }
            if (SettingsViewUtilities.DrawButton(exportRect, exportLabel, SettingsViewUtilities.CachedText(tooltipKey)))
            {
                OpenDumpMenu(settings, subjectLabel, bodyDefName, activeSubject, exportAction);
            }
            if (SettingsViewUtilities.MeasuredTextWidth(viewLabel) + 30f > radioWidth) TooltipHandler.TipRegion(radioRect, viewLabel);
            return Math.Max(radioRect.yMax, exportRect.yMax) - y;
        }

        /// <summary>
        /// Opens the dump menu for exporting test bench data in various formats.
        /// </summary>
        /// <param name="settings">The settings object containing the test bench configuration.</param>
        /// <param name="subjectLabel">The label of the subject being tested.</param>
        /// <param name="bodyDefName">The body definition name of the subject.</param>
        /// <param name="activeSubject">The currently active test subject entry.</param>
        /// <param name="exportAction">The action to invoke for exporting the dump data.</param>
        private static void OpenDumpMenu(
            Settings settings, 
            string subjectLabel, 
            string bodyDefName, 
            TestSubjectEntry activeSubject, 
            Func<Settings, DumpScope, DumpFormat, TestSubjectEntry, string> exportAction)
        {
            List<FloatMenuOption> dumpOptions = new List<FloatMenuOption>
            {
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_Selected".Translate(subjectLabel, "TXT").ToString(), () =>
                {
                    string path = exportAction(settings, DumpScope.SelectedSubject, DumpFormat.Text, activeSubject);
                    NotifyDumpResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_Selected".Translate(subjectLabel, "XML").ToString(), () =>
                {
                    string path = exportAction(settings, DumpScope.SelectedSubject, DumpFormat.Xml, activeSubject);
                    NotifyDumpResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_Archetype".Translate(bodyDefName, "TXT").ToString(), () =>
                {
                    string path = exportAction(settings, DumpScope.BodyDefArchetype, DumpFormat.Text, activeSubject);
                    NotifyDumpResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_Archetype".Translate(bodyDefName, "XML").ToString(), () =>
                {
                    string path = exportAction(settings, DumpScope.BodyDefArchetype, DumpFormat.Xml, activeSubject);
                    NotifyDumpResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_FullCensus".Translate("TXT").ToString(), () =>
                {
                    string path = exportAction(settings, DumpScope.FullCensus, DumpFormat.Text, activeSubject);
                    NotifyDumpResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_FullCensus".Translate("XML").ToString(), () =>
                {
                    string path = exportAction(settings, DumpScope.FullCensus, DumpFormat.Xml, activeSubject);
                    NotifyDumpResult(path);
                })
            };
            Find.WindowStack.Add(new FloatMenu(dumpOptions));
        }

        /// <summary>
        /// Notifies the user of the result of a dump export operation.
        /// </summary>
        /// <param name="path">The file path of the exported dump result.</param>
        private static void NotifyDumpResult(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                Messages.Message("OverHaulers_TestBench_ExportedMessage".Translate(path).ToString(), MessageTypeDefOf.PositiveEvent, false);
            }
        }

        #endregion

        #region 4. LEFT CONTROL COLUMN (SUBJECT PICKER & TOPOLOGY READOUT)

        /// <summary>
        /// Draws the left control column of the Test Bench section, which includes the reference subject picker button, topology readout badge,
        /// and universal sandbox medical planner.
        /// </summary>
        /// <param name="inner">The rectangle defining the area in which to draw the left control column.</param>
        /// <param name="columnStartY">The starting vertical position within the column.</param>
        /// <param name="leftWidth">The width of the left control column.</param>
        /// <param name="isResetHovered">Indicates whether the reset button is currently hovered.</param>
        /// <param name="settings">The settings object containing the test bench configuration.</param>
        /// <returns>The updated vertical position after drawing the left control column.</returns>
        private static float DrawLeftControlColumn(Rect inner, float columnStartY, float leftWidth, bool isResetHovered, Settings settings)
        {
            float leftY = columnStartY;
            TestSubjectEntry subject = TestBench.ActiveSubject;

            // 1. Reference Subject Picker Button
            string currentSubjectLabel = subject != null ? subject.Label : "OverHaulers_None".Translate().ToString();
            string subjectButtonLabel = "OverHaulers_TestBench_Subject".Translate(currentSubjectLabel).ToString();
            float subjectButtonHeight = SettingsViewUtilities.ButtonSize(subjectButtonLabel, leftWidth, minHeight: 26f, fixedWidth: leftWidth).y;
            Rect subjectButtonRect = new Rect(inner.x, leftY, leftWidth, subjectButtonHeight);
            if (SettingsViewUtilities.DrawButton(subjectButtonRect, subjectButtonLabel))
            {
                Find.WindowStack.Add(new Dialog_TestSubjectPicker(
                    TestBench.activeGroupingDimension,
                    TestBench.ActiveSubject,
                    entry => 
                    { 
                        TestBench.ActiveSubject = entry;
                        ResetPlannerViewState();
                    }
                ));
            }
            leftY += subjectButtonHeight + 2f;

            // 2. Topology Readout Badge
            SpeciesTopologyTemplate speciesTemplate = null;
            if (subject?.BodyDef != null)
            {
                speciesTemplate = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(subject.BodyDef);
                int partCount = speciesTemplate != null ? speciesTemplate.PartCount : subject.BodyDef.AllParts.Count;
                string modName = subject.ModName ?? "Core";

                Text.Font = GameFont.Tiny;
                GUI.color = SettingsViewUtilities.DescriptionTextColor;
                Widgets.Label(new Rect(inner.x + 4f, leftY, leftWidth - 4f, 16f), "OverHaulers_TestBench_ActiveTopology".Translate(subject.BodyDef.defName, partCount, modName).ToString());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                leftY += 20f;
            }

            // 3. Universal Sandbox Medical Planner (Flattened)
            leftY = DrawUniversalSandboxPlannerView(inner, leftY, leftWidth, settings);

            return leftY;
        }

        #endregion

        #region 5. UNIVERSAL SANDBOX ACCORDION PLANNER VIEW (FLATTENED)

        #region 5A. Planner Main Controls & Simulated Drug Badges

        /// <summary>
        /// Draws the universal sandbox planner view, including search bar, systemic stimulants, and staged conditions controls.
        /// </summary>
        /// <param name="inner">The rectangle defining the area in which to draw the planner view.</param>
        /// <param name="leftY">The current vertical position within the left control column, updated as elements are drawn.</param>
        /// <param name="leftWidth">The width of the left control column.</param>
        /// <param name="settings">The settings object containing the test bench configuration.</param>
        /// <returns>The updated vertical position after drawing the planner view.</returns>
        private static float DrawUniversalSandboxPlannerView(
            Rect inner, 
            float leftY, 
            float leftWidth, 
            Settings settings)
        {
            SandboxPawnHarness harness = TestBench.Harness;
            MassCapacityModel model = TestBench.GetCachedModel(settings, out _);

            // 1. Search Bar & Clear Button
            Rect searchBarRect = new Rect(inner.x, leftY, leftWidth, 24f);
            sandboxSearchText = SettingsViewUtilities.DrawSearchField(searchBarRect, sandboxSearchText,
                "OverHaulers_Sandbox_SearchPlaceholder", GameFont.Tiny);
            leftY += 28f;

            // 2. Systemic Stimulants & Staged Conditions Dropdown
            float halfWidth = (leftWidth - 6f) / 2f;
            string drugLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_AddDrug");
            string expandLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_ExpandAll");
            string collapseLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_CollapseAll");
            Rect rowBounds = new Rect(inner.x, leftY, leftWidth, 0f);
            float rowX = inner.x;
            float rowY = leftY;
            float rowHeight = 0f;
            Rect drugButtonRect = SettingsViewUtilities.PlaceButton(rowBounds, ref rowX, ref rowY, ref rowHeight,
                drugLabel, 4f, minWidth: halfWidth, minHeight: 22f);
            Text.Font = GameFont.Tiny;
            Rect expandButtonRect = SettingsViewUtilities.PlaceButton(rowBounds, ref rowX, ref rowY, ref rowHeight,
                expandLabel, 4f, minWidth: (halfWidth - 4f) / 2f, minHeight: 22f);
            Rect collapseButtonRect = SettingsViewUtilities.PlaceButton(rowBounds, ref rowX, ref rowY, ref rowHeight,
                collapseLabel, 4f, minWidth: (halfWidth - 4f) / 2f, minHeight: 22f);
            Text.Font = GameFont.Small;

            if (SettingsViewUtilities.DrawButton(drugButtonRect, drugLabel))
            {
                List<FloatMenuOption> drugOptions = new List<FloatMenuOption>();
                List<HediffDef> availableDrugs = MedicalRecipeCatalog.GetSystemicDrugs();

                for (int i = 0; i < availableDrugs.Count; i++)
                {
                    HediffDef drugDef = availableDrugs[i];
                    bool isActive = harness.ActiveSimulatedDrugs.Contains(drugDef);
                    string stageLabel = MedicalRecipeCatalog.GetFormattedConditionLabel(drugDef);
                    string optionLabel = $"{(isActive ? "✓ " : "")}{stageLabel}";

                    drugOptions.Add(new FloatMenuOption(optionLabel, () =>
                    {
                        harness.SimulateToggleDrug(drugDef);
                        TestBench.MarkDirty();
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(drugOptions));
            }

            Text.Font = GameFont.Tiny;
            if (SettingsViewUtilities.DrawButton(expandButtonRect, expandLabel))
            {
                for (int g = 0; g < model.EvaluatedParts.Count; g++)
                {
                    expandedGroupLabels.Add(model.EvaluatedParts[g].Label);
                }
            }
            if (SettingsViewUtilities.DrawButton(collapseButtonRect, collapseLabel))
            {
                expandedGroupLabels.Clear();
            }
            Text.Font = GameFont.Small;
            leftY = rowY + rowHeight + 4f;

            // 3. Active Simulated Condition Badges
            if (harness.ActiveSimulatedDrugs.Count > 0)
            {
                Text.Font = GameFont.Tiny;
                foreach (var activeDrug in harness.ActiveSimulatedDrugs)
                {
                    Rect tagRect = new Rect(inner.x + 4f, leftY, leftWidth - 8f, 18f);
                    Widgets.DrawBoxSolid(tagRect, new Color(0.15f, 0.20f, 0.25f, 0.8f));
                    Widgets.Label(new Rect(tagRect.x + 4f, tagRect.y + 1f, tagRect.width - 24f, tagRect.height), MedicalRecipeCatalog.GetFormattedConditionLabel(activeDrug));

                    if (Widgets.ButtonText(new Rect(tagRect.x + tagRect.width - 18f, tagRect.y, 18f, 18f), "✕"))
                    {
                        harness.SimulateToggleDrug(activeDrug);
                        TestBench.MarkDirty();
                        break;
                    }
                    leftY += 20f;
                }
                Text.Font = GameFont.Small;
                leftY += 4f;
            }

            // 4. Dynamic Revert Button
            if (harness.HasModifications)
            {
                bool isLiveSubject = TestBench.ActiveSubject?.IsLivePawn == true;
                Color critColor = OverHaulers.settings?.colorCritical ?? PresentationDefaults.ColorCriticalDefault;
                GUI.color = Color.Lerp(Color.white, critColor, 0.6f);
                Text.Font = GameFont.Tiny;

                string revertButtonLabel = isLiveSubject 
                    ? "OverHaulers_Sandbox_RevertLive".Translate().ToString() 
                    : "OverHaulers_Sandbox_RevertBaseline".Translate().ToString();

                Vector2 revertSize = SettingsViewUtilities.ButtonSize(revertButtonLabel, leftWidth, minHeight: 22f, fixedWidth: leftWidth);
                Rect clearPlanRect = new Rect(inner.x, leftY, revertSize.x, revertSize.y);
                if (SettingsViewUtilities.DrawButton(clearPlanRect, revertButtonLabel))
                {
                    harness.ResetToSource();
                    TestBench.MarkDirty();
                }
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                leftY += revertSize.y + 4f;
            }

            // 5. Flattened Accordions
            if (model.EvaluatedParts.Count > 0)
            {
                Rect groupViewRect = new Rect(inner.x, leftY, leftWidth, 0f);

                for (int g = 0; g < model.EvaluatedParts.Count; g++)
                {
                    PartViewNode groupNode = model.EvaluatedParts[g];
                    leftY = DrawPassiveGroupAccordion(groupViewRect, leftY, groupNode, harness);
                }
                leftY += 4f;
            }

            return leftY;
        }

        #endregion

        #region 5B. Passive Group Accordion Headers

        /// <summary>
        /// Draws a passive group accordion, including its header and any child elements, within the sandbox test bench view.
        /// </summary>
        /// <param name="viewRect">The rectangle defining the area in which to draw the accordion.</param>
        /// <param name="currentY">The current vertical position within the view, updated as elements are drawn.</param>
        /// <param name="groupNode">The passive group node representing the accordion's content.</param>
        /// <param name="harness">The sandbox pawn harness providing context for the accordion.</param>
        /// <returns>The updated vertical position after drawing the accordion.</returns>
        private static float DrawPassiveGroupAccordion(
            Rect viewRect,
            float currentY,
            PartViewNode groupNode,
            SandboxPawnHarness harness)
        {
            if (groupNode == null) return currentY;

            string searchFilter = sandboxSearchText.Trim().ToLowerInvariant();
            bool isSearching = !string.IsNullOrEmpty(searchFilter);

            if (isSearching && !GroupMatchesFilter(groupNode, searchFilter))
            {
                return currentY;
            }

            bool isExpanded = isSearching || expandedGroupLabels.Contains(groupNode.Label);

            Text.Font = GameFont.Tiny;
            string operationLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_OpBtn");
            Vector2 operationSize = SettingsViewUtilities.ButtonSize(operationLabel, viewRect.width,
                minWidth: 70f, minHeight: 20f, maxWidth: viewRect.width * 0.5f);
            Rect headerRect = new Rect(viewRect.x, currentY, viewRect.width, Math.Max(24f, operationSize.y + 4f));
            Widgets.DrawBoxSolid(headerRect, new Color(0.12f, 0.15f, 0.18f, 0.9f));
            Widgets.DrawHighlightIfMouseover(headerRect);

            string expandArrow = isExpanded ? "▼ " : "▶ ";
            string headerText = $"{expandArrow}{groupNode.Label}";

            GUI.color = new Color(1f, 0.8f, 0.3f);
            Widgets.Label(new Rect(headerRect.x + 6f, headerRect.y + 3f, headerRect.width - operationSize.x - 10f, 18f), headerText.ToUpperInvariant());
            GUI.color = Color.white;

            // Group Quick Actions [Options ▼]
            Rect bulkButtonRect = new Rect(headerRect.xMax - operationSize.x - 4f, headerRect.y + 2f, operationSize.x, operationSize.y);
            if (SettingsViewUtilities.DrawButton(bulkButtonRect, operationLabel))
            {
                OpenGroupBulkOperationsMenu(groupNode.SubParts, harness);
            }

            // Click header to toggle expand/collapse
            if (Widgets.ButtonInvisible(new Rect(headerRect.x, headerRect.y, headerRect.width - operationSize.x - 6f, headerRect.height)))
            {
                if (expandedGroupLabels.Contains(groupNode.Label)) expandedGroupLabels.Remove(groupNode.Label);
                else expandedGroupLabels.Add(groupNode.Label);
            }

            Text.Font = GameFont.Small;
            currentY += headerRect.height + 2f;

            // Expanded Pre-Calculated Part Rows
            if (isExpanded && groupNode.SubParts != null)
            {
                for (int i = 0; i < groupNode.SubParts.Count; i++)
                {
                    currentY = DrawPassivePartNodeRecursive(viewRect, currentY, groupNode.SubParts[i], 0, harness, searchFilter);
                }
                currentY += 4f;
            }

            return currentY;
        }

        #endregion

        #region 5C. Recursive Part Node Rendering & Badge Formatter

        /// <summary>
        /// Recursively draws a passive part node and its child nodes within the sandbox test bench view.
        /// </summary>
        /// <param name="viewRect">The rectangle defining the area in which to draw the part node.</param>
        /// <param name="currentY">The current vertical position within the view, updated as elements are drawn.</param>
        /// <param name="node">The passive part node to be drawn.</param>
        /// <param name="depth">The depth level of the node within the hierarchy, used for indentation.</param>
        /// <param name="harness">The sandbox pawn harness providing context for the part node.</param>
        /// <param name="searchFilter">The search filter text used to determine if the node should be displayed.</param>
        /// <returns>The updated vertical position after drawing the part node and its children.</returns>
        private static float DrawPassivePartNodeRecursive(
            Rect viewRect,
            float currentY,
            PartViewNode node,
            int depth,
            SandboxPawnHarness harness,
            string searchFilter)
        {
            if (node == null) return currentY;

            bool matchesFilter = string.IsNullOrEmpty(searchFilter) || 
                                 node.Label.ToLowerInvariant().Contains(searchFilter) || 
                                 (node.Record?.def?.defName != null && node.Record.def.defName.ToLowerInvariant().Contains(searchFilter));

            if (matchesFilter)
            {
                Text.Font = GameFont.Tiny;
                string operationLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_OpBtn");
                Vector2 operationSize = SettingsViewUtilities.ButtonSize(operationLabel, viewRect.width,
                    minWidth: 70f, minHeight: 20f, maxWidth: viewRect.width * 0.5f);
                Rect rowRect = new Rect(viewRect.x, currentY, viewRect.width, Math.Max(22f, operationSize.y + 2f));
                Widgets.DrawHighlightIfMouseover(rowRect);
                float indentOffset = Math.Min(depth * 8f, 24f);
                float labelWidth = viewRect.width - operationSize.x - 6f - indentOffset;

                Rect labelRect = new Rect(rowRect.x + 4f + indentOffset, rowRect.y + 2f, labelWidth, 18f);
                Rect buttonRect = new Rect(rowRect.xMax - operationSize.x - 2f, rowRect.y, operationSize.x, operationSize.y);

                bool isVirtualAnchor = node.Record != null && node.Record.coverageAbs <= 0f && node.Record != node.Record.body?.corePart;
                bool hasOperations = node.Record != null && MedicalRecipeCatalog.HasAnyOperationsFor(node.Record.def);

                string fullPartLabel = BuildPassiveNodeDisplayString(node, harness, isVirtualAnchor, out Color statusColor);

                Text.Font = GameFont.Tiny;
                GUI.color = statusColor;
                Widgets.Label(labelRect, fullPartLabel);
                GUI.color = Color.white;

                // Interactive Button for operable parts; Disabled Muted Button for Virtual Anchors
                if (isVirtualAnchor && !hasOperations)
                {
                    GUI.enabled = false;
                    Text.Font = GameFont.Tiny;
                    Widgets.ButtonText(buttonRect, "—");
                    Text.Font = GameFont.Small;
                    GUI.enabled = true;
                    TooltipHandler.TipRegion(buttonRect, "OverHaulers_VirtualAnchor_Tooltip".Translate().ToString());
                }
                else if (node.Record != null && SettingsViewUtilities.DrawButton(buttonRect, operationLabel))
                {
                    OpenPartSimulationMenu(node.Record, harness);
                }
                Text.Font = GameFont.Small;

                currentY += rowRect.height + 2f;
            }

            if (node.SubParts != null && node.SubParts.Count > 0)
            {
                for (int c = 0; c < node.SubParts.Count; c++)
                {
                    currentY = DrawPassivePartNodeRecursive(viewRect, currentY, node.SubParts[c], depth + 1, harness, searchFilter);
                }
            }

            return currentY;
        }

        /// <summary>
        /// Builds a display string for a passive part node, dynamically applying the user's active accessibility palette LERP curves.
        /// </summary>
        /// <param name="node">The passive part node for which to build the display string.</param>
        /// <param name="harness">The sandbox pawn harness providing context for the part node.</param>
        /// <param name="isVirtualAnchor">Indicates whether the node represents a virtual anchor.</param>
        /// <param name="labelColor">The color to be applied to the label text, determined based on the node's status.</param>
        /// <returns>A formatted display string for the passive part node.</returns>
        private static string BuildPassiveNodeDisplayString(
            PartViewNode node, 
            SandboxPawnHarness harness, 
            bool isVirtualAnchor,
            out Color labelColor)
        {
            var settings = OverHaulers.settings;
            Color critColor = settings?.colorCritical ?? PresentationDefaults.ColorCriticalDefault;
            Color boostColor = settings?.colorBoosted ?? PresentationDefaults.ColorBoostedDefault;

            // 1. Virtual Anchor Grey-Box Badge
            if (isVirtualAnchor)
            {
                labelColor = SettingsViewUtilities.DescriptionTextColor;
                return "OverHaulers_NodeBadge_VirtualAnchor".Translate(node.Label).Colorize(labelColor);
            }

            // 1b. Inoperable Sub-Socket (Absorbed into an artificial parent limb)
            if (node.Record != null && harness.SandboxPawn?.health?.hediffSet != null &&
                harness.SandboxPawn.health.hediffSet.AncestorHasDirectlyAddedParts(node.Record))
            {
                labelColor = SettingsViewUtilities.DescriptionTextColor;
                return "OverHaulers_NodeBadge_Inoperable".Translate(node.Label).Colorize(labelColor);
            }

            // 2. Simulated Replacement, Amputation or Trauma (User-Applied Sandbox Deltas)
            if (node.Record != null && harness.ReplacementDeltas.TryGetValue(node.Record, out SimulatedModification repl))
            {
                if (repl.OpType == SimulatedOpType.Amputate)
                {
                    labelColor = critColor;
                    return "OverHaulers_NodeBadge_Missing".Translate(node.Label).Colorize(labelColor);
                }
                if (repl.OpType == SimulatedOpType.Trauma)
                {
                    labelColor = ReportFormatter.GetMedicalColor(repl.Efficiency);
                    return "OverHaulers_NodeBadge_Wounded".Translate(node.Label, ReportFormatter.FormatPercentage(repl.Efficiency, true)).Colorize(labelColor);
                }
                
                labelColor = (repl.Efficiency > 1.0f) 
                    ? boostColor 
                    : ReportFormatter.GetMedicalColor(repl.Efficiency);

                return "OverHaulers_NodeBadge_Prosthetic".Translate(node.Label, repl.DisplayLabel, ReportFormatter.FormatPercentage(repl.Efficiency, true)).Colorize(labelColor);
            }

            // 3. Simulated Implant (User-Applied Sandbox Delta)
            if (node.Record != null && harness.ImplantDeltas.TryGetValue(node.Record, out List<SimulatedModification> imps) && imps.Count > 0)
            {
                labelColor = boostColor;
                return "OverHaulers_NodeBadge_Implant".Translate(node.Label, imps[0].DisplayLabel).Colorize(labelColor);
            }

            // 4. Live Colonist Authentic Prosthetic
            if (node.HasProsthetic)
            {
                labelColor = (node.ProstheticColor != Color.white) 
                    ? node.ProstheticColor 
                    : ReportFormatter.GetMedicalColor(node.EfficiencyRating);

                return "OverHaulers_NodeBadge_Prosthetic".Translate(node.Label, node.ProstheticName, ReportFormatter.FormatPercentage(node.EfficiencyRating, true)).Colorize(labelColor);
            }

            // 5. Live Colonist Authentic Implant
            if (!string.IsNullOrEmpty(node.AthleticImplantName))
            {
                labelColor = (node.AthleticImplantColor != Color.white) 
                    ? node.AthleticImplantColor 
                    : Color.white;

                return "OverHaulers_NodeBadge_Implant".Translate(node.Label, node.AthleticImplantName).Colorize(labelColor);
            }

            // 6. Live Colonist Authentic Missing Bone
            if (node.IsMissing)
            {
                labelColor = critColor;
                return "OverHaulers_NodeBadge_Missing".Translate(node.Label).Colorize(labelColor);
            }

            // 7. Live Colonist Authentic Wound
            if (node.HealthFraction < 1.0f - PawnDataRegistry.EfficiencyEpsilon)
            {
                labelColor = ReportFormatter.GetMedicalColor(node.HealthFraction);
                return "OverHaulers_NodeBadge_Wounded".Translate(node.Label, ReportFormatter.FormatPercentage(node.HealthFraction, true)).Colorize(labelColor);
            }

            // 8. Natural Unmodified Tissue
            labelColor = SettingsViewUtilities.DescriptionTextColor;
            return "OverHaulers_NodeBadge_Natural".Translate(node.Label).Colorize(labelColor);
        }

        /// <summary>
        /// Determines whether the specified group node or any of its sub-parts match the given search filter.
        /// </summary>
        /// <param name="groupNode">The group node to be evaluated against the search filter.</param>
        /// <param name="searchFilter">The search filter text used to determine if the node should be displayed.</param>
        /// <returns>True if the group node or any of its sub-parts match the search filter; otherwise, false.</returns>
        private static bool GroupMatchesFilter(PartViewNode groupNode, string searchFilter)
        {
            if (groupNode.Label.ToLowerInvariant().Contains(searchFilter)) return true;

            if (groupNode.SubParts != null)
            {
                for (int i = 0; i < groupNode.SubParts.Count; i++)
                {
                    if (NodeMatchesFilter(groupNode.SubParts[i], searchFilter)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether the specified node or any of its sub-parts match the given search filter.
        /// </summary>
        /// <param name="node">The node to be evaluated against the search filter.</param>
        /// <param name="searchFilter">The search filter text used to determine if the node should be displayed.</param>
        /// <returns>True if the node or any of its sub-parts match the search filter; otherwise, false.</returns>
        private static bool NodeMatchesFilter(PartViewNode node, string searchFilter)
        {
            if (node.Label.ToLowerInvariant().Contains(searchFilter)) return true;
            if (node.Record?.def?.defName != null && node.Record.def.defName.ToLowerInvariant().Contains(searchFilter)) return true;

            if (node.SubParts != null)
            {
                for (int i = 0; i < node.SubParts.Count; i++)
                {
                    if (NodeMatchesFilter(node.SubParts[i], searchFilter)) return true;
                }
            }
            return false;
        }

        #endregion

        #region 5D. Bulk Operations & Replacement Tiers

        /// <summary>
        /// Opens the bulk operations menu for the specified group of sub-parts within the sandbox pawn harness context.
        /// </summary>
        /// <param name="subParts">The list of sub-part nodes for which to open the bulk operations menu.</param>
        /// <param name="harness">The sandbox pawn harness providing context for the bulk operations.</param>
        /// <remarks>
        /// This method collects all body part records from the specified sub-parts and presents a bulk operations menu
        /// allowing the user to apply replacements to multiple parts simultaneously.
        /// </remarks>
        private static void OpenGroupBulkOperationsMenu(
            List<PartViewNode> subParts, 
            SandboxPawnHarness harness)
        {
            if (subParts == null || subParts.Count == 0) return;

            List<BodyPartRecord> targetRecords = new List<BodyPartRecord>();
            CollectSubPartRecordsRecursive(subParts, targetRecords);
            if (targetRecords.Count == 0) return;

            // Root-first order: ensures parent limb joints are evaluated before child digits
            targetRecords.Sort((a, b) => GetPartDepth(a).CompareTo(GetPartDepth(b)));

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            float minEfficiency = float.MaxValue;
            float maxEfficiency = float.MinValue;
            bool hasAnyReplacements = false;

            for (int i = 0; i < targetRecords.Count; i++)
            {
                var repls = MedicalRecipeCatalog.GetReplacementsFor(targetRecords[i].def);
                if (repls != null && repls.Count > 0)
                {
                    for (int j = 0; j < repls.Count; j++)
                    {
                        float eff = repls[j].Efficiency;
                        if (eff < minEfficiency) minEfficiency = eff;
                        if (eff > maxEfficiency) maxEfficiency = eff;
                        hasAnyReplacements = true;
                    }
                }
            }

            if (hasAnyReplacements)
            {
                int maxPct = (int)Math.Round(maxEfficiency * 100f, MidpointRounding.ToEven);
                int minPct = (int)Math.Round(minEfficiency * 100f, MidpointRounding.ToEven);

                // 1. Bulk Maximum Upgrade Option
                options.Add(new FloatMenuOption("OverHaulers_Sandbox_BulkMaxUpgrade".Translate(ReportFormatter.FormatPercentage(maxEfficiency, true)).ToString(), () =>
                {
                    ApplyBulkReplacementTier(targetRecords, harness, pickHighest: true);
                }));

                // 2. Bulk Budget / Basic Option
                if (minPct != maxPct)
                {
                    options.Add(new FloatMenuOption("OverHaulers_Sandbox_BulkBudgetUpgrade".Translate(ReportFormatter.FormatPercentage(minEfficiency, true)).ToString(), () =>
                    {
                        ApplyBulkReplacementTier(targetRecords, harness, pickHighest: false);
                    }));
                }
            }

            // 3. Bulk Destroy
            options.Add(new FloatMenuOption("OverHaulers_Sandbox_BulkDestroy".Translate().ToString(), () =>
            {
                for (int i = 0; i < targetRecords.Count; i++)
                {
                    BodyPartRecord part = targetRecords[i];
                    if (harness.SandboxPawn.health.hediffSet.PartIsMissing(part)) continue;

                    if (IsLimbPartRecord(part))
                    {
                        harness.SimulateAmputation(part);
                    }
                    else if (part.coverageAbs > 0f)
                    {
                        harness.SimulateSevereTrauma(part);
                    }
                }
                TestBench.MarkDirty();
            }));

            // 4. Bulk Revert
            options.Add(new FloatMenuOption("OverHaulers_Sandbox_BulkRevert".Translate().ToString(), () =>
            {
                for (int i = 0; i < targetRecords.Count; i++)
                {
                    harness.SimulateRevertPart(targetRecords[i]);
                }
                TestBench.MarkDirty();
            }));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Applies a bulk replacement tier to the specified body part records within the sandbox pawn harness context.
        /// </summary>
        /// <param name="targetRecords">The list of body part records to which the replacement tier should be applied.</param>
        /// <param name="harness">The sandbox pawn harness providing context for the replacement operations.</param>
        /// <param name="pickHighest">Indicates whether to pick the highest efficiency replacement (true) or the lowest efficiency replacement (false).</param>
        private static void ApplyBulkReplacementTier(List<BodyPartRecord> targetRecords, SandboxPawnHarness harness, bool pickHighest)
        {
            Pawn pawn = harness.SandboxPawn;
            if (pawn?.health?.hediffSet == null) return;

            for (int i = 0; i < targetRecords.Count; i++)
            {
                BodyPartRecord currentPart = targetRecords[i];
                var repls = MedicalRecipeCatalog.GetReplacementsFor(currentPart.def);
                if (repls == null || repls.Count == 0) continue;

                MedicalOperationEntry selectedOp = default;
                bool found = false;

                for (int j = 0; j < repls.Count; j++)
                {
                    var op = repls[j];
                    if (!op.Recipe.Worker.AvailableOnNow(pawn, currentPart)) continue;

                    if (!found)
                    {
                        selectedOp = op;
                        found = true;
                    }
                    else if (pickHighest ? (op.Efficiency > selectedOp.Efficiency) : (op.Efficiency < selectedOp.Efficiency))
                    {
                        selectedOp = op;
                    }
                }

                if (found)
                {
                    harness.SimulateReplacement(currentPart, selectedOp.Recipe, selectedOp.Hediff, selectedOp.Label, selectedOp.Efficiency);
                }
            }

            TestBench.MarkDirty();
        }

        /// <summary>
        /// Gets the depth of the specified body part within the body part hierarchy. The depth is defined as the number of parent parts above
        ///  the specified part.
        /// </summary>
        /// <param name="part">The body part record for which to determine the depth.</param>
        /// <returns>The depth of the specified body part within the hierarchy, with 0 indicating a top-level part.</returns>
        private static int GetPartDepth(BodyPartRecord part)
        {
            int depth = 0;
            for (BodyPartRecord curr = part?.parent; curr != null; curr = curr.parent) depth++;
            return depth;
        }

        /// <summary>
        /// Recursively collects all body part records from the specified list of part view nodes and adds them to the results list.
        /// </summary>
        /// <param name="nodes">The list of part view nodes to process.</param>
        /// <param name="results">The list to which collected body part records will be added.</param>
        private static void CollectSubPartRecordsRecursive(List<PartViewNode> nodes, List<BodyPartRecord> results)
        {
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Record != null) results.Add(nodes[i].Record);
                if (nodes[i].SubParts != null) CollectSubPartRecordsRecursive(nodes[i].SubParts, results);
            }
        }

        #endregion

        #region 5E. Single Part Simulation Menus & Limb Predicates

        /// <summary>
        /// Opens the simulation menu for the specified body part, allowing the user to perform various operations on it.
        /// </summary>
        /// <param name="targetPart">The body part for which to open the simulation menu.</param>
        /// <param name="harness">The sandbox pawn harness associated with the simulation.</param>
        private static void OpenPartSimulationMenu(
            BodyPartRecord targetPart, 
            SandboxPawnHarness harness)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            Pawn pawn = harness.SandboxPawn;
            if (pawn?.health?.hediffSet == null) return;

            bool ancestorHasAdded = pawn.health.hediffSet.AncestorHasDirectlyAddedParts(targetPart);
            bool ancestorMissing = targetPart.parent != null && pawn.health.hediffSet.PartIsMissing(targetPart.parent);
            bool partMissing = pawn.health.hediffSet.PartIsMissing(targetPart);

            if (ancestorHasAdded)
            {
                options.Add(new FloatMenuOption("⚠ " + "OverHaulers_Op_DisabledParentProsthetic".Translate().ToString(), null) { Disabled = true });
            }
            else if (ancestorMissing)
            {
                options.Add(new FloatMenuOption("⚠ " + "OverHaulers_Op_DisabledParentMissing".Translate().ToString(), null) { Disabled = true });
            }

            // 1. Replacements
            List<MedicalOperationEntry> availableReplacements = MedicalRecipeCatalog.GetReplacementsFor(targetPart.def);
            if (availableReplacements != null && availableReplacements.Count > 0)
            {
                for (int i = 0; i < availableReplacements.Count; i++)
                {
                    MedicalOperationEntry operationEntry = availableReplacements[i];
                    bool canInstall = !ancestorMissing && operationEntry.Recipe.Worker.AvailableOnNow(pawn, targetPart);

                    string label = "OverHaulers_Op_AddReplacement".Translate(operationEntry.Label,
                        ReportFormatter.FormatPercentage(operationEntry.Efficiency, true)).ToString();

                    FloatMenuOption opt = new FloatMenuOption(label, () => {
                        harness.SimulateReplacement(
                            targetPart, operationEntry.Recipe,
                            operationEntry.Hediff, operationEntry.Label,
                            operationEntry.Efficiency);
                        TestBench.MarkDirty();
                    });
                    opt.Disabled = !canInstall;
                    options.Add(opt);
                }
            }

            // 2. Implants
            List<MedicalOperationEntry> availableImplants = MedicalRecipeCatalog.GetImplantsFor(targetPart.def);
            if (availableImplants != null && availableImplants.Count > 0)
            {
                for (int i = 0; i < availableImplants.Count; i++)
                {
                    MedicalOperationEntry operationEntry = availableImplants[i];
                    bool canInstall = !ancestorMissing && !partMissing && !ancestorHasAdded && operationEntry.Recipe.Worker.AvailableOnNow(pawn, targetPart);

                    string label = "OverHaulers_Op_AddImplant".Translate(operationEntry.Label).ToString();
                    FloatMenuOption opt = new FloatMenuOption(label, () => {
                        harness.SimulateAddImplant(
                            targetPart, operationEntry.Recipe,
                            operationEntry.Hediff, operationEntry.Label);
                        TestBench.MarkDirty();
                    });
                    opt.Disabled = !canInstall;
                    options.Add(opt);
                }
            }

            // 3. Unified Destructive Action
            bool canDestroy = !ancestorMissing && !partMissing && !ancestorHasAdded;
            if (IsLimbPartRecord(targetPart))
            {
                FloatMenuOption opt = new FloatMenuOption(
                    "OverHaulers_Op_Destroy".Translate().ToString(),
                    () => {
                        harness.SimulateAmputation(targetPart);
                        TestBench.MarkDirty();
                    }
                );
                opt.Disabled = !canDestroy;
                options.Add(opt);
            }
            else if (targetPart.coverageAbs > 0f)
            {
                FloatMenuOption opt = new FloatMenuOption(
                    "OverHaulers_Op_Destroy".Translate().ToString(),
                    () => {
                        harness.SimulateSevereTrauma(targetPart);
                        TestBench.MarkDirty();
                    }
                );
                opt.Disabled = !canDestroy;
                options.Add(opt);
            }

            // 4. Revert to Authentic Live / Baseline State
            if (harness.ReplacementDeltas.ContainsKey(targetPart) || harness.ImplantDeltas.ContainsKey(targetPart))
            {
                options.Add(new FloatMenuOption(
                    "OverHaulers_Op_Revert".Translate().ToString(),
                    () => {
                        harness.SimulateRevertPart(targetPart);
                        TestBench.MarkDirty();
                    }
                ));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Determines whether the specified body part is considered a limb (arm or leg) part.
        /// </summary>
        /// <param name="part">The body part to check.</param>
        /// <returns>True if the body part is a limb part; otherwise, false.</returns>
        private static bool IsLimbPartRecord(BodyPartRecord part)
        {
            if (part?.def == null) return false;
            return TopologyLayoutCompiler.HasAnyTag(part.def, TopologyLayoutCompiler.armTags) ||
                   TopologyLayoutCompiler.HasAnyTag(part.def, TopologyLayoutCompiler.legTags) ||
                   TopologyLayoutCompiler.IsLimbGroupPart(part);
        }

        #endregion

        #endregion

        #region 6. RIGHT COLUMN PREVIEW & COMPARISON BANNER

        /// <summary>
        /// Draws the right-hand preview column, which includes the top action toolbar (View Modes & Dump options),
        /// a comparison delta banner, and an info card breakdown of the simulated changes.
        /// </summary>
        /// <param name="inner">The inner rectangle defining the drawing area.</param>
        /// <param name="columnStartY">The starting Y position of the column.</param>
        /// <param name="rightX">The X position of the right column.</param>
        /// <param name="rightWidth">The width of the right column.</param>
        /// <param name="leftColumnHeight">The height of the left column, used for alignment purposes.</param>
        /// <param name="settings">The current settings object.</param>
        /// <returns>The updated Y position after drawing the right preview column.</returns>
        private static float DrawRightPreviewColumn(
            Rect inner, 
            float columnStartY, 
            float rightX, 
            float rightWidth, 
            float leftColumnHeight, 
            Settings settings)
        {
            // 1. Top Action Toolbar: (O) InfoCard [Dump ▼] | (O) X-Ray [Dump ▼]
            float rightY = DrawTopActionBar(inner, columnStartY, rightX, rightWidth, settings);

            string liveExplanation = TestBench.GetCachedExplanation(settings, out float biologicalBaseline);

            // 2. Comparison Delta Banner
            Rect bannerRect = new Rect(inner.x + rightX, rightY, rightWidth, 24f);
            Widgets.DrawBoxSolid(bannerRect, SettingsViewUtilities.DarkTargetHighlightColor);

            float deltaMass = TestBench.lastCalculatedDelta;
            float percentChange = biologicalBaseline > 0f ? deltaMass / biologicalBaseline : 0f;
            string percentText = (deltaMass >= 0f ? "+" : "") + ReportFormatter.FormatPercentage(percentChange, true);
            string deltaFormattedText = "OverHaulers_Delta_MassChange".Translate(ReportFormatter.FormatMass(deltaMass, true, true), percentText).ToString();
            Color deltaColor = deltaMass > 0.001f ? settings.colorBoosted : (deltaMass < -0.001f ? settings.colorCritical : settings.colorHealthy);

            // Fetch resolution method [0/1/2/3] for the active subject
            CalibrationMethod method = SpeciesBaselineCalibration.GetResolutionMethod(TestBench.ActiveSubject?.RaceDef);
            string methodTag = $"[{(int)method}]";

            string baselineComparisonLine = "OverHaulers_Delta_ComparisonLine".Translate(
                "OverHaulers_Delta_Baseline".Translate().ToString() + " " + methodTag, ReportFormatter.FormatMass(biologicalBaseline, true, false),
                "OverHaulers_Delta_Simulated".Translate().ToString(), ReportFormatter.FormatMass(TestBench.lastCalculatedFinalMass, true, false)
            ).ToString();

            Text.Font = GameFont.Tiny;
            float baselineTextWidth = Text.CalcSize(baselineComparisonLine).x;
            Rect baselineLabelRect = new Rect(bannerRect.x + 8f, bannerRect.y + 3f, baselineTextWidth, 20f);
            Widgets.Label(baselineLabelRect, baselineComparisonLine);

            ThingDef subjectRace = TestBench.ActiveSubject?.RaceDef;
            string methodTooltip = BuildCalibrationTooltip(subjectRace, method);
            TooltipHandler.TipRegion(baselineLabelRect, methodTooltip);

            GUI.color = deltaColor;
            Widgets.Label(new Rect(bannerRect.x + 8f + baselineTextWidth, bannerRect.y + 3f, rightWidth - baselineTextWidth - 16f, 20f), deltaFormattedText);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            rightY += 30f;

            // 3. InfoCard Breakdown Preview Box
            string printableExplanation = liveExplanation.Replace(InfoCardOverlay.TagSentinel, "").Replace(InfoCardOverlay.TagProsthetic, "    ").Replace(InfoCardOverlay.TagAthletic, "    ").Replace(InfoCardOverlay.TagInjury, "    ");
            float previewBoxHeight = Math.Max(leftColumnHeight - (rightY - columnStartY), Text.CalcHeight(printableExplanation, rightWidth - SettingsViewUtilities.PanelPadding * 2f) + 20f);

            Rect previewBoxRect = new Rect(inner.x + rightX, rightY, rightWidth, previewBoxHeight);
            Widgets.DrawMenuSection(previewBoxRect);
            InfoCardOverlay.DrawUnifiedExplanationLabel(previewBoxRect.ContractedBy(SettingsViewUtilities.PanelPadding), liveExplanation);

            return rightY + previewBoxHeight;
        }

        /// <summary>
        /// Builds the tooltip text for the calibration method of a given species.
        /// </summary>
        /// <param name="raceDef">The race definition of the species for which to build the tooltip.</param>
        /// <param name="method">The calibration method of the species.</param>
        /// <returns>A string containing the formatted tooltip text for the specified species and calibration method.</returns>
        private static string BuildCalibrationTooltip(ThingDef raceDef, CalibrationMethod method)
        {
            string statusTitle;
            string statusDesc;

            if (method == CalibrationMethod.TestingFallback)
            {
                if (SpeciesBaselineCalibration.IsPendingLiveRescue(raceDef))
                {
                    statusTitle = "OverHaulers_Calibration_Status_PendingComps".Translate().ToString();
                    statusDesc = "OverHaulers_Calibration_Desc_PendingComps".Translate().ToString();
                }
                else
                {
                    statusTitle = "OverHaulers_Calibration_Status_NonPack".Translate().ToString();
                    statusDesc = "OverHaulers_Calibration_Desc_NonPack".Translate().ToString();
                }
            }
            else if (method == CalibrationMethod.PristineDummy)
            {
                statusTitle = "OverHaulers_Calibration_Status_Pristine".Translate().ToString();
                statusDesc = "OverHaulers_Calibration_Desc_Pristine".Translate().ToString();
            }
            else if (method == CalibrationMethod.LiveRescue)
            {
                statusTitle = "OverHaulers_Calibration_Status_LiveRescue".Translate().ToString();
                statusDesc = "OverHaulers_Calibration_Desc_LiveRescue".Translate().ToString();
            }
            else
            {
                statusTitle = "OverHaulers_Calibration_Status_Emergency".Translate().ToString();
                statusDesc = "OverHaulers_Calibration_Desc_Emergency".Translate().ToString();
            }

            string activeHeader = "OverHaulers_Calibration_ActiveHeader".Translate((int)method, statusTitle).ToString();
            string lastErrorDetail = SpeciesBaselineCalibration.GetLastErrorDetail(raceDef);
            string errorSection = !string.IsNullOrEmpty(lastErrorDetail)
                ? "\n\n" + "OverHaulers_Calibration_DiagnosticDetail".Translate(lastErrorDetail).ToString()
                : string.Empty;

            string taxonomyLegend = "OverHaulers_Calibration_TaxonomyHeader".Translate().ToString();

            return $"{activeHeader}\n{statusDesc}{errorSection}\n\n----------------------------------------\n{taxonomyLegend}";
        }

        #endregion
    }
}
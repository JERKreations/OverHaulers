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
        private static readonly Func<Settings, ExportScope, ExportFormat, TestSubjectEntry, string> groupingExport = GroupingExportManager.ExportGroupingData;
        private static readonly Func<Settings, ExportScope, ExportFormat, TestSubjectEntry, string> topologyExport = TopologyExportManager.ExportTopologyData;

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

            DrawTestBenchHeaderDirect(viewRect, "OverHaulers_TestBench_Header", settings, resetAction, localY, out localY, leftWidth, rightWidth, gap);

            float columnStartY = localY;

            // LEFT COLUMN: Subject Picker, Topology Badge & Universal Sandbox Medical Planner (Flattened)
            float leftY = DrawLeftControlColumn(viewRect, columnStartY, leftWidth, settings);

            // RIGHT COLUMN: Top Action Toolbar (View Modes / Exports) & Live Delta Preview
            float rightY = DrawRightPreviewColumn(viewRect, columnStartY, leftWidth + gap, rightWidth, leftY - columnStartY, settings);

            currentY = Math.Max(leftY, rightY) + 10f;
        }

        #endregion

        #region 3. TOP ACTION TOOLBAR & EXPORT MENUS

        /// <summary>
        /// Draws the Test Bench title, verbose toggle, and reset control aligned with the columns.
        /// </summary>
        /// <param name="containerRect">The rectangle defining the container area for the header.</param>
        /// <param name="headerLabelKey">The translation key for the header title.</param>
        /// <param name="settings">The current settings object.</param>
        /// <param name="resetAction">The action to invoke when the reset button is clicked.</param>
        /// <param name="currentY">The current vertical position within the container.</param>
        /// <param name="nextY">The vertical position after drawing the header.</param>
        /// <param name="leftWidth">The width allocated for the left column.</param>
        /// <param name="rightWidth">The width allocated for the right column.</param>
        /// <param name="columnGap">The gap between the left and right columns.</param>
        private static void DrawTestBenchHeaderDirect(
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

            GameFont origFont = Text.Font;
            float titleHeight;
            Text.Font = GameFont.Medium;
            try
            {
                titleHeight = Math.Max(30f, SettingsViewUtilities.MeasuredTextHeight(title, leftWidth));
                Widgets.Label(new Rect(containerRect.x, currentY, leftWidth, titleHeight), title);
            }
            finally
            {
                Text.Font = origFont;
            }

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
                SettingsViewUtilities.OnSettingMutated(SettingMutationScope.Presentation);
            }

            // [Reset] Button - Resets Test Bench sandbox state only
            if (SettingsViewUtilities.DrawButton(resetRect, resetLabel))
            {
                resetAction?.Invoke();
                ResetPlannerViewState();
            }

            nextY = Math.Max(currentY + titleHeight, Math.Max(verboseRect.yMax, resetRect.yMax)) + SettingsViewUtilities.ControlGap;
        }

        /// <summary>
        /// Draws the top action toolbar of the right preview column, which includes radio buttons for selecting the view mode
        /// (InfoCard or TopologyXRay) and corresponding export menus.
        /// </summary>
        /// <param name="inner">The inner rectangle defining the drawing area.</param>
        /// <param name="localY">The current vertical position within the inner rectangle.</param>
        /// <param name="rightX">The horizontal position of the right column.</param>
        /// <param name="rightWidth">The width allocated for the right column.</param>
        /// <param name="settings">The current settings object.</param>
        /// <returns>The updated vertical position after drawing the top action bar.</returns>
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

        /// <summary>
        /// Draws an export view control, including a radio button and an export button.
        /// </summary>
        /// <param name="x">The horizontal position of the control.</param>
        /// <param name="y">The vertical position of the control.</param>
        /// <param name="width">The total width allocated for the control.</param>
        /// <param name="viewLabel">The label for the view radio button.</param>
        /// <param name="exportLabel">The label for the export button.</param>
        /// <param name="desiredExportWidth">The desired width for the export button.</param>
        /// <param name="mode">The view mode associated with the radio button.</param>
        /// <param name="tooltipKey">The translation key for the tooltip.</param>
        /// <param name="settings">The current settings object.</param>
        /// <param name="subjectLabel">The label for the test subject.</param>
        /// <param name="bodyDefName">The body definition name of the test subject.</param>
        /// <param name="activeSubject">The active test subject entry.</param>
        /// <param name="exportAction">The action to invoke when exporting.</param>
        /// <returns>The total height of the control.</returns>
        private static float DrawExportViewControl(
            float x, float y, float width, string viewLabel, string exportLabel, float desiredExportWidth,
            TestBenchViewMode mode, string tooltipKey, Settings settings, string subjectLabel, string bodyDefName,
            TestSubjectEntry activeSubject, Func<Settings, ExportScope, ExportFormat, TestSubjectEntry, string> exportAction)
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
                OpenExportMenu(settings, subjectLabel, bodyDefName, activeSubject, exportAction);
            }
            if (SettingsViewUtilities.MeasuredTextWidth(viewLabel) + 30f > radioWidth) TooltipHandler.TipRegion(radioRect, viewLabel);
            return Math.Max(radioRect.yMax, exportRect.yMax) - y;
        }

        /// <summary>
        /// Opens the export menu for exporting test bench data in various formats.
        /// </summary>
        /// <param name="settings">The settings object containing the test bench configuration.</param>
        /// <param name="subjectLabel">The label of the subject being tested.</param>
        /// <param name="bodyDefName">The body definition name of the subject.</param>
        /// <param name="activeSubject">The currently active test subject entry.</param>
        /// <param name="exportAction">The action to invoke for exporting the data.</param>
        private static void OpenExportMenu(
            Settings settings, 
            string subjectLabel, 
            string bodyDefName, 
            TestSubjectEntry activeSubject, 
            Func<Settings, ExportScope, ExportFormat, TestSubjectEntry, string> exportAction)
        {
            List<FloatMenuOption> exportOptions = new List<FloatMenuOption>
            {
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_Selected".Translate(subjectLabel, "TXT").ToString(), () =>
                {
                    string path = exportAction(settings, ExportScope.SelectedSubject, ExportFormat.Text, activeSubject);
                    NotifyExportResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_Selected".Translate(subjectLabel, "XML").ToString(), () =>
                {
                    string path = exportAction(settings, ExportScope.SelectedSubject, ExportFormat.Xml, activeSubject);
                    NotifyExportResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_Archetype".Translate(bodyDefName, "TXT").ToString(), () =>
                {
                    string path = exportAction(settings, ExportScope.BodyDefArchetype, ExportFormat.Text, activeSubject);
                    NotifyExportResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_Archetype".Translate(bodyDefName, "XML").ToString(), () =>
                {
                    string path = exportAction(settings, ExportScope.BodyDefArchetype, ExportFormat.Xml, activeSubject);
                    NotifyExportResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_FullCensus".Translate("TXT").ToString(), () =>
                {
                    string path = exportAction(settings, ExportScope.FullCensus, ExportFormat.Text, activeSubject);
                    NotifyExportResult(path);
                }),
                new FloatMenuOption("OverHaulers_TestBench_ExportOption_FullCensus".Translate("XML").ToString(), () =>
                {
                    string path = exportAction(settings, ExportScope.FullCensus, ExportFormat.Xml, activeSubject);
                    NotifyExportResult(path);
                })
            };
            Find.WindowStack.Add(new FloatMenu(exportOptions));
        }

        /// <summary>
        /// Notifies the user of the result of an export operation.
        /// </summary>
        /// <param name="path">The file path of the exported result.</param>
        private static void NotifyExportResult(string path)
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
        /// <param name="settings">The settings object containing the test bench configuration.</param>
        /// <returns>The updated vertical position after drawing the left control column.</returns>
        private static float DrawLeftControlColumn(Rect inner, float columnStartY, float leftWidth, Settings settings)
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

                GameFont origFont = Text.Font;
                Color origColor = GUI.color;
                try
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = SettingsViewUtilities.DescriptionTextColor;
                    Widgets.Label(new Rect(inner.x + 4f, leftY, leftWidth - 4f, 16f), "OverHaulers_TestBench_ActiveTopology".Translate(subject.BodyDef.defName, partCount, modName).ToString());
                }
                finally
                {
                    GUI.color = origColor;
                    Text.Font = origFont;
                }
                leftY += 20f;
            }

            // 3. Universal Sandbox Medical Planner (Flattened)
            leftY = DrawUniversalSandboxPlannerView(inner, leftY, leftWidth, settings);

            return leftY;
        }

        #endregion

        #region 5. UNIVERSAL SANDBOX ACCORDION PLANNER VIEW (FLATTENED)

        #region 5A. Planner Main Controls & Simulated Conditions

        /// <summary>
        /// Draws the universal sandbox planner view, including search, condition controls, and anatomy.
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

            // 2. Condition and anatomy controls
            float halfWidth = (leftWidth - 6f) / 2f;
            string drugLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_AddCondition");
            string expandLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_ExpandAll");
            string collapseLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_CollapseAll");
            Rect rowBounds = new Rect(inner.x, leftY, leftWidth, 0f);
            float rowX = inner.x;
            float rowY = leftY;
            float rowHeight = 0f;
            Rect drugButtonRect = SettingsViewUtilities.PlaceButton(rowBounds, ref rowX, ref rowY, ref rowHeight,
                drugLabel, 4f, minWidth: halfWidth, minHeight: 22f);
            TooltipHandler.TipRegion(drugButtonRect, SettingsViewUtilities.CachedText("OverHaulers_Sandbox_AddCondition_Tooltip"));
            GameFont origRowFont = Text.Font;
            Rect expandButtonRect;
            Rect collapseButtonRect;
            Text.Font = GameFont.Tiny;
            try
            {
                expandButtonRect = SettingsViewUtilities.PlaceButton(rowBounds, ref rowX, ref rowY, ref rowHeight,
                    expandLabel, 4f, minWidth: (halfWidth - 4f) / 2f, minHeight: 22f);
                collapseButtonRect = SettingsViewUtilities.PlaceButton(rowBounds, ref rowX, ref rowY, ref rowHeight,
                    collapseLabel, 4f, minWidth: (halfWidth - 4f) / 2f, minHeight: 22f);
            }
            finally
            {
                Text.Font = origRowFont;
            }

            if (SettingsViewUtilities.DrawButton(drugButtonRect, drugLabel))
            {
                OpenConditionCategoryMenu(harness);
            }

            Text.Font = GameFont.Tiny;
            try
            {
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
            }
            finally
            {
                Text.Font = origRowFont;
            }
            leftY = rowY + rowHeight + 4f;

            // 3. Active simulated conditions
            if (harness.SimulatedConditions.Count > 0)
            {
                GameFont origCondFont = Text.Font;
                Text.Font = GameFont.Tiny;
                try
                {
                    for (int i = 0; i < harness.SimulatedConditions.Count; i++)
                    {
                        SimulatedCondition condition = harness.SimulatedConditions[i];
                        Hediff hediff = condition.Hediff;
                        string label = MedicalCatalog.GetFormattedConditionLabel(hediff);
                        string stageLabel = hediff.CurStage?.label;
                        if (!string.IsNullOrEmpty(stageLabel) &&
                            label.IndexOf(stageLabel, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            label = "OverHaulers_Sandbox_Condition_Stage".Translate(label, stageLabel.CapitalizeFirst()).ToString();
                        }
                        if (hediff.Part != null)
                        {
                            label = "OverHaulers_Sandbox_Condition_OnPart".Translate(label, hediff.Part.LabelCap.ToString()).ToString();
                        }

                        float tagHeight = Math.Max(18f, SettingsViewUtilities.MeasuredTextHeight(label, leftWidth - 36f) + 2f);
                        Rect tagRect = new Rect(inner.x + 4f, leftY, leftWidth - 8f, tagHeight);
                        Widgets.DrawBoxSolid(tagRect, new Color(0.15f, 0.20f, 0.25f, 0.8f));
                        Widgets.Label(new Rect(tagRect.x + 4f, tagRect.y + 1f, tagRect.width - 24f, tagRect.height), label);

                        if (Widgets.ButtonText(new Rect(tagRect.x + tagRect.width - 18f, tagRect.y, 18f, 18f), "✕"))
                        {
                            RunConditionAction(() => harness.RemoveSimulatedCondition(condition));
                            break;
                        }
                        leftY += tagHeight + 2f;

                        if (SandboxPawnHarness.TryGetConditionSeverityRange(condition, out float min, out float max))
                        {
                            string severityLabel = "OverHaulers_Sandbox_Condition_Severity".Translate(
                                hediff.Severity.ToString("F1")).ToString();
                            float labelWidth = Math.Min(leftWidth * 0.45f, SettingsViewUtilities.MeasuredTextWidth(severityLabel) + 4f);
                            Widgets.Label(new Rect(inner.x + 4f, leftY, labelWidth, 20f), severityLabel);
                            float rawSeverity = Widgets.HorizontalSlider(
                                new Rect(inner.x + labelWidth + 4f, leftY, leftWidth - labelWidth - 8f, 20f),
                                hediff.Severity, min, max, true);

                            float snappedSeverity = (float)Math.Round(rawSeverity, 1, MidpointRounding.ToEven);
                            snappedSeverity = Mathf.Clamp(snappedSeverity, min, max);

                            // Snap to F1 precision increments to smooth micro-step re-solve stutter
                            if (Math.Abs(snappedSeverity - hediff.Severity) > 0.01f)
                            {
                                RunConditionAction(() => harness.SetConditionSeverity(condition, snappedSeverity));
                            }
                            leftY += 22f;
                        }
                        else if (hediff.def.stages != null && hediff.def.stages.Count > 1)
                        {
                            Rect stageButton = new Rect(inner.x + 4f, leftY, leftWidth - 8f, 20f);
                            if (SettingsViewUtilities.DrawButton(stageButton, SettingsViewUtilities.CachedText("OverHaulers_Sandbox_Condition_StageButton")))
                                OpenConditionStageMenu(harness, condition);
                            leftY += 22f;
                        }
                    }
                }
                finally
                {
                    Text.Font = origCondFont;
                }
                leftY += 4f;
            }

            if (harness.HasModifications && TestBench.ImmediateDeathRisk != false)
            {
                string warningKey = TestBench.ImmediateDeathRisk == true
                    ? "OverHaulers_Sandbox_ImmediateDeath"
                    : "OverHaulers_Sandbox_DeathCheckUnavailable";
                string warning = SettingsViewUtilities.CachedText(warningKey);
                GameFont origWarnFont = Text.Font;
                Color origWarnColor = GUI.color;
                float warningHeight;
                try
                {
                    Text.Font = GameFont.Tiny;
                    warningHeight = SettingsViewUtilities.MeasuredTextHeight(warning, leftWidth);
                    GUI.color = settings?.colorCritical ?? PresentationDefaults.ColorCriticalDefault;
                    Widgets.Label(new Rect(inner.x, leftY, leftWidth, warningHeight), warning);
                }
                finally
                {
                    GUI.color = origWarnColor;
                    Text.Font = origWarnFont;
                }
                leftY += warningHeight + 4f;
            }

            // 4. Dynamic Revert Button
            if (harness.HasModifications)
            {
                bool isLiveSubject = TestBench.ActiveSubject?.IsLivePawn == true;
                Color critColor = settings?.colorCritical ?? PresentationDefaults.ColorCriticalDefault;
                GameFont origRevertFont = Text.Font;
                Color origRevertColor = GUI.color;
                Vector2 revertSize;
                try
                {
                    GUI.color = Color.Lerp(Color.white, critColor, 0.6f);
                    Text.Font = GameFont.Tiny;

                    string revertButtonLabel = isLiveSubject
                        ? "OverHaulers_Sandbox_RevertLive".Translate().ToString()
                        : "OverHaulers_Sandbox_RevertBaseline".Translate().ToString();

                    revertSize = SettingsViewUtilities.ButtonSize(revertButtonLabel, leftWidth, minHeight: 22f, fixedWidth: leftWidth);
                    Rect clearPlanRect = new Rect(inner.x, leftY, revertSize.x, revertSize.y);
                    if (SettingsViewUtilities.DrawButton(clearPlanRect, revertButtonLabel))
                    {
                        harness.ResetToSource();
                        TestBench.MarkDirty();
                    }
                }
                finally
                {
                    Text.Font = origRevertFont;
                    GUI.color = origRevertColor;
                }
                leftY += revertSize.y + 4f;
            }

            leftY = DrawLiveApplicationControl(inner, leftY, leftWidth, harness);

            // 5. Flattened Accordions
            if (model.EvaluatedParts.Count > 0)
            {
                Rect groupViewRect = new Rect(inner.x, leftY, leftWidth, 0f);

                for (int g = 0; g < model.EvaluatedParts.Count; g++)
                {
                    PartViewNode groupNode = model.EvaluatedParts[g];
                    leftY = DrawPassiveGroupAccordion(groupViewRect, leftY, groupNode, harness, settings);
                }
                leftY += 4f;
            }

            return leftY;
        }

        /// <summary>
        /// Opens the condition category menu for the specified sandbox pawn harness.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness for which to open the condition category menu.</param>
        private static void OpenConditionCategoryMenu(SandboxPawnHarness harness)
        {
            IReadOnlyList<MedicalConditionCategory> categories = MedicalCatalog.GetConditionCategories();
            List<FloatMenuOption> options = new List<FloatMenuOption>(categories.Count);
            for (int i = 0; i < categories.Count; i++)
            {
                MedicalConditionCategory category = categories[i];
                if ((category == MedicalConditionCategory.MedicalTargetUnknown ||
                     category == MedicalConditionCategory.Other) &&
                    BuildConditionEntryOptions(harness, category).Count == 0)
                    continue;

                string key;
                switch (category)
                {
                    case MedicalConditionCategory.Ingestibles:
                        key = "OverHaulers_Sandbox_Condition_Ingestibles";
                        break;
                    case MedicalConditionCategory.SystemicMedical:
                        key = "OverHaulers_Sandbox_Condition_SystemicMedical";
                        break;
                    case MedicalConditionCategory.LocalizedMedical:
                        key = "OverHaulers_Sandbox_Condition_LocalizedMedical";
                        break;
                    case MedicalConditionCategory.MedicalTargetUnknown:
                        key = "OverHaulers_Sandbox_Condition_MedicalTargetUnknown";
                        break;
                    default:
                        key = "OverHaulers_Sandbox_Condition_Other";
                        break;
                }
                options.Add(new FloatMenuOption(SettingsViewUtilities.CachedText(key),
                    () => OpenConditionEntryMenu(harness, category)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Opens the condition entry menu for the specified sandbox pawn harness and medical condition category.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness for which to open the condition entry menu.</param>
        /// <param name="category">The medical condition category for which to display entries.</param>
        private static void OpenConditionEntryMenu(SandboxPawnHarness harness, MedicalConditionCategory category)
        {
            List<FloatMenuOption> options = BuildConditionEntryOptions(harness, category);
            if (options.Count == 0)
                options.Add(new FloatMenuOption(SettingsViewUtilities.CachedText("OverHaulers_Sandbox_Condition_NoEntries"), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Builds the list of condition entry options for the specified sandbox pawn harness and medical condition category.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness for which to build the condition entry options.</param>
        /// <param name="category">The medical condition category for which to build the condition entry options.</param>
        /// <returns>A list of float menu options representing the condition entries.</returns>
        private static List<FloatMenuOption> BuildConditionEntryOptions(SandboxPawnHarness harness, MedicalConditionCategory category)
        {
            IReadOnlyList<MedicalConditionEntry> entries = MedicalCatalog.GetConditionEntries(category);
            List<FloatMenuOption> options = new List<FloatMenuOption>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                MedicalConditionEntry entry = entries[i];
                bool wholeBody = HasWholeBodyConditionTarget(harness.SandboxPawn, entry);
                bool hasKnownPartTarget = false;
                for (int t = 0; t < entry.Targets.Count; t++)
                {
                    MedicalConditionTarget target = entry.Targets[t];
                    if (!ConditionTargetMatchesPawn(harness.SandboxPawn, target)) continue;
                    MedicalConditionTargetKind kind = target.Kind;
                    if (kind == MedicalConditionTargetKind.SpecificPart ||
                        kind == MedicalConditionTargetKind.AnyLivePart ||
                        kind == MedicalConditionTargetKind.InfectablePart)
                    {
                        hasKnownPartTarget = true;
                        break;
                    }
                }

                List<BodyPartRecord> parts = GetConditionTargetParts(harness.SandboxPawn, entry);
                if (!wholeBody && entry.HasPartLocalImpact && !entry.HasSystemicStageImpact && parts.Count == 0)
                    continue;
                bool available = wholeBody || parts.Count > 0;
                bool hasSingleTarget = (wholeBody && parts.Count == 0) || (!wholeBody && parts.Count == 1);
                bool active = false;
                for (int a = 0; a < harness.SimulatedConditions.Count; a++)
                {
                    if (harness.SimulatedConditions[a].Hediff.def == entry.Hediff)
                    {
                        active = true;
                        break;
                    }
                }

                string label = active ? "✓ " + entry.Label : entry.Label;
                if (!available)
                {
                    string reason = SettingsViewUtilities.CachedText(hasKnownPartTarget
                        ? "OverHaulers_Sandbox_Condition_NoTargets"
                        : "OverHaulers_Sandbox_Condition_UnknownTarget");
                    label = "OverHaulers_Sandbox_Condition_Unavailable".Translate(label, reason).ToString();
                }

                FloatMenuOption option = new FloatMenuOption(label, available
                    ? (Action)(() =>
                    {
                        if (hasSingleTarget)
                            ToggleCondition(harness, entry.Hediff, wholeBody ? null : parts[0]);
                        else
                            OpenConditionTargetMenu(harness, entry);
                    })
                    : null);
                option.Disabled = !available;
                options.Add(option);
            }
            return options;
        }

        /// <summary>
        /// Determines whether the specified pawn has a whole-body condition target for the given medical condition entry.
        /// </summary>
        /// <param name="pawn">The pawn to check for a whole-body condition target.</param>
        /// <param name="entry">The medical condition entry to check against.</param>
        /// <returns><c>true</c> if the pawn has a whole-body condition target for the entry; otherwise, <c>false</c>.</returns>
        private static bool HasWholeBodyConditionTarget(Pawn pawn, MedicalConditionEntry entry)
        {
            return MedicalCatalog.HasWholeBodyConditionTarget(pawn, entry);
        }

        /// <summary>
        /// Determines whether the specified medical condition target matches the given pawn.
        /// </summary>
        /// <param name="pawn">The pawn to check against the condition target.</param>
        /// <param name="target">The medical condition target to check.</param>
        /// <returns><c>true</c> if the condition target matches the pawn; otherwise, <c>false</c>.</returns>
        internal static bool ConditionTargetMatchesPawn(Pawn pawn, MedicalConditionTarget target)
        {
            return MedicalCatalog.ConditionTargetMatchesPawn(pawn, target);
        }

        /// <summary>
        /// Gets the list of body parts that are relevant condition targets for the specified pawn and medical condition entry.
        /// </summary>
        /// <param name="pawn">The pawn for which to get the condition target parts.</param>
        /// <param name="entry">The medical condition entry to check against.</param>
        /// <returns>A list of body parts that are relevant condition targets for the entry.</returns>
        internal static List<BodyPartRecord> GetConditionTargetParts(Pawn pawn, MedicalConditionEntry entry)
        {
            return MedicalCatalog.GetConditionTargetParts(pawn, entry, solverRelevantOnly: true);
        }

        /// <summary>
        /// Opens a menu for selecting the condition target for the specified sandbox pawn harness and medical condition entry.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness for which to open the condition target menu.</param>
        /// <param name="entry">The medical condition entry for which to select the condition target.</param>
        /// <remarks>
        /// This method displays a menu allowing the user to choose the condition target for the specified pawn and medical condition entry.
        /// It automatically marks already selected targets with a checkmark.
        /// </remarks>
        private static void OpenConditionTargetMenu(SandboxPawnHarness harness, MedicalConditionEntry entry)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (HasWholeBodyConditionTarget(harness.SandboxPawn, entry))
            {
                string label = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_Condition_WholeBody");
                if (harness.FindSimulatedCondition(entry.Hediff, null) != null) label = "✓ " + label;
                options.Add(new FloatMenuOption(label, () => ToggleCondition(harness, entry.Hediff, null)));
            }

            List<BodyPartRecord> parts = GetConditionTargetParts(harness.SandboxPawn, entry);
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                string label = part.LabelCap.ToString();
                if (harness.FindSimulatedCondition(entry.Hediff, part) != null) label = "✓ " + label;
                options.Add(new FloatMenuOption(label, () => ToggleCondition(harness, entry.Hediff, part)));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption(SettingsViewUtilities.CachedText("OverHaulers_Sandbox_Condition_NoTargets"), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Toggles the specified medical condition for the given sandbox pawn harness and body part.
        /// </summary>
        /// <param name="harness"></param>
        /// <param name="def"></param>
        /// <param name="part"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private static void ToggleCondition(SandboxPawnHarness harness, HediffDef def, BodyPartRecord part)
        {
            SimulatedCondition selected = harness.FindSimulatedCondition(def, part);
            if (selected == null && part != null &&
                MedicalCatalog.TryGetConditionEntry(def, out MedicalConditionEntry entry) &&
                (entry.Sources & MedicalConditionSource.WoundInfection) != 0)
            {
                Pawn pawn = harness.SandboxPawn;
                string warning = "OverHaulers_Sandbox_Condition_HypotheticalInfection".Translate(
                    def.LabelCap.ToString(), part.LabelCap.ToString()).ToString();
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(warning, () =>
                    RunConditionAction(() =>
                    {
                        if (harness.SandboxPawn != pawn || !GetConditionTargetParts(pawn, entry).Contains(part))
                            throw new InvalidOperationException("The selected infection target is no longer available.");
                        harness.SimulateCondition(def, part);
                    })));
                return;
            }

            RunConditionAction(() =>
            {
                SimulatedCondition active = harness.FindSimulatedCondition(def, part);
                if (active == null) harness.SimulateCondition(def, part);
                else harness.RemoveSimulatedCondition(active);
            });
        }

        /// <summary>
        /// Opens a menu for selecting the stage of the specified simulated condition.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness associated with the condition.</param>
        /// <param name="condition">The simulated condition for which to select the stage.</param>
        /// <remarks>
        /// This method displays a menu allowing the user to choose the stage of the specified simulated condition.
        /// It automatically formats the stage label and severity for display.
        /// </remarks>
        private static void OpenConditionStageMenu(SandboxPawnHarness harness, SimulatedCondition condition)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<HediffStage> stages = condition.Hediff.def.stages;
            for (int i = 0; i < stages.Count; i++)
            {
                if (!SandboxPawnHarness.TryGetConditionStageSeverity(condition, i, out float severity)) continue;
                string stageLabel = !string.IsNullOrEmpty(stages[i].label)
                    ? stages[i].label.CapitalizeFirst()
                    : "OverHaulers_Sandbox_Condition_StageNumber".Translate(i + 1).ToString();
                string label = "OverHaulers_Sandbox_Condition_StageOption".Translate(
                    stageLabel, ReportFormatter.FormatAdaptive(severity, true)).ToString();
                float targetSeverity = severity;
                options.Add(new FloatMenuOption(label, () =>
                {
                    RunConditionAction(() => harness.SetConditionSeverity(condition, targetSeverity));
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption(SettingsViewUtilities.CachedText("OverHaulers_Sandbox_Condition_NoEntries"), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Executes the specified action related to a simulated condition, handling exceptions and updating the test bench state.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <remarks>
        /// This method wraps the execution of the specified action in a try-catch block to handle exceptions.
        /// It also ensures that the test bench state is updated after the action is executed.
        /// </remarks>
        private static void RunConditionAction(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                OHLog.Warn("TestBench.Condition", ex, "Could not update the simulated condition.");
                Messages.Message("OverHaulers_Sandbox_Condition_Error".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
            }
            finally
            {
                TestBench.Harness.DirtyHealthCache();
                TestBench.MarkDirty();
            }
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
        /// <param name="settings">The current settings object providing color and presentation preferences.</param>
        /// <returns>The updated vertical position after drawing the accordion.</returns>
        private static float DrawPassiveGroupAccordion(
            Rect viewRect,
            float currentY,
            PartViewNode groupNode,
            SandboxPawnHarness harness,
            Settings settings)
        {
            if (groupNode == null) return currentY;

            string searchFilter = sandboxSearchText.Trim().ToLowerInvariant();
            bool isSearching = !string.IsNullOrEmpty(searchFilter);

            if (isSearching && !GroupMatchesFilter(groupNode, searchFilter))
            {
                return currentY;
            }

            bool isExpanded = isSearching || expandedGroupLabels.Contains(groupNode.Label);

            GameFont origFont = Text.Font;
            Color origColor = GUI.color;
            Rect headerRect;
            try
            {
                Text.Font = GameFont.Tiny;
                string operationLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_OpBtn");
                Vector2 operationSize = SettingsViewUtilities.ButtonSize(operationLabel, viewRect.width,
                    minWidth: 70f, minHeight: 20f, maxWidth: viewRect.width * 0.5f);
                headerRect = new Rect(viewRect.x, currentY, viewRect.width, Math.Max(24f, operationSize.y + 4f));
                Widgets.DrawBoxSolid(headerRect, new Color(0.12f, 0.15f, 0.18f, 0.9f));
                Widgets.DrawHighlightIfMouseover(headerRect);

                string expandArrow = isExpanded ? "▼ " : "▶ ";
                string headerText = $"{expandArrow}{groupNode.Label}";

                GUI.color = new Color(1f, 0.8f, 0.3f);
                Widgets.Label(new Rect(headerRect.x + 6f, headerRect.y + 3f, headerRect.width - operationSize.x - 10f, 18f), headerText.ToUpperInvariant());
                GUI.color = origColor;

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
            }
            finally
            {
                GUI.color = origColor;
                Text.Font = origFont;
            }
            currentY += headerRect.height + 2f;

            // Expanded Pre-Calculated Part Rows
            if (isExpanded && groupNode.SubParts != null)
            {
                for (int i = 0; i < groupNode.SubParts.Count; i++)
                {
                    currentY = DrawPassivePartNodeRecursive(viewRect, currentY, groupNode.SubParts[i], 0, harness, searchFilter, settings);
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
        /// <param name="settings">The current settings object providing color and presentation preferences.</param>
        /// <returns>The updated vertical position after drawing the part node and its children.</returns>
        private static float DrawPassivePartNodeRecursive(
            Rect viewRect,
            float currentY,
            PartViewNode node,
            int depth,
            SandboxPawnHarness harness,
            string searchFilter,
            Settings settings)
        {
            if (node == null) return currentY;

            bool matchesFilter = string.IsNullOrEmpty(searchFilter) || 
                                 node.Label.ToLowerInvariant().Contains(searchFilter) || 
                                 (node.Record?.def?.defName != null && node.Record.def.defName.ToLowerInvariant().Contains(searchFilter));

            if (matchesFilter)
            {
                GameFont origFont = Text.Font;
                Color origColor = GUI.color;
                bool prevEnabled = GUI.enabled;
                Rect rowRect;
                try
                {
                    Text.Font = GameFont.Tiny;
                    string operationLabel = SettingsViewUtilities.CachedText("OverHaulers_Sandbox_OpBtn");
                    Vector2 operationSize = SettingsViewUtilities.ButtonSize(operationLabel, viewRect.width,
                        minWidth: 70f, minHeight: 20f, maxWidth: viewRect.width * 0.5f);
                    rowRect = new Rect(viewRect.x, currentY, viewRect.width, Math.Max(22f, operationSize.y + 2f));
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    float indentOffset = Math.Min(depth * 8f, 24f);
                    float labelWidth = viewRect.width - operationSize.x - 6f - indentOffset;

                    Rect labelRect = new Rect(rowRect.x + 4f + indentOffset, rowRect.y + 2f, labelWidth, 18f);
                    Rect buttonRect = new Rect(rowRect.xMax - operationSize.x - 2f, rowRect.y, operationSize.x, operationSize.y);

                    bool isVirtualAnchor = node.Record != null && node.Record.coverageAbs <= 0f && node.Record != node.Record.body?.corePart;
                    bool hasOperations = node.Record != null && MedicalCatalog.HasAnyOperationsFor(node.Record.def);

                    string fullPartLabel = BuildPassiveNodeDisplayString(node, harness, isVirtualAnchor, settings, out Color statusColor);

                    GUI.color = statusColor;
                    Widgets.Label(labelRect, fullPartLabel);
                    GUI.color = origColor;

                    // Interactive Button for operable parts; Disabled Muted Button for Virtual Anchors
                    if (isVirtualAnchor && !hasOperations)
                    {
                        GUI.enabled = false;
                        Widgets.ButtonText(buttonRect, "—");
                        GUI.enabled = prevEnabled;
                        TooltipHandler.TipRegion(buttonRect, "OverHaulers_VirtualAnchor_Tooltip".Translate().ToString());
                    }
                    else if (node.Record != null && SettingsViewUtilities.DrawButton(buttonRect, operationLabel))
                    {
                        OpenPartSimulationMenu(node.Record, harness);
                    }
                }
                finally
                {
                    GUI.enabled = prevEnabled;
                    GUI.color = origColor;
                    Text.Font = origFont;
                }

                currentY += rowRect.height + 2f;
            }

            if (node.SubParts != null && node.SubParts.Count > 0)
            {
                for (int c = 0; c < node.SubParts.Count; c++)
                {
                    currentY = DrawPassivePartNodeRecursive(viewRect, currentY, node.SubParts[c], depth + 1, harness, searchFilter, settings);
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
        /// <param name="settings">The current settings object providing color and presentation preferences.</param>
        /// <param name="labelColor">The color to be applied to the label text, determined based on the node's status.</param>
        /// <returns>A formatted display string for the passive part node.</returns>
        private static string BuildPassiveNodeDisplayString(
            PartViewNode node, 
            SandboxPawnHarness harness, 
            bool isVirtualAnchor,
            Settings settings,
            out Color labelColor)
        {
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

            // 2. Synthetic Damage Intent (User-Applied Sandbox Delta)
            if (node.Record != null &&
                harness.DamageDeltas.TryGetValue(node.Record, out List<SimulatedDamage> damageEvents) &&
                damageEvents.Count > 0)
            {
                float maxHealth = node.Record.def.GetMaxHealth(harness.SandboxPawn);
                float healthRatio = damageEvents[damageEvents.Count - 1].HealthAfter / Math.Max(1f, maxHealth);
                labelColor = ReportFormatter.GetMedicalColor(healthRatio);
                return "OverHaulers_NodeBadge_Wounded".Translate(
                    node.Label,
                    ReportFormatter.FormatPercentage(healthRatio, true)).Colorize(labelColor);
            }

            // 3. Simulated Replacement (User-Applied Sandbox Delta)
            if (node.Record != null && harness.ReplacementDeltas.TryGetValue(node.Record, out SimulatedModification repl))
            {
                Color defaultColor = (repl.Efficiency > 1.0f)
                    ? boostColor
                    : ReportFormatter.GetMedicalColor(repl.Efficiency);
                labelColor = ReportFormatter.GetProstheticColor(defaultColor, repl.Efficiency);

                return "OverHaulers_NodeBadge_Prosthetic".Translate(node.Label, repl.DisplayLabel, ReportFormatter.FormatPercentage(repl.Efficiency, true)).Colorize(labelColor);
            }

            // 4. Simulated Implant (User-Applied Sandbox Delta)
            if (node.Record != null && harness.ImplantDeltas.TryGetValue(node.Record, out List<SimulatedModification> imps) && imps.Count > 0)
            {
                labelColor = boostColor;
                return "OverHaulers_NodeBadge_Implant".Translate(node.Label, imps[0].DisplayLabel).Colorize(labelColor);
            }

            // 5. Live Colonist Authentic Prosthetic
            if (node.HasProsthetic)
            {
                labelColor = (node.ProstheticColor != Color.white) 
                    ? node.ProstheticColor 
                    : ReportFormatter.GetMedicalColor(node.EfficiencyRating);

                return "OverHaulers_NodeBadge_Prosthetic".Translate(node.Label, node.ProstheticName, ReportFormatter.FormatPercentage(node.EfficiencyRating, true)).Colorize(labelColor);
            }

            // 6. Live Colonist Authentic Implant
            if (!string.IsNullOrEmpty(node.AthleticImplantName))
            {
                labelColor = (node.AthleticImplantColor != Color.white) 
                    ? node.AthleticImplantColor 
                    : Color.white;

                return "OverHaulers_NodeBadge_Implant".Translate(node.Label, node.AthleticImplantName).Colorize(labelColor);
            }

            // 7. Live Colonist Authentic Missing Bone
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
                var repls = MedicalCatalog.GetReplacementsFor(harness.SandboxPawn, targetRecords[i]);
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

            // 3. Randomized Synthetic Damage
            options.Add(new FloatMenuOption("OverHaulers_Sandbox_BulkDamage".Translate().ToString(), () =>
            {
                harness.SimulateRandomDamage(targetRecords);
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
        internal static void ApplyBulkReplacementTier(List<BodyPartRecord> targetRecords, SandboxPawnHarness harness, bool pickHighest)
        {
            Pawn pawn = harness.SandboxPawn;
            if (pawn?.health?.hediffSet == null) return;

            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            {
                for (int i = 0; i < targetRecords.Count; i++)
                {
                    BodyPartRecord currentPart = targetRecords[i];
                    var repls = MedicalCatalog.GetReplacementsFor(pawn, currentPart);
                    if (repls == null || repls.Count == 0) continue;

                    MedicalOperationEntry selectedOp = default;
                    bool found = false;

                    for (int j = 0; j < repls.Count; j++)
                    {
                        var op = repls[j];
                        if (!CanInstallReplacement(pawn, currentPart, op.Recipe)) continue;

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
        }

        /// <summary>
        /// Applies the same parent-socket and recipe checks to bulk and single replacements.
        /// Ensures that the replacement can be installed on the specified body part for the given pawn.
        /// </summary>
        /// <param name="pawn">The pawn on which the replacement is to be installed.</param>
        /// <param name="part">The body part record representing the target part for the replacement.</param>
        /// <param name="recipe">The recipe defining the replacement operation.</param>
        /// <returns>True if the replacement can be installed; otherwise, false.</returns>
        internal static bool CanInstallReplacement(Pawn pawn, BodyPartRecord part, RecipeDef recipe)
        {
            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            {
                if (!MedicalCatalog.IsRecipeAvailableToPawn(pawn, recipe)) return false;
                return !pawn.health.hediffSet.AncestorHasDirectlyAddedParts(part) &&
                    (part.parent == null || !pawn.health.hediffSet.PartIsMissing(part.parent)) &&
                    recipe.Worker.AvailableOnNow(pawn, part);
            }
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

            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            {
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
                IReadOnlyList<MedicalOperationEntry> availableReplacements =
                    MedicalCatalog.GetReplacementsFor(pawn, targetPart);
                if (availableReplacements != null && availableReplacements.Count > 0)
                {
                    for (int i = 0; i < availableReplacements.Count; i++)
                    {
                        MedicalOperationEntry operationEntry = availableReplacements[i];
                        bool canInstall = CanInstallReplacement(pawn, targetPart, operationEntry.Recipe);

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
                IReadOnlyList<MedicalOperationEntry> availableImplants =
                    MedicalCatalog.GetImplantsFor(pawn, targetPart);
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

                // 3. Synthetic Part Damage
                bool canDamage = !ancestorMissing && !partMissing && !ancestorHasAdded && targetPart.coverageAbs > 0f;
                if (targetPart.coverageAbs > 0f)
                {
                    FloatMenuOption opt = new FloatMenuOption(
                        "OverHaulers_Op_Damage".Translate().ToString(),
                        () => OpenPartDamageMenu(targetPart, harness)
                    );
                    opt.Disabled = !canDamage;
                    options.Add(opt);
                }

                // 4. Native Worker-Backed Part Removal / Natural Restoration
                IReadOnlyList<MedicalProcedureTarget> procedureTargets = MedicalCatalog.GetNativeProcedureTargets(pawn);
                for (int i = 0; i < procedureTargets.Count; i++)
                {
                    MedicalProcedureTarget target = procedureTargets[i];
                    if (target.Part != targetPart || target.UnavailableReason != null) continue;

                    if (target.Recipe?.Worker is Recipe_RemoveBodyPart)
                    {
                        RecipeDef recipe = target.Recipe;
                        string label = "OverHaulers_Op_RemovePart".Translate(target.Label).ToString();
                        options.Add(new FloatMenuOption(label, () =>
                        {
                            harness.SimulateRecipePartRemoval(targetPart, recipe, target.Label);
                            TestBench.MarkDirty();
                        }));
                    }
                    else if (target.Recipe?.Worker is Recipe_InstallNaturalBodyPart)
                    {
                        RecipeDef recipe = target.Recipe;
                        string label = "OverHaulers_Op_RestoreNaturalPart".Translate(target.Label).ToString();
                        options.Add(new FloatMenuOption(label, () =>
                        {
                            harness.SimulateRecipeNaturalRestoration(targetPart, recipe, target.Label);
                            TestBench.MarkDirty();
                        }));
                    }
                }

                // 5. Revert to Authentic Live / Baseline State
                if (harness.ReplacementDeltas.ContainsKey(targetPart) ||
                    harness.ImplantDeltas.ContainsKey(targetPart) ||
                    harness.DamageDeltas.ContainsKey(targetPart) ||
                    harness.PartStateDeltas.ContainsKey(targetPart))
                {
                    options.Add(new FloatMenuOption(
                        "OverHaulers_Op_Revert".Translate().ToString(),
                        () => {
                            harness.SimulateRevertPart(targetPart);
                            TestBench.MarkDirty();
                        }
                    ));
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Opens granular synthetic damage choices for one sandbox body part.
        /// </summary>
        /// <param name="part">The body part to apply damage to.</param>
        /// <param name="harness">The sandbox pawn harness associated with the body part.</param>
        /// <remarks>
        /// This method displays a menu allowing the user to choose the type and severity of synthetic damage to apply to the specified body part.
        /// It automatically formats the damage label and severity for display and checks if the damage can be applied.
        /// </remarks>
        private static void OpenPartDamageMenu(BodyPartRecord part, SandboxPawnHarness harness)
        {
            DamageDef[] damageTypes = { DamageDefOf.Crush, DamageDefOf.Cut, DamageDefOf.Burn };
            float[] severities = { 0.25f, 0.5f, 0.75f, 0.9f };
            List<FloatMenuOption> options = new List<FloatMenuOption>(damageTypes.Length * severities.Length);

            for (int d = 0; d < damageTypes.Length; d++)
            {
                DamageDef damage = damageTypes[d];
                if (damage == null) continue;

                for (int s = 0; s < severities.Length; s++)
                {
                    float severity = severities[s];
                    string label = "OverHaulers_Op_DamageEntry".Translate(
                        damage.LabelCap,
                        ReportFormatter.FormatPercentage(severity, true)).ToString();
                    bool canApply = harness.CanSimulateDamage(part, damage, severity);
                    FloatMenuOption option = new FloatMenuOption(label, canApply
                        ? (Action)(() =>
                    {
                        harness.SimulateDamage(part, damage, severity);
                        TestBench.MarkDirty();
                    })
                        : null);
                    option.Disabled = !canApply;
                    options.Add(option);
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        #endregion

        #endregion

        #region 6. RIGHT COLUMN PREVIEW & COMPARISON BANNER

        /// <summary>
        /// Draws the right-hand preview column, which includes the top action toolbar (View Modes & Export options),
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
            // 1. Top Action Toolbar: (O) InfoCard [Export ▼] | (O) X-Ray [Export ▼]
            float rightY = DrawTopActionBar(inner, columnStartY, rightX, rightWidth, settings);

            string liveExplanation = TestBench.GetCachedExplanation(settings, out float biologicalBaseline);

            // 2. Comparison Delta Banner
            Rect bannerRect = new Rect(inner.x + rightX, rightY, rightWidth, 24f);
            Widgets.DrawBoxSolid(bannerRect, SettingsViewUtilities.DarkTargetHighlightColor);

            float deltaMass = TestBench.lastCalculatedDelta;
            float percentChange = biologicalBaseline > 0f ? deltaMass / biologicalBaseline : 0f;
            string percentText = (deltaMass >= 0f ? "+" : "") + ReportFormatter.FormatPercentage(percentChange, true);
            string deltaFormattedText = "OverHaulers_Delta_MassChange".Translate(ReportFormatter.FormatMass(deltaMass, true, true), percentText).ToString();
            Color boostColor = settings?.colorBoosted ?? PresentationDefaults.ColorBoostedDefault;
            Color critColor = settings?.colorCritical ?? PresentationDefaults.ColorCriticalDefault;
            Color healthyColor = settings?.colorHealthy ?? PresentationDefaults.ColorHealthyDefault;
            Color deltaColor = deltaMass > 0.001f ? boostColor : (deltaMass < -0.001f ? critColor : healthyColor);

            // Fetch resolution method [0/1/2] for the active subject
            CalibrationMethod method = SpeciesBaselineCalibration.GetResolutionMethod(TestBench.ActiveSubject?.RaceDef);
            string methodTag = $"[{(int)method}]";

            string baselineComparisonLine = "OverHaulers_Delta_ComparisonLine".Translate(
                "OverHaulers_Delta_Baseline".Translate().ToString() + " " + methodTag, ReportFormatter.FormatMass(biologicalBaseline, true, false),
                "OverHaulers_Delta_Simulated".Translate().ToString(), ReportFormatter.FormatMass(TestBench.lastCalculatedFinalMass, true, false)
            ).ToString();

            GameFont origFont = Text.Font;
            Color origColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                float baselineTextWidth = Text.CalcSize(baselineComparisonLine).x;
                Rect baselineLabelRect = new Rect(bannerRect.x + 8f, bannerRect.y + 3f, baselineTextWidth, 20f);
                Widgets.Label(baselineLabelRect, baselineComparisonLine);

                ThingDef subjectRace = TestBench.ActiveSubject?.RaceDef;
                string methodTooltip = BuildCalibrationTooltip(subjectRace, method);
                TooltipHandler.TipRegion(baselineLabelRect, methodTooltip);

                GUI.color = deltaColor;
                Widgets.Label(new Rect(bannerRect.x + 8f + baselineTextWidth, bannerRect.y + 3f, rightWidth - baselineTextWidth - 16f, 20f), deltaFormattedText);
            }
            finally
            {
                GUI.color = origColor;
                Text.Font = origFont;
            }
            rightY += 30f;

            // 3. InfoCard Breakdown Preview Box
            string printableExplanation = liveExplanation
                .Replace(InfoCardOverlay.TagSentinel, "")
                .Replace(InfoCardOverlay.TagProsthetic, "   ")
                .Replace(InfoCardOverlay.TagAthletic, "   ")
                .Replace(InfoCardOverlay.TagInjury, "   ");
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
                if (SpeciesBaselineCalibration.IsPendingWorldLoadSweep(raceDef))
                {
                    statusTitle = "OverHaulers_Calibration_Status_PendingWorldLoad".Translate().ToString();
                    statusDesc = "OverHaulers_Calibration_Desc_PendingWorldLoad".Translate().ToString();
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
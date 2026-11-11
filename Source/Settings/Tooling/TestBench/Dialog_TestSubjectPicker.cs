using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>[TEST-03] In-memory picker preferences shared between dialog instances.</summary>
    internal sealed class TestSubjectPickerSession
    {
        private readonly Dictionary<GroupingDimension, HashSet<string>> expandedByDimension =
            new Dictionary<GroupingDimension, HashSet<string>>();

        public bool CaravanCapableOnly = true;

        public HashSet<string> ExpandedGroups(GroupingDimension dimension)
        {
            if (!expandedByDimension.TryGetValue(dimension, out HashSet<string> groups))
            {
                groups = new HashSet<string>();
                expandedByDimension.Add(dimension, groups);
            }
            return groups;
        }
    }

    /// <summary>
    /// [TEST-03] Interactive, searchable species and live pawn picker dialog for the Test Bench.
    /// Provides instant text search, caravan-capability filtering, dynamic 5-way slicing, and session-persistent group expansion.
    /// </summary>
    public class Dialog_TestSubjectPicker : Window
    {
        #region 1. CONSTANTS & FIELDS

        private const float HeaderHeight = 32f;
        private const float SearchBarHeight = 28f;
        private const float DimensionBarHeight = 26f;
        private const float RowHeight = 28f;
        private const float GroupHeaderHeight = 24f;
        private const float ListTopPadding = 4f;
        private const float GroupHeaderGap = 2f;
        private const float ListBottomPadding = 16f;
        private const float ListScrollbarGutter = 16f;

        private static readonly TestSubjectPickerSession session = new TestSubjectPickerSession();

        private string searchText = string.Empty;
        private Vector2 scrollPosition = Vector2.zero;
        private GroupingDimension currentDimension;
        private readonly Action<TestSubjectEntry> onSelectedCallback;
        private readonly TestSubjectEntry currentSelected;

        #endregion

        #region 2. WINDOW CONFIG & CONSTRUCTOR

        /// <summary>
        /// Gets the initial size of the test subject picker dialog when it is first displayed.
        /// </summary>
        /// <remarks>
        /// DO NOT DELETE: 0 reference is a false positive.
        /// </remarks>
        public override Vector2 InitialSize => new Vector2(620f, 660f);

        /// <summary>
        /// Initializes a new instance of the <see cref="Dialog_TestSubjectPicker"/> class.
        /// </summary>
        /// <param name="initialDimension">The initial grouping dimension to display.</param>
        /// <param name="currentSelected">The currently selected test subject entry.</param>
        /// <param name="onSelected">The callback to invoke when a test subject is selected.</param>
        public Dialog_TestSubjectPicker(
            GroupingDimension initialDimension, 
            TestSubjectEntry currentSelected, 
            Action<TestSubjectEntry> onSelected)
        {
            this.currentDimension = initialDimension;
            this.currentSelected = currentSelected;
            this.onSelectedCallback = onSelected;

            this.doCloseX = true;
            this.doCloseButton = false;
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = true;
        }

        #endregion

        #region 3. WINDOW DRAWING LIFECYCLE

        /// <summary>
        /// Draws the contents of the test subject picker dialog within the specified rectangle.
        /// </summary>
        /// <param name="inRect">The rectangle within which to draw the dialog contents.</param>
        /// <remarks>
        /// This method is called by the RimWorld UI framework to render the dialog's contents.
        /// It is responsible for drawing the header, search bar, dimension tabs, and the scrollable list of test subjects.
        /// DO NOT DELETE: 0 reference is a false positive.
        /// </remarks>
        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;

            // 1. Header Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width - 36f, HeaderHeight), "OverHaulers_PickerTitle".Translate());
            Text.Font = GameFont.Small;
            y += HeaderHeight + SettingsViewUtilities.ControlGap;

            // 2. Search Box with Clear Button
            Rect searchBarRect = new Rect(inRect.x, y, inRect.width, SearchBarHeight);
            DrawSearchBar(searchBarRect);
            y += SearchBarHeight + SettingsViewUtilities.ControlGap;

            // 3. Slicing Dimension Selector Tabs
            Rect dimBarRect = new Rect(inRect.x, y, inRect.width, DimensionBarHeight);
            y = DrawDimensionTabs(dimBarRect) + SettingsViewUtilities.ControlGap;

            // 4. Eligibility Filter
            Rect filterRect = new Rect(inRect.x, y, inRect.width, SettingsLayout.DefaultToggleHeight);
            string filterTooltip = SettingsViewUtilities.CachedText("OverHaulers_Picker_CaravanCapableOnly_Tooltip");
            string filterLabel = SettingsViewUtilities.LabelWithTooltip(SettingsViewUtilities.CachedText("OverHaulers_Picker_CaravanCapableOnly"), filterTooltip);
            bool previousFilter = session.CaravanCapableOnly;
            Widgets.CheckboxLabeled(filterRect, filterLabel, ref session.CaravanCapableOnly);
            if (!string.IsNullOrEmpty(filterTooltip)) TooltipHandler.TipRegion(filterRect, filterTooltip);
            if (previousFilter != session.CaravanCapableOnly) scrollPosition = Vector2.zero;
            y += SettingsLayout.DefaultToggleHeight + SettingsViewUtilities.ControlGap;

            // 5. Scrollable Grouped Results List
            Rect listRect = new Rect(inRect.x, y, inRect.width, inRect.height - (y - inRect.y));
            DrawSubjectList(listRect);
        }

        /// <summary>
        /// Draws the search bar with a clear button within the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle within which to draw the search bar.</param>
        /// <remarks>
        /// The search bar includes a text input field and a clear button. The clear button resets the search text when clicked.
        /// </remarks>
        private void DrawSearchBar(Rect rect)
        {
            string previous = searchText;
            searchText = SettingsViewUtilities.DrawSearchField(rect, searchText, "OverHaulers_SearchPlaceholder", GameFont.Small);

            if (searchText != previous)
            {
                scrollPosition = Vector2.zero;
            }
        }

        /// <summary>
        /// Draws the dimension tabs for grouping test subjects.
        /// </summary>
        /// <param name="rect">The rectangle area within which the dimension tabs should be drawn.</param>
        private float DrawDimensionTabs(Rect rect)
        {
            bool inGame = Current.ProgramState == ProgramState.Playing && Current.Game != null;
            int tabCount = inGame ? 5 : 4;

            float gap = SettingsViewUtilities.CompactGap;
            float btnWidth = (rect.width - (gap * (tabCount - 1))) / (float)tabCount;
            float x = rect.x;
            float y = rect.y;
            float rowHeight = 0f;
            DrawDimensionButton(rect, ref x, ref y, ref rowHeight, btnWidth, gap, "OverHaulers_GroupMode_Mod", GroupingDimension.Mod);
            DrawDimensionButton(rect, ref x, ref y, ref rowHeight, btnWidth, gap, "OverHaulers_GroupMode_BodyDef", GroupingDimension.BodyDef);
            DrawDimensionButton(rect, ref x, ref y, ref rowHeight, btnWidth, gap, "OverHaulers_GroupMode_Category", GroupingDimension.Category);
            DrawDimensionButton(rect, ref x, ref y, ref rowHeight, btnWidth, gap, "OverHaulers_GroupMode_FleshType", GroupingDimension.FleshType);

            if (inGame)
            {
                DrawDimensionButton(rect, ref x, ref y, ref rowHeight, btnWidth, gap, "OverHaulers_GroupMode_Colony", GroupingDimension.Colony);
            }
            return y + rowHeight;
        }

        /// <summary>
        /// Draws a single dimension button within the specified rectangle.
        /// </summary>
        private void DrawDimensionButton(Rect bounds, ref float x, ref float y, ref float rowHeight,
            float minWidth, float gap, string labelKey, GroupingDimension dimension)
        {
            string label = SettingsViewUtilities.CachedText(labelKey);
            Rect rect = SettingsViewUtilities.PlaceButton(bounds, ref x, ref y, ref rowHeight, label,
                gap, minWidth: minWidth, minHeight: DimensionBarHeight);
            bool isSelected = currentDimension == dimension;
            if (isSelected)
            {
                Widgets.DrawBoxSolid(rect, SettingsViewUtilities.DarkTargetHighlightColor);
            }

            Color origColor = GUI.color;
            if (isSelected) GUI.color = Color.cyan;

            if (SettingsViewUtilities.DrawButton(rect, label))
            {
                currentDimension = dimension;
                TestBench.activeGroupingDimension = dimension;
                scrollPosition = Vector2.zero;
            }

            GUI.color = origColor;
        }

        /// <summary>
        /// Draws the list of test subjects within the specified rectangle, including group headers and individual entries.
        /// </summary>
        /// <param name="rect">The rectangle area within which the subject list should be drawn.</param>
        private void DrawSubjectList(Rect rect)
        {
            List<TestSubjectGroup> groups = TestSubjectRegistry.GetFilteredGroups(currentDimension, searchText, session.CaravanCapableOnly);
            HashSet<string> expandedGroups = session.ExpandedGroups(currentDimension);

            float totalContentHeight = ListTopPadding;
            int totalMatchingSpecies = 0;
            bool isSearching = !string.IsNullOrEmpty(searchText);

            // Calculate dynamic scroll height based on expanded/collapsed states
            for (int g = 0; g < groups.Count; g++)
            {
                TestSubjectGroup group = groups[g];
                totalContentHeight += GroupHeaderHeight + GroupHeaderGap;

                bool isExpanded = isSearching || expandedGroups.Contains(group.GroupLabel);
                if (isExpanded)
                {
                    totalContentHeight += (group.Entries.Count * RowHeight);
                }
                
                totalContentHeight += SettingsViewUtilities.ControlGap;
                totalMatchingSpecies += group.Entries.Count;
            }

            if (totalMatchingSpecies == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(rect, "OverHaulers_NoSearchResults".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - ListScrollbarGutter, Math.Max(rect.height, totalContentHeight + ListBottomPadding));
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);

            float curY = ListTopPadding;

            for (int g = 0; g < groups.Count; g++)
            {
                TestSubjectGroup group = groups[g];
                bool isExpanded = isSearching || expandedGroups.Contains(group.GroupLabel);

                // 1. Group Header (Interactive Accordion)
                Rect groupHeaderRect = new Rect(0f, curY, viewRect.width, GroupHeaderHeight);
                Widgets.DrawBoxSolid(groupHeaderRect, new Color(0.12f, 0.15f, 0.18f, 0.9f));
                Widgets.DrawHighlightIfMouseover(groupHeaderRect);

                if (Widgets.ButtonInvisible(groupHeaderRect))
                {
                    if (expandedGroups.Contains(group.GroupLabel))
                        expandedGroups.Remove(group.GroupLabel);
                    else
                        expandedGroups.Add(group.GroupLabel);
                }

                GUI.color = new Color(1f, 0.8f, 0.3f);
                Text.Font = GameFont.Tiny;
                
                string arrow = isExpanded ? "▼" : "▶";
                string headerLabel = $"{arrow} {group.GroupLabel} ({group.Entries.Count})".ToUpperInvariant();

                Widgets.Label(new Rect(groupHeaderRect.x + 6f, groupHeaderRect.y + 3f, groupHeaderRect.width - 16f, groupHeaderRect.height - 3f), headerLabel);
                
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                curY += GroupHeaderHeight + GroupHeaderGap;

                // 2. Group Rows (Only drawn if expanded)
                if (isExpanded)
                {
                    for (int i = 0; i < group.Entries.Count; i++)
                    {
                        TestSubjectEntry entry = group.Entries[i];
                        Rect rowRect = new Rect(0f, curY, viewRect.width, RowHeight);

                        bool isSelected = currentSelected != null && 
                            (entry.LivePawn != null ? entry.LivePawn == currentSelected.LivePawn : entry.RaceDef == currentSelected.RaceDef);

                        if (isSelected)
                        {
                            Widgets.DrawBoxSolid(rowRect, SettingsViewUtilities.DarkTargetHighlightColor);
                        }

                        Widgets.DrawHighlightIfMouseover(rowRect);

                        // Single continuous full-width label rect
                        Rect fullLabelRect = new Rect(rowRect.x + 8f, rowRect.y + 4f, rowRect.width - 16f, rowRect.height - 4f);

                        string primaryLabel = entry.Label;
                        if (isSelected)
                        {
                            primaryLabel = primaryLabel.Colorize(Color.cyan);
                        }

                        string secondaryTag = GetSecondaryTag(entry);
                        string secondaryFormatted = $"• {secondaryTag}".Colorize(SettingsViewUtilities.DescriptionTextColor);
                        string fullRowText = $"{primaryLabel} {secondaryFormatted}";

                        Text.Font = GameFont.Tiny;
                        Text.WordWrap = false;
                        Widgets.Label(fullLabelRect, fullRowText);
                        Text.WordWrap = true;

                        // Click Trigger
                        if (Widgets.ButtonInvisible(rowRect))
                        {
                            onSelectedCallback?.Invoke(entry);
                            Close();
                            break;
                        }

                        curY += RowHeight;
                    }
                }

                curY += SettingsViewUtilities.ControlGap;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Retrieves the secondary tag text for a test subject entry, which provides additional context based on the current grouping
        ///  dimension.
        /// </summary>
        /// <param name="entry">The test subject entry for which to retrieve the secondary tag.</param>
        /// <returns>A formatted string representing the secondary tag for the entry.</returns>
        private string GetSecondaryTag(TestSubjectEntry entry)
        {
            if (entry.IsLivePawn)
            {
                float safeBodySize = MedicalClassifier.GetSafeBodySize(entry.LivePawn);
                float baseMassCapacity = SpeciesBaselineCalibration.ResolveSpeciesBaseline(entry.LivePawn);
                float currentOffset = PawnDataRegistry.GetOffset(entry.LivePawn, baseMassCapacity);
                float finalMass = Math.Max(MassCapacitySolver.HardCompatibilitySafetyFloorKg , baseMassCapacity + currentOffset);
                return "OverHaulers_TestBench_SubjectLiveTag".Translate(ReportFormatter.FormatAdaptive(safeBodySize, true), ReportFormatter.FormatMass(finalMass, true, false)).ToString();
            }

            string bodyName = entry.BodyDef?.defName ?? "Body";

            switch (currentDimension)
            {
                case GroupingDimension.BodyDef:
                    return $"{entry.ModName} • {entry.CategoryName} • {entry.FleshTypeName}";
                case GroupingDimension.Category:
                    return $"{entry.ModName} • {bodyName} • {entry.FleshTypeName}";
                case GroupingDimension.FleshType:
                    return $"{entry.ModName} • {bodyName} • {entry.CategoryName}";
                case GroupingDimension.Mod:
                default:
                    return $"{bodyName} • {entry.CategoryName} • {entry.FleshTypeName}";
            }
        }

        #endregion
    }
}
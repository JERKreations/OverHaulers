using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TEST-03] Interactive, searchable species and live pawn picker dialog for the Test Bench.
    /// Provides instant text search, dynamic 5-way slicing, and collapsible accordion group headers.
    /// </summary>
    public class Dialog_TestSubjectPicker : Window
    {
        #region 1. FIELDS & CONSTANTS

        private string searchText = string.Empty;
        private Vector2 scrollPosition = Vector2.zero;
        private GroupingDimension currentDimension;
        private readonly Action<TestSubjectEntry> onSelectedCallback;
        private readonly TestSubjectEntry currentSelected;

        // Tracks which group headers are currently expanded. 
        // Empty by default means all are collapsed initially.
        private readonly HashSet<string> expandedGroups = new HashSet<string>();

        private const float HeaderHeight = 32f;
        private const float SearchBarHeight = 28f;
        private const float DimensionBarHeight = 26f;
        private const float RowHeight = 28f;
        private const float GroupHeaderHeight = 24f;

        #endregion

        #region 2. CONSTRUCTOR & WINDOW CONFIG

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

        public override Vector2 InitialSize => new Vector2(620f, 660f);

        #endregion

        #region 3. WINDOW DRAWING LIFECYCLE

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;

            // 1. Header Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width - 36f, HeaderHeight), "OverHaulers_PickerTitle".Translate());
            Text.Font = GameFont.Small;
            y += HeaderHeight + 6f;

            // 2. Search Box with Clear Button
            Rect searchBarRect = new Rect(inRect.x, y, inRect.width, SearchBarHeight);
            DrawSearchBar(searchBarRect);
            y += SearchBarHeight + 8f;

            // 3. Slicing Dimension Selector Tabs
            Rect dimBarRect = new Rect(inRect.x, y, inRect.width, DimensionBarHeight);
            DrawDimensionTabs(dimBarRect);
            y += DimensionBarHeight + 8f;

            // 4. Scrollable Grouped Results List
            Rect listRect = new Rect(inRect.x, y, inRect.width, inRect.height - (y - inRect.y));
            DrawSubjectList(listRect);
        }

        private void DrawSearchBar(Rect rect)
        {
            float clearBtnWidth = 24f;
            Rect inputRect = new Rect(rect.x, rect.y, rect.width - clearBtnWidth - 4f, rect.height);
            Rect clearRect = new Rect(rect.x + rect.width - clearBtnWidth, rect.y, clearBtnWidth, rect.height);

            string previous = searchText;
            searchText = Widgets.TextField(inputRect, searchText);

            if (string.IsNullOrEmpty(searchText))
            {
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(inputRect.x + 6f, inputRect.y + 4f, inputRect.width - 12f, inputRect.height), "OverHaulers_SearchPlaceholder".Translate());
                    GUI.color = Color.white;
                }
            }

            if (Widgets.ButtonText(clearRect, "✕"))
            {
                searchText = string.Empty;
            }

            if (searchText != previous)
            {
                scrollPosition = Vector2.zero;
            }
        }

        private void DrawDimensionTabs(Rect rect)
        {
            bool inGame = Current.ProgramState == ProgramState.Playing && Current.Game != null;
            int tabCount = inGame ? 5 : 4;

            float gap = 4f;
            float btnWidth = (rect.width - (gap * (tabCount - 1))) / (float)tabCount;

            DrawDimensionButton(new Rect(rect.x, rect.y, btnWidth, rect.height), "OverHaulers_GroupMode_Mod".Translate(), GroupingDimension.Mod);
            DrawDimensionButton(new Rect(rect.x + (btnWidth + gap), rect.y, btnWidth, rect.height), "OverHaulers_GroupMode_BodyDef".Translate(), GroupingDimension.BodyDef);
            DrawDimensionButton(new Rect(rect.x + (btnWidth + gap) * 2f, rect.y, btnWidth, rect.height), "OverHaulers_GroupMode_Category".Translate(), GroupingDimension.Category);
            DrawDimensionButton(new Rect(rect.x + (btnWidth + gap) * 3f, rect.y, btnWidth, rect.height), "OverHaulers_GroupMode_FleshType".Translate(), GroupingDimension.FleshType);

            if (inGame)
            {
                DrawDimensionButton(new Rect(rect.x + (btnWidth + gap) * 4f, rect.y, btnWidth, rect.height), "OverHaulers_GroupMode_Colony".Translate(), GroupingDimension.Colony);
            }
        }

        private void DrawDimensionButton(Rect rect, string label, GroupingDimension dimension)
        {
            bool isSelected = currentDimension == dimension;
            if (isSelected)
            {
                Widgets.DrawBoxSolid(rect, SettingsViewUtilities.DarkTargetHighlightColor);
            }

            Color origColor = GUI.color;
            if (isSelected) GUI.color = Color.cyan;

            if (Widgets.ButtonText(rect, label))
            {
                currentDimension = dimension;
                TestBench.activeGroupingDimension = dimension;
                scrollPosition = Vector2.zero;
                expandedGroups.Clear(); // Collapse all when switching tabs
            }

            GUI.color = origColor;
        }

        private void DrawSubjectList(Rect rect)
        {
            List<TestSubjectGroup> groups = TestSubjectRegistry.GetFilteredGroups(currentDimension, searchText);

            float totalContentHeight = 4f; // Synchronized with starting curY
            int totalMatchingSpecies = 0;
            bool isSearching = !string.IsNullOrEmpty(searchText);

            // Calculate dynamic scroll height based on expanded/collapsed states
            for (int g = 0; g < groups.Count; g++)
            {
                TestSubjectGroup group = groups[g];
                totalContentHeight += GroupHeaderHeight + 2f; // Header is always visible

                bool isExpanded = isSearching || expandedGroups.Contains(group.GroupLabel);
                if (isExpanded)
                {
                    totalContentHeight += (group.Entries.Count * RowHeight);
                }
                
                totalContentHeight += 6f; // Synchronized with curY += 6f (gap to next group)
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

            // Add 16px of comfortable bottom padding so the last item never touches the window edge
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, totalContentHeight + 16f));
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);

            float curY = 4f;

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
                curY += GroupHeaderHeight + 2f;

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

                curY += 6f; // Gap to next group
            }

            Widgets.EndScrollView();
        }

        private string GetSecondaryTag(TestSubjectEntry entry)
        {
            if (entry.IsLivePawn)
            {
                float safeBodySize = MedicalClassifier.GetSafeBodySize(entry.LivePawn);
                float baseMassCapacity = ModpackBaselineCalibration.ResolveArchetypeCalibratedBaseline(entry.LivePawn);
                float currentOffset = PawnDataRegistry.GetOffset(entry.LivePawn, baseMassCapacity);
                float finalMass = Mathf.Max(0.01f, baseMassCapacity + currentOffset);
                return $"BodySize {safeBodySize:F1}x • {finalMass.ToStringMass()}";
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
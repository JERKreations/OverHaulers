using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. TEST BENCH CHECKS

        /// <summary>Checks button overrides and row placement with synthetic geometry fixtures.</summary>
        private static void CheckButtonLayout(SelfCheckContext ctx)
        {
            Vector2 limited = SettingsViewUtilities.ButtonSize("fixture", 100f,
                minWidth: 90f, maxWidth: 80f, fixedWidth: 150f, fixedHeight: 26f);
            Expect.Equal(80f, limited.x, "button width must respect the available maximum");
            Expect.Equal(26f, limited.y, "fixed button height was not retained");
            Vector2 capped = SettingsViewUtilities.ButtonSize("fixture", 100f,
                fixedWidth: 50f, fixedHeight: 40f, maxHeight: 30f);
            Expect.True(capped.x == 50f && capped.y == 30f, "maximum button height must cap a fixed height");

            Rect bounds = new Rect(0f, 0f, 100f, 100f);
            float x = bounds.x;
            float y = bounds.y;
            float rowHeight = 0f;
            Rect first = SettingsViewUtilities.PlaceButton(bounds, ref x, ref y, ref rowHeight,
                "first", 5f, fixedWidth: 60f, fixedHeight: 25f);
            Rect second = SettingsViewUtilities.PlaceButton(bounds, ref x, ref y, ref rowHeight,
                "second", 5f, fixedWidth: 30f, fixedHeight: 35f);
            Rect third = SettingsViewUtilities.PlaceButton(bounds, ref x, ref y, ref rowHeight,
                "third", 5f, fixedWidth: 40f, fixedHeight: 20f);
            Expect.True(first.x == 0f && first.y == 0f && second.x == 65f && second.y == 0f,
                "buttons that fit should remain on the same row");
            Expect.True(third.x == 0f && third.y == 40f && rowHeight == 20f,
                "wrapped button must start after the previous row's tallest control");
        }

        /// <summary>Checks picker session preferences and independent group expansion by dimension.</summary>
        private static void CheckSubjectPickerSession(SelfCheckContext ctx)
        {
            TestSubjectPickerSession session = new TestSubjectPickerSession();
            Expect.True(session.CaravanCapableOnly, "picker filter must default to on");
            HashSet<string> sourceGroups = session.ExpandedGroups(GroupingDimension.Mod);
            HashSet<string> anatomyGroups = session.ExpandedGroups(GroupingDimension.BodyDef);
            sourceGroups.Add("shared");
            Expect.True(!anatomyGroups.Contains("shared"), "group expansion leaked between dimensions");
            anatomyGroups.Add("shared");
            sourceGroups.Remove("shared");
            session.CaravanCapableOnly = false;
            Expect.True(!session.CaravanCapableOnly, "picker filter state was not retained");
            Expect.True(ReferenceEquals(anatomyGroups, session.ExpandedGroups(GroupingDimension.BodyDef)),
                "picker did not retain the dimension expansion set");
            Expect.True(session.ExpandedGroups(GroupingDimension.BodyDef).Contains("shared"),
                "switching dimensions lost group expansion");
            Expect.True(!session.ExpandedGroups(GroupingDimension.Mod).Contains("shared"),
                "collapsing a group was not retained");
            TestSubjectPickerSession fresh = new TestSubjectPickerSession();
            Expect.True(fresh.CaravanCapableOnly && fresh.ExpandedGroups(GroupingDimension.BodyDef).Count == 0,
                "new picker session must begin with defaults");
        }

        /// <summary>Checks each archetype grouping against the production caravan eligibility predicate.</summary>
        private static void CheckCaravanSubjectFilter(SelfCheckContext ctx)
        {
            if (TestSubjectRegistry.AllSubjects.Count == 0)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoSubject".Translate().ToString());
                return;
            }

            GroupingDimension[] dimensions =
            {
                GroupingDimension.Mod, GroupingDimension.BodyDef,
                GroupingDimension.Category, GroupingDimension.FleshType
            };

            for (int d = 0; d < dimensions.Length; d++)
            {
                GroupingDimension dimension = dimensions[d];
                List<TestSubjectGroup> all = TestSubjectRegistry.GetFilteredGroups(dimension, null);
                List<TestSubjectGroup> filtered = TestSubjectRegistry.GetFilteredGroups(dimension, null, true);
                int groupIndex = 0;

                for (int g = 0; g < all.Count; g++)
                {
                    TestSubjectGroup source = all[g];
                    int entryIndex = 0;
                    TestSubjectGroup actual = groupIndex < filtered.Count ? filtered[groupIndex] : null;
                    for (int i = 0; i < source.Entries.Count; i++)
                    {
                        TestSubjectEntry entry = source.Entries[i];
                        if (!PawnDataRegistry.IsSpeciesCaravanCapable(entry.RaceDef)) continue;
                        Expect.True(actual != null && actual.GroupLabel == source.GroupLabel,
                            dimension + " missing eligible group " + source.GroupLabel);
                        Expect.True(entryIndex < actual.Entries.Count && ReferenceEquals(entry, actual.Entries[entryIndex]),
                            dimension + " missing or reordered eligible subject " + entry.Label);
                        entryIndex++;
                    }
                    if (entryIndex > 0)
                    {
                        Expect.Equal(entryIndex, actual.Entries.Count, dimension + " included an ineligible subject");
                        groupIndex++;
                    }
                }

                Expect.Equal(groupIndex, filtered.Count, dimension + " included an unexpected group");
                Expect.True(ReferenceEquals(filtered, TestSubjectRegistry.GetFilteredGroups(dimension, null, true)),
                    dimension + " did not reuse the filtered index");
                List<TestSubjectGroup> noMatches = TestSubjectRegistry.GetFilteredGroups(dimension, "\u0001", true);
                Expect.Equal(0, noMatches.Count, dimension + " search included a nonmatching subject");
                if (filtered.Count > 0 && !string.IsNullOrWhiteSpace(filtered[0].GroupLabel))
                {
                    TestSubjectGroup expected = filtered[0];
                    List<TestSubjectGroup> search = TestSubjectRegistry.GetFilteredGroups(dimension, expected.GroupLabel, true);
                    bool found = false;
                    for (int g = 0; g < search.Count; g++)
                    {
                        if (search[g].GroupLabel != expected.GroupLabel) continue;
                        Expect.Equal(expected.Entries.Count, search[g].Entries.Count, dimension + " group search dropped eligible subjects");
                        for (int i = 0; i < expected.Entries.Count; i++)
                        {
                            Expect.True(ReferenceEquals(expected.Entries[i], search[g].Entries[i]), dimension + " group search reordered subjects");
                        }
                        found = true;
                        break;
                    }
                    Expect.True(found, dimension + " group search omitted matching group");
                }
                Expect.True(ReferenceEquals(all, TestSubjectRegistry.GetFilteredGroups(dimension, null)),
                    dimension + " filter changed the unfiltered index");
            }
        }

        #endregion
    }
}

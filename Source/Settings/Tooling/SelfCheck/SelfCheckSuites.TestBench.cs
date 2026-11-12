using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. TEST BENCH CHECKS

        /// <summary>Checks the opt-in gradient and label rendering with synthetic, distinct colour fixtures.</summary>
        private static void CheckProstheticPalette(SelfCheckContext ctx)
        {
            Settings settings = OverHaulers.settings;
            bool originalEnabled = settings != null && settings.prostheticColorOverride;
            bool originalCustom = settings != null && settings.customProstheticPalette;
            Color originalCritical = settings == null ? PresentationDefaults.ColorCriticalDefault
                : originalCustom ? settings.prostheticColorCritical : settings.colorCritical;
            Color originalHealthy = settings == null ? PresentationDefaults.ColorHealthyDefault
                : originalCustom ? settings.prostheticColorHealthy : settings.colorHealthy;
            Color originalBoosted = settings == null ? PresentationDefaults.ColorBoostedDefault
                : originalCustom ? settings.prostheticColorBoosted : settings.colorBoosted;

            Color low = new Color(0.1f, 0.2f, 0.3f);
            Color neutral = new Color(0.4f, 0.5f, 0.6f);
            Color high = new Color(0.7f, 0.8f, 0.9f);
            Color source = new Color(0.2f, 0.8f, 0.4f);

            try
            {
                ReportFormatter.SetProstheticPalette(false, low, neutral, high);
                Expect.True(ReportFormatter.GetProstheticColor(source, 0.75f).Equals(source),
                    "disabled override must preserve the prosthetic source colour");
                Expect.True(ReportFormatter.GetImplantColor(source).Equals(source),
                    "disabled override must preserve the implant source colour");

                ReportFormatter.SetProstheticPalette(true, low, neutral, high);
                Expect.True(ReportFormatter.GetProstheticColor(source, 0f).Equals(low) &&
                    ReportFormatter.GetProstheticColor(source, 0.5f).Equals(low), "low endpoint must clamp at 50%");
                Expect.True(ReportFormatter.GetProstheticColor(source, 0.75f).Equals(Color.Lerp(low, neutral, 0.5f)),
                    "75% must interpolate halfway to neutral");
                Expect.True(ReportFormatter.GetProstheticColor(source, 1f).Equals(neutral), "100% must use neutral");
                Expect.True(ReportFormatter.GetProstheticColor(source, 1.25f).Equals(Color.Lerp(neutral, high, 0.5f)),
                    "125% must interpolate halfway to high");
                Expect.True(ReportFormatter.GetProstheticColor(source, 1.5f).Equals(high) &&
                    ReportFormatter.GetProstheticColor(source, 2f).Equals(high), "high endpoint must clamp at 150%");
                Expect.True(ReportFormatter.GetImplantColor(source).Equals(neutral),
                    "separate implants must use the neutral colour");

                MassCapacityModel model = new MassCapacityModel();
                for (int i = 0; i < model.CapacityLevels.Length; i++) model.CapacityLevels[i] = 1f;
                PartViewNode group = new PartViewNode { Label = "fixture-group", HealthFraction = 1f, EfficiencyRating = 1f };
                group.AddSubPart(new PartViewNode
                {
                    Label = "fixture-arm", HealthFraction = 1f, EfficiencyRating = 0.75f,
                    HasProsthetic = true, ProstheticName = "fixture-prosthetic", ProstheticColor = source,
                    AthleticImplantName = "fixture-implant", AthleticImplantColor = source
                });
                model.EvaluatedParts.Add(group);

                string text = ReportFormatter.BuildExplanation(model);
                Expect.True(text.IndexOf("fixture-prosthetic".Colorize(Color.Lerp(low, neutral, 0.5f)), StringComparison.Ordinal) >= 0,
                    "prosthetic label must use the gradient");
                Expect.True(text.IndexOf("fixture-implant".Colorize(neutral), StringComparison.Ordinal) >= 0,
                    "implant label must use neutral");
            }
            finally
            {
                ReportFormatter.SetProstheticPalette(originalEnabled, originalCritical, originalHealthy, originalBoosted);
            }
        }

        /// <summary>Checks compact breakdown visibility using synthetic neutral prosthetic nodes.</summary>
        private static void CheckNeutralProstheticBreakdown(SelfCheckContext ctx)
        {
            Settings settings = OverHaulers.settings;
            Color healthy = settings != null ? settings.colorHealthy : PresentationDefaults.ColorHealthyDefault;
            Color critical = settings != null ? settings.colorCritical : PresentationDefaults.ColorCriticalDefault;
            Color boosted = settings != null ? settings.colorBoosted : PresentationDefaults.ColorBoostedDefault;
            bool originalVerbose = settings != null ? settings.verboseBreakdown : PresentationDefaults.VerboseBreakdown;

            try
            {
                ReportFormatter.SetPalette(healthy, critical, boosted, false);

                MassCapacityModel model = new MassCapacityModel();
                for (int i = 0; i < model.CapacityLevels.Length; i++) model.CapacityLevels[i] = 1f;

                PartViewNode group = new PartViewNode { Label = "fixture-group", HealthFraction = 1f, EfficiencyRating = 1f };
                PartViewNode arm = new PartViewNode
                {
                    Record = new BodyPartRecord(), Label = "fixture-arm", HealthFraction = 1f, EfficiencyRating = 1f,
                    HasProsthetic = true, ProstheticName = "fixture-arm-prosthetic"
                };
                PartViewNode inherited = new PartViewNode
                {
                    Record = new BodyPartRecord(), Label = "fixture-inherited", HealthFraction = 1f, EfficiencyRating = 1f,
                    HasProsthetic = true, ProstheticName = arm.ProstheticName, IsInherited = true
                };
                arm.AddSubPart(inherited);
                group.AddSubPart(arm);
                group.AddSubPart(new PartViewNode { Label = "fixture-natural", HealthFraction = 1f, EfficiencyRating = 1f });
                model.EvaluatedParts.Add(group);
                model.EvaluatedParts.Add(new PartViewNode
                {
                    Record = new BodyPartRecord(), Label = "fixture-organ", HealthFraction = 1f, EfficiencyRating = 1f,
                    HasProsthetic = true, ProstheticName = "fixture-organ-prosthetic"
                });

                string text = ReportFormatter.BuildExplanation(model);
                Expect.True(text.IndexOf("fixture-group", StringComparison.Ordinal) >= 0 &&
                    text.IndexOf("fixture-arm-prosthetic", StringComparison.Ordinal) >= 0,
                    "neutral prosthetic must appear in its structural group");
                Expect.True(text.IndexOf("fixture-organ", StringComparison.Ordinal) >= 0 &&
                    text.IndexOf("fixture-organ-prosthetic", StringComparison.Ordinal) >= 0,
                    "standalone neutral prosthetic must show its name");
                Expect.True(text.IndexOf("fixture-inherited", StringComparison.Ordinal) < 0,
                    "inherited prosthetic must not be duplicated");
                Expect.True(text.IndexOf("fixture-natural", StringComparison.Ordinal) < 0,
                    "unmodified natural parts must remain hidden");
                Expect.Equal(0f, model.Offset, "displaying neutral prosthetics must not alter the model offset");
            }
            finally
            {
                ReportFormatter.SetPalette(healthy, critical, boosted, originalVerbose);
            }
        }

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

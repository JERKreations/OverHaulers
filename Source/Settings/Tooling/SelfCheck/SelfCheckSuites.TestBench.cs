using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. TEST BENCH CHECKS

        private static HediffDef visibilityActiveImplant;
        private static HediffDef visibilityInactiveImplant;

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
                    IsStructural = true, HasProsthetic = true, ProstheticName = "fixture-prosthetic", ProstheticColor = source,
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
                    IsStructural = true, HasProsthetic = true, ProstheticName = "fixture-arm-prosthetic",
                    AthleticImplantName = "fixture-arm-prosthetic"
                };
                PartViewNode inherited = new PartViewNode
                {
                    Record = new BodyPartRecord(), Label = "fixture-inherited", HealthFraction = 1f, EfficiencyRating = 1f,
                    IsStructural = true, HasProsthetic = true, ProstheticName = arm.ProstheticName, IsInherited = true
                };
                arm.AddSubPart(inherited);
                group.AddSubPart(arm);
                group.AddSubPart(new PartViewNode { Label = "fixture-natural", HealthFraction = 1f, EfficiencyRating = 1f });
                model.EvaluatedParts.Add(group);
                model.EvaluatedParts.Add(new PartViewNode
                {
                    Record = new BodyPartRecord(), Label = "fixture-organ", HealthFraction = 1f, EfficiencyRating = 1f,
                    IsOrgan = true, IsCapacitySource = true, HasProsthetic = true, ProstheticName = "fixture-organ-prosthetic"
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

                arm.EfficiencyRating = 1.5f;
                inherited.EfficiencyRating = 1.5f;
                text = ReportFormatter.BuildExplanation(model);
                Expect.True(text.IndexOf("fixture-inherited", StringComparison.Ordinal) < 0,
                    "inherited prosthetics must remain hidden at non-neutral efficiency");

                ReportFormatter.SetPalette(healthy, critical, boosted, true);
                text = ReportFormatter.BuildExplanation(model);
                Expect.True(text.IndexOf("fixture-inherited", StringComparison.Ordinal) >= 0,
                    "verbose output must retain inherited parts");
            }
            finally
            {
                ReportFormatter.SetPalette(healthy, critical, boosted, originalVerbose);
            }
        }

        /// <summary>Checks synthetic compact and verbose relevance fixtures, including implants sharing a part.</summary>
        private static void CheckPartVisibility(SelfCheckContext ctx)
        {
            Settings settings = OverHaulers.settings;
            Color healthy = settings != null ? settings.colorHealthy : PresentationDefaults.ColorHealthyDefault;
            Color critical = settings != null ? settings.colorCritical : PresentationDefaults.ColorCriticalDefault;
            Color boosted = settings != null ? settings.colorBoosted : PresentationDefaults.ColorBoostedDefault;
            bool originalVerbose = settings != null ? settings.verboseBreakdown : PresentationDefaults.VerboseBreakdown;

            if (visibilityActiveImplant == null)
            {
                visibilityActiveImplant = new HediffDef
                {
                    defName = "FixtureActiveImplant",
                    label = "fixture active implant",
                    stages = new List<HediffStage>
                    {
                        new HediffStage
                        {
                            capMods = new List<PawnCapacityModifier>
                            {
                                new PawnCapacityModifier { capacity = PawnCapacityDefOf.Moving, offset = 0.1f }
                            }
                        }
                    }
                };
                visibilityInactiveImplant = new HediffDef { defName = "FixtureInactiveImplant", label = "fixture inactive implant" };
            }
            HediffDef active = visibilityActiveImplant;
            HediffDef inactive = visibilityInactiveImplant;
            PartViewNode arm = new PartViewNode
            {
                Record = new BodyPartRecord(), Label = "fixture arm", IsStructural = true,
                HealthFraction = 1f, EfficiencyRating = 1f, HasProsthetic = true, ProstheticName = "fixture arm replacement"
            };
            PartViewNode eye = new PartViewNode
            {
                Record = new BodyPartRecord(), Label = "fixture eye", IsOrgan = true,
                HealthFraction = 1f, EfficiencyRating = 1f, HasProsthetic = true, ProstheticName = "fixture eye replacement"
            };
            PartViewNode heart = new PartViewNode
            {
                Record = new BodyPartRecord(), Label = "fixture heart", IsOrgan = true, IsCapacitySource = true,
                HealthFraction = 1f, EfficiencyRating = 1f, HasProsthetic = true, ProstheticName = "fixture heart replacement"
            };
            eye.AddImplant(new PartImplantCold { Def = active, AffectsAthletics = true });
            eye.AddImplant(new PartImplantCold { Def = inactive, AffectsAthletics = false });

            MassCapacityModel model = new MassCapacityModel();
            for (int i = 0; i < model.CapacityLevels.Length; i++) model.CapacityLevels[i] = 1f;
            PartViewNode group = new PartViewNode { Label = "fixture group", HealthFraction = 1f, EfficiencyRating = 1f };
            group.AddSubPart(arm);
            group.AddSubPart(eye);
            group.AddSubPart(heart);
            group.AddSubPart(new PartViewNode
            {
                Label = "fixture natural", IsStructural = true, HealthFraction = 1f, EfficiencyRating = 1f, TotalOffset = 1f
            });
            PartViewNode inherited = new PartViewNode
            {
                Record = new BodyPartRecord(), Label = "fixture inherited", IsStructural = true,
                HealthFraction = 1f, EfficiencyRating = 1f, HasProsthetic = true, IsInherited = true,
                ProstheticName = arm.ProstheticName
            };
            inherited.AddImplant(new PartImplantCold { Def = active, AffectsAthletics = true });
            arm.AddSubPart(inherited);
            PartViewNode separatelyInstalled = new PartViewNode
            {
                Record = new BodyPartRecord(), Label = "fixture separately installed", IsStructural = true,
                HealthFraction = 1f, EfficiencyRating = 1f, HasProsthetic = true,
                ProstheticName = arm.ProstheticName
            };
            model.EvaluatedParts.Add(group);

            try
            {
                ReportFormatter.SetPalette(healthy, critical, boosted, false);
                string compact = ReportFormatter.BuildExplanation(model);
                Expect.True(compact.Contains("fixture arm replacement") && compact.Contains("fixture heart replacement"),
                    "neutral structural and capacity-source replacements must appear");
                Expect.True(compact.Contains(active.LabelCap.ToString()) && !compact.Contains(inactive.LabelCap.ToString()),
                    "only the relevant implant on a shared part must appear");
                Expect.True(!compact.Contains("fixture eye replacement") && !compact.Contains("fixture natural"),
                    "irrelevant replacements and natural parts must remain hidden");
                int firstReplacement = compact.IndexOf("fixture arm replacement", StringComparison.Ordinal);
                Expect.True(compact.Contains("fixture inherited") &&
                    compact.IndexOf("fixture arm replacement", firstReplacement + 1, StringComparison.Ordinal) < 0,
                    "relevant implants on inherited parts must not duplicate the parent replacement");
                arm.AddSubPart(separatelyInstalled);
                compact = ReportFormatter.BuildExplanation(model);
                Expect.True(compact.Contains("fixture separately installed") &&
                    compact.IndexOf("fixture arm replacement", firstReplacement + 1, StringComparison.Ordinal) >= 0,
                    "separately installed replacements must not be suppressed as inherited");

                Hediff activeHediff = new Hediff_Implant { def = active };
                Hediff inactiveHediff = new Hediff_Implant { def = inactive };
                Expect.True(HyperlinkUtility.IsRelevantHediff(eye, activeHediff) &&
                    !HyperlinkUtility.IsRelevantHediff(eye, inactiveHediff),
                    "implant links must be filtered individually");
                Expect.True(HyperlinkUtility.IsRelevantHediff(arm, new Hediff_AddedPart { def = inactive }) &&
                    !HyperlinkUtility.IsRelevantHediff(eye, new Hediff_AddedPart { def = inactive }),
                    "neutral structural replacements must link but unrelated organ replacements must not");

                ReportFormatter.SetPalette(healthy, critical, boosted, true);
                string verbose = ReportFormatter.BuildExplanation(model);
                Expect.True(verbose.Contains("<i>- fixture natural") && verbose.Contains("fixture eye replacement") &&
                    verbose.Contains(inactive.LabelCap.ToString()), "verbose output must retain and mute irrelevant details");
                Expect.True(verbose.Contains(active.LabelCap.ToString()) && verbose.Contains("fixture arm replacement"),
                    "verbose output must retain relevant details");
            }
            finally
            {
                ReportFormatter.SetPalette(healthy, critical, boosted, originalVerbose);
            }
        }

        /// <summary>Checks projected human head relevance against the real body topology.</summary>
        private static void CheckHeadPartVisibility(SelfCheckContext ctx)
        {
            TestSubjectEntry subject = TestSubjectRegistry.GetDefaultSubject();
            if (subject == null || subject.BodyDef == null || subject.BodyDef != BodyDefOf.Human)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoHuman".Translate().ToString());
                return;
            }

            SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(subject.BodyDef);
            BodyPartRecord tongue = null;
            List<BodyPartRecord> parts = subject.BodyDef.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].def.defName == "Tongue")
                {
                    tongue = parts[i];
                    break;
                }
            }

            Expect.True(template != null, "human topology must be available");
            if (tongue == null || template.GetPartType(tongue) != PartType.HeadPart ||
                MedicalClassifier.IsCapacitySourcePart(tongue.def))
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoUnrelatedTongue".Translate().ToString());
                return;
            }
            int index = template.GetPartIndex(tongue);
            Expect.True(index >= 0, "human tongue must be indexed in the topology");

            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: false))
            {
                MassCapacityModel model = new MassCapacityModel();
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
                ViewProjection.InspectAndProjectForPawn(harness.SandboxPawn, baseline, model);
                PartViewNode node = model.GetNodeForPartIndex(index);
                Expect.True(node != null && !node.IsStructural && !node.IsCapacitySource,
                    "the projected tongue must not become relevant solely from its head category");

                node.HasProsthetic = true;
                node.ProstheticAffectsAthletics = false;
                Expect.True(!node.IsRelevantAddedPart, "an unrelated tongue replacement must be hidden");
                node.ProstheticAffectsAthletics = true;
                Expect.True(node.IsRelevantAddedPart, "a head replacement affecting a modeled capacity must remain relevant");
                node.ProstheticAffectsAthletics = false;
                node.IsCapacitySource = true;
                Expect.True(node.IsRelevantAddedPart, "a head capacity-source replacement must remain relevant");
            }
        }

        /// <summary>Checks bulk replacement against real recipes and changing sandbox anatomy.</summary>
        private static void CheckBulkReplacementAncestry(SelfCheckContext ctx)
        {
            TestSubjectEntry subject = TestSubjectRegistry.GetDefaultSubject();
            if (subject?.BodyDef == null)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoSubject".Translate().ToString());
                return;
            }

            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: false))
            {
                Pawn pawn = harness.SandboxPawn;
                List<BodyPartRecord> parts = subject.BodyDef.AllParts;
                BodyPartRecord parent = null;
                BodyPartRecord child = null;
                BodyPartRecord independent = null;
                RecipeDef childRecipe = null;
                bool checkedOperationIndexReadOnly = false;
                RecipeDef vgeElephantLeg = DefDatabase<RecipeDef>.GetNamedSilentFail("GR_InstallThrumboLeg");

                if (vgeElephantLeg != null && subject.RaceDef?.race != null &&
                    subject.RaceDef.race.Humanlike)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        IReadOnlyList<MedicalOperationEntry> pawnReplacements =
                            MedicalCatalog.GetReplacementsFor(pawn, parts[i]);
                        if (pawnReplacements == null) continue;
                        for (int j = 0; j < pawnReplacements.Count; j++)
                        {
                            Expect.True(
                                pawnReplacements[j].Recipe != vgeElephantLeg,
                                "animal-owned VGE leg replacement must not enter a human operation list");
                        }
                    }
                }

                for (int i = 0; i < parts.Count && parent == null; i++)
                {
                    BodyPartRecord candidate = parts[i];
                    IReadOnlyList<MedicalOperationEntry> replacements =
                        MedicalCatalog.GetReplacementsFor(pawn, candidate);
                    if (replacements == null) continue;
                    if (!checkedOperationIndexReadOnly)
                    {
                        ICollection<MedicalOperationEntry> operationCollection =
                            replacements as ICollection<MedicalOperationEntry>;
                        Expect.True(operationCollection != null && operationCollection.IsReadOnly,
                            "operation indexes must not expose mutable catalog collections");
                        checkedOperationIndexReadOnly = true;
                    }

                    bool available = false;
                    for (int j = 0; j < replacements.Count; j++)
                    {
                        if (ToolingDialog.CanInstallReplacement(pawn, candidate, replacements[j].Recipe))
                        {
                            available = true;
                            break;
                        }
                    }
                    if (!available) continue;

                    for (int j = 0; j < parts.Count && parent == null; j++)
                    {
                        BodyPartRecord descendant = parts[j];
                        BodyPartRecord ancestor = descendant.parent;
                        while (ancestor != null && ancestor != candidate) ancestor = ancestor.parent;
                        if (ancestor == null) continue;

                        IReadOnlyList<MedicalOperationEntry> childReplacements =
                            MedicalCatalog.GetReplacementsFor(pawn, descendant);
                        if (childReplacements == null) continue;
                        for (int k = 0; k < childReplacements.Count; k++)
                        {
                            if (!ToolingDialog.CanInstallReplacement(pawn, descendant, childReplacements[k].Recipe)) continue;
                            parent = candidate;
                            child = descendant;
                            childRecipe = childReplacements[k].Recipe;
                            break;
                        }
                    }
                }

                if (parent == null)
                {
                    ctx.Skip("OverHaulers_SelfCheck_Skip_NoNestedReplacements".Translate().ToString());
                    return;
                }

                for (int i = 0; i < parts.Count; i++)
                {
                    BodyPartRecord candidate = parts[i];
                    if (candidate == parent || candidate.def != parent.def) continue;
                    BodyPartRecord ancestor = candidate.parent;
                    while (ancestor != null && ancestor != parent) ancestor = ancestor.parent;
                    if (ancestor != null) continue;
                    ancestor = parent.parent;
                    while (ancestor != null && ancestor != candidate) ancestor = ancestor.parent;
                    if (ancestor != null) continue;

                    IReadOnlyList<MedicalOperationEntry> replacements =
                        MedicalCatalog.GetReplacementsFor(pawn, candidate);
                    for (int j = 0; j < replacements.Count; j++)
                    {
                        if (!ToolingDialog.CanInstallReplacement(pawn, candidate, replacements[j].Recipe)) continue;
                        independent = candidate;
                        break;
                    }
                    if (independent != null) break;
                }

                List<BodyPartRecord> targets = new List<BodyPartRecord> { parent, child };
                if (independent != null) targets.Add(independent);
                Expect.True(SandboxPawnHarness.CanApplyConditionToPart(pawn, parent),
                    "a present natural part must remain eligible for a condition");
                ToolingDialog.ApplyBulkReplacementTier(targets, harness, pickHighest: true);
                Expect.True(harness.ReplacementDeltas.ContainsKey(parent), "bulk replacement must install the parent");
                Expect.True(!SandboxPawnHarness.CanApplyConditionToPart(pawn, parent) &&
                    !SandboxPawnHarness.CanApplyConditionToPart(pawn, child),
                    "a replacement and its covered descendants must be ineligible for part conditions");
                Expect.True(!ToolingDialog.CanInstallReplacement(pawn, child, childRecipe),
                    "a replaced ancestor must disable descendant replacements");
                Expect.True(!harness.ReplacementDeltas.ContainsKey(child),
                    "bulk replacement must not install a descendant beneath the replaced parent");
                if (independent != null)
                {
                    Expect.True(harness.ReplacementDeltas.ContainsKey(independent),
                        "bulk replacement must still install an eligible independent branch");
                }

                harness.ResetToSource();
                harness.SimulatePartRemoval(parent);
                Expect.True(pawn.health.hediffSet.PartIsMissing(parent), "the target must be missing for the replacement check");
                IReadOnlyList<MedicalOperationEntry> missingReplacements =
                    MedicalCatalog.GetReplacementsFor(pawn, parent);
                for (int i = 0; i < missingReplacements.Count; i++)
                {
                    RecipeDef recipe = missingReplacements[i].Recipe;
                    if (!recipe.Worker.AvailableOnNow(pawn, parent)) continue;
                    Expect.True(ToolingDialog.CanInstallReplacement(pawn, parent, recipe),
                        "an eligible missing target must remain replaceable");
                    break;
                }
            }
        }

        /// <summary>Checks independent part conditions and preservation of an existing condition.</summary>
        private static void CheckPartConditions(SelfCheckContext ctx)
        {
            TestSubjectEntry subject = TestSubjectRegistry.GetDefaultSubject();
            HediffDef asthma = DefDatabase<HediffDef>.GetNamedSilentFail("Asthma");
            if (subject?.BodyDef == null || asthma == null)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoConditionFixture".Translate().ToString());
                return;
            }

            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: false))
            {
                Pawn pawn = harness.SandboxPawn;
                BodyPartRecord first = null;
                BodyPartRecord second = null;
                List<BodyPartRecord> parts = subject.BodyDef.AllParts;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].def.defName != "Lung" || !SandboxPawnHarness.CanApplyConditionToPart(pawn, parts[i]))
                        continue;
                    if (first == null) first = parts[i];
                    else { second = parts[i]; break; }
                }

                if (second == null)
                {
                    ctx.Skip("OverHaulers_SelfCheck_Skip_NoConditionFixture".Translate().ToString());
                    return;
                }

                Expect.True(MedicalCatalog.TryGetConditionEntry(asthma, out MedicalConditionEntry asthmaEntry),
                    "the catalog must include part-specific asthma");
                ICollection<MedicalConditionTarget> asthmaTargets =
                    asthmaEntry.Targets as ICollection<MedicalConditionTarget>;
                Expect.True(asthmaTargets != null && asthmaTargets.IsReadOnly,
                    "condition target metadata must not expose its builder collection");
                IReadOnlyList<MedicalConditionEntry> localizedEntries =
                    MedicalCatalog.GetConditionEntries(MedicalConditionCategory.LocalizedMedical);
                ICollection<MedicalConditionEntry> localizedCollection =
                    localizedEntries as ICollection<MedicalConditionEntry>;
                Expect.True(localizedCollection != null && localizedCollection.IsReadOnly,
                    "condition category indexes must not expose mutable catalog collections");
                List<BodyPartRecord> eligible = ToolingDialog.GetConditionTargetParts(pawn, asthmaEntry);
                Expect.True(eligible.Contains(first) && eligible.Contains(second),
                    "both natural lungs must be offered as asthma targets");
                HediffDef organDecay = DefDatabase<HediffDef>.GetNamedSilentFail("OrganDecay");
                if (organDecay != null)
                {
                    bool hasOrganDecayEntry = MedicalCatalog.TryGetConditionEntry(organDecay, out MedicalConditionEntry organDecayEntry);
                    Expect.True(hasOrganDecayEntry, "the catalog must include impactful organ decay");
                    if (hasOrganDecayEntry && organDecay.onlyLifeThreateningTo != null)
                    {
                        for (int p = 0; p < organDecay.onlyLifeThreateningTo.Count; p++)
                        {
                            BodyPartDef lifeThreateningPart = organDecay.onlyLifeThreateningTo[p];
                            for (int t = 0; t < organDecayEntry.Targets.Count; t++)
                            {
                                MedicalConditionTarget target = organDecayEntry.Targets[t];
                                Expect.True(
                                    target.Source != MedicalConditionSource.ConditionDefinition ||
                                    target.Kind != MedicalConditionTargetKind.SpecificPart ||
                                    target.Part != lifeThreateningPart,
                                    "onlyLifeThreateningTo must not be interpreted as an application target");
                            }
                        }
                    }
                }
                HediffDef gutWorms = DefDatabase<HediffDef>.GetNamedSilentFail("GutWorms");
                if (gutWorms != null)
                {
                    bool hasGutWormsEntry = MedicalCatalog.TryGetConditionEntry(gutWorms, out MedicalConditionEntry gutWormsEntry);
                    Expect.True(hasGutWormsEntry, "the catalog must include impactful gut worms");
                    if (hasGutWormsEntry)
                    {
                        bool hasStomachTarget = false;
                        bool hasAnyLivePartTarget = false;
                        for (int t = 0; t < gutWormsEntry.Targets.Count; t++)
                        {
                            MedicalConditionTarget target = gutWormsEntry.Targets[t];
                            hasStomachTarget |= target.Kind == MedicalConditionTargetKind.SpecificPart &&
                                target.Part != null && target.Part.defName == "Stomach";
                            hasAnyLivePartTarget |= target.Kind == MedicalConditionTargetKind.AnyLivePart;
                        }
                        Expect.True(
                            hasStomachTarget && !hasAnyLivePartTarget,
                            "gut worms must use their incident's stomach target rather than all live parts");
                        Expect.True(
                            (gutWormsEntry.Categories & MedicalConditionCategory.LocalizedMedical) != 0 &&
                            (gutWormsEntry.Categories & MedicalConditionCategory.SystemicMedical) == 0,
                            "gut worms must be categorized by their localized target");

                        List<BodyPartRecord> gutWormTargets = ToolingDialog.GetConditionTargetParts(pawn, gutWormsEntry);
                        Expect.True(
                            gutWormTargets.Count == 1 && gutWormTargets[0].def.defName == "Stomach",
                            "the human sandbox must offer only the stomach for gut worms");
                    }
                }
                ThingDef otherRace = DefDatabase<ThingDef>.GetNamedSilentFail("Cow");
                if (otherRace != null && otherRace != pawn.def)
                {
                    MedicalConditionTarget otherRaceTarget = new MedicalConditionTarget(
                        MedicalConditionTargetKind.SpecificPart, first.def,
                        MedicalConditionSource.SpeciesHediffGiver, otherRace, null, null);
                    MedicalConditionEntry otherRaceEntry = new MedicalConditionEntry(
                        asthma, asthma.LabelCap.ToString(), MedicalConditionCategory.LocalizedMedical,
                        MedicalConditionSource.SpeciesHediffGiver, false, true,
                        new List<MedicalConditionTarget> { otherRaceTarget });
                    Expect.Equal(0, ToolingDialog.GetConditionTargetParts(pawn, otherRaceEntry).Count,
                        "another species' giver must not authorize condition targets");
                    Expect.True(!ToolingDialog.ConditionTargetMatchesPawn(pawn, otherRaceTarget),
                        "species-specific target provenance must match the subject");
                }
                HediffDef cataract = DefDatabase<HediffDef>.GetNamedSilentFail("Cataract");
                if (cataract != null && MedicalCatalog.TryGetConditionEntry(cataract, out MedicalConditionEntry cataractEntry))
                {
                    Expect.Equal(0, ToolingDialog.GetConditionTargetParts(pawn, cataractEntry).Count,
                        "eye-only efficiency effects must not be offered as modeled capacity conditions");
                }
                HediffDef penoxycyline = DefDatabase<HediffDef>.GetNamedSilentFail("PenoxycylineHigh");
                if (penoxycyline != null)
                    Expect.True(!MedicalCatalog.TryGetConditionEntry(penoxycyline, out _),
                        "an ingestible with no relevant effect must not enter the condition catalog");
                HediffDef psychicAnesthesia = DefDatabase<HediffDef>.GetNamedSilentFail("PsychicAnesthesia");
                if (psychicAnesthesia != null)
                {
                    Expect.True(!MedicalCatalog.TryGetConditionEntry(psychicAnesthesia, out _) &&
                        !MedicalCatalog.TryGetMedicalConditionEntry(psychicAnesthesia, out _),
                        "psychic anesthesia must be excluded from both condition catalogs");
                }

                Hediff original = HediffMaker.MakeHediff(asthma, pawn, first);
                original.Severity = 0.3f;
                pawn.health.AddHediff(original, first);
                harness.SimulateCondition(asthma, first);
                Expect.True(harness.SimulatedConditions[0].Hediff == original,
                    "an existing condition on the target must be reused");
                Expect.True(SandboxPawnHarness.TryGetConditionSeverityRange(harness.SimulatedConditions[0], out float min, out float max) &&
                    min <= 0.1f && max >= 0.45f, "asthma must expose its defined severity range");
                harness.SetConditionSeverity(harness.SimulatedConditions[0], 0.45f);
                Expect.True(original.Severity == 0.45f && original.CurStage == asthma.stages[2],
                    "severity changes must select the actual asthma stage");
                harness.RemoveSimulatedCondition(harness.SimulatedConditions[0]);
                Expect.True(pawn.health.hediffSet.hediffs.Contains(original) && original.Severity == 0.3f,
                    "removing the simulation must restore the existing condition");

                HediffDef flu = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
                if (flu != null && flu.stages != null && flu.stages.Count > 1 && flu.maxSeverity == float.MaxValue)
                {
                    Expect.True(MedicalCatalog.TryGetConditionEntry(flu, out MedicalConditionEntry fluEntry),
                        "the catalog must include whole-body flu");
                    Expect.True(
                        (fluEntry.Categories & MedicalConditionCategory.SystemicMedical) != 0 &&
                        (fluEntry.Categories & MedicalConditionCategory.LocalizedMedical) == 0,
                        "flu must be categorized by its whole-body target");
                    bool wholeBody = false;
                    for (int i = 0; i < fluEntry.Targets.Count; i++)
                    {
                        if (fluEntry.Targets[i].Kind == MedicalConditionTargetKind.WholeBody) wholeBody = true;
                    }
                    Expect.True(wholeBody, "flu must have a verified whole-body target");
                    harness.SimulateCondition(flu, null);
                    SimulatedCondition simulatedFlu = harness.SimulatedConditions[0];
                    Expect.True(!SandboxPawnHarness.TryGetConditionSeverityRange(simulatedFlu, out _, out _),
                        "an unbounded condition must not invent slider bounds");
                    Expect.True(SandboxPawnHarness.TryGetConditionStageSeverity(simulatedFlu, 1, out float fluSeverity),
                        "an unbounded condition must support its defined stages");
                    harness.SetConditionSeverity(simulatedFlu, fluSeverity);
                    Expect.True(simulatedFlu.Hediff.CurStage == flu.stages[1],
                        "selecting a defined stage must update the active condition");
                    harness.RemoveSimulatedCondition(simulatedFlu);
                }

                HediffDef bloodLoss = DefDatabase<HediffDef>.GetNamedSilentFail("BloodLoss");
                if (bloodLoss != null && bloodLoss.lethalSeverity > 0f)
                {
                    harness.SimulateCondition(bloodLoss, null);
                    SimulatedCondition simulatedBloodLoss = harness.SimulatedConditions[0];
                    harness.SetConditionSeverity(simulatedBloodLoss, bloodLoss.lethalSeverity);
                    Expect.True(pawn.health.ShouldBeDead() && !pawn.Dead,
                        "the immediate death predicate must identify lethal sandbox severity without killing the shell");
                    harness.RemoveSimulatedCondition(simulatedBloodLoss);
                }

                harness.SimulateCondition(asthma, first);
                harness.SimulateCondition(asthma, second);
                Expect.Equal(2, harness.SimulatedConditions.Count, "each target must have its own simulation");
                harness.SimulatePartRemoval(first);
                Expect.Equal(1, harness.SimulatedConditions.Count, "removing a part must discard its simulation");
                Expect.True(harness.SimulatedConditions[0].Hediff.Part == second &&
                    !SandboxPawnHarness.CanApplyConditionToPart(pawn, first),
                    "an independent condition must survive and the missing part must be ineligible");
                eligible = ToolingDialog.GetConditionTargetParts(pawn, asthmaEntry);
                Expect.True(!eligible.Contains(first) && eligible.Contains(second),
                    "a missing lung must not be offered as an asthma target");
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

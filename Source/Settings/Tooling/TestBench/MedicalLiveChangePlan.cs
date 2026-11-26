using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [TEST-07] Compiles sandbox intents into a read-only, revalidatable live physical-outcome plan.
    /// It never schedules bills, consumes ingredients, or mutates a live pawn.
    /// Resides under Source/Settings/Tooling/TestBench/.
    /// </summary>
    public static class MedicalLiveChangePlanCompiler
    {
        #region 1. [TEST-07] AUTHORIZATION & CANONICAL ENTRY

        /// <summary>Returns true only while the user can explicitly access god-mode live actions.</summary>
        public static bool IsLiveApplicationAuthorized()
        {
            return Prefs.DevMode &&
                DebugSettings.godMode &&
                Current.ProgramState == ProgramState.Playing;
        }

        /// <summary>
        /// Compiles the harness's current intents against its bound live pawn.
        /// </summary>
        public static MedicalLiveChangePlan Build(SandboxPawnHarness harness)
        {
            if (harness == null) throw new ArgumentNullException(nameof(harness));

            Pawn pawn = harness.SourcePawn;
            List<MedicalLiveChange> changes = new List<MedicalLiveChange>();
            if (pawn == null || pawn.Destroyed || harness.BoundSubject == null)
            {
                return new MedicalLiveChangePlan(
                    pawn,
                    harness.BoundSubject,
                    harness.LiveIntentGeneration,
                    changes);
            }

            // BREAKPOINT ANCHOR: Live Physical Outcome Resolution
            AddReplacementChanges(harness, pawn, changes);
            AddImplantChanges(harness, pawn, changes);
            AddDamageChanges(harness, pawn, changes);
            AddPartStateChanges(harness, pawn, changes);
            AddSimulationOnlyConditionChanges(harness, changes);

            return new MedicalLiveChangePlan(
                pawn,
                harness.BoundSubject,
                harness.LiveIntentGeneration,
                changes);
        }

        /// <summary>Revalidates one intent against the pawn's current state immediately before execution.</summary>
        internal static MedicalLiveChange Revalidate(Pawn pawn, MedicalLiveChangeIntent intent)
        {
            if (pawn == null || pawn.Destroyed || intent == null)
            {
                return new MedicalLiveChange(
                    intent,
                    MedicalLiveActionPolicy.Unsupported,
                    MedicalLiveChangeStatus.Stale,
                    null,
                    "OverHaulers_LiveChange_Reason_StalePawn");
            }

            if (intent.Kind == MedicalLiveChangeKind.InstallReplacement ||
                intent.Kind == MedicalLiveChangeKind.InstallImplant ||
                intent.Kind == MedicalLiveChangeKind.RemoveBodyPart ||
                intent.Kind == MedicalLiveChangeKind.RestoreNaturalPart)
            {
                return ResolveRecipeChange(pawn, intent, MedicalLiveActionPolicy.InstantPhysicalOutcome);
            }

            if (intent.Kind == MedicalLiveChangeKind.ApplyDamage)
            {
                bool ready = IsPawnPart(pawn, intent.Part) &&
                    !pawn.health.hediffSet.PartIsMissing(intent.Part) &&
                    intent.Damage != null &&
                    intent.Amount > 0f;
                return new MedicalLiveChange(
                    intent,
                    ready
                        ? MedicalLiveActionPolicy.InstantPhysicalOutcome
                        : MedicalLiveActionPolicy.Unsupported,
                    ready ? MedicalLiveChangeStatus.Ready : MedicalLiveChangeStatus.Stale,
                    ready ? intent.Part : null,
                    ready ? null : "OverHaulers_LiveChange_Reason_StaleTarget");
            }

            return new MedicalLiveChange(
                intent,
                MedicalLiveActionPolicy.SimulationOnly,
                MedicalLiveChangeStatus.SimulationOnly,
                null,
                "OverHaulers_LiveChange_Reason_SimulationOnly");
        }

        #endregion

        #region 2. [TEST-07] INTENT COMPILATION

        private static void AddReplacementChanges(
            SandboxPawnHarness harness,
            Pawn pawn,
            List<MedicalLiveChange> changes)
        {
            foreach (KeyValuePair<BodyPartRecord, SimulatedModification> pair in harness.ReplacementDeltas)
            {
                SimulatedModification modification = pair.Value;
                MedicalLiveChangeIntent intent = new MedicalLiveChangeIntent(
                    MedicalLiveChangeKind.InstallReplacement,
                    pair.Key,
                    modification.SourceRecipe,
                    modification.TargetHediff,
                    null,
                    modification.Efficiency,
                    modification.DisplayLabel);
                changes.Add(ResolveRecipeChange(pawn, intent, MedicalLiveActionPolicy.InstantPhysicalOutcome));
            }
        }

        private static void AddImplantChanges(
            SandboxPawnHarness harness,
            Pawn pawn,
            List<MedicalLiveChange> changes)
        {
            foreach (KeyValuePair<BodyPartRecord, List<SimulatedModification>> pair in harness.ImplantDeltas)
            {
                List<SimulatedModification> modifications = pair.Value;
                if (modifications == null) continue;

                for (int i = 0; i < modifications.Count; i++)
                {
                    SimulatedModification modification = modifications[i];
                    MedicalLiveChangeIntent intent = new MedicalLiveChangeIntent(
                        MedicalLiveChangeKind.InstallImplant,
                        pair.Key,
                        modification.SourceRecipe,
                        modification.TargetHediff,
                        null,
                        modification.Efficiency,
                        modification.DisplayLabel);
                    changes.Add(ResolveRecipeChange(pawn, intent, MedicalLiveActionPolicy.InstantPhysicalOutcome));
                }
            }
        }

        private static void AddDamageChanges(
            SandboxPawnHarness harness,
            Pawn pawn,
            List<MedicalLiveChange> changes)
        {
            foreach (KeyValuePair<BodyPartRecord, List<SimulatedDamage>> pair in harness.DamageDeltas)
            {
                List<SimulatedDamage> damageEvents = pair.Value;
                if (damageEvents == null) continue;

                for (int i = 0; i < damageEvents.Count; i++)
                {
                    SimulatedDamage damage = damageEvents[i];
                    MedicalLiveChangeIntent intent = new MedicalLiveChangeIntent(
                        MedicalLiveChangeKind.ApplyDamage,
                        pair.Key,
                        null,
                        null,
                        damage.Damage,
                        damage.AppliedDamage,
                        damage.Damage?.LabelCap.ToString());

                    bool hasPart = IsPawnPart(pawn, pair.Key);
                    MedicalLiveChangeStatus status = hasPart && !pawn.health.hediffSet.PartIsMissing(pair.Key)
                        ? MedicalLiveChangeStatus.Ready
                        : MedicalLiveChangeStatus.Stale;
                    changes.Add(new MedicalLiveChange(
                        intent,
                        MedicalLiveActionPolicy.InstantPhysicalOutcome,
                        status,
                        status == MedicalLiveChangeStatus.Ready ? pair.Key : null,
                        status == MedicalLiveChangeStatus.Ready
                            ? null
                            : "OverHaulers_LiveChange_Reason_StaleTarget"));
                }
            }
        }

        private static void AddPartStateChanges(
            SandboxPawnHarness harness,
            Pawn pawn,
            List<MedicalLiveChange> changes)
        {
            foreach (KeyValuePair<BodyPartRecord, SimulatedPartStateChange> pair in harness.PartStateDeltas)
            {
                SimulatedPartStateChange state = pair.Value;
                MedicalLiveChangeIntent intent = new MedicalLiveChangeIntent(
                    state.IsRemoval
                        ? MedicalLiveChangeKind.RemoveBodyPart
                        : MedicalLiveChangeKind.RestoreNaturalPart,
                    pair.Key,
                    state.Recipe,
                    null,
                    null,
                    state.IsRemoval ? 1f : 0f,
                    state.DisplayLabel);
                changes.Add(ResolveRecipeChange(pawn, intent, MedicalLiveActionPolicy.InstantPhysicalOutcome));
            }
        }

        private static void AddSimulationOnlyConditionChanges(
            SandboxPawnHarness harness,
            List<MedicalLiveChange> changes)
        {
            IReadOnlyList<SimulatedCondition> conditions = harness.SimulatedConditions;
            for (int i = 0; i < conditions.Count; i++)
            {
                Hediff hediff = conditions[i].Hediff;
                MedicalLiveChangeIntent intent = new MedicalLiveChangeIntent(
                    MedicalLiveChangeKind.ApplyCondition,
                    hediff.Part,
                    null,
                    hediff.def,
                    null,
                    hediff.Severity,
                    MedicalCatalog.GetFormattedConditionLabel(hediff));
                changes.Add(new MedicalLiveChange(
                    intent,
                    MedicalLiveActionPolicy.SimulationOnly,
                    MedicalLiveChangeStatus.SimulationOnly,
                    null,
                    "OverHaulers_LiveChange_Reason_SimulationOnly"));
            }
        }

        #endregion

        #region 3. [TEST-07] NATIVE RECIPE RESOLUTION

        private static MedicalLiveChange ResolveRecipeChange(
            Pawn pawn,
            MedicalLiveChangeIntent intent,
            MedicalLiveActionPolicy proposedPolicy)
        {
            RecipeDef recipe = intent.Recipe;
            if (recipe == null ||
                !MedicalCatalog.IsRecipeAvailableToPawn(pawn, recipe) ||
                !SupportsInstantOutcome(recipe, intent.Kind))
            {
                return new MedicalLiveChange(
                    intent,
                    MedicalLiveActionPolicy.Unsupported,
                    MedicalLiveChangeStatus.Unsupported,
                    null,
                    "OverHaulers_LiveChange_Reason_UnsupportedWorker");
            }
            if (intent.Part == null || !IsPawnPart(pawn, intent.Part))
            {
                return new MedicalLiveChange(
                    intent,
                    proposedPolicy,
                    MedicalLiveChangeStatus.Stale,
                    null,
                    "OverHaulers_LiveChange_Reason_StaleTarget");
            }

            try
            {
                if (!recipe.AvailableNow)
                {
                    return new MedicalLiveChange(
                        intent,
                        proposedPolicy,
                        MedicalLiveChangeStatus.Invalid,
                        null,
                        "OverHaulers_LiveChange_Reason_NotAvailable");
                }
                if (intent.Kind == MedicalLiveChangeKind.RemoveBodyPart &&
                    pawn.health.hediffSet.PartIsMissing(intent.Part))
                {
                    return new MedicalLiveChange(
                        intent,
                        proposedPolicy,
                        MedicalLiveChangeStatus.Invalid,
                        null,
                        "OverHaulers_LiveChange_Reason_NotAvailable");
                }
                if (intent.Kind == MedicalLiveChangeKind.RestoreNaturalPart &&
                    !pawn.health.hediffSet.PartIsMissing(intent.Part) &&
                    !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(intent.Part))
                {
                    return new MedicalLiveChange(
                        intent,
                        proposedPolicy,
                        MedicalLiveChangeStatus.Invalid,
                        null,
                        "OverHaulers_LiveChange_Reason_NotAvailable");
                }

                AcceptanceReport report = recipe.Worker.AvailableReport(pawn, intent.Part);
                bool matchesWorkerTarget = recipe.Worker.GetPartsToApplyOn(pawn, recipe).Contains(intent.Part);
                if (!matchesWorkerTarget || !recipe.AvailableOnNow(pawn, intent.Part) || !report.Accepted)
                {
                    return new MedicalLiveChange(
                        intent,
                        proposedPolicy,
                        MedicalLiveChangeStatus.Invalid,
                        null,
                        report.Accepted
                            ? "OverHaulers_LiveChange_Reason_NotAvailable"
                            : "OverHaulers_LiveChange_Reason_WorkerRejected");
                }
            }
            catch (Exception ex)
            {
                OHLog.Warn(
                    "TestBench.MedicalLiveChangePlan:ResolveRecipe",
                    ex,
                    "Could not resolve native recipe target '" + recipe.defName + "'.");
                return new MedicalLiveChange(
                    intent,
                    proposedPolicy,
                    MedicalLiveChangeStatus.Unsupported,
                    null,
                    "OverHaulers_LiveChange_Reason_WorkerFailed");
            }

            return new MedicalLiveChange(
                intent,
                proposedPolicy,
                MedicalLiveChangeStatus.Ready,
                intent.Part,
                null);
        }

        private static bool SupportsInstantOutcome(RecipeDef recipe, MedicalLiveChangeKind kind)
        {
            RecipeWorker worker = recipe?.Worker;
            if (worker == null) return false;

            if (kind == MedicalLiveChangeKind.InstallReplacement)
                return worker is Recipe_InstallArtificialBodyPart;
            if (kind == MedicalLiveChangeKind.InstallImplant)
                return worker is Recipe_InstallImplant;
            if (kind == MedicalLiveChangeKind.RemoveBodyPart)
                return worker is Recipe_RemoveBodyPart;
            if (kind == MedicalLiveChangeKind.RestoreNaturalPart)
                return worker is Recipe_InstallNaturalBodyPart;
            return false;
        }

        private static bool IsPawnPart(Pawn pawn, BodyPartRecord part)
        {
            List<BodyPartRecord> parts = pawn?.RaceProps?.body?.AllParts;
            return parts != null && part != null && parts.Contains(part);
        }

        #endregion
    }

    internal static class MedicalLiveChangeEnumerableExtensions
    {
        public static bool Contains<T>(this IEnumerable<T> values, T expected)
            where T : class
        {
            foreach (T value in values)
            {
                if (ReferenceEquals(value, expected)) return true;
            }
            return false;
        }
    }
}

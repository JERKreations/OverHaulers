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
        /// <param name="harness">The sandbox pawn harness containing the current intents and bound live pawn.</param>
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
            List<BodyPartRecord> pendingRestorations = GetPendingRestorations(harness);
            AddPartStateChanges(harness, pawn, changes);
            AddReplacementChanges(harness, pawn, changes);
            AddImplantChanges(harness, pawn, changes, pendingRestorations);
            AddDamageChanges(harness, pawn, changes, pendingRestorations);
            AddSimulationOnlyConditionChanges(harness, changes);

            return new MedicalLiveChangePlan(
                pawn,
                harness.BoundSubject,
                harness.LiveIntentGeneration,
                changes);
        }

        /// <summary>
        /// Revalidates one intent against the pawn's current state immediately before execution.
        /// </summary>
        /// <param name="pawn">The live pawn against which the intent is revalidated.</param>
        /// <param name="intent">The medical live change intent to revalidate.</param>
        /// <returns>The revalidated medical live change.</returns>
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

        /// <summary>
        /// Compiles all medical live change intents for the given pawn and sandbox harness into a list of resolved changes.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness containing the current intents and bound live pawn.</param>
        /// <param name="pawn">The live pawn for whom the changes are being compiled.</param>
        /// <param name="changes">The list to which resolved medical live changes will be added.</param>
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

        /// <summary>
        /// Compiles all medical live change intents for the given pawn and sandbox harness into a list of resolved changes.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness containing the current intents and bound live pawn.</param>
        /// <param name="pawn">The live pawn for whom the changes are being compiled.</param>
        /// <param name="changes">The list to which resolved medical live changes will be added.</param>
        /// <param name="pendingRestorations">The list of body parts pending restoration, used to determine coverage for implants.</param>
        private static void AddImplantChanges(
            SandboxPawnHarness harness,
            Pawn pawn,
            List<MedicalLiveChange> changes,
            List<BodyPartRecord> pendingRestorations)
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
                    changes.Add(ResolveRecipeChange(
                        pawn,
                        intent,
                        MedicalLiveActionPolicy.InstantPhysicalOutcome,
                        IsPartOrDescendantCoveredByRestoration(pair.Key, pendingRestorations)));
                }
            }
        }

        /// <summary>
        /// Adds medical live changes for damage events to the list of resolved changes.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness containing the current intents and bound live pawn.</param>
        /// <param name="pawn">The live pawn for whom the changes are being compiled.</param>
        /// <param name="changes">The list to which resolved medical live changes will be added.</param>
        /// <param name="pendingRestorations">The list of body parts pending restoration, used to determine coverage for implants.</param>
        private static void AddDamageChanges(
            SandboxPawnHarness harness,
            Pawn pawn,
            List<MedicalLiveChange> changes,
            List<BodyPartRecord> pendingRestorations)
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
                    bool pendingRestoration = IsPartOrDescendantCoveredByRestoration(pair.Key, pendingRestorations);
                    MedicalLiveChangeStatus status = hasPart &&
                        (!pawn.health.hediffSet.PartIsMissing(pair.Key) || pendingRestoration)
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

        /// <summary>
        /// Retrieves the list of body parts pending restoration from the sandbox harness.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness containing the current intents and bound live pawn.</param>
        /// <returns>A list of body parts pending restoration.</returns>
        private static List<BodyPartRecord> GetPendingRestorations(SandboxPawnHarness harness)
        {
            var result = new List<BodyPartRecord>();
            foreach (KeyValuePair<BodyPartRecord, SimulatedPartStateChange> pair in harness.PartStateDeltas)
            {
                if (!pair.Value.IsRemoval) result.Add(pair.Key);
            }
            return result;
        }

        /// <summary>
        /// Determines whether the specified body part or any of its descendants are covered by pending restorations.
        /// </summary>
        /// <param name="part">The body part to check.</param>
        /// <param name="pendingRestorations">The list of body parts pending restoration.</param>
        /// <returns>True if the part or any descendant is covered by restoration; otherwise, false.</returns>
        private static bool IsPartOrDescendantCoveredByRestoration(
            BodyPartRecord part,
            List<BodyPartRecord> pendingRestorations)
        {
            for (int i = 0; i < pendingRestorations.Count; i++)
            {
                BodyPartRecord cursor = part;
                while (cursor != null)
                {
                    if (cursor == pendingRestorations[i]) return true;
                    cursor = cursor.parent;
                }
            }
            return false;
        }

        /// <summary>
        /// Adds medical live changes for part state changes (restorations and removals) to the list of resolved changes.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness containing the current intents and bound live pawn.</param>
        /// <param name="pawn">The live pawn for whom the changes are being compiled.</param>
        /// <param name="changes">The list to which resolved medical live changes will be added.</param>
        private static void AddPartStateChanges(
            SandboxPawnHarness harness,
            Pawn pawn,
            List<MedicalLiveChange> changes)
        {
            var restorations = new List<SimulatedPartStateChange>();
            var removals = new List<SimulatedPartStateChange>();
            foreach (KeyValuePair<BodyPartRecord, SimulatedPartStateChange> pair in harness.PartStateDeltas)
            {
                SimulatedPartStateChange state = pair.Value;
                (state.IsRemoval ? removals : restorations).Add(state);
            }

            restorations.Sort((a, b) => GetPartDepth(a.Part).CompareTo(GetPartDepth(b.Part)));
            for (int i = 0; i < restorations.Count + removals.Count; i++)
            {
                SimulatedPartStateChange state = i < restorations.Count
                    ? restorations[i]
                    : removals[i - restorations.Count];
                MedicalLiveChangeIntent intent = new MedicalLiveChangeIntent(
                    state.IsRemoval
                        ? MedicalLiveChangeKind.RemoveBodyPart
                        : MedicalLiveChangeKind.RestoreNaturalPart,
                    state.Part,
                    state.Recipe,
                    null,
                    null,
                    state.IsRemoval ? 1f : 0f,
                    state.DisplayLabel);
                changes.Add(ResolveRecipeChange(pawn, intent, MedicalLiveActionPolicy.InstantPhysicalOutcome));
            }
        }

        /// <summary>
        /// Adds medical live changes for damage events to the list of resolved changes.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness containing the current intents and bound live pawn.</param>
        /// <param name="pawn">The live pawn for whom the changes are being compiled.</param>
        /// <param name="changes">The list to which resolved medical live changes will be added.</param>
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

        /// <summary>
        /// Resolves a medical live change for a recipe, taking into account the pawn's current state and the proposed action policy.
        /// </summary>
        /// <param name="pawn">The live pawn for whom the change is being resolved.</param>
        /// <param name="intent">The medical live change intent.</param>
        /// <param name="proposedPolicy">The proposed action policy for the change.</param>
        /// <param name="allowPendingRestoration">Indicates whether pending restorations should be considered for implants.</param>
        /// <returns>A resolved medical live change.</returns>
        private static MedicalLiveChange ResolveRecipeChange(
            Pawn pawn,
            MedicalLiveChangeIntent intent,
            MedicalLiveActionPolicy proposedPolicy,
            bool allowPendingRestoration = false)
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
                if (allowPendingRestoration && intent.Kind == MedicalLiveChangeKind.InstallImplant)
                {
                    return new MedicalLiveChange(
                        intent,
                        proposedPolicy,
                        MedicalLiveChangeStatus.Ready,
                        intent.Part,
                        null);
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

        /// <summary>
        /// Determines whether a given recipe supports instant physical outcomes for the specified kind of medical live change.
        /// </summary>
        /// <param name="recipe">The recipe to check.</param>
        /// <param name="kind">The kind of medical live change.</param>
        /// <returns>True if the recipe supports instant outcomes; otherwise, false.</returns>
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

        /// <summary>
        /// Determines whether the specified body part belongs to the given pawn.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <param name="part">The body part to verify.</param>
        /// <returns>True if the body part belongs to the pawn; otherwise, false.</returns>
        private static bool IsPawnPart(Pawn pawn, BodyPartRecord part)
        {
            List<BodyPartRecord> parts = pawn?.RaceProps?.body?.AllParts;
            return parts != null && part != null && parts.Contains(part);
        }

        /// <summary>
        /// Gets the depth of the specified body part within the body hierarchy.
        /// </summary>
        /// <param name="part">The body part whose depth is to be calculated.</param>
        /// <returns>The depth of the body part, with 0 indicating a top-level part.</returns>
        private static int GetPartDepth(BodyPartRecord part)
        {
            int depth = 0;
            for (BodyPartRecord cursor = part?.parent; cursor != null; cursor = cursor.parent) depth++;
            return depth;
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for working with collections of medical live changes.
    /// </summary>
    internal static class MedicalLiveChangeEnumerableExtensions
    {
        /// <summary>
        /// Determines whether the specified collection contains the expected value by reference.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="values">The collection to search.</param>
        /// <param name="expected">The value to look for.</param>
        /// <returns>True if the collection contains the expected value by reference; otherwise, false.</returns>
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

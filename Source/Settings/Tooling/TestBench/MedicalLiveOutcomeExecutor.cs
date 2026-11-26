using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>[TEST-07] Outcome summary returned after a confirmed live-action batch.</summary>
    public sealed class MedicalLiveExecutionResult
    {
        public readonly int AppliedCount;
        public readonly ReadOnlyCollection<MedicalLiveChange> AppliedChanges;
        public readonly ReadOnlyCollection<MedicalLiveChange> FailedChanges;

        public MedicalLiveExecutionResult(
            int appliedCount,
            List<MedicalLiveChange> appliedChanges,
            List<MedicalLiveChange> failedChanges)
        {
            AppliedCount = appliedCount;
            AppliedChanges = new ReadOnlyCollection<MedicalLiveChange>(appliedChanges);
            FailedChanges = new ReadOnlyCollection<MedicalLiveChange>(failedChanges);
        }
    }

    /// <summary>
    /// [TEST-07] Executes only vetted, reviewed physical outcomes on a live pawn.
    /// It bypasses medical bills and never invokes arbitrary recipe workers.
    /// Resides under Source/Settings/Tooling/TestBench/.
    /// </summary>
    public static class MedicalLiveOutcomeExecutor
    {
        #region 1. [TEST-07] CANONICAL BATCH EXECUTION

        /// <summary>
        /// Revalidates authorization, intent generation, and every ready row before applying the physical outcome.
        /// </summary>
        public static MedicalLiveExecutionResult Execute(
            SandboxPawnHarness harness,
            MedicalLiveChangePlan reviewedPlan)
        {
            if (!MedicalLiveChangePlanCompiler.IsLiveApplicationAuthorized())
                throw new InvalidOperationException("Developer mode and god mode are required for live application.");
            if (harness == null) throw new ArgumentNullException(nameof(harness));
            if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
            if (!ReferenceEquals(reviewedPlan.Pawn, harness.SourcePawn) ||
                reviewedPlan.IntentGeneration != harness.LiveIntentGeneration)
                throw new InvalidOperationException("The Test Bench plan is stale and must be reviewed again.");
            if (reviewedPlan.Pawn == null || reviewedPlan.Pawn.Destroyed)
                throw new InvalidOperationException("The reviewed pawn is no longer available.");
            if (!UnityData.IsInMainThread)
                throw new InvalidOperationException("Live medical outcomes must execute on the main thread.");

            List<MedicalLiveChange> applied = new List<MedicalLiveChange>(reviewedPlan.ReadyCount);
            List<MedicalLiveChange> failed = new List<MedicalLiveChange>();
            for (int i = 0; i < reviewedPlan.Changes.Count; i++)
            {
                MedicalLiveChange reviewed = reviewedPlan.Changes[i];
                if (reviewed.Status != MedicalLiveChangeStatus.Ready) continue;

                MedicalLiveChange current = MedicalLiveChangePlanCompiler.Revalidate(
                    reviewedPlan.Pawn,
                    reviewed.Intent);
                if (current.Status != MedicalLiveChangeStatus.Ready)
                {
                    failed.Add(current);
                    continue;
                }

                try
                {
                    ApplyOne(reviewedPlan.Pawn, current);
                    applied.Add(current);
                }
                catch (Exception ex)
                {
                    OHLog.Warn(
                        "TestBench.MedicalLiveOutcomeExecutor:ApplyOne",
                        ex,
                        "Could not apply reviewed live outcome '" + (current.Intent?.Label ?? "?") + "'.");
                    failed.Add(new MedicalLiveChange(
                        current.Intent,
                        current.Policy,
                        MedicalLiveChangeStatus.Invalid,
                        current.ResolvedPart,
                        "OverHaulers_LiveChange_Reason_ExecutionFailed"));
                }
            }

            if (applied.Count > 0)
            {
                PawnDataRegistry.Invalidate(reviewedPlan.Pawn.thingIDNumber);
                harness.BindSubject(reviewedPlan.Subject);
            }

            return new MedicalLiveExecutionResult(applied.Count, applied, failed);
        }

        #endregion

        #region 2. [TEST-07] VETTED OUTCOME ADAPTERS

        private static void ApplyOne(Pawn pawn, MedicalLiveChange change)
        {
            switch (change.Intent.Kind)
            {
                case MedicalLiveChangeKind.InstallReplacement:
                    ApplyReplacement(pawn, change);
                    break;
                case MedicalLiveChangeKind.InstallImplant:
                    ApplyImplant(pawn, change);
                    break;
                case MedicalLiveChangeKind.ApplyDamage:
                    ApplyDamage(pawn, change);
                    break;
                case MedicalLiveChangeKind.RemoveBodyPart:
                    ApplyPartRemoval(pawn, change);
                    break;
                case MedicalLiveChangeKind.RestoreNaturalPart:
                    ApplyNaturalRestoration(pawn, change);
                    break;
                default:
                    throw new InvalidOperationException("The reviewed intent has no supported physical outcome adapter.");
            }
        }

        private static void ApplyReplacement(Pawn pawn, MedicalLiveChange change)
        {
            RecipeDef recipe = change.Intent.Recipe;
            BodyPartRecord part = change.ResolvedPart;
            HediffDef hediff = change.Intent.Hediff;
            if (!(recipe?.Worker is Recipe_InstallArtificialBodyPart) || part == null || hediff == null)
                throw new InvalidOperationException("The replacement outcome is not supported.");

            // BREAKPOINT ANCHOR: Native Replacement Branch Outcome
            pawn.health.RestorePart(part, null, checkStateChange: false);
            pawn.health.AddHediff(hediff, part);
        }

        private static void ApplyImplant(Pawn pawn, MedicalLiveChange change)
        {
            RecipeDef recipe = change.Intent.Recipe;
            BodyPartRecord part = change.ResolvedPart;
            HediffDef hediff = change.Intent.Hediff;
            if (!(recipe?.Worker is Recipe_InstallImplant) || part == null || hediff == null)
                throw new InvalidOperationException("The implant outcome is not supported.");

            pawn.health.AddHediff(hediff, part);
        }

        private static void ApplyDamage(Pawn pawn, MedicalLiveChange change)
        {
            DamageDef damage = change.Intent.Damage;
            BodyPartRecord part = change.ResolvedPart;
            float amount = change.Intent.Amount;
            if (damage == null || part == null || amount <= 0f)
                throw new InvalidOperationException("The damage intent is invalid.");

            DamageInfo damageInfo = CreateNativeDamageInfo(damage, amount, part);
            damageInfo.SetAllowDamagePropagation(false);
            pawn.TakeDamage(damageInfo);
        }

        private static void ApplyPartRemoval(Pawn pawn, MedicalLiveChange change)
        {
            BodyPartRecord part = change.ResolvedPart;
            if (!(change.Intent.Recipe?.Worker is Recipe_RemoveBodyPart) || part == null)
                throw new InvalidOperationException("The part-removal outcome is not supported.");

            // BREAKPOINT ANCHOR: Native Part-Removal Physical Outcome
            pawn.health.RestorePart(part, null, checkStateChange: false);
            Hediff missing = HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, part);
            if (missing == null)
                throw new InvalidOperationException("Could not create the live missing-part state.");
            pawn.health.AddHediff(missing, part);
        }

        private static void ApplyNaturalRestoration(Pawn pawn, MedicalLiveChange change)
        {
            BodyPartRecord part = change.ResolvedPart;
            if (!(change.Intent.Recipe?.Worker is Recipe_InstallNaturalBodyPart) || part == null)
                throw new InvalidOperationException("The natural restoration outcome is not supported.");

            // BREAKPOINT ANCHOR: Native Natural-Part Restoration Outcome
            pawn.health.RestorePart(part, null, checkStateChange: false);
        }

        #endregion

        #region 3. [TEST-07] RUNTIME DAMAGE CONSTRUCTION

        private static DamageInfo CreateNativeDamageInfo(
            DamageDef damage,
            float amount,
            BodyPartRecord part)
        {
            ConstructorInfo[] constructors = typeof(DamageInfo).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length < 6 ||
                    parameters[0].ParameterType != typeof(DamageDef) ||
                    parameters[1].ParameterType != typeof(float))
                    continue;

                object[] arguments = new object[parameters.Length];
                for (int p = 0; p < parameters.Length; p++)
                {
                    ParameterInfo parameter = parameters[p];
                    arguments[p] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
                }

                arguments[0] = damage;
                arguments[1] = amount;
                arguments[2] = 0f;
                arguments[3] = 0f;
                arguments[4] = null;
                arguments[5] = part;
                return (DamageInfo)constructors[i].Invoke(arguments);
            }

            throw new MissingMethodException("A compatible DamageInfo constructor was not found.");
        }

        #endregion
    }
}

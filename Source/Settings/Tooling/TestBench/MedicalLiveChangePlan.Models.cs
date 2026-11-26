using System;
using System.Collections.ObjectModel;
using Verse;

namespace OverHaulers
{
    #region 1. [TEST-07] LIVE-ACTION POLICY MODELS

    /// <summary>Defines which actions a Test Bench intent may export to a live pawn.</summary>
    public enum MedicalLiveActionPolicy : byte
    {
        SimulationOnly = 0,
        InstantPhysicalOutcome = 1,
        Unsupported = 2
    }

    /// <summary>Identifies the physical outcome requested by a live-action intent.</summary>
    public enum MedicalLiveChangeKind : byte
    {
        InstallReplacement = 0,
        InstallImplant = 1,
        ApplyDamage = 2,
        ApplyCondition = 3,
        RemoveBodyPart = 4,
        RestoreNaturalPart = 5
    }

    /// <summary>Classifies one resolved live change for review.</summary>
    public enum MedicalLiveChangeStatus : byte
    {
        Ready = 0,
        SimulationOnly = 1,
        Stale = 2,
        Invalid = 3,
        Unsupported = 4
    }

    /// <summary>
    /// Immutable Test Bench intent captured independently from the temporary sandbox hediff instance.
    /// </summary>
    public sealed class MedicalLiveChangeIntent
    {
        public readonly MedicalLiveChangeKind Kind;
        public readonly BodyPartRecord Part;
        public readonly RecipeDef Recipe;
        public readonly HediffDef Hediff;
        public readonly DamageDef Damage;
        public readonly float Amount;
        public readonly string Label;

        /// <summary>
        /// Initializes a new instance of the <see cref="MedicalLiveChangeIntent"/> class.
        /// </summary>
        /// <param name="kind">The kind of live change.</param>
        /// <param name="part">The body part affected by the change.</param>
        /// <param name="recipe">The recipe applied, if any.</param>
        /// <param name="hediff">The hediff applied, if any.</param>
        /// <param name="damage">The damage applied, if any.</param>
        /// <param name="amount">The amount of damage, if applicable.</param>
        /// <param name="label">The display label for the change.</param>
        public MedicalLiveChangeIntent(
            MedicalLiveChangeKind kind,
            BodyPartRecord part,
            RecipeDef recipe,
            HediffDef hediff,
            DamageDef damage,
            float amount,
            string label)
        {
            Kind = kind;
            Part = part;
            Recipe = recipe;
            Hediff = hediff;
            Damage = damage;
            Amount = amount;
            Label = label;
        }
    }

    /// <summary>One intent after live-pawn policy, freshness, and native-target validation.</summary>
    public sealed class MedicalLiveChange
    {
        public readonly MedicalLiveChangeIntent Intent;
        public readonly MedicalLiveActionPolicy Policy;
        public readonly MedicalLiveChangeStatus Status;
        public readonly BodyPartRecord ResolvedPart;
        public readonly string Reason;

        /// <summary>
        /// Initializes a new instance of the <see cref="MedicalLiveChange"/> class.
        /// </summary>
        /// <param name="intent">The medical live change intent.</param>
        /// <param name="policy">The action policy applied to the change.</param>
        /// <param name="status">The status of the change.</param>
        /// <param name="resolvedPart">The resolved body part for the change.</param>
        /// <param name="reason">The reason for the change status.</param>
        public MedicalLiveChange(
            MedicalLiveChangeIntent intent,
            MedicalLiveActionPolicy policy,
            MedicalLiveChangeStatus status,
            BodyPartRecord resolvedPart,
            string reason)
        {
            Intent = intent;
            Policy = policy;
            Status = status;
            ResolvedPart = resolvedPart;
            Reason = reason;
        }
    }

    /// <summary>
    /// Immutable review snapshot. It remains valid only for the captured pawn and sandbox intent generation.
    /// </summary>
    public sealed class MedicalLiveChangePlan
    {
        public readonly Pawn Pawn;
        public readonly TestSubjectEntry Subject;
        public readonly int IntentGeneration;
        public readonly ReadOnlyCollection<MedicalLiveChange> Changes;
        public readonly int ReadyCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="MedicalLiveChangePlan"/> class.
        /// </summary>
        /// <param name="pawn">The pawn for whom the change plan is created.</param>
        /// <param name="subject">The test subject entry associated with the pawn.</param>
        /// <param name="intentGeneration">The intent generation number.</param>
        /// <param name="changes">The list of medical live changes.</param>
        public MedicalLiveChangePlan(
            Pawn pawn,
            TestSubjectEntry subject,
            int intentGeneration,
            System.Collections.Generic.List<MedicalLiveChange> changes)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));

            Pawn = pawn;
            Subject = subject;
            IntentGeneration = intentGeneration;
            Changes = new ReadOnlyCollection<MedicalLiveChange>(changes);
            for (int i = 0; i < changes.Count; i++)
            {
                if (changes[i].Status == MedicalLiveChangeStatus.Ready) ReadyCount++;
            }
        }
    }

    #endregion
}

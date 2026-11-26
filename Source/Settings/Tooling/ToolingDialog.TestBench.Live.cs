using System;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    public partial class ToolingDialog
    {
        #region 1. [TEST-07] GOD-MODE LIVE EXPORT

        /// <summary>
        /// Draws the single live-outcome review entry point while developer and god mode are active.
        /// </summary>
        /// <param name="bounds">The bounding rectangle for the control.</param>
        /// <param name="currentY">The current Y position within the bounds.</param>
        /// <param name="width">The width of the control.</param>
        /// <param name="harness">The sandbox pawn harness containing the live changes.</param>
        /// <returns>The updated Y position after drawing the control.</returns>
        private static float DrawLiveApplicationControl(
            Rect bounds,
            float currentY,
            float width,
            SandboxPawnHarness harness)
        {
            if (!MedicalLiveChangePlanCompiler.IsLiveApplicationAuthorized() ||
                harness?.SourcePawn == null ||
                harness.ReplacementDeltas.Count + harness.ImplantDeltas.Count +
                harness.DamageDeltas.Count + harness.PartStateDeltas.Count == 0)
                return currentY;

            string label = SettingsViewUtilities.CachedText("OverHaulers_LiveChange_ReviewButton");
            Vector2 size = SettingsViewUtilities.ButtonSize(label, width, minHeight: 28f, fixedWidth: width);
            Rect button = new Rect(bounds.x, currentY, size.x, size.y);
            if (SettingsViewUtilities.DrawButton(
                button,
                label,
                SettingsViewUtilities.CachedText("OverHaulers_LiveChange_ReviewTooltip")))
            {
                OpenLiveChangeReview(harness);
            }

            return currentY + size.y + SettingsViewUtilities.CompactGap;
        }

        /// <summary>
        /// Builds and displays the explicit review/confirmation batch.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness containing the live changes.</param>
        private static void OpenLiveChangeReview(SandboxPawnHarness harness)
        {
            MedicalLiveChangePlan plan = MedicalLiveChangePlanCompiler.Build(harness);
            if (plan.ReadyCount == 0)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    SettingsViewUtilities.CachedText("OverHaulers_LiveChange_NoReadyChanges"),
                    "Close".Translate().ToString(),
                    null,
                    null,
                    null,
                    SettingsViewUtilities.CachedText("OverHaulers_LiveChange_ReviewTitle")));
                return;
            }

            string text = BuildLiveChangeReviewText(plan);
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                text,
                () => ApplyReviewedLiveChanges(harness, plan),
                destructive: true,
                title: SettingsViewUtilities.CachedText("OverHaulers_LiveChange_ConfirmTitle")));
        }

        #endregion

        #region 2. [TEST-07] REVIEW FORMATTING & EXECUTION

        /// <summary>
        /// Builds the textual representation of the live change review.
        /// </summary>
        /// <param name="plan">The medical live change plan containing the changes to review.</param>
        /// <returns>A formatted string representing the review of live changes.</returns>
        private static string BuildLiveChangeReviewText(MedicalLiveChangePlan plan)
        {
            var builder = new StringBuilder(512);
            builder.AppendLine("OverHaulers_LiveChange_ReviewHeader".Translate(
                plan.Pawn?.LabelShort,
                plan.ReadyCount).ToString());
            builder.AppendLine();

            for (int i = 0; i < plan.Changes.Count; i++)
            {
                MedicalLiveChange change = plan.Changes[i];
                string partLabel = change.Intent.Part?.LabelCap.ToString()
                    ?? SettingsViewUtilities.CachedText("OverHaulers_LiveChange_WholeBody");
                string stateLabel = change.Status == MedicalLiveChangeStatus.Ready
                    ? SettingsViewUtilities.CachedText("OverHaulers_LiveChange_Status_Ready")
                    : SettingsViewUtilities.CachedText("OverHaulers_LiveChange_Status_Omitted");
                string reason = change.Status == MedicalLiveChangeStatus.Ready || change.Reason.NullOrEmpty()
                    ? string.Empty
                    : " - " + change.Reason.Translate().ToString();
                builder.AppendLine(
                    "OverHaulers_LiveChange_ReviewRow".Translate(
                        stateLabel,
                        change.Intent.Label ?? change.Intent.Kind.ToString(),
                        partLabel,
                        reason).ToString());
            }

            builder.AppendLine();
            builder.AppendLine(SettingsViewUtilities.CachedText("OverHaulers_LiveChange_BypassWarning"));
            return builder.ToString();
        }

        /// <summary>
        /// Applies the reviewed live changes to the sandbox pawn harness.
        /// </summary>
        /// <param name="harness">The sandbox pawn harness containing the live changes.</param>
        /// <param name="plan">The medical live change plan containing the changes to apply.</param>
        /// <remarks>
        /// This method attempts to apply all changes in the plan and provides user feedback
        /// through messages indicating the success or failure of the operation.
        /// </remarks>
        private static void ApplyReviewedLiveChanges(
            SandboxPawnHarness harness,
            MedicalLiveChangePlan plan)
        {
            try
            {
                MedicalLiveExecutionResult result = MedicalLiveOutcomeExecutor.Execute(harness, plan);
                if (result.AppliedCount > 0)
                {
                    TestBench.MarkDirty();
                    Messages.Message(
                        "OverHaulers_LiveChange_AppliedMessage".Translate(
                            result.AppliedCount,
                            result.FailedChanges.Count).ToString(),
                        result.FailedChanges.Count > 0
                            ? MessageTypeDefOf.CautionInput
                            : MessageTypeDefOf.PositiveEvent,
                        false);
                }
                else
                {
                    Messages.Message(
                        SettingsViewUtilities.CachedText("OverHaulers_LiveChange_NoneApplied"),
                        MessageTypeDefOf.CautionInput,
                        false);
                }
            }
            catch (Exception ex)
            {
                OHLog.Warn(
                    "TestBench.ToolingDialog.LiveChanges",
                    ex,
                    "Could not apply reviewed live Test Bench changes.");
                Messages.Message(
                    SettingsViewUtilities.CachedText("OverHaulers_LiveChange_Error"),
                    MessageTypeDefOf.NegativeEvent,
                    false);
            }
        }

        #endregion
    }
}

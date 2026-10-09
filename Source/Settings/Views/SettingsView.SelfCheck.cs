using UnityEngine;
using Verse;

namespace OverHaulers
{
    public static partial class SettingsView
    {
        #region 1. [DIAG-04] SELF-CHECK PRESENTATION CACHE

        private const float SelfCheckRowHeight = 18f;
        private const float SelfCheckTagWidth = 44f;

        // Row strings and measured detail heights are rebuilt only when results, language or layout width change.
        private static int selfCheckCacheVersion = -1;
        private static int selfCheckCacheEpoch = -1;
        private static float selfCheckCacheWidth = -1f;
        private static string selfCheckSummaryText = string.Empty;
        private static string[] selfCheckRowLabels;
        private static float[] selfCheckDetailHeights;

        // The running counter changes every frame while checks execute, so it is rebuilt only when the count changes.
        private static string selfCheckRunningText = string.Empty;
        private static int selfCheckRunningFinished = -1;

        /// <summary>
        /// Rebuilds the cached summary, row labels and detail heights if results, language or width changed since the last build.
        /// </summary>
        private static void RefreshSelfCheckCacheIfStale(float width)
        {
            int epoch = SettingsViewUtilities.TranslationEpoch;
            if (selfCheckCacheVersion == SelfCheckRunner.ResultsVersion && selfCheckCacheEpoch == epoch && Mathf.Approximately(selfCheckCacheWidth, width)) return;

            selfCheckCacheVersion = SelfCheckRunner.ResultsVersion;
            selfCheckCacheEpoch = epoch;
            selfCheckCacheWidth = width;
            selfCheckRunningFinished = -1;

            int skipped = SelfCheckRunner.SkippedCount;
            int considered = SelfCheckRunner.Results.Count - skipped;
            string summary = "OverHaulers_SelfCheck_Summary".Translate(SelfCheckRunner.PassedCount, considered).ToString();
            if (skipped > 0)
            {
                summary += "  " + "OverHaulers_SelfCheck_SummarySkipped".Translate(skipped).ToString();
            }
            selfCheckSummaryText = summary;

            int count = SelfCheckRunner.Results.Count;
            selfCheckRowLabels = new string[count];
            selfCheckDetailHeights = new float[count];
            float detailWidth = width - SelfCheckTagWidth;

            for (int i = 0; i < count; i++)
            {
                SelfCheckResult r = SelfCheckRunner.Results[i];
                selfCheckRowLabels[i] = SettingsViewUtilities.CachedText(r.GroupKey) + ": " + SettingsViewUtilities.CachedText(r.NameKey);
                selfCheckDetailHeights[i] = string.IsNullOrEmpty(r.Detail)
                    ? 0f
                    : SettingsViewUtilities.CalcTextHeight(r.Detail, detailWidth, GameFont.Tiny);
            }
        }

        #endregion

        #region 2. [DIAG-04] SELF-CHECK BLOCK DRAWER

        /// <summary>
        /// Draws the in-game self-check block at the bottom of the Diagnostics section.
        /// </summary>
        /// <param name="inner">The inner rectangle of the diagnostics section box.</param>
        /// <param name="localY">The current Y position within the section.</param>
        /// <returns>The updated Y position after drawing the block.</returns>
        public static float DrawSelfCheckBlock(Rect inner, float localY)
        {
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(inner.x, localY, inner.width, 22f), SettingsViewUtilities.CachedText("OverHaulers_SelfCheck_Header"));
            localY += 24f;

            localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, SettingsViewUtilities.CachedText("OverHaulers_SelfCheck_Desc"), localY);
            localY += 2f;

            bool running = SelfCheckRunner.State == SelfCheckState.Running;

            Rect runRect = new Rect(inner.x, localY, Mathf.Min(170f, inner.width * 0.3f), 28f);
            bool originalEnabled = GUI.enabled;
            try
            {
                GUI.enabled = originalEnabled && !running;
                if (Widgets.ButtonText(runRect, SettingsViewUtilities.CachedText("OverHaulers_SelfCheck_Run")))
                {
                    SelfCheckRunner.Start();
                }
            }
            finally
            {
                GUI.enabled = originalEnabled;
            }

            // Status beside the button
            Rect statusRect = new Rect(runRect.xMax + 12f, localY, inner.width - runRect.width - 12f, 28f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            try
            {
                if (running)
                {
                    if (selfCheckRunningFinished != SelfCheckRunner.FinishedCount)
                    {
                        selfCheckRunningFinished = SelfCheckRunner.FinishedCount;
                        selfCheckRunningText = "OverHaulers_SelfCheck_Running".Translate(selfCheckRunningFinished, SelfCheckRunner.TotalCount).ToString();
                    }

                    GUI.color = SettingsViewUtilities.DescriptionTextColor;
                    Widgets.Label(statusRect, selfCheckRunningText);
                }
                else if (SelfCheckRunner.HasResults)
                {
                    RefreshSelfCheckCacheIfStale(inner.width);

                    GUI.color = SelfCheckRunner.FailedCount == 0 ? SettingsViewUtilities.HealthyColor : SettingsViewUtilities.CriticalColor;
                    Widgets.Label(statusRect, selfCheckSummaryText);
                }
            }
            finally
            {
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            localY += 34f;

            if (SelfCheckRunner.HasResults)
            {
                localY = DrawSelfCheckResults(inner, localY);
            }

            return localY + 6f;
        }

        #endregion

        #region 3. [DIAG-04] SELF-CHECK RESULT ROWS

        /// <summary>
        /// Draws one row per executed check with its outcome tag, name and any note or failure reason, using cached strings and heights.
        /// </summary>
        /// <param name="inner">The inner rectangle of the diagnostics section.</param>
        /// <param name="localY">The current Y position within the section.</param>
        /// <returns>The updated Y position.</returns>
        private static float DrawSelfCheckResults(Rect inner, float localY)
        {
            RefreshSelfCheckCacheIfStale(inner.width);
            Text.Font = GameFont.Tiny;

            try
            {
                for (int i = 0; i < SelfCheckRunner.Results.Count; i++)
                {
                    SelfCheckResult r = SelfCheckRunner.Results[i];

                    string tagKey;
                    Color color;
                    switch (r.Outcome)
                    {
                        case SelfCheckOutcome.Passed:
                            tagKey = "OverHaulers_SelfCheck_Pass";
                            color = SettingsViewUtilities.HealthyColor;
                            break;
                        case SelfCheckOutcome.Skipped:
                            tagKey = "OverHaulers_SelfCheck_Skip";
                            color = SettingsViewUtilities.DescriptionTextColor;
                            break;
                        default:
                            tagKey = "OverHaulers_SelfCheck_Fail";
                            color = SettingsViewUtilities.CriticalColor;
                            break;
                    }

                    Rect tagRect = new Rect(inner.x, localY, SelfCheckTagWidth, SelfCheckRowHeight);
                    Rect nameRect = new Rect(tagRect.xMax, localY, inner.width - SelfCheckTagWidth, SelfCheckRowHeight);

                    GUI.color = color;
                    Widgets.Label(tagRect, SettingsViewUtilities.CachedText(tagKey));
                    GUI.color = Color.white;
                    Widgets.Label(nameRect, selfCheckRowLabels[i]);
                    localY += SelfCheckRowHeight;

                    // Failures and skips always show their reason; passes show their note only when there is one.
                    float detailHeight = selfCheckDetailHeights[i];
                    if (detailHeight > 0f)
                    {
                        GUI.color = r.Outcome == SelfCheckOutcome.Failed ? SettingsViewUtilities.CriticalColor : SettingsViewUtilities.DescriptionTextColor;
                        Widgets.Label(new Rect(inner.x + SelfCheckTagWidth, localY, inner.width - SelfCheckTagWidth, detailHeight), r.Detail);
                        GUI.color = Color.white;
                        localY += detailHeight + 2f;
                    }
                }
            }
            finally
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            return localY;
        }

        #endregion
    }
}

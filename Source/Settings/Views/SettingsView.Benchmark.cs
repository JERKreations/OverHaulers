using System.Globalization;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    public static partial class SettingsView
    {
        #region 1. [DIAG-03] BENCHMARK PRESENTATION CACHE

        private const float BenchmarkRowHeight = 18f;

        private static readonly float[] BenchmarkSolverColumns = { 0.30f, 0.12f, 0.15f, 0.14f, 0.17f, 0.12f };
        private static readonly float[] BenchmarkRampColumns = { 0.20f, 0.40f, 0.40f };
        private static readonly float[] BenchmarkSnapshotColumns = { 0.34f, 0.11f, 0.19f, 0.16f, 0.10f, 0.10f };

        // Row strings are rebuilt only when results or the active language change, never per OnGUI event.
        private static int benchmarkCacheVersion = -1;
        private static int benchmarkCacheEpoch = -1;
        private static string benchmarkStartupLine = string.Empty;
        private static string[] benchmarkSolverHeaders;
        private static string[][] benchmarkSolverRows;
        private static string[] benchmarkRampHeaders;
        private static string[][] benchmarkRampRows;
        private static string benchmarkSnapshotLine = string.Empty;
        private static string[] benchmarkSnapshotHeaders;
        private static string[][] benchmarkSnapshotRows;

        // Status strings that embed changing values are rebuilt only when those values change.
        private static string benchmarkRunningText = string.Empty;
        private static string benchmarkRunningLabel;
        private static int benchmarkRunningPercent = -1;
        private static string benchmarkFailedText = string.Empty;
        private static string benchmarkFailedMessage;

        /// <summary>
        /// Rebuilds every cached results string if the results or the active language changed since the last build.
        /// </summary>
        private static void RefreshBenchmarkCacheIfStale()
        {
            int epoch = SettingsViewUtilities.TranslationEpoch;
            if (benchmarkCacheVersion == BenchmarkRunner.ResultsVersion && benchmarkCacheEpoch == epoch) return;

            benchmarkCacheVersion = BenchmarkRunner.ResultsVersion;
            benchmarkCacheEpoch = epoch;
            benchmarkRunningLabel = null;
            benchmarkFailedMessage = null;

            benchmarkStartupLine = "OverHaulers_Benchmark_Startup".Translate(
                BenchmarkRunner.TopologyCompileMs.ToString("F1", CultureInfo.InvariantCulture),
                BenchmarkRunner.TopologyBodyDefCount,
                BenchmarkRunner.TopologyMaxPartsBody,
                BenchmarkRunner.TopologyMaxParts).ToString();

            benchmarkSolverHeaders = new[]
            {
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_ColSubject"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_ColProfile"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_ColMedian"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_ColP95"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_ColRate"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_ColGc")
            };

            benchmarkSolverRows = new string[BenchmarkRunner.Results.Count][];
            for (int i = 0; i < BenchmarkRunner.Results.Count; i++)
            {
                BenchmarkSolverResult r = BenchmarkRunner.Results[i];
                string[] cells = new string[BenchmarkSolverColumns.Length];

                cells[0] = r.SubjectLabel + " (" + r.PartCount + ")";
                cells[1] = BenchmarkRunner.ProfileLabel(r.Profile);

                if (r.Failed)
                {
                    cells[2] = cells[3] = cells[4] = cells[5] = "—";
                }
                else
                {
                    cells[2] = r.MedianUs.ToString("F1", CultureInfo.InvariantCulture);
                    cells[3] = r.P95Us.ToString("F1", CultureInfo.InvariantCulture);
                    cells[4] = r.SolvesPerSecond.ToString("F0", CultureInfo.InvariantCulture);
                    cells[5] = r.GcCollections.ToString(CultureInfo.InvariantCulture);
                }

                benchmarkSolverRows[i] = cells;
            }

            benchmarkRampHeaders = new[]
            {
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_RampColPawns"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_RampColReference"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_RampColWorst")
            };

            double referenceUs = BenchmarkRunner.ReferenceMedianUs();
            double worstUs = BenchmarkRunner.WorstMedianUs();
            benchmarkRampRows = new string[BenchmarkRunner.RampPawnCounts.Length][];
            for (int i = 0; i < BenchmarkRunner.RampPawnCounts.Length; i++)
            {
                int pawnCount = BenchmarkRunner.RampPawnCounts[i];
                benchmarkRampRows[i] = new[]
                {
                    pawnCount.ToString(CultureInfo.InvariantCulture),
                    FormatBenchmarkRampCell(pawnCount * referenceUs / 1000.0),
                    FormatBenchmarkRampCell(pawnCount * worstUs / 1000.0)
                };
            }

            RebuildBenchmarkSnapshotStrings();
        }

        /// <summary>
        /// Builds the cached strings for the background snapshot table results (single-thread cost and contention scenarios).
        /// </summary>
        private static void RebuildBenchmarkSnapshotStrings()
        {
            if (!SnapshotTableBenchmark.HasResults)
            {
                benchmarkSnapshotLine = string.Empty;
                benchmarkSnapshotHeaders = new string[0];
                benchmarkSnapshotRows = new string[0][];
                return;
            }

            SnapshotReadResult read = SnapshotTableBenchmark.ReadResult;
            benchmarkSnapshotLine = "OverHaulers_Benchmark_TableSingle".Translate(
                read.HitNanoseconds.ToString("F1", CultureInfo.InvariantCulture),
                read.MissNanoseconds.ToString("F1", CultureInfo.InvariantCulture),
                read.PawnCount,
                read.TableCapacity).ToString();

            benchmarkSnapshotHeaders = new[]
            {
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableColScenario"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableColReaders"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableColReads"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableColWrites"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableColRetry"),
                SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableColTimeout")
            };

            benchmarkSnapshotRows = new string[SnapshotTableBenchmark.ContentionResults.Count][];
            for (int i = 0; i < SnapshotTableBenchmark.ContentionResults.Count; i++)
            {
                SnapshotContentionResult r = SnapshotTableBenchmark.ContentionResults[i];
                benchmarkSnapshotRows[i] = new[]
                {
                    r.WritesPerFrame > 0
                        ? "OverHaulers_Benchmark_TableScenarioPaced".Translate(r.WritesPerFrame).ToString()
                        : SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableScenarioSaturating"),
                    r.ReaderThreads.ToString(CultureInfo.InvariantCulture),
                    r.ReadsPerSecond.ToString("F0", CultureInfo.InvariantCulture),
                    r.WritesPerSecond.ToString("F0", CultureInfo.InvariantCulture),
                    r.RetryPercent.ToString("F2", CultureInfo.InvariantCulture),
                    r.TimeoutPercent.ToString("F2", CultureInfo.InvariantCulture)
                };
            }
        }

        /// <summary>
        /// Formats one estimate cell as milliseconds plus the share of a 60 fps frame it represents.
        /// </summary>
        /// <param name="milliseconds">The estimated cost in milliseconds.</param>
        /// <returns>The formatted cell text.</returns>
        private static string FormatBenchmarkRampCell(double milliseconds)
        {
            double percent = milliseconds / BenchmarkRunner.FrameBudgetMs60Fps * 100.0;
            return milliseconds.ToString("F2", CultureInfo.InvariantCulture) + " ms  (" + percent.ToString("F1", CultureInfo.InvariantCulture) + "%)";
        }

        #endregion

        #region 2. [DIAG-03] BENCHMARK BLOCK DRAWER

        /// <summary>
        /// Draws the in-game performance benchmark block at the bottom of the Diagnostics section.
        /// </summary>
        /// <param name="inner">The inner rectangle of the diagnostics section box.</param>
        /// <param name="localY">The current Y position within the section.</param>
        /// <returns>The updated Y position after drawing the block.</returns>
        public static float DrawBenchmarkBlock(Rect inner, float localY)
        {
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(inner.x, localY, inner.width, 22f), SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Header"));
            localY += 24f;

            localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Desc"), localY);
            localY += 2f;

            bool running = BenchmarkRunner.State == BenchmarkState.Running;
            bool hasResults = BenchmarkRunner.HasResults;

            // Action row: Run/Cancel, Copy, Export, Open folder
            float gap = 8f;
            float buttonWidth = Mathf.Min(150f, (inner.width - gap * 3f) / 4f);
            Rect runRect = new Rect(inner.x, localY, buttonWidth, 28f);
            Rect copyRect = new Rect(runRect.xMax + gap, localY, buttonWidth, 28f);
            Rect exportRect = new Rect(copyRect.xMax + gap, localY, buttonWidth, 28f);
            Rect folderRect = new Rect(exportRect.xMax + gap, localY, buttonWidth, 28f);

            if (running)
            {
                if (Widgets.ButtonText(runRect, SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Cancel")))
                {
                    BenchmarkRunner.Cancel();
                }
            }
            else if (Widgets.ButtonText(runRect, SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Run")))
            {
                BenchmarkRunner.Start();
            }

            bool originalEnabled = GUI.enabled;
            try
            {
                GUI.enabled = originalEnabled && hasResults;

                if (Widgets.ButtonText(copyRect, SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Copy")))
                {
                    GUIUtility.systemCopyBuffer = BenchmarkRunner.BuildReport();
                    Messages.Message(SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Copied"), MessageTypeDefOf.PositiveEvent, false);
                }

                if (Widgets.ButtonText(exportRect, SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Export")))
                {
                    string path = BenchmarkRunner.ExportReport();
                    Messages.Message("OverHaulers_DumpExportedMessage".Translate(path).ToString(), MessageTypeDefOf.PositiveEvent, false);
                }
            }
            finally
            {
                GUI.enabled = originalEnabled;
            }

            if (Widgets.ButtonText(folderRect, SettingsViewUtilities.CachedText("OverHaulers_OpenDumpsFolder")))
            {
                DiagnosticExportUtility.OpenExportDirectory();
            }

            localY += 36f;

            if (running)
            {
                localY = DrawBenchmarkProgress(inner, localY);
            }
            else if (BenchmarkRunner.State == BenchmarkState.Failed)
            {
                if (benchmarkFailedMessage != BenchmarkRunner.ErrorMessage)
                {
                    benchmarkFailedMessage = BenchmarkRunner.ErrorMessage;
                    benchmarkFailedText = "OverHaulers_Benchmark_Failed".Translate(benchmarkFailedMessage).ToString();
                }
                localY = DrawBenchmarkStatusLine(inner, localY, benchmarkFailedText, SettingsViewUtilities.CriticalColor);
            }
            else if (BenchmarkRunner.State == BenchmarkState.Cancelled)
            {
                localY = DrawBenchmarkStatusLine(inner, localY, SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Cancelled"), SettingsViewUtilities.DescriptionTextColor);
            }

            if (hasResults)
            {
                localY = DrawBenchmarkResults(inner, localY);
            }

            return localY + 6f;
        }

        /// <summary>
        /// Draws the progress bar and its status text while the benchmark is running.
        /// </summary>
        /// <param name="inner">The inner rectangle of the diagnostics section.</param>
        /// <param name="localY">The current Y position within the section.</param>
        /// <returns>The updated Y position.</returns>
        private static float DrawBenchmarkProgress(Rect inner, float localY)
        {
            Rect barRect = new Rect(inner.x, localY, inner.width, 20f);
            Widgets.FillableBar(barRect, BenchmarkRunner.Progress);

            int percent = (int)(BenchmarkRunner.Progress * 100f);
            if (percent != benchmarkRunningPercent || benchmarkRunningLabel != BenchmarkRunner.CurrentLabel)
            {
                benchmarkRunningPercent = percent;
                benchmarkRunningLabel = BenchmarkRunner.CurrentLabel;
                benchmarkRunningText = "OverHaulers_Benchmark_Running".Translate(benchmarkRunningLabel, percent).ToString();
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, benchmarkRunningText);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            return localY + 26f;
        }

        /// <summary>
        /// Draws a single-line status message in the given colour.
        /// </summary>
        /// <param name="inner">The inner rectangle of the diagnostics section.</param>
        /// <param name="localY">The current Y position within the section.</param>
        /// <param name="text">The message to draw.</param>
        /// <param name="color">The text colour.</param>
        /// <returns>The updated Y position.</returns>
        private static float DrawBenchmarkStatusLine(Rect inner, float localY, string text, Color color)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = color;
            Widgets.Label(new Rect(inner.x, localY, inner.width, BenchmarkRowHeight), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return localY + BenchmarkRowHeight + 6f;
        }

        #endregion

        #region 3. [DIAG-03] BENCHMARK RESULT TABLES

        /// <summary>
        /// Draws the startup line, the solver table, the re-solve estimate table and the snapshot table results from cached strings.
        /// </summary>
        /// <param name="inner">The inner rectangle of the diagnostics section.</param>
        /// <param name="localY">The current Y position within the section.</param>
        /// <returns>The updated Y position.</returns>
        private static float DrawBenchmarkResults(Rect inner, float localY)
        {
            RefreshBenchmarkCacheIfStale();
            Text.Font = GameFont.Tiny;

            try
            {
                Widgets.Label(new Rect(inner.x, localY, inner.width, BenchmarkRowHeight), benchmarkStartupLine);
                localY += BenchmarkRowHeight + 4f;

                GUI.color = SettingsViewUtilities.DescriptionTextColor;
                DrawBenchmarkRow(inner, localY, BenchmarkSolverColumns, benchmarkSolverHeaders);
                GUI.color = Color.white;
                localY += BenchmarkRowHeight;

                for (int i = 0; i < benchmarkSolverRows.Length; i++)
                {
                    DrawBenchmarkRow(inner, localY, BenchmarkSolverColumns, benchmarkSolverRows[i]);
                    localY += BenchmarkRowHeight;
                }

                localY += 8f;

                Widgets.Label(new Rect(inner.x, localY, inner.width, BenchmarkRowHeight), SettingsViewUtilities.CachedText("OverHaulers_Benchmark_RampHeader"));
                localY += BenchmarkRowHeight;

                GUI.color = SettingsViewUtilities.DescriptionTextColor;
                DrawBenchmarkRow(inner, localY, BenchmarkRampColumns, benchmarkRampHeaders);
                GUI.color = Color.white;
                localY += BenchmarkRowHeight;

                for (int i = 0; i < benchmarkRampRows.Length; i++)
                {
                    DrawBenchmarkRow(inner, localY, BenchmarkRampColumns, benchmarkRampRows[i]);
                    localY += BenchmarkRowHeight;
                }

                if (SnapshotTableBenchmark.HasResults)
                {
                    localY = DrawBenchmarkSnapshotTable(inner, localY + 8f);
                }

                localY += 6f;
                localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, SettingsViewUtilities.CachedText("OverHaulers_Benchmark_Notes"), localY);
                if (SnapshotTableBenchmark.HasResults)
                {
                    localY = SettingsViewUtilities.DrawSectionDescriptionDirect(inner, SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableNotes"), localY);
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

        /// <summary>
        /// Draws the background snapshot table results: the single-thread read cost line and the reader-versus-writer scenario table.
        /// </summary>
        private static float DrawBenchmarkSnapshotTable(Rect inner, float localY)
        {
            Widgets.Label(new Rect(inner.x, localY, inner.width, BenchmarkRowHeight), SettingsViewUtilities.CachedText("OverHaulers_Benchmark_TableHeader"));
            localY += BenchmarkRowHeight;

            Widgets.Label(new Rect(inner.x, localY, inner.width, BenchmarkRowHeight), benchmarkSnapshotLine);
            localY += BenchmarkRowHeight + 2f;

            GUI.color = SettingsViewUtilities.DescriptionTextColor;
            DrawBenchmarkRow(inner, localY, BenchmarkSnapshotColumns, benchmarkSnapshotHeaders);
            GUI.color = Color.white;
            localY += BenchmarkRowHeight;

            for (int i = 0; i < benchmarkSnapshotRows.Length; i++)
            {
                DrawBenchmarkRow(inner, localY, BenchmarkSnapshotColumns, benchmarkSnapshotRows[i]);
                localY += BenchmarkRowHeight;
            }

            return localY;
        }

        /// <summary>
        /// Draws one table row; the first column is left-aligned and the rest right-aligned.
        /// </summary>
        private static void DrawBenchmarkRow(Rect inner, float y, float[] fractions, string[] cells)
        {
            float x = inner.x;
            for (int c = 0; c < fractions.Length; c++)
            {
                float width = inner.width * fractions[c];
                Text.Anchor = c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                Widgets.Label(new Rect(x, y, width - 6f, BenchmarkRowHeight), cells[c]);
                x += width;
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        #endregion
    }
}

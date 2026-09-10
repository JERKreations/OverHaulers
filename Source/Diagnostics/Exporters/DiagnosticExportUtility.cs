using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    #region 1. [DIAG-00] DUMP SCOPE & FORMAT ENUMS

    /// <summary>
    /// [DIAG-00] Scope of diagnostic census data to compile and export.
    /// </summary>
    public enum DumpScope
    {
        SelectedSubject = 0,
        BodyDefArchetype = 1,
        FullCensus = 2
    }

    /// <summary>
    /// [DIAG-00] File format serialization target for diagnostic dumps.
    /// </summary>
    public enum DumpFormat
    {
        Text = 0,
        Xml = 1
    }

    #endregion

    /// <summary>
    /// [DIAG-00] Shared formatting, census table generation, date-based folder routing, and file I/O utilities for diagnostic dump exporters.
    /// Resides under Source/Diagnostics/.
    /// </summary>
    public static class DiagnosticExportUtility
    {
        #region 2. HEADER & METADATA FORMATTING

        public static void AppendHeaderBanner(
            StringBuilder builder, 
            string title, 
            DumpScope scope, 
            string timestamp, 
            TestSubjectEntry subject)
        {
            builder.AppendLine("========================================================================================================================");
            builder.AppendLine(title);
            builder.AppendLine($"Scope: {scope} | Generated: {timestamp}");
            if (subject != null)
            {
                builder.AppendLine($"Active Subject: {subject.Label} | Race: {subject.RaceDef?.defName ?? "None"} | BodyDef: {subject.BodyDef?.defName ?? "None"}");
            }
            builder.AppendLine("========================================================================================================================");
            builder.AppendLine();
        }

        #endregion

        #region 3. MACRO SPECIES SUMMARY TABLE GENERATOR

        /// <summary>
        /// Appends a summary table of species sharing the specified body archetype or all loaded species, depending on the dump scope.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the summary table to.</param>
        /// <param name="scope">The scope of the dump, determining which species to include.</param>
        /// <param name="targetBody">The body archetype to filter species by when scope is BodyDefArchetype.</param>
        /// <param name="allPawnThings">The list of all loaded pawn ThingDefs to consider for the summary table.</param>
        public static void AppendSpeciesSummaryTable(
            StringBuilder builder, 
            DumpScope scope, 
            BodyDef targetBody, 
            List<ThingDef> allPawnThings)
        {
            if (scope != DumpScope.FullCensus && scope != DumpScope.BodyDefArchetype) return;

            builder.AppendLine("------------------------------------------------------------------------------------------------------------------------");
            builder.AppendLine(scope == DumpScope.FullCensus 
                ? "[1. MACRO SPECIES SUMMARY TABLE (ALL LOADED SPECIES)]" 
                : $"[1. SPECIES SHARING ARCHETYPE: {targetBody.defName}]");
            builder.AppendLine("------------------------------------------------------------------------------------------------------------------------");
            builder.AppendLine(string.Format("{0,-36} | {1,-32} | {2,5} | {3,5} | {4,5} | {5,5} | {6,5} | {7,5}",
                "Species Label", "BodyDef Name", "Parts", "Core", "Manip", "Move", "Dual", "Head"));
            builder.AppendLine(new string('-', 120));

            for (int i = 0; i < allPawnThings.Count; i++)
            {
                ThingDef thing = allPawnThings[i];
                BodyDef body = thing.race.body;
                if (scope == DumpScope.BodyDefArchetype && body != targetBody) continue;

                SpeciesTopologyTemplate tmpl = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                if (tmpl == null) continue;

                string speciesLabel = PadOrTruncate(thing.LabelCap.ToString(), 36);
                string bodyName = PadOrTruncate(body.defName, 32);

                builder.AppendLine(string.Format("{0,-36} | {1,-32} | {2,5} | {3,5} | {4,5} | {5,5} | {6,5} | {7,5}",
                    speciesLabel, bodyName, tmpl.PartCount, 
                    tmpl.Counts.CoreParts, tmpl.Counts.ManipulationParts, 
                    tmpl.Counts.MovingParts, tmpl.Counts.DualLimbs, tmpl.Counts.HeadParts));
            }

            builder.AppendLine();
        }

        #endregion

        #region 4. SCOPE RESOLUTION, DATE-BASED ROUTING & FILE I/O

        /// <summary>
        /// Resolves the root directory for exporting diagnostic dumps, creating it if necessary.
        /// </summary>
        /// <returns>The full path to the root export directory for diagnostic dumps.</returns>
        public static string ResolveRootExportDirectory()
        {
            try
            {
                string modRoot = OverHaulers.ContentPack?.RootDir;
                if (!string.IsNullOrEmpty(modRoot) && Directory.Exists(modRoot))
                {
                    string rootDir = Path.Combine(modRoot, "DiagnosticDumps");
                    if (!Directory.Exists(rootDir))
                    {
                        Directory.CreateDirectory(rootDir);
                    }
                    return rootDir;
                }
            }
            catch (Exception ex)
            {
                OHLog.Solver.WarnException("ResolveRootExportDirectory", ex);
            }

            string fallbackRoot = Path.Combine(GenFilePaths.SaveDataFolderPath, "OverHaulers_DiagnosticDumps");
            if (!Directory.Exists(fallbackRoot))
            {
                Directory.CreateDirectory(fallbackRoot);
            }
            return fallbackRoot;
        }

        /// <summary>
        /// Resolves the export directory for the current date, creating it if necessary.
        /// </summary>
        /// <returns>The full path to the export directory for the current date.</returns>
        public static string ResolveExportDirectory()
        {
            string rootDir = ResolveRootExportDirectory();
            string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
            string targetDir = Path.Combine(rootDir, dateFolder);

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            return targetDir;
        }

        /// <summary>
        /// Opens the export directory in the OS file explorer.
        /// Opens today's dated subfolder if it exists and contains files; otherwise opens the root DiagnosticDumps directory.
        /// 100% platform-agnostic with zero native P/Invoke calls.
        /// </summary>
        public static void OpenExportDirectory()
        {
            try
            {
                string rootDir = ResolveRootExportDirectory();
                string todayDir = Path.Combine(rootDir, DateTime.Now.ToString("yyyy-MM-dd"));

                // Only open today's folder if it actually exists on disk (i.e. dumps were generated today)
                string targetToOpen = Directory.Exists(todayDir) ? todayDir : rootDir;

                if (Directory.Exists(targetToOpen))
                {
                    Application.OpenURL(targetToOpen);
                }
            }
            catch (Exception ex)
            {
                OHLog.Solver.WarnException("OpenExportDirectoryFailed", ex);
            }
        }

        /// <summary>
        /// Resolves the list of target bodies based on the specified dump scope.
        /// </summary>
        /// <param name="scope">The scope of the dump, determining which bodies to include.</param>
        /// <param name="targetBody">The specific body archetype to include when the scope is limited.</param>
        /// <param name="allBodies">The list of all available body definitions.</param>
        /// <returns>The list of body definitions to be included in the dump based on the scope.</returns>
        public static List<BodyDef> ResolveTargetBodies(DumpScope scope, BodyDef targetBody, List<BodyDef> allBodies)
        {
            if (scope == DumpScope.FullCensus)
            {
                return allBodies;
            }
            return new List<BodyDef> { targetBody };
        }

        /// <summary>
        /// Resolves a string tag representing the scope of the dump, used for file naming and organization.
        /// </summary>
        /// <param name="scope">The scope of the dump, determining the context of the tag.</param>
        /// <param name="subject">The test subject entry, used when the scope is limited to a single subject.</param>
        /// <param name="targetBody">The specific body archetype, used when the scope is limited to a body definition.</param>
        /// <returns>A string tag representing the resolved scope for the dump.</returns>
        public static string ResolveScopeTag(DumpScope scope, TestSubjectEntry subject, BodyDef targetBody)
        {
            switch (scope)
            {
                case DumpScope.SelectedSubject:
                    return $"Single_{SanitizeFileName(subject?.Label ?? "Subject")}";
                case DumpScope.BodyDefArchetype:
                    return $"Archetype_{SanitizeFileName(targetBody?.defName ?? "Body")}";
                default:
                    return "FullCensus";
            }
        }

        /// <summary>
        /// Writes the contents of the provided StringBuilder to an export file, using the specified file prefix, scope tag, and format. If writing
        ///  to the primary export directory fails, a fallback directory is used.
        /// </summary>
        /// <param name="builder">The StringBuilder containing the content to be written to the export file.</param>
        /// <param name="filePrefix">The prefix to use for the export file name.</param>
        /// <param name="scopeTag">The tag representing the scope of the dump, used in the file name.</param>
        /// <param name="format">The format of the export file (text or XML).</param>
        /// <returns>The full path to the export file that was written.</returns>
        public static string WriteExportFile(
            StringBuilder builder, 
            string filePrefix, 
            string scopeTag, 
            DumpFormat format = DumpFormat.Text)
        {
            string ext = format == DumpFormat.Xml ? "xml" : "txt";
            string fileName = $"{filePrefix}_{scopeTag}_{DateTime.Now:HHmmss}.{ext}";
            string exportDir = ResolveExportDirectory();
            string exportPath = Path.Combine(exportDir, fileName);

            try
            {
                File.WriteAllText(exportPath, builder.ToString());
            }
            catch (Exception ex)
            {
                OHLog.Solver.WarnException("WriteExportFileModRootFailed", ex);
                string fallbackDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "OverHaulers_DiagnosticDumps", DateTime.Now.ToString("yyyy-MM-dd"));
                if (!Directory.Exists(fallbackDir)) Directory.CreateDirectory(fallbackDir);

                string fallbackPath = Path.Combine(fallbackDir, fileName);
                File.WriteAllText(fallbackPath, builder.ToString());
                exportPath = fallbackPath;
            }
            finally
            {
                builder.Clear();
            }

            return exportPath;
        }

        #endregion

        #region 5. STRING SANITIZATION & XML ESCAPING HELPERS

        /// <summary>
        /// Pads or truncates the given string to the specified width. If the string is longer than the width, it is truncated and appended
        ///  with ellipsis. If it is shorter, it is padded with spaces.
        /// </summary>
        /// <param name="str">The string to be padded or truncated.</param>
        /// <param name="width">The target width for the string.</param>
        /// <returns>The string adjusted to the specified width.</returns>
        public static string PadOrTruncate(string str, int width)
        {
            if (string.IsNullOrEmpty(str)) return new string(' ', width);
            if (str.Length > width) return str.Substring(0, width - 3) + "...";
            return str.PadRight(width);
        }

        /// <summary>
        /// Sanitizes the given file name by replacing invalid characters with underscores and spaces with underscores. If the name is null or
        ///  empty, returns "Unknown".
        /// </summary>
        /// <param name="name">The file name to be sanitized.</param>
        /// <returns>The sanitized file name.</returns>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            char[] invalids = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalids.Length; i++)
            {
                name = name.Replace(invalids[i], '_');
            }
            return name.Replace(' ', '_');
        }

        /// <summary>
        /// Escapes special XML characters in the given string, replacing them with their corresponding XML entities. If the string is null or
        ///  empty, returns an empty string.
        /// </summary>
        /// <param name="unescaped">The string containing unescaped XML characters.</param>
        /// <returns>The string with XML characters escaped.</returns>
        public static string EscapeXml(string unescaped)
        {
            if (string.IsNullOrEmpty(unescaped)) return string.Empty;
            return unescaped
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        #endregion
    }
}
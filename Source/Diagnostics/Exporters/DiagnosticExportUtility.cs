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

        public static List<BodyDef> ResolveTargetBodies(DumpScope scope, BodyDef targetBody, List<BodyDef> allBodies)
        {
            if (scope == DumpScope.FullCensus)
            {
                return allBodies;
            }
            return new List<BodyDef> { targetBody };
        }

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

        public static string PadOrTruncate(string str, int width)
        {
            if (string.IsNullOrEmpty(str)) return new string(' ', width);
            if (str.Length > width) return str.Substring(0, width - 3) + "...";
            return str.PadRight(width);
        }

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
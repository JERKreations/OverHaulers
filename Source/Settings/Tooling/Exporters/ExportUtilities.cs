using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    #region 1. [DIAG-00] EXPORT SCOPE & FORMAT ENUMS

    /// <summary>
    /// [DIAG-00] Scope of diagnostic census data to compile and export.
    /// </summary>
    public enum ExportScope
    {
        SelectedSubject = 0,
        BodyDefArchetype = 1,
        FullCensus = 2
    }

    /// <summary>
    /// [DIAG-00] File format serialization target for diagnostic exports.
    /// </summary>
    public enum ExportFormat
    {
        Text = 0,
        Xml = 1
    }

    #endregion

    /// <summary>
    /// [DIAG-00] Shared formatting, census table generation, date-based folder routing, and file I/O utilities for diagnostic export management.
    /// Resides under Source/Settings/Tooling/Exporters/.
    /// </summary>
    public static class ExportUtilities
    {
        #region 2. HEADER & METADATA FORMATTING

        public static void AppendHeaderBanner(
            StringBuilder builder, 
            string title, 
            ExportScope scope, 
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
        /// Appends a summary table of species sharing the specified body archetype or all loaded species, depending on the export scope.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the summary table to.</param>
        /// <param name="scope">The scope of the export, determining which species to include.</param>
        /// <param name="targetBody">The body archetype to filter species by when scope is BodyDefArchetype.</param>
        /// <param name="allPawnThings">The list of all loaded pawn ThingDefs to consider for the summary table.</param>
        public static void AppendSpeciesSummaryTable(
            StringBuilder builder, 
            ExportScope scope, 
            BodyDef targetBody, 
            List<ThingDef> allPawnThings)
        {
            if (scope != ExportScope.FullCensus && scope != ExportScope.BodyDefArchetype) return;

            builder.AppendLine("------------------------------------------------------------------------------------------------------------------------");
            builder.AppendLine(scope == ExportScope.FullCensus 
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
                if (scope == ExportScope.BodyDefArchetype && body != targetBody) continue;

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

        private const string PrimaryRootFolderName = "DiagnosticExports";
        private const string FallbackRootFolderName = "OverHaulers_DiagnosticExports";

        private static bool modRootWriteFailed;

        /// <summary>
        /// Resolves the fallback export root under RimWorld's user save data folder, creating it if necessary.
        /// </summary>
        private static string ResolveFallbackRootDirectory()
        {
            string fallbackRoot = Path.Combine(GenFilePaths.SaveDataFolderPath, FallbackRootFolderName);
            if (!Directory.Exists(fallbackRoot))
            {
                Directory.CreateDirectory(fallbackRoot);
            }
            return fallbackRoot;
        }

        /// <summary>
        /// Resolves the root directory for exporting diagnostic exports, creating it if necessary.
        /// </summary>
        /// <returns>The full path to the root export directory for diagnostic exports.</returns>
        public static string ResolveRootExportDirectory()
        {
            if (!modRootWriteFailed)
            {
                try
                {
                    string modRoot = OverHaulers.ContentPack?.RootDir;
                    if (!string.IsNullOrEmpty(modRoot) && Directory.Exists(modRoot))
                    {
                        string rootDir = Path.Combine(modRoot, PrimaryRootFolderName);
                        if (!Directory.Exists(rootDir))
                        {
                            Directory.CreateDirectory(rootDir);
                        }
                        return rootDir;
                    }
                }
                catch (Exception ex)
                {
                    modRootWriteFailed = true;
                    OHLog.Warn("TestBench.ResolveRootExportDirectory", ex, "Failed to resolve root export directory.");
                }
            }

            return ResolveFallbackRootDirectory();
        }

        /// <summary>
        /// Resolves the export directory for the current date, creating it if necessary and falling back to
        /// the user save data folder if the primary mod directory is not writable.
        /// </summary>
        /// <returns>The full path to the export directory for the current date.</returns>
        public static string ResolveExportDirectory()
        {
            string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
            string rootDir = ResolveRootExportDirectory();
            string targetDir = Path.Combine(rootDir, dateFolder);

            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                return targetDir;
            }
            catch (Exception ex)
            {
                modRootWriteFailed = true;
                OHLog.Warn("TestBench.ResolveExportDirectory", ex, "Failed to create dated export subfolder in the primary directory.");
            }

            string fallbackDir = Path.Combine(ResolveFallbackRootDirectory(), dateFolder);
            if (!Directory.Exists(fallbackDir))
            {
                Directory.CreateDirectory(fallbackDir);
            }
            return fallbackDir;
        }

        /// <summary>
        /// Opens the export directory in the OS file explorer.
        /// Opens today's dated subfolder if it exists; otherwise opens the root DiagnosticExports directory.
        /// 100% platform-agnostic with zero native P/Invoke calls.
        /// </summary>
        public static void OpenExportDirectory()
        {
            try
            {
                string rootDir = ResolveRootExportDirectory();
                string todayDir = Path.Combine(rootDir, DateTime.Now.ToString("yyyy-MM-dd"));

                // Only open today's folder if it actually exists on disk (i.e. exports were generated today)
                string targetToOpen = Directory.Exists(todayDir) ? todayDir : rootDir;

                if (Directory.Exists(targetToOpen))
                {
                    Application.OpenURL(targetToOpen);
                }
            }
            catch (Exception ex)
            {
                OHLog.Warn("TestBench.OpenExportDirectoryFailed", ex, "Failed to open export directory.");
            }
        }

        /// <summary>
        /// Resolves the list of target bodies based on the specified export scope.
        /// </summary>
        /// <param name="scope">The scope of the export, determining which bodies to include.</param>
        /// <param name="targetBody">The specific body archetype to include when the scope is limited.</param>
        /// <param name="allBodies">The list of all available body definitions.</param>
        /// <returns>The list of body definitions to be included in the export based on the scope.</returns>
        public static List<BodyDef> ResolveTargetBodies(ExportScope scope, BodyDef targetBody, List<BodyDef> allBodies)
        {
            if (scope == ExportScope.FullCensus)
            {
                return allBodies;
            }
            return new List<BodyDef> { targetBody };
        }

        /// <summary>
        /// Ensures topologies are initialized and prepares sorted species/body census lists and fallback scope for exporters.
        /// Single Source of Truth shared across diagnostic exporters.
        /// </summary>
        public static void PrepareCensusExport(
            ref ExportScope scope,
            TestSubjectEntry subject,
            out string timestamp,
            out BodyDef targetBody,
            out List<ThingDef> allPawnThings,
            out List<BodyDef> targetBodies)
        {
            TopologyLayoutCompiler.EnsureInitialized();
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            List<ThingDef> loadedThings = DefDatabase<ThingDef>.AllDefsListForReading;
            allPawnThings = new List<ThingDef>(loadedThings != null ? loadedThings.Count : 0);
            if (loadedThings != null)
            {
                for (int i = 0; i < loadedThings.Count; i++)
                {
                    ThingDef t = loadedThings[i];
                    if (t != null && t.category == ThingCategory.Pawn && t.race?.body != null)
                    {
                        allPawnThings.Add(t);
                    }
                }
            }

            List<BodyDef> loadedBodies = DefDatabase<BodyDef>.AllDefsListForReading;
            List<BodyDef> allBodies = loadedBodies != null ? new List<BodyDef>(loadedBodies) : new List<BodyDef>();

            allPawnThings.Sort((a, b) => string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.OrdinalIgnoreCase));
            allBodies.Sort((a, b) => string.Compare(a.defName, b.defName, StringComparison.OrdinalIgnoreCase));

            targetBody = subject?.BodyDef;
            if (scope != ExportScope.FullCensus && targetBody == null)
            {
                scope = ExportScope.FullCensus;
            }

            targetBodies = ResolveTargetBodies(scope, targetBody, allBodies);
        }

        /// <summary>
        /// Resolves a string tag representing the scope of the export, used for file naming and organization.
        /// </summary>
        /// <param name="scope">The scope of the export, determining the context of the tag.</param>
        /// <param name="subject">The test subject entry, used when the scope is limited to a single subject.</param>
        /// <param name="targetBody">The specific body archetype, used when the scope is limited to a body definition.</param>
        /// <returns>A string tag representing the resolved scope for the export.</returns>
        public static string ResolveScopeTag(ExportScope scope, TestSubjectEntry subject, BodyDef targetBody)
        {
            switch (scope)
            {
                case ExportScope.SelectedSubject:
                    return $"Single_{SanitizeFileName(subject?.Label ?? "Subject")}";
                case ExportScope.BodyDefArchetype:
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
        /// <param name="scopeTag">The tag representing the scope of the export, used in the file name.</param>
        /// <param name="format">The format of the export file (text or XML).</param>
        /// <returns>The full path to the export file that was written.</returns>
        public static string WriteExportFile(
            StringBuilder builder, 
            string filePrefix, 
            string scopeTag, 
            ExportFormat format = ExportFormat.Text)
        {
            if (builder == null) return string.Empty;

            string ext = format == ExportFormat.Xml ? "xml" : "txt";
            string fileName = $"{filePrefix}_{scopeTag}_{DateTime.Now:HHmmss}.{ext}";
            string content = builder.ToString();
            string exportPath;

            try
            {
                string exportDir = ResolveExportDirectory();
                exportPath = Path.Combine(exportDir, fileName);
                File.WriteAllText(exportPath, content);
            }
            catch (Exception ex)
            {
                modRootWriteFailed = true;
                OHLog.Warn("TestBench.WriteExportFileModRootFailed", ex, "Failed to write export file to the primary export directory.");

                string fallbackDir = Path.Combine(ResolveFallbackRootDirectory(), DateTime.Now.ToString("yyyy-MM-dd"));
                if (!Directory.Exists(fallbackDir)) Directory.CreateDirectory(fallbackDir);

                string fallbackPath = Path.Combine(fallbackDir, fileName);
                File.WriteAllText(fallbackPath, content);
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
        /// Formats part classification flags (<c>ORGAN</c>, <c>RECLAIMED</c>) without temporary list allocations.
        /// </summary>
        /// <param name="isOrgan">Indicates if the part is classified as an organ.</param>
        /// <param name="wasReclaimed">Indicates if the part was reclaimed.</param>
        /// <param name="noneValue">The string to return if neither flag is set.</param>
        public static string FormatPartFlags(bool isOrgan, bool wasReclaimed, string noneValue = "-")
        {
            if (isOrgan && wasReclaimed) return "ORGAN+RECLAIMED";
            if (isOrgan) return "ORGAN";
            if (wasReclaimed) return "RECLAIMED";
            return noneValue;
        }

        /// <summary>
        /// Formats a deterministic English part-type taxonomy tag for diagnostic census exports.
        /// </summary>
        public static string FormatPartTypeTag(PartType type, BodyPartRecord part = null)
        {
            if (part != null && part.coverageAbs <= 0f && part != part.body?.corePart)
            {
                return "Apparel / Virtual Anchor";
            }

            switch (type)
            {
                case PartType.None: return "Internal Metabolic Organ";
                case PartType.HeadPart: return "Head / Cranial";
                case PartType.CorePart: return "Torso / Core Column";
                case PartType.ManipulationPart: return "Manipulation Limb";
                case PartType.MovingPart: return "Moving Limb";
                case PartType.DualLimb: return "Dual (Manip / Move) Limb";
                default: return type.ToString();
            }
        }

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
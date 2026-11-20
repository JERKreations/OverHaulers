using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-01] TOPOLOGY X-RAY EXPORTER
    /// Diagnostic exporter for canonical skeletal topologies, 1D ancestral jump matrices,
    /// and regional budget weightings across plain text and structured XML targets.
    /// Resides under Source/Settings/Tooling/Exporters/.
    /// </summary>
    public static class TopologyExportManager
    {
        #region 1. CONSTANTS & POOLED SCRATCHPADS

        private static readonly StringBuilder exportBuilder = new StringBuilder(65536);

        #endregion

        #region 2. PRIMARY EXPORT ENTRY POINT

        /// <summary>
        /// Exports a diagnostic report of canonical skeletal topologies, 1D ancestral jump matrices, and regional budget weightings.
        /// </summary>
        /// <param name="settings">The export settings to use.</param>
        /// <param name="scope">The scope determining which bodies to include in the export.</param>
        /// <param name="format">The format of the export (text or XML).</param>
        /// <param name="subject">The test subject entry, if any, to focus the export on.</param>
        /// <returns>The path to the exported topology report, or null if the export failed.</returns>
        public static string ExportTopologyData(
            Settings settings, 
            ExportScope scope, 
            ExportFormat format = ExportFormat.Text, 
            TestSubjectEntry subject = null)
        {
            exportBuilder.Clear();
            try
            {
                ExportUtilities.PrepareCensusExport(
                    ref scope,
                    subject,
                    out string timestamp,
                    out BodyDef targetBody,
                    out List<ThingDef> allPawnThings,
                    out List<BodyDef> targetBodies);

                if (format == ExportFormat.Xml)
                {
                    ExportTopologyXml(exportBuilder, settings, scope, timestamp, subject, targetBodies, allPawnThings);
                }
                else
                {
                    ExportTopologyText(exportBuilder, settings, scope, timestamp, subject, targetBodies, allPawnThings);
                }

                string scopeTag = ExportUtilities.ResolveScopeTag(scope, subject, targetBody);
                return ExportUtilities.WriteExportFile(exportBuilder, "TD", scopeTag, format);
            }
            catch (Exception ex)
            {
                OHLog.Warn("TestBench.ExportTopologyDataFailed", ex, "Failed to export topology data.");
                return null;
            }
            finally
            {
                exportBuilder.Clear();
            }
        }

        #endregion

        #region 3. TEXT SERIALIZATION ENGINE

        /// <summary>
        /// Exports the topology information in a human-readable text format.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the exported text to.</param>
        /// <param name="settings">The export settings to use.</param>
        /// <param name="scope">The scope determining which bodies to include in the export.</param>
        /// <param name="timestamp">The timestamp of the export.</param>
        /// <param name="subject">The test subject entry, if any, to focus the export on.</param>
        /// <param name="targetBodies">The list of target body definitions to include in the export.</param>
        /// <param name="allPawnThings">The list of all available pawn things to reference in the export.</param>
        private static void ExportTopologyText(
            StringBuilder builder,
            Settings settings,
            ExportScope scope,
            string timestamp,
            TestSubjectEntry subject,
            List<BodyDef> targetBodies,
            List<ThingDef> allPawnThings)
        {
            // 1. Header Banner
            ExportUtilities.AppendHeaderBanner(builder, "OVERHAULERS TOPOLOGY & ANATOMICAL CENSUS DATA", scope, timestamp, subject);

            // 2. Section 1: Species Summary Table
            ExportUtilities.AppendSpeciesSummaryTable(builder, scope, subject?.BodyDef, allPawnThings);

            // 3. Section 2: Canonical Topology X-Ray per BodyDef
            builder.AppendLine("------------------------------------------------------------------------------------------------------------------------");
            builder.AppendLine(scope == ExportScope.FullCensus 
                ? "[2. DETAILED TOPOLOGY X-RAY PER BODYDEF (CANONICAL TOP-DOWN ORDER)]" 
                : $"[2. DETAILED TOPOLOGY X-RAY FOR ARCHETYPE: {subject?.BodyDef?.defName ?? "Body"}]");
            builder.AppendLine("------------------------------------------------------------------------------------------------------------------------");

            for (int b = 0; b < targetBodies.Count; b++)
            {
                BodyDef body = targetBodies[b];
                SpeciesTopologyTemplate tmpl = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                if (tmpl == null) continue;

                builder.AppendLine();
                builder.AppendLine($"=== BODYDEF: {body.defName} ({tmpl.PartCount} Total Parts) | Core: {tmpl.CorePart?.LabelCap ?? "None"} | Head: {tmpl.HeadPart?.LabelCap ?? "None"} ===");

                PartCounts counts = tmpl.Counts;
                SolverConfiguration solverConfig = settings.ToSolverConfiguration();
                SystemicEvaluationContext.ResolveNormalizedBudgets(
                    in counts,
                    in solverConfig,
                    out float normTorso,
                    out float normArm,
                    out float normLeg);

                builder.AppendLine($"Regional Mass Budgets: Torso {normTorso:P3} | Arms {normArm:P3} | Legs {normLeg:P3}");
                builder.AppendLine();
                builder.AppendLine("Compiled Canonical Skeletal Hierarchy:");

                string defaultParent = "Root / Core";

                for (int c = 0; c < tmpl.PartCount; c++)
                {
                    int idx = tmpl.CanonicalDisplayIndices != null && tmpl.CanonicalDisplayIndices.Length == tmpl.PartCount 
                        ? tmpl.CanonicalDisplayIndices[c] 
                        : c;

                    BodyPartRecord part = tmpl.IndexedParts[idx];
                    PartType type = tmpl.PartTypes[idx];

                    float weightFactor = tmpl.StaticWeightFactors[idx];
                    string typeTag = ExportUtilities.FormatPartTypeTag(type, part);
                    string weightPct = weightFactor.ToString("P3");
                    int parentIdx = tmpl.ParentIndices[idx];
                    string parentName = parentIdx != -1 ? tmpl.IndexedParts[parentIdx].Label : defaultParent;

                    string modelTag = tmpl.WeightModels[idx] == LimbWeightModel.NotApplicable ? "" : $" | Model: {tmpl.WeightModels[idx]}";
                    string tagsSuffix = string.IsNullOrEmpty(tmpl.TagsDisplay[idx]) ? "" : $" | Tags: {tmpl.TagsDisplay[idx]}";

                    string flagsFormatted = ExportUtilities.FormatPartFlags(tmpl.IsOrgan[idx], tmpl.WasReclaimedAsAnchor[idx], string.Empty);
                    string flagsSuffix = flagsFormatted.Length > 0 ? $" | Flags: {flagsFormatted}" : "";

                    builder.AppendLine(string.Format("  - {0,-32} : {1,6} | Parent: {2,-24} | [{3}] | HP: {4,3}{5}{6}{7}",
                        part.LabelCap, weightPct, ExportUtilities.PadOrTruncate(parentName, 24), typeTag, tmpl.HitPoints[idx], modelTag, flagsSuffix, tagsSuffix));
                }
            }
        }

        #endregion

        #region 4. XML SERIALIZATION ENGINE

        /// <summary>
        /// Exports the topology information in XML format.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the exported XML to.</param>
        /// <param name="settings">The export settings to use.</param>
        /// <param name="scope">The scope determining which bodies to include in the export.</param>
        /// <param name="timestamp">The timestamp of the export.</param>
        /// <param name="subject">The test subject entry, if any, to focus the export on.</param>
        /// <param name="targetBodies">The list of target body definitions to include in the export.</param>
        /// <param name="allPawnThings">The list of all available pawn things to reference in the export.</param>
        private static void ExportTopologyXml(
            StringBuilder builder,
            Settings settings,
            ExportScope scope,
            string timestamp,
            TestSubjectEntry subject,
            List<BodyDef> targetBodies,
            List<ThingDef> allPawnThings)
        {
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine($"<OverHaulersTopologyCensus generated=\"{timestamp}\" scope=\"{scope}\">");

            if (subject != null)
            {
                builder.AppendLine($"  <ActiveSubject label=\"{ExportUtilities.EscapeXml(subject.Label)}\" raceDef=\"{subject.RaceDef?.defName ?? "None"}\" bodyDef=\"{subject.BodyDef?.defName ?? "None"}\" />");
            }

            // Macro Species Summary Table
            if (scope == ExportScope.FullCensus || scope == ExportScope.BodyDefArchetype)
            {
                builder.AppendLine("  <MacroSpeciesSummary>");
                for (int i = 0; i < allPawnThings.Count; i++)
                {
                    ThingDef thing = allPawnThings[i];
                    BodyDef body = thing.race.body;
                    if (scope == ExportScope.BodyDefArchetype && body != subject?.BodyDef) continue;

                    SpeciesTopologyTemplate tmpl = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                    if (tmpl == null) continue;

                    builder.AppendLine($"    <Species label=\"{ExportUtilities.EscapeXml(thing.LabelCap.ToString())}\" bodyDef=\"{body.defName}\" partCount=\"{tmpl.PartCount}\" core=\"{tmpl.Counts.CoreParts}\" manip=\"{tmpl.Counts.ManipulationParts}\" move=\"{tmpl.Counts.MovingParts}\" dual=\"{tmpl.Counts.DualLimbs}\" head=\"{tmpl.Counts.HeadParts}\" />");
                }
                builder.AppendLine("  </MacroSpeciesSummary>");
            }

            // Detailed BodyDef Archetypes
            builder.AppendLine("  <BodyDefArchetypes>");

            SolverConfiguration solverConfig = settings.ToSolverConfiguration();

            for (int b = 0; b < targetBodies.Count; b++)
            {
                BodyDef body = targetBodies[b];
                SpeciesTopologyTemplate tmpl = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                if (tmpl == null) continue;

                PartCounts counts = tmpl.Counts;
                SystemicEvaluationContext.ResolveNormalizedBudgets(
                    in counts,
                    in solverConfig,
                    out float normTorso,
                    out float normArm,
                    out float normLeg);

                builder.AppendLine($"    <BodyDef defName=\"{body.defName}\" partCount=\"{tmpl.PartCount}\" corePart=\"{ExportUtilities.EscapeXml(tmpl.CorePart?.def?.defName ?? "None")}\" headPart=\"{ExportUtilities.EscapeXml(tmpl.HeadPart?.def?.defName ?? "None")}\">");
                builder.AppendLine($"      <RegionalMassBudgets torsoPct=\"{normTorso:P3}\" armsPct=\"{normArm:P3}\" legsPct=\"{normLeg:P3}\" />");

                builder.AppendLine("      <SkeletalHierarchy>");
                for (int c = 0; c < tmpl.PartCount; c++)
                {
                    int idx = tmpl.CanonicalDisplayIndices != null && tmpl.CanonicalDisplayIndices.Length == tmpl.PartCount 
                        ? tmpl.CanonicalDisplayIndices[c] 
                        : c;

                    BodyPartRecord part = tmpl.IndexedParts[idx];
                    PartType type = tmpl.PartTypes[idx];
                    int parentIdx = tmpl.ParentIndices[idx];
                    string parentDef = parentIdx != -1 ? tmpl.IndexedParts[parentIdx].def.defName : "ROOT";

                    string weightModelAttr = tmpl.WeightModels[idx] == LimbWeightModel.NotApplicable ? "" : $" weightModel=\"{tmpl.WeightModels[idx]}\"";
                    string rootAttr = tmpl.RootPartIndex[idx] >= 0 ? $" rootPartIndex=\"{tmpl.RootPartIndex[idx]}\" depth=\"{tmpl.Depth[idx]}\" siblingCount=\"{tmpl.SiblingCount[idx]}\"" : "";
                    string flagsAttr = (tmpl.IsOrgan[idx] ? " isOrgan=\"true\"" : "") + (tmpl.WasReclaimedAsAnchor[idx] ? " wasReclaimedAsAnchor=\"true\"" : "");

                    builder.AppendLine($"        <Part index=\"{idx}\" label=\"{ExportUtilities.EscapeXml(part.LabelCap.ToString())}\" def=\"{part.def.defName}\" type=\"{type}\" staticWeight=\"{(tmpl.StaticWeightFactors[idx] * 100f):F3}%\" parentIndex=\"{parentIdx}\" parentDef=\"{parentDef}\" hitPoints=\"{tmpl.HitPoints[idx]}\" tags=\"{ExportUtilities.EscapeXml(tmpl.TagsDisplay[idx])}\" groups=\"{ExportUtilities.EscapeXml(tmpl.GroupsDisplay[idx])}\"{weightModelAttr}{rootAttr}{flagsAttr} />");
                }
                builder.AppendLine("      </SkeletalHierarchy>");
                builder.AppendLine("    </BodyDef>");
            }

            builder.AppendLine("  </BodyDefArchetypes>");
            builder.AppendLine("</OverHaulersTopologyCensus>");
        }

        #endregion
    }
}
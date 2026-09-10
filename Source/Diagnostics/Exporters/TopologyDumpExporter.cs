using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-01] TOPOLOGY X-RAY DUMP EXPORTER
    /// Diagnostic exporter dumping canonical skeletal topologies, 1D ancestral jump matrices,
    /// and regional budget weightings across plain text and structured XML targets.
    /// Resides under Source/Diagnostics/.
    /// </summary>
    public static class TopologyDumpExporter
    {
        #region 1. CONSTANTS & POOLED SCRATCHPADS

        private static readonly StringBuilder dumpBuilder = new StringBuilder(65536);

        #endregion

        #region 2. PRIMARY EXPORT ENTRY POINT

        /// <summary>
        /// Exports a topology dump for the specified settings, scope, format, and test subject.
        /// </summary>
        /// <param name="settings">The export settings to use.</param>
        /// <param name="scope">The scope determining which bodies to include in the dump.</param>
        /// <param name="format">The format of the export (text or XML).</param>
        /// <param name="subject">The test subject entry, if any, to focus the dump on.</param>
        /// <returns>The path to the exported topology dump file, or null if the export failed.</returns>
        public static string ExportTopologyDump(
            Settings settings, 
            DumpScope scope, 
            DumpFormat format = DumpFormat.Text, 
            TestSubjectEntry subject = null)
        {
            try
            {
                TopologyLayoutCompiler.EnsureInitialized();
                dumpBuilder.Clear();

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                List<ThingDef> allPawnThings = DefDatabase<ThingDef>.AllDefsListForReading.FindAll(t => t.category == ThingCategory.Pawn && t.race?.body != null);
                List<BodyDef> allBodies = DefDatabase<BodyDef>.AllDefsListForReading;

                allPawnThings.Sort((a, b) => string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.OrdinalIgnoreCase));
                allBodies.Sort((a, b) => string.Compare(a.defName, b.defName, StringComparison.OrdinalIgnoreCase));

                BodyDef targetBody = subject?.BodyDef;
                if (scope != DumpScope.FullCensus && targetBody == null)
                {
                    scope = DumpScope.FullCensus;
                }

                List<BodyDef> targetBodies = DiagnosticExportUtility.ResolveTargetBodies(scope, targetBody, allBodies);

                if (format == DumpFormat.Xml)
                {
                    ExportTopologyXml(dumpBuilder, settings, scope, timestamp, subject, targetBodies, allPawnThings);
                }
                else
                {
                    ExportTopologyText(dumpBuilder, settings, scope, timestamp, subject, targetBodies, allPawnThings);
                }

                string scopeTag = DiagnosticExportUtility.ResolveScopeTag(scope, subject, targetBody);
                return DiagnosticExportUtility.WriteExportFile(dumpBuilder, "TD", scopeTag, format);
            }
            catch (Exception ex)
            {
                OHLog.Solver.WarnException("ExportTopologyDump", ex);
                return null;
            }
        }

        #endregion

        #region 3. TEXT SERIALIZATION ENGINE

        /// <summary>
        /// Exports the topology information in a human-readable text format.
        /// </summary>
        /// <param name="builder">The StringBuilder to append the exported text to.</param>
        /// <param name="settings">The export settings to use.</param>
        /// <param name="scope">The scope determining which bodies to include in the dump.</param>
        /// <param name="timestamp">The timestamp of the export.</param>
        /// <param name="subject">The test subject entry, if any, to focus the dump on.</param>
        /// <param name="targetBodies">The list of target body definitions to include in the dump.</param>
        /// <param name="allPawnThings">The list of all available pawn things to reference in the dump.</param>
        private static void ExportTopologyText(
            StringBuilder builder,
            Settings settings,
            DumpScope scope,
            string timestamp,
            TestSubjectEntry subject,
            List<BodyDef> targetBodies,
            List<ThingDef> allPawnThings)
        {
            // 1. Header Banner
            DiagnosticExportUtility.AppendHeaderBanner(builder, "OVERHAULERS TOPOLOGY & ANATOMICAL CENSUS DUMP", scope, timestamp, subject);

            // 2. Section 1: Species Summary Table
            DiagnosticExportUtility.AppendSpeciesSummaryTable(builder, scope, subject?.BodyDef, allPawnThings);

            // 3. Section 2: Canonical Topology X-Ray per BodyDef
            builder.AppendLine("------------------------------------------------------------------------------------------------------------------------");
            builder.AppendLine(scope == DumpScope.FullCensus 
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

                float budgetTorso = settings.GetBudget(PartType.CorePart);
                float budgetArm = settings.GetBudget(PartType.ManipulationPart);
                float budgetLeg = settings.GetBudget(PartType.MovingPart);
                float totalBudget = budgetTorso + budgetArm + budgetLeg;
                if (totalBudget <= 0f) totalBudget = 1.0f;

                builder.AppendLine($"Regional Mass Budgets: Torso {(budgetTorso / totalBudget):P1} | Arms {(budgetArm / totalBudget):P1} | Legs {(budgetLeg / totalBudget):P1}");
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
                    string typeTag = TestBench.GetLocalizedPartTypeTag(type, part);
                    string weightPct = (weightFactor * 100f).ToString("F1") + "%";
                    int parentIdx = tmpl.ParentIndices[idx];
                    string parentName = parentIdx != -1 ? tmpl.IndexedParts[parentIdx].Label : defaultParent;

                    string modelTag = tmpl.WeightModels[idx] == LimbWeightModel.NotApplicable ? "" : $" | Model: {tmpl.WeightModels[idx]}";
                    string tagsSuffix = string.IsNullOrEmpty(tmpl.TagsDisplay[idx]) ? "" : $" | Tags: {tmpl.TagsDisplay[idx]}";

                    List<string> flags = new List<string>();
                    if (tmpl.IsOrgan[idx]) flags.Add("ORGAN");
                    if (tmpl.WasReclaimedAsAnchor[idx]) flags.Add("RECLAIMED");
                    string flagsSuffix = flags.Count > 0 ? $" | Flags: {string.Join("+", flags)}" : "";

                    builder.AppendLine(string.Format("  - {0,-32} : {1,6} | Parent: {2,-24} | [{3}] | HP: {4,3}{5}{6}{7}",
                        part.LabelCap, weightPct, DiagnosticExportUtility.PadOrTruncate(parentName, 24), typeTag, tmpl.HitPoints[idx], modelTag, flagsSuffix, tagsSuffix));
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
        /// <param name="scope">The scope determining which bodies to include in the dump.</param>
        /// <param name="timestamp">The timestamp of the export.</param>
        /// <param name="subject">The test subject entry, if any, to focus the dump on.</param>
        /// <param name="targetBodies">The list of target body definitions to include in the dump.</param>
        /// <param name="allPawnThings">The list of all available pawn things to reference in the dump.</param>
        private static void ExportTopologyXml(
            StringBuilder builder,
            Settings settings,
            DumpScope scope,
            string timestamp,
            TestSubjectEntry subject,
            List<BodyDef> targetBodies,
            List<ThingDef> allPawnThings)
        {
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine($"<OverHaulersTopologyCensus generated=\"{timestamp}\" scope=\"{scope}\">");

            if (subject != null)
            {
                builder.AppendLine($"  <ActiveSubject label=\"{DiagnosticExportUtility.EscapeXml(subject.Label)}\" raceDef=\"{subject.RaceDef?.defName ?? "None"}\" bodyDef=\"{subject.BodyDef?.defName ?? "None"}\" />");
            }

            // Macro Species Summary Table
            if (scope == DumpScope.FullCensus || scope == DumpScope.BodyDefArchetype)
            {
                builder.AppendLine("  <MacroSpeciesSummary>");
                for (int i = 0; i < allPawnThings.Count; i++)
                {
                    ThingDef thing = allPawnThings[i];
                    BodyDef body = thing.race.body;
                    if (scope == DumpScope.BodyDefArchetype && body != subject?.BodyDef) continue;

                    SpeciesTopologyTemplate tmpl = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                    if (tmpl == null) continue;

                    builder.AppendLine($"    <Species label=\"{DiagnosticExportUtility.EscapeXml(thing.LabelCap.ToString())}\" bodyDef=\"{body.defName}\" partCount=\"{tmpl.PartCount}\" core=\"{tmpl.Counts.CoreParts}\" manip=\"{tmpl.Counts.ManipulationParts}\" move=\"{tmpl.Counts.MovingParts}\" dual=\"{tmpl.Counts.DualLimbs}\" head=\"{tmpl.Counts.HeadParts}\" />");
                }
                builder.AppendLine("  </MacroSpeciesSummary>");
            }

            // Detailed BodyDef Archetypes
            builder.AppendLine("  <BodyDefArchetypes>");

            float budgetTorso = settings.GetBudget(PartType.CorePart);
            float budgetArm = settings.GetBudget(PartType.ManipulationPart);
            float budgetLeg = settings.GetBudget(PartType.MovingPart);
            float totalBudget = budgetTorso + budgetArm + budgetLeg;
            if (totalBudget <= 0f) totalBudget = 1.0f;

            for (int b = 0; b < targetBodies.Count; b++)
            {
                BodyDef body = targetBodies[b];
                SpeciesTopologyTemplate tmpl = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                if (tmpl == null) continue;

                builder.AppendLine($"    <BodyDef defName=\"{body.defName}\" partCount=\"{tmpl.PartCount}\" corePart=\"{DiagnosticExportUtility.EscapeXml(tmpl.CorePart?.def?.defName ?? "None")}\" headPart=\"{DiagnosticExportUtility.EscapeXml(tmpl.HeadPart?.def?.defName ?? "None")}\">");
                builder.AppendLine($"      <RegionalMassBudgets torsoPct=\"{(budgetTorso / totalBudget):P1}\" armsPct=\"{(budgetArm / totalBudget):P1}\" legsPct=\"{(budgetLeg / totalBudget):P1}\" />");

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

                    builder.AppendLine($"        <Part index=\"{idx}\" label=\"{DiagnosticExportUtility.EscapeXml(part.LabelCap.ToString())}\" def=\"{part.def.defName}\" type=\"{type}\" staticWeight=\"{(tmpl.StaticWeightFactors[idx] * 100f):F2}%\" parentIndex=\"{parentIdx}\" parentDef=\"{parentDef}\" hitPoints=\"{tmpl.HitPoints[idx]}\" tags=\"{DiagnosticExportUtility.EscapeXml(tmpl.TagsDisplay[idx])}\" groups=\"{DiagnosticExportUtility.EscapeXml(tmpl.GroupsDisplay[idx])}\"{weightModelAttr}{rootAttr}{flagsAttr} />");
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
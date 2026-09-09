using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-02] GROUPING & INFOCARD BREAKDOWN DUMP EXPORTER
    /// Compiles diagnostic reports of body part groupings, nested hierarchy trees,
    /// load-bearing weight shares, applied pathology/prosthetics, systemic athletic capacities,
    /// and simulated InfoCards across plain text and structured XML targets.
    /// Resides under Source/Diagnostics/.
    /// </summary>
    public static class GroupingDumpExporter
    {
        #region 1. CONSTANTS & POOLED SCRATCHPADS

        private static readonly StringBuilder dumpBuilder = new StringBuilder(131072);

        #endregion

        #region 2. PRIMARY EXPORT ENTRY POINT

        public static string ExportGroupingDump(
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
                    ExportGroupingXml(dumpBuilder, settings, scope, timestamp, subject, targetBodies, allPawnThings);
                }
                else
                {
                    ExportGroupingText(dumpBuilder, settings, scope, timestamp, subject, targetBodies, allPawnThings);
                }

                string scopeTag = DiagnosticExportUtility.ResolveScopeTag(scope, subject, targetBody);
                return DiagnosticExportUtility.WriteExportFile(dumpBuilder, "GD", scopeTag, format);
            }
            catch (Exception ex)
            {
                OHLog.Solver.WarnException("ExportGroupingDump", ex);
                return null;
            }
        }

        #endregion

        #region 3. TEXT SERIALIZATION ENGINE

        private static void ExportGroupingText(
            StringBuilder builder,
            Settings settings,
            DumpScope scope,
            string timestamp,
            TestSubjectEntry subject,
            List<BodyDef> targetBodies,
            List<ThingDef> allPawnThings)
        {
            // 1. Header Banner
            DiagnosticExportUtility.AppendHeaderBanner(builder, "OVERHAULERS BODY PART GROUPING & INFOCARD BREAKDOWN DIAGNOSTIC", scope, timestamp, subject);

            // 2. Section 1: Species Summary Table
            DiagnosticExportUtility.AppendSpeciesSummaryTable(builder, scope, subject?.BodyDef, allPawnThings);

            // 3. Section 2: Topological Hierarchy & InfoCard Trees
            for (int b = 0; b < targetBodies.Count; b++)
            {
                BodyDef body = targetBodies[b];
                SpeciesTopologyTemplate tmpl = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                if (tmpl == null) continue;

                builder.AppendLine("========================================================================================================================");
                builder.AppendLine($"BODYDEF: {body.defName} ({tmpl.PartCount} Total Parts) | Core: {tmpl.CorePart?.LabelCap ?? "None"} | Head: {tmpl.HeadPart?.LabelCap ?? "None"}");
                builder.AppendLine("========================================================================================================================");

                float budgetTorso = settings.GetBudget(PartType.CorePart);
                float budgetArm = settings.GetBudget(PartType.ManipulationPart);
                float budgetLeg = settings.GetBudget(PartType.MovingPart);
                float totalBudget = budgetTorso + budgetArm + budgetLeg;
                if (totalBudget <= 0f) totalBudget = 1.0f;

                builder.AppendLine($"Configured Regional Budgets: Torso {(budgetTorso / totalBudget):P1} | Arms {(budgetArm / totalBudget):P1} | Legs {(budgetLeg / totalBudget):P1}");
                builder.AppendLine();

                // Section 2.A: Parallel Weight Vector Breakdown Table
                builder.AppendLine("PARALLEL WEIGHT VECTOR BREAKDOWN:");
                builder.AppendLine(string.Format("  {0,-34} | {1,-18} | {2,8} | {3,8} | {4,8} | {5,10} | {6,4} | {7,-11} | {8,-10} | {9,-26} | {10}",
                    "Part Label", "PartType", "W.Core", "W.Manip", "W.Move", "Static Wt%", "HP", "WeightModel", "Flags", "Nearest Ancestor", "Tags"));
                builder.AppendLine("  " + new string('-', 160));

                for (int i = 0; i < tmpl.PartCount; i++)
                {
                    int idx = tmpl.CanonicalDisplayIndices != null && tmpl.CanonicalDisplayIndices.Length == tmpl.PartCount 
                        ? tmpl.CanonicalDisplayIndices[i] 
                        : i;

                    BodyPartRecord part = tmpl.IndexedParts[idx];
                    PartType type = tmpl.PartTypes[idx];

                    string typeName = type.ToString();
                    string wCore = tmpl.WeightCore[idx] > 0f ? tmpl.WeightCore[idx].ToString("F3") : "-";
                    string wManip = tmpl.WeightManipulation[idx] > 0f ? tmpl.WeightManipulation[idx].ToString("F3") : "-";
                    string wMove = tmpl.WeightMoving[idx] > 0f ? tmpl.WeightMoving[idx].ToString("F3") : "-";
                    string staticPct = (tmpl.StaticWeightFactors[idx] * 100f).ToString("F2") + "%";

                    int parentIdx = tmpl.ParentIndices[idx];
                    string parentName = parentIdx != -1 ? tmpl.IndexedParts[parentIdx].Label : "ROOT";

                    int ancestorOfType = tmpl.GetNearestAncestor(idx, type);
                    string ancestorStr = ancestorOfType != -1 ? tmpl.IndexedParts[ancestorOfType].Label : "None";

                    string weightModelStr = tmpl.WeightModels[idx] == LimbWeightModel.NotApplicable ? "-" : tmpl.WeightModels[idx].ToString();

                    List<string> flags = new List<string>();
                    if (tmpl.IsOrgan[idx]) flags.Add("ORGAN");
                    if (tmpl.WasReclaimedAsAnchor[idx]) flags.Add("RECLAIMED");
                    string flagsStr = flags.Count > 0 ? string.Join("+", flags) : "-";

                    builder.AppendLine(string.Format("  {0,-34} | {1,-18} | {2,8} | {3,8} | {4,8} | {5,10} | {6,4} | {7,-11} | {8,-10} | {9,-26} | {10}",
                        DiagnosticExportUtility.PadOrTruncate(part.LabelCap.ToString(), 34), typeName, wCore, wManip, wMove, staticPct, tmpl.HitPoints[idx], weightModelStr, flagsStr, DiagnosticExportUtility.PadOrTruncate(ancestorStr, 26), tmpl.TagsDisplay[idx]));
                }

                builder.AppendLine();

                // Section 2.B: Simulated Model Population (Isolated from Active UI Session)
                TestSubjectEntry evalSubject = ResolveEvaluationSubject(scope, subject, body, allPawnThings);
                MassCapacityModel massModel = ResolveAndPopulateDiagnosticModel(evalSubject, settings, out float cleanBaseline, out AnatomicalWorkspace workspace);

                try
                {
                    // 1. Caravan Mass Capacity Progression Readout
                    float finalMass = Mathf.Max(0.01f, cleanBaseline + massModel.Offset);
                    builder.AppendLine("CARAVAN MASS CAPACITY PROGRESSION:");
                    builder.AppendLine($"  Baseline: {cleanBaseline.ToStringMass()}  ➔  Final: {finalMass.ToStringMass()} (Offset: {massModel.Offset.ToStringMassOffset()}, Total Rating: {massModel.TotalMultiplier:F2}x)");
                    builder.AppendLine();

                    // 2. Athletic Capacities Readout
                    builder.AppendLine("ATHLETIC CAPACITIES:");
                    builder.AppendLine($"  - Breathing:     {(massModel.CapacityLevels[0] * 100f):F0}%");
                    builder.AppendLine($"  - Blood Pumping: {(massModel.CapacityLevels[1] * 100f):F0}%");
                    builder.AppendLine($"  - Moving:        {(massModel.CapacityLevels[2] * 100f):F0}%");
                    builder.AppendLine($"  - Manipulation:  {(massModel.CapacityLevels[3] * 100f):F0}%");
                    builder.AppendLine($"  - Consciousness: {(massModel.CapacityLevels[4] * 100f):F0}%");
                    builder.AppendLine();

                    // 3. Athletic Impacts / Ailments Readout
                    builder.AppendLine("ATHLETIC IMPACTS & SYSTEMIC CONDITIONS:");
                    if (massModel.Ailments.Count == 0)
                    {
                        builder.AppendLine("  - None");
                    }
                    else
                    {
                        for (int a = 0; a < massModel.Ailments.Count; a++)
                        {
                            builder.AppendLine($"  - {massModel.Ailments[a]}");
                        }
                    }
                    builder.AppendLine();

                    // 4. Simulated InfoCard View Model Nesting Tree
                    builder.AppendLine("INFOCARD VIEW MODEL NESTING TREE (As Rendered in UI):");

                    if (massModel.EvaluatedParts.Count == 0)
                    {
                        builder.AppendLine("  [WARNING: No visual group nodes generated for this BodyDef!]");
                    }
                    else
                    {
                        for (int g = 0; g < massModel.EvaluatedParts.Count; g++)
                        {
                            PartViewNode groupNode = massModel.EvaluatedParts[g];
                            builder.AppendLine($"  + [{groupNode.Label}] (Subtotal: {groupNode.TotalOffset.ToStringMassOffset()}, P: {groupNode.ProstheticOffset.ToStringMassOffset()}, A: {groupNode.AthleticOffset.ToStringMassOffset()}, I: {groupNode.HealthOffset.ToStringMassOffset()})");
                            DumpSubPartsRecursive(builder, groupNode.SubParts, 2);
                        }
                    }
                }
                finally
                {
                    workspace?.Clear();
                }

                builder.AppendLine();

                // Section 2.C: Automated Anomaly Scanner
                builder.AppendLine("TOPOLOGICAL ANOMALY AUDIT:");
                List<string> anomalies = DetectTopologicalAnomalies(tmpl, body, massModel);
                if (anomalies.Count == 0)
                {
                    builder.AppendLine("  ✓ [CLEAN] No structural anomalies or orphaned weight branches detected.");
                }
                else
                {
                    for (int a = 0; a < anomalies.Count; a++)
                    {
                        builder.AppendLine($"  ⚠ [ANOMALY] {anomalies[a]}");
                    }
                }

                builder.AppendLine();
            }
        }

        #endregion

        #region 4. XML SERIALIZATION ENGINE

        private static void ExportGroupingXml(
            StringBuilder builder,
            Settings settings,
            DumpScope scope,
            string timestamp,
            TestSubjectEntry subject,
            List<BodyDef> targetBodies,
            List<ThingDef> allPawnThings)
        {
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine($"<OverHaulersGroupingCensus generated=\"{timestamp}\" scope=\"{scope}\">");

            if (subject != null)
            {
                builder.AppendLine($"  <ActiveSubject label=\"{DiagnosticExportUtility.EscapeXml(subject.Label)}\" raceDef=\"{subject.RaceDef?.defName ?? "None"}\" bodyDef=\"{subject.BodyDef?.defName ?? "None"}\" isLivePawn=\"{subject.IsLivePawn}\" />");
            }

            builder.AppendLine("  <Archetypes>");

            for (int b = 0; b < targetBodies.Count; b++)
            {
                BodyDef body = targetBodies[b];
                SpeciesTopologyTemplate tmpl = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                if (tmpl == null) continue;

                builder.AppendLine($"    <BodyDef defName=\"{body.defName}\" partCount=\"{tmpl.PartCount}\" corePart=\"{DiagnosticExportUtility.EscapeXml(tmpl.CorePart?.def?.defName ?? "None")}\" headPart=\"{DiagnosticExportUtility.EscapeXml(tmpl.HeadPart?.def?.defName ?? "None")}\">");

                // Parallel Weight Vector Breakdown
                builder.AppendLine("      <ParallelWeightBreakdown>");
                for (int i = 0; i < tmpl.PartCount; i++)
                {
                    int idx = tmpl.CanonicalDisplayIndices != null && tmpl.CanonicalDisplayIndices.Length == tmpl.PartCount ? tmpl.CanonicalDisplayIndices[i] : i;
                    BodyPartRecord part = tmpl.IndexedParts[idx];
                    PartType type = tmpl.PartTypes[idx];
                    int parentIdx = tmpl.ParentIndices[idx];

                    string weightModelAttr = tmpl.WeightModels[idx] == LimbWeightModel.NotApplicable ? "" : $" weightModel=\"{tmpl.WeightModels[idx]}\"";
                    string rootAttr = tmpl.RootPartIndex[idx] >= 0 ? $" rootPartIndex=\"{tmpl.RootPartIndex[idx]}\" depth=\"{tmpl.Depth[idx]}\" siblingCount=\"{tmpl.SiblingCount[idx]}\"" : "";
                    string flagsAttr = (tmpl.IsOrgan[idx] ? " isOrgan=\"true\"" : "") + (tmpl.WasReclaimedAsAnchor[idx] ? " wasReclaimedAsAnchor=\"true\"" : "");

                    builder.AppendLine($"        <Part index=\"{idx}\" label=\"{DiagnosticExportUtility.EscapeXml(part.LabelCap.ToString())}\" def=\"{part.def.defName}\" type=\"{type}\" weightCore=\"{tmpl.WeightCore[idx]:F3}\" weightManip=\"{tmpl.WeightManipulation[idx]:F3}\" weightMove=\"{tmpl.WeightMoving[idx]:F3}\" staticWeightPct=\"{(tmpl.StaticWeightFactors[idx] * 100f):F2}%\" parentIndex=\"{parentIdx}\" hitPoints=\"{tmpl.HitPoints[idx]}\" tags=\"{DiagnosticExportUtility.EscapeXml(tmpl.TagsDisplay[idx])}\" groups=\"{DiagnosticExportUtility.EscapeXml(tmpl.GroupsDisplay[idx])}\"{weightModelAttr}{rootAttr}{flagsAttr} />");
                }
                builder.AppendLine("      </ParallelWeightBreakdown>");

                // Simulated Progression, Capacities & InfoCard View Model Tree
                TestSubjectEntry evalSubject = ResolveEvaluationSubject(scope, subject, body, allPawnThings);
                MassCapacityModel massModel = ResolveAndPopulateDiagnosticModel(evalSubject, settings, out float cleanBaseline, out AnatomicalWorkspace workspace);

                try
                {
                    float finalMass = Mathf.Max(0.01f, cleanBaseline + massModel.Offset);
                    builder.AppendLine($"      <CaravanMassProgression baselineKg=\"{cleanBaseline:F2}\" solvedOffsetKg=\"{massModel.Offset:F2}\" finalMassKg=\"{finalMass:F2}\" totalMultiplier=\"{massModel.TotalMultiplier:F2}\" />");

                    builder.AppendLine($"      <AthleticCapacities breathing=\"{massModel.CapacityLevels[0]:F2}\" bloodPumping=\"{massModel.CapacityLevels[1]:F2}\" moving=\"{massModel.CapacityLevels[2]:F2}\" manipulation=\"{massModel.CapacityLevels[3]:F2}\" consciousness=\"{massModel.CapacityLevels[4]:F2}\" />");

                    builder.AppendLine($"      <AthleticImpacts count=\"{massModel.Ailments.Count}\">");
                    for (int a = 0; a < massModel.Ailments.Count; a++)
                    {
                        builder.AppendLine($"        <Impact label=\"{DiagnosticExportUtility.EscapeXml(massModel.Ailments[a])}\" />");
                    }
                    builder.AppendLine("      </AthleticImpacts>");

                    builder.AppendLine("      <InfoCardViewModelTree>");
                    for (int g = 0; g < massModel.EvaluatedParts.Count; g++)
                    {
                        PartViewNode groupNode = massModel.EvaluatedParts[g];
                        builder.AppendLine($"        <GroupNode label=\"{DiagnosticExportUtility.EscapeXml(groupNode.Label)}\" subtotalKg=\"{groupNode.TotalOffset:F2}\" prostheticKg=\"{groupNode.ProstheticOffset:F2}\" athleticKg=\"{groupNode.AthleticOffset:F2}\" healthKg=\"{groupNode.HealthOffset:F2}\">");
                        DumpSubPartsXmlRecursive(builder, groupNode.SubParts, 10);
                        builder.AppendLine("        </GroupNode>");
                    }
                    builder.AppendLine("      </InfoCardViewModelTree>");
                }
                finally
                {
                    workspace?.Clear();
                }

                // Anomalies
                List<string> anomalies = DetectTopologicalAnomalies(tmpl, body, massModel);
                builder.AppendLine($"      <TopologicalAnomalies status=\"{(anomalies.Count == 0 ? "Clean" : "AnomaliesDetected")}\" count=\"{anomalies.Count}\">");
                for (int a = 0; a < anomalies.Count; a++)
                {
                    builder.AppendLine($"        <Anomaly description=\"{DiagnosticExportUtility.EscapeXml(anomalies[a])}\" />");
                }
                builder.AppendLine("      </TopologicalAnomalies>");

                builder.AppendLine("    </BodyDef>");
            }

            builder.AppendLine("  </Archetypes>");
            builder.AppendLine("</OverHaulersGroupingCensus>");
        }

        #endregion

        #region 5. ISOLATED DIAGNOSTIC INGRESS COMPILATION HELPER

        /// <summary>
        /// Populates a MassCapacityModel for diagnostic exports.
        /// Protects the active interactive TestBench session by evaluating non-active subjects inside an isolated ephemeral harness.
        /// </summary>
        private static MassCapacityModel ResolveAndPopulateDiagnosticModel(
            TestSubjectEntry evalSubject,
            Settings settings,
            out float cleanBaseline,
            out AnatomicalWorkspace activeWorkspace)
        {
            activeWorkspace = null;
            MassCapacityModel massModel = new MassCapacityModel();

            IAnatomicalDataSource dataSource;
            SandboxPawnHarness isolatedHarness = null;

            // BREAKPOINT ANCHOR: Harness Isolation Guard
            // If evaluating the currently selected test subject, use the active harness without mutating it.
            // If evaluating an unselected census species, instantiate a temporary ephemeral harness to avoid wiping UI modifications.
            if (evalSubject != null && evalSubject == TestBench.ActiveSubject && TestBench.Harness.IsValid)
            {
                dataSource = new PawnAnatomicalSource(TestBench.Harness.SandboxPawn);
            }
            else
            {
                isolatedHarness = new SandboxPawnHarness();
                isolatedHarness.BindSubject(evalSubject);
                dataSource = new PawnAnatomicalSource(isolatedHarness.SandboxPawn);
            }

            try
            {
                cleanBaseline = dataSource.ResolveBaselineCapacity();
                float solvedOffset = MassCapacitySolver.SolveMassCapacityOffset(
                    dataSource, cleanBaseline, out cleanBaseline, settings, true, out activeWorkspace);

                ViewProjection.RebuildDetailedModel_Internal(dataSource, massModel, solvedOffset, cleanBaseline, activeWorkspace);
            }
            finally
            {
                isolatedHarness?.Dispose();
            }

            return massModel;
        }

        #endregion

        #region 6. TREE PARSING & ANOMALY SCANNER HELPERS

        private static void DumpSubPartsRecursive(StringBuilder builder, List<PartViewNode> subParts, int depth)
        {
            if (subParts == null || subParts.Count == 0) return;

            string indent = new string(' ', depth * 2);

            for (int i = 0; i < subParts.Count; i++)
            {
                PartViewNode node = subParts[i];
                string organTag = node.IsOrgan ? " [ORGAN]" : "";
                string weightStr = node.ProportionalWeight > 0f ? $" (Weight: {(node.ProportionalWeight * 100f):F1}%)" : "";

                string conditionTag = BuildTextConditionTag(node);
                string channelDetails = BuildTextChannelDetails(node);

                builder.AppendLine($"  {indent}- {node.Label}{organTag}{weightStr}{conditionTag}: {node.TotalOffset.ToStringMassOffset()}{channelDetails}");

                if (node.SubParts != null && node.SubParts.Count > 0)
                {
                    DumpSubPartsRecursive(builder, node.SubParts, depth + 1);
                }
            }
        }

        private static string BuildTextConditionTag(PartViewNode node)
        {
            if (node == null) return string.Empty;

            List<string> tags = new List<string>();

            if (node.HasProsthetic)
            {
                string pName = !string.IsNullOrEmpty(node.ProstheticName) ? node.ProstheticName : "Prosthetic";
                string prefix = node.IsInherited ? "Inherited " : "";
                tags.Add($"{prefix}{pName} ({(node.EfficiencyRating * 100f):F0}%)");
            }
            else if (node.IsMissing)
            {
                if (node.IsNeutralized)
                {
                    tags.Add("Missing, Ignored");
                }
                else
                {
                    tags.Add("Missing (0% HP)");
                }
            }
            else if (node.HealthFraction < 1.0f - SettingsDefaults.EfficiencyEpsilon)
            {
                tags.Add($"HP: {(node.HealthFraction * 100f):F0}%");
            }

            if (!string.IsNullOrEmpty(node.AthleticImplantName) && node.AthleticImplantName != node.ProstheticName)
            {
                tags.Add($"Implant: {node.AthleticImplantName}");
            }

            if (!string.IsNullOrEmpty(node.LocalAilmentName))
            {
                tags.Add($"Ailment: {node.LocalAilmentName}");
            }

            if (tags.Count == 0) return string.Empty;
            return " [" + string.Join(", ", tags) + "]";
        }

        private static string BuildTextChannelDetails(PartViewNode node)
        {
            if (node == null) return string.Empty;

            bool hasProsthetic = Math.Abs(node.ProstheticOffset) >= SettingsDefaults.EfficiencyEpsilon;
            bool hasAthletic = Math.Abs(node.AthleticOffset) >= SettingsDefaults.EfficiencyEpsilon;
            bool hasHealth = Math.Abs(node.HealthOffset) >= SettingsDefaults.EfficiencyEpsilon;

            if (!hasProsthetic && !hasAthletic && !hasHealth) return string.Empty;

            List<string> channelBreakdowns = new List<string>();
            if (hasProsthetic) channelBreakdowns.Add($"P: {node.ProstheticOffset.ToStringMassOffset()}");
            if (hasAthletic) channelBreakdowns.Add($"A: {node.AthleticOffset.ToStringMassOffset()}");
            if (hasHealth) channelBreakdowns.Add($"I: {node.HealthOffset.ToStringMassOffset()}");

            return $" ({string.Join(", ", channelBreakdowns)})";
        }

        private static void DumpSubPartsXmlRecursive(StringBuilder builder, List<PartViewNode> subParts, int indentSpaces)
        {
            if (subParts == null || subParts.Count == 0) return;
            string indent = new string(' ', indentSpaces);

            for (int i = 0; i < subParts.Count; i++)
            {
                PartViewNode node = subParts[i];
                string defName = node.Record?.def?.defName ?? "None";
                bool hasChildren = node.SubParts != null && node.SubParts.Count > 0;

                StringBuilder attrBuilder = new StringBuilder();
                attrBuilder.Append($"label=\"{DiagnosticExportUtility.EscapeXml(node.Label)}\" ");
                attrBuilder.Append($"def=\"{defName}\" ");
                attrBuilder.Append($"isOrgan=\"{node.IsOrgan}\" ");
                attrBuilder.Append($"weightPct=\"{(node.ProportionalWeight * 100f):F1}%\" ");
                attrBuilder.Append($"totalOffsetKg=\"{node.TotalOffset:F2}\" ");

                if (node.HasProsthetic)
                {
                    attrBuilder.Append("hasProsthetic=\"true\" ");
                    if (!string.IsNullOrEmpty(node.ProstheticName)) attrBuilder.Append($"prostheticName=\"{DiagnosticExportUtility.EscapeXml(node.ProstheticName)}\" ");
                    attrBuilder.Append($"efficiencyRating=\"{node.EfficiencyRating:F2}\" ");
                    if (node.IsInherited) attrBuilder.Append("isInherited=\"true\" ");
                }

                if (node.IsMissing)
                {
                    attrBuilder.Append("isMissing=\"true\" ");
                    if (node.IsNeutralized) attrBuilder.Append("isNeutralized=\"true\" ");
                }
                else if (node.HealthFraction < 1.0f - SettingsDefaults.EfficiencyEpsilon)
                {
                    attrBuilder.Append($"healthFraction=\"{node.HealthFraction:F2}\" ");
                }

                if (!string.IsNullOrEmpty(node.AthleticImplantName))
                {
                    attrBuilder.Append($"athleticImplant=\"{DiagnosticExportUtility.EscapeXml(node.AthleticImplantName)}\" ");
                }

                if (!string.IsNullOrEmpty(node.LocalAilmentName))
                {
                    attrBuilder.Append($"ailment=\"{DiagnosticExportUtility.EscapeXml(node.LocalAilmentName)}\" ");
                }

                if (Math.Abs(node.ProstheticOffset) >= SettingsDefaults.EfficiencyEpsilon)
                {
                    attrBuilder.Append($"prostheticOffsetKg=\"{node.ProstheticOffset:F2}\" ");
                }
                if (Math.Abs(node.AthleticOffset) >= SettingsDefaults.EfficiencyEpsilon)
                {
                    attrBuilder.Append($"athleticOffsetKg=\"{node.AthleticOffset:F2}\" ");
                }
                if (Math.Abs(node.HealthOffset) >= SettingsDefaults.EfficiencyEpsilon)
                {
                    attrBuilder.Append($"healthOffsetKg=\"{node.HealthOffset:F2}\" ");
                }

                string attributes = attrBuilder.ToString().TrimEnd();

                if (hasChildren)
                {
                    builder.AppendLine($"{indent}<PartNode {attributes}>");
                    DumpSubPartsXmlRecursive(builder, node.SubParts, indentSpaces + 2);
                    builder.AppendLine($"{indent}</PartNode>");
                }
                else
                {
                    builder.AppendLine($"{indent}<PartNode {attributes} />");
                }
            }
        }

        private static TestSubjectEntry ResolveEvaluationSubject(
            DumpScope scope, 
            TestSubjectEntry subject, 
            BodyDef body, 
            List<ThingDef> allPawnThings)
        {
            if (scope == DumpScope.SelectedSubject && subject != null)
            {
                return subject;
            }

            ThingDef sampleThing = allPawnThings.Find(t => t.race?.body == body) ?? ThingDefOf.Human;
            return new TestSubjectEntry(
                sampleThing.LabelCap.ToString(),
                body,
                sampleThing,
                sampleThing.race?.baseBodySize > 0f ? sampleThing.race.baseBodySize : 1.0f,
                sampleThing.modContentPack,
                sampleThing.modContentPack?.Name ?? "Core",
                "Diagnostic", // Replaced FleshTypeDef with string
                "Diagnostic"  // Replaced BiologicalClassName with string
            );
        }

        private static List<string> DetectTopologicalAnomalies(SpeciesTopologyTemplate template, BodyDef body, MassCapacityModel model)
        {
            List<string> anomalies = new List<string>();

            for (int i = 0; i < template.PartCount; i++)
            {
                PartType type = template.PartTypes[i];
                if ((type == PartType.ManipulationPart || type == PartType.MovingPart || type == PartType.DualLimb) &&
                    template.StaticWeightFactors[i] <= 0f)
                {
                    anomalies.Add($"Limb part '{template.IndexedParts[i].Label}' (Type: {type}) has 0.00% static weight allocation.");
                }

                if (template.WasReclaimedAsAnchor[i])
                {
                    anomalies.Add($"Part '{template.IndexedParts[i].Label}' was reclassified from an ancestry-inherited limb type to CorePart via the anchor-bridge heuristic (it carries no explicit limb tag of its own) - verify this is a genuine connective/anchor bone and not a mistagged limb segment.");
                }
            }

            if (model?.EvaluatedParts != null)
            {
                for (int g = 0; g < model.EvaluatedParts.Count; g++)
                {
                    PartViewNode group = model.EvaluatedParts[g];
                    if (group.SubParts != null && group.SubParts.Count > 10)
                    {
                        anomalies.Add($"Group '{group.Label}' has {group.SubParts.Count} top-level nodes; child limbs may have failed to nest under parent joints.");
                    }
                }
            }

            for (int i = 0; i < template.PartCount; i++)
            {
                int parentIdx = template.ParentIndices[i];
                if (parentIdx >= template.PartCount || parentIdx < -1)
                {
                    anomalies.Add($"Part '{template.IndexedParts[i].Label}' has out-of-bounds parent index ({parentIdx}).");
                }
            }

            return anomalies;
        }

        #endregion
    }
}
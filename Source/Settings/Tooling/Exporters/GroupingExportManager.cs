using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-02] GROUPING & INFOCARD BREAKDOWN EXPORTER
    /// Compiles diagnostic reports of body part groupings, nested hierarchy trees,
    /// load-bearing weight shares, applied pathology/prosthetics, systemic athletic capacities,
    /// and simulated InfoCards across plain text and structured XML targets.
    /// Resides under Source/Settings/Tooling/Exporters/.
    /// </summary>
    public static class GroupingExportManager
    {
        #region 1. CONSTANTS & POOLED SCRATCHPADS

        private static readonly StringBuilder exportBuilder = new StringBuilder(131072);

        #endregion

        #region 2. PRIMARY EXPORT ENTRY POINT

        /// <summary>
        /// Exports a diagnostic report of body part groupings, nested hierarchy trees, load-bearing weight shares, applied pathology/prosthetics,
        ///  systemic athletic capacities, and simulated InfoCards for the specified subject or scope.
        /// </summary>
        /// <param name="settings">The export settings to be used.</param>
        /// <param name="scope">The scope of the export (e.g., full census or specific subject).</param>
        /// <param name="format">The format of the export file (text or XML).</param>
        /// <param name="subject">The specific subject for which the export is generated, if applicable.</param>
        /// <returns>The full path to the export file that was written.</returns>
        public static string ExportGroupingData(
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
                    ExportGroupingXml(exportBuilder, settings, scope, timestamp, subject, targetBodies, allPawnThings);
                }
                else
                {
                    ExportGroupingText(exportBuilder, settings, scope, timestamp, subject, targetBodies, allPawnThings);
                }

                string scopeTag = ExportUtilities.ResolveScopeTag(scope, subject, targetBody);
                return ExportUtilities.WriteExportFile(exportBuilder, "GD", scopeTag, format);
            }
            catch (Exception ex)
            {
                OHLog.Warn("TestBench.ExportGroupingDataFailed", ex, "Failed to export grouping data.");
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
        /// Exports the body part grouping and related diagnostic information in plain text format.
        /// </summary>
        /// <param name="builder">The StringBuilder used to construct the text output.</param>
        /// <param name="settings">The export settings to be used.</param>
        /// <param name="scope">The scope of the export (e.g., full census or specific subject).</param>
        /// <param name="timestamp">The timestamp of the export.</param>
        /// <param name="subject">The specific subject for which the export is generated, if applicable.</param>
        /// <param name="targetBodies">The list of target body definitions to include in the export.</param>
        /// <param name="allPawnThings">The list of all pawn things relevant to the export.</param>
        private static void ExportGroupingText(
            StringBuilder builder,
            Settings settings,
            ExportScope scope,
            string timestamp,
            TestSubjectEntry subject,
            List<BodyDef> targetBodies,
            List<ThingDef> allPawnThings)
        {
            // 1. Header Banner
            ExportUtilities.AppendHeaderBanner(builder, "OVERHAULERS BODY PART GROUPING & INFOCARD BREAKDOWN DIAGNOSTIC", scope, timestamp, subject);

            // 2. Section 1: Species Summary Table
            ExportUtilities.AppendSpeciesSummaryTable(builder, scope, subject?.BodyDef, allPawnThings);

            // 3. Section 2: Topological Hierarchy & InfoCard Trees
            SolverConfiguration solverConfig = settings.ToSolverConfiguration();
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

                builder.AppendLine("========================================================================================================================");
                builder.AppendLine($"BODYDEF: {body.defName} ({tmpl.PartCount} Total Parts) | Core: {tmpl.CorePart?.LabelCap ?? "None"} | Head: {tmpl.HeadPart?.LabelCap ?? "None"}");
                builder.AppendLine("========================================================================================================================");

                PartCounts counts = tmpl.Counts;
                SystemicEvaluationContext.ResolveNormalizedBudgets(
                    in counts,
                    in solverConfig,
                    out float normTorso,
                    out float normArm,
                    out float normLeg);

                builder.AppendLine($"Configured Regional Budgets: Torso {(budgetTorso / totalBudget):P3} | Arms {(budgetArm / totalBudget):P3} | Legs {(budgetLeg / totalBudget):P3}");
                builder.AppendLine($"Effective Regional Budgets:  Torso {normTorso:P3} | Arms {normArm:P3} | Legs {normLeg:P3}");
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
                    string staticPct = (tmpl.StaticWeightFactors[idx] * 100f).ToString("F3") + "%";

                    int ancestorOfType = tmpl.GetNearestAncestor(idx, type);
                    string ancestorStr = ancestorOfType != -1 ? tmpl.IndexedParts[ancestorOfType].Label : "None";

                    string weightModelStr = tmpl.WeightModels[idx] == LimbWeightModel.NotApplicable ? "-" : tmpl.WeightModels[idx].ToString();
                    string flagsStr = ExportUtilities.FormatPartFlags(tmpl.IsOrgan[idx], tmpl.WasReclaimedAsAnchor[idx]);

                    builder.AppendLine(string.Format("  {0,-34} | {1,-18} | {2,8} | {3,8} | {4,8} | {5,10} | {6,4} | {7,-11} | {8,-10} | {9,-26} | {10}",
                        ExportUtilities.PadOrTruncate(part.LabelCap.ToString(), 34), typeName, wCore, wManip, wMove, staticPct, tmpl.HitPoints[idx], weightModelStr, flagsStr, ExportUtilities.PadOrTruncate(ancestorStr, 26), tmpl.TagsDisplay[idx]));
                }

                builder.AppendLine();

                // Section 2.B: Simulated Model Population (Isolated from Active UI Session)
                TestSubjectEntry evalSubject = ResolveEvaluationSubject(scope, subject, body, allPawnThings);
                MassCapacityModel massModel = ResolveAndPopulateDiagnosticModel(evalSubject, out float cleanBaseline);

                // 1. Caravan Mass Capacity Progression Readout
                float finalMass = massModel.FinalCapacity;
                builder.AppendLine("CARAVAN MASS CAPACITY PROGRESSION:");
                builder.AppendLine($"  Baseline: {cleanBaseline.ToStringMass()}  ➔  Final: {finalMass.ToStringMass()} (Offset: {massModel.Offset.ToStringMassOffset()}, Total Rating: {massModel.TotalMultiplier:F2}x)");
                builder.AppendLine();

                // 2. Athletic Capacities Readout
                builder.AppendLine("ATHLETIC CAPACITIES:");
                builder.AppendLine($"  - Breathing:     {(massModel.CapacityLevels[0] * 100f):F0}%");
                builder.AppendLine($"  - Blood Pumping: {(massModel.CapacityLevels[1] * 100f):F0}%");
                builder.AppendLine($"  - Moving:        {(massModel.CapacityLevels[2] * 100f):F0}%");
                builder.AppendLine($"  - Manipulation:  {(massModel.CapacityLevels[3] * 100f):F0}%");
                builder.AppendLine();

                // 3. Athletic Impacts / Ailments Readout
                bool hasConsciousnessImpact = Math.Abs(massModel.ConsciousnessLevel - 1.0f) >= PawnDataRegistry.EfficiencyEpsilon;
                builder.AppendLine("ATHLETIC IMPACTS & SYSTEMIC CONDITIONS:");
                if (!hasConsciousnessImpact && massModel.Ailments.Count == 0)
                {
                    builder.AppendLine("  - None");
                }
                else
                {
                    if (hasConsciousnessImpact)
                    {
                        builder.AppendLine($"  - Consciousness: {(massModel.ConsciousnessLevel * 100f):F0}%");
                    }
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
                        ExportSubPartsRecursive(builder, groupNode.SubParts, 2);
                    }
                }

                builder.AppendLine();

                // Section 2.C: Automated Anomaly Scanner
                builder.AppendLine("TOPOLOGICAL ANOMALY AUDIT:");
                List<string> anomalies = DetectTopologicalAnomalies(tmpl, massModel);
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

        /// <summary>
        /// Exports the body part grouping and related diagnostic information in XML format.
        /// </summary>
        /// <param name="builder">The StringBuilder used to construct the XML output.</param>
        /// <param name="settings">The export settings to be used.</param>
        /// <param name="scope">The scope of the export (e.g., full census or specific subject).</param>
        /// <param name="timestamp">The timestamp of the export.</param>
        /// <param name="subject">The specific subject for which the export is generated, if applicable.</param>
        /// <param name="targetBodies">The list of target body definitions to include in the export.</param>
        /// <param name="allPawnThings">The list of all pawn things relevant to the export.</param>
        private static void ExportGroupingXml(
            StringBuilder builder,
            Settings settings,
            ExportScope scope,
            string timestamp,
            TestSubjectEntry subject,
            List<BodyDef> targetBodies,
            List<ThingDef> allPawnThings)
        {
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine($"<OverHaulersGroupingCensus generated=\"{timestamp}\" scope=\"{scope}\">");

            if (subject != null)
            {
                builder.AppendLine($"  <ActiveSubject label=\"{ExportUtilities.EscapeXml(subject.Label)}\" raceDef=\"{ExportUtilities.EscapeXml(subject.RaceDef?.defName ?? "None")}\" bodyDef=\"{ExportUtilities.EscapeXml(subject.BodyDef?.defName ?? "None")}\" isLivePawn=\"{subject.IsLivePawn}\" />");
            }

            builder.AppendLine("  <Archetypes>");
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

                builder.AppendLine($"    <BodyDef defName=\"{ExportUtilities.EscapeXml(body.defName)}\" partCount=\"{tmpl.PartCount}\" corePart=\"{ExportUtilities.EscapeXml(tmpl.CorePart?.def?.defName ?? "None")}\" headPart=\"{ExportUtilities.EscapeXml(tmpl.HeadPart?.def?.defName ?? "None")}\">");
                builder.AppendLine($"      <RegionalMassBudgets torsoPct=\"{normTorso:P3}\" armsPct=\"{normArm:P3}\" legsPct=\"{normLeg:P3}\" />");

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

                    builder.AppendLine($"        <Part index=\"{idx}\" label=\"{ExportUtilities.EscapeXml(part.LabelCap.ToString())}\" def=\"{ExportUtilities.EscapeXml(part.def.defName)}\" type=\"{type}\" weightCore=\"{tmpl.WeightCore[idx]:F3}\" weightManip=\"{tmpl.WeightManipulation[idx]:F3}\" weightMove=\"{tmpl.WeightMoving[idx]:F3}\" staticWeightPct=\"{(tmpl.StaticWeightFactors[idx] * 100f):F3}%\" parentIndex=\"{parentIdx}\" hitPoints=\"{tmpl.HitPoints[idx]}\" tags=\"{ExportUtilities.EscapeXml(tmpl.TagsDisplay[idx])}\" groups=\"{ExportUtilities.EscapeXml(tmpl.GroupsDisplay[idx])}\"{weightModelAttr}{rootAttr}{flagsAttr} />");
                }
                builder.AppendLine("      </ParallelWeightBreakdown>");

                // Simulated Progression, Capacities & InfoCard View Model Tree
                TestSubjectEntry evalSubject = ResolveEvaluationSubject(scope, subject, body, allPawnThings);
                MassCapacityModel massModel = ResolveAndPopulateDiagnosticModel(evalSubject, out float cleanBaseline);

                float finalMass = massModel.FinalCapacity;
                builder.AppendLine($"      <CaravanMassProgression baselineKg=\"{cleanBaseline.ToString("R", CultureInfo.InvariantCulture)}\" solvedOffsetKg=\"{massModel.Offset.ToString("R", CultureInfo.InvariantCulture)}\" finalMassKg=\"{finalMass.ToString("R", CultureInfo.InvariantCulture)}\" totalMultiplier=\"{massModel.TotalMultiplier.ToString("R", CultureInfo.InvariantCulture)}\" />");

                builder.AppendLine($"      <AthleticCapacities breathing=\"{massModel.CapacityLevels[0]:F2}\" bloodPumping=\"{massModel.CapacityLevels[1]:F2}\" moving=\"{massModel.CapacityLevels[2]:F2}\" manipulation=\"{massModel.CapacityLevels[3]:F2}\" />");

                bool hasConsciousnessImpact = Math.Abs(massModel.ConsciousnessLevel - 1.0f) >= PawnDataRegistry.EfficiencyEpsilon;
                int totalImpacts = massModel.Ailments.Count + (hasConsciousnessImpact ? 1 : 0);
                builder.AppendLine($"      <AthleticImpacts count=\"{totalImpacts}\">");
                if (hasConsciousnessImpact)
                {
                    builder.AppendLine($"        <Impact label=\"Consciousness: {(massModel.ConsciousnessLevel * 100f):F0}%\" />");
                }
                for (int a = 0; a < massModel.Ailments.Count; a++)
                {
                    builder.AppendLine($"        <Impact label=\"{ExportUtilities.EscapeXml(massModel.Ailments[a])}\" />");
                }
                builder.AppendLine("      </AthleticImpacts>");

                builder.AppendLine("      <InfoCardViewModelTree>");
                for (int g = 0; g < massModel.EvaluatedParts.Count; g++)
                {
                    PartViewNode groupNode = massModel.EvaluatedParts[g];
                    builder.AppendLine($"        <GroupNode label=\"{ExportUtilities.EscapeXml(groupNode.Label)}\" subtotalKg=\"{groupNode.TotalOffset.ToString("R", CultureInfo.InvariantCulture)}\" prostheticKg=\"{groupNode.ProstheticOffset.ToString("R", CultureInfo.InvariantCulture)}\" athleticKg=\"{groupNode.AthleticOffset.ToString("R", CultureInfo.InvariantCulture)}\" healthKg=\"{groupNode.HealthOffset.ToString("R", CultureInfo.InvariantCulture)}\">");
                    ExportSubPartsXmlRecursive(builder, groupNode.SubParts, 10);
                    builder.AppendLine("        </GroupNode>");
                }
                builder.AppendLine("      </InfoCardViewModelTree>");

                // Anomalies
                List<string> anomalies = DetectTopologicalAnomalies(tmpl, massModel);
                builder.AppendLine($"      <TopologicalAnomalies status=\"{(anomalies.Count == 0 ? "Clean" : "AnomaliesDetected")}\" count=\"{anomalies.Count}\">");
                for (int a = 0; a < anomalies.Count; a++)
                {
                    builder.AppendLine($"        <Anomaly description=\"{ExportUtilities.EscapeXml(anomalies[a])}\" />");
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
        /// Workspace lease lifecycle is managed and released internally by the solver.
        /// </summary>
        /// <param name="evalSubject">The test subject being evaluated.</param>
        /// <param name="cleanBaseline">The resolved clean baseline mass capacity for the subject.</param>
        /// <returns>The populated MassCapacityModel for the given test subject.</returns>
        private static MassCapacityModel ResolveAndPopulateDiagnosticModel(
            TestSubjectEntry evalSubject,
            out float cleanBaseline)
        {
            MassCapacityModel massModel = new MassCapacityModel();
            if (evalSubject == null)
            {
                cleanBaseline = 0f;
                return massModel;
            }

            SandboxPawnHarness isolatedHarness = null;
            SandboxPawnHarness activeHarness;

            using (HeadlessPawnShell.EnterSafetyFallbackScope())
            {
                // BREAKPOINT ANCHOR: Harness Isolation Guard
                // If evaluating the currently selected test subject, use the active harness without mutating it.
                // If evaluating an unselected census species, instantiate a temporary ephemeral harness to avoid wiping UI modifications.
                if (evalSubject != null && evalSubject == TestBench.ActiveSubject && TestBench.Harness.IsValid)
                {
                    activeHarness = TestBench.Harness;
                }
                else
                {
                    isolatedHarness = new SandboxPawnHarness();
                    isolatedHarness.BindSubject(evalSubject);
                    activeHarness = isolatedHarness;
                }

                try
                {
                    var source = activeHarness.CreateDataSource();
                    cleanBaseline = source.ResolveBaselineCapacity();
                    ViewProjection.InspectAndProject(source, cleanBaseline, massModel);
                }
                finally
                {
                    isolatedHarness?.Dispose();
                }
            }

            return massModel;
        }

        #endregion

        #region 6. TREE PARSING & ANOMALY SCANNER HELPERS

        /// <summary>
        /// Recursively exports the details of sub-parts of a body part into the provided StringBuilder, including their conditions, weights, and
        ///  any anomalies.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the sub-part details will be appended.</param>
        /// <param name="subParts">The list of sub-parts to be exported.</param>
        /// <param name="depth">The current depth in the body part hierarchy, used for indentation.</param>
        private static void ExportSubPartsRecursive(StringBuilder builder, List<PartViewNode> subParts, int depth)
        {
            if (subParts == null || subParts.Count == 0) return;

            string indent = new string(' ', depth * 2);

            for (int i = 0; i < subParts.Count; i++)
            {
                PartViewNode node = subParts[i];
                string organTag = node.IsOrgan ? " [ORGAN]" : "";
                string weightStr = node.ProportionalWeight > 0f ? $" (Weight: {(node.ProportionalWeight * 100f):F3}%)" : "";

                string conditionTag = BuildTextConditionTag(node);
                string channelDetails = BuildTextChannelDetails(node);

                builder.AppendLine($"  {indent}- {node.Label}{organTag}{weightStr}{conditionTag}: {node.TotalOffset.ToStringMassOffset()}{channelDetails}");

                if (node.SubParts != null && node.SubParts.Count > 0)
                {
                    ExportSubPartsRecursive(builder, node.SubParts, depth + 1);
                }
            }
        }

        /// <summary>
        /// Builds a textual representation of the condition tags for the given body part node.
        /// </summary>
        /// <param name="node">The body part node for which to build the condition tags.</param>
        /// <returns>A string containing the condition tags for the body part, or an empty string if there are none.</returns>
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
            else if (node.HealthFraction < 1.0f - PawnDataRegistry.EfficiencyEpsilon)
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

        /// <summary>
        /// Builds a textual representation of the channel details (prosthetic, athletic, and health offsets) for the given body part node.
        /// </summary>
        /// <param name="node">The body part node for which to build the channel details.</param>
        /// <returns>A string containing the channel details for the body part, or an empty string if there are none.</returns>
        private static string BuildTextChannelDetails(PartViewNode node)
        {
            if (node == null) return string.Empty;

            bool hasProsthetic = Math.Abs(node.ProstheticOffset) >= PawnDataRegistry.EfficiencyEpsilon;
            bool hasAthletic = Math.Abs(node.AthleticOffset) >= PawnDataRegistry.EfficiencyEpsilon;
            bool hasHealth = Math.Abs(node.HealthOffset) >= PawnDataRegistry.EfficiencyEpsilon;

            if (!hasProsthetic && !hasAthletic && !hasHealth) return string.Empty;

            List<string> channelBreakdowns = new List<string>();
            if (hasProsthetic) channelBreakdowns.Add($"P: {node.ProstheticOffset.ToStringMassOffset()}");
            if (hasAthletic) channelBreakdowns.Add($"A: {node.AthleticOffset.ToStringMassOffset()}");
            if (hasHealth) channelBreakdowns.Add($"I: {node.HealthOffset.ToStringMassOffset()}");

            return $" ({string.Join(", ", channelBreakdowns)})";
        }

        /// <summary>
        /// Recursively exports the details of sub-parts of a body part into the provided StringBuilder, including their conditions, weights, and
        ///  any anomalies.
        /// </summary>
        /// <param name="builder">The StringBuilder to which the XML representation of the sub-parts will be appended.</param>
        /// <param name="subParts">The list of sub-part nodes to be exported.</param>
        /// <param name="indentSpaces">The number of spaces to use for indentation in the XML output.</param>
        private static void ExportSubPartsXmlRecursive(StringBuilder builder, List<PartViewNode> subParts, int indentSpaces)
        {
            if (subParts == null || subParts.Count == 0) return;
            string indent = new string(' ', indentSpaces);

            for (int i = 0; i < subParts.Count; i++)
            {
                PartViewNode node = subParts[i];
                string defName = node.Record?.def?.defName ?? "None";
                bool hasChildren = node.SubParts != null && node.SubParts.Count > 0;

                builder.Append(indent).Append("<PartNode");
                builder.Append(" label=\"").Append(ExportUtilities.EscapeXml(node.Label)).Append('"');
                builder.Append(" def=\"").Append(ExportUtilities.EscapeXml(defName)).Append('"');
                builder.Append(" isOrgan=\"").Append(node.IsOrgan).Append('"');
                builder.Append(" weightPct=\"").Append((node.ProportionalWeight * 100f).ToString("F3")).Append("%\"");
                builder.Append(" totalOffsetKg=\"").Append(node.TotalOffset.ToString("R", CultureInfo.InvariantCulture)).Append('"');

                if (node.HasProsthetic)
                {
                    builder.Append(" hasProsthetic=\"true\"");
                    if (!string.IsNullOrEmpty(node.ProstheticName))
                    {
                        builder.Append(" prostheticName=\"").Append(ExportUtilities.EscapeXml(node.ProstheticName)).Append('"');
                    }
                    builder.Append(" efficiencyRating=\"").Append(node.EfficiencyRating.ToString("R", CultureInfo.InvariantCulture)).Append('"');
                    if (node.IsInherited) builder.Append(" isInherited=\"true\"");
                }

                if (node.IsMissing)
                {
                    builder.Append(" isMissing=\"true\"");
                    if (node.IsNeutralized) builder.Append(" isNeutralized=\"true\"");
                }
                else if (node.HealthFraction < 1.0f - PawnDataRegistry.EfficiencyEpsilon)
                {
                    builder.Append(" healthFraction=\"").Append(node.HealthFraction.ToString("R", CultureInfo.InvariantCulture)).Append('"');
                }

                if (!string.IsNullOrEmpty(node.AthleticImplantName))
                {
                    builder.Append(" athleticImplant=\"").Append(ExportUtilities.EscapeXml(node.AthleticImplantName)).Append('"');
                }

                if (!string.IsNullOrEmpty(node.LocalAilmentName))
                {
                    builder.Append(" ailment=\"").Append(ExportUtilities.EscapeXml(node.LocalAilmentName)).Append('"');
                }

                if (Math.Abs(node.ProstheticOffset) >= PawnDataRegistry.EfficiencyEpsilon)
                {
                    builder.Append(" prostheticOffsetKg=\"").Append(node.ProstheticOffset.ToString("R", CultureInfo.InvariantCulture)).Append('"');
                }
                if (Math.Abs(node.AthleticOffset) >= PawnDataRegistry.EfficiencyEpsilon)
                {
                    builder.Append(" athleticOffsetKg=\"").Append(node.AthleticOffset.ToString("R", CultureInfo.InvariantCulture)).Append('"');
                }
                if (Math.Abs(node.HealthOffset) >= PawnDataRegistry.EfficiencyEpsilon)
                {
                    builder.Append(" healthOffsetKg=\"").Append(node.HealthOffset.ToString("R", CultureInfo.InvariantCulture)).Append('"');
                }

                if (hasChildren)
                {
                    builder.AppendLine(">");
                    ExportSubPartsXmlRecursive(builder, node.SubParts, indentSpaces + 2);
                    builder.Append(indent).AppendLine("</PartNode>");
                }
                else
                {
                    builder.AppendLine(" />");
                }
            }
        }

        /// <summary>
        /// Resolves the appropriate evaluation subject for the given scope, body, and available pawn things. If the scope is set to the
        ///  selected subject and a subject is provided, it returns that subject; otherwise, it selects a sample pawn thing matching the body
        ///  and constructs a new TestSubjectEntry.
        /// </summary>
        /// <param name="scope">The scope determining whether to use the selected subject or a sample pawn thing.</param>
        /// <param name="subject">The currently selected test subject, if any.</param>
        /// <param name="body">The body definition to match when selecting a sample pawn thing.</param>
        /// <param name="allPawnThings">The list of all available pawn things to choose from.</param>
        /// <returns>The resolved TestSubjectEntry based on the provided scope and available pawn things.</returns>
        private static TestSubjectEntry ResolveEvaluationSubject(
            ExportScope scope, 
            TestSubjectEntry subject, 
            BodyDef body, 
            List<ThingDef> allPawnThings)
        {
            if (scope == ExportScope.SelectedSubject && subject != null)
            {
                return subject;
            }

            ThingDef sampleThing = allPawnThings.Find(t => t.race?.body == body);
            if (sampleThing == null) return null;

            return new TestSubjectEntry(
                sampleThing.LabelCap.ToString(),
                body,
                sampleThing,
                sampleThing.modContentPack?.Name ?? "Core",
                "Diagnostic",
                "Diagnostic"
            );
        }

        /// <summary>
        /// Detects topological anomalies in the given species topology template and mass capacity model for the specified body.
        /// </summary>
        /// <param name="template">The species topology template to analyze.</param>
        /// <param name="body">The body definition associated with the template.</param>
        /// <param name="model">The mass capacity model to check for anomalies.</param>
        /// <returns>A list of strings describing any detected topological anomalies.</returns>
        private static List<string> DetectTopologicalAnomalies(SpeciesTopologyTemplate template, MassCapacityModel model)
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
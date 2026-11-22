using System.Collections.Generic;
using Verse;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. TOPOLOGY CHECKS

        /// <summary>
        /// Verifies that every compiled body template matches its body definition, maintains acyclic parent-before-child
        /// depth ordering, and conserves dynamic regional and static weight budgets according to active pair scaling.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckTopologyIntegrity(SelfCheckContext ctx)
        {
            List<BodyDef> bodies = DefDatabase<BodyDef>.AllDefsListForReading;
            if (bodies == null || bodies.Count == 0)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoBodies".Translate().ToString());
                return;
            }

            TopologyLayoutCompiler.EnsureInitialized();

            ref readonly SolverConfiguration activeConfig = ref MassCapacitySolver.ActiveConfiguration;
            bool hasNonZeroSettingsBudget = (activeConfig.BudgetCorePart + activeConfig.BudgetManipulationPart + activeConfig.BudgetMovingPart) > 0f;

            int checkedCount = 0;
            int uncompiled = 0;
            bool[] visitedScratch = new bool[64];

            for (int b = 0; b < bodies.Count; b++)
            {
                BodyDef body = bodies[b];
                SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                if (template == null)
                {
                    uncompiled++;
                    continue;
                }

                string who = body.defName;
                int count = template.PartCount;

                Expect.Equal(body.AllParts.Count, count, who + ": part count");
                Expect.True(template.IndexedParts.Length >= count, who + ": IndexedParts shorter than part count");
                Expect.True(template.PartTypes.Length >= count, who + ": PartTypes shorter than part count");
                Expect.True(template.ParentIndices.Length >= count, who + ": ParentIndices shorter than part count");
                Expect.True(template.DepthSortedIndices.Length >= count, who + ": DepthSortedIndices shorter than part count");
                Expect.True(template.StaticWeightFactors.Length >= count, who + ": StaticWeightFactors shorter than part count");

                if (visitedScratch.Length < count)
                {
                    visitedScratch = new bool[count];
                }
                else
                {
                    System.Array.Clear(visitedScratch, 0, count);
                }

                float torsoSum = 0f;
                float manipSum = 0f;
                float moveSum = 0f;
                float staticSum = 0f;

                for (int i = 0; i < count; i++)
                {
                    int parent = template.ParentIndices[i];
                    Expect.True(parent >= -1 && parent < count && parent != i, who + ": part " + i + " has invalid parent index " + parent);

                    float weight = template.StaticWeightFactors[i];
                    Expect.True(!float.IsNaN(weight) && !float.IsInfinity(weight) && weight >= 0f, who + ": part " + i + " has invalid static weight factor " + weight);

                    if (template.WeightModels[i] == LimbWeightModel.GeometricDecay &&
                        parent != -1 &&
                        template.PartTypes[parent] == template.PartTypes[i] &&
                        template.WeightModels[parent] == LimbWeightModel.GeometricDecay)
                    {
                        Expect.True(
                            weight <= template.StaticWeightFactors[parent] + 1e-5f,
                            who + ": child limb segment " + i + " outweighs parent " + parent);
                    }

                    torsoSum += template.WeightCore[i];
                    manipSum += template.WeightManipulation[i];
                    moveSum += template.WeightMoving[i];
                    staticSum += weight;
                }

                for (int d = 0; d < count; d++)
                {
                    int idx = template.DepthSortedIndices[d];
                    Expect.True(idx >= 0 && idx < count, who + ": DepthSortedIndices[" + d + "] out of range (" + idx + ")");
                    Expect.True(!visitedScratch[idx], who + ": duplicate slot " + idx + " in DepthSortedIndices");

                    int parent = template.ParentIndices[idx];
                    if (parent != -1)
                    {
                        Expect.True(visitedScratch[parent], who + ": DepthSortedIndices placed child " + idx + " before parent " + parent);
                    }

                    visitedScratch[idx] = true;
                }

                // 1. Torso structural column always normalizes to 1.0 across axial & peripheral bones
                if (torsoSum > 0f)
                {
                    Expect.True(System.Math.Abs(torsoSum - 1.0f) < 0.002f, who + ": WeightCore sum expected ~1.0 but got " + torsoSum);
                }

                // 2. Equal underlying channel sums always normalize to 1.0 regardless of limb root count
                int totalManipRoots = template.Counts.TotalManipulationRoots;
                int totalMovingRoots = template.Counts.TotalMovingRoots;

                if (totalManipRoots > 0)
                {
                    Expect.True(System.Math.Abs(manipSum - 1.0f) < 0.002f,
                        who + ": WeightManipulation sum expected ~1.000 but got " + manipSum.ToString("F3"));
                }

                if (totalMovingRoots > 0)
                {
                    Expect.True(System.Math.Abs(moveSum - 1.0f) < 0.002f,
                        who + ": WeightMoving sum expected ~1.000 but got " + moveSum.ToString("F3"));
                }

                // 3. Verify static weight factors sum against active normalized regional budgets (always ~1.000)
                PartCounts counts = template.Counts;
                SystemicEvaluationContext.ResolveNormalizedBudgets(
                    in counts,
                    in activeConfig,
                    out float normTorso,
                    out float normManip,
                    out float normMove);

                float expectedStaticSum = ((torsoSum > 0f ? 1.0f : 0f) * normTorso)
                                        + ((totalManipRoots > 0 ? 1.0f : 0f) * normManip)
                                        + ((totalMovingRoots > 0 ? 1.0f : 0f) * normMove);

                if (hasNonZeroSettingsBudget && (torsoSum > 0f || totalManipRoots > 0 || totalMovingRoots > 0))
                {
                    Expect.True(System.Math.Abs(staticSum - expectedStaticSum) < 0.002f,
                        who + ": StaticWeightFactors sum expected ~" + expectedStaticSum.ToString("F3") + " but got " + staticSum.ToString("F3"));
                }

                checkedCount++;
            }

            ctx.Note(uncompiled > 0
                ? "OverHaulers_SelfCheck_Note_TopologyPartial".Translate(checkedCount, uncompiled).ToString()
                : "OverHaulers_SelfCheck_Note_Topology".Translate(checkedCount).ToString());
        }

        #endregion
    }
}
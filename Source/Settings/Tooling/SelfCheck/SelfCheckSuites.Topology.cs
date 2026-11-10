using System.Collections.Generic;
using Verse;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. TOPOLOGY CHECKS

        /// <summary>
        /// Verifies that every compiled body template matches its body definition and has valid parent indices and static weight factors.
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

            int checkedCount = 0;
            int uncompiled = 0;

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
                Expect.True(template.StaticWeightFactors.Length >= count, who + ": StaticWeightFactors shorter than part count");

                for (int i = 0; i < count; i++)
                {
                    int parent = template.ParentIndices[i];
                    Expect.True(parent >= -1 && parent < count && parent != i, who + ": part " + i + " has invalid parent index " + parent);

                    float weight = template.StaticWeightFactors[i];
                    Expect.True(!float.IsNaN(weight) && !float.IsInfinity(weight) && weight >= 0f, who + ": part " + i + " has invalid static weight factor " + weight);
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

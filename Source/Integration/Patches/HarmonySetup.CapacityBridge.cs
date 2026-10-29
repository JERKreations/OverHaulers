using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [INT-01] Universal Harmony bridge hooking into RimWorld's MassUtility.Capacity.
    /// Serves as the single, universal egress point delivering anatomical mass capacity offsets
    /// to the game engine across all play modes and external mod configurations.
    /// Resides under Source/Integration/Patches/.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. [INT-CAP-01] THREAD-STATIC SCRATCHPADS & CALL DEPTH

        [ThreadStatic]
        private static int capacityCallDepth;

        #endregion

        #region 2. [INT-CAP-02] UNIVERSAL CAPACITY PATCH REGISTRATION

        /// <summary>
        /// Applies the universal capacity postfix patch to MassUtility.Capacity.
        /// </summary>
        /// <param name="harmony">The Harmony instance used to apply the patch.</param>
        internal static void ApplyDirectCapacityPatch(Harmony harmony)
        {
            MethodInfo targetMethod = AccessTools.Method(typeof(MassUtility), nameof(MassUtility.Capacity));
            if (targetMethod == null)
            {
                OHLog.Error(LogDomain.Integration, "ApplyDirectCapacityPatch", null, "Failed to resolve MassUtility.Capacity method target.");
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Prefix));
            MethodInfo postfix = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Postfix));
            MethodInfo finalizer = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Finalizer));

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix), finalizer: new HarmonyMethod(finalizer));
        }

        #endregion

        #region 3. [INT-CAP-03] HARMONY HOOKS (PREFIX, POSTFIX, FINALIZER)

        /// <summary>
        /// Prefix patch tracking call depth to prevent recursive postfix double-dipping.
        /// </summary>
        private static void MassUtility_Capacity_Prefix()
        {
            capacityCallDepth++;
        }

        /// <summary>
        /// Universal postfix patch delivering the biological mass offset directly to MassUtility.Capacity on root calls only.
        /// Immune to third-party apparel debuffs, order-of-operation anomalies, and recursive queries.
        /// </summary>
        /// <param name="p">The pawn whose mass capacity is being evaluated.</param>
        /// <param name="__result">The running capacity calculation result, modified by our anatomical offset.</param>
        /// <param name="explanation">A StringBuilder containing the explanation for the calculation, if requested.</param>
        private static void MassUtility_Capacity_Postfix(Pawn p, ref float __result, StringBuilder explanation)
        {
            if (p == null) return;

            // RE-ENTRANCY & ADOPTION SHIELD: If an external mod queries MassUtility.Capacity recursively,
            // or if a foreign stat is adopted (which delivers the offset directly via MassCapacityStatPart.TransformValue),
            // return the unmodified baseline to prevent double-counting.
            if (capacityCallDepth > 1 || IntegrationPipeline.IsForeignStatAdopted) return;

            // BREAKPOINT ANCHOR: Dummy Evaluation Bypass
            if (SpeciesBaselineCalibration.IsResolvingBaseline) return;

            // INGRESS GATE: Completely skip non-caravan species during live play
            if (!PawnDataRegistry.CanCarryCaravanMass(p)) return;

            // FAST-PATH: If cache node is fresh and valid, bypass baseline resolution and BodySize getters
            if (PawnDataRegistry.TryGetFreshOffset(p.thingIDNumber, out float fastOffset))
            {
                __result += fastOffset;
                __result = MassCapacitySolver.EnforceSafetyFloorForPawn(__result, p);
                if (explanation != null)
                {
                    SyncFormationCapacityEntry(explanation, p, __result);
                }
                return;
            }

            float cleanBiologicalBaseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(p);
            if (cleanBiologicalBaseline <= 0f) return;

            float calculatedOffset = PawnDataRegistry.GetOffset(p, cleanBiologicalBaseline);

            __result += calculatedOffset;

            // EGRESS CLAMP: Guarantee the actual game result obeys the minimum safety floor
            __result = MassCapacitySolver.EnforceSafetyFloorForPawn(__result, p);

            // EXPLANATION SYNC: If caller provided an explanation StringBuilder (e.g. CollectionsMassCalculator),
            // replace vanilla's pre-postfix baseline entry with the true final capacity.
            if (explanation != null) SyncFormationCapacityEntry(explanation, p, __result);
        }

        /// <summary>
        /// Finalizer guaranteeing the call depth shield is decremented even if the original method or third-party patches throw an exception.
        /// </summary>
        private static void MassUtility_Capacity_Finalizer()
        {
            if (capacityCallDepth > 0) capacityCallDepth--;
        }

        #endregion

        #region 4. [INT-CAP-04] STRING EXPLANATION SYNCHRONIZATION HELPERS

        /// <summary>
        /// Synchronizes the numeric capacity entry written into vanilla's caravan formation explanation buffer.
        /// Scans character indices backwards directly within the StringBuilder without allocating intermediate strings.
        /// </summary>
        /// <param name="explanation">The explanation StringBuilder being populated by vanilla.</param>
        /// <param name="pawn">The pawn entity whose mass capacity was updated.</param>
        /// <param name="finalCapacity">The solved final mass capacity in kilograms.</param>
        private static void SyncFormationCapacityEntry(StringBuilder explanation, Pawn pawn, float finalCapacity)
        {
            if (explanation == null || pawn == null || explanation.Length == 0) return;

            string pawnLabel = pawn.LabelShortCap.ToString();
            int labelLen = pawnLabel.Length;
            int sbLen = explanation.Length;

            // 1. Scan backwards to locate the pawn's label without allocating explanation.ToString()
            int pawnIndex = -1;
            for (int i = sbLen - labelLen; i >= 0; i--)
            {
                bool match = true;
                for (int j = 0; j < labelLen; j++)
                {
                    if (char.ToUpperInvariant(explanation[i + j]) != char.ToUpperInvariant(pawnLabel[j]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    pawnIndex = i;
                    break;
                }
            }

            if (pawnIndex < 0) return;

            // 2. Scan forward from the label to find the colon separator
            int colonIndex = -1;
            for (int i = pawnIndex + labelLen; i < sbLen; i++)
            {
                if (explanation[i] == ':')
                {
                    colonIndex = i;
                    break;
                }
                if (explanation[i] == '\n') break;
            }

            if (colonIndex < 0) return;

            // 3. Scan for the end of the line (or end of buffer)
            int lineEndIndex = sbLen;
            for (int i = colonIndex + 1; i < sbLen; i++)
            {
                if (explanation[i] == '\r' || explanation[i] == '\n')
                {
                    lineEndIndex = i;
                    break;
                }
            }

            // 4. In-place replacement of the numeric substring
            int oldLength = lineEndIndex - (colonIndex + 1);
            if (oldLength > 0)
            {
                explanation.Remove(colonIndex + 1, oldLength);
            }

            explanation.Insert(colonIndex + 1, " " + finalCapacity.ToStringMassOffset());
        }

        /// <summary>
        /// Performs an allocation-free reverse search through the StringBuilder for a target substring.
        /// </summary>
        /// <param name="sb">The StringBuilder to search within.</param>
        /// <param name="target">The substring to search for.</param>
        /// <returns>The index of the last occurrence of the target substring, or -1 if not found.</returns>
        private static int LastIndexOfSubstring(StringBuilder sb, string target)
        {
            if (sb == null || string.IsNullOrEmpty(target) || sb.Length < target.Length) return -1;

            int targetLen = target.Length;
            int startIdx = sb.Length - targetLen;

            for (int i = startIdx; i >= 0; i--)
            {
                bool match = true;
                for (int j = 0; j < targetLen; j++)
                {
                    if (sb[i + j] != target[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return i;
            }

            return -1;
        }

        /// <summary>
        /// Scans forward from a startIndex in the StringBuilder to find the nearest newline character or end of buffer.
        /// </summary>
        /// <param name="sb">The StringBuilder to search within.</param>
        /// <param name="startIndex">The index to start scanning from.</param>
        /// <returns>The index of the nearest newline character or the end of the buffer if none is found.</returns>
        private static int IndexOfLineEnd(StringBuilder sb, int startIndex)
        {
            if (sb == null) return 0;

            for (int i = startIndex; i < sb.Length; i++)
            {
                if (sb[i] == '\r' || sb[i] == '\n')
                {
                    return i;
                }
            }

            return sb.Length;
        }

        #endregion
    }
}
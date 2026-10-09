using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-01] UNIVERSAL CAPACITY HARMONY BRIDGE

    /// <summary>
    /// Universal Harmony bridge hooking into RimWorld's MassUtility.Capacity.
    /// Serves as the single, universal egress point delivering anatomical mass capacity offsets
    /// to the game engine across all play modes and external mod configurations.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. DIRECT CAPACITY PATCH BINDING & POSTFIX BRIDGE

        [ThreadStatic]
        private static int capacityCallDepth;

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

            // RE-ENTRANCY SHIELD: If an external mod's StatWorker (e.g. VEF) queries MassUtility.Capacity
            // from within a capacity calculation to read the species baseline, return the clean unmodified baseline!
            if (capacityCallDepth > 1)
            {
                return;
            }

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
                    SyncExplanationCapacity(explanation, p, __result);
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
            if (explanation != null)
            {
                SyncExplanationCapacity(explanation, p, __result);
            }
        }

        /// <summary>
        /// Synchronizes vanilla's explanation StringBuilder entry for the pawn with the true post-offset mass capacity.
        /// Non-destructively replaces only the numeric mass token on the pawn's entry line, preserving any
        /// preceding or subsequent entries appended by vanilla or third-party mods.
        /// </summary>
        /// <param name="explanation">The StringBuilder containing the explanation text.</param>
        /// <param name="p">The pawn whose capacity is being synchronized.</param>
        /// <param name="finalCapacity">The final mass capacity value to insert into the explanation.</param>
        private static void SyncExplanationCapacity(StringBuilder explanation, Pawn p, float finalCapacity)
        {
            if (explanation == null || p == null) return;

            string label = p.LabelShortCap;
            if (string.IsNullOrEmpty(label)) return;

            int lastIdx = LastIndexOfPawnPrefix(explanation, label);
            if (lastIdx >= 0)
            {
                int prefixLen = 4 + label.Length + 2; // "  - " + label + ": "
                int valueStart = lastIdx + prefixLen;

                // Find the end of the current line to preserve any trailing lines or third-party annotations
                int valueEnd = valueStart;
                int sbLen = explanation.Length;
                while (valueEnd < sbLen && explanation[valueEnd] != '\r' && explanation[valueEnd] != '\n')
                {
                    valueEnd++;
                }

                explanation.Remove(valueStart, valueEnd - valueStart);
                explanation.Insert(valueStart, finalCapacity.ToStringMassOffset());
            }
        }

        /// <summary>
        /// Finds the last character index of the vanilla entry prefix ("  - " + label + ": ")
        /// within the explanation StringBuilder.
        /// </summary>
        /// <param name="sb">The StringBuilder containing the explanation text.</param>
        /// <param name="label">The label of the pawn to search for.</param>
        /// <returns>The last character index of the pawn prefix, or -1 if not found.</returns>
        private static int LastIndexOfPawnPrefix(StringBuilder sb, string label)
        {
            if (sb == null || string.IsNullOrEmpty(label)) return -1;

            // Prefix layout: "  - " (4 chars) + label + ": " (2 chars)
            int prefixLen = 4 + label.Length + 2;
            int sbLen = sb.Length;
            if (sbLen < prefixLen) return -1;

            for (int i = sbLen - prefixLen; i >= 0; i--)
            {
                // Quick pre-filter on initial bullet indentation and trailing colon-space
                if (sb[i] != ' ' || sb[i + 1] != ' ' || sb[i + 2] != '-' || sb[i + 3] != ' ')
                {
                    continue;
                }

                if (sb[i + 4 + label.Length] != ':' || sb[i + 5 + label.Length] != ' ')
                {
                    continue;
                }

                // Verify label characters
                bool match = true;
                for (int j = 0; j < label.Length; j++)
                {
                    if (sb[i + 4 + j] != label[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Finalizer guaranteeing the call depth shield is decremented even if the original method or third-party patches throw an exception.
        /// </summary>
        private static void MassUtility_Capacity_Finalizer()
        {
            if (capacityCallDepth > 0)
            {
                capacityCallDepth--;
            }
        }

        #endregion
    }

    #endregion
}
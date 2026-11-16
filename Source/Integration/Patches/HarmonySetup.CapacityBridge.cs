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
                OHLog.Error("Integration.ApplyDirectCapacityPatch", null, "Failed to resolve MassUtility.Capacity method target.");
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Prefix));
            MethodInfo postfix = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Postfix));
            MethodInfo finalizer = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Finalizer));

            harmony.Patch(
                targetMethod,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix) { priority = Priority.Last },
                finalizer: new HarmonyMethod(finalizer)
            );
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
        [HarmonyPriority(Priority.Last)]
        private static void MassUtility_Capacity_Postfix(Pawn p, ref float __result, StringBuilder explanation)
        {
            if (p == null) return;
            if (capacityCallDepth > 1) return;

            // RE-ENTRANCY & ADOPTION SHIELD:
            // When a foreign stat is adopted, numerical offset delivery is handled by MassCapacityStatPart.
            // If the adopted profile explicitly declares syncFormationCapacity (e.g. Combat Extended),
            // sanitize vanilla's explanation buffer before returning to clean up stale vanilla +35kg lines.
            if (IntegrationPipeline.IsForeignStatAdopted)
            {
                if (IntegrationPipeline.ActiveProfileSyncFormationCapacity && explanation != null)
                {
                    // BREAKPOINT ANCHOR: Order-Agnostic Capacity Query
                    // Query the adopted stat directly so the explanation line matches the true solved capacity
                    // regardless of whether Over Haulers or the host mod executed its postfix first.
                    StatDef adoptedStat = IntegrationPipeline.ActiveMassCapacityStat;
                    float finalCapacity = adoptedStat != null ? p.GetStatValue(adoptedStat) : __result;
                    SyncFormationCapacityEntry(explanation, p, finalCapacity);
                }
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
                __result = MassCapacitySolver.EnforceHardCompatibilitySafetyFloorForPawn(__result, p);
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
            __result = MassCapacitySolver.EnforceHardCompatibilitySafetyFloorForPawn(__result, p);

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
        /// Scans character indices backwards directly within the StringBuilder without allocating intermediate strings,
        /// validating left/right label delimiters to avoid prefix collisions on numbered pawns.
        /// </summary>
        /// <param name="explanation">The explanation StringBuilder being populated by vanilla.</param>
        /// <param name="pawn">The pawn entity whose mass capacity was updated.</param>
        /// <param name="finalCapacity">The solved final mass capacity in kilograms.</param>
        private static void SyncFormationCapacityEntry(StringBuilder explanation, Pawn pawn, float finalCapacity)
        {
            if (explanation == null || pawn == null || explanation.Length == 0) return;

            string pawnLabel = pawn.LabelShortCap.ToString();
            if (string.IsNullOrEmpty(pawnLabel)) return;

            int labelLen = pawnLabel.Length;
            int sbLen = explanation.Length;

            // 1. Scan backwards to locate an exact pawn label entry followed immediately by ':'
            int pawnIndex = -1;
            for (int i = sbLen - labelLen - 1; i >= 0; i--)
            {
                if (explanation[i + labelLen] != ':') continue;

                // Validate left boundary: start of buffer, newline, or vanilla "- " list bullet
                bool validLeftBoundary = (i == 0) ||
                                         (explanation[i - 1] == '\n') ||
                                         (i >= 2 && explanation[i - 2] == '-' && explanation[i - 1] == ' ');
                if (!validLeftBoundary) continue;

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

            int colonIndex = pawnIndex + labelLen;

            // 2. Scan for the end of the line (or end of buffer)
            int lineEndIndex = sbLen;
            for (int i = colonIndex + 1; i < sbLen; i++)
            {
                if (explanation[i] == '\r' || explanation[i] == '\n')
                {
                    lineEndIndex = i;
                    break;
                }
            }

            // 3. In-place replacement of the numeric substring
            int oldLength = lineEndIndex - (colonIndex + 1);
            if (oldLength > 0)
            {
                explanation.Remove(colonIndex + 1, oldLength);
            }

            explanation.Insert(colonIndex + 1, " " + finalCapacity.ToStringMassOffset());
        }

        #endregion
    }
}
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
                OHLog.Error(LogDomain.Integration, "Failed to resolve MassUtility.Capacity method target.");
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Prefix));
            MethodInfo postfix = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Postfix));

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
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
        /// <param name="p">The pawn whose carrying capacity is being evaluated.</param>
        /// <param name="__result">The running capacity calculation result, modified by our anatomical offset.</param>
        /// <param name="explanation">A StringBuilder containing the explanation for the calculation, if requested.</param>
        private static void MassUtility_Capacity_Postfix(Pawn p, ref float __result, StringBuilder explanation)
        {
            try
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
                    __result = MassCapacitySolver.EnforceSafetyFloor(__result, p.LabelShortCap);
                    return;
                }

                float cleanBiologicalBaseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(p);
                if (cleanBiologicalBaseline <= 0f) return;

                float calculatedOffset = PawnDataRegistry.GetOffset(p, cleanBiologicalBaseline);

                __result += calculatedOffset;

                // EGRESS CLAMP: Guarantee the actual game result obeys the minimum safety floor
                __result = MassCapacitySolver.EnforceSafetyFloor(__result, p.LabelShortCap);
            }
            finally
            {
                if (capacityCallDepth > 0)
                {
                    capacityCallDepth--;
                }
            }
        }

        #endregion
    }

    #endregion
}
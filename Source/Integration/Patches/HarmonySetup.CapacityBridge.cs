using System;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Provides patches and bridges for handling direct capacity calculations in the MassUtility class, allowing for custom integration
    ///  logic to be executed after the original method.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. DIRECT CAPACITY PATCH BINDING & POSTFIX BRIDGE

        private static bool isCapacityPatched = false;

        [ThreadStatic]
        private static int capacityCallDepth;

        /// <summary>
        /// Applies the direct capacity patch to the MassUtility.Capacity method, allowing for custom postfix logic to be executed.
        /// Enforces strict idempotency to prevent duplicate postfix chaining.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the patch.</param>
        private static void ApplyDirectCapacityPatch(Harmony harmony)
        {
            if (isCapacityPatched) return;

            var originalMethod = AccessTools.Method(typeof(MassUtility), nameof(MassUtility.Capacity));
            var prefixMethod = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Prefix));
            var postfixMethod = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Postfix));

            if (originalMethod != null && prefixMethod != null && postfixMethod != null)
            {
                var patchInfo = Harmony.GetPatchInfo(originalMethod);
                if (patchInfo != null && patchInfo.Postfixes != null)
                {
                    for (int i = 0; i < patchInfo.Postfixes.Count; i++)
                    {
                        if (patchInfo.Postfixes[i].PatchMethod == postfixMethod)
                        {
                            isCapacityPatched = true;
                            return;
                        }
                    }
                }

                harmony.Patch(originalMethod, prefix: new HarmonyMethod(prefixMethod), postfix: new HarmonyMethod(postfixMethod));
                isCapacityPatched = true;
            }
        }

        /// <summary>
        /// Prefix tracking re-entrancy depth per thread.
        /// </summary>
        private static void MassUtility_Capacity_Prefix()
        {
            capacityCallDepth++;
        }

        /// <summary>
        /// Postfix patch for mass utility capacity calculations.
        /// Dispatches directly to the active integration pipeline driver on root calls only.
        /// </summary>
        /// <param name="p">The pawn whose mass utility capacity is being evaluated.</param>
        /// <param name="__result">The result of the mass utility capacity calculation, which may be modified by the integration pipeline.</param>
        /// <param name="explanation">A StringBuilder containing the explanation for the mass utility capacity calculation.</param>
        private static void MassUtility_Capacity_Postfix(Pawn p, ref float __result, StringBuilder explanation)
        {
            try
            {
                if (p == null) return;

                // RE-ENTRANCY SHIELD: If an external mod's StatWorker (e.g. VEF) is querying MassUtility.Capacity
                // from within a capacity calculation to read the species baseline, return the clean unmodified baseline!
                if (capacityCallDepth > 1)
                {
                    return;
                }

                IntegrationPipeline.ActiveDriver?.OnMassUtilityCapacityPostfix(p, ref __result, explanation);
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
}
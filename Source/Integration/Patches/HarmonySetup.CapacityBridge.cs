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

        /// <summary>
        /// Applies the direct capacity patch to the MassUtility.Capacity method, allowing for custom postfix logic to be executed.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the patch.</param>
        private static void ApplyDirectCapacityPatch(Harmony harmony)
        {
            var originalMethod = AccessTools.Method(typeof(MassUtility), nameof(MassUtility.Capacity));
            var postfixMethod = AccessTools.Method(typeof(HarmonySetup), nameof(MassUtility_Capacity_Postfix));

            if (originalMethod != null && postfixMethod != null)
            {
                harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
            }
        }

        /// <summary>
        /// Postfix patch for mass utility capacity calculations.
        /// Dispatches directly to the active integration pipeline driver.
        /// </summary>
        /// <param name="p">The pawn whose mass utility capacity is being evaluated.</param>
        /// <param name="__result">The result of the mass utility capacity calculation, which may be modified by the integration pipeline.</param>
        /// <param name="explanation">A StringBuilder containing the explanation for the mass utility capacity calculation.</param>
        private static void MassUtility_Capacity_Postfix(Pawn p, ref float __result, StringBuilder explanation)
        {
            if (p == null) return;
            IntegrationPipeline.ActiveDriver?.OnMassUtilityCapacityPostfix(p, ref __result, explanation);
        }

        #endregion
    }
}
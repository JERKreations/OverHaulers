using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-06] NATIVE CARAVAN STAT DEF-OF REGISTRY

    /// <summary>
    /// [INT-06] Zero-allocation static pointer cache for native OverHaulers Def references.
    /// Bound deterministically by RimWorld's DefOfHelper at engine boot.
    /// Resides under Source/Integration/Stats/.
    /// </summary>
    [DefOf]
    public static class OverHaulersStatDefOf
    {
#pragma warning disable CS0649 // Field is assigned reflectively by RimWorld's DefOfHelper
        public static StatDef OverHaulers_CaravanMassCapacity;
#pragma warning restore CS0649

        static OverHaulersStatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(OverHaulersStatDefOf));
        }
    }

    #endregion
}
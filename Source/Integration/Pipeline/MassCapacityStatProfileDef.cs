using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-05] DECLARATIVE STAT ADOPTION PROFILE

    /// <summary>
    /// Declarative XML profile used to configure adoption of third-party caravan mass stats (e.g. VEF_MassCarryCapacity).
    /// Provides high-speed O(1) matching for known mods without requiring on-demand CIL bytecode scanning.
    /// </summary>
    public class MassCapacityStatProfileDef : Def
    {
        #region 1. XML DEFINITION CONFIGURATION

        public string modPackageId;
        public string statDefName;
        public int priority = 0;
        public string customDescriptionKey;

        #endregion

        #region 2. VALIDATION & ACTIVE STAT RESOLUTION

        /// <summary>
        /// Validates whether the declared mod package is loaded and the target StatDef exists in the game database.
        /// </summary>
        /// <param name="resolvedStat">Outputs the resolved StatDef if active and valid; otherwise, null.</param>
        /// <returns>True if the mod is active and the target StatDef is found; otherwise, false.</returns>
        public bool IsValidAndActive(out StatDef resolvedStat)
        {
            resolvedStat = null;

            if (!string.IsNullOrEmpty(modPackageId) && !ModsConfig.IsActive(modPackageId))
            {
                return false;
            }

            if (string.IsNullOrEmpty(statDefName))
            {
                return false;
            }

            resolvedStat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            return resolvedStat != null;
        }

        #endregion
    }

    #endregion
}
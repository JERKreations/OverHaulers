using System.Collections.Generic;
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

        /// <summary>
        /// The unique identifier of the mod package that provides the target StatDef.
        /// Used to check if the mod is active before attempting to resolve the StatDef.
        /// </summary>
        public string modPackageId;

        /// <summary>The name of the StatDef provided by the target mod.</summary>
        public string statDefName;

        /// <summary>
        /// The priority of this profile when multiple profiles target the same StatDef.
        /// Higher values take precedence.
        /// </summary>
        public int priority = 0;

        /// <summary>Optional key for a custom description to display in the UI.</summary>
        public string customDescriptionKey;

        /// <summary>Short label for the host mod, used in UI displays to indicate which mod's deductions are being applied.</summary>
        public string hostLabelShort;

        /// <summary>
        /// When true, Over Haulers takes full ownership of the adopted stat, neutralizing defined host multipliers in favor of its
        ///  own calculations.
        /// </summary>
        public bool allowAuthoritativeTakeover;

        /// <summary>
        /// When true, sanitizes vanilla's caravan explanation StringBuilder to replace stale +35kg entries left behind by host mods
        ///  that only patch return values via Harmony postfixes (e.g. Combat Extended).
        /// </summary>
        public bool syncFormationCapacity;

        /// <summary>List of capacity factors from the host mod to neutralize when applying the adopted stat.</summary>
        public List<string> neutraliseCapacityFactors;

        /// <summary>List of stat factors from the host mod to neutralize when applying the adopted stat.</summary>
        public List<string> neutraliseStatFactors;

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
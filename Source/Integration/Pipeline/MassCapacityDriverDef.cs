using System;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Declarative XML definition registering an external mod's StatDef or caravan driver.
    /// Allows OverHaulers and third-party modders to establish seamless integration purely through XML.
    /// </summary>
    public class MassCapacityDriverDef : Def
    {
        /// <summary>
        /// The Steam/RimWorld package ID of the mod this driver targets (e.g. "vanillaexpanded.framework").
        /// Optional: If left null/empty, the driver will be considered active as long as the stat exists.
        /// </summary>
        public string modPackageId;

        /// <summary>
        /// The defName of the StatDef used by the external mod for caravan mass (e.g. "CaravanMassCapacity").
        /// Kept as a string to avoid XML cross-reference errors when the target mod is not loaded.
        /// </summary>
        public string statDefName;

        /// <summary>
        /// The C# driver implementation to instantiate. Defaults to GenericStatDriver.
        /// </summary>
        public Type driverClass = typeof(GenericStatDriver);

        /// <summary>
        /// Priority weighting if multiple drivers match. Higher priorities are evaluated first.
        /// </summary>
        public int priority = 100;

        /// <summary>
        /// Determines if the driver is valid and active based on the current mod list and available StatDefs.
        /// </summary>
        /// <param name="resolvedStat">Outputs the resolved StatDef if the driver is valid and active; otherwise, null.</param>
        /// <returns>True if the driver is valid and active; otherwise, false.</returns>
        public bool IsValidAndActive(out StatDef resolvedStat)
        {
            resolvedStat = null;

            // 1. If a specific packageId is required, verify that the mod is active in the player's mod list
            if (!string.IsNullOrEmpty(modPackageId) && ModLister.GetActiveModWithIdentifier(modPackageId) == null)
            {
                return false;
            }

            // 2. Verify that the target StatDef actually exists in the current session
            if (string.IsNullOrEmpty(statDefName))
            {
                return false;
            }

            resolvedStat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            return resolvedStat != null;
        }
    }
}
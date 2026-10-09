using HarmonyLib;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-01] HARMONY BOOTSTRAP & ORCHESTRATION
    
    /// <summary>
    /// [INT-01] Central Harmony bootstrap orchestrator.
    /// Manages master patch registration pipelines, dynamic safety hook lifecycles,
    /// and third-party compatibility bridges across modular partial domains.
    /// </summary>
    [StaticConstructorOnStartup]
    public static partial class HarmonySetup
    {
        /// <summary>Active Harmony instance handle used for dynamic driver rebinding.</summary>
        public static Harmony HarmonyInstance { get; private set; }

        private static bool isCompatibilityInitialized = false;

        static HarmonySetup()
        {
            InitializeCompatibilityLayer();
        }

        /// <summary>
        /// Initializes the compatibility layer for the OverHaulers mod, setting up Harmony patches and integration pipelines.
        /// Ensures that all necessary hooks and safety measures are applied before the mod becomes active.
        /// This method is called automatically during the static constructor of the HarmonySetup class.
        /// </summary>
        private static void InitializeCompatibilityLayer()
        {
            if (isCompatibilityInitialized)
            {
                OHLog.Integration.Warn("HarmonySetup", null, $"InitializeCompatibilityLayer called more than once! Duplicate call intercepted from:\n{new System.Diagnostics.StackTrace(1, true)}");
                return;
            }
            isCompatibilityInitialized = true;

            HarmonyInstance = new Harmony("com.overhaulers.mod");

            // 1. Reactive invalidation patches (Pawn health, apparel, equipment, death, despawn)
            InstallInvalidationPatches(HarmonyInstance);

            // 2. Third-party mod drivers (VEF, PUAH, Simple Sidearms, Custom XML drivers)
            Guarded("IntegrationPipeline.Initialize", () => IntegrationPipeline.Initialize(HarmonyInstance));

            // 3. Native MassUtility.Capacity direct postfix bridge
            Guarded("MassUtility.Capacity", () => ApplyDirectCapacityPatch(HarmonyInstance));

            // 4. Strip the node an earlier version wrote into saves (avoids one-time "class not found" errors)
            Guarded("World.ExposeComponents", () => InstallLegacySaveCleanup(HarmonyInstance));

            // Sandbox shields and main-menu fallbacks are intentionally not installed here; they are attached on demand
            // while a sandbox harness is alive (see AcquireSandboxShields) so normal play pays nothing for them.
        }

        /// <summary>
        /// Runs a single patch installation step, logging (rather than propagating) any failure so that
        /// one broken hook cannot prevent the remaining hooks from being applied.
        /// </summary>
        /// <param name="label">Human-readable name of the patch, used for throttled logging.</param>
        /// <param name="install">The installation action to execute.</param>
        private static void Guarded(string label, System.Action install)
        {
            try
            {
                install();
            }
            catch (System.Exception ex)
            {
                OHLog.Integration.Warn("Patch:" + label, ex, $"Failed to install '{label}'. The feature it supports may be unavailable.");
            }
        }

        /// <summary>
        /// Wraps one of this class's static patch methods as a <see cref="HarmonyMethod"/>, failing loudly if it cannot be found.
        /// </summary>
        /// <param name="methodName">The name of the static patch method declared on <see cref="HarmonySetup"/>.</param>
        private static HarmonyMethod OwnPatch(string methodName)
        {
            System.Reflection.MethodInfo method = AccessTools.Method(typeof(HarmonySetup), methodName);
            if (method == null)
            {
                throw new System.MissingMethodException(typeof(HarmonySetup).FullName, methodName);
            }
            return new HarmonyMethod(method);
        }
    }

    #endregion
}
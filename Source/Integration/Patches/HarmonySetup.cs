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

            // 1. Permanent sandbox entity shields (nanosecond guards for dummy pawns)
            InstallPermanentSandboxShields(HarmonyInstance);

            // 2. Conditionally attach main-menu fallbacks if starting on the Main Menu
            if (Current.ProgramState != ProgramState.Playing)
            {
                EnsureSafetyPatchesApplied();
            }

            // 3. Reactive invalidation patches (Pawn health, apparel, equipment, death, despawn)
            InstallInvalidationPatches(HarmonyInstance);

            // 4. Third-party mod drivers (VEF, PUAH, Simple Sidearms, Custom XML drivers)
            IntegrationPipeline.Initialize(HarmonyInstance);

            // 5. Native MassUtility.Capacity direct postfix bridge
            ApplyDirectCapacityPatch(HarmonyInstance);
        }
    }

    #endregion
}
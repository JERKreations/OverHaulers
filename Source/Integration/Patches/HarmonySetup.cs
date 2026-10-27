using HarmonyLib;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [INT-01] Central Harmony bootstrap orchestrator.
    /// Manages master patch registration pipelines, dynamic safety hook lifecycles,
    /// and third-party compatibility bridges across modular partial domains.
    /// Resides under Source/Integration/Patches/.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. STATIC STATE & HARMONY INSTANCE

        /// <summary>Active Harmony instance handle used for dynamic driver rebinding.</summary>
        public static Harmony HarmonyInstance { get; private set; }

        private static bool isCompatibilityInitialized = false;

        #endregion

        #region 2. LIFECYCLE & INITIALIZATION ENTRY POINT

        /// <summary>
        /// [INT-01] Explicitly initializes the master Harmony compatibility layer, patches, and integration pipeline.
        /// Invoked deterministically by <see cref="OverHaulersLifecycle"/> during engine bootstrap.
        /// Thread-safe and idempotent; executes in 1 CPU cycle once initialized.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (isCompatibilityInitialized) return;
            isCompatibilityInitialized = true;

            var watch = System.Diagnostics.Stopwatch.StartNew();

            HarmonyInstance = new Harmony("com.overhaulers.mod");

            // 1. Reactive invalidation patches (Pawn health, gene mutations, death, despawn)
            InstallInvalidationPatches(HarmonyInstance);

            // 2. Presentation and UI overlay patches (InfoCard labels, caravan inspector bars)
            InstallPresentationPatches(HarmonyInstance);

            // 3. Third-party mod integration and compatibility bridges (Must run before capacity patches)
            Guarded("IntegrationPipeline.Initialize", () => IntegrationPipeline.Initialize(HarmonyInstance));

            // 4. Native MassUtility.Capacity direct postfix bridge
            Guarded("MassUtility.Capacity", () => ApplyDirectCapacityPatch(HarmonyInstance));

            // 5. Strip the node an earlier version wrote into saves (avoids one-time "class not found" errors)
            Guarded("World.ExposeComponents", () => InstallLegacySaveCleanup(HarmonyInstance));

            // 6. Permanent branchless safety shields and main-menu fallbacks
            Guarded("Safety.InstallPermanentShields", () => InstallPermanentSafetyShields(HarmonyInstance));

            // Count total unique methods patched by this Harmony instance
            int patchedMethodCount = 0;
            var patchedMethods = HarmonyInstance.GetPatchedMethods();
            if (patchedMethods != null)
            {
                foreach (var _ in patchedMethods) patchedMethodCount++;
            }

            watch.Stop();

            OHLog.Integration.HarmonyInitialized(watch.Elapsed.TotalMilliseconds, patchedMethodCount);
        }

        #endregion

        #region 3. PRIVATE GUARDED EXECUTION & REFLECTION HELPERS

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

        #endregion
    }
}
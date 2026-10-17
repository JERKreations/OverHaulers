using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [INT-SAFE-01] PERMANENT STATIC SAFETY GUARDS & MAIN-MENU FALLBACKS
    /// Statically hooks <see cref="Pawn.Kill"/>, <see cref="Pawn_HealthTracker.CheckForStateChange"/>,
    /// <see cref="Pawn.HealthScale"/>, <see cref="StatWorker.GetValueUnfinalized"/>, and <see cref="Find"/> manager getters once at startup.
    /// Provides zero-cost, branchless protection against two-way mod interference:
    /// <list type="bullet">
    ///   <item><description><b>Outward Protection:</b> Completely suppresses state changes and death handlers on <see cref="HeadlessPawnShell"/> instances via <see cref="Priority.First"/> prefixes, preventing foreign mods from observing unspawned pawns.</description></item>
    ///   <item><description><b>Inward Protection:</b> Catches third-party mod exceptions thrown during dummy pawn evaluation via <see cref="Priority.Last"/> Harmony Finalizers, neutralizing foreign errors.</description></item>
    ///   <item><description><b>Main-Menu Fallbacks:</b> Provides fallback managers when <see cref="Current.Game"/> is null, preventing NREs during Main Menu calibration.</description></item>
    /// </list>
    /// Resides strictly under Source/Integration/Patches/.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. [INT-SAFE-01] FALLBACK ENGINE MANAGERS (MAIN-MENU EXECUTION)

        private static UniqueIDsManager fallbackUniqueIDsManager;
        private static TickManager fallbackTickManager;
        private static FactionManager fallbackFactionManager;

        #endregion

        #region 2. [INT-SAFE-02] PERMANENT SAFETY SHIELD REGISTRATION

        /// <summary>
        /// [INT-SAFE-02] Installs all permanent safety shields and main-menu fallback hooks.
        /// Invoked exactly once during <see cref="InitializeCompatibilityLayer"/>.
        /// </summary>
        /// <param name="harmony">The canonical Harmony instance for OverHaulers.</param>
        public static void InstallPermanentSafetyShields(Harmony harmony)
        {
            // 1. Main-Menu Find.* Fallbacks
            Guarded("Safety.UniqueIDsManager", () =>
            {
                var target = AccessTools.PropertyGetter(typeof(Find), nameof(Find.UniqueIDsManager));
                harmony.Patch(target, prefix: OwnPatch(nameof(UniqueIDsManager_Prefix)));
            });

            Guarded("Safety.TickManager", () =>
            {
                var target = AccessTools.PropertyGetter(typeof(Find), nameof(Find.TickManager));
                harmony.Patch(target, prefix: OwnPatch(nameof(TickManager_Prefix)));
            });

            Guarded("Safety.FactionManager", () =>
            {
                var target = AccessTools.PropertyGetter(typeof(Find), nameof(Find.FactionManager));
                harmony.Patch(target, prefix: OwnPatch(nameof(FactionManager_Prefix)));
            });

            // 2. Headless State-Change Shield (Priority.First)
            Guarded("Safety.CheckForStateChange", () =>
            {
                var target = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.CheckForStateChange));
                harmony.Patch(target, prefix: OwnPatch(nameof(CheckForStateChange_Prefix)));
            });

            // 3. Headless Death Shield (Priority.First)
            Guarded("Safety.Pawn.Kill", () =>
            {
                var target = AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill));
                harmony.Patch(target, prefix: OwnPatch(nameof(Pawn_Kill_Prefix)));
            });

            // 4. HealthScale Exception Neutralizer Finalizer (Priority.Last)
            Guarded("Safety.HealthScale", () =>
            {
                var target = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.HealthScale));
                harmony.Patch(target, finalizer: OwnPatch(nameof(HealthScale_Finalizer)));
            });

            // 5. StatWorker Exception Neutralizer Finalizer (Priority.Last)
            Guarded("Safety.StatWorker", () =>
            {
                var target = AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized));
                harmony.Patch(target, finalizer: OwnPatch(nameof(StatWorker_GetValueUnfinalized_Finalizer)));
            });
        }

        #endregion

        #region 3. [INT-SAFE-03] FALLBACK GETTER PREFIXES

        /// <summary>
        /// Provides a prefix patch for UniqueIDsManager, returning a fallback instance if the game is not yet initialized.
        /// </summary>
        /// <param name="__result">The result of the UniqueIDsManager retrieval.</param>
        /// <returns>False if the game is not yet initialized, preventing the original method from executing; true otherwise.</returns>
        private static bool UniqueIDsManager_Prefix(ref UniqueIDsManager __result)
        {
            if (Current.Game == null)
            {
                if (fallbackUniqueIDsManager == null) fallbackUniqueIDsManager = new UniqueIDsManager();
                __result = fallbackUniqueIDsManager;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Provides a prefix patch for TickManager, returning a fallback instance if the game is not yet initialized.
        /// </summary>
        /// <param name="__result">The result of the TickManager retrieval.</param>
        /// <returns>False if the game is not yet initialized, preventing the original method from executing; true otherwise.</returns>
        private static bool TickManager_Prefix(ref TickManager __result)
        {
            if (Current.Game == null)
            {
                if (fallbackTickManager == null) fallbackTickManager = new TickManager();
                __result = fallbackTickManager;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Provides a prefix patch for FactionManager, returning a fallback instance if the game is not yet initialized.
        /// </summary>
        /// <param name="__result">The result of the FactionManager retrieval.</param>
        /// <returns>False if the game is not yet initialized, preventing the original method from executing; true otherwise.</returns>
        private static bool FactionManager_Prefix(ref FactionManager __result)
        {
            if (Current.Game == null)
            {
                if (fallbackFactionManager == null) fallbackFactionManager = new FactionManager();
                __result = fallbackFactionManager;
                return false;
            }
            return true;
        }

        #endregion

        #region 4. [INT-SAFE-04] SHIELD TARGETS (INWARD & OUTWARD PROTECTION)

        /// <summary>
        /// [OUTWARD SHIELD] Completely suppresses health state checks on headless pawn shells.
        /// Priority.First guarantees this executes before foreign mod prefixes can crash on unspawned pawns.
        /// </summary>
        /// <param name="___pawn">The pawn instance being evaluated.</param>
        /// <returns>False if the pawn is a headless shell, preventing the original method from executing; true otherwise.</returns>
        [HarmonyPriority(Priority.First)]
        private static bool CheckForStateChange_Prefix(Pawn ___pawn)
        {
            if (___pawn != null && ___pawn.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// [OUTWARD SHIELD] Completely suppresses death execution and corpse instantiation on headless pawn shells.
        /// Priority.First guarantees external death listeners never observe the dummy entity.
        /// </summary>
        /// <param name="__instance">The pawn instance being killed.</param>
        /// <returns>False if the pawn is a headless shell, preventing the original method from executing; true otherwise.</returns>
        [HarmonyPriority(Priority.First)]
        private static bool Pawn_Kill_Prefix(Pawn __instance)
        {
            if (__instance != null && __instance.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// [INWARD SHIELD] Catches and neutralizes exceptions in <see cref="Pawn.HealthScale"/> on headless pawn shells.
        /// Priority.Last guarantees this catches unhandled errors from both engine code and third-party postfixes.
        /// </summary>
        /// <param name="__exception">The exception thrown by the original method, if any.</param>
        /// <param name="__instance">The pawn instance being evaluated.</param>
        /// <param name="__result">The result of the health scale evaluation.</param>
        [HarmonyPriority(Priority.Last)]
        private static Exception HealthScale_Finalizer(Exception __exception, Pawn __instance, ref float __result)
        {
            if (__instance != null && __instance.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId)
            {
                if (__exception != null)
                {
                    __result = __instance.RaceProps?.baseHealthScale ?? 1.0f;
                    return null;
                }
            }
            return __exception;
        }

        /// <summary>
        /// [INWARD SHIELD] Catches and neutralizes third-party postfix crashes during species baseline evaluation.
        /// </summary>
        /// <param name="__exception">The exception thrown by the original method, if any.</param>
        /// <param name="req">The stat request being evaluated.</param>
        /// <param name="__result">The result of the stat evaluation.</param>
        [HarmonyPriority(Priority.Last)]
        private static Exception StatWorker_GetValueUnfinalized_Finalizer(Exception __exception, StatRequest req, ref float __result)
        {
            if (__exception != null && SpeciesBaselineCalibration.IsResolvingBaseline)
            {
                if (req.HasThing && req.Thing is Pawn dummy && dummy.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId)
                {
                    return null;
                }
            }
            return __exception;
        }

        #endregion
    }
}
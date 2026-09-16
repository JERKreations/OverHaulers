using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Provides safety patches and fallback mechanisms for handling dummy pawns on the Main Menu, ensuring stability and preventing
    ///  third-party mod crashes.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. MAIN MENU FALLBACK LIFECYCLE & GETTER HOOKS

        private static readonly Harmony SafetyHarmony = new Harmony("com.overhaulers.mod.mainmenusafety");
        private static readonly object patchSyncRoot = new object();
        private static bool isSafetyPatched = false;

        private static UniqueIDsManager fallbackUniqueIDsManager;
        private static TickManager fallbackTickManager;
        private static FactionManager fallbackFactionManager;

        /// <summary>
        /// Dynamically attaches fallback hooks to Find.UniqueIDsManager, Find.TickManager, and Find.FactionManager.
        /// Strictly invoked only when manipulating dummy pawns on the Main Menu.
        /// </summary>
        public static void EnsureSafetyPatchesApplied()
        {
            if (isSafetyPatched || Current.ProgramState == ProgramState.Playing) return;

            lock (patchSyncRoot)
            {
                if (isSafetyPatched || Current.ProgramState == ProgramState.Playing) return;

                try
                {
                    var uniqueIDsGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.UniqueIDsManager));
                    var uniqueIDsPrefix = AccessTools.Method(typeof(HarmonySetup), nameof(UniqueIDsManager_Prefix));
                    if (uniqueIDsGetter != null && uniqueIDsPrefix != null)
                    {
                        SafetyHarmony.Patch(uniqueIDsGetter, prefix: new HarmonyMethod(uniqueIDsPrefix));
                    }

                    var tickManagerGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.TickManager));
                    var tickManagerPrefix = AccessTools.Method(typeof(HarmonySetup), nameof(TickManager_Prefix));
                    if (tickManagerGetter != null && tickManagerPrefix != null)
                    {
                        SafetyHarmony.Patch(tickManagerGetter, prefix: new HarmonyMethod(tickManagerPrefix));
                    }

                    var factionManagerGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.FactionManager));
                    var factionManagerPrefix = AccessTools.Method(typeof(HarmonySetup), nameof(FactionManager_Prefix));
                    if (factionManagerGetter != null && factionManagerPrefix != null)
                    {
                        SafetyHarmony.Patch(factionManagerGetter, prefix: new HarmonyMethod(factionManagerPrefix));
                    }

                    isSafetyPatched = true;
                }
                catch (Exception ex)
                {
                    OHLog.Integration.Warn("HarmonySetup_Safety", ex, "Failed to apply main menu fallback patches.");
                }
            }
        }

        /// <summary>
        /// Completely detaches fallback hooks from Find.UniqueIDsManager, Find.TickManager, and Find.FactionManager.
        /// Invoked when a world loads, guaranteeing 100% native execution speed during gameplay.
        /// </summary>
        public static void DeescalateSafetyPatches()
        {
            if (!isSafetyPatched) return;

            lock (patchSyncRoot)
            {
                if (!isSafetyPatched) return;

                try
                {
                    SafetyHarmony.UnpatchAll(SafetyHarmony.Id);
                    isSafetyPatched = false;
                }
                catch (Exception ex)
                {
                    OHLog.Integration.Warn("HarmonySetup_Safety", ex, "Failed to de-escalate main menu fallback patches.");
                }
            }
        }

        #endregion

        #region 2. PERMANENT HEADLESS SANDBOX SHIELDS

        /// <summary>
        /// Installs permanent, fast-path entity shields that protect against third-party mod crashes on dummy pawns.
        /// Does not hook global property getters.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the safety patches.</param>
        private static void InstallPermanentSandboxShields(Harmony harmony)
        {
            try
            {
                // 1. Headless Sandbox State-Change Shield
                var stateChangeMethod = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.CheckForStateChange));
                var stateChangePrefix = AccessTools.Method(typeof(HarmonySetup), nameof(CheckForStateChange_Prefix));
                if (stateChangeMethod != null && stateChangePrefix != null)
                {
                    harmony.Patch(stateChangeMethod, prefix: new HarmonyMethod(stateChangePrefix));
                }

                // 2. Headless Sandbox Death Shield
                var pawnKillMethod = AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill));
                var pawnKillPrefix = AccessTools.Method(typeof(HarmonySetup), nameof(Pawn_Kill_Prefix));
                if (pawnKillMethod != null && pawnKillPrefix != null)
                {
                    harmony.Patch(pawnKillMethod, prefix: new HarmonyMethod(pawnKillPrefix));
                }

                // 3. Headless Sandbox HealthScale Shield
                var healthScaleGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.HealthScale));
                var healthScaleFinalizer = AccessTools.Method(typeof(HarmonySetup), nameof(HealthScale_Finalizer));
                if (healthScaleGetter != null && healthScaleFinalizer != null)
                {
                    harmony.Patch(healthScaleGetter, finalizer: new HarmonyMethod(healthScaleFinalizer));
                }

                // 4. Headless Sandbox StatWorker Finalizer (Suppresses VEF Animal Genes NREs)
                var statWorkerMethod = AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized));
                var statWorkerFinalizer = AccessTools.Method(typeof(HarmonySetup), nameof(StatWorker_GetValueUnfinalized_Finalizer));
                if (statWorkerMethod != null && statWorkerFinalizer != null)
                {
                    harmony.Patch(statWorkerMethod, finalizer: new HarmonyMethod(statWorkerFinalizer));
                }
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("HarmonySetup_Safety", ex, "Failed to install permanent sandbox shields.");
            }
        }

        #endregion

        #region 3. FALLBACK GETTER PREFIX TARGETS

        /// <summary>
        /// Provides a prefix patch for UniqueIDsManager, returning a fallback instance if the game is not yet initialized.
        /// </summary>
        /// <param name="__result">The result of the UniqueIDsManager instance, either the fallback or the actual game instance.</param>
        /// <returns>False if the fallback instance is used, true otherwise.</returns>
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
        /// <param name="__result">The result of the TickManager instance, either the fallback or the actual game instance.</param>
        /// <returns>False if the fallback instance is used, true otherwise.</returns>
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
        /// <param name="__result">The result of the FactionManager instance, either the fallback or the actual game instance.</param>
        /// <returns>False if the fallback instance is used, true otherwise.</returns>
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

        #region 4. SANDBOX ENTITY SHIELD TARGETS

        /// <summary>
        /// Provides a prefix patch for checking state changes on pawns, bypassing the check for the sandbox dummy pawn.
        /// </summary>
        /// <param name="___pawn">The pawn being checked for state changes.</param>
        /// <returns>False if the pawn is the sandbox dummy pawn, true otherwise.</returns>
        private static bool CheckForStateChange_Prefix(Pawn ___pawn)
        {
            if (___pawn != null && ___pawn.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId) return false; 
            return true;
        }

        /// <summary>
        /// Provides a prefix patch for the Pawn.Kill method, bypassing the kill operation for the sandbox dummy pawn.
        /// </summary>
        /// <param name="__instance">The pawn instance being killed.</param>
        /// <returns>False if the pawn is the sandbox dummy pawn, true otherwise.</returns>
        private static bool Pawn_Kill_Prefix(Pawn __instance)
        {
            if (__instance != null && __instance.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId) return false; 
            return true;
        }

        /// <summary>
        /// Provides a finalizer patch for the HealthScale property, rescuing the base health scale for the sandbox dummy pawn if an exception occurs.
        /// </summary>
        /// <param name="__exception">The exception thrown during the original method execution, if any.</param>
        /// <param name="__instance">The pawn instance whose health scale is being evaluated.</param>
        /// <param name="__result">The result of the health scale calculation, which may be overridden in case of an exception.</param>
        /// <returns>Null if the exception is handled and suppressed, otherwise the original exception.</returns>
        private static Exception HealthScale_Finalizer(Exception __exception, Pawn __instance, ref float __result)
        {
            if (__instance != null && __instance.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId)
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
        /// [TEST-05] Finalizer shield on StatWorker.GetValueUnfinalized.
        /// Catches and suppresses third-party postfix crashes (e.g. VEF Animal Genes) during
        /// dummy pawn evaluation on the Main Menu, rescuing the successfully calculated base result.
        /// </summary>
        /// <param name="__exception">The exception thrown during the original method execution, if any.</param>
        /// <param name="req">The StatRequest being evaluated.</param>
        /// <param name="__result">The result of the stat calculation, which may be overridden in case of an exception.</param>
        /// <returns>Null if the exception is handled and suppressed, otherwise the original exception.</returns>
        private static Exception StatWorker_GetValueUnfinalized_Finalizer(Exception __exception, StatRequest req, ref float __result)
        {
            if (__exception != null && SpeciesBaselineCalibration.IsResolvingBaseline)
            {
                if (req.HasThing && req.Thing is Pawn dummy && dummy.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId)
                {
                    // Suppress the exception so we can rescue the baseline __result calculated before the crash
                    return null;
                }
            }
            return __exception;
        }

        #endregion
    }
}
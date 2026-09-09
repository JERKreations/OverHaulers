using System;
using UnityEngine;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-01] HARMONY BOOTSTRAP & MAIN MENU SAFETY PATCHES
    
    [StaticConstructorOnStartup]
    public static class HarmonySetup
    {
        /// <summary>Active Harmony instance handle used for dynamic driver rebinding.</summary>
        public static Harmony HarmonyInstance { get; private set; }

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
            HarmonyInstance = new Harmony("com.overhaulers.mod");

            InstallMainMenuSafetyPatches(HarmonyInstance);
            InstallInvalidationPatches(HarmonyInstance);
            IntegrationPipeline.Initialize(HarmonyInstance);
            ApplyDirectCapacityPatch(HarmonyInstance);
        }

        /// <summary>
        /// Applies the direct capacity patch to the MassUtility.Capacity method, allowing for custom postfix logic to be executed.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the patch.</param>
        private static void ApplyDirectCapacityPatch(Harmony harmony)
        {
            var originalMethod = AccessTools.Method(typeof(MassUtility), nameof(MassUtility.Capacity));
            var postfixMethod = AccessTools.Method(typeof(DirectPatchBridge), nameof(DirectPatchBridge.Postfix));

            if (originalMethod != null && postfixMethod != null)
            {
                harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
            }
        }

        private static void InstallMainMenuSafetyPatches(Harmony harmony)
        {
            try
            {
                // 1. Fallback UniqueIDsManager
                var uniqueIDsGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.UniqueIDsManager));
                var uniqueIDsPrefix = AccessTools.Method(typeof(MainMenuSafetyPatches), nameof(MainMenuSafetyPatches.UniqueIDsManager_Prefix));
                if (uniqueIDsGetter != null && uniqueIDsPrefix != null)
                {
                    harmony.Patch(uniqueIDsGetter, prefix: new HarmonyMethod(uniqueIDsPrefix));
                }

                // 2. Fallback TickManager
                var tickManagerGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.TickManager));
                var tickManagerPrefix = AccessTools.Method(typeof(MainMenuSafetyPatches), nameof(MainMenuSafetyPatches.TickManager_Prefix));
                if (tickManagerGetter != null && tickManagerPrefix != null)
                {
                    harmony.Patch(tickManagerGetter, prefix: new HarmonyMethod(tickManagerPrefix));
                }

                // 3. Fallback FactionManager
                var factionManagerGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.FactionManager));
                var factionManagerPrefix = AccessTools.Method(typeof(MainMenuSafetyPatches), nameof(MainMenuSafetyPatches.FactionManager_Prefix));
                if (factionManagerGetter != null && factionManagerPrefix != null)
                {
                    harmony.Patch(factionManagerGetter, prefix: new HarmonyMethod(factionManagerPrefix));
                }

                // 4. Headless Sandbox State-Change Shield
                var stateChangeMethod = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.CheckForStateChange));
                var stateChangePrefix = AccessTools.Method(typeof(MainMenuSafetyPatches), nameof(MainMenuSafetyPatches.CheckForStateChange_Prefix));
                if (stateChangeMethod != null && stateChangePrefix != null)
                {
                    harmony.Patch(stateChangeMethod, prefix: new HarmonyMethod(stateChangePrefix));
                }

                // 5. Headless Sandbox Death Shield
                var pawnKillMethod = AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill));
                var pawnKillPrefix = AccessTools.Method(typeof(MainMenuSafetyPatches), nameof(MainMenuSafetyPatches.Pawn_Kill_Prefix));
                if (pawnKillMethod != null && pawnKillPrefix != null)
                {
                    harmony.Patch(pawnKillMethod, prefix: new HarmonyMethod(pawnKillPrefix));
                }

                // 6. Headless Sandbox HealthScale Shield
                var healthScaleGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.HealthScale));
                var healthScaleFinalizer = AccessTools.Method(typeof(MainMenuSafetyPatches), nameof(MainMenuSafetyPatches.HealthScale_Finalizer));
                if (healthScaleGetter != null && healthScaleFinalizer != null)
                {
                    harmony.Patch(healthScaleGetter, finalizer: new HarmonyMethod(healthScaleFinalizer));
                }

                // 7. Headless Sandbox StatWorker Finalizer (Suppresses VEF Animal Genes NREs)
                var statWorkerMethod = AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized));
                var statWorkerFinalizer = AccessTools.Method(typeof(MainMenuSafetyPatches), nameof(MainMenuSafetyPatches.StatWorker_GetValueUnfinalized_Finalizer));
                if (statWorkerMethod != null && statWorkerFinalizer != null)
                {
                    harmony.Patch(statWorkerMethod, finalizer: new HarmonyMethod(statWorkerFinalizer));
                }
            }
            catch (Exception ex)
            {
                OHLog.Integration.WarnHarmonyPatchFailed(ex);
            }
        }

        private static void InstallInvalidationPatches(Harmony harmony)
        {
            try
            {
                var hediffChanged = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged));
                var hediffPostfix = AccessTools.Method(typeof(CacheInvalidationPatches), nameof(CacheInvalidationPatches.Notify_HediffChanged_Postfix));
                if (hediffChanged != null && hediffPostfix != null) harmony.Patch(hediffChanged, postfix: new HarmonyMethod(hediffPostfix));

                var apparelAdded = AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded));
                var apparelAddedPostfix = AccessTools.Method(typeof(CacheInvalidationPatches), nameof(CacheInvalidationPatches.Notify_ApparelAdded_Postfix));
                if (apparelAdded != null && apparelAddedPostfix != null) harmony.Patch(apparelAdded, postfix: new HarmonyMethod(apparelAddedPostfix));

                var apparelRemoved = AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved));
                var apparelRemovedPostfix = AccessTools.Method(typeof(CacheInvalidationPatches), nameof(CacheInvalidationPatches.Notify_ApparelRemoved_Postfix));
                if (apparelRemoved != null && apparelRemovedPostfix != null) harmony.Patch(apparelRemoved, postfix: new HarmonyMethod(apparelRemovedPostfix));

                var equipAdded = AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded));
                var equipAddedPostfix = AccessTools.Method(typeof(CacheInvalidationPatches), nameof(CacheInvalidationPatches.Notify_EquipmentAdded_Postfix));
                if (equipAdded != null && equipAddedPostfix != null) harmony.Patch(equipAdded, postfix: new HarmonyMethod(equipAddedPostfix));

                var equipRemoved = AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved));
                var equipRemovedPostfix = AccessTools.Method(typeof(CacheInvalidationPatches), nameof(CacheInvalidationPatches.Notify_EquipmentRemoved_Postfix));
                if (equipRemoved != null && equipRemovedPostfix != null) harmony.Patch(equipRemoved, postfix: new HarmonyMethod(equipRemovedPostfix));

                var pawnKill = AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill));
                var pawnKillPostfix = AccessTools.Method(typeof(CacheInvalidationPatches), nameof(CacheInvalidationPatches.Notify_PawnKilled_Postfix));
                if (pawnKill != null && pawnKillPostfix != null) harmony.Patch(pawnKill, postfix: new HarmonyMethod(pawnKillPostfix));

                var widgetsLabel = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new Type[] { typeof(Rect), typeof(string) });
                var labelPrefix = AccessTools.Method(typeof(InfoCardOverlay), nameof(InfoCardOverlay.Prefix));
                if (widgetsLabel != null && labelPrefix != null) harmony.Patch(widgetsLabel, prefix: new HarmonyMethod(labelPrefix));

                var caravanExplanationGetter = AccessTools.PropertyGetter(typeof(RimWorld.Planet.Caravan), "MassCapacityExplanation");
                var caravanExplanationPostfix = AccessTools.Method(typeof(CaravanUIIntegration), nameof(CaravanUIIntegration.Caravan_MassCapacityExplanation_Postfix));
                if (caravanExplanationGetter != null && caravanExplanationPostfix != null) harmony.Patch(caravanExplanationGetter, postfix: new HarmonyMethod(caravanExplanationPostfix));

                var drawCaravanInfoPrefix = AccessTools.Method(typeof(CaravanUIIntegration), nameof(CaravanUIIntegration.DrawCaravanInfo_Prefix));
                var drawMethods = typeof(RimWorld.Planet.CaravanUIUtility).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                for (int i = 0; i < drawMethods.Length; i++)
                {
                    var m = drawMethods[i];
                    if (m.Name == nameof(RimWorld.Planet.CaravanUIUtility.DrawCaravanInfo))
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(drawCaravanInfoPrefix));
                    }
                }
            }
            catch (Exception ex)
            {
                OHLog.Integration.WarnHarmonyPatchFailed(ex);
            }
        }
    }

    #endregion

    #region 2. MAIN MENU SAFETY PATCHES

    public static class MainMenuSafetyPatches
    {
        private static UniqueIDsManager fallbackUniqueIDsManager;
        private static TickManager fallbackTickManager;
        private static FactionManager fallbackFactionManager;

        public static bool UniqueIDsManager_Prefix(ref UniqueIDsManager __result)
        {
            if (Current.Game == null)
            {
                if (fallbackUniqueIDsManager == null) fallbackUniqueIDsManager = new UniqueIDsManager();
                __result = fallbackUniqueIDsManager;
                return false; 
            }
            return true;
        }

        public static bool TickManager_Prefix(ref TickManager __result)
        {
            if (Current.Game == null)
            {
                if (fallbackTickManager == null) fallbackTickManager = new TickManager();
                __result = fallbackTickManager;
                return false; 
            }
            return true;
        }

        public static bool FactionManager_Prefix(ref FactionManager __result)
        {
            if (Current.Game == null)
            {
                if (fallbackFactionManager == null) fallbackFactionManager = new FactionManager();
                __result = fallbackFactionManager;
                return false; 
            }
            return true;
        }

        public static bool CheckForStateChange_Prefix(Pawn ___pawn)
        {
            if (___pawn != null && ___pawn.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId) return false; 
            return true;
        }

        public static bool Pawn_Kill_Prefix(Pawn __instance)
        {
            if (__instance != null && __instance.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId) return false; 
            return true;
        }

        public static Exception HealthScale_Finalizer(Exception __exception, Pawn __instance, ref float __result)
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
        public static Exception StatWorker_GetValueUnfinalized_Finalizer(Exception __exception, StatRequest req, ref float __result)
        {
            if (__exception != null && ModpackBaselineCalibration.IsResolvingBaseline)
            {
                if (req.HasThing && req.Thing is Pawn dummy && dummy.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId)
                {
                    // Suppress the exception so we can rescue the baseline __result calculated before the crash
                    return null;
                }
            }
            return __exception;
        }
    }

    #endregion

    #region 3. HARMONY PATCH DRIVER

    public static class DirectPatchBridge
    {
        public static void Postfix(Pawn p, ref float __result, System.Text.StringBuilder explanation)
        {
            if (p == null) return;
            IntegrationPipeline.ActiveDriver?.OnMassUtilityCapacityPostfix(p, ref __result, explanation);
        }
    }

    #endregion
}
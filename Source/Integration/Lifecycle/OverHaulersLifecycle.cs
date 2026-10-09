using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    #region 1. [LIFE-01] SAVE-FREE SESSION LIFECYCLE DRIVER

    /// <summary>
    /// [LIFE-01] Bootstraps the lifecycle driver once at startup.
    /// Over Haulers deliberately has no WorldComponent/GameComponent: RimWorld records those by class name inside the save file
    /// and logs errors when the class is missing, which would make the mod impossible to remove mid-save cleanly.
    /// A persistent, scene-independent Unity object is used instead, so nothing of ours is ever written to a save.
    /// Resides under Source/Integration/Lifecycle/.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class OverHaulersLifecycle
    {
        static OverHaulersLifecycle()
        {
            GameObject host = new GameObject("OverHaulers_Lifecycle");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<OverHaulersLifecycleDriver>();
        }
    }

    /// <summary>
    /// Per-frame driver that detects game sessions (new game, load, return to menu) by tracking the identity of
    /// <see cref="Current.Game"/>, initialises runtime state once per session and performs the periodic tick work
    /// (cache cleanup, deferred calibration sweep, telemetry reports). Holds runtime state only; persists nothing.
    /// </summary>
    public sealed class OverHaulersLifecycleDriver : MonoBehaviour
    {
        private Game trackedGame;
        private int lastSeenTick = int.MinValue;
        private int lastReportTick = -1;
        private int coreInitializedTick = -1;
        private bool isCalibrationSweepDone;

        /// <summary>
        /// Unity per-frame callback; runs one pump and keeps any failure from escaping into the engine loop.
        /// </summary>
        private void Update()
        {
            try
            {
                Pump();
            }
            catch (System.Exception ex)
            {
                OHLog.Lifecycle.Warn("LifecycleDriver", ex, "Lifecycle driver update failed.");
            }
        }

        /// <summary>
        /// Advances the benchmark and self-check time slices, detects session changes and performs the periodic tick work once a game is playing.
        /// </summary>
        private void Pump()
        {
            // Benchmark time slices run in every program state, including the main menu.
            BenchmarkRunner.Pump();
            SelfCheckRunner.Pump();

            // ProgramState only becomes Playing at the very end of Game.FinalizeInit, on the main thread,
            // so a session is never started while a save is still being loaded.
            if (Current.ProgramState != ProgramState.Playing)
            {
                trackedGame = null;
                return;
            }

            Game game = Current.Game;
            TickManager tickManager = game?.tickManager;
            if (tickManager == null) return;

            if (!ReferenceEquals(game, trackedGame))
            {
                trackedGame = game;
                BeginSession(tickManager.TicksGame);
            }

            if (tickManager.Paused) return;

            int currentTick = tickManager.TicksGame;
            if (currentTick == lastSeenTick) return;
            lastSeenTick = currentTick;

            MaybeRunDeferredCalibrationSweep(currentTick);
            PawnDataRegistry.UpdateTick(currentTick);
            MaybeReportTelemetry(currentTick);
        }

        /// <summary>
        /// Resets all runtime state for a freshly started or loaded game.
        /// </summary>
        private void BeginSession(int currentTick)
        {
            BenchmarkRunner.Cancel();
            SelfCheckRunner.Cancel();
            lastSeenTick = int.MinValue;
            lastReportTick = -1;
            isCalibrationSweepDone = false;

            // 1. De-escalate main-menu fallback hooks immediately upon entering world state
            HarmonySetup.DeescalateSafetyPatches();

            // 2. Clear all cached pawn data for a clean slate
            PawnDataRegistry.ClearAllCaches();

            // 3. Freshly compile all species skeletal topologies (un-locked across save loads)
            TopologyLayoutCompiler.InvalidateAllTopologies();
            TopologyLayoutCompiler.EnsureInitialized();

            // 4. Pre-index all medical recipes and stimulants
            MedicalRecipeCatalog.InitializeCatalog();

            // 5. Reset dynamic baseline calibrations (batch sweep itself is deferred - see MaybeRunDeferredCalibrationSweep)
            SpeciesBaselineCalibration.Reset();

            coreInitializedTick = currentTick;

            // 6. Reset the lifecycle log
            OHLog.Lifecycle.WorldLoadedReset();
        }

        /// <summary>
        /// The initial calibration sweep is deferred a short while after a session starts.
        /// Some externally-patched caravan-capacity stat providers (e.g. third-party mod stats) build their own
        /// per-species data lazily and aren't guaranteed ready immediately - a short delay lets them settle
        /// before we pre-calibrate against them, avoiding species being misclassified as needing live-pawn rescue.
        /// </summary>
        /// <param name="currentTick">The current game tick used to determine if the deferred calibration sweep should run.</param>
        private void MaybeRunDeferredCalibrationSweep(int currentTick)
        {
            if (isCalibrationSweepDone || coreInitializedTick < 0) return;

            int elapsed = currentTick - coreInitializedTick;
            if (elapsed < SettingsDefaults.WorldLoadCalibrationDelayTicks && elapsed >= 0) return;

            isCalibrationSweepDone = true;

            // BATCH PRE-CALIBRATION SWEEP: Pre-calibrate caravan species ThingDefs in one fast sweep
            SpeciesBaselineCalibration.RunBatchSweep(onlyCaravanCapable: true, "OverHaulers_SweepContext_WorldLoad".Translate().ToString());

            // WARM-UP SWEEP: Pre-populate background MassSnapshotCache for active caravan pawns
            WarmupActivePawns();
        }

        /// <summary>
        /// Evaluates active caravan-capable pawns on maps and in caravans, pre-populating
        /// MassSnapshotCache before background pathfinding threads query it.
        /// </summary>
        private void WarmupActivePawns()
        {
            // Early exit if the integration pipeline is not initialized
            if (!IntegrationPipeline.IsInitialized) return;

            // Sweep Map Pawns
            if (Find.Maps != null)
            {
                for (int m = 0; m < Find.Maps.Count; m++)
                {
                    Map map = Find.Maps[m];
                    if (map?.mapPawns == null) continue;

                    var pawns = map.mapPawns.AllPawnsSpawned;
                    for (int p = 0; p < pawns.Count; p++)
                    {
                        EvaluatePawnForWarmup(pawns[p]);
                    }
                }
            }

            // Sweep World Caravans
            if (Find.WorldObjects?.Caravans != null)
            {
                var caravans = Find.WorldObjects.Caravans;
                for (int c = 0; c < caravans.Count; c++)
                {
                    Caravan caravan = caravans[c];
                    if (caravan?.PawnsListForReading == null) continue;

                    var pawns = caravan.PawnsListForReading;
                    for (int p = 0; p < pawns.Count; p++)
                    {
                        EvaluatePawnForWarmup(pawns[p]);
                    }
                }
            }
        }

        /// <summary>
        /// Evaluates a pawn for warmup, pre-populating the mass snapshot cache if the pawn is capable of carrying caravan mass.
        /// </summary>
        /// <param name="pawn">The pawn to evaluate for warmup.</param>
        private void EvaluatePawnForWarmup(Pawn pawn)
        {
            if (pawn != null && PawnDataRegistry.CanCarryCaravanMass(pawn))
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
                PawnDataRegistry.GetOffset(pawn, baseline);
            }
        }

        /// <summary>
        /// Dispatches the periodic performance telemetry report when the configured interval has elapsed.
        /// </summary>
        /// <param name="currentTick">The current game tick.</param>
        private void MaybeReportTelemetry(int currentTick)
        {
            if (lastReportTick < 0)
            {
                lastReportTick = currentTick;
            }

            int intervalTicks = OverHaulers.settings != null
                ? OverHaulers.settings.ReportMetricsIntervalTicks
                : SettingsDefaults.ReportMetricsIntervalHours * GenDate.TicksPerHour;

            if (intervalTicks <= 0) return;

            if (currentTick - lastReportTick >= intervalTicks || currentTick < lastReportTick)
            {
                lastReportTick = currentTick;
                int hours = OverHaulers.settings?.reportMetricsIntervalHours ?? SettingsDefaults.ReportMetricsIntervalHours;
                PerformanceTelemetry.GenerateAndDispatchReport(hours);
            }
        }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        /// <summary>True once the post-load deferred baseline calibration sweep has finished.</summary>
        public static bool IsCalibrationSweepDone { get; internal set; }

        /// <summary>
        /// Initializes the Over Haulers lifecycle driver, mounts the persistent Unity GameObject, subscribes to cache events, and triggers
        ///  the deterministic bootstrap sequence.
        /// Invoked automatically by the Unity engine at startup due to the <see cref="StaticConstructorOnStartup"/> attribute.
        /// Ensures that all necessary subsystems and compatibility layers are initialized before any gameplay logic executes.
        /// </summary>
        static OverHaulersLifecycle()
        {
            // 1. Mount persistent lifecycle driver GameObject into Unity engine
            GameObject host = new GameObject("OverHaulers_Lifecycle");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<OverHaulersLifecycleDriver>();

            // Subscribe to Core cache flush events to automatically re-warm colony pawns
            PawnDataRegistry.OnCacheCleared += HandleCacheCleared;

            // 2. Begin timed bootstrap sequence
            var watch = System.Diagnostics.Stopwatch.StartNew();
            OHLog.ActivityJournal.BeginSession("OverHaulers_Journal_BootstrapHeader".Translate("...").ToString());

            // DETERMINISTIC BOOTSTRAP SEQUENCE:
            // 1. Stat Adoption & Harmony Compatibility Layer
            HarmonySetup.EnsureInitialized();

            // 2. Static Skeletons
            TopologyLayoutCompiler.EnsureInitialized();

            // 3. Atomic Snapshot Registry
            MassSnapshotCache.EnsureInitialized();

            watch.Stop();
            OHLog.ActivityJournal.SetHeader("OverHaulers_Journal_BootstrapHeader".Translate(watch.Elapsed.TotalMilliseconds.ToString("F1")).ToString());
            OHLog.ActivityJournal.Dispatch();
        }

        /// <summary>
        /// Handles downward cache clear events by re-warming active colony pawns if a game is actively playing.
        /// </summary>
        private static void HandleCacheCleared()
        {
            InfoCardOverlay.ClearCache();

            if (Current.ProgramState == ProgramState.Playing && IsCalibrationSweepDone)
            {
                WarmupActivePawns();
            }
        }

        /// <summary>
        /// [LIFE-02] Canonical lifecycle entry point for full runtime re-initialization and cache recompilation.
        /// Orchestrates teardown and rebuilding of Core and Integration systems in strict topological order.
        /// Consumed by the Test Bench without inverting downward layer directionality.
        /// </summary>
        /// <param name="onlyCaravanCapable">If true, filters species sweeps to caravan-capable fauna.</param>
        /// <param name="sweepContext">Human-readable label for the batch sweep summary log.</param>
        public static void ReinitializeRuntime(bool onlyCaravanCapable = true, string sweepContext = null)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            BenchmarkRunner.Cancel();
            SelfCheckRunner.Cancel();

            // Open Recompile Activity Journal
            OHLog.ActivityJournal.BeginSession("OverHaulers_Journal_RecompileHeader".Translate("...").ToString());

            // 1. Flush Core caches, medical classifier caches, and UI layout typography
            PawnDataRegistry.ClearAllCaches();
            DetailedModelRegistry.Clear();
            InfoCardOverlay.ClearCache();
            MedicalClassifier.ClearStaticCaches();

            // 2. Invalidate and recompile all skeletal topologies (routes bullet #1)
            TopologyLayoutCompiler.InvalidateAllTopologies();
            TopologyLayoutCompiler.EnsureInitialized();

            // 3. Reset and re-run baseline calibration sweep (routes bullet #2)
            SpeciesBaselineCalibration.Reset();
            string context = !string.IsNullOrEmpty(sweepContext)
                ? sweepContext
                : "OverHaulers_Log_Calibration_Context_RecompileAll".Translate().ToString();
            SpeciesBaselineCalibration.RunBatchSweep(onlyCaravanCapable, context);

            // 4. Re-index medical surgery and implant catalog (routes bullet #3)
            MedicalRecipeCatalog.ClearAndReinitialize();

            // 5. Mark calibration ready and proactively re-warm all active pawns
            IsCalibrationSweepDone = true;
            WarmupActivePawns();

            watch.Stop();

            // Update headline with final total duration and dispatch unified report
            OHLog.ActivityJournal.SetHeader("OverHaulers_Journal_RecompileHeader".Translate(watch.Elapsed.TotalMilliseconds.ToString("F1")).ToString());
            OHLog.ActivityJournal.Dispatch();
        }

        /// <summary>
        /// [LIFE-03] Evaluates active caravan-capable pawns on maps and in caravans, pre-populating
        /// Core caches and the atomic snapshot table before pathfinding threads query them.
        /// </summary>
        public static void WarmupActivePawns()
        {
            // Early exit if the integration pipeline is not initialized or game is not playing
            if (!IntegrationPipeline.IsInitialized || Current.ProgramState != ProgramState.Playing) return;

            int warmedCount = 0;

            try
            {
                // Sweep Map Pawns
                if (Find.Maps != null)
                {
                    for (int m = 0; m < Find.Maps.Count; m++)
                    {
                        Map map = Find.Maps[m];
                        if (map?.mapPawns == null) continue;

                        IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                        if (pawns == null) continue;

                        for (int p = 0; p < pawns.Count; p++)
                        {
                            if (EvaluatePawnForWarmup(pawns[p])) warmedCount++;
                        }
                    }
                }

                // Sweep World Caravans
                if (Find.WorldObjects?.Caravans != null)
                {
                    List<Caravan> caravans = Find.WorldObjects.Caravans;
                    for (int c = 0; c < caravans.Count; c++)
                    {
                        Caravan caravan = caravans[c];
                        if (caravan?.PawnsListForReading == null) continue;

                        IReadOnlyList<Pawn> pawns = caravan.PawnsListForReading;
                        if (pawns == null) continue;

                        for (int p = 0; p < pawns.Count; p++)
                        {
                            if (EvaluatePawnForWarmup(pawns[p])) warmedCount++;
                        }
                    }
                }

                if (warmedCount > 0 && OHLog.ActivityJournal.IsActive)
                {
                    OHLog.ActivityJournal.RecordBullet("OverHaulers_Journal_ActivePawnsWarmedUp".Translate(warmedCount).ToString());
                }
            }
            catch (Exception ex)
            {
                OHLog.Lifecycle.Warn("WarmupActivePawns", ex, "Failed to warm up active player pawns.");
            }
        }

        /// <summary>
        /// Evaluates a pawn for warmup, seating it in PawnDataRegistry and MassSnapshotCache.
        /// </summary>
        /// <param name="pawn">The pawn to evaluate for warmup.</param>
        /// <returns>True if the pawn was valid and evaluated; false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EvaluatePawnForWarmup(Pawn pawn)
        {
            if (pawn != null && !pawn.Dead && !pawn.Suspended && PawnDataRegistry.CanCarryCaravanMass(pawn))
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
                PawnDataRegistry.GetOffset(pawn, baseline);
                return true;
            }
            return false;
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
                if (trackedGame != null)
                {
                    TeardownSession();
                    trackedGame = null;
                }
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
        /// Flushes runtime caches and discards pending journal sessions when exiting to the Main Menu.
        /// </summary>
        private void TeardownSession()
        {
            BenchmarkRunner.Cancel();
            SelfCheckRunner.Cancel();
            OHLog.ActivityJournal.Discard();

            PawnDataRegistry.ClearAllCaches();
            DetailedModelRegistry.Clear();
            InfoCardOverlay.ClearCache();
            SpeciesBaselineCalibration.Reset();
        }

        /// <summary>
        /// Resets all runtime state for a freshly started or loaded game session.
        /// </summary>
        private void BeginSession(int currentTick)
        {
            BenchmarkRunner.Cancel();
            SelfCheckRunner.Cancel();
            lastSeenTick = int.MinValue;
            lastReportTick = -1;
            OverHaulersLifecycle.IsCalibrationSweepDone = false;

            // Open the multi-tick World Session Journal
            OHLog.ActivityJournal.BeginSession("OverHaulers_Journal_WorldSessionHeader".Translate().ToString());

            // 1. Clear all cached pawn data, presentation models, and UI layout typography
            int snapshotPurged = MassSnapshotCache.OccupiedSlotsCount;
            PawnDataRegistry.ClearAllCaches();
            OHLog.ActivityJournal.RecordBullet("OverHaulers_Journal_SnapshotReset".Translate(
                MassSnapshotCache.TableCapacity, 
                snapshotPurged
            ).ToString());

            int presPurged = DetailedModelRegistry.ActiveEntryCount;
            DetailedModelRegistry.Clear();
            InfoCardOverlay.ClearCache();
            if (presPurged > 0)
            {
                OHLog.ActivityJournal.RecordBullet("OverHaulers_Journal_PresentationReset".Translate(
                    presPurged, 
                    DetailedModelRegistry.DefaultBaseCapacity
                ).ToString());
            }

            // 2. Preserve static topologies (idempotent: does NOT recompile if already initialized at boot!)
            TopologyLayoutCompiler.EnsureInitialized();
            OHLog.ActivityJournal.RecordBullet("OverHaulers_Journal_TopologyPreserved".Translate(
                TopologyLayoutCompiler.CompiledTemplateCount
            ).ToString());

            // 3. Reset dynamic baseline calibrations (batch sweep will run at Tick 10 and finish the journal)
            SpeciesBaselineCalibration.Reset();

            coreInitializedTick = currentTick;

            // Register deferred calibration ticket and dispatch (waits until Tick 10 before printing)
            OHLog.ActivityJournal.RegisterDeferredOperation("Calibration");
            OHLog.ActivityJournal.Dispatch();
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
            if (OverHaulersLifecycle.IsCalibrationSweepDone || coreInitializedTick < 0) return;

            int elapsed = currentTick - coreInitializedTick;
            if (elapsed < SettingsDefaults.WorldLoadCalibrationDelayTicks && elapsed >= 0) return;

            OverHaulersLifecycle.IsCalibrationSweepDone = true;

            // BATCH PRE-CALIBRATION SWEEP: Automatically routes bullet into active ActivityJournal
            SpeciesBaselineCalibration.RunBatchSweep(onlyCaravanCapable: true, "OverHaulers_Log_Calibration_Context_WorldLoad".Translate().ToString());

            // WARM-UP SWEEP: Pre-populate background MassSnapshotCache for active caravan pawns
            OverHaulersLifecycle.WarmupActivePawns();

            // BREAKPOINT ANCHOR: Complete deferred calibration operation (triggers atomic report emission)
            OHLog.ActivityJournal.CompleteDeferredOperation("Calibration");
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
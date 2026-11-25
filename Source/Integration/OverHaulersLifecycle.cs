using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace OverHaulers
{
    #region 1. [LIFE-01] SAVE-FREE SESSION LIFECYCLE BOOTSTRAPPER

    /// <summary>
    /// [LIFE-01] Bootstraps the lifecycle driver once at startup.
    /// Over Haulers deliberately has no WorldComponent/GameComponent: RimWorld records those by class name inside the save file
    /// and logs errors when the class is missing, which would make the mod impossible to remove mid-save cleanly.
    /// A persistent, scene-independent Unity object is used instead, so nothing of ours is ever written to a save.
    /// Resides under Source/Integration/.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class OverHaulersLifecycle
    {
        #region 1A. Static State & Properties
        /// <summary>True once the post-load deferred baseline calibration sweep has finished.</summary>
        public static bool IsCalibrationSweepDone { get; internal set; }
        #endregion

        #region 1B. Bootstrap Lifecycle
        /// <summary>
        /// Initializes the Over Haulers lifecycle driver, mounts the persistent Unity GameObject, subscribes to cache events, and triggers
        /// the deterministic bootstrap sequence.
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
            if (Current.ProgramState == ProgramState.Playing && IsCalibrationSweepDone)
            {
                WarmupActivePawns();
            }
        }
        #endregion

        #region 1C. Public Runtime Re-Initialization API
        /// <summary>
        /// [LIFE-02] Canonical lifecycle entry point for full runtime re-initialization and cache recompilation.
        /// Orchestrates teardown and rebuilding of Core systems in strict topological order.
        /// Decoupled from Presentation and Settings layers.
        /// </summary>
        /// <param name="onlyCaravanCapable">If true, filters species sweeps to caravan-capable fauna.</param>
        public static void ReinitializeRuntime(bool onlyCaravanCapable = true)
        {
            IsCalibrationSweepDone = false;
            LifecycleHooks.RaiseRuntimeResetting();

            // 1. Flush Core caches and medical classifier caches. Keep cache-clear callbacks from warming pawns before recompilation.
            PawnDataRegistry.ClearAllCaches();
            MedicalClassifier.ClearStaticCaches();

            // 2. Invalidate and recompile all skeletal topologies
            TopologyLayoutCompiler.InvalidateAllTopologies();
            TopologyLayoutCompiler.EnsureInitialized();

            // 3. Reset and re-run baseline calibration sweep
            SpeciesBaselineCalibration.Reset();
            SpeciesBaselineCalibration.RunBatchSweep(onlyCaravanCapable, finalizeWorldLoad: Current.ProgramState == ProgramState.Playing);

            // 4. Mark calibration ready and proactively re-warm all active pawns
            IsCalibrationSweepDone = true;
            WarmupActivePawns();
        }
        #endregion

        #region 1D. Warmup Engine & Predicates
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
                OHLog.Warn("Lifecycle.WarmupActivePawns", ex, "Failed to warm up active player pawns.");
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
        #endregion
    }

    #endregion

    #region 2. [LIFE-02] SCENE-INDEPENDENT UNITY LIFECYCLE DRIVER

    /// <summary>
    /// Per-frame driver that detects game sessions (new game, load, return to menu) by tracking the identity of
    /// <see cref="Current.Game"/>, initialises runtime state once per session and performs the periodic tick work
    /// (cache cleanup, adaptive calibration verification, telemetry reports). Holds runtime state only; persists nothing.
    /// </summary>
    public sealed class OverHaulersLifecycleDriver : MonoBehaviour
    {
        #region 2A. Backing State
        /// <summary>Maximum delay in ticks between world load and final sentinel/fallback verification.</summary>
        private const int WorldLoadCalibrationDelayTicks = 120;

        private Game trackedGame;
        private int lastSeenTick = int.MinValue;
        private int lastReportTick = -1;
        private int coreInitializedTick = -1;
        private int nextAdaptiveRetryTick = -1;
        #endregion

        #region 2B. Unity Callbacks
        /// <summary>
        /// Unity per-frame callback; runs one pump and keeps any failure from escaping into the engine loop.
        /// </summary>
        private void Update()
        {
            try
            {
                Pump();
            }
            catch (Exception ex)
            {
                OHLog.Warn("Lifecycle.LifecycleDriver", ex, "Lifecycle driver update failed.");
            }
        }
        #endregion

        #region 2C. Session Pump & Dispatch
        /// <summary>
        /// Advances the benchmark and self-check time slices, detects session changes and performs the periodic tick work once a game is playing.
        /// </summary>
        private void Pump()
        {
            // Tooling time slices (benchmark, self-check) run in every program state, including the main menu.
            LifecycleHooks.RaiseFramePumped();

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

            MaybeRunAdaptiveCalibrationChecks(currentTick);
            PawnDataRegistry.UpdateTick(currentTick);
            MaybeReportTelemetry(currentTick);
        }

        /// <summary>
        /// Flushes runtime caches and discards pending journal sessions when exiting to the Main Menu.
        /// </summary>
        private void TeardownSession()
        {
            LifecycleHooks.RaiseRuntimeResetting();
            OHLog.ActivityJournal.Discard();

            PawnDataRegistry.ClearAllCaches();
            DetailedModelRegistry.Clear();
            InfoCardOverlay.ClearCache();
            SpeciesBaselineCalibration.Reset();
            nextAdaptiveRetryTick = -1;
        }

        /// <summary>
        /// Resets all runtime state for a freshly started or loaded game session and runs immediate tick-0 calibration.
        /// </summary>
        private void BeginSession(int currentTick)
        {
            LifecycleHooks.RaiseRuntimeResetting();
            lastSeenTick = int.MinValue;
            lastReportTick = -1;
            nextAdaptiveRetryTick = -1;
            OverHaulersLifecycle.IsCalibrationSweepDone = false;

            // Open the World Session Journal
            OHLog.ActivityJournal.BeginSession("OverHaulers_Journal_WorldSessionHeader".Translate().ToString());

            // 1. Verify stat adoption integrity against late static startup mods
            IntegrationPipeline.VerifyAdoptedStatIntegrity();

            // 2. Clear all cached pawn data, presentation models, and UI layout typography
            int snapshotPurged = MassSnapshotCache.OccupiedSlotsCount;
            int presPurged = DetailedModelRegistry.ActiveEntryCount;
            PawnDataRegistry.ClearAllCaches();
            OHLog.ActivityJournal.RecordBullet("OverHaulers_Event_Snapshot_Cleared".Translate(
                snapshotPurged,
                MassSnapshotCache.TableCapacity
            ).ToString());

            DetailedModelRegistry.Clear();
            InfoCardOverlay.ClearCache();
            if (presPurged > 0)
            {
                OHLog.ActivityJournal.RecordBullet("OverHaulers_Journal_PresentationReset".Translate(
                    presPurged,
                    DetailedModelRegistry.DefaultBaseCapacity
                ).ToString());
            }

            // 3. Preserve static topologies if still compiled; otherwise EnsureInitialized() logs the recompile directly.
            bool topologiesPreserved = TopologyLayoutCompiler.IsCompiled;
            TopologyLayoutCompiler.EnsureInitialized();
            if (topologiesPreserved)
            {
                OHLog.ActivityJournal.RecordBullet("OverHaulers_Journal_TopologyPreserved".Translate(
                    TopologyLayoutCompiler.CompiledTemplateCount
                ).ToString());
            }

            // 4. Immediately calibrate species ready at FinalizeInit (pre-finalization so slow external mods stay retryable)
            SpeciesBaselineCalibration.Reset();
            SpeciesBaselineCalibration.RunBatchSweep(onlyCaravanCapable: true, finalizeWorldLoad: false);
            SpeciesBaselineCalibration.CaptureSentinelSnapshot();

            // 5. Warm up active caravan pawns immediately so even paused sessions start with populated snapshots
            OverHaulersLifecycle.WarmupActivePawns();

            coreInitializedTick = currentTick;
            if (SpeciesBaselineCalibration.HasPendingWorldLoadEntries)
            {
                nextAdaptiveRetryTick = currentTick + 1;
            }

            OHLog.ActivityJournal.Dispatch();
        }
        #endregion

        #region 2D. Maintenance Drivers & Adaptive Calibration Verification
        /// <summary>
        /// Incrementally retries only species that failed tick-0 calibration as early as tick 1/15/60,
        /// then executes a lightweight sentinel check at tick 120 to catch external mods that swap in
        /// real carry-weight formulas only after their first game tick.
        /// </summary>
        /// <param name="currentTick">The current game tick.</param>
        private void MaybeRunAdaptiveCalibrationChecks(int currentTick)
        {
            if (OverHaulersLifecycle.IsCalibrationSweepDone || coreInitializedTick < 0) return;

            int elapsed = currentTick - coreInitializedTick;
            if (elapsed < 0)
            {
                coreInitializedTick = currentTick;
                return;
            }

            // 1. Dynamic early retry for any species that failed tick-0 calibration
            if (nextAdaptiveRetryTick != -1 &&
                currentTick >= nextAdaptiveRetryTick &&
                elapsed < WorldLoadCalibrationDelayTicks)
            {
                if (SpeciesBaselineCalibration.HasPendingWorldLoadEntries)
                {
                    SpeciesBaselineCalibration.RetryPendingWorldLoadEntries(finalizeWorldLoad: false);
                    PawnDataRegistry.MarkAllStale();
                    OverHaulersLifecycle.WarmupActivePawns();
                }

                if (!SpeciesBaselineCalibration.HasPendingWorldLoadEntries)
                {
                    nextAdaptiveRetryTick = -1;
                }
                else
                {
                    nextAdaptiveRetryTick = elapsed < 15
                        ? coreInitializedTick + 15
                        : coreInitializedTick + 60;
                }
            }

            if (elapsed < WorldLoadCalibrationDelayTicks) return;

            // 2. Final checkpoint: verify sentinel parity and finalize any remaining pending species
            OverHaulersLifecycle.IsCalibrationSweepDone = true;
            nextAdaptiveRetryTick = -1;

            if (SpeciesBaselineCalibration.HasSentinelDrifted())
            {
                SpeciesBaselineCalibration.Reset();
                SpeciesBaselineCalibration.RunBatchSweep(onlyCaravanCapable: true, finalizeWorldLoad: true);
                PawnDataRegistry.MarkAllStale();
                OverHaulersLifecycle.WarmupActivePawns();
                return;
            }

            if (SpeciesBaselineCalibration.HasPendingWorldLoadEntries)
            {
                SpeciesBaselineCalibration.RetryPendingWorldLoadEntries(finalizeWorldLoad: true);
                PawnDataRegistry.MarkAllStale();
                OverHaulersLifecycle.WarmupActivePawns();
                return;
            }

            SpeciesBaselineCalibration.MarkWorldLoadSweepCompleted();
        }

        /// <summary>
        /// Dispatches the periodic performance telemetry report when the configured interval has elapsed.
        /// Reads interval directly from active Core runtime configuration snapshot.</summary>
        /// <param name="currentTick">The current game tick.</param>
        private void MaybeReportTelemetry(int currentTick)
        {
            if (lastReportTick < 0)
            {
                lastReportTick = currentTick;
            }

            int intervalHours = PawnDataRegistry.ActiveConfiguration.ReportMetricsIntervalHours;
            if (intervalHours <= 0)
            {
                lastReportTick = currentTick;
                return;
            }

            int intervalTicks = intervalHours * GenDate.TicksPerHour;

            if (currentTick - lastReportTick >= intervalTicks || currentTick < lastReportTick)
            {
                lastReportTick = currentTick;
                PerformanceTelemetry.GenerateAndDispatchReport(intervalHours);
            }
        }
        #endregion
    }

    #endregion
}
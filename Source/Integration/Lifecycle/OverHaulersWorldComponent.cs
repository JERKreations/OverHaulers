using RimWorld;
using RimWorld.Planet;
using Verse;

namespace OverHaulers
{
    #region 1. [LIFE-01] WORLD TRANSITION LIFECYCLE

    /// <summary>
    /// [LIFE-01] OVERHAULERS WORLD COMPONENT
    /// Ensures that the OverHaulers mod is properly initialized and maintains its state across world transitions.
    /// Resides under Source/Integration/Lifecycle/.
    /// </summary>
    public class OverHaulersWorldComponent : WorldComponent
    {
        private int lastReportTick = -1;
        private bool isWorldInitialized = false;
        private bool isCalibrationSweepDone = false;
        private int coreInitializedTick = -1;

        public OverHaulersWorldComponent(World world) : base(world)
        {
        }

        private void EnsureInitialized()
        {
            if (isWorldInitialized) return;

            isWorldInitialized = true;

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

            coreInitializedTick = Find.TickManager?.TicksGame ?? 0;

            // 6. Reset the lifecycle log
            OHLog.Lifecycle.WorldLoadedReset();
        }

        /// <summary>
        /// The initial calibration sweep is deferred past the very first WorldComponentTick after a save loads.
        /// Some externally-patched caravan-capacity stat providers (e.g. third-party mod stats) build their own
        /// per-species data lazily and aren't guaranteed ready on that first tick - a short delay lets them settle
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
            SpeciesBaselineCalibration.RunBatchSweep(onlyCaravanCapable: true, "World Load");

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
        /// Performs periodic updates for the world component, including deferred calibration sweeps and performance telemetry reporting.
        /// </summary>
        /// <remarks>
        /// This method is called once per game tick and is responsible for ensuring that the world component remains up-to-date.
        /// It handles deferred calibration sweeps for pawns and updates the performance telemetry at specified intervals.
        /// </remarks>
        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            EnsureInitialized();

            if (Find.TickManager != null && !Find.TickManager.Paused)
            {
                int currentTick = Find.TickManager.TicksGame;
                MaybeRunDeferredCalibrationSweep(currentTick);
                PawnDataRegistry.UpdateTick(currentTick);

                if (lastReportTick < 0)
                {
                    lastReportTick = currentTick;
                }

                int intervalTicks = OverHaulers.settings != null 
                    ? OverHaulers.settings.ReportMetricsIntervalTicks 
                    : SettingsDefaults.ReportMetricsIntervalHours * GenDate.TicksPerHour;

                // Check if it's time to report performance telemetry based on the configured interval.
                if (intervalTicks > 0)
                {
                    if (currentTick - lastReportTick >= intervalTicks || currentTick < lastReportTick)
                    {
                        lastReportTick = currentTick;
                        int hours = OverHaulers.settings?.reportMetricsIntervalHours ?? SettingsDefaults.ReportMetricsIntervalHours;
                        PerformanceTelemetry.GenerateAndDispatchReport(hours);
                    }
                }
            }
        }

        /// <summary>
        /// Exposes the data for saving and loading, including the last report tick.
        /// </summary>
        /// <remarks>
        /// This method is called by the RimWorld save/load system to persist the state of the world component.
        /// It ensures that the last report tick is correctly saved and restored across game sessions.
        /// </remarks>
        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Values.Look(ref lastReportTick, "lastReportTick", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
            }
        }
    }

    #endregion
}
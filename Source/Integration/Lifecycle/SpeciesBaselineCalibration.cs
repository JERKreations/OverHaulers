using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. CALIBRATION TAXONOMY

    public enum CalibrationMethod : byte
    {
        PristineDummy = 0,     // [0] Real stat worker clean resolution on pristine dummy
        TestingFallback = 1,   // [1] Non-pack species or uninitialized modded archetypes assigned testing scalar (35 kg/size)
        LiveRescue = 2,        // [2] Caravan-capable species rescued by a live spawned pawn
        EmergencyFailsafe = 3  // [3] Complete stat failure; clamped to emergency assumption
    }

    #endregion

    /// <summary>
    /// [SSoT] Centralized dynamic calibration engine.
    /// Manages species-specific baseline carrying capacity scalars using a 3-Tier Fallback system: 
    /// Pristine Dummy Pawn -> Live Pawn Rescue -> Testing/Emergency Assumption.
    /// </summary>
    public static class SpeciesBaselineCalibration
    {
        #region 2. QUARANTINED TESTING & LAST-RESORT SENTINEL

        // Testing and emergency fallback baseline assumption (used for non-pack species in the Test Bench and true emergency failures)
        public static float massCapacityScalarTestingAndFallbackOnly = 35f;

        #endregion

        #region 3. CACHE & THREAD-LOCKS

        public struct CalibrationEntry
        {
            public float Scalar;
            public CalibrationMethod Method;
            // Populated when a fallback tier was hit; surfaced via the Test Bench tooltip, never spammed per species to the log.
            public string LastErrorDetail;
        }

        private static readonly Dictionary<ThingDef, CalibrationEntry> speciesCache = new Dictionary<ThingDef, CalibrationEntry>(128);
        private static readonly HashSet<ThingDef> liveRescueInProgress = new HashSet<ThingDef>();
        private static readonly List<string> pendingSweepNoticeSpecies = new List<string>(64);
        private static readonly object calibrationLock = new object();

        // Named sentinels for the CalibrationEntry.Scalar 3-tier fallback protocol.
        private const float PendingLiveRescueSentinel = -1f;
        private const float EmergencyFailsafeSentinel = -2f;
        private const float LegitimatelyNonCaravanSentinel = -3f;

        [ThreadStatic]
        public static bool IsResolvingBaseline;

        #endregion

        #region 4. DIAGNOSTIC COUNTERS & BATCH LOGGING

        public static int CountDummySuccess { get; private set; }
        public static int CountTestingFallback { get; private set; }
        public static int CountLiveRescue { get; private set; }
        public static int CountEmergencyFallback { get; private set; }

        /// <summary>
        /// Logs a summary of the calibration sweep batch, including diagnostic counters, timing information,
        /// and a single consolidated "Don't Panic" notice for any species operating on testing benchmarks due to Main Menu limitations.
        /// </summary>
        /// <param name="context">The context or label for the batch sweep.</param>
        /// <param name="ms">The duration of the batch sweep in milliseconds.</param>
        /// <param name="totalSpecies">The total number of species evaluated in the batch sweep.</param>
        public static void LogBatchSweepSummary(string context, double ms, int totalSpecies)
        {
            Log.Message("OverHaulers_Log_CalibrationSweepComplete".Translate(
                ms.ToString("F1"),
                context,
                CountDummySuccess,
                CountTestingFallback,
                CountLiveRescue,
                CountEmergencyFallback,
                totalSpecies
            ).ToString());

            // "Don't Panic" Coalesced Notice: Single consolidated informational log for Main Menu comp limitations
            if (pendingSweepNoticeSpecies.Count > 0)
            {
                string speciesList = string.Join(", ", pendingSweepNoticeSpecies);
                Log.Message("OverHaulers_Log_CalibrationPendingNotice".Translate(
                    pendingSweepNoticeSpecies.Count,
                    speciesList
                ).ToString());

                pendingSweepNoticeSpecies.Clear();
            }
        }

        #endregion

        #region 5. EVALUATION ENGINES

        /// <summary>
        /// Attempts to evaluate the species baseline using an unspawned dummy pawn.
        /// Instantiates the extended LiveSandboxPawnHarness mid-game to satisfy comp-dependent mods.
        /// Falls back to vanilla MassUtility before failing over to the live-rescue queue.
        /// </summary>
        private static float EvaluateDummyPawn(ThingDef raceDef, out string errorDetail)
        {
            errorDetail = null;
            try
            {
                // Instantiate the appropriate sandbox harness based on the current program state.
                SandboxPawnHarness harness = (Current.ProgramState == ProgramState.Playing) 
                    ? new LiveSandboxPawnHarness() 
                    : new SandboxPawnHarness();

                // The harness will be used within a using block to ensure proper disposal.
                using (harness)
                {
                    TestSubjectEntry dummySubject = new TestSubjectEntry(
                        raceDef.defName, raceDef.race?.body, raceDef,
                        "Core", "Unknown", "Unknown"
                    );

                    harness.BindSubject(dummySubject);

                    // Bind the dummy subject to the harness to prepare for evaluation.
                    if (harness.IsValid)
                    {
                        Pawn dummyPawn = harness.SandboxPawn;
                        float cleanCapacity = 0f;

                        IsResolvingBaseline = true;
                        try
                        {
                            // Evaluate clean mass capacity based on active driver stat or fallbacks
                            if (IntegrationPipeline.ActiveDriver is GenericStatDriver statDriver && statDriver.ActiveMassCapacityStat != null)
                            {
                                cleanCapacity = statDriver.ActiveMassCapacityStat.Worker.GetValueUnfinalized(StatRequest.For(dummyPawn), applyPostProcess: false);
                                if (cleanCapacity <= 0f)
                                {
                                    cleanCapacity = statDriver.ActiveMassCapacityStat.Worker.GetValueAbstract(raceDef);
                                }
                                // If driver stat returns 0 for this species, fallback to vanilla MassUtility before giving up
                                if (cleanCapacity <= 0f)
                                {
                                    cleanCapacity = MassUtility.Capacity(dummyPawn, null);
                                }
                            }
                            // If the active mass capacity stat is not available, fall back to the generic mass utility method
                            else
                            {
                                cleanCapacity = MassUtility.Capacity(dummyPawn, null);
                            }
                        }
                        catch (Exception ex)
                        {
                            errorDetail = ex.Message;
                            // Attempt to recover the clean capacity using abstract stat or vanilla MassUtility
                            if (IntegrationPipeline.ActiveDriver is GenericStatDriver statDriver && statDriver.ActiveMassCapacityStat != null)
                            {
                                cleanCapacity = statDriver.ActiveMassCapacityStat.Worker.GetValueAbstract(raceDef);
                            }
                            if (cleanCapacity <= 0f)
                            {
                                cleanCapacity = MassUtility.Capacity(dummyPawn, null);
                            }
                        }
                        finally
                        {
                            // Reset the resolving baseline flag to indicate that the evaluation is complete.
                            IsResolvingBaseline = false;
                        }

                        float cleanBodySize = MedicalClassifier.GetSafeBodySize(dummyPawn);

                        if (cleanCapacity > 0f && cleanBodySize > 0.01f)
                        {
                            return cleanCapacity / cleanBodySize;
                        }

                        // Naturally non-caravan species legitimately return 0 in vanilla; return sentinel -3f
                        if (!PawnDataRegistry.IsCaravanCapable(raceDef))
                        {
                            return LegitimatelyNonCaravanSentinel;
                        }

                        if (errorDetail == null)
                        {
                            errorDetail = "Pristine dummy stat worker returned 0 capacity.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errorDetail = ex.Message;
            }

            if (!PawnDataRegistry.IsCaravanCapable(raceDef))
            {
                // If the species is naturally non-caravan capable, return the sentinel value to indicate this special case.
                return LegitimatelyNonCaravanSentinel;
            }

            return 0f;
        }

        /// <summary>
        /// Evaluates a live, fully-spawned pawn.
        /// Used to rescue the calibration for caravan-capable species that crash or return 0 on sterile dummy pawns.
        /// </summary>
        /// <param name="pawn">The live pawn to evaluate.</param>
        /// <param name="errorDetail">Outputs any error detail encountered during evaluation.</param>
        /// <returns>The evaluated mass capacity scalar for the live pawn.</returns>
        private static float EvaluateLivePawn(Pawn pawn, out string errorDetail)
        {
            errorDetail = null;
            float liveCapacity = 0f;
            
            IsResolvingBaseline = true;
            try
            {
                // Begin evaluation of the live pawn's mass capacity.
                if (IntegrationPipeline.ActiveDriver is GenericStatDriver statDriver && statDriver.ActiveMassCapacityStat != null)
                {
                    liveCapacity = statDriver.ActiveMassCapacityStat.Worker.GetValueUnfinalized(StatRequest.For(pawn), applyPostProcess: false);
                }
                else
                {
                    liveCapacity = MassUtility.Capacity(pawn, null);
                }
            }
            catch (Exception ex)
            {
                errorDetail = ex.Message;
                return 0f;
            }
            finally
            {
                // Ensure that the resolving baseline flag is reset even if an exception occurs during evaluation.
                IsResolvingBaseline = false;
            }

            float liveBodySize = MedicalClassifier.GetSafeBodySize(pawn);
            if (liveCapacity > 0f && liveBodySize > 0.01f)
            {
                return liveCapacity / liveBodySize;
            }

            return 0f;
        }

        #endregion

        #region 5b. SHARED CLASSIFICATION

        /// <summary>
        /// Classifies a dummy-pawn evaluation result into a cache entry, applying the shared 3-tier sentinel rules.
        /// Single source of truth for this mapping - used by both PrecalibrateDef and ResolveSpeciesBaseline.
        /// </summary>
        /// <param name="raceDef">The ThingDef of the species being classified.</param>
        /// <param name="dummyScalar">The evaluated mass capacity scalar for the dummy pawn.</param>
        /// <param name="errorDetail">Any error detail encountered during the evaluation.</param>
        /// <returns>A CalibrationEntry representing the classification of the dummy result.</returns>
        private static CalibrationEntry ClassifyDummyResult(ThingDef raceDef, float dummyScalar, string errorDetail)
        {
            if (dummyScalar == LegitimatelyNonCaravanSentinel)
            {
                CountTestingFallback++;
                return new CalibrationEntry { Scalar = massCapacityScalarTestingAndFallbackOnly, Method = CalibrationMethod.TestingFallback, LastErrorDetail = errorDetail };
            }

            if (dummyScalar <= 0f)
            {
                CountTestingFallback++;
                string pendingDetail = !string.IsNullOrEmpty(errorDetail)
                    ? $"Dummy evaluation failed: {errorDetail}. Operating on testing fallback pending live pawn rescue."
                    : "Pristine dummy returned 0 capacity (comp-dependent mod?). Operating on testing fallback pending live pawn rescue.";

                // Track for consolidated "Don't Panic" sweep notice
                string label = raceDef.label ?? raceDef.defName;
                if (!pendingSweepNoticeSpecies.Contains(label))
                {
                    pendingSweepNoticeSpecies.Add(label);
                }

                // Stored as TestingFallback [1] with Pending sentinel; only transitions to LiveRescue [2] once a real spawned pawn rescues it
                return new CalibrationEntry { Scalar = PendingLiveRescueSentinel, Method = CalibrationMethod.TestingFallback, LastErrorDetail = pendingDetail };
            }

            CountDummySuccess++;
            return new CalibrationEntry { Scalar = dummyScalar, Method = CalibrationMethod.PristineDummy, LastErrorDetail = null };
        }

        #endregion

        #region 6. UNIFIED RESOLUTION API

        /// <summary>
        /// Retrieves the calibration resolution method used for a specific race definition.
        /// Used by the Test Bench comparison banner to stamp [0/1/2/3] badges.
        /// Reflects TestingFallback [1] if the species is pending a live rescue.
        /// </summary>
        /// <param name="raceDef">The race definition for which to retrieve the calibration resolution method.</param>
        /// <returns>The calibration method used for the specified race definition.</returns>
        public static CalibrationMethod GetResolutionMethod(ThingDef raceDef)
        {
            if (raceDef == null) return CalibrationMethod.PristineDummy;

            lock (calibrationLock)
            {
                if (speciesCache.TryGetValue(raceDef, out CalibrationEntry entry))
                {
                    // If still pending a live rescue, it is currently operating on TestingFallback [1]
                    if (entry.Scalar == PendingLiveRescueSentinel)
                    {
                        return CalibrationMethod.TestingFallback;
                    }
                    return entry.Method;
                }
            }

            return PawnDataRegistry.IsCaravanCapable(raceDef) 
                ? CalibrationMethod.PristineDummy 
                : CalibrationMethod.TestingFallback;
        }

        /// <summary>
        /// Returns true if the species is a caravan-capable archetype that failed dummy evaluation and is awaiting live pawn rescue.
        /// </summary>
        /// <param name="raceDef">The race definition to check.</param>
        /// <returns>True if the species is pending a live pawn rescue; otherwise, false.</returns>
        public static bool IsPendingLiveRescue(ThingDef raceDef)
        {
            if (raceDef == null) return false;

            lock (calibrationLock)
            {
                if (speciesCache.TryGetValue(raceDef, out CalibrationEntry entry))
                {
                    return entry.Scalar == PendingLiveRescueSentinel;
                }
            }

            return false;
        }

        /// <summary>
        /// Retrieves the exception detail (if any) from the fallback tier that resolved a species' calibration.
        /// Surfaced only via the Test Bench tooltip, never spammed to the console.
        /// </summary>
        /// <param name="raceDef">The race definition for which to retrieve the last error detail.</param>
        /// <returns>The last error detail associated with the specified race definition, or null if none exists.</returns>
        public static string GetLastErrorDetail(ThingDef raceDef)
        {
            if (raceDef == null) return null;

            lock (calibrationLock)
            {
                if (speciesCache.TryGetValue(raceDef, out CalibrationEntry entry))
                {
                    return entry.LastErrorDetail;
                }
            }

            return null;
        }

        /// <summary>
        /// Runs a timed batch sweep across species ThingDefs and prints a single consolidated checkpoint report
        /// and coalesced notice.
        /// </summary>
        /// <param name="onlyCaravanCapable">If true, only caravan-capable species will be included in the batch sweep.</param>
        /// <param name="context">A string providing context for the batch sweep, used in logging the summary report.</param>
        /// <remarks>
        /// This method iterates over all species ThingDefs, optionally filtering for only caravan-capable species.
        /// Each species is pre-calibrated, and a consolidated performance report is logged at the end of the sweep.
        /// </remarks>
        public static void RunBatchSweep(bool onlyCaravanCapable, string context)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int totalSweep = 0;
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;

            // Clear the pending sweep notice species list before starting the batch sweep to ensure accurate reporting.
            lock (calibrationLock)
            {
                pendingSweepNoticeSpecies.Clear();
            }

            if (allDefs != null)
            {
                for (int i = 0; i < allDefs.Count; i++)
                {
                    ThingDef def = allDefs[i];
                    if (def != null && def.category == ThingCategory.Pawn && def.race != null)
                    {
                        if (!onlyCaravanCapable || PawnDataRegistry.IsCaravanCapable(def))
                        {
                            PrecalibrateDef(def);
                            totalSweep++;
                        }
                    }
                }
            }

            watch.Stop();
            LogBatchSweepSummary(context, watch.Elapsed.TotalMilliseconds, totalSweep);
        }

        /// <summary>
        /// Pre-calibrates an archetype species directly from its ThingDef during batch sweeps.
        /// </summary>
        /// <param name="raceDef">The race definition of the species to precalibrate.</param>
        /// <returns>The scalar value resulting from the precalibration of the specified species.</returns>
        public static float PrecalibrateDef(ThingDef raceDef)
        {
            if (raceDef == null) return 0f;

            lock (calibrationLock)
            {
                if (!speciesCache.TryGetValue(raceDef, out CalibrationEntry entry))
                {
                    float scalar = EvaluateDummyPawn(raceDef, out string errorDetail);
                    entry = ClassifyDummyResult(raceDef, scalar, errorDetail);
                    speciesCache[raceDef] = entry;
                }

                return entry.Scalar;
            }
        }

        /// <summary>
        /// Resolves the true baseline capacity for a specific pawn type by multiplying its active BodySize
        /// against its cached, species-specific pristine scalar.
        /// Executes silently for individual on-demand lookups.
        /// </summary>
        /// <param name="pawn">The pawn for which to resolve the species baseline.</param>
        /// <returns>The calculated baseline mass capacity in kg.</returns>
        /// <remarks>
        /// This method first attempts to retrieve the species-specific scalar from the cache.
        /// If the scalar indicates that a live calibration is required, it will attempt to perform
        /// a live rescue for fully-spawned pawns on an active map. During this process, headless
        /// sandbox pawns and unspawned pawns will fall back to the testing and dummy evaluation scalar.
        /// </remarks>
        public static float ResolveSpeciesBaseline(Pawn pawn)
        {
            if (pawn == null || pawn.def == null) return 0f;

            CalibrationEntry entry;
            bool requiresLiveCalibration = false;

            // 1. Check cache and attempt pristine dummy evaluation
            lock (calibrationLock)
            {
                if (!speciesCache.TryGetValue(pawn.def, out entry))
                {
                    float dummyScalar = EvaluateDummyPawn(pawn.def, out string dummyError);
                    entry = ClassifyDummyResult(pawn.def, dummyScalar, dummyError);
                    speciesCache[pawn.def] = entry;
                }

                if (entry.Scalar == PendingLiveRescueSentinel)
                {
                    requiresLiveCalibration = true;
                }
            }

            float currentBodySize = MedicalClassifier.GetSafeBodySize(pawn);

            // 2. Perform live rescue outside the lock to prevent stalling background threads.
            // Only real, fully-spawned pawns on an active map can execute a live rescue.
            // Headless sandbox dummy pawns and unspawned pawns must NEVER attempt live rescues or log false warnings.
            if (requiresLiveCalibration)
            {
                if (pawn.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId || !pawn.Spawned || pawn.Map == null)
                {
                    return massCapacityScalarTestingAndFallbackOnly * currentBodySize;
                }

                bool ownsRescue;
                lock (calibrationLock)
                {
                    ownsRescue = liveRescueInProgress.Add(pawn.def);
                }

                // If this thread owns the live rescue, it will proceed to evaluate the live pawn.
                // Otherwise, it will return the testing/fallback value immediately.
                if (!ownsRescue)
                {
                    return massCapacityScalarTestingAndFallbackOnly * currentBodySize;
                }

                try
                {
                    // Evaluate the live pawn to obtain the most accurate available scalar for this species.
                    float liveScalar = EvaluateLivePawn(pawn, out string liveError);

                    lock (calibrationLock)
                    {
                        // Update the species cache with the result of the live rescue.
                        if (speciesCache.TryGetValue(pawn.def, out entry) && entry.Scalar == PendingLiveRescueSentinel)
                        {
                            if (liveScalar > 0f)
                            {
                                entry = new CalibrationEntry { Scalar = liveScalar, Method = CalibrationMethod.LiveRescue, LastErrorDetail = liveError };
                                CountLiveRescue++;
                            }
                            // If the live evaluation failed, fall back to the emergency failsafe with explicit logging
                            else
                            {
                                entry = new CalibrationEntry { Scalar = EmergencyFailsafeSentinel, Method = CalibrationMethod.EmergencyFailsafe, LastErrorDetail = liveError };
                                CountEmergencyFallback++;

                                // Log the reason for falling back to the emergency failsafe.
                                string detail = !string.IsNullOrEmpty(liveError)
                                    ? $"Live pawn evaluation failed with exception: {liveError}"
                                    : "Both pristine dummy and live evaluations returned 0 capacity. Applied Emergency Failsafe [3] baseline assumption (35 kg * BodySize).";

                                OHLog.Lifecycle.Warn(pawn.def.defName, null, detail);
                            }
                            speciesCache[pawn.def] = entry;
                        }
                    }
                }
                finally
                {
                    lock (calibrationLock)
                    {
                        // Release ownership of the live rescue for this species.
                        liveRescueInProgress.Remove(pawn.def);
                    }
                }
            }

            // 3. Apply the final resolved scalar
            if (entry.Scalar == EmergencyFailsafeSentinel)
            {
                return massCapacityScalarTestingAndFallbackOnly * currentBodySize;
            }

            return entry.Scalar * currentBodySize;
        }

        /// <summary>
        /// Resets the calibration state, clearing the species cache and resetting all counters and notices.
        /// </summary>
        public static void Reset()
        {
            lock (calibrationLock)
            {
                speciesCache.Clear();
                liveRescueInProgress.Clear();
                pendingSweepNoticeSpecies.Clear();
                CountDummySuccess = 0;
                CountTestingFallback = 0;
                CountLiveRescue = 0;
                CountEmergencyFallback = 0;
            }
        }

        #endregion
    }
}
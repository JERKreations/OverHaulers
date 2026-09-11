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
        TestingFallback = 1,   // [1] Non-pack species assigned testing scalar (35 kg/size)
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
            // Populated only when a fallback tier was hit; surfaced via the Test Bench tooltip, never logged (avoids per-species log spam).
            public string LastErrorDetail;
        }

        private static readonly Dictionary<ThingDef, CalibrationEntry> speciesCache = new Dictionary<ThingDef, CalibrationEntry>(128);
        private static readonly HashSet<ThingDef> liveRescueInProgress = new HashSet<ThingDef>();
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
        /// Logs a summary of the calibration sweep batch, including diagnostic counters and timing information.
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
        }

        #endregion

        #region 5. EVALUATION ENGINES

        /// <summary>
        /// Attempts to evaluate the species baseline using an unspawned dummy pawn.
        /// Instantiates the extended LiveSandboxPawnHarness mid-game to satisfy comp-dependent mods.
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
                            // Evaluate the clean mass capacity of the dummy pawn based on the active mass capacity stat or fallback methods.
                            if (IntegrationPipeline.ActiveDriver is GenericStatDriver statDriver && statDriver.ActiveMassCapacityStat != null)
                            {
                                cleanCapacity = statDriver.ActiveMassCapacityStat.Worker.GetValueUnfinalized(StatRequest.For(dummyPawn), applyPostProcess: false);
                                if (cleanCapacity <= 0f)
                                {
                                    cleanCapacity = statDriver.ActiveMassCapacityStat.Worker.GetValueAbstract(raceDef);
                                }
                            }
                            // If the active mass capacity stat is not available, fall back to the generic mass utility method.
                            else
                            {
                                cleanCapacity = MassUtility.Capacity(dummyPawn, null);
                            }
                        }
                        catch (Exception ex)
                        {
                            errorDetail = ex.Message;
                            // Attempt to recover the clean capacity using the abstract value if an exception occurred.
                            if (IntegrationPipeline.ActiveDriver is GenericStatDriver statDriver && statDriver.ActiveMassCapacityStat != null)
                            {
                                cleanCapacity = statDriver.ActiveMassCapacityStat.Worker.GetValueAbstract(raceDef);
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
        /// Single source of truth for this mapping - used by both PrecalibrateDef and ResolveBaseline.
        /// </summary>
        /// <param name="dummyScalar">The evaluated mass capacity scalar for the dummy pawn.</param>
        /// <param name="errorDetail">Any error detail encountered during the evaluation.</param>
        /// <returns>A CalibrationEntry representing the classification of the dummy result.</returns>
        private static CalibrationEntry ClassifyDummyResult(float dummyScalar, string errorDetail)
        {
            if (dummyScalar == LegitimatelyNonCaravanSentinel)
            {
                CountTestingFallback++;
                return new CalibrationEntry { Scalar = massCapacityScalarTestingAndFallbackOnly, Method = CalibrationMethod.TestingFallback, LastErrorDetail = errorDetail };
            }

            if (dummyScalar <= 0f)
            {
                return new CalibrationEntry { Scalar = PendingLiveRescueSentinel, Method = CalibrationMethod.LiveRescue, LastErrorDetail = errorDetail };
            }

            CountDummySuccess++;
            return new CalibrationEntry { Scalar = dummyScalar, Method = CalibrationMethod.PristineDummy, LastErrorDetail = null };
        }

        #endregion

        #region 6. UNIFIED RESOLUTION API

        /// <summary>
        /// Retrieves the calibration resolution method used for a specific race definition.
        /// Used by the Test Bench comparison banner to stamp [0/1/2/3] badges.
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
                    return entry.Method;
                }
            }

            return PawnDataRegistry.IsCaravanCapable(raceDef) 
                ? CalibrationMethod.PristineDummy 
                : CalibrationMethod.TestingFallback;
        }

        /// <summary>
        /// Retrieves the exception detail (if any) from the fallback tier that resolved a species' calibration.
        /// Surfaced only via the Test Bench tooltip, never logged.
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
        /// Runs a timed batch sweep across species ThingDefs and prints a single consolidated checkpoint report.
        /// </summary>
        /// <param name="onlyCaravanCapable">If true, only caravan-capable species will be included in the batch sweep.</param>
        /// <param name="context">A string providing context for the batch sweep, used in logging the summary report.</param>
        /// <returns>None. The method performs the batch sweep and logs the summary report.</returns>
        public static void RunBatchSweep(bool onlyCaravanCapable, string context)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int totalSweep = 0;
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;

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
                    entry = ClassifyDummyResult(scalar, errorDetail);
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
        public static float ResolveBaseline(Pawn pawn)
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
                    entry = ClassifyDummyResult(dummyScalar, dummyError);
                    speciesCache[pawn.def] = entry;
                }

                if (entry.Scalar == PendingLiveRescueSentinel)
                {
                    requiresLiveCalibration = true;
                }
            }

            float currentBodySize = MedicalClassifier.GetSafeBodySize(pawn);

            // 2. Perform live rescue outside the lock to prevent stalling background threads.
            // Only the first caller per species performs the rescue; concurrent callers for the same still-pending
            // species use the safe testing/fallback value for this query only, without overwriting the shared cache.
            if (requiresLiveCalibration)
            {
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
                            // If the live evaluation failed, fall back to the emergency failsafe.
                            else
                            {
                                entry = new CalibrationEntry { Scalar = EmergencyFailsafeSentinel, Method = CalibrationMethod.EmergencyFailsafe, LastErrorDetail = liveError };
                                CountEmergencyFallback++;
                                OHLog.Lifecycle.Warn(pawn.def.defName, null, liveError);
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
        /// Resets the calibration state, clearing the species cache and resetting all counters.
        /// </summary>
        public static void Reset()
        {
            lock (calibrationLock)
            {
                speciesCache.Clear();
                liveRescueInProgress.Clear();
                CountDummySuccess = 0;
                CountTestingFallback = 0;
                CountLiveRescue = 0;
                CountEmergencyFallback = 0;
            }
        }

        #endregion
    }
}
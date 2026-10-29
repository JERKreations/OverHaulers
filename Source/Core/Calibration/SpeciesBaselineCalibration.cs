using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. CALIBRATION TAXONOMY

    /// <summary>
    /// [INT-CALIB-01] Explicit state of a species baseline calibration entry.
    /// Completely replaces floating-point sentinel smuggling (-1f, -2f, -3f).
    /// </summary>
    public enum CalibrationStatus : byte
    {
        Resolved = 0,
        PendingLiveRescue = 1,
        EmergencyFailsafe = 2,
        NonCaravan = 3
    }

    /// <summary>
    /// [INT-CALIB-01] The specific resolution pathway that produced the baseline mass scalar.
    /// </summary>
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
    /// Manages species-specific baseline mass capacity scalars using a 3-Tier Fallback system: 
    /// Pristine Dummy Pawn -> Live Pawn Rescue -> Testing/Emergency Assumption.
    /// Resides strictly under Source/Core/Calibration/.
    /// </summary>
    public static class SpeciesBaselineCalibration
    {
        #region 2. QUARANTINED TESTING & LAST-RESORT CONSTANTS

        /// <summary>
        /// Testing and emergency fallback baseline assumption (used for non-pack species in the Test Bench and true emergency failures).
        /// </summary>
        /// <remarks>
        /// This value should ONLY exist within the context of testing or emergency fallback scenarios, and not be defined outside of
        /// the controlled environment of the calibration engine itself.
        /// </remarks>
        public const float massCapacityScalarTestingAndFallbackOnly = 35f;

        #endregion

        #region 3. CACHE & THREAD-LOCKS

        /// <summary>
        /// Authoritative cached calibration descriptor for a species archetype.
        /// </summary>
        public struct CalibrationEntry
        {
            /// <summary>The resolved physical mass capacity scalar in kg / BodySize (guaranteed >= 0.0f).</summary>
            public float Scalar;

            /// <summary>The explicit resolution state of this entry.</summary>
            public CalibrationStatus Status;

            /// <summary>The engine tier that resolved this entry.</summary>
            public CalibrationMethod Method;

            /// <summary>Populated when a fallback tier was hit; surfaced via Test Bench tooltips without log spam.</summary>
            public string LastErrorDetail;
        }

        private static readonly Dictionary<ThingDef, CalibrationEntry> speciesCache = new Dictionary<ThingDef, CalibrationEntry>(128);
        private static readonly HashSet<ThingDef> liveRescueInProgress = new HashSet<ThingDef>();
        private static readonly List<string> pendingSweepNoticeSpecies = new List<string>(64);
        private static readonly object calibrationLock = new object();

        [ThreadStatic]
        public static bool IsResolvingBaseline;

        [ThreadStatic]
        internal static string LastCalibrationException;

        #endregion

        #region 4. DIAGNOSTIC COUNTERS & BATCH LOGGING

        public static int CountDummySuccess { get; private set; }
        public static int CountTestingFallback { get; private set; }
        public static int CountLiveRescue { get; private set; }
        public static int CountEmergencyFallback { get; private set; }

        /// <summary>
        /// Logs a summary of the calibration sweep batch, including diagnostic counters, timing information,
        /// and a single consolidated notice for species operating on testing benchmarks due to Main Menu limitations.
        /// </summary>
        /// <param name="context">The context or label for the batch sweep.</param>
        /// <param name="ms">The duration of the batch sweep in milliseconds.</param>
        /// <param name="totalSpecies">The total number of species evaluated in the batch sweep.</param>
        public static void LogBatchSweepSummary(string context, double ms, int totalSpecies)
        {
            OHLog.Calibration.SweepCompleted(
                ms,
                context,
                CountDummySuccess,
                CountTestingFallback,
                CountLiveRescue,
                CountEmergencyFallback,
                totalSpecies
            );

            // Coalesced Notice: Single consolidated informational log for Main Menu comp limitations
            if (pendingSweepNoticeSpecies.Count > 0)
            {
                string speciesList = string.Join(", ", pendingSweepNoticeSpecies);
                OHLog.Calibration.PendingNotice(pendingSweepNoticeSpecies.Count, speciesList);
                pendingSweepNoticeSpecies.Clear();
            }
        }

        #endregion

        #region 5. EVALUATION ENGINES

        /// <summary>
        /// Attempts to evaluate the species baseline using an unspawned dummy pawn via Core HeadlessPawnShell.
        /// Fully decouples baseline calibration from the Settings/TestBench layer.
        /// </summary>
        /// <param name="raceDef">The ThingDef representing the species to evaluate.</param>
        /// <param name="scalar">Outputs the evaluated scalar in kg / BodySize if successful; otherwise 0f.</param>
        /// <param name="errorDetail">Outputs any error detail encountered during the evaluation.</param>
        /// <returns>True if the pristine dummy evaluated a positive capacity; otherwise false.</returns>
        private static bool EvaluateDummyPawn(ThingDef raceDef, out float scalar, out string errorDetail)
        {
            scalar = 0f;
            errorDetail = null;

            try
            {
                using (HeadlessPawnShell shell = HeadlessPawnShell.CreateClean(raceDef, initializeComps: Current.ProgramState == ProgramState.Playing))
                {
                    if (shell.IsValid)
                    {
                        Pawn dummyPawn = shell.Pawn;
                        float cleanCapacity = 0f;

                        IsResolvingBaseline = true;
                        LastCalibrationException = null;
                        try
                        {
                            cleanCapacity = MassUtility.Capacity(dummyPawn, null);
                        }
                        catch (Exception ex)
                        {
                            errorDetail = ex.Message;
                            cleanCapacity = 0f;
                        }
                        finally
                        {
                            IsResolvingBaseline = false;
                        }

                        // If a safety finalizer caught a third-party crash, retrieve its captured message
                        if (!string.IsNullOrEmpty(LastCalibrationException))
                        {
                            errorDetail = LastCalibrationException;
                            LastCalibrationException = null;
                        }

                        float cleanBodySize = MedicalClassifier.GetSafeBodySize(dummyPawn);

                        if (cleanCapacity > 0f && cleanBodySize > 0.01f && string.IsNullOrEmpty(errorDetail))
                        {
                            scalar = cleanCapacity / cleanBodySize;
                            return true;
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

            return false;
        }

        /// <summary>
        /// Evaluates a live, fully-spawned pawn.
        /// Used to rescue the calibration for caravan-capable species that crash or return 0 on sterile dummy pawns.
        /// </summary>
        /// <param name="pawn">The live pawn being evaluated.</param>
        /// <param name="errorDetail">Outputs any error detail encountered during the evaluation.</param>
        /// <returns>The mass capacity scalar for the live pawn, or 0 if evaluation fails.</returns>
        private static float EvaluateLivePawn(Pawn pawn, out string errorDetail)
        {
            errorDetail = null;
            float liveCapacity = 0f;

            IsResolvingBaseline = true;
            try
            {
                liveCapacity = MassUtility.Capacity(pawn, null);
            }
            catch (Exception ex)
            {
                errorDetail = ex.Message;
                return 0f;
            }
            finally
            {
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

        #region 6. SHARED CLASSIFICATION & CANONICAL RESOLUTION

        /// <summary>
        /// Classifies a dummy-pawn evaluation result into a cache entry, applying strongly-typed fallback rules.
        /// Single source of truth for this mapping - used by both PrecalibrateDef and ResolveSpeciesBaseline.
        /// </summary>
        private static CalibrationEntry ClassifyDummyResult(ThingDef raceDef, bool success, float dummyScalar, string errorDetail)
        {
            if (!PawnDataRegistry.IsSpeciesCaravanCapable(raceDef))
            {
                CountTestingFallback++;
                return new CalibrationEntry
                {
                    Scalar = massCapacityScalarTestingAndFallbackOnly,
                    Status = CalibrationStatus.NonCaravan,
                    Method = CalibrationMethod.TestingFallback,
                    LastErrorDetail = errorDetail
                };
            }

            if (!success || dummyScalar <= 0f)
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

                return new CalibrationEntry
                {
                    Scalar = massCapacityScalarTestingAndFallbackOnly,
                    Status = CalibrationStatus.PendingLiveRescue,
                    Method = CalibrationMethod.TestingFallback,
                    LastErrorDetail = pendingDetail
                };
            }

            CountDummySuccess++;
            return new CalibrationEntry
            {
                Scalar = dummyScalar,
                Status = CalibrationStatus.Resolved,
                Method = CalibrationMethod.PristineDummy,
                LastErrorDetail = null
            };
        }

        /// <summary>
        /// Single canonical main-thread path that returns a species' calibration entry, evaluating a pristine dummy pawn on a cache miss.
        /// The slow dummy evaluation runs outside <c>calibrationLock</c> so background readers are never blocked behind it;
        /// only the cache lookup and the publication of the result are locked.
        /// </summary>
        private static CalibrationEntry ResolveEntryOnMainThread(ThingDef raceDef)
        {
            lock (calibrationLock)
            {
                if (speciesCache.TryGetValue(raceDef, out CalibrationEntry cachedEntry))
                {
                    return cachedEntry;
                }
            }

            bool success = EvaluateDummyPawn(raceDef, out float scalar, out string errorDetail);

            lock (calibrationLock)
            {
                if (!speciesCache.TryGetValue(raceDef, out CalibrationEntry entry))
                {
                    entry = ClassifyDummyResult(raceDef, success, scalar, errorDetail);
                    speciesCache[raceDef] = entry;
                }

                return entry;
            }
        }

        #endregion

        #region 7. PUBLIC RESOLUTION API

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

            return PawnDataRegistry.IsSpeciesCaravanCapable(raceDef) 
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
                    return entry.Status == CalibrationStatus.PendingLiveRescue;
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
        /// Ensures species baseline mass capacity calibrations have been executed.
        /// If uninitialized, executes a batch sweep across caravan-capable species.
        /// Thread-safe and idempotent; executes in 1 CPU cycle once calibrated.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (speciesCache.Count > 0) return;

            lock (calibrationLock)
            {
                if (speciesCache.Count > 0) return;

                string context = Current.ProgramState == ProgramState.Playing
                    ? "OverHaulers_Log_Calibration_Context_WorldLoad".Translate().ToString()
                    : "Test Bench";

                RunBatchSweep(onlyCaravanCapable: true, context);
            }
        }

        /// <summary>
        /// Runs a timed batch sweep across species ThingDefs and prints a single consolidated checkpoint report.
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
                        if (!onlyCaravanCapable || PawnDataRegistry.IsSpeciesCaravanCapable(def))
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
        /// Guaranteed to return a non-negative scalar (>= 0.0f).
        /// </summary>
        /// <param name="raceDef">The race definition of the species to precalibrate.</param>
        /// <returns>The scalar value resulting from the precalibration of the specified species.</returns>
        public static float PrecalibrateDef(ThingDef raceDef)
        {
            if (raceDef == null) return 0f;

            // Off-thread callers must never instantiate dummy pawns; they may only read an already-resolved entry.
            if (!UnityData.IsInMainThread)
            {
                lock (calibrationLock)
                {
                    if (speciesCache.TryGetValue(raceDef, out CalibrationEntry cachedEntry))
                    {
                        return cachedEntry.Scalar;
                    }
                }

                return massCapacityScalarTestingAndFallbackOnly;
            }

            return ResolveEntryOnMainThread(raceDef).Scalar;
        }

        /// <summary>
        /// Resolves the true baseline capacity for a specific pawn type by multiplying its active BodySize
        /// against its cached, species-specific pristine scalar.
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

            // 1. OFF-THREAD FAST PATH: Background worker threads (e.g. async pathfinding) must never
            // instantiate dummy pawns, touch comps, or execute live rescues outside Unity's main thread.
            if (!UnityData.IsInMainThread)
            {
                float safeBodySize = MedicalClassifier.GetSafeBodySize(pawn);
                lock (calibrationLock)
                {
                    if (speciesCache.TryGetValue(pawn.def, out CalibrationEntry cachedEntry))
                    {
                        return cachedEntry.Scalar * safeBodySize;
                    }
                }

                return massCapacityScalarTestingAndFallbackOnly * safeBodySize;
            }

            // 2. Check cache and attempt pristine dummy evaluation (Main Thread only) via canonical resolver.
            CalibrationEntry entry = ResolveEntryOnMainThread(pawn.def);
            bool requiresLiveCalibration = entry.Status == CalibrationStatus.PendingLiveRescue;

            float currentBodySize = MedicalClassifier.GetSafeBodySize(pawn);

            // 3. Perform live rescue outside the lock to prevent stalling background threads.
            // Only real, fully-spawned pawns on an active map can execute a live rescue.
            // Headless sandbox dummy pawns and unspawned pawns must NEVER attempt live rescues or log false warnings.
            if (requiresLiveCalibration)
            {
                if (pawn.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId || !pawn.Spawned || pawn.Map == null)
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
                        if (speciesCache.TryGetValue(pawn.def, out entry) && entry.Status == CalibrationStatus.PendingLiveRescue)
                        {
                            if (liveScalar > 0f)
                            {
                                entry = new CalibrationEntry
                                {
                                    Scalar = liveScalar,
                                    Status = CalibrationStatus.Resolved,
                                    Method = CalibrationMethod.LiveRescue,
                                    LastErrorDetail = liveError
                                };
                                CountLiveRescue++;
                            }
                            // If the live evaluation failed, fall back to the emergency failsafe with explicit logging
                            else
                            {
                                entry = new CalibrationEntry
                                {
                                    Scalar = massCapacityScalarTestingAndFallbackOnly,
                                    Status = CalibrationStatus.EmergencyFailsafe,
                                    Method = CalibrationMethod.EmergencyFailsafe,
                                    LastErrorDetail = liveError
                                };
                                CountEmergencyFallback++;

                                // Log the reason for falling back to the emergency failsafe.
                                string detail = !string.IsNullOrEmpty(liveError)
                                    ? $"Live pawn evaluation failed with exception: {liveError}"
                                    : "Both pristine dummy and live evaluations returned 0 capacity. Applied Emergency Failsafe [3] baseline assumption (35 kg * BodySize).";

                                OHLog.Calibration.Warn(pawn.def.defName, null, detail);
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

            // 4. Apply the final resolved scalar (guaranteed >= 0.0f)
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
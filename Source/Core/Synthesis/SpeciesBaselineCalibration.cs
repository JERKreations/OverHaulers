using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. CALIBRATION TAXONOMY

    /// <summary>
    /// [CORE-SYNTH-03] Explicit state of a species baseline calibration entry.
    /// Completely replaces floating-point sentinel smuggling (-1f, -2f, -3f).
    /// </summary>
    public enum CalibrationStatus : byte
    {
        Resolved = 0,
        PendingWorldLoadSweep = 1,
        EmergencyFailsafe = 2,
        NonCaravan = 3
    }

    /// <summary>
    /// [CORE-SYNTH-03] The specific resolution pathway that produced the baseline mass scalar.
    /// </summary>
    public enum CalibrationMethod : byte
    {
        PristineDummy = 0,     // [0] Real stat worker clean resolution on pristine dummy
        TestingFallback = 1,   // [1] Non-pack species or species awaiting the world-load sweep
        EmergencyFailsafe = 2  // [2] Dummy evaluation failed after the world-load sweep began
    }

    #endregion

    /// <summary>
    /// [SSoT] Centralized dynamic calibration engine.
    /// Resolves species-specific baseline mass capacity scalars using pristine dummy pawns.
    /// Failed early evaluations are retried during the world-load sweep; remaining failures use a fallback.
    /// Resides under Source/Core/Synthesis/.
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
        private static readonly List<string> pendingSweepNoticeSpecies = new List<string>(64);
        private static readonly object calibrationLock = new object();
        private static bool worldLoadSweepStarted;

        /// <summary>Raised after a world-load calibration sweep so dependent previews can refresh.</summary>
        public static event Action WorldLoadSweepCompleted;

        [ThreadStatic]
        public static bool IsResolvingBaseline;

        [ThreadStatic]
        internal static string LastCalibrationException;

        #endregion

        #region 4. DIAGNOSTIC COUNTERS & BATCH LOGGING

        public static int CountDummySuccess { get; private set; }
        public static int CountTestingFallback { get; private set; }
        public static int CountEmergencyFallback { get; private set; }

        /// <summary>
        /// Logs a summary of the calibration sweep batch, including diagnostic counters, timing information,
        /// and a single consolidated notice for species operating on testing benchmarks due to Main Menu limitations.
        /// </summary>
        /// <param name="ms">The duration of the batch sweep in milliseconds.</param>
        /// <param name="totalSpecies">The total number of species evaluated in the batch sweep.</param>
        public static void LogBatchSweepSummary(double ms, int totalSpecies)
        {
            CoreLogEvents.CalibrationSweepCompleted(
                ms,
                CountDummySuccess,
                CountTestingFallback,
                CountEmergencyFallback,
                totalSpecies
            );

            // Coalesced notice for species awaiting a world-load retry.
            if (pendingSweepNoticeSpecies.Count > 0)
            {
                string speciesList = string.Join(", ", pendingSweepNoticeSpecies);
                CoreLogEvents.CalibrationPendingNotice(pendingSweepNoticeSpecies.Count, speciesList);
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
                if (worldLoadSweepStarted)
                {
                    CountEmergencyFallback++;
                    string failureDetail = !string.IsNullOrEmpty(errorDetail)
                        ? "Pristine dummy evaluation failed: " + errorDetail
                        : "Pristine dummy evaluation returned 0 capacity.";
                    OHLog.Warn("Calibration." + raceDef.defName, null,
                        failureDetail + " Applied emergency fallback (35 kg per BodySize).");
                    return new CalibrationEntry
                    {
                        Scalar = massCapacityScalarTestingAndFallbackOnly,
                        Status = CalibrationStatus.EmergencyFailsafe,
                        Method = CalibrationMethod.EmergencyFailsafe,
                        LastErrorDetail = failureDetail
                    };
                }

                CountTestingFallback++;
                string pendingDetail = !string.IsNullOrEmpty(errorDetail)
                    ? $"Dummy evaluation failed: {errorDetail}. Operating on testing fallback pending the world-load sweep."
                    : "Pristine dummy returned 0 capacity. Operating on testing fallback pending the world-load sweep.";

                // Track for consolidated "Don't Panic" sweep notice
                string label = raceDef.label ?? raceDef.defName;
                if (!pendingSweepNoticeSpecies.Contains(label))
                {
                    pendingSweepNoticeSpecies.Add(label);
                }

                return new CalibrationEntry
                {
                    Scalar = massCapacityScalarTestingAndFallbackOnly,
                    Status = CalibrationStatus.PendingWorldLoadSweep,
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
        /// Single canonical main-thread path that resolves a cache miss or retries a pending world-load entry.
        /// The slow dummy evaluation runs outside <c>calibrationLock</c> so background readers are never blocked behind it;
        /// only the cache lookup and the publication of the result are locked.
        /// </summary>
        private static CalibrationEntry ResolveEntryOnMainThread(ThingDef raceDef, bool retryPendingWorldLoad = false)
        {
            lock (calibrationLock)
            {
                if (speciesCache.TryGetValue(raceDef, out CalibrationEntry cachedEntry) &&
                    (!retryPendingWorldLoad || cachedEntry.Status != CalibrationStatus.PendingWorldLoadSweep))
                {
                    return cachedEntry;
                }
            }

            bool success = EvaluateDummyPawn(raceDef, out float scalar, out string errorDetail);

            lock (calibrationLock)
            {
                bool exists = speciesCache.TryGetValue(raceDef, out CalibrationEntry entry);
                if (!exists || (retryPendingWorldLoad && entry.Status == CalibrationStatus.PendingWorldLoadSweep))
                {
                    if (exists) CountTestingFallback--;
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
        /// Used by the Test Bench comparison banner to stamp [0/1/2] badges.
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
        /// Returns true if the species failed dummy evaluation and is awaiting the world-load sweep.
        /// </summary>
        /// <param name="raceDef">The race definition to check.</param>
        /// <returns>True if the species is pending the world-load sweep; otherwise, false.</returns>
        public static bool IsPendingWorldLoadSweep(ThingDef raceDef)
        {
            if (raceDef == null) return false;

            lock (calibrationLock)
            {
                if (speciesCache.TryGetValue(raceDef, out CalibrationEntry entry))
                {
                    return entry.Status == CalibrationStatus.PendingWorldLoadSweep;
                }
            }

            return false;
        }

        /// <summary>
        /// Retrieves the exception detail (if any) from the fallback tier that resolved a species' calibration.
        /// Surfaced via the Test Bench tooltip; final failures are also logged.
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
        /// Idempotent; the initial sweep must run on the main thread.
        /// </summary>
        public static void EnsureInitialized()
        {
            lock (calibrationLock)
            {
                if (speciesCache.Count > 0) return;
            }

            RunBatchSweep(onlyCaravanCapable: true);
        }

        /// <summary>
        /// Runs a timed batch sweep across species ThingDefs and prints a single consolidated checkpoint report.
        /// </summary>
        /// <param name="onlyCaravanCapable">If true, only caravan-capable species will be included in the batch sweep.</param>
        /// <param name="finalizeWorldLoad">If true, retry pending entries and treat failures as final for this session.</param>
        /// <remarks>
        /// This method iterates over all species ThingDefs, optionally filtering for only caravan-capable species.
        /// Each species is pre-calibrated, and a consolidated performance report is logged at the end of the sweep.
        /// </remarks>
        public static void RunBatchSweep(bool onlyCaravanCapable, bool finalizeWorldLoad = false)
        {
            if (!UnityData.IsInMainThread)
                throw new InvalidOperationException("Species calibration sweeps must run on the main thread.");

            var watch = System.Diagnostics.Stopwatch.StartNew();
            int totalSweep = 0;
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;

            // Clear the pending sweep notice species list before starting the batch sweep to ensure accurate reporting.
            lock (calibrationLock)
            {
                pendingSweepNoticeSpecies.Clear();
                if (finalizeWorldLoad) worldLoadSweepStarted = true;
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
                            if (finalizeWorldLoad)
                                ResolveEntryOnMainThread(def, retryPendingWorldLoad: true);
                            else
                                PrecalibrateDef(def);
                            totalSweep++;
                        }
                    }
                }
            }

            watch.Stop();
            LogBatchSweepSummary(watch.Elapsed.TotalMilliseconds, totalSweep);
            if (finalizeWorldLoad) WorldLoadSweepCompleted?.Invoke();
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
        /// Failed early dummy evaluations use the testing fallback until the world-load sweep retries them.
        /// After that sweep begins, failed evaluations use the emergency fallback for the session.
        /// </remarks>
        public static float ResolveSpeciesBaseline(Pawn pawn)
        {
            if (pawn == null || pawn.def == null) return 0f;

            // 1. OFF-THREAD FAST PATH: Background worker threads (e.g. async pathfinding) must never
            // instantiate dummy pawns or touch comps outside Unity's main thread.
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
            float currentBodySize = MedicalClassifier.GetSafeBodySize(pawn);
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
                pendingSweepNoticeSpecies.Clear();
                worldLoadSweepStarted = false;
                CountDummySuccess = 0;
                CountTestingFallback = 0;
                CountEmergencyFallback = 0;
            }
        }

        #endregion
    }
}
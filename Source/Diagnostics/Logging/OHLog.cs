using System;
using System.Collections.Concurrent;
using Verse;

namespace OverHaulers
{
    public enum LogDomain
    {
        Lifecycle,
        Calibration,
        Integration,
        Topology,
        Solver,
        Presentation,
        TestBench,
        Performance
    }

    /// <summary>
    /// Centralized, thread-safe diagnostics and logging engine for OverHaulers.
    /// Provides dynamic context-based throttles for hardcoded error logs (max 3 prints per unique source),
    /// while routing informative lifecycle events through localized XML translation keys.
    /// </summary>
    public static partial class OHLog
    {
        #region 1. DYNAMIC ATOMIC THROTTLING ENGINE

        public const int MaxWarnCutoff = 3;
        private static readonly ConcurrentDictionary<string, int> throttleCounts = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// Emits a hardcoded, unlocalized warning with full exception details, 
        /// throttled to a maximum of 3 prints per unique (domain + context) key to prevent log spam.
        /// </summary>
        public static void Warn(LogDomain domain, string context, Exception ex = null, string customMessage = null)
        {
            string throttleKey = $"{domain}_{context}";
            int count = throttleCounts.AddOrUpdate(throttleKey, 1, (_, cur) => cur + 1);

            if (count <= MaxWarnCutoff)
            {
                string countTag = $"#{count}/{MaxWarnCutoff}";
                string msgPart = !string.IsNullOrEmpty(customMessage) ? $" {customMessage}" : "";
                string exPart = ex != null ? $": {ex}" : "";

                Log.Warning($"[Over Haulers] {domain} Warning {countTag} [{context}]{msgPart}{exPart}");
            }
        }

        /// <summary>
        /// Emits an unthrottled, hardcoded critical error.
        /// </summary>
        public static void Error(LogDomain domain, string context, Exception ex = null, string customMessage = null)
        {
            string msgPart = !string.IsNullOrEmpty(customMessage) ? $" {customMessage}" : "";
            string exPart = ex != null ? $": {ex}" : "";

            Log.Error($"[Over Haulers] {domain} Error [{context}]{msgPart}{exPart}");
        }

        #endregion

        #region 2. DOMAIN CONVENIENCE WRAPPERS & INFORMATIVE LOGS

        // --- LIFECYCLE DOMAIN ---

        /// <summary>
        /// Logs events related to the lifecycle of the mod, such as world loading and session management.
        /// </summary>
        public static class Lifecycle
        {
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Lifecycle, context, ex, customMessage);
        }

        // --- CALIBRATION DOMAIN ---

        /// <summary>
        /// Logs events related to species baseline mass capacity calibration sweeps and resolution notices.
        /// Automatically routes into the active ActivityJournal if a multi-action session is open.
        /// </summary>
        public static class Calibration
        {
            /// <summary>
            /// Logs the completion of a calibration sweep, including timing and counts of various categories.
            /// Automatically routes into the active ActivityJournal if a multi-action session is open.
            /// </summary>
            /// <param name="ms">The duration of the calibration sweep in milliseconds.</param>
            /// <param name="context">The context or source of the calibration sweep.</param>
            /// <param name="pristine">The count of pristine entries processed.</param>
            /// <param name="testing">The count of testing entries processed.</param>
            /// <param name="live">The count of live entries processed.</param>
            /// <param name="emergency">The count of emergency entries processed.</param>
            /// <param name="total">The total count of entries processed.</param>
            public static void SweepCompleted(double ms, string context, int pristine, int testing, int live, int emergency, int total)
            {
                if (ActivityJournal.IsActive)
                {
                    ActivityJournal.RecordBullet("OverHaulers_Journal_CalibrationSweep".Translate(
                        pristine,
                        testing,
                        live,
                        emergency,
                        ms.ToString("F1")
                    ).ToString());
                }
                else
                {
                    Log.Message("OverHaulers_Log_Calibration_SweepComplete".Translate(
                        ms.ToString("F1"),
                        context,
                        pristine,
                        testing,
                        live,
                        emergency,
                        total
                    ).ToString());
                }
            }

            /// <summary>
            /// Logs a pending notice for calibration, including the count and list of species.
            /// </summary>
            /// <param name="count">The number of pending calibration notices.</param>
            /// <param name="speciesList">A comma-separated list of species involved in the pending notice.</param>
            public static void PendingNotice(int count, string speciesList)
            {
                if (ActivityJournal.IsActive)
                {
                    ActivityJournal.RecordBullet("OverHaulers_Journal_CalibrationPendingNotice".Translate(count, speciesList).ToString());
                }
                else
                {
                    Log.Message("OverHaulers_Log_Calibration_PendingNotice".Translate(count, speciesList).ToString());
                }
            }

            /// <summary>
            /// Logs a warning message within the calibration domain, optionally including an exception and custom message.
            /// </summary>
            /// <param name="context">The context or source of the warning.</param>
            /// <param name="ex">An optional exception associated with the warning.</param>
            /// <param name="customMessage">An optional custom message to include in the warning.</param>
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Calibration, context, ex, customMessage);
        }

        // --- INTEGRATION DOMAIN ---

        /// <summary>
        /// Logs events related to integration with external mods and systems.
        /// Automatically routes into the active ActivityJournal if a multi-action session is open.
        /// </summary>
        public static class Integration
        {
            /// <summary>
            /// Logs the initialization of Harmony compatibility, including the number of engine hooks applied and the time taken.
            /// </summary>
            /// <param name="ms">The time taken for Harmony initialization in milliseconds.</param>
            /// <param name="patchCount">The number of engine hooks applied during Harmony initialization.</param>
            public static void HarmonyInitialized(double ms, int patchCount)
            {
                if (ActivityJournal.IsActive)
                {
                    ActivityJournal.RecordBullet("OverHaulers_Journal_HarmonyInitialized".Translate(
                        patchCount, 
                        ms.ToString("F1")
                    ).ToString());
                }
                else
                {
                    Log.Message($"[Over Haulers] Harmony compatibility initialized ({ms:F1} ms, one-time): {patchCount} engine hooks applied.");
                }
            }

            /// <summary>
            /// Logs the detection of external patches on MassUtility.Capacity, including the mod owners responsible.
            /// </summary>
            /// <param name="modOwners">A string listing the mod owners that applied external patches.</param>
            public static void ExternalPatchesDetected(string modOwners)
            {
                if (ActivityJournal.IsActive)
                {
                    ActivityJournal.RecordBullet("OverHaulers_Journal_ExternalPatchesDetected".Translate(modOwners).ToString());
                }
                else
                {
                    Log.Message("OverHaulers_Log_Bootstrap_ExternalPatchesDetected".Translate(modOwners).ToString());
                }
            }

            /// <summary>
            /// Logs the binding of a stat presentation, including the owner, stat name, and any suffix used.
            /// </summary>
            /// <param name="owner">The owner of the stat presentation.</param>
            /// <param name="statName">The name of the stat being bound.</param>
            /// <param name="suffix">The suffix applied to the stat presentation.</param>
            public static void StatPresentationBound(string owner, string statName, string suffix)
            {
                if (ActivityJournal.IsActive)
                {
                    ActivityJournal.RecordBullet("OverHaulers_Journal_StatPresentationBound".Translate(statName, owner, suffix).ToString());
                }
                else
                {
                    Log.Message("OverHaulers_Log_Integration_StatPresentationBound".Translate(statName, owner, suffix).ToString());
                }
            }

            /// <summary>
            /// Logs the initialization of the medical catalog, including the number of replacements, implants, and drugs indexed, as
            ///  well as the time taken.
            /// </summary>
            /// <param name="ms">The time taken for medical catalog initialization in milliseconds.</param>
            /// <param name="replacements">The number of replacements indexed.</param>
            /// <param name="implants">The number of implants indexed.</param>
            /// <param name="drugs">The number of drugs indexed.</param>
            public static void MedicalCatalogInitialized(float ms, int replacements, int implants, int drugs)
            {
                if (ActivityJournal.IsActive)
                {
                    ActivityJournal.RecordBullet("OverHaulers_Journal_MedicalCatalog".Translate(
                        replacements,
                        implants,
                        drugs,
                        ms.ToString("F1")
                    ).ToString());
                }
                else
                {
                    Log.Message("OverHaulers_Log_Integration_MedicalCatalogIndexed".Translate(
                        ms.ToString("F1"),
                        replacements,
                        implants,
                        drugs
                    ).ToString());
                }
            }

            /// <summary>
            /// Logs the recovery of a stale preference identified by its key.
            /// </summary>
            /// <param name="staleKey">The key of the stale preference that was recovered.</param>
            public static void StalePreferenceRecovered(string staleKey) =>
                Log.Message("OverHaulers_Log_Integration_StalePreferenceRecovered".Translate(staleKey).ToString());

            /// <summary>
            /// Logs a warning message within the integration domain, optionally including an exception and a custom message.
            /// </summary>
            /// <param name="context">The context in which the warning occurred.</param>
            /// <param name="ex">An optional exception associated with the warning.</param>
            /// <param name="customMessage">An optional custom message to include in the warning.</param>
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Integration, context, ex, customMessage);
        }

        // --- TOPOLOGY DOMAIN ---

        /// <summary>
        /// Logs events related to the compilation and management of species skeletal topologies.
        /// Automatically routes into the active ActivityJournal if a multi-action session is open.
        /// </summary>
        public static class Topology
        {
            public static void CompilationCompleted(float ms, int species, int maxParts, string maxDef, int maxDepth)
            {
                if (ActivityJournal.IsActive)
                {
                    ActivityJournal.RecordBullet("OverHaulers_Journal_TopologyCompiled".Translate(
                        species,
                        ms.ToString("F1"),
                        maxDef,
                        maxParts,
                        maxDepth
                    ).ToString());
                }
                else
                {
                    Log.Message("OverHaulers_Log_Topology_CompilationComplete".Translate(
                        ms.ToString("F1"),
                        species,
                        maxParts,
                        maxDef,
                        maxDepth
                    ).ToString());
                }
            }

            public static void TopologyInvalidated(string bodyDefName) => 
                Log.Message("OverHaulers_Log_Topology_TemplatePurged".Translate(bodyDefName).ToString());

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Topology, context, ex, customMessage);
        }

        // --- SOLVER DOMAIN ---

        /// <summary>
        /// Logs events related to the solver component of the mod.
        /// </summary>
        public static class Solver
        {
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Solver, context, ex, customMessage);
        }

        // --- PRESENTATION DOMAIN ---

        /// <summary>
        /// Logs events related to the presentation layer of the mod.
        /// </summary>
        public static class Presentation
        {
            public static void CacheScaledUp(int oldCap, int newCap, int recycles) =>
                Log.Message("OverHaulers_Log_Presentation_ScaledUp".Translate(oldCap, newCap, recycles).ToString());

            public static void CacheFlushed(int clearedCount, int restoredCap)
            {
                if (ActivityJournal.IsActive) return;

                Log.Message("OverHaulers_Log_Presentation_Flushed".Translate(clearedCount, restoredCap).ToString());
            }

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Presentation, context, ex, customMessage);
        }

        // --- TESTBENCH DOMAIN ---

        /// <summary>
        /// Logs events related to the testbench component of the mod.
        /// </summary>
        public static class TestBench
        {
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.TestBench, context, ex, customMessage);
        }

        #endregion
    }
}
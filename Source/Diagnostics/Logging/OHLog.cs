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
            public static void WorldLoadedReset() => 
                Log.Message("OverHaulers_Log_World_Reset".Translate().ToString());

            public static void FullRecompilationCompleted(double ms) =>
                Log.Message("OverHaulers_Log_Lifecycle_FullRecompileComplete".Translate(ms.ToString("F1")).ToString());

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Lifecycle, context, ex, customMessage);
        }

        // --- CALIBRATION DOMAIN ---

        /// <summary>
        /// Logs events related to species baseline mass capacity calibration sweeps and resolution notices.
        /// </summary>
        public static class Calibration
        {
            public static void SweepCompleted(double ms, string context, int pristine, int testing, int live, int emergency, int total) =>
                Log.Message("OverHaulers_Log_Calibration_SweepComplete".Translate(
                    ms.ToString("F1"),
                    context,
                    pristine,
                    testing,
                    live,
                    emergency,
                    total
                ).ToString());

            public static void PendingNotice(int count, string speciesList) =>
                Log.Message("OverHaulers_Log_Calibration_PendingNotice".Translate(count, speciesList).ToString());

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Calibration, context, ex, customMessage);
        }

        // --- INTEGRATION DOMAIN ---

        /// <summary>
        /// Logs events related to integration with external mods and systems.
        /// </summary>
        public static class Integration
        {
            public static void ExternalPatchesDetected(string modOwners) => 
                Log.Message("OverHaulers_Log_Bootstrap_ExternalPatchesDetected".Translate(modOwners).ToString());

            public static void StatPresentationBound(string owner, string statName, string suffix) => 
                Log.Message("OverHaulers_Log_Integration_StatPresentationBound".Translate(statName, owner, suffix).ToString());

            public static void MedicalCatalogInitialized(float ms, int replacements, int implants, int drugs) => 
                Log.Message("OverHaulers_Log_Integration_MedicalCatalogIndexed".Translate(ms.ToString("F1"), replacements, implants, drugs).ToString());

            public static void StalePreferenceRecovered(string staleKey) =>
                Log.Message("OverHaulers_Log_Integration_StalePreferenceRecovered".Translate(staleKey).ToString());

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Integration, context, ex, customMessage);
        }

        // --- TOPOLOGY DOMAIN ---

        /// <summary>
        /// Logs events related to the compilation and management of species skeletal topologies.
        /// </summary>
        public static class Topology
        {
            public static void CompilationCompleted(float ms, int species, int maxParts, string maxDef, int maxDepth) => 
                Log.Message("OverHaulers_Log_Topology_CompilationComplete".Translate(ms.ToString("F1"), species, maxParts, maxDef, maxDepth).ToString());

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
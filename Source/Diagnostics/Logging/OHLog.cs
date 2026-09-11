using System;
using System.Collections.Concurrent;
using Verse;

namespace OverHaulers
{
    public enum LogDomain
    {
        Lifecycle,
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
        public static class Lifecycle
        {
            public static void WorldLoadedReset() => 
                Log.Message("OverHaulers_Log_WorldReset".Translate().ToString());

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Lifecycle, context, ex, customMessage);
        }

        // --- INTEGRATION DOMAIN ---
        public static class Integration
        {
            public static void ExternalPatchesDetected(string modOwners) => 
                Log.Message("OverHaulers_Log_ExternalMassPatchesDetected".Translate(modOwners).ToString());

            public static void CustomDriverRegistered(string driverId) => 
                Log.Message("OverHaulers_Log_CustomDriverRegistered".Translate(driverId).ToString());

            public static void StatDrivenBound(string owner, string statName, string suffix) => 
                Log.Message("OverHaulers_Log_StatDriven".Translate(statName, owner, suffix).ToString());

            public static void DirectFallbackActive(string suffix) => 
                Log.Message("OverHaulers_Log_DirectFallback".Translate(suffix).ToString());

            public static void MedicalCatalogInitialized(float ms, int replacements, int implants, int drugs) => 
                Log.Message("OverHaulers_Log_MedicalCatalogInitialized".Translate(ms.ToString("F2"), replacements, implants, drugs).ToString());

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Integration, context, ex, customMessage);
        }

        // --- TOPOLOGY DOMAIN ---
        public static class Topology
        {
            public static void CompilationCompleted(float ms, int species, int maxParts, string maxDef, int maxDepth) => 
                Log.Message("OverHaulers_Log_TopologyCompilationCompleted".Translate(ms.ToString("F2"), species, maxParts, maxDef, maxDepth).ToString());

            public static void TopologyInvalidated(string bodyDefName) => 
                Log.Message("OverHaulers_Log_TopologyInvalidated".Translate(bodyDefName).ToString());

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Topology, context, ex, customMessage);
        }

        // --- SOLVER DOMAIN ---
        public static class Solver
        {
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Solver, context, ex, customMessage);
        }

        // --- PRESENTATION DOMAIN ---
        public static class Presentation
        {
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Presentation, context, ex, customMessage);
        }

        // --- TESTBENCH DOMAIN ---
        public static class TestBench
        {
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.TestBench, context, ex, customMessage);
        }

        #endregion
    }
}
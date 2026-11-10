using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-05] CORE LOG EVENTS
    /// Localized informative events raised by Core subsystems (calibration, topology, medical catalog).
    /// Each event shares one positional argument list between its journal bullet and console message.
    /// Resides under Source/Core/Logging/.
    /// </summary>
    internal static class CoreLogEvents
    {
        #region 1. CALIBRATION

        /// <summary>Logs the completion of a species baseline calibration sweep.</summary>
        public static void CalibrationSweepCompleted(double ms, int pristine, int testing, int live, int emergency, int total) =>
            OHLog.Event("OverHaulers_Event_Calibration_SweepComplete",
                ms.ToString("F1"), pristine, testing, live, emergency, total);

        /// <summary>Logs a notice that caravan-capable species are running on temporary benchmarks.</summary>
        public static void CalibrationPendingNotice(int count, string speciesList) =>
            OHLog.Event("OverHaulers_Event_Calibration_PendingNotice",
                count, speciesList);

        #endregion

        #region 2. TOPOLOGY

        /// <summary>Logs the completion of a skeletal topology compilation pass.</summary>
        public static void TopologyCompilationCompleted(float ms, int species, int maxParts, string maxDef, int maxDepth) =>
            OHLog.Event("OverHaulers_Event_Topology_CompilationComplete",
                ms.ToString("F1"), species, maxParts, maxDef, maxDepth);

        /// <summary>Logs that a compiled topology template was purged.</summary>
        public static void TopologyInvalidated(string bodyDefName) =>
            OHLog.Event("OverHaulers_Event_Topology_TemplatePurged", bodyDefName);

        #endregion

        #region 3. MEDICAL CATALOG

        /// <summary>Logs the completion of medical catalog indexing.</summary>
        public static void MedicalCatalogInitialized(float ms, int replacements, int implants, int drugs) =>
            OHLog.Event("OverHaulers_Event_MedicalCatalog_Indexed",
                ms.ToString("F1"), replacements, implants, drugs);

        #endregion
    }
}

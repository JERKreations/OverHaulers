using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-05] PRESENTATION LOG EVENTS
    /// Localized informative events raised by the Presentation layer's model cache.
    /// Resides under Source/Presentation/.
    /// </summary>
    internal static class PresentationLogEvents
    {
        /// <summary>Logs that the presentation model cache scaled up under load.</summary>
        public static void CacheScaledUp(int oldCap, int newCap, int recycles) =>
            OHLog.Info("OverHaulers_Log_Presentation_ScaledUp", oldCap, newCap, recycles);

        /// <summary>Logs that the presentation model cache was flushed; suppressed while a journal session summarises the phase.</summary>
        public static void CacheFlushed(int clearedCount, int restoredCap) =>
            OHLog.InfoUnlessJournaled("OverHaulers_Log_Presentation_Flushed", clearedCount, restoredCap);
    }
}

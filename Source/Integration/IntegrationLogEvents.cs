using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-05] INTEGRATION LOG EVENTS
    /// Localized informative events raised by the Integration layer (Harmony setup, stat adoption, preference recovery).
    /// Resides under Source/Integration/.
    /// </summary>
    internal static class IntegrationLogEvents
    {
        /// <summary>Logs the one-time Harmony initialization with the number of engine hooks applied.</summary>
        public static void HarmonyInitialized(double ms, int patchCount) =>
            OHLog.Event("OverHaulers_Journal_HarmonyInitialized", "OverHaulers_Log_Integration_HarmonyInitialized",
                ms.ToString("F1"), patchCount);

        /// <summary>Logs the adoption of a caravan mass capacity stat presentation.</summary>
        public static void StatPresentationBound(string owner, string statName, string suffix) =>
            OHLog.Event("OverHaulers_Journal_StatPresentationBound", "OverHaulers_Log_Integration_StatPresentationBound",
                statName, owner, suffix);

        /// <summary>Logs the recovery of a stale saved presentation preference.</summary>
        public static void StalePreferenceRecovered(string staleKey) =>
            OHLog.Info("OverHaulers_Log_Integration_StalePreferenceRecovered", staleKey);
    }
}

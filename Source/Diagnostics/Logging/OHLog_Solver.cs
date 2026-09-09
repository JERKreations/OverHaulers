using System;
using System.Threading;
using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 6. [SOLV-00] SOLVER LOGGING DOMAIN

        /// <summary>
        /// Logs warnings related to solver exceptions, capacity level retrieval issues, and consciousness impact failures.
        /// Each warning is throttled to a maximum of three occurrences to prevent log spam.
        /// </summary>
        public static class Solver
        {
            public static void WarnException(string pawnName, Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountSolverException);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[OverHaulers Solver Error #{count}/{MaxWarnCutoff} - {pawnName}]: " + ex.ToString());
                }
            }

            public static void WarnGetCapacityLevelException(string pawnName, string capacityName, Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountGetCapacityLevel);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[OverHaulers Capacity Error #{count}/{MaxWarnCutoff} - {pawnName}:{capacityName}]: " + ex.ToString());
                }
            }

            public static void WarnConsciousnessImpactFailed(Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountConsciousnessImpact);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[OverHaulers Consciousness Warning #{count}/{MaxWarnCutoff}]: " + ex.ToString());
                }
            }

            public static void WarnSystemicAilmentExtractionFailed(string pawnName, Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountSystemicAilmentExtraction);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[OverHaulers Systemic Ailment Extraction Warning #{count}/{MaxWarnCutoff} - {pawnName}]: " + ex.ToString());
                }
            }
        }

        #endregion
    }
}
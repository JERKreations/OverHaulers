using System.Collections.Generic;
using System.Text;
using System.Threading;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Centralized, thread-safe logging and diagnostics wrapper for OverHaulers.
    /// Implements a 3-print count-based interlocked throttle to provide full stack traces without log spam.
    /// </summary>
    public static partial class OHLog
    {
        #region 1. SHARED TRACE BUFFERS & ATOMIC THROTTLES

        public struct SafetyClampRecord
        {
            public float RawMass;
            public float ClampedMass;
        }

        private const int MaxWarnCutoff = 3;

        private static readonly HashSet<int> pendingEvictedPawnIds = new HashSet<int>(64);
        private static readonly Dictionary<string, SafetyClampRecord> pendingClampedPawns = new Dictionary<string, SafetyClampRecord>(16);
        private static readonly StringBuilder pooledReportBuilder = new StringBuilder(1024);

        private static int warnCountPatchDisassembly = 0;
        private static int warnCountGetCapacityLevel = 0;
        private static int warnCountMetabolicInit = 0;
        private static int warnCountConsciousnessImpact = 0;
        private static int warnCountHarmonyPatchFailed = 0;
        private static int warnCountTopologyCompilationFailed = 0;
        private static int warnCountSolverException = 0;
        private static int warnCountBodySizeAccess = 0;
        private static int warnCountReferenceDesync = 0;
        private static int warnCountSystemicAilmentExtraction = 0;
        private static int warnCountCalibrationFallback = 0;
        private static int warnCountPresentationException = 0;
        private static int warnCountTestBenchException = 0;

        #endregion
        
        #region 2. [LIFE-00] LIFECYCLE LOGGING DOMAIN

        /// <summary>
        /// Logs warnings and informational messages related to the OverHaulers lifecycle, including world load resets and other lifecycle events.
        /// </summary>
        public static class Lifecycle
        {
            public static void WorldLoadedReset()
            {
                Log.Message("OverHaulers_Log_WorldReset".Translate().ToString());
            }

            /// <summary>
            /// Logs a warning indicating a species' baseline calibration was irrecoverable and a testing/fallback scalar was applied instead.
            /// </summary>
            public static void WarnCalibrationFallbackApplied(string speciesDefName, float fallbackScalar)
            {
                int count = Interlocked.Increment(ref warnCountCalibrationFallback);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[Over Haulers] Calibration Warning #{count}/{MaxWarnCutoff}: Irrecoverable mass calibration failure for caravan-capable species '{speciesDefName}'. Falling back to testing and fallback baseline assumption ({fallbackScalar}).");
                }
            }
        }

        #endregion
    }
}
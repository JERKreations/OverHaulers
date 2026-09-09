using System;
using System.Threading;
using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 5. [TOPO-00] TOPOLOGY LOGGING DOMAIN

        /// <summary>
        /// Provides logging functionality specifically for the topology layout compiler within the OverHaulers mod.
        /// Includes methods for logging compilation completion, warnings, topology invalidations, and reference desynchronization issues.
        /// </summary>
        public static class Topology
        {
            public static void CompilationCompleted(float ms, int speciesCount, int maxPartCount, string maxPartDefName, int maxSkeletalDepth)
            {
                Log.Message("OverHaulers_Log_TopologyCompilationCompleted".Translate(
                    ms.ToString("F2"), 
                    speciesCount,
                    maxPartCount, 
                    maxPartDefName, 
                    maxSkeletalDepth
                ).ToString());
            }

            public static void WarnCompilationFailed(Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountTopologyCompilationFailed);
                if (count <= MaxWarnCutoff)
                {
                    Log.Error($"[Over Haulers] Topology Compilation Error #{count}/{MaxWarnCutoff}: " + ex.ToString());
                }
            }

            public static void TopologyInvalidated(string bodyDefName)
            {
                Log.Message("OverHaulers_Log_TopologyInvalidated".Translate(bodyDefName).ToString());
            }

            public static void WarnReferenceDesync(string bodyDefName)
            {
                int count = Interlocked.Increment(ref warnCountReferenceDesync);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[Over Haulers] Reference Desync Warning #{count}/{MaxWarnCutoff} - '{bodyDefName}': Dynamic body structure change or reference desync detected. Re-compiling species layout template.");
                }
            }
        }

        #endregion
    }
}
using System;
using System.Threading;
using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 3. [INT-00] INTEGRATION LOGGING DOMAIN

        /// <summary>
        /// Logs warnings and informational messages related to external integration, 
        /// including mod patches, custom drivers, stat-driven bindings, and direct fallbacks.
        /// </summary>
        public static class Integration
        {
            public static void ExternalPatchesDetected(string modOwners)
            {
                Log.Message("OverHaulers_Log_ExternalMassPatchesDetected".Translate(modOwners).ToString());
            }

            public static void CustomDriverRegistered(string driverId)
            {
                Log.Message("OverHaulers_Log_CustomDriverRegistered".Translate(driverId).ToString());
            }

            public static void StatDrivenBound(string owner, string statName, string suffix)
            {
                Log.Message("OverHaulers_Log_StatDriven".Translate(statName, owner, suffix).ToString());
            }

            public static void DirectFallbackActive(string suffix)
            {
                Log.Message("OverHaulers_Log_DirectFallback".Translate(suffix).ToString());
            }

            public static void StandaloneBypassEngaged(string modOwners)
            {
                Log.Message("OverHaulers_Log_StandaloneBypassEngaged".Translate(modOwners).ToString());
            }

            public static void WarnPatchDisassemblyFailed(string patchName, Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountPatchDisassembly);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[Over Haulers] CIL Disassembly Warning #{count}/{MaxWarnCutoff} - {patchName}]: " + ex.ToString());
                }
            }

            public static void WarnHarmonyPatchFailed(Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountHarmonyPatchFailed);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[Over Haulers] Harmony Error #{count}/{MaxWarnCutoff}: " + ex.ToString());
                }
            }

            public static void WarnMetabolicInitFailed(Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountMetabolicInit);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[Over Haulers] Metabolic Error #{count}/{MaxWarnCutoff}: " + ex.ToString());
                }
            }

            public static void WarnBodySizeAccessFailed(string pawnName, Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountBodySizeAccess);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[Over Haulers] BodySize Error #{count}/{MaxWarnCutoff} - {pawnName}: " + ex.ToString());
                }
            }

            public static void MedicalCatalogInitialized(float ms, int replacementCount, int implantCount, int drugCount)
            {
                Log.Message("OverHaulers_Log_MedicalCatalogInitialized".Translate(
                    ms.ToString("F2"),
                    replacementCount,
                    implantCount,
                    drugCount
                ).ToString());
            }
        }

        #endregion
    }
}
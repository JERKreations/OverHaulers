using System;
using System.Threading;
using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 8. [TEST-00] TESTBENCH LOGGING DOMAIN

        /// <summary>
        /// Logs warnings for the Mod Settings Interactive Test Bench dev tool (sandbox harness, subject evaluation),
        /// distinct from the core Solver domain since these only ever run in dev-facing sandbox code, never live gameplay.
        /// Each warning is throttled to a maximum of three occurrences to prevent log spam.
        /// </summary>
        public static class TestBench
        {
            public static void WarnException(string context, Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountTestBenchException);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[Over Haulers] TestBench Error #{count}/{MaxWarnCutoff} - {context}]: " + ex.ToString());
                }
            }
        }

        #endregion
    }
}
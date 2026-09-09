using System;
using System.Threading;
using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 7. [PRES-00] PRESENTATION LOGGING DOMAIN

        /// <summary>
        /// Logs warnings for UI/rendering-layer exceptions (caravan tooltips, info card overlay, view projection),
        /// distinct from the core Solver domain since these failures only degrade rendering, not calculation.
        /// Each warning is throttled to a maximum of three occurrences to prevent log spam.
        /// </summary>
        public static class Presentation
        {
            public static void WarnException(string context, Exception ex)
            {
                int count = Interlocked.Increment(ref warnCountPresentationException);
                if (count <= MaxWarnCutoff)
                {
                    Log.Warning($"[Over Haulers] Presentation Error #{count}/{MaxWarnCutoff} - {context}]: " + ex.ToString());
                }
            }
        }

        #endregion
    }
}
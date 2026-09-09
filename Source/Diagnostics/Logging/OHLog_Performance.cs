using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 4. [PERF-00] PERFORMANCE LOGGING DOMAIN

        /// <summary>
        /// Logs warnings and informational messages related to performance metrics, 
        /// including pawn evictions, safety floor clamps, and cumulative performance reports.
        /// </summary>
        public static class Performance
        {
            public static void RecordPawnEvicted(int thingID)
            {
                if (OverHaulers.settings == null) return;
                if (OverHaulers.settings.reportMetricsIntervalHours > 0 && OverHaulers.settings.logPawnEvictions)
                {
                    pendingEvictedPawnIds.Add(thingID);
                }
            }

            public static void RecordSafetyFloorClamped(string pawnName, float rawMass, float clampedMass)
            {
                if (OverHaulers.settings == null) return;
                if (OverHaulers.settings.reportMetricsIntervalHours > 0 && OverHaulers.settings.logSafetyFloorClamps)
                {
                    pendingClampedPawns[pawnName] = new SafetyClampRecord 
                    { 
                        RawMass = rawMass, 
                        ClampedMass = clampedMass 
                    };
                }
            }

            public static void ClearTraceBuffers()
            {
                pendingEvictedPawnIds.Clear();
                pendingClampedPawns.Clear();
            }

            public static void DispatchCumulativeReport(
                int elapsedHours,
                int totalQueries, int mainQueries, int bgQueries,
                float hitRatio, int hits, int misses,
                int invalidations, int evictions,
                int activeWorkspaces)
            {
                var settings = OverHaulers.settings;
                if (settings == null) return;

                lock (pooledReportBuilder)
                {
                    pooledReportBuilder.Clear();

                    pooledReportBuilder.Append("OverHaulers_Log_PerformanceReportHeader".Translate(elapsedHours).ToString());

                    if (settings.logQueryMetrics)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Queries".Translate(totalQueries, mainQueries, bgQueries).ToString());
                    }

                    if (settings.logCacheMetrics)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Cache".Translate(hitRatio.ToString("F1"), hits, misses).ToString());
                    }

                    if (settings.logLifecycleMetrics)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_LifeCycle".Translate(invalidations, evictions).ToString());
                    }

                    if (settings.logWorkspaceMetrics)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Workspaces".Translate(activeWorkspaces).ToString());
                    }

                    if (settings.logSafetyFloorClamps && pendingClampedPawns.Count > 0)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_SafetyFloorClampsHeader".Translate().ToString());
                        foreach (var kvp in pendingClampedPawns)
                        {
                            pooledReportBuilder.AppendLine();
                            pooledReportBuilder.Append("OverHaulers_Log_SafetyFloorClampEntry".Translate(
                                kvp.Key, 
                                kvp.Value.RawMass.ToString("F2"), 
                                kvp.Value.ClampedMass.ToString("F2")
                            ).ToString());
                        }
                    }

                    if (settings.logPawnEvictions && pendingEvictedPawnIds.Count > 0)
                    {
                        string idsFormatted = string.Join(", ", pendingEvictedPawnIds);
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_EvictedPawnsBuffer".Translate(pendingEvictedPawnIds.Count, idsFormatted).ToString());
                    }

                    Log.Message(pooledReportBuilder.ToString());

                    pooledReportBuilder.Clear();
                    ClearTraceBuffers();
                }
            }
        }

        #endregion
    }
}
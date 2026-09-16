using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 1. [PERF-00] PERFORMANCE TRACE MODELS & STATE BUFFERS

        public struct SafetyClampRecord
        {
            public float RawMass;
            public float ClampedMass;
        }

        #endregion

        #region 2. [PERF-00] PERFORMANCE LOGGING DOMAIN

        /// <summary>
        /// Manages performance telemetry buffers, periodic cumulative reporting,
        /// safety floor clamp tracking, and pawn cache eviction auditing.
        /// </summary>
        public static class Performance
        {
            private static readonly HashSet<int> pendingEvictedPawnIds = new HashSet<int>(64);
            private static readonly Dictionary<string, SafetyClampRecord> pendingClampedPawns = new Dictionary<string, SafetyClampRecord>(16);
            private static readonly StringBuilder pooledReportBuilder = new StringBuilder(1024);
            private static readonly object bufferLock = new object();

            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Performance, context, ex, customMessage);

            public static void RecordPawnEvicted(int thingID)
            {
                if (OverHaulers.settings == null) return;
                if (OverHaulers.settings.reportMetricsIntervalHours > 0 && OverHaulers.settings.logPawnEvictions)
                {
                    lock (bufferLock)
                    {
                        pendingEvictedPawnIds.Add(thingID);
                    }
                }
            }

            public static void RecordSafetyFloorClamped(string pawnName, float rawMass, float clampedMass)
            {
                if (OverHaulers.settings == null) return;
                if (OverHaulers.settings.reportMetricsIntervalHours > 0 && OverHaulers.settings.logSafetyFloorClamps)
                {
                    lock (bufferLock)
                    {
                        pendingClampedPawns[pawnName] = new SafetyClampRecord 
                        { 
                            RawMass = rawMass, 
                            ClampedMass = clampedMass 
                        };
                    }
                }
            }

            public static void ClearTraceBuffers()
            {
                lock (bufferLock)
                {
                    pendingEvictedPawnIds.Clear();
                    pendingClampedPawns.Clear();
                }
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

                lock (bufferLock)
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
                    pendingEvictedPawnIds.Clear();
                    pendingClampedPawns.Clear();
                }
            }
        }

        #endregion
    }
}
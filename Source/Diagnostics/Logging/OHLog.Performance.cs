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

            /// <summary>
            /// Compiles and dispatches the localized cumulative performance report to the RimWorld console log.
            /// Synchronized via bufferLock to ensure thread-safe StringBuilder assembly and trace buffer clearing.
            /// </summary>
            public static void DispatchCumulativeReport(
                int elapsedHours,
                // Query Metrics
                int totalQueries, int mainQueries, int bgQueries,
                // Hit Ratios
                float mainHitRatio, int hits, int misses,
                float bgHitRatio, int bgHits, int bgMisses,
                float totalHitRatio,
                // Lifecycle & Debounce
                int invalidations, int culledInvalidations, float debounceEfficiency, int evictions,
                // Snapshot Table Health & Parity
                int mainCacheCount, int snapshotOccupied, int tableCapacity,
                float avgProbeDepth, int peakProbe, int maxProbeSteps, int displacements,
                int retries, int timeouts,
                // Pooled Workspaces
                int activeWorkspaces)
            {
                var settings = OverHaulers.settings;
                if (settings == null) return;

                lock (bufferLock)
                {
                    pooledReportBuilder.Clear();

                    pooledReportBuilder.Append("OverHaulers_Log_PerformanceReportHeader".Translate(elapsedHours).ToString());

                    // 1. Query Metrics
                    if (settings.logQueryMetrics)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Queries".Translate(
                            totalQueries, 
                            mainQueries, 
                            bgQueries
                        ).ToString());
                    }

                    // 2. Cache Hit Efficiency
                    if (settings.logCacheMetrics)
                    {
                        pooledReportBuilder.AppendLine();
                        if (bgQueries > 0)
                        {
                            pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_CacheDetailed".Translate(
                                totalHitRatio.ToString("F1"),
                                mainHitRatio.ToString("F1"), hits, misses,
                                bgHitRatio.ToString("F1"), bgHits, bgMisses
                            ).ToString());
                        }
                        else
                        {
                            pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Cache".Translate(
                                mainHitRatio.ToString("F1"), 
                                hits, 
                                misses
                            ).ToString());
                        }
                    }

                    // 3. Snapshot Table & Probing Health (NEW)
                    if (settings.logSnapshotTableMetrics)
                    {
                        float loadPercent = tableCapacity > 0 ? ((float)snapshotOccupied / tableCapacity) * 100f : 0f;

                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_SnapshotHealth".Translate(
                            mainCacheCount,
                            snapshotOccupied,
                            tableCapacity,
                            loadPercent.ToString("F1"),
                            avgProbeDepth.ToString("F2"),
                            peakProbe,
                            maxProbeSteps,
                            displacements
                        ).ToString());

                        // If reader contention occurred during this interval, append secondary diagnostic line
                        if (retries > 0 || timeouts > 0)
                        {
                            pooledReportBuilder.AppendLine();
                            pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_SnapshotContention".Translate(
                                retries,
                                timeouts
                            ).ToString());
                        }
                    }

                    // 4. Lifecycle Events & Combat Debounce Efficacy
                    if (settings.logLifecycleMetrics)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_LifeCycleDetailed".Translate(
                            invalidations,
                            culledInvalidations,
                            debounceEfficiency.ToString("F1"),
                            evictions
                        ).ToString());
                    }

                    // 5. Pooled Workspaces
                    if (settings.logWorkspaceMetrics)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Workspaces".Translate(activeWorkspaces).ToString());
                    }

                    // 6. Safety Floor Clamps
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

                    // 7. Evicted Pawn IDs
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
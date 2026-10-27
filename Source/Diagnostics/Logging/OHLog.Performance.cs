using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace OverHaulers
{
    public static partial class OHLog
    {
        #region 1. [PERF-00] PERFORMANCE TRACE MODELS & STATE BUFFERS

        /// <summary>
        /// Represents a record of a pawn's mass being clamped to a safety floor, storing both the raw and clamped values.
        /// </summary>
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

            /// <summary>
            /// Logs a warning message within the performance telemetry domain.
            /// </summary>
            /// <param name="context">The context or source of the warning.</param>
            /// <param name="ex">An optional exception associated with the warning.</param>
            /// <param name="customMessage">An optional custom message to include in the warning.</param>
            public static void Warn(string context, Exception ex = null, string customMessage = null) => 
                OHLog.Warn(LogDomain.Performance, context, ex, customMessage);

            /// <summary>
            /// Logs the initialization of the snapshot table with its capacity, dynamic scaling status, minimum and maximum capacities,
            /// and probe depth. Automatically routes into the active ActivityJournal during engine bootstrap.
            /// </summary>
            public static void SnapshotTableInitialized(int capacity, bool dynamic, int min, int max, int probeDepth)
            {
                string mode = dynamic ? "Dynamic" : "Static";
                if (ActivityJournal.IsActive)
                {
                    ActivityJournal.RecordBullet("OverHaulers_Journal_SnapshotInitialized".Translate(capacity, mode, min, max, probeDepth).ToString());
                }
                else
                {
                    Log.Message("OverHaulers_Log_Snapshot_Initialized".Translate(capacity, mode, min, max, probeDepth).ToString());
                }
            }

            /// <summary>
            /// Logs the event of the snapshot table scaling up, including old and new capacities, current occupancy, and load percentage.
            /// </summary>
            public static void SnapshotTableScaledUp(int oldCapacity, int newCapacity, int occupancy, float loadPercent) =>
                Log.Message("OverHaulers_Log_Snapshot_ScaledUp".Translate(oldCapacity, newCapacity, occupancy, loadPercent.ToString("F1")).ToString());

            /// <summary>
            /// Logs the event of the snapshot table contracting, including old and new capacities, current occupancy, and load percentage.
            /// </summary>
            public static void SnapshotTableContracted(int oldCapacity, int newCapacity, int occupancy, float loadPercent) =>
                Log.Message("OverHaulers_Log_Snapshot_Contracted".Translate(oldCapacity, newCapacity, occupancy, loadPercent.ToString("F1")).ToString());

            /// <summary>
            /// Logs the event of the snapshot table being flushed, including the number of cleared entries and the restored capacity.
            /// Suppressed when an ActivityJournal session is active to prevent duplicate logging during world load.
            /// </summary>
            public static void SnapshotTableFlushed(int clearedCount, int restoredCapacity)
            {
                if (ActivityJournal.IsActive) return;

                Log.Message("OverHaulers_Log_Snapshot_Flushed".Translate(clearedCount, restoredCapacity).ToString());
            }

            /// <summary>
            /// Records the eviction of a pawn from the snapshot table, storing its unique ID for later reporting.
            /// </summary>
            /// <param name="thingID">The unique ID of the evicted pawn.</param>
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

            /// <summary>
            /// Records an instance where a pawn's mass was clamped to the safety floor, storing both the raw and clamped values.
            /// </summary>
            /// <param name="pawnName">The name of the pawn whose mass was clamped.</param>
            /// <param name="rawMass">The original mass of the pawn before clamping.</param>
            /// <param name="clampedMass">The mass of the pawn after being clamped to the safety floor.</param>
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

            /// <summary>
            /// Clears all pending trace buffers, including evicted pawn IDs and safety clamp records.
            /// </summary>
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
            /// <param name="s">The telemetry snapshot containing all aggregated performance metrics for the reporting interval.</param>
            public static void DispatchCumulativeReport(in TelemetrySnapshot s)
            {
                var settings = OverHaulers.settings;
                if (settings == null) return;

                lock (bufferLock)
                {
                    pooledReportBuilder.Clear();

                    try
                    {
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReportHeader".Translate(s.ElapsedHours).ToString());

                        // 1. Query Metrics
                        if (settings.logQueryMetrics)
                        {
                            pooledReportBuilder.AppendLine();
                            pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Queries".Translate(
                                s.TotalQueries, 
                                s.MainQueries, 
                                s.BgQueries
                            ).ToString());
                        }

                        // 2. Cache Hit Efficiency
                        if (settings.logCacheMetrics)
                        {
                            pooledReportBuilder.AppendLine();
                            if (s.BgQueries > 0)
                            {
                                pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_CacheDetailed".Translate(
                                    s.TotalHitRatio.ToString("F1"),
                                    s.MainHitRatio.ToString("F1"), s.Hits, s.Misses,
                                    s.BgHitRatio.ToString("F1"), s.BgHits, s.BgMisses
                                ).ToString());
                            }
                            else
                            {
                                pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Cache".Translate(
                                    s.MainHitRatio.ToString("F1"), 
                                    s.Hits, 
                                    s.Misses
                                ).ToString());
                            }
                        }

                        // 3. Presentation UI Model Cache
                        if (settings.logPresentationMetrics && (s.PresentationHits > 0 || s.PresentationMisses > 0 || s.PresentationActiveCount > 0))
                        {
                            pooledReportBuilder.AppendLine();
                            pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_PresentationDetailed".Translate(
                                s.PresentationActiveCount,
                                s.PresentationCapacity,
                                s.PresentationHits,
                                s.PresentationHitRatio.ToString("F1"),
                                s.PresentationMisses,
                                s.PresentationRecycles
                            ).ToString());
                        }

                        // 4. Snapshot Table & Probing Health
                        if (settings.logSnapshotTableMetrics)
                        {
                            float loadPercent = s.TableCapacity > 0 ? ((float)s.SnapshotOccupied / s.TableCapacity) * 100f : 0f;

                            pooledReportBuilder.AppendLine();
                            pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_SnapshotHealth".Translate(
                                s.MainCacheCount,
                                s.SnapshotOccupied,
                                s.TableCapacity,
                                loadPercent.ToString("F1"),
                                s.AvgProbeDepth.ToString("F2"),
                                s.PeakProbe,
                                s.MaxProbeSteps,
                                s.Displacements
                            ).ToString());

                            if (s.Retries > 0 || s.Timeouts > 0)
                            {
                                pooledReportBuilder.AppendLine();
                                pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_SnapshotContention".Translate(
                                    s.Retries,
                                    s.Timeouts
                                ).ToString());
                            }
                        }

                        // 5. Lifecycle Events & Combat Debounce Efficacy
                        if (settings.logLifecycleMetrics)
                        {
                            pooledReportBuilder.AppendLine();
                            pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_LifeCycleDetailed".Translate(
                                s.Invalidations,
                                s.CulledInvalidations,
                                s.DebounceEfficiency.ToString("F1"),
                                s.Evictions
                            ).ToString());
                        }

                        // 6. Pooled Workspaces
                        if (settings.logWorkspaceMetrics)
                        {
                            pooledReportBuilder.AppendLine();
                            pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Workspaces".Translate(s.ActiveWorkspaces).ToString());
                        }

                        // 7. Safety Floor Clamps
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

                        // 8. Evicted Pawn IDs
                        if (settings.logPawnEvictions && pendingEvictedPawnIds.Count > 0)
                        {
                            string idsFormatted = string.Join(", ", pendingEvictedPawnIds);
                            pooledReportBuilder.AppendLine();
                            pooledReportBuilder.Append("OverHaulers_Log_EvictedPawnsBuffer".Translate(pendingEvictedPawnIds.Count, idsFormatted).ToString());
                        }

                        Log.Message(pooledReportBuilder.ToString());
                    }
                    finally
                    {
                        pooledReportBuilder.Clear();
                        pendingEvictedPawnIds.Clear();
                        pendingClampedPawns.Clear();
                    }
                }
            }
        }

        #endregion
    }
}
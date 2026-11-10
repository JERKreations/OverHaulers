using System.Collections.Generic;
using System.Text;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Represents a record of a pawn's mass being clamped to a safety floor, storing both the raw and clamped values.
    /// </summary>
    public struct SafetyClampRecord
    {
        public float RawMass;
        public float ClampedMass;
    }

    /// <summary>
    /// [PERF-00] PERFORMANCE REPORT
    /// Manages performance telemetry trace buffers, periodic cumulative reporting,
    /// safety floor clamp tracking, snapshot table lifecycle events and pawn cache eviction auditing.
    /// Resides under Source/Core/Logging/; layered on <see cref="OHLog"/> for warnings and event routing.
    /// </summary>
    public static class PerformanceReport
    {
        #region 1. STATE BUFFERS & TOGGLES

        private static readonly HashSet<int> pendingEvictedPawnIds = new HashSet<int>(64);
        private static readonly Dictionary<string, SafetyClampRecord> pendingClampedPawns = new Dictionary<string, SafetyClampRecord>(16);
        private static readonly StringBuilder pooledReportBuilder = new StringBuilder(1024);
        private static readonly object bufferLock = new object();

        // Downward synchronized telemetry toggles (thread-safe, zero settings dereferencing)
        private static volatile bool isLogPawnEvictionsActive = false;
        private static volatile bool isLogSafetyFloorClampsActive = false;
        private static volatile bool isLogQueryMetricsActive = false;
        private static volatile bool isLogCacheMetricsActive = false;
        private static volatile bool isLogPresentationMetricsActive = false;
        private static volatile bool isLogSnapshotTableMetricsActive = false;
        private static volatile bool isLogLifecycleMetricsActive = false;
        private static volatile bool isLogWorkspaceMetricsActive = false;

        #endregion

        #region 2. CONFIGURATION & WARNINGS

        /// <summary>
        /// Configures performance logging flags pushed downward via <see cref="RuntimeConfiguration"/>.
        /// Preserves strict downward layer directionality (Logging never references Settings).
        /// </summary>
        public static void Configure(in RuntimeConfiguration config)
        {
            bool enabled = config.IsTelemetryEnabled;
            isLogPawnEvictionsActive = enabled && config.LogPawnEvictions;
            isLogSafetyFloorClampsActive = enabled && config.LogSafetyFloorClamps;
            isLogQueryMetricsActive = enabled && config.LogQueryMetrics;
            isLogCacheMetricsActive = enabled && config.LogCacheMetrics;
            isLogPresentationMetricsActive = enabled && config.LogPresentationMetrics;
            isLogSnapshotTableMetricsActive = enabled && config.LogSnapshotTableMetrics;
            isLogLifecycleMetricsActive = enabled && config.LogLifecycleMetrics;
            isLogWorkspaceMetricsActive = enabled && config.LogWorkspaceMetrics;
        }

        #endregion

        #region 3. SNAPSHOT TABLE EVENTS

        /// <summary>
        /// Logs the initialization of the snapshot table with its capacity, dynamic scaling status, minimum and maximum capacities,
        /// and probe depth. Automatically routes into the active ActivityJournal during engine bootstrap.
        /// </summary>
        public static void SnapshotTableInitialized(int capacity, bool dynamic, int min, int max, int probeDepth) =>
            OHLog.Event("OverHaulers_Event_Snapshot_Initialized",
                capacity, dynamic ? "Dynamic" : "Static", min, max, probeDepth);

        /// <summary>Logs a snapshot table capacity change with its previous occupancy and load, routing it into an active journal.</summary>
        public static void SnapshotTableCapacityChanged(int oldCapacity, int newCapacity, int occupancy, float loadPercent) =>
            OHLog.Event("OverHaulers_Event_Snapshot_ScaleEvent",
                oldCapacity, newCapacity, occupancy, loadPercent.ToString("F1"));

        /// <summary>
        /// Logs a snapshot table clear, including the number of removed entries and the restored capacity.
        /// Suppressed when an ActivityJournal session is active to prevent duplicate logging during world load.
        /// </summary>
        public static void SnapshotTableCleared(int clearedCount, int restoredCapacity) =>
            OHLog.InfoUnlessJournaled("OverHaulers_Event_Snapshot_Cleared", clearedCount, restoredCapacity);

        #endregion

        #region 4. TRACE RECORDING

        /// <summary>
        /// Records the eviction of a pawn from the snapshot table, storing its unique ID for later reporting.
        /// Thread-safe: checks active flag atomically without touching Settings.
        /// </summary>
        /// <param name="thingID">The unique ID of the evicted pawn.</param>
        public static void RecordPawnEvicted(int thingID)
        {
            if (!isLogPawnEvictionsActive) return;

            lock (bufferLock)
            {
                pendingEvictedPawnIds.Add(thingID);
            }
        }

        /// <summary>
        /// Records an instance where a pawn's mass was clamped to the safety floor.
        /// Thread-safe: safe to invoke from background pathfinding threads.
        /// </summary>
        /// <param name="pawnName">The name of the pawn whose mass was clamped.</param>
        /// <param name="rawMass">The original mass of the pawn before clamping.</param>
        /// <param name="clampedMass">The mass of the pawn after being clamped to the safety floor.</param>
        public static void RecordHardSafetyFloorClamped(string pawnName, float rawMass, float clampedMass)
        {
            if (!isLogSafetyFloorClampsActive) return;

            lock (bufferLock)
            {
                pendingClampedPawns[pawnName] = new SafetyClampRecord
                {
                    RawMass = rawMass,
                    ClampedMass = clampedMass
                };
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

        #endregion

        #region 5. CUMULATIVE REPORT

        /// <summary>
        /// Compiles the localized cumulative performance report and dispatches it through the shared log sink.
        /// Synchronized via bufferLock to ensure thread-safe StringBuilder assembly and trace buffer clearing.
        /// </summary>
        /// <param name="s">The telemetry snapshot containing all aggregated performance metrics for the reporting interval.</param>
        public static void DispatchCumulativeReport(in TelemetrySnapshot s)
        {
            lock (bufferLock)
            {
                pooledReportBuilder.Clear();

                try
                {
                    OHLog.AppendReportHeader(
                        pooledReportBuilder,
                        "OverHaulers_Log_PerformanceReportHeader".Translate(s.ElapsedHours).ToString());

                    // 1. Query Metrics
                    if (isLogQueryMetricsActive)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Queries".Translate(
                            s.TotalQueries,
                            s.MainQueries,
                            s.BgQueries
                        ).ToString());
                    }

                    // 2. Cache Hit Efficiency
                    if (isLogCacheMetricsActive)
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
                    if (isLogPresentationMetricsActive && (s.PresentationHits > 0 || s.PresentationMisses > 0 || s.PresentationActiveCount > 0))
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
                    if (isLogSnapshotTableMetricsActive)
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
                    if (isLogLifecycleMetricsActive)
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
                    if (isLogWorkspaceMetricsActive)
                    {
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_PerformanceReport_Workspaces".Translate(s.ActiveWorkspaces).ToString());
                    }

                    // 7. Safety Floor Clamps
                    if (isLogSafetyFloorClampsActive && pendingClampedPawns.Count > 0)
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
                    if (isLogPawnEvictionsActive && pendingEvictedPawnIds.Count > 0)
                    {
                        string idsFormatted = string.Join(", ", pendingEvictedPawnIds);
                        pooledReportBuilder.AppendLine();
                        pooledReportBuilder.Append("OverHaulers_Log_EvictedPawnsBuffer".Translate(pendingEvictedPawnIds.Count, idsFormatted).ToString());
                    }

                    OHLog.DispatchLogMessage(pooledReportBuilder.ToString());
                }
                finally
                {
                    pooledReportBuilder.Clear();
                    pendingEvictedPawnIds.Clear();
                    pendingClampedPawns.Clear();
                }
            }
        }

        #endregion
    }
}

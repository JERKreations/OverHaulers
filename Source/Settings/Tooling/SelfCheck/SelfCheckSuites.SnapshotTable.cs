using System;
using System.Collections.Generic;
using System.Threading;
using Verse;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. SNAPSHOT TABLE FIXTURE

        // Synthetic fixture capacity keeps deliberate collision tests cheap; live sizing tests use SnapshotTable defaults.
        private const int TestCapacity = 256;

        #endregion

        #region 2. SNAPSHOT TABLE CHECKS

        /// <summary>Finds pawn IDs whose natural hash slot equals <paramref name="slot"/>, forcing deliberate collisions.</summary>
        private static List<int> IdsForSlot(SnapshotTable table, int slot, int count)
        {
            List<int> ids = new List<int>(count);
            for (int id = 1; ids.Count < count && id < 50000000; id++)
            {
                if (table.NaturalSlotOf(id) == slot) ids.Add(id);
            }

            Expect.True(ids.Count == count, "could not construct " + count + " colliding IDs for slot " + slot);
            return ids;
        }

        /// <summary>
        /// Verifies that writing and reading various floating-point offsets (including extremes and negative values) returns exact bit-level values.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckPackUnpack(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity, forwardTelemetry: false, enableDynamicScaling: false);
            float[] samples = { 0f, -12.5f, 123456.78f, -0.0001f, 0.001f, 9999.99f };

            for (int i = 0; i < samples.Length; i++)
            {
                int id = 500 + i;
                table.WriteState(id, samples[i]);
                Expect.Equal(samples[i], table.GetOffsetThreadSafe(id), "stored float offset for id " + id);
            }
        }

        /// <summary>
        /// Verifies that unknown and non-positive pawn IDs read as a neutral offset and are never stored.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckNeutralDefaults(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity, enableDynamicScaling: false);

            Expect.Equal(0f, table.GetOffsetThreadSafe(42), "offset of unknown pawn");

            table.WriteState(0, 5f);
            table.WriteState(-7, 5f);
            Expect.Equal(0, table.OccupiedSlotsCount, "occupancy after writing non-positive IDs");
            Expect.Equal(0f, table.GetOffsetThreadSafe(-7), "offset of negative ID");
            Expect.True(!table.Evict(-7), "evicting a negative ID must report false");
        }

        /// <summary>
        /// Verifies that a written value reads back and overwriting replaces it without growing occupancy.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckWriteOverwriteRead(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity, enableDynamicScaling: false);

            table.WriteState(10, 7.5f);
            Expect.Equal(7.5f, table.GetOffsetThreadSafe(10), "offset after write");
            Expect.Equal(1, table.OccupiedSlotsCount, "occupancy after first write");

            table.WriteState(10, -3f);
            Expect.Equal(-3f, table.GetOffsetThreadSafe(10), "offset after overwrite");
            Expect.Equal(1, table.OccupiedSlotsCount, "overwrite must not grow occupancy");

            table.WriteState(11, 4f);
            Expect.Equal(4f, table.GetOffsetThreadSafe(11), "offset for distinct key");
            Expect.Equal(2, table.OccupiedSlotsCount, "occupancy after second distinct write");
        }

        /// <summary>
        /// Verifies that eviction removes an entry, reports missing keys correctly, and that a reset empties the table.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckEvictAndReset(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity, enableDynamicScaling: false);

            table.WriteState(20, 1f);
            Expect.True(table.Evict(20), "evicting a present key must report true");
            Expect.Equal(0f, table.GetOffsetThreadSafe(20), "offset after eviction");
            Expect.Equal(0, table.OccupiedSlotsCount, "occupancy after eviction");
            Expect.True(!table.Evict(20), "evicting a missing key must report false");

            for (int id = 1; id <= 50; id++) table.WriteState(id, id);
            table.Reset();
            Expect.Equal(0, table.OccupiedSlotsCount, "occupancy after reset");
            Expect.Equal(0f, table.GetOffsetThreadSafe(25), "offset after reset");
        }

        /// <summary>
        /// Builds a cluster of colliding keys and verifies every other key stays reachable after evicting each member in turn (backward-shift deletion).
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckClusterEviction(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity, enableDynamicScaling: false);
            List<int> ids = IdsForSlot(table, 100, 8);

            // Evict each position within the cluster in turn and verify every other key is still reachable.
            for (int victim = 0; victim < ids.Count; victim++)
            {
                table.Reset();
                for (int i = 0; i < ids.Count; i++) table.WriteState(ids[i], ids[i]);

                Expect.True(table.Evict(ids[victim]), "evicting cluster member " + victim);

                for (int i = 0; i < ids.Count; i++)
                {
                    float expected = i == victim ? 0f : ids[i];
                    Expect.Equal(expected, table.GetOffsetThreadSafe(ids[i]), "member " + i + " after evicting member " + victim);
                }

                Expect.Equal(ids.Count - 1, table.OccupiedSlotsCount, "occupancy after evicting member " + victim);
            }
        }

        /// <summary>
        /// Replays thousands of random inserts and evictions across interleaving collision clusters and compares every read with a reference dictionary.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckRandomAgainstModel(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity, enableDynamicScaling: false);

            // Two natural slots three apart so their probe chains interleave, plus an isolated third cluster.
            List<int> universe = new List<int>();
            universe.AddRange(IdsForSlot(table, 100, 7));
            universe.AddRange(IdsForSlot(table, 103, 7));
            universe.AddRange(IdsForSlot(table, 200, 7));

            Dictionary<int, float> model = new Dictionary<int, float>();
            Random rng = new Random(12345);

            for (int step = 0; step < 4000; step++)
            {
                int id = universe[rng.Next(universe.Count)];
                if (rng.Next(3) == 0)
                {
                    bool expected = model.Remove(id);
                    Expect.Equal(expected, table.Evict(id), "step " + step + ": evict result for " + id);
                }
                else
                {
                    float value = 1 + rng.Next(1000);
                    model[id] = value;
                    table.WriteState(id, value);
                }

                for (int p = 0; p < universe.Count; p++)
                {
                    int probe = universe[p];
                    bool present = model.TryGetValue(probe, out float stored);
                    Expect.Equal(present ? stored : 0f, table.GetOffsetThreadSafe(probe), "step " + step + ": offset of " + probe);
                }

                Expect.Equal(model.Count, table.OccupiedSlotsCount, "step " + step + ": occupancy");
            }
        }

        /// <summary>
        /// Verifies that once the probe window is saturated each overflow displaces exactly one entry, the newest key stays readable and occupancy stays capped.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckSaturation(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity, enableDynamicScaling: false);
            int window = SnapshotTable.MaxProbeSteps;
            int overflow = 4;
            List<int> ids = IdsForSlot(table, 50, window + overflow);

            for (int i = 0; i < ids.Count; i++) table.WriteState(ids[i], ids[i]);

            Expect.Equal((float)ids[ids.Count - 1], table.GetOffsetThreadSafe(ids[ids.Count - 1]), "newest key must always be readable");
            Expect.Equal(window, table.OccupiedSlotsCount, "occupancy must stay capped at the probe window");
            Expect.Equal((long)overflow, table.DisplacedWrites, "displacement count");

            int readable = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (table.GetOffsetThreadSafe(ids[i]) == ids[i]) readable++;
            }
            Expect.Equal(window, readable, "number of readable keys");
        }

        /// <summary>
        /// Runs background readers against a flat-out writer and verifies no read ever returns a torn or foreign value.
        /// Bounded by wall-clock time so it cannot stall a frame for long.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckConcurrentReaders(SelfCheckContext ctx)
        {
            // Dynamic scaling stays enabled to exercise buffer publication while readers are active.
            // Offsets derive from the pawn ID, so a torn read or key/payload mismatch shows up as a
            // value that is neither "miss" nor the ID itself.
            SnapshotTable table = new SnapshotTable(TestCapacity);
            List<int> ids = IdsForSlot(table, 70, 6);
            ids.AddRange(IdsForSlot(table, 71, 6));

            int violations = 0;
            long reads = 0;
            bool stop = false;
            List<Thread> readers = new List<Thread>(3);

            for (int r = 0; r < 3; r++)
            {
                Thread thread = new Thread(() =>
                {
                    while (!Volatile.Read(ref stop))
                    {
                        for (int i = 0; i < ids.Count; i++)
                        {
                            int id = ids[i];
                            float offset = table.GetOffsetThreadSafe(id);
                            if (offset != 0f && offset != id)
                            {
                                Interlocked.Increment(ref violations);
                            }
                            Interlocked.Increment(ref reads);
                        }
                    }
                });
                thread.IsBackground = true;
                readers.Add(thread);
                thread.Start();
            }

            // Time-bounded (not cycle-bounded) so the readers get enough runtime to genuinely overlap the writer.
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            Random rng = new Random(99);
            int cycles = 0;
            while (watch.ElapsedMilliseconds < 150)
            {
                int id = ids[rng.Next(ids.Count)];
                if (rng.Next(3) == 0) table.Evict(id);
                else table.WriteState(id, id);
                cycles++;
            }

            Volatile.Write(ref stop, true);
            for (int r = 0; r < readers.Count; r++)
            {
                Expect.True(readers[r].Join(5000), "a reader thread failed to stop");
            }

            Expect.True(Interlocked.Read(ref reads) > 0, "readers performed no reads");
            Expect.Equal(0, Volatile.Read(ref violations), "torn or foreign values observed (" + Interlocked.Read(ref reads) + " reads, " + cycles + " writer cycles)");
            ctx.Note("OverHaulers_SelfCheck_Note_ConcurrentReaders".Translate(cycles, Interlocked.Read(ref reads).ToString()).ToString());
        }

        /// <summary>
        /// Verifies scaling thresholds using both an isolated synthetic table and the live table's configured capacities.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckDynamicScaling(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(
                TestCapacity,
                forwardTelemetry: false,
                enableDynamicScaling: true,
                minimumCapacity: TestCapacity
            );
            Expect.Equal(TestCapacity, table.Capacity, "synthetic table initial capacity");

            int growthOccupancy = (int)(TestCapacity * SnapshotTable.HighWatermarkRatio);
            for (int i = 1; i < growthOccupancy; i++)
            {
                table.WriteState(i, (float)i);
            }

            Expect.Equal(TestCapacity, table.Capacity, "synthetic table must not expand before the high watermark");
            table.WriteState(growthOccupancy, (float)growthOccupancy);
            Expect.Equal(TestCapacity << 1, table.Capacity, "synthetic table must expand at the configured high watermark");
            Expect.Equal(growthOccupancy, table.OccupiedSlotsCount, "occupancy must remain accurate after expansion");

            // Verify rehash integrity
            for (int i = 1; i <= growthOccupancy; i++)
            {
                Expect.Equal((float)i, table.GetOffsetThreadSafe(i), "offset of key " + i + " after expansion");
            }

            int contractionOccupancy = (int)((TestCapacity << 1) * SnapshotTable.LowWatermarkRatio);
            for (int i = contractionOccupancy + 2; i <= growthOccupancy; i++)
            {
                Expect.True(table.Evict(i), "eviction of key " + i);
            }

            Expect.Equal(TestCapacity << 1, table.Capacity, "synthetic table must not contract above the low watermark");
            Expect.True(table.Evict(contractionOccupancy + 1), "eviction at the low watermark");
            Expect.Equal(TestCapacity, table.Capacity, "synthetic table must contract at the configured low watermark");
            Expect.Equal(contractionOccupancy, table.OccupiedSlotsCount, "occupancy after contraction");

            // Verify remaining keys
            for (int i = 1; i <= contractionOccupancy; i++)
            {
                Expect.Equal((float)i, table.GetOffsetThreadSafe(i), "offset of key " + i + " after contraction");
            }

            table.Reset();
            Expect.Equal(TestCapacity, table.Capacity, "synthetic table reset must return to its configured minimum");
            Expect.Equal(0, table.OccupiedSlotsCount, "occupancy must reset to zero");

            SnapshotTable liveSizingTable = new SnapshotTable(
                SnapshotTable.DefaultCapacity,
                forwardTelemetry: false,
                enableDynamicScaling: true,
                minimumCapacity: SnapshotTable.MinCapacity
            );
            Expect.Equal(SnapshotTable.DefaultCapacity, liveSizingTable.Capacity, "live table must start at default capacity");

            int liveTargetCapacity = Math.Min(SnapshotTable.DefaultCapacity * 4, SnapshotTable.MaxCapacity);
            int liveInserted = 0;
            while (liveSizingTable.Capacity < liveTargetCapacity && liveInserted < SnapshotTable.MaxCapacity)
            {
                liveInserted++;
                liveSizingTable.WriteState(liveInserted, (float)liveInserted);
            }

            Expect.Equal(liveTargetCapacity, liveSizingTable.Capacity, "live table must grow to the configured test target");
            liveSizingTable.Reset();
            Expect.Equal(SnapshotTable.MinCapacity, liveSizingTable.Capacity, "live table reset must return to the configured minimum");

            int liveLargePopulation = 0;
            int firstLiveId = 1001;
            while (liveSizingTable.Capacity < liveTargetCapacity && liveLargePopulation < SnapshotTable.MaxCapacity)
            {
                int id = firstLiveId + liveLargePopulation;
                liveSizingTable.WriteState(id, (float)id);
                liveLargePopulation++;
            }

            Expect.Equal(liveTargetCapacity, liveSizingTable.Capacity, "live table must grow to the configured test target after reset");
            int retainedPopulation = (int)(SnapshotTable.MinCapacity * SnapshotTable.LowWatermarkRatio);
            liveSizingTable.BeginEvictionBatch();
            try
            {
                int evictedPopulation = liveLargePopulation - retainedPopulation;
                for (int i = 0; i < evictedPopulation; i++)
                {
                    Expect.True(liveSizingTable.Evict(firstLiveId + i), "live table eviction of key " + (firstLiveId + i));
                }

                Expect.Equal(liveTargetCapacity, liveSizingTable.Capacity, "live table contraction must be deferred during an eviction batch");
                Expect.Equal(retainedPopulation, liveSizingTable.OccupiedSlotsCount, "occupancy during batched evictions");
            }
            finally
            {
                liveSizingTable.EndEvictionBatch();
            }

            Expect.Equal(SnapshotTable.MinCapacity, liveSizingTable.Capacity, "batched evictions must contract directly to the configured minimum");
            Expect.Equal(retainedPopulation, liveSizingTable.OccupiedSlotsCount, "occupancy after live table contraction");
            for (int i = liveLargePopulation - retainedPopulation; i < liveLargePopulation; i++)
            {
                int id = firstLiveId + i;
                Expect.Equal((float)id, liveSizingTable.GetOffsetThreadSafe(id), "retained key after batched contraction " + id);
            }

            CheckEvictionBatchEdges();
            CheckCollisionContraction();
        }

        /// <summary>Exercises empty, nested, and repopulated batches against an isolated table.</summary>
        private static void CheckEvictionBatchEdges()
        {
            SnapshotTable table = new SnapshotTable(TestCapacity, minimumCapacity: TestCapacity >> 2);
            table.BeginEvictionBatch();
            table.EndEvictionBatch();
            Expect.Equal(TestCapacity, table.Capacity, "empty eviction batch must not contract");

            int lowWatermark = (int)(TestCapacity * SnapshotTable.LowWatermarkRatio);
            for (int id = 1; id <= lowWatermark + 1; id++)
            {
                table.WriteState(id, id);
            }

            table.BeginEvictionBatch();
            try
            {
                table.BeginEvictionBatch();
                try
                {
                    Expect.True(table.Evict(1), "nested batch eviction");
                }
                finally
                {
                    table.EndEvictionBatch();
                }

                Expect.Equal(TestCapacity, table.Capacity, "inner batch must not contract");
                table.WriteState(lowWatermark + 2, lowWatermark + 2);
            }
            finally
            {
                table.EndEvictionBatch();
            }

            Expect.Equal(TestCapacity, table.Capacity, "repopulation above the low watermark must cancel contraction");
            Expect.Equal(lowWatermark + 1, table.OccupiedSlotsCount, "occupancy after repopulated batch");
            for (int id = 2; id <= lowWatermark + 2; id++)
            {
                Expect.Equal((float)id, table.GetOffsetThreadSafe(id), "retained key after repopulated batch " + id);
            }

            table.BeginEvictionBatch();
            try
            {
                table.BeginEvictionBatch();
                try
                {
                    Expect.True(table.Evict(2), "nested batch eviction with pending contraction");
                }
                finally
                {
                    table.EndEvictionBatch();
                }
                Expect.Equal(TestCapacity, table.Capacity, "pending contraction must wait for the outermost batch");
            }
            finally
            {
                table.EndEvictionBatch();
            }
            Expect.Equal(TestCapacity >> 1, table.Capacity, "outermost batch must apply the pending contraction");
            for (int id = 3; id <= lowWatermark + 2; id++)
            {
                Expect.Equal((float)id, table.GetOffsetThreadSafe(id), "retained key after nested batch " + id);
            }

            bool rejectedUnmatchedEnd = false;
            try
            {
                table.EndEvictionBatch();
            }
            catch (InvalidOperationException)
            {
                rejectedUnmatchedEnd = true;
            }
            Expect.True(rejectedUnmatchedEnd, "ending an unmatched eviction batch must fail");

            table.BeginEvictionBatch();
            try
            {
                for (int id = 3; id <= lowWatermark - (lowWatermark >> 1) + 2; id++)
                {
                    Expect.True(table.Evict(id), "eviction before resetting an active batch");
                }
                table.Reset();
            }
            finally
            {
                table.EndEvictionBatch();
            }
            Expect.Equal(TestCapacity >> 2, table.Capacity, "reset during a batch must retain the configured minimum");
        }

        /// <summary>Verifies collision-heavy contractions retain every entry and fall back to a safe capacity.</summary>
        private static void CheckCollisionContraction()
        {
            int minimum = TestCapacity >> 2;
            SnapshotTable table = new SnapshotTable(TestCapacity, minimumCapacity: minimum);
            SnapshotTable minimumHash = new SnapshotTable(minimum, enableDynamicScaling: false, minimumCapacity: minimum);
            List<int> ids = IdsForSlot(minimumHash, 0, SnapshotTable.MaxProbeSteps + 1);
            int extraId = IdsForSlot(table, minimum >> 1, 1)[0];

            for (int i = 0; i < ids.Count; i++) table.WriteState(ids[i], ids[i]);
            table.WriteState(extraId, extraId);
            Expect.Equal(ids.Count + 1, table.OccupiedSlotsCount, "collision fixture occupancy before contraction");
            Expect.True(table.Evict(extraId), "collision fixture trigger eviction");
            Expect.Equal(TestCapacity >> 1, table.Capacity, "contraction must use a larger candidate when the minimum probe window is saturated");
            Expect.Equal(ids.Count, table.OccupiedSlotsCount, "all entries must survive collision fallback");
            for (int i = 0; i < ids.Count; i++)
            {
                Expect.Equal((float)ids[i], table.GetOffsetThreadSafe(ids[i]), "collision fallback key " + ids[i]);
            }

            table = new SnapshotTable(TestCapacity, minimumCapacity: minimum);
            SnapshotTable halfHash = new SnapshotTable(TestCapacity >> 1, enableDynamicScaling: false, minimumCapacity: minimum);
            ids = IdsForSlot(halfHash, 0, SnapshotTable.MaxProbeSteps + 1);
            extraId = IdsForSlot(table, minimum >> 1, 1)[0];
            for (int i = 0; i < ids.Count; i++) table.WriteState(ids[i], ids[i]);
            table.WriteState(extraId, extraId);
            Expect.True(table.Evict(extraId), "collision fixture trigger for retaining the original buffer");
            Expect.Equal(TestCapacity, table.Capacity, "contraction must retain the original buffer when no smaller candidate fits");
            Expect.Equal(ids.Count, table.OccupiedSlotsCount, "all entries must survive rejected contraction");
            for (int i = 0; i < ids.Count; i++)
            {
                Expect.Equal((float)ids[i], table.GetOffsetThreadSafe(ids[i]), "key after rejected contraction " + ids[i]);
            }

            Expect.True(table.Evict(ids[0]), "evicting a collision must allow a later contraction");
            Expect.Equal(minimum, table.Capacity, "table must contract after the collision pressure is relieved");
            for (int i = 1; i < ids.Count; i++)
            {
                Expect.Equal((float)ids[i], table.GetOffsetThreadSafe(ids[i]), "key after collision pressure is relieved " + ids[i]);
            }
        }

        #endregion
    }
}

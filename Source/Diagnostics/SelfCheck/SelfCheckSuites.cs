using System;
using System.Collections.Generic;
using System.Threading;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [DIAG-04] The self-check catalogue. Add new checks to <see cref="RegisterAll"/>; a check passes by returning,
    /// fails by throwing (use <see cref="Expect"/>), and can opt out via <see cref="SelfCheckContext.Skip"/>.
    /// Resides under Source/Diagnostics/SelfCheck/.
    /// </summary>
    internal static class SelfCheckSuites
    {
        // Keyed-translation identifiers; the UI translates them, exported reports print them verbatim.
        private const string GroupTable = "OverHaulers_SelfCheck_Group_SnapshotTable";
        private const string GroupCollections = "OverHaulers_SelfCheck_Group_Collections";
        private const string GroupTopology = "OverHaulers_SelfCheck_Group_Topology";
        private const string GroupSolver = "OverHaulers_SelfCheck_Group_Solver";

        // Small power-of-two table so deliberate collisions are cheap to construct.
        private const int TestCapacity = 256;

        /// <summary>
        /// Registers every check in execution order; each runs on its own frame.
        /// </summary>
        /// <param name="list">The queue the checks are appended to.</param>
        public static void RegisterAll(List<SelfCheckDef> list)
        {
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_PackUnpack", CheckPackUnpack));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_NeutralDefaults", CheckNeutralDefaults));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_WriteOverwriteRead", CheckWriteOverwriteRead));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_EvictAndReset", CheckEvictAndReset));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_ClusterEviction", CheckClusterEviction));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_RandomAgainstModel", CheckRandomAgainstModel));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_Saturation", CheckSaturation));
            list.Add(new SelfCheckDef(GroupTable, "OverHaulers_SelfCheck_Check_ConcurrentReaders", CheckConcurrentReaders));
            list.Add(new SelfCheckDef(GroupCollections, "OverHaulers_SelfCheck_Check_UniqueList", CheckUniqueList));
            list.Add(new SelfCheckDef(GroupTopology, "OverHaulers_SelfCheck_Check_TopologyIntegrity", CheckTopologyIntegrity));
            list.Add(new SelfCheckDef(GroupSolver, "OverHaulers_SelfCheck_Check_SolverDeterminism", CheckSolverDeterminism));
            list.Add(new SelfCheckDef(GroupSolver, "OverHaulers_SelfCheck_Check_WoundsNeverIncrease", CheckWoundsNeverIncrease));
        }

        #region 1. SNAPSHOT TABLE CHECKS

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
        /// Verifies that packing two floats into one 64-bit word and unpacking them returns the original values, including extremes.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckPackUnpack(SelfCheckContext ctx)
        {
            float[][] samples =
            {
                new[] { 0f, 1f },
                new[] { -12.5f, 0.75f },
                new[] { 123456.78f, 0.0001f },
                new[] { float.MaxValue, float.MinValue }
            };

            for (int i = 0; i < samples.Length; i++)
            {
                SnapshotTable.Unpack(SnapshotTable.Pack(samples[i][0], samples[i][1]), out float offset, out float multiplier);
                Expect.Equal(samples[i][0], offset, "offset of sample " + i);
                Expect.Equal(samples[i][1], multiplier, "multiplier of sample " + i);
            }
        }

        /// <summary>
        /// Verifies that unknown and non-positive pawn IDs read as neutral defaults (offset 0, multiplier 1) and are never stored.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckNeutralDefaults(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity);

            Expect.Equal(0f, table.GetOffsetThreadSafe(42), "offset of unknown pawn");
            Expect.Equal(1f, table.GetMultiplierThreadSafe(42), "multiplier of unknown pawn");

            table.WriteState(0, 5f, 2f);
            table.WriteState(-7, 5f, 2f);
            Expect.Equal(0, table.OccupiedSlotsCount, "occupancy after writing non-positive IDs");
            Expect.Equal(0f, table.GetOffsetThreadSafe(-7), "offset of negative ID");
            Expect.Equal(1f, table.GetMultiplierThreadSafe(-7), "multiplier of negative ID");
            Expect.True(!table.Evict(-7), "evicting a negative ID must report false");
        }

        /// <summary>
        /// Verifies that a written value reads back, that overwriting replaces it without growing occupancy, and that a non-positive multiplier reads as neutral.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckWriteOverwriteRead(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity);

            table.WriteState(10, 7.5f, 1.25f);
            Expect.Equal(7.5f, table.GetOffsetThreadSafe(10), "offset after write");
            Expect.Equal(1.25f, table.GetMultiplierThreadSafe(10), "multiplier after write");
            Expect.Equal(1, table.OccupiedSlotsCount, "occupancy after first write");

            table.WriteState(10, -3f, 0.5f);
            Expect.Equal(-3f, table.GetOffsetThreadSafe(10), "offset after overwrite");
            Expect.Equal(0.5f, table.GetMultiplierThreadSafe(10), "multiplier after overwrite");
            Expect.Equal(1, table.OccupiedSlotsCount, "overwrite must not grow occupancy");

            table.WriteState(11, 4f, 0f);
            Expect.Equal(1f, table.GetMultiplierThreadSafe(11), "non-positive stored multiplier must read as neutral");
        }

        /// <summary>
        /// Verifies that eviction removes an entry, reports missing keys correctly, and that a reset empties the table.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckEvictAndReset(SelfCheckContext ctx)
        {
            SnapshotTable table = new SnapshotTable(TestCapacity);

            table.WriteState(20, 1f, 1f);
            Expect.True(table.Evict(20), "evicting a present key must report true");
            Expect.Equal(0f, table.GetOffsetThreadSafe(20), "offset after eviction");
            Expect.Equal(0, table.OccupiedSlotsCount, "occupancy after eviction");
            Expect.True(!table.Evict(20), "evicting a missing key must report false");

            for (int id = 1; id <= 50; id++) table.WriteState(id, id, 1f);
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
            SnapshotTable table = new SnapshotTable(TestCapacity);
            List<int> ids = IdsForSlot(table, 100, 8);

            // Evict each position within the cluster in turn and verify every other key is still reachable.
            for (int victim = 0; victim < ids.Count; victim++)
            {
                table.Reset();
                for (int i = 0; i < ids.Count; i++) table.WriteState(ids[i], ids[i], 2f);

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
            SnapshotTable table = new SnapshotTable(TestCapacity);

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
                    table.WriteState(id, value, value + 0.5f);
                }

                for (int p = 0; p < universe.Count; p++)
                {
                    int probe = universe[p];
                    bool present = model.TryGetValue(probe, out float stored);
                    Expect.Equal(present ? stored : 0f, table.GetOffsetThreadSafe(probe), "step " + step + ": offset of " + probe);
                    Expect.Equal(present ? stored + 0.5f : 1f, table.GetMultiplierThreadSafe(probe), "step " + step + ": multiplier of " + probe);
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
            SnapshotTable table = new SnapshotTable(TestCapacity);
            int window = SnapshotTable.MaxProbeSteps;
            int overflow = 4;
            List<int> ids = IdsForSlot(table, 50, window + overflow);

            for (int i = 0; i < ids.Count; i++) table.WriteState(ids[i], ids[i], 1f);

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
            // Offsets and multipliers derive from the pawn ID, so a torn read or key/payload mismatch shows up as a
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
                            float multiplier = table.GetMultiplierThreadSafe(id);
                            if ((offset != 0f && offset != id) || (multiplier != 1f && multiplier != id))
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
                else table.WriteState(id, id, id);
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

        #endregion

        #region 2. COLLECTION CHECKS

        /// <summary>
        /// Verifies the unique list rejects nulls and duplicates, preserves insertion order, merges, clears and enumerates correctly.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckUniqueList(SelfCheckContext ctx)
        {
            UniqueList<string> list = new UniqueList<string>();
            Expect.True(list.Add("b"), "first add");
            Expect.True(list.Add("a"), "second add");
            Expect.True(!list.Add("b"), "duplicate add must be rejected");
            Expect.True(!list.Add(null), "null add must be rejected");
            Expect.Equal(2, list.Count, "count after adds");
            Expect.Equal("b", list[0], "insertion order [0]");
            Expect.Equal("a", list[1], "insertion order [1]");

            UniqueList<int> target = new UniqueList<int>();
            target.Add(1);
            target.Add(2);
            UniqueList<int> source = new UniqueList<int>();
            source.Add(2);
            source.Add(3);
            target.AddRange(source);
            target.AddRange(null);
            Expect.Equal(3, target.Count, "count after AddRange");

            List<int> seen = new List<int>();
            foreach (int value in target) seen.Add(value);
            Expect.True(seen.Count == 3 && seen[0] == 1 && seen[1] == 2 && seen[2] == 3, "enumeration order");

            target.Clear();
            Expect.Equal(0, target.Count, "count after clear");
            Expect.True(target.Add(2), "re-adding after clear");
        }

        #endregion

        #region 3. TOPOLOGY CHECKS

        /// <summary>
        /// Verifies that every compiled body template matches its body definition and has valid parent indices and static weight factors.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckTopologyIntegrity(SelfCheckContext ctx)
        {
            List<BodyDef> bodies = DefDatabase<BodyDef>.AllDefsListForReading;
            if (bodies == null || bodies.Count == 0)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoBodies".Translate().ToString());
                return;
            }

            TopologyLayoutCompiler.EnsureInitialized();

            int checkedCount = 0;
            int uncompiled = 0;

            for (int b = 0; b < bodies.Count; b++)
            {
                BodyDef body = bodies[b];
                SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(body);
                if (template == null)
                {
                    uncompiled++;
                    continue;
                }

                string who = body.defName;
                int count = template.PartCount;

                Expect.Equal(body.AllParts.Count, count, who + ": part count");
                Expect.True(template.IndexedParts.Length >= count, who + ": IndexedParts shorter than part count");
                Expect.True(template.PartTypes.Length >= count, who + ": PartTypes shorter than part count");
                Expect.True(template.ParentIndices.Length >= count, who + ": ParentIndices shorter than part count");
                Expect.True(template.StaticWeightFactors.Length >= count, who + ": StaticWeightFactors shorter than part count");

                for (int i = 0; i < count; i++)
                {
                    int parent = template.ParentIndices[i];
                    Expect.True(parent >= -1 && parent < count && parent != i, who + ": part " + i + " has invalid parent index " + parent);

                    float weight = template.StaticWeightFactors[i];
                    Expect.True(!float.IsNaN(weight) && !float.IsInfinity(weight) && weight >= 0f, who + ": part " + i + " has invalid static weight factor " + weight);
                }

                checkedCount++;
            }

            ctx.Note(uncompiled > 0
                ? "OverHaulers_SelfCheck_Note_TopologyPartial".Translate(checkedCount, uncompiled).ToString()
                : "OverHaulers_SelfCheck_Note_Topology".Translate(checkedCount).ToString());
        }

        #endregion

        #region 4. SOLVER CHECKS

        /// <summary>
        /// Builds a sandbox harness bound to a subject, optionally with the representative wound set applied.
        /// </summary>
        /// <param name="subject">The test subject to mirror.</param>
        /// <param name="wounded">True to apply the representative wounds.</param>
        /// <returns>A valid, bound harness; the caller must dispose it.</returns>
        private static SandboxPawnHarness CreateHarness(TestSubjectEntry subject, bool wounded)
        {
            SandboxPawnHarness harness = SandboxPawnHarness.CreateForCurrentProgramState();

            harness.BindSubject(subject);
            if (!harness.IsValid)
            {
                harness.Dispose();
                throw new SelfCheckFailedException("could not build a sandbox pawn for " + subject.Label);
            }

            if (wounded)
            {
                harness.SimulateRepresentativeWounds();
            }

            return harness;
        }

        /// <summary>
        /// Solves a sandbox pawn once, releases the workspace and verifies the result is finite.
        /// </summary>
        /// <param name="pawn">The sandbox pawn to solve.</param>
        /// <param name="baseline">The species baseline mass capacity in kg.</param>
        /// <returns>The solved caravan mass capacity offset in kg.</returns>
        private static float SolveOnce(Pawn pawn, float baseline)
        {
            float offset = MassCapacitySolver.SolveMassCapacityOffset(
                pawn, baseline, out float _, OverHaulers.settings, false, out AnatomicalWorkspace workspace);

            Expect.True(workspace != null, "the solver reported an internal failure (no workspace returned)");
            WorkspacePool.ReleaseWorkspace(workspace);
            Expect.True(!float.IsNaN(offset) && !float.IsInfinity(offset), "the solver produced a non-finite offset");
            return offset;
        }

        /// <summary>
        /// Verifies that solving the same unchanged pawn repeatedly returns identical results.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckSolverDeterminism(SelfCheckContext ctx)
        {
            TestSubjectEntry subject = TestSubjectRegistry.GetDefaultSubject();
            if (subject?.BodyDef == null)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoSubject".Translate().ToString());
                return;
            }

            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: false))
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
                float first = SolveOnce(harness.SandboxPawn, baseline);

                for (int i = 0; i < 3; i++)
                {
                    Expect.Equal(first, SolveOnce(harness.SandboxPawn, baseline), "solve " + (i + 2) + " vs solve 1");
                }

                ctx.Note("OverHaulers_SelfCheck_Note_SolverDeterminism".Translate(subject.Label, baseline.ToStringMass(), first.ToStringMassOffset()).ToString());
            }
        }

        /// <summary>
        /// Verifies that applying the representative wounds never increases the solved caravan mass capacity offset.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckWoundsNeverIncrease(SelfCheckContext ctx)
        {
            TestSubjectEntry subject = TestSubjectRegistry.GetDefaultSubject();
            if (subject?.BodyDef == null)
            {
                ctx.Skip("OverHaulers_SelfCheck_Skip_NoSubject".Translate().ToString());
                return;
            }

            float pristine;
            float wounded;

            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: false))
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
                pristine = SolveOnce(harness.SandboxPawn, baseline);
            }

            using (SandboxPawnHarness harness = CreateHarness(subject, wounded: true))
            {
                float baseline = SpeciesBaselineCalibration.ResolveSpeciesBaseline(harness.SandboxPawn);
                wounded = SolveOnce(harness.SandboxPawn, baseline);
            }

            Expect.True(wounded <= pristine + SettingsDefaults.EfficiencyEpsilon, "wounded offset " + wounded.ToString("F3") + " kg exceeds pristine offset " + pristine.ToString("F3") + " kg");
            ctx.Note("OverHaulers_SelfCheck_Note_WoundsNeverIncrease".Translate(subject.Label, pristine.ToStringMassOffset(), wounded.ToStringMassOffset()).ToString());
        }

        #endregion
    }
}

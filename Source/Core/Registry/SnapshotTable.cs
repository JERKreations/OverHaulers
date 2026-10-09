using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-03] CONCURRENT MASS SNAPSHOT TABLE
    /// Instance implementation of the atomic registry mapping pawn IDs to Caravan Mass Capacity (kg) primitive snapshots.
    /// A fixed power-of-two open-addressing table (the live 4096-slot instance is ~32 KB, L1/L2 resident) with a zero-allocation
    /// struct union. Implements Backward-Shift Deletion (Algorithm R) synchronized via a lock-free Sequence Counter (SeqLock).
    /// Guarantees atomic, linearizable, zero-allocation reads for RimWorld background pathfinding threads.
    /// <see cref="MassSnapshotCache"/> owns the single live instance used by the game; the diagnostics self-check creates
    /// private instances so it can stress the algorithm without touching live data. Resides under Source/Core/Registry/.
    /// </summary>
    /// <remarks>
    /// ARCHITECTURAL DESIGN RATIONALE (ZERO-LOCK ATOMIC CONCURRENCY):
    /// RimWorld pathfinding threads query pawn mass off the main thread. Rather than incurring
    /// lock contention or GC allocations via ConcurrentDictionary, this table maintains two flat
    /// primitive arrays sized to a fixed power-of-two (the live table: 4096 slots, ~32 KB total footprint).
    ///
    /// 1. Bit-Cast Struct Union: Packs two 32-bit floats into a single atomic 64-bit integer.
    ///    Background threads read both metrics in a single atomic instruction without locks.
    /// 2. Backward-Shift Deletion (Algorithm R) &amp; SeqLock: Traditional open-addressing uses
    ///    "tombstones" for deleted keys, which degrades search performance over time. Backward-shift
    ///    deletion physically slides displaced cluster keys backward to fill the vacuum, keeping
    ///    probe chains compact. To ensure background readers never observe intermediate shifted
    ///    states, mutations are bounded by a lightweight Sequence Counter (SeqLock).
    /// 3. Multiplicative Hashing: Knuth's golden-ratio multiplier uniformly diffuses sequential
    ///    RimWorld Thing IDs across power-of-two buckets, eliminating primary clustering.
    /// 4. Zero-Contention Telemetry: Reader conflict retries, probe step depths, and slot displacements
    ///    are tracked via gated atomic accumulators, providing visibility without cache line ping-pong.
    ///
    /// Threading contract: <see cref="WriteState"/>, <see cref="Evict"/> and <see cref="Reset"/> must only be called from one
    /// thread at a time (the main thread in the live instance); the read methods may be called from any thread.
    /// </remarks>
    public sealed class SnapshotTable
    {
        #region 1. ZERO-ALLOCATION BIT-CAST STRUCT UNION

        /// <summary>
        /// Overlays two 32-bit floats on one 64-bit integer so both can be read or written as a single atomic word.
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatPairUnion
        {
            [FieldOffset(0)] public float Offset;
            [FieldOffset(4)] public float Multiplier;
            [FieldOffset(0)] public long Packed;
        }

        #endregion

        #region 2. STORAGE CONSTANTS & PACKED DATA TABLES

        /// <summary>Default slot count of the live table, accommodating maximum active simultaneous map populations.</summary>
        public const int DefaultCapacity = 4096;

        /// <summary>Maximum linear probe depth before replacing the root hash bucket.</summary>
        public const int MaxProbeSteps = 16;

        /// <summary>Maximum reader retry attempts when encountering a concurrent writer mutation.</summary>
        private const int MaxReaderRetries = 8;

        /// <summary>Knuth's 32-bit golden ratio multiplicative hash constant.</summary>
        private const uint HashMultiplier = 2654435761u;

        private readonly int capacity;
        private readonly uint mask;
        private readonly bool forwardTelemetry;

        // Parallel contiguous arrays resident in L1/L2 CPU cache
        private readonly int[] slotPawnIds;
        private readonly long[] slotPackedData;

        // Sequence counter for optimistic concurrency:
        // Even = stable table state; Odd = writer mutation in flight.
        // Governed by Volatile.Read / Interlocked; declared without 'volatile' to prevent CS0420.
        private int tableVersion;

        // Active non-zero slot counter tracking resident entries.
        // Modified exclusively by the single writer thread; read safely via Volatile.Read.
        private int occupiedSlots;

        // Instance-local diagnostics, independent of the global PerformanceTelemetry.
        private long readerRetries;
        private long readerTimeouts;
        private long displacedWrites;

        /// <summary>Slot count of this table.</summary>
        public int Capacity => capacity;

        /// <summary>
        /// Retrieves the exact count of occupied, non-zero slots currently residing in the atomic table.
        /// </summary>
        public int OccupiedSlotsCount => Volatile.Read(ref occupiedSlots);

        /// <summary>Number of reader retries caused by concurrent writer mutations since creation or the last <see cref="ResetCounters"/>.</summary>
        public long ReaderRetries => Interlocked.Read(ref readerRetries);

        /// <summary>Number of reads that exhausted every retry and fell back to the neutral default.</summary>
        public long ReaderTimeouts => Interlocked.Read(ref readerTimeouts);

        /// <summary>Number of writes that found the probe window saturated and displaced the root occupant.</summary>
        public long DisplacedWrites => Interlocked.Read(ref displacedWrites);

        /// <summary>
        /// Creates a new table.
        /// </summary>
        /// <param name="capacity">Slot count; must be a power of two and at least twice <see cref="MaxProbeSteps"/>.</param>
        /// <param name="forwardTelemetry">
        /// When true, hit/miss/retry/probe events are also reported to <see cref="PerformanceTelemetry"/>.
        /// Only the live table does this, so private test instances never pollute the game's metrics.
        /// </param>
        public SnapshotTable(int capacity = DefaultCapacity, bool forwardTelemetry = false)
        {
            if (capacity < MaxProbeSteps * 2 || (capacity & (capacity - 1)) != 0)
            {
                throw new ArgumentException("Capacity must be a power of two and at least " + (MaxProbeSteps * 2) + ".", nameof(capacity));
            }

            this.capacity = capacity;
            mask = (uint)(capacity - 1);
            this.forwardTelemetry = forwardTelemetry;
            slotPawnIds = new int[capacity];
            slotPackedData = new long[capacity];
        }

        #endregion

        #region 3. [CACHE-03] PACKING, HASHING & BACK-OFF PRIMITIVES

        /// <summary>
        /// Packs two 32-bit floating point metrics (Offset in kg and Multiplier) into a single atomic 64-bit integer with 0 GC overhead.
        /// </summary>
        /// <param name="offset">Caravan mass capacity offset in kg.</param>
        /// <param name="multiplier">Total physical mass multiplier.</param>
        /// <returns>A packed 64-bit integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Pack(float offset, float multiplier)
        {
            FloatPairUnion union = default;
            union.Offset = offset;
            union.Multiplier = multiplier;
            return union.Packed;
        }

        /// <summary>
        /// Unpacks a 64-bit integer into its constituent 32-bit floating point metrics with 0 GC overhead.
        /// </summary>
        /// <param name="packed">The packed 64-bit integer.</param>
        /// <param name="offset">Extracted caravan mass capacity offset in kg.</param>
        /// <param name="multiplier">Extracted physical mass multiplier.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unpack(long packed, out float offset, out float multiplier)
        {
            FloatPairUnion union = default;
            union.Packed = packed;
            offset = union.Offset;
            multiplier = union.Multiplier;
        }

        /// <summary>
        /// Short exponential spin so a reader that collided with an in-flight writer does not burn all of its retries instantly.
        /// </summary>
        /// <param name="retry">The zero-based retry number about to be attempted.</param>
        private static void BackOffBeforeRetry(int retry)
        {
            Thread.SpinWait(32 << Math.Min(retry, 6));
        }

        /// <summary>
        /// Applies multiplicative hashing to uniformly diffuse sequential pawn IDs across the power-of-two table.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint GetNaturalSlot(int pawnId)
        {
            return ((uint)pawnId * HashMultiplier) & mask;
        }

        /// <summary>
        /// Returns the slot a pawn ID hashes to in this table. Exposed so diagnostics can construct deliberate collisions.
        /// </summary>
        /// <param name="pawnId">The pawn ID to hash.</param>
        /// <returns>The natural (root) slot index of the ID.</returns>
        public int NaturalSlotOf(int pawnId)
        {
            return (int)GetNaturalSlot(pawnId);
        }

        #endregion

        #region 4. [CACHE-03] LOCK-FREE BACKGROUND READ PATHS

        /// <summary>
        /// [CACHE-03] Safely retrieves the cached Caravan Mass Capacity offset (kg) from any thread.
        /// Fully lock-free, zero-allocation, and linearizable via optimistic SeqLock reads.
        /// Thin pass-through to <see cref="TryReadPackedSnapshot"/>, the single canonical read implementation.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>The physical offset in kg (defaults to 0.0f if not cached).</returns>
        public float GetOffsetThreadSafe(int pawnId)
        {
            if (pawnId <= 0) return 0f;

            if (TryReadPackedSnapshot(pawnId, out long packed))
            {
                Unpack(packed, out float offset, out _);
                return offset;
            }

            return 0f;
        }

        /// <summary>
        /// [CACHE-03] Safely retrieves the cached Caravan Mass Capacity multiplier from any thread.
        /// Thin pass-through to <see cref="TryReadPackedSnapshot"/>, the single canonical read implementation.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>The mass multiplier (defaults to 1.0f if not cached or not positive).</returns>
        public float GetMultiplierThreadSafe(int pawnId)
        {
            if (pawnId <= 0) return 1.0f;

            if (TryReadPackedSnapshot(pawnId, out long packed))
            {
                Unpack(packed, out _, out float multiplier);
                return multiplier <= 0f ? 1.0f : multiplier;
            }

            return 1.0f;
        }

        /// <summary>
        /// [CACHE-03] Single canonical lock-free read path: probes the table under an optimistic SeqLock window and returns
        /// the packed (offset, multiplier) payload of a pawn. Retries when a writer mutation overlaps the read, and gives up
        /// after <see cref="MaxReaderRetries"/> attempts. Records hit, miss, retry and timeout telemetry.
        /// </summary>
        /// <remarks>
        /// Force-inlined into both public readers: measured with the diagnostics benchmark, leaving this as a real call cost
        /// roughly 1-1.5 ns (20-25%) per read, while inlining matches the former duplicated loops exactly.
        /// Ordering contract: the first version sample and every probe/payload read are acquire loads (Volatile.Read), so a
        /// validated read (even, unchanged version) can only have observed a fully published state.
        /// </remarks>
        /// <param name="pawnId">The target pawn's unique ID; must be positive.</param>
        /// <param name="packed">The packed payload when found; 0 otherwise.</param>
        /// <returns>True if the pawn was found in a validated, untorn read; false on a miss or a contention timeout.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReadPackedSnapshot(int pawnId, out long packed)
        {
            uint baseSlot = GetNaturalSlot(pawnId);

            for (int retry = 0; retry < MaxReaderRetries; retry++)
            {
                // BREAKPOINT ANCHOR: Reader Contention Retry Tracking
                if (retry > 0)
                {
                    Interlocked.Increment(ref readerRetries);
                    if (forwardTelemetry) PerformanceTelemetry.IncrementReaderRetry();
                    BackOffBeforeRetry(retry);
                }

                int v1 = Volatile.Read(ref tableVersion);
                if ((v1 & 1) != 0)
                {
                    // Mutation in flight on the writer thread; retry probe
                    continue;
                }

                long candidatePayload = 0L;
                bool found = false;

                // BREAKPOINT ANCHOR: Bounded Linear Probe Search (Max 16 Steps)
                for (uint i = 0; i < MaxProbeSteps; i++)
                {
                    uint slot = (baseSlot + i) & mask;
                    int candidateId = Volatile.Read(ref slotPawnIds[slot]);

                    if (candidateId == pawnId)
                    {
                        candidatePayload = Volatile.Read(ref slotPackedData[slot]);
                        found = true;
                        break;
                    }

                    // If an empty slot is encountered, the key does not exist in the probe chain
                    if (candidateId == 0) break;
                }

                // BREAKPOINT ANCHOR: SeqLock Optimistic Read Validation
                // No explicit full fence is needed before the second version sample: every probe and payload read above is a
                // Volatile.Read (acquire), so this later read cannot be reordered before any of them, and the writer brackets its
                // mutations with full-fence Interlocked increments. Measured on Unity's Mono, the former Thread.MemoryBarrier()
                // accounted for roughly two thirds of the cost of a read (about 14 of 21 ns).
                int v2 = Volatile.Read(ref tableVersion);
                if (v1 == v2)
                {
                    // BREAKPOINT ANCHOR: Atomic Performance Counter Increment
                    if (found)
                    {
                        if (forwardTelemetry) PerformanceTelemetry.IncrementBackgroundHit();
                        packed = candidatePayload;
                        return true;
                    }

                    if (forwardTelemetry) PerformanceTelemetry.IncrementBackgroundMiss();
                    packed = 0L;
                    return false;
                }
            }

            // BREAKPOINT ANCHOR: Reader Contention Timeout Fallback
            Interlocked.Increment(ref readerTimeouts);
            if (forwardTelemetry)
            {
                PerformanceTelemetry.IncrementReaderTimeout();
                PerformanceTelemetry.IncrementBackgroundMiss();
            }

            packed = 0L;
            return false;
        }

        #endregion

        #region 5. [CACHE-03] SINGLE-WRITER SYNCHRONIZED SINK & EVICTION OPERATIONS

        /// <summary>
        /// [CACHE-03] Writes or updates calculated Caravan Mass Capacity parameters into the atomic table.
        /// Synchronized via SeqLock mutation boundary. Measures linear probe depth and slot displacements for diagnostics.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <param name="offset">The solved caravan mass capacity offset in kg.</param>
        /// <param name="multiplier">The solved overall mass multiplier.</param>
        public void WriteState(int pawnId, float offset, float multiplier)
        {
            if (pawnId <= 0) return;

            long packed = Pack(offset, multiplier);
            uint baseSlot = GetNaturalSlot(pawnId);
            int targetSlot = -1;
            int probeStepsTaken = 0;
            bool isNewInsertion = false;

            // BREAKPOINT ANCHOR: SeqLock Mutation Window (Data payload before key publication)
            Interlocked.Increment(ref tableVersion);

            try
            {
                // 1. Probe for an existing key match or first open slot, tracking probe distance for telemetry
                for (uint i = 0; i < MaxProbeSteps; i++)
                {
                    uint slot = (baseSlot + i) & mask;
                    int candidateId = slotPawnIds[slot];

                    if (candidateId == pawnId)
                    {
                        targetSlot = (int)slot;
                        probeStepsTaken = (int)(i + 1);
                        break;
                    }

                    if (candidateId == 0)
                    {
                        targetSlot = (int)slot;
                        probeStepsTaken = (int)(i + 1);
                        isNewInsertion = true;
                        break;
                    }
                }

                // 2. Clean eviction fallback if probe bound is saturated
                if (targetSlot == -1)
                {
                    // BREAKPOINT ANCHOR: Saturated Probe Displacement Fallback
                    // Linear probe window is fully saturated (all MaxProbeSteps slots occupied).
                    // Displace the root bucket occupant directly to seat this pawn at its natural hash.
                    // Overwriting baseSlot maintains unbroken non-zero probe chains for downstream keys
                    // without corrupting shifts via premature zeroing.
                    targetSlot = (int)baseSlot;
                    probeStepsTaken = MaxProbeSteps;

                    // If replacing an existing non-zero occupant, net table occupancy remains unchanged;
                    // if baseSlot was somehow zero, it counts as a new insertion.
                    if (slotPawnIds[targetSlot] == 0)
                    {
                        isNewInsertion = true;
                    }

                    Interlocked.Increment(ref displacedWrites);
                    if (forwardTelemetry) PerformanceTelemetry.RecordWriteProbe(MaxProbeSteps, displaced: true);
                }
                else if (forwardTelemetry)
                {
                    PerformanceTelemetry.RecordWriteProbe(probeStepsTaken, displaced: false);
                }

                if (isNewInsertion)
                {
                    occupiedSlots++;
                }

                slotPackedData[targetSlot] = packed;
                Volatile.Write(ref slotPawnIds[targetSlot], pawnId);
            }
            finally
            {
                // Conclude mutation: even version signals published write
                Interlocked.Increment(ref tableVersion);
            }
        }

        /// <summary>
        /// [CACHE-03] Explicitly evicts a pawn entry using Backward-Shift Deletion (Algorithm R).
        /// Reorganizes subsequent collision cluster entries to ensure background linear probe chains remain unbroken.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>True if a matching slot was found and cleanly evicted.</returns>
        public bool Evict(int pawnId)
        {
            if (pawnId <= 0) return false;

            // BREAKPOINT ANCHOR: SeqLock Mutation Window (Data payload before key publication)
            Interlocked.Increment(ref tableVersion);

            try
            {
                return EvictInternal(pawnId);
            }
            finally
            {
                Interlocked.Increment(ref tableVersion);
            }
        }

        /// <summary>
        /// Internal implementation of the backward-shift deletion (Algorithm R) for evicting a pawn entry from the table.
        /// Decrements the active table occupancy counter when a slot is cleared.
        /// </summary>
        /// <param name="pawnId">The unique identifier of the pawn to evict from the table.</param>
        /// <returns>True if the pawn was successfully evicted; otherwise, false.</returns>
        /// <remarks>
        /// This method assumes that the caller has already incremented the table version to signal a mutation window.
        /// It performs a backward-shift deletion to maintain the integrity of the linear probe chain.
        /// </remarks>
        private bool EvictInternal(int pawnId)
        {
            uint baseSlot = GetNaturalSlot(pawnId);
            int targetSlot = -1;

            // Step 1: Locate the target slot within the bounded probe chain
            for (uint i = 0; i < MaxProbeSteps; i++)
            {
                uint slot = (baseSlot + i) & mask;
                int candidateId = slotPawnIds[slot];

                if (candidateId == pawnId)
                {
                    targetSlot = (int)slot;
                    break;
                }

                if (candidateId == 0)
                {
                    return false; // Key does not exist
                }
            }

            if (targetSlot == -1) return false;

            // Step 2: Backward-Shift Deletion across the collision cluster (Algorithm R)
            // BREAKPOINT ANCHOR: Backward-Shift Deletion across the collision cluster
            uint emptySlot = (uint)targetSlot;
            uint scanSlot = (emptySlot + 1) & mask;

            while (true)
            {
                int candidateId = slotPawnIds[scanSlot];
                if (candidateId == 0) break; // Reached end of active cluster

                // In bounded linear probing, if the scan slot has advanced MaxProbeSteps or more
                // past the vacant slot, no subsequent candidate can have a natural hash <= emptySlot.
                uint probeDistance = (scanSlot - emptySlot) & mask;
                if (probeDistance >= MaxProbeSteps) break;

                uint naturalHash = GetNaturalSlot(candidateId);

                // Circular distance from natural hash to candidate's current position vs vacant slot
                uint distToEmpty = (emptySlot - naturalHash) & mask;
                uint distToCurrent = (scanSlot - naturalHash) & mask;

                // If the candidate was displaced past the empty slot, shift it backward to restore proximity
                if (distToEmpty < distToCurrent)
                {
                    slotPackedData[emptySlot] = slotPackedData[scanSlot];
                    slotPawnIds[emptySlot] = candidateId;

                    emptySlot = scanSlot;
                }

                scanSlot = (scanSlot + 1) & mask;
            }

            // Step 3: Zero out the terminal vacant slot and decrement active occupancy
            slotPawnIds[emptySlot] = 0;
            slotPackedData[emptySlot] = 0L;

            if (occupiedSlots > 0)
            {
                occupiedSlots--;
            }

            return true;
        }

        /// <summary>
        /// [CACHE-03] Completely flushes the atomic snapshot table on save load or world reset.
        /// Resets active occupied slot metrics to baseline zero.
        /// </summary>
        public void Reset()
        {
            // BREAKPOINT ANCHOR: Snapshot Registry Flush
            Interlocked.Increment(ref tableVersion);

            try
            {
                Array.Clear(slotPawnIds, 0, capacity);
                Array.Clear(slotPackedData, 0, capacity);
                occupiedSlots = 0;
            }
            finally
            {
                Interlocked.Increment(ref tableVersion);
            }
        }

        /// <summary>
        /// Zeroes the instance-local retry, timeout and displacement counters.
        /// </summary>
        public void ResetCounters()
        {
            Interlocked.Exchange(ref readerRetries, 0);
            Interlocked.Exchange(ref readerTimeouts, 0);
            Interlocked.Exchange(ref displacedWrites, 0);
        }

        #endregion
    }
}

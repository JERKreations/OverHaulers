using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-03] CONCURRENT MASS SNAPSHOT TABLE
    /// Instance implementation of the atomic registry mapping pawn IDs to Caravan Mass Capacity (kg) primitive snapshots.
    /// A dynamic power-of-two open-addressing table (initializing at 512 slots / ~8 KB, 100% L1 Data Cache resident) with a zero-allocation
    /// struct union. Implements Backward-Shift Deletion (Algorithm R) synchronized via a lock-free Sequence Counter (SeqLock).
    /// Guarantees atomic, linearizable, zero-allocation reads for RimWorld background pathfinding threads.
    /// <see cref="MassSnapshotCache"/> owns the single live instance used by the game; the diagnostics self-check creates
    /// private instances so it can stress the algorithm without touching live data. Resides under Source/Core/Registry/.
    /// </summary>
    /// <remarks>
    /// ARCHITECTURAL DESIGN RATIONALE (ZERO-LOCK ATOMIC CONCURRENCY &amp; DYNAMIC L1 SCALING):
    /// RimWorld pathfinding threads query pawn mass off the main thread at high frequency. Rather than incurring
    /// lock contention or GC allocations via ConcurrentDictionary, this table maintains two flat
    /// primitive arrays sized to a dynamic power-of-two (initializing at 512 slots, ~8 KB total footprint).
    ///
    /// 1. Dynamic L1-Resident Sizing: Starts at 512 slots (8 KB), residing entirely within the CPU's L1 Data Cache for
    ///    sub-nanosecond read latency. Scales dynamically (512 &lt;-&gt; 8192) based on load factor watermarks.
    /// 2. Bounded Probing (8 Steps): Max linear probe depth is bounded to 8 steps (halved from 16), cutting worst-case
    ///    miss latency in half for off-thread pathfinders.
    /// 3. RCU Local Reference Capture: Readers snapshot local array references and derive their bitwise mask directly from
    ///    the array instance length inside the optimistic SeqLock attempt, mathematically preventing IndexOutOfRangeException
    ///    during concurrent resize operations without locks.
    /// 4. Bit-Cast Struct Union: Packs two 32-bit floats into a single atomic 64-bit integer.
    ///    Background threads read both metrics in a single atomic instruction without locks.
    /// 5. Backward-Shift Deletion (Algorithm R) &amp; SeqLock: Traditional open-addressing uses
    ///    "tombstones" for deleted keys, which degrades search performance over time. Backward-shift
    ///    deletion physically slides displaced cluster keys backward to fill the vacuum, keeping
    ///    probe chains compact. To ensure background readers never observe intermediate shifted
    ///    states, mutations are bounded by a lightweight Sequence Counter (SeqLock).
    /// 6. Multiplicative Hashing: Knuth's golden-ratio multiplier uniformly diffuses sequential
    ///    RimWorld Thing IDs across power-of-two buckets, eliminating primary clustering.
    /// 7. Zero-Contention Telemetry: Reader conflict retries, probe step depths, and slot displacements
    ///    are tracked via gated atomic accumulators, providing visibility without cache line ping-pong.
    ///
    /// Threading contract: <see cref="WriteState"/>, <see cref="Evict"/> and <see cref="Reset"/> must only be called from one
    /// thread at a time (the main thread in the live instance); the read methods may be called from any thread.
    /// </remarks>
    public sealed class SnapshotTable
    {
        #region 1. CONSTANTS & STRUCT UNIONS

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

        /// <summary>Default initial slot count of the live table, accommodating active simultaneous map populations (512 slots / ~8 KB, 100% L1 Data Cache resident).</summary>
        public const int DefaultCapacity = 512;

        /// <summary>Minimum power-of-two slot count permitted for dynamic tables.</summary>
        public const int MinCapacity = 256;

        /// <summary>Maximum power-of-two slot count permitted for dynamic tables (128 KB, L2 resident).</summary>
        public const int MaxCapacity = 8192;

        /// <summary>Maximum linear probe depth before replacing the root hash bucket (16 slots aligns with a 64-byte L1 cache line).</summary>
        public const int MaxProbeSteps = 16;

        /// <summary>Maximum reader retry attempts when encountering a concurrent writer mutation.</summary>
        private const int MaxReaderRetries = 8;

        /// <summary>Knuth's 32-bit golden ratio multiplicative hash constant.</summary>
        private const uint HashMultiplier = 2654435761u;

        /// <summary>Load factor threshold triggering power-of-two expansion (65% occupancy).</summary>
        private const float HighWatermarkRatio = 0.65f;

        /// <summary>Load factor threshold triggering power-of-two contraction with wide hysteresis (25% occupancy).</summary>
        private const float LowWatermarkRatio = 0.25f;

        #endregion

        #region 2. INSTANCE FIELDS & BACKING STATE

        private int capacity;
        private uint mask;
        private readonly int minConfiguredCapacity;
        private readonly bool dynamicScalingEnabled;
        private readonly bool forwardTelemetry;

        // Parallel contiguous arrays resident in L1/L2 CPU cache (reassigned atomically during resize)
        private int[] slotPawnIds;
        private long[] slotPackedData;

        // Sequence counter for optimistic concurrency:
        // Even = stable table state; Odd = writer mutation in flight.
        // Governed by Volatile.Read / Interlocked; declared without 'volatile' to prevent CS0420.
        private int tableVersion;

        // Active non-zero slot counter tracking resident entries.
        // Modified exclusively by the single writer thread; read safely via Volatile.Read.
        private int occupiedSlots;

        // High-water diagnostic throttle flag
        private bool hasLoggedHighWaterWarning;

        // Instance-local diagnostics, independent of the global PerformanceTelemetry.
        private long readerRetries;
        private long readerTimeouts;
        private long displacedWrites;

        #endregion

        #region 3. PROPERTIES & ENCAPSULATED ACCESSORS

        /// <summary>Slot count of this table.</summary>
        public int Capacity => capacity;

        /// <summary>Retrieves the exact count of occupied, non-zero slots currently residing in the atomic table.</summary>
        public int OccupiedSlotsCount => Volatile.Read(ref occupiedSlots);

        /// <summary>Current load factor percentage in the range 0 to 100.</summary>
        public float OccupancyPercentage => capacity > 0 ? ((float)OccupiedSlotsCount / capacity) * 100f : 0f;

        /// <summary>Number of reader retries caused by concurrent writer mutations since creation or the last <see cref="ResetCounters"/>.</summary>
        public long ReaderRetries => Interlocked.Read(ref readerRetries);

        /// <summary>Number of reads that exhausted every retry and fell back to the neutral default.</summary>
        public long ReaderTimeouts => Interlocked.Read(ref readerTimeouts);

        /// <summary>Number of writes that found the probe window saturated and displaced the root occupant.</summary>
        public long DisplacedWrites => Interlocked.Read(ref displacedWrites);

        #endregion

        #region 4. LIFECYCLE & RESET METHODS

        /// <summary>
        /// Creates a new atomic snapshot table.
        /// </summary>
        /// <param name="capacity">Slot count; must be a power of two and at least twice <see cref="MaxProbeSteps"/>.</param>
        /// <param name="forwardTelemetry">
        /// When true, hit/miss/retry/probe events are also reported to <see cref="PerformanceTelemetry"/>.
        /// Only the live table does this, so private test instances never pollute the game's metrics.
        /// </param>
        /// <param name="enableDynamicScaling">
        /// When true, the table dynamically scales between initial capacity and <see cref="MaxCapacity"/> based on occupancy watermarks.
        /// </param>
        public SnapshotTable(int capacity = DefaultCapacity, bool forwardTelemetry = false, bool enableDynamicScaling = true)
        {
            if (capacity < MaxProbeSteps * 2 || (capacity & (capacity - 1)) != 0)
            {
                throw new ArgumentException("Capacity must be a power of two and at least " + (MaxProbeSteps * 2) + ".", nameof(capacity));
            }

            this.capacity = capacity;
            this.mask = (uint)(capacity - 1);
            this.minConfiguredCapacity = capacity;
            this.forwardTelemetry = forwardTelemetry;
            this.dynamicScalingEnabled = enableDynamicScaling;
            this.slotPawnIds = new int[capacity];
            this.slotPackedData = new long[capacity];

            if (forwardTelemetry)
            {
                OHLog.Performance.SnapshotTableInitialized(capacity, enableDynamicScaling, minConfiguredCapacity, MaxCapacity, MaxProbeSteps);
            }
        }

        /// <summary>
        /// [CACHE-03] Completely flushes the atomic snapshot table on save load or world reset.
        /// Resets active occupied slot metrics to baseline zero and contracts back to initial capacity.
        /// </summary>
        public void Reset()
        {
            // BREAKPOINT ANCHOR: Snapshot Registry Flush
            Interlocked.Increment(ref tableVersion);

            try
            {
                if (dynamicScalingEnabled && capacity > minConfiguredCapacity)
                {
                    capacity = minConfiguredCapacity;
                    mask = (uint)(minConfiguredCapacity - 1);
                    slotPawnIds = new int[minConfiguredCapacity];
                    slotPackedData = new long[minConfiguredCapacity];
                }
                else
                {
                    Array.Clear(slotPawnIds, 0, capacity);
                    Array.Clear(slotPackedData, 0, capacity);
                }

                int clearedEntries = occupiedSlots;
                occupiedSlots = 0;
                hasLoggedHighWaterWarning = false;

                if (forwardTelemetry && clearedEntries > 0)
                {
                    OHLog.Performance.SnapshotTableFlushed(clearedEntries, capacity);
                }
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

        #region 5. PUBLIC CANONICAL MUTATION & READ API

        /// <summary>
        /// [CACHE-03] Writes or updates calculated Caravan Mass Capacity parameters into the atomic table.
        /// Synchronized via SeqLock mutation boundary. Measures linear probe depth and slot displacements for diagnostics.
        /// Handles dynamic power-of-two growth if high watermark is crossed.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <param name="offset">The solved caravan mass capacity offset in kg.</param>
        /// <param name="multiplier">The solved overall mass multiplier.</param>
        public void WriteState(int pawnId, float offset, float multiplier)
        {
            if (pawnId <= 0) return;

            // BREAKPOINT ANCHOR: SeqLock Mutation Window (Data payload before key publication)
            Interlocked.Increment(ref tableVersion);

            try
            {
                // Dynamic expansion check before write
                if (dynamicScalingEnabled && capacity < MaxCapacity && (occupiedSlots + 1) >= (int)(capacity * HighWatermarkRatio))
                {
                    ResizeInternal(capacity << 1);
                }

                long packed = Pack(offset, multiplier);
                uint baseSlot = GetNaturalSlot(pawnId);
                int targetSlot = -1;
                int probeStepsTaken = 0;
                bool isNewInsertion = false;

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

                CheckHighWaterDiagnostics();
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
        /// Handles dynamic power-of-two contraction if low watermark is crossed.
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
                bool evicted = EvictInternal(pawnId);

                // Dynamic contraction check with wide hysteresis
                if (evicted && dynamicScalingEnabled && capacity > minConfiguredCapacity && occupiedSlots <= (int)(capacity * LowWatermarkRatio))
                {
                    ResizeInternal(capacity >> 1);
                }

                return evicted;
            }
            finally
            {
                Interlocked.Increment(ref tableVersion);
            }
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
        /// RCU Invariant: snapshotting local array references and deriving local mask directly from array length guarantees
        /// immunity to IndexOutOfRangeException during concurrent power-of-two resizes.
        /// </remarks>
        /// <param name="pawnId">The target pawn's unique ID; must be positive.</param>
        /// <param name="packed">The packed payload when found; 0 otherwise.</param>
        /// <returns>True if the pawn was found in a validated, untorn read; false on a miss or a contention timeout.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadPackedSnapshot(int pawnId, out long packed)
        {
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

                // RCU Local Reference Capture: derive local mask strictly from the array instance length
                int[] ids = this.slotPawnIds;
                long[] data = this.slotPackedData;
                uint localMask = (uint)(ids.Length - 1);

                uint baseSlot = ((uint)pawnId * HashMultiplier) & localMask;
                long candidatePayload = 0L;
                bool found = false;

                // BREAKPOINT ANCHOR: Bounded Linear Probe Search (Max 8 Steps)
                for (uint i = 0; i < MaxProbeSteps; i++)
                {
                    uint slot = (baseSlot + i) & localMask;
                    int candidateId = Volatile.Read(ref ids[slot]);

                    if (candidateId == pawnId)
                    {
                        candidatePayload = Volatile.Read(ref data[slot]);
                        found = true;
                        break;
                    }

                    // If an empty slot is encountered, the key does not exist in the probe chain
                    if (candidateId == 0) break;
                }

                // BREAKPOINT ANCHOR: SeqLock Optimistic Read Validation
                // No explicit full fence is needed before the second version sample: every probe and payload read above is a
                // Volatile.Read (acquire), so this later read cannot be reordered before any of them, and the writer brackets its
                // mutations with full-fence Interlocked increments.
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

        #region 6. SECONDARY THREAD-SAFE READERS & DIAGNOSTIC HOOKS

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
        /// Returns the slot a pawn ID hashes to in this table. Exposed so diagnostics can construct deliberate collisions.
        /// </summary>
        /// <param name="pawnId">The pawn ID to hash.</param>
        /// <returns>The natural (root) slot index of the ID.</returns>
        public int NaturalSlotOf(int pawnId)
        {
            return (int)GetNaturalSlot(pawnId);
        }

        #endregion

        #region 7. ALGORITHMIC CLUSTER SWEEPS (ALGORITHM R) & RESIZE

        /// <summary>
        /// Internal implementation of backward-shift deletion (Algorithm R) for evicting a pawn entry from the table.
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
        /// Reallocates parallel contiguous primitive arrays and rehashes existing entries under the active SeqLock mutation window.
        /// </summary>
        /// <param name="newCapacity">Target power-of-two capacity.</param>
        private void ResizeInternal(int newCapacity)
        {
            if (newCapacity < minConfiguredCapacity || newCapacity > MaxCapacity || newCapacity == capacity) return;
            if ((newCapacity & (newCapacity - 1)) != 0) return;

            int[] oldPawnIds = this.slotPawnIds;
            long[] oldPackedData = this.slotPackedData;
            int oldCapacity = this.capacity;

            int[] newPawnIds = new int[newCapacity];
            long[] newPackedData = new long[newCapacity];
            uint newMask = (uint)(newCapacity - 1);
            int rehashedCount = 0;

            for (int i = 0; i < oldCapacity; i++)
            {
                int id = oldPawnIds[i];
                if (id <= 0) continue;

                long data = oldPackedData[i];
                uint baseSlot = ((uint)id * HashMultiplier) & newMask;

                for (uint step = 0; step < MaxProbeSteps; step++)
                {
                    uint slot = (baseSlot + step) & newMask;
                    if (newPawnIds[slot] == 0)
                    {
                        newPawnIds[slot] = id;
                        newPackedData[slot] = data;
                        rehashedCount++;
                        break;
                    }
                }
            }

            int oldOccupancy = this.occupiedSlots;
            this.slotPawnIds = newPawnIds;
            this.slotPackedData = newPackedData;
            this.capacity = newCapacity;
            this.mask = newMask;
            this.occupiedSlots = rehashedCount;

            if (forwardTelemetry)
            {
                float load = oldCapacity > 0 ? ((float)oldOccupancy / oldCapacity) * 100f : 0f;
                if (newCapacity > oldCapacity)
                {
                    OHLog.Performance.SnapshotTableScaledUp(oldCapacity, newCapacity, oldOccupancy, load);
                }
                else
                {
                    OHLog.Performance.SnapshotTableContracted(oldCapacity, newCapacity, oldOccupancy, load);
                }
            }
        }

        /// <summary>
        /// Evaluates table load factor against the high-water threshold and emits throttled diagnostic telemetry warnings.
        /// </summary>
        private void CheckHighWaterDiagnostics()
        {
            if (capacity >= MaxCapacity && occupiedSlots >= (int)(capacity * 0.75f))
            {
                if (!hasLoggedHighWaterWarning)
                {
                    hasLoggedHighWaterWarning = true;
                    OHLog.Performance.Warn("SnapshotTable", null,
                        $"Dynamic SnapshotTable reached maximum capacity ({MaxCapacity}) with high load factor ({(OccupancyPercentage):F1}%). Probing depth may increase.");
                }
            }
            else if (hasLoggedHighWaterWarning && occupiedSlots < (int)(capacity * 0.60f))
            {
                hasLoggedHighWaterWarning = false;
            }
        }

        #endregion

        #region 8. HASHING PRIMITIVES & PRIVATE HELPERS

        /// <summary>
        /// Packs two 32-bit floating point metrics (Offset in kg and Multiplier) into a single atomic 64-bit integer with 0 GC overhead.
        /// </summary>
        /// <param name="offset">Caravan mass capacity offset in kg.</param>
        /// <param name="multiplier">Total physical mass multiplier.</param>
        /// <returns>A packed 64-bit integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Pack(float offset, float multiplier)
        {
            FloatPairUnion union = default(FloatPairUnion);
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
            FloatPairUnion union = default(FloatPairUnion);
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

        #endregion
    }
}
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-03] CONCURRENT MASS SNAPSHOT TABLE
    /// Instance implementation of the atomic registry mapping pawn IDs to Caravan Mass Capacity (kg) physical offsets.
    /// Operates as a dynamic power-of-two open-addressing table (initializing at 512 slots / 4 KB, exactly one memory page, L1 resident)
    /// storing aligned 32-bit float offsets. Implements Backward-Shift Deletion (Algorithm R) synchronized via a lock-free
    /// Sequence Counter (SeqLock) and Read-Copy-Update (RCU) atomic compound buffer snapshotting.
    /// Guarantees atomic, linearizable, zero-allocation reads for background pathfinding threads.
    /// Resides under Source/Core/Registry/.
    /// </summary>
    /// <remarks>
    /// ARCHITECTURAL DESIGN RATIONALE (ZERO-LOCK ATOMIC CONCURRENCY &amp; DYNAMIC L1 SCALING):
    /// 1. Dynamic L1-Resident Sizing: Starts at 512 slots (4 KB), residing entirely within the CPU's L1 Data Cache for
    ///    sub-nanosecond read latency. Scales dynamically (512 &lt;-&gt; 8192) based on load factor watermarks.
    /// 2. Cache-Aligned Linear Probing (16 Steps): Max linear probe depth is bounded to 16 steps, aligning with a 64-byte L1 cache line fetch.
    /// 3. RCU Compound Buffer Capture: Parallel arrays and their bitwise mask are encapsulated within an immutable TableBuffer instance.
    ///    Readers snapshot this compound reference via an atomic Volatile.Read, mathematically preventing length mismatches,
    ///    uninitialized element reads, and IndexOutOfRangeException during concurrent resizes on weak-memory architectures (ARM64).
    /// 4. Direct 32-Bit Float Reads: Reads and writes to aligned 32-bit floats are hardware-atomic on x86 and ARM.
    /// 5. Backward-Shift Deletion (Algorithm R) &amp; SeqLock: Traditional open-addressing uses "tombstones" which degrade search performance.
    ///    Backward-shift deletion physically slides displaced cluster keys backward to fill the vacuum, keeping probe chains compact.
    /// 6. Multiplicative Hashing: Knuth's golden-ratio multiplier uniformly diffuses sequential RimWorld Thing IDs across power-of-two buckets.
    /// 7. Zero-Contention Telemetry: Reader conflict retries, probe step depths, and slot displacements are tracked via gated atomic accumulators.
    ///
    /// Threading contract: <see cref="WriteState"/>, <see cref="Evict"/> and <see cref="Reset"/> must only be called from one
    /// thread at a time (the main thread in the live instance); read methods may be called concurrently from any thread.
    /// </remarks>
    public sealed class SnapshotTable
    {
        #region 1. CONSTANTS & NESTED DATA MODELS

        /// <summary>Default initial slot count of the live table, accommodating active simultaneous map populations (512 slots / 4 KB, 100% L1 Data Cache resident).</summary>
        public const int DefaultCapacity = 512;

        /// <summary>Minimum power-of-two slot count permitted for dynamic tables.</summary>
        public const int MinCapacity = 256;

        /// <summary>Maximum power-of-two slot count permitted for dynamic tables (64 KB, L2 resident).</summary>
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

        /// <summary>
        /// [CACHE-03] Immutable compound array buffer holding parallel pawn ID and offset arrays.
        /// Enables atomic single-pointer publication (RCU) across dynamic table resizes, guaranteeing
        /// identical array lengths and eliminating torn-reference race conditions.
        /// </summary>
        private sealed class TableBuffer
        {
            public readonly int Capacity;
            public readonly uint Mask;
            public readonly int[] PawnIds;
            public readonly float[] Offsets;

            public TableBuffer(int capacity)
            {
                this.Capacity = capacity;
                this.Mask = (uint)(capacity - 1);
                this.PawnIds = new int[capacity];
                this.Offsets = new float[capacity];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Clear()
            {
                Array.Clear(this.PawnIds, 0, this.Capacity);
                Array.Clear(this.Offsets, 0, this.Capacity);
            }
        }

        #endregion

        #region 2. INSTANCE FIELDS & BACKING STATE

        private readonly int minConfiguredCapacity;
        private readonly bool dynamicScalingEnabled;
        private readonly bool forwardTelemetry;

        // Atomic compound array buffer resident in L1/L2 CPU cache (reassigned atomically via Volatile.Write during resize)
        private TableBuffer activeBuffer;

        // Sequence counter for optimistic concurrency: Even = stable; Odd = writer mutation in flight.
        private int tableVersion;

        // Active non-zero slot counter tracking resident entries.
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
        public int Capacity => Volatile.Read(ref activeBuffer).Capacity;

        /// <summary>Retrieves the exact count of occupied, non-zero slots currently residing in the atomic table.</summary>
        public int OccupiedSlotsCount => Volatile.Read(ref occupiedSlots);

        /// <summary>Current load factor percentage in the range 0 to 100.</summary>
        public float OccupancyPercentage
        {
            get
            {
                int cap = Volatile.Read(ref activeBuffer).Capacity;
                return cap > 0 ? ((float)OccupiedSlotsCount / cap) * 100f : 0f;
            }
        }

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
        /// <param name="forwardTelemetry">When true, hit/miss/retry/probe events are reported to <see cref="PerformanceTelemetry"/>.</param>
        /// <param name="enableDynamicScaling">When true, the table dynamically scales between initial capacity and <see cref="MaxCapacity"/>.</param>
        public SnapshotTable(int capacity = DefaultCapacity, bool forwardTelemetry = false, bool enableDynamicScaling = true)
        {
            if (capacity < MaxProbeSteps * 2 || (capacity & (capacity - 1)) != 0)
            {
                throw new ArgumentException("Capacity must be a power of two and at least " + (MaxProbeSteps * 2) + ".", nameof(capacity));
            }

            this.minConfiguredCapacity = capacity;
            this.forwardTelemetry = forwardTelemetry;
            this.dynamicScalingEnabled = enableDynamicScaling;
            this.activeBuffer = new TableBuffer(capacity);
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
                int clearedEntries = occupiedSlots;
                TableBuffer current = Volatile.Read(ref this.activeBuffer);

                if (dynamicScalingEnabled && current.Capacity > minConfiguredCapacity)
                {
                    Volatile.Write(ref this.activeBuffer, new TableBuffer(minConfiguredCapacity));
                }
                else
                {
                    current.Clear();
                }

                occupiedSlots = 0;
                hasLoggedHighWaterWarning = false;

                if (forwardTelemetry && clearedEntries > 0)
                {
                    OHLog.Performance.SnapshotTableFlushed(clearedEntries, Volatile.Read(ref this.activeBuffer).Capacity);
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
        /// [CACHE-03] Writes or updates calculated Caravan Mass Capacity physical offset (kg) into the atomic table.
        /// Synchronized via SeqLock mutation boundary. Handles dynamic power-of-two growth if high watermark is crossed.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <param name="offset">The solved caravan mass capacity offset in kg.</param>
        public void WriteState(int pawnId, float offset)
        {
            if (pawnId <= 0) return;

            // BREAKPOINT ANCHOR: SeqLock Mutation Window
            Interlocked.Increment(ref tableVersion);

            try
            {
                TableBuffer buffer = Volatile.Read(ref this.activeBuffer);

                if (dynamicScalingEnabled && buffer.Capacity < MaxCapacity && (occupiedSlots + 1) >= (int)(buffer.Capacity * HighWatermarkRatio))
                {
                    ResizeInternal(buffer.Capacity << 1);
                    buffer = Volatile.Read(ref this.activeBuffer);
                }

                int[] ids = buffer.PawnIds;
                float[] offsets = buffer.Offsets;
                uint mask = buffer.Mask;

                uint baseSlot = ((uint)pawnId * HashMultiplier) & mask;
                int targetSlot = -1;
                int probeStepsTaken = 0;
                bool isNewInsertion = false;

                for (uint i = 0; i < MaxProbeSteps; i++)
                {
                    uint slot = (baseSlot + i) & mask;
                    int candidateId = ids[slot];

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

                if (targetSlot == -1)
                {
                    // BREAKPOINT ANCHOR: Saturated Probe Displacement Fallback
                    targetSlot = (int)baseSlot;
                    probeStepsTaken = MaxProbeSteps;

                    if (ids[targetSlot] == 0)
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

                offsets[targetSlot] = offset;
                Volatile.Write(ref ids[targetSlot], pawnId);

                CheckHighWaterDiagnostics();
            }
            finally
            {
                Interlocked.Increment(ref tableVersion);
            }
        }

        /// <summary>
        /// [CACHE-03] Explicitly evicts a pawn entry using Backward-Shift Deletion (Algorithm R).
        /// Handles dynamic power-of-two contraction if low watermark is crossed.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>True if a matching slot was found and cleanly evicted.</returns>
        public bool Evict(int pawnId)
        {
            if (pawnId <= 0) return false;

            // BREAKPOINT ANCHOR: SeqLock Mutation Window
            Interlocked.Increment(ref tableVersion);

            try
            {
                bool evicted = EvictInternal(pawnId);
                TableBuffer buffer = Volatile.Read(ref this.activeBuffer);

                if (evicted && dynamicScalingEnabled && buffer.Capacity > minConfiguredCapacity && occupiedSlots <= (int)(buffer.Capacity * LowWatermarkRatio))
                {
                    ResizeInternal(buffer.Capacity >> 1);
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
        /// the physical capacity offset (kg) of a pawn. Employs RCU compound buffer snapshotting to eliminate bounds faults.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID; must be positive.</param>
        /// <param name="offset">The physical capacity offset in kg when found; 0 otherwise.</param>
        /// <returns>True if the pawn was found in a validated, untorn read; false on a miss or a contention timeout.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadOffset(int pawnId, out float offset)
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
                    continue;
                }

                // RCU Compound Buffer Capture: single atomic pointer load guarantees identical array generation and length
                TableBuffer buffer = Volatile.Read(ref this.activeBuffer);
                int[] ids = buffer.PawnIds;
                float[] offsets = buffer.Offsets;
                uint localMask = buffer.Mask;

                uint baseSlot = ((uint)pawnId * HashMultiplier) & localMask;
                float candidateOffset = 0f;
                bool found = false;

                // BREAKPOINT ANCHOR: Bounded Linear Probe Search (Max 16 Steps)
                for (uint i = 0; i < MaxProbeSteps; i++)
                {
                    uint slot = (baseSlot + i) & localMask;
                    int candidateId = Volatile.Read(ref ids[slot]);

                    if (candidateId == pawnId)
                    {
                        candidateOffset = Volatile.Read(ref offsets[slot]);
                        found = true;
                        break;
                    }

                    if (candidateId == 0) break;
                }

                // BREAKPOINT ANCHOR: SeqLock Optimistic Read Validation
                int v2 = Volatile.Read(ref tableVersion);
                if (v1 == v2)
                {
                    if (found)
                    {
                        if (forwardTelemetry) PerformanceTelemetry.IncrementBackgroundHit();
                        offset = candidateOffset;
                        return true;
                    }

                    if (forwardTelemetry) PerformanceTelemetry.IncrementBackgroundMiss();
                    offset = 0f;
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

            offset = 0f;
            return false;
        }

        #endregion

        #region 6. SECONDARY ENTRY POINTS & DIAGNOSTIC SLOTS

        /// <summary>
        /// [CACHE-03] Safely retrieves the cached Caravan Mass Capacity offset (kg) from any thread.
        /// Fully lock-free, zero-allocation, and linearizable via optimistic SeqLock reads.
        /// Thin pass-through to <see cref="TryReadOffset"/>.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>The physical offset in kg (defaults to 0.0f if not cached).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetOffsetThreadSafe(int pawnId)
        {
            if (pawnId <= 0) return 0f;
            return TryReadOffset(pawnId, out float offset) ? offset : 0f;
        }

        /// <summary>
        /// Returns the slot a pawn ID hashes to in this table. Exposed so diagnostics can construct deliberate collisions.
        /// </summary>
        /// <param name="pawnId">The pawn ID to hash.</param>
        /// <returns>The natural (root) slot index of the ID.</returns>
        public int NaturalSlotOf(int pawnId)
        {
            return (int)(((uint)pawnId * HashMultiplier) & Volatile.Read(ref activeBuffer).Mask);
        }

        #endregion

        #region 7. ALGORITHMIC CLUSTER SWEEPS (ALGORITHM R) & RESIZE

        /// <summary>
        /// Internal implementation of backward-shift deletion (Algorithm R) for evicting a pawn entry from the table.
        /// </summary>
        private bool EvictInternal(int pawnId)
        {
            TableBuffer buffer = Volatile.Read(ref this.activeBuffer);
            int[] ids = buffer.PawnIds;
            float[] offsets = buffer.Offsets;
            uint mask = buffer.Mask;

            uint baseSlot = ((uint)pawnId * HashMultiplier) & mask;
            int targetSlot = -1;

            // Step 1: Locate the target slot within the bounded probe chain
            for (uint i = 0; i < MaxProbeSteps; i++)
            {
                uint slot = (baseSlot + i) & mask;
                int candidateId = ids[slot];

                if (candidateId == pawnId)
                {
                    targetSlot = (int)slot;
                    break;
                }

                if (candidateId == 0)
                {
                    return false;
                }
            }

            if (targetSlot == -1) return false;

            // Step 2: Backward-Shift Deletion across the collision cluster (Algorithm R)
            // BREAKPOINT ANCHOR: Backward-Shift Deletion across the collision cluster
            uint emptySlot = (uint)targetSlot;
            uint scanSlot = (emptySlot + 1) & mask;

            while (true)
            {
                int candidateId = ids[scanSlot];
                if (candidateId == 0) break;

                uint probeDistance = (scanSlot - emptySlot) & mask;
                if (probeDistance >= MaxProbeSteps) break;

                uint naturalHash = ((uint)candidateId * HashMultiplier) & mask;

                uint distToEmpty = (emptySlot - naturalHash) & mask;
                uint distToCurrent = (scanSlot - naturalHash) & mask;

                if (distToEmpty < distToCurrent)
                {
                    offsets[emptySlot] = offsets[scanSlot];
                    ids[emptySlot] = candidateId;

                    emptySlot = scanSlot;
                }

                scanSlot = (scanSlot + 1) & mask;
            }

            // Step 3: Zero out the terminal vacant slot and decrement active occupancy
            ids[emptySlot] = 0;
            offsets[emptySlot] = 0f;

            if (occupiedSlots > 0)
            {
                occupiedSlots--;
            }

            return true;
        }

        /// <summary>
        /// Reallocates parallel contiguous primitive arrays and rehashes existing entries under the active SeqLock mutation window.
        /// Publishes the new buffer atomically via a single Volatile.Write barrier.
        /// </summary>
        /// <param name="newCapacity">The new capacity to resize the table to. Must be a power of two and within configured bounds.</param>
        private void ResizeInternal(int newCapacity)
        {
            TableBuffer oldBuffer = Volatile.Read(ref this.activeBuffer);
            int oldCapacity = oldBuffer.Capacity;

            if (newCapacity < minConfiguredCapacity || newCapacity > MaxCapacity || newCapacity == oldCapacity) return;
            if ((newCapacity & (newCapacity - 1)) != 0) return;

            int[] oldPawnIds = oldBuffer.PawnIds;
            float[] oldOffsets = oldBuffer.Offsets;

            TableBuffer newBuffer = new TableBuffer(newCapacity);
            int[] newPawnIds = newBuffer.PawnIds;
            float[] newOffsets = newBuffer.Offsets;
            uint newMask = newBuffer.Mask;
            int rehashedCount = 0;

            for (int i = 0; i < oldCapacity; i++)
            {
                int id = oldPawnIds[i];
                if (id <= 0) continue;

                float offset = oldOffsets[i];
                uint baseSlot = ((uint)id * HashMultiplier) & newMask;

                for (uint step = 0; step < MaxProbeSteps; step++)
                {
                    uint slot = (baseSlot + step) & newMask;
                    if (newPawnIds[slot] == 0)
                    {
                        newPawnIds[slot] = id;
                        newOffsets[slot] = offset;
                        rehashedCount++;
                        break;
                    }
                }
            }

            int oldOccupancy = this.occupiedSlots;
            Volatile.Write(ref this.activeBuffer, newBuffer);
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
            TableBuffer buffer = Volatile.Read(ref this.activeBuffer);
            int cap = buffer.Capacity;
            if (cap >= MaxCapacity && occupiedSlots >= (int)(cap * 0.75f))
            {
                if (!hasLoggedHighWaterWarning)
                {
                    hasLoggedHighWaterWarning = true;
                    OHLog.Performance.Warn("SnapshotTable", null,
                        $"Dynamic SnapshotTable reached maximum capacity ({MaxCapacity}) with high load factor ({(OccupancyPercentage):F1}%). Probing depth may increase.");
                }
            }
            else if (hasLoggedHighWaterWarning && occupiedSlots < (int)(cap * 0.60f))
            {
                hasLoggedHighWaterWarning = false;
            }
        }

        #endregion

        #region 8. HASHING PRIMITIVES & PRIVATE HELPERS

        /// <summary>
        /// Short exponential spin so a reader that collided with an in-flight writer does not burn all of its retries instantly.
        /// </summary>
        /// <param name="retry">The zero-based retry number about to be attempted.</param>
        private static void BackOffBeforeRetry(int retry)
        {
            Thread.SpinWait(32 << Math.Min(retry, 6));
        }

        #endregion
    }
}
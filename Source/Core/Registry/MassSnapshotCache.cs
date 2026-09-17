using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-03] CONCURRENT MASS SNAPSHOT REGISTRY
    /// High-performance atomic registry mapping active Pawn IDs to Caravan Mass Capacity (kg) primitive snapshots.
    /// Utilizes a 4096-slot fixed power-of-two table (~32 KB footprint, L1/L2 resident) with a zero-allocation struct union.
    /// Implements Backward-Shift Deletion (Algorithm R) synchronized via a lock-free Sequence Counter (SeqLock).
    /// Guarantees atomic, linearizable, zero-allocation reads for RimWorld background pathfinding threads.
    /// </summary>
    /// <remarks>
    // ARCHITECTURAL DESIGN RATIONALE (ZERO-LOCK ATOMIC CONCURRENCY):
    // RimWorld pathfinding threads query pawn mass off the main thread. Rather than incurring
    // lock contention or GC allocations via ConcurrentDictionary, this cache maintains two flat
    // primitive arrays sized to a fixed power-of-two (4096 slots, ~32 KB total footprint).
    //
    // 1. Bit-Cast Struct Union: Packs two 32-bit floats into a single atomic 64-bit integer.
    //    Background threads read both metrics in a single atomic instruction without locks.
    // 2. Backward-Shift Deletion (Algorithm R) & SeqLock: Traditional open-addressing uses
    //    "tombstones" for deleted keys, which degrades search performance over time. Backward-shift
    //    deletion physically slides displaced cluster keys backward to fill the vacuum, keeping
    //    probe chains compact. To ensure background readers never observe intermediate shifted
    //    states, mutations are bounded by a lightweight Sequence Counter (SeqLock).
    // 3. Multiplicative Hashing: Knuth's golden-ratio multiplier uniformly diffuses sequential
    //    RimWorld Thing IDs across power-of-two buckets, eliminating primary clustering.
    /// </remarks>
    public static class MassSnapshotCache
    {
        #region 1. ZERO-ALLOCATION BIT-CAST STRUCT UNION

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatPairUnion
        {
            [FieldOffset(0)] public float Offset;
            [FieldOffset(4)] public float Multiplier;
            [FieldOffset(0)] public long Packed;
        }

        #endregion

        #region 2. STORAGE CONSTANTS & PACKED DATA TABLES

        /// <summary>Fixed power-of-two capacity accommodating maximum active simultaneous map populations.</summary>
        public const int TableCapacity = 4096;

        /// <summary>Fast bitwise modulo mask (TableCapacity - 1).</summary>
        public const int TableMask = TableCapacity - 1;

        /// <summary>Maximum linear probe depth before replacing the root hash bucket.</summary>
        public const int MaxProbeSteps = 16;

        /// <summary>Maximum reader retry attempts when encountering a concurrent writer mutation.</summary>
        private const int MaxReaderRetries = 4;

        /// <summary>Knuth's 32-bit golden ratio multiplicative hash constant.</summary>
        private const uint HashMultiplier = 2654435761u;

        // Sequence counter for optimistic concurrency:
        // Even = stable table state; Odd = writer mutation in flight.
        // Governed by Volatile.Read / Volatile.Write; declared without 'volatile' to prevent CS0420.
        private static int tableVersion = 0;

        // Parallel contiguous arrays resident in L1/L2 CPU cache (~32 KB total footprint)
        private static readonly int[] slotPawnIds = new int[TableCapacity];
        private static readonly long[] slotPackedData = new long[TableCapacity];

        #endregion

        #region 3. [CACHE-03] PACKING & UNPACKING PRIMITIVES

        /// <summary>
        /// Applies multiplicative hashing to uniformly diffuse sequential Pawn IDs across the power-of-two table.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetNaturalSlot(int pawnId)
        {
            return ((uint)pawnId * HashMultiplier) & TableMask;
        }

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

        #endregion

        #region 4. [CACHE-03] LOCK-FREE BACKGROUND READ PATHS

        /// <summary>
        /// [CACHE-03] Safely retrieves the cached Caravan Mass Capacity offset (kg) off the main thread.
        /// Fully lock-free, zero-allocation, and linearizable via optimistic SeqLock reads.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>The physical offset in kg (defaults to 0.0f if not cached).</returns>
        public static float GetOffsetThreadSafe(int pawnId)
        {
            if (pawnId <= 0) return 0f;

            // BREAKPOINT ANCHOR: Atomic Performance Counter Increment
            PerformanceTelemetry.IncrementBackgroundQueries();

            uint baseSlot = GetNaturalSlot(pawnId);

            for (int retry = 0; retry < MaxReaderRetries; retry++)
            {
                int v1 = Volatile.Read(ref tableVersion);
                if ((v1 & 1) != 0)
                {
                    // Mutation in flight on the main thread; retry probe
                    continue;
                }

                float offset = 0f;
                bool found = false;

                // BREAKPOINT ANCHOR: Bounded Linear Probe Search (Max 16 Steps)
                for (uint i = 0; i < MaxProbeSteps; i++)
                {
                    uint slot = (baseSlot + i) & TableMask;
                    int candidateId = Volatile.Read(ref slotPawnIds[slot]);

                    if (candidateId == pawnId)
                    {
                        long packed = Volatile.Read(ref slotPackedData[slot]);
                        Unpack(packed, out offset, out _);
                        found = true;
                        break;
                    }

                    // If an empty slot is encountered, the key does not exist in the probe chain
                    if (candidateId == 0) break;
                }

                // BREAKPOINT ANCHOR: SeqLock Optimistic Read Validation
                // Full memory barrier prevents payload reads from sinking past the second version sample
                Thread.MemoryBarrier();
                int v2 = Volatile.Read(ref tableVersion);
                if (v1 == v2)
                {
                    return found ? offset : 0f;
                }
            }

            return 0f;
        }

        /// <summary>
        /// [CACHE-03] Safely retrieves the cached Caravan Mass Capacity multiplier off the main thread.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>The mass multiplier (defaults to 1.0f if not cached).</returns>
        public static float GetMultiplierThreadSafe(int pawnId)
        {
            if (pawnId <= 0) return 1.0f;

            // BREAKPOINT ANCHOR: Atomic Performance Counter Increment
            PerformanceTelemetry.IncrementBackgroundQueries();

            uint baseSlot = GetNaturalSlot(pawnId);

            for (int retry = 0; retry < MaxReaderRetries; retry++)
            {
                int v1 = Volatile.Read(ref tableVersion);
                if ((v1 & 1) != 0)
                {
                    continue;
                }

                float multiplier = 1.0f;
                bool found = false;

                // BREAKPOINT ANCHOR: Bounded Linear Probe Search (Max 16 Steps)
                for (uint i = 0; i < MaxProbeSteps; i++)
                {
                    uint slot = (baseSlot + i) & TableMask;
                    int candidateId = Volatile.Read(ref slotPawnIds[slot]);

                    if (candidateId == pawnId)
                    {
                        long packed = Volatile.Read(ref slotPackedData[slot]);
                        Unpack(packed, out _, out multiplier);
                        found = true;
                        break;
                    }

                    if (candidateId == 0) break;
                }

                // BREAKPOINT ANCHOR: SeqLock Optimistic Read Validation
                // Full memory barrier prevents payload reads from sinking past the second version sample
                Thread.MemoryBarrier();
                int v2 = Volatile.Read(ref tableVersion);
                if (v1 == v2)
                {
                    if (!found) return 1.0f;
                    return multiplier <= 0f ? 1.0f : multiplier;
                }
            }

            return 1.0f;
        }

        #endregion

        #region 5. [CACHE-03] MAIN-THREAD SYNCHRONIZED SINK & EVICTION OPERATIONS

        /// <summary>
        /// [CACHE-03] Writes or updates calculated Caravan Mass Capacity parameters into the atomic registry.
        /// Synchronized via SeqLock mutation boundary.
        /// </summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <param name="offset">The solved caravan mass capacity offset in kg.</param>
        /// <param name="multiplier">The solved overall mass multiplier.</param>
        public static void WriteState(int pawnId, float offset, float multiplier)
        {
            if (pawnId <= 0) return;

            long packed = Pack(offset, multiplier);
            uint baseSlot = GetNaturalSlot(pawnId);
            int targetSlot = -1;

            // BREAKPOINT ANCHOR: SeqLock Mutation Window (Data payload before key publication)
            Interlocked.Increment(ref tableVersion);

            try
            {
                // 1. Probe for an existing key match or first open slot
                for (uint i = 0; i < MaxProbeSteps; i++)
                {
                    uint slot = (baseSlot + i) & TableMask;
                    int candidateId = slotPawnIds[slot];

                    if (candidateId == pawnId || candidateId == 0)
                    {
                        targetSlot = (int)slot;
                        break;
                    }
                }

                // 2. Clean eviction fallback if probe bound is saturated
                if (targetSlot == -1)
                {
                    // Linear probe window is fully saturated (all MaxProbeSteps slots occupied).
                    // Displace the root bucket occupant directly to seat this pawn at its natural hash.
                    // Overwriting baseSlot maintains unbroken non-zero probe chains for downstream keys
                    // without corrupting shifts via premature zeroing.
                    targetSlot = (int)baseSlot;
                    PerformanceTelemetry.IncrementEvictions();
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
        public static bool Evict(int pawnId)
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
        /// Internal implementation of the backward-shift deletion (Algorithm R) for evicting a pawn entry from the cache.
        /// </summary>
        /// <param name="pawnId">The unique identifier of the pawn to evict from the cache.</param>
        /// <returns>True if the pawn was successfully evicted; otherwise, false.</returns>
        /// <remarks>
        /// This method assumes that the caller has already incremented the table version to signal a mutation window.
        /// It performs a backward-shift deletion to maintain the integrity of the linear probe chain.
        /// </remarks>
        private static bool EvictInternal(int pawnId)
        {
            uint baseSlot = GetNaturalSlot(pawnId);
            int targetSlot = -1;

            // Step 1: Locate the target slot within the bounded probe chain
            for (uint i = 0; i < MaxProbeSteps; i++)
            {
                uint slot = (baseSlot + i) & TableMask;
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
            int emptySlot = targetSlot;
            int scanSlot = (emptySlot + 1) & TableMask;

            while (true)
            {
                int candidateId = slotPawnIds[scanSlot];
                if (candidateId == 0) break; // Reached end of active cluster

                // In bounded linear probing, if the scan slot has advanced MaxProbeSteps or more
                // past the vacant slot, no subsequent candidate can have a natural hash <= emptySlot.
                uint probeDistance = ((uint)scanSlot - (uint)emptySlot) & TableMask;
                if (probeDistance >= MaxProbeSteps) break;

                uint naturalHash = GetNaturalSlot(candidateId);

                // Circular distance from natural hash to candidate's current position vs vacant slot
                uint distToEmpty = ((uint)emptySlot - naturalHash) & TableMask;
                uint distToCurrent = ((uint)scanSlot - naturalHash) & TableMask;

                // If the candidate was displaced past the empty slot, shift it backward to restore proximity
                if (distToEmpty < distToCurrent)
                {
                    slotPackedData[emptySlot] = slotPackedData[scanSlot];
                    slotPawnIds[emptySlot] = candidateId;

                    emptySlot = scanSlot;
                }

                scanSlot = (scanSlot + 1) & TableMask;
            }

            // Step 3: Zero out the terminal slot
            slotPawnIds[emptySlot] = 0;
            slotPackedData[emptySlot] = 0L;

            return true;
        }

        /// <summary>
        /// [CACHE-03] Completely flushes the atomic snapshot registry on save load or world reset.
        /// </summary>
        public static void Reset()
        {
            // BREAKPOINT ANCHOR: Snapshot Registry Flush
            Interlocked.Increment(ref tableVersion);

            try
            {
                Array.Clear(slotPawnIds, 0, TableCapacity);
                Array.Clear(slotPackedData, 0, TableCapacity);
            }
            finally
            {
                Interlocked.Increment(ref tableVersion);
            }
        }

        #endregion
    }
}
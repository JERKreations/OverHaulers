using System.Runtime.CompilerServices;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-03] CONCURRENT MASS SNAPSHOT REGISTRY (LIVE INSTANCE FACADE)
    /// Static entry point over the single live <see cref="SnapshotTable"/> mapping active pawn IDs to Caravan Mass Capacity (kg)
    /// primitive snapshots. Guarantees atomic, linearizable, zero-allocation reads for RimWorld background pathfinding threads.
    /// All behaviour lives in <see cref="SnapshotTable"/>; this class only fixes the capacity, enables telemetry forwarding and
    /// keeps the original static API stable for the rest of the mod. Resides under Source/Core/Registry/.
    /// </summary>
    /// <remarks>
    /// The full architectural design rationale (zero-lock atomic concurrency, bit-cast struct union, backward-shift deletion with a
    /// SeqLock, multiplicative hashing and zero-contention telemetry) is documented on <see cref="SnapshotTable"/>.
    /// </remarks>
    public static class MassSnapshotCache
    {
        /// <summary>Fixed power-of-two capacity accommodating maximum active simultaneous map populations.</summary>
        public const int TableCapacity = SnapshotTable.DefaultCapacity;

        /// <summary>Fast bitwise modulo mask (TableCapacity - 1).</summary>
        public const int TableMask = TableCapacity - 1;

        /// <summary>Maximum linear probe depth before replacing the root hash bucket.</summary>
        public const int MaxProbeSteps = SnapshotTable.MaxProbeSteps;

        private static readonly SnapshotTable liveTable = new SnapshotTable(TableCapacity, forwardTelemetry: true);

        /// <summary>Exact count of occupied, non-zero slots currently residing in the live table.</summary>
        public static int OccupiedSlotsCount => liveTable.OccupiedSlotsCount;

        /// <summary>
        /// Packs a caravan mass capacity offset (kg) and a multiplier into one 64-bit word.
        /// </summary>
        /// <param name="offset">Offset in kg.</param>
        /// <param name="multiplier">Total physical mass multiplier.</param>
        /// <returns>The packed 64-bit word.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Pack(float offset, float multiplier) => SnapshotTable.Pack(offset, multiplier);

        /// <summary>
        /// Unpacks a 64-bit word produced by <see cref="Pack"/>.
        /// </summary>
        /// <param name="packed">The packed word.</param>
        /// <param name="offset">Extracted offset in kg.</param>
        /// <param name="multiplier">Extracted multiplier.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unpack(long packed, out float offset, out float multiplier) => SnapshotTable.Unpack(packed, out offset, out multiplier);

        /// <summary>Safely retrieves the cached offset (kg) from any thread; 0 when not cached.</summary>
        public static float GetOffsetThreadSafe(int pawnId) => liveTable.GetOffsetThreadSafe(pawnId);

        /// <summary>Safely retrieves the cached multiplier from any thread; 1 when not cached.</summary>
        public static float GetMultiplierThreadSafe(int pawnId) => liveTable.GetMultiplierThreadSafe(pawnId);

        /// <summary>Writes or updates a pawn's snapshot. Main thread only.</summary>
        public static void WriteState(int pawnId, float offset, float multiplier) => liveTable.WriteState(pawnId, offset, multiplier);

        /// <summary>Evicts a pawn's snapshot with backward-shift deletion. Main thread only.</summary>
        public static bool Evict(int pawnId) => liveTable.Evict(pawnId);

        /// <summary>Flushes the live table on save load or world reset. Main thread only.</summary>
        public static void Reset() => liveTable.Reset();
    }
}

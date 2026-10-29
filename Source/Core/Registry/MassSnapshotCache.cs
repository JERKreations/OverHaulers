using System.Runtime.CompilerServices;

namespace OverHaulers
{
    /// <summary>
    /// [CACHE-02] CONCURRENT MASS SNAPSHOT REGISTRY (LIVE INSTANCE FACADE)
    /// Static entry point over the single live <see cref="SnapshotTable"/> mapping active pawn IDs to Caravan Mass Capacity
    /// physical offsets. Guarantees atomic, linearizable, zero-allocation reads for background pathfinding threads.
    /// Resides strictly under Source/Core/Registry/.
    /// </summary>
    public static class MassSnapshotCache
    {
        #region 1. CONSTANTS

        /// <summary>Maximum linear probe depth before replacing the root hash bucket.</summary>
        public const int MaxProbeSteps = SnapshotTable.MaxProbeSteps;

        #endregion

        #region 2. STATIC STATE & BACKING INSTANCE

        private static readonly SnapshotTable liveTable = new SnapshotTable(SnapshotTable.DefaultCapacity, forwardTelemetry: true);

        /// <summary>Tracks whether the atomic snapshot table has been primed during engine bootstrap.</summary>
        private static bool isInitialized = false;

        static MassSnapshotCache()
        {
            // Explicit static constructor removes the CIL 'beforefieldinit' flag,
            // guaranteeing liveTable is only allocated when EnsureInitialized() is invoked.
        }

        #endregion

        #region 3. PROPERTIES & ENCAPSULATED ACCESSORS

        /// <summary>Current power-of-two capacity accommodating active simultaneous map populations.</summary>
        public static int TableCapacity => liveTable.Capacity;

        /// <summary>Fast bitwise modulo mask (TableCapacity - 1).</summary>
        public static int TableMask => liveTable.Capacity - 1;

        /// <summary>Exact count of occupied, non-zero slots currently residing in the live table.</summary>
        public static int OccupiedSlotsCount => liveTable.OccupiedSlotsCount;

        /// <summary>Current load factor percentage in the range 0 to 100.</summary>
        public static float OccupancyPercentage => liveTable.OccupancyPercentage;

        #endregion

        #region 4. LIFECYCLE & INITIALIZATION

        /// <summary>
        /// Explicitly primes and allocates the atomic snapshot table during engine bootstrap.
        /// Idempotent; records initialization telemetry on first execution. Main thread only.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureInitialized()
        {
            if (isInitialized) return;
            isInitialized = true;

            OHLog.Performance.SnapshotTableInitialized(
                TableCapacity,
                dynamic: true,
                SnapshotTable.MinCapacity,
                SnapshotTable.MaxCapacity,
                MaxProbeSteps
            );
        }

        /// <summary>Flushes the live table on save load or world reset. Main thread only.</summary>
        public static void Reset() => liveTable.Reset();

        #endregion

        #region 5. PUBLIC CANONICAL READ & MUTATION API

        /// <summary>Safely retrieves the cached physical offset (kg) from any thread; 0 when not cached.</summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>The physical offset in kg.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetOffsetThreadSafe(int pawnId) => liveTable.GetOffsetThreadSafe(pawnId);

        /// <summary>Attempts to read the cached physical offset (kg) from any thread under optimistic SeqLock validation.</summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <param name="offset">Outputs the cached physical offset in kg.</param>
        /// <returns>True if cached and validated; false on miss or timeout.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadOffset(int pawnId, out float offset) => liveTable.TryReadOffset(pawnId, out offset);

        /// <summary>Writes or updates a pawn's physical offset (kg). Main thread only.</summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <param name="offset">The solved caravan mass capacity offset in kg.</param>
        public static void WriteState(int pawnId, float offset) => liveTable.WriteState(pawnId, offset);

        /// <summary>Evicts a pawn's snapshot with backward-shift deletion. Main thread only.</summary>
        /// <param name="pawnId">The target pawn's unique ID.</param>
        /// <returns>True if evicted; false otherwise.</returns>
        public static bool Evict(int pawnId) => liveTable.Evict(pawnId);

        #endregion
    }
}
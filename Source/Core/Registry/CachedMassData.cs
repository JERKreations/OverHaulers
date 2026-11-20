namespace OverHaulers
{
    #region 1. CACHED MASS DATA CONTAINER

    /// <summary>
    /// [CACHE-01] Stores cached numerical mass capacity metrics for a pawn, including the last calculated offsets,
    /// summary breakdown metrics, monotonic state revision, and combat debounce timestamps.
    /// 100% decoupled from UI presentation models.
    /// Resides under Source/Core/Registry/.
    /// </summary>
    public class CachedMassData
    {
        #region 1A. Instance Fields

        /// <summary>The last calculated mass offset applied to the biological baseline.</summary>
        public float Offset;

        /// <summary>Fully clamped final Caravan Mass Capacity.</summary>
        public float FinalCapacity;

        /// <summary>Total multiplier applied to the biological baseline to obtain the final capacity.</summary>
        public float TotalMultiplier;

        /// <summary>The boost to mass capacity provided by prosthetics.</summary>
        public float ProstheticBoost;

        /// <summary>The reduction in mass capacity due to health deficits and wounds.</summary>
        public float HealthDeficit;

        /// <summary>The contribution to mass capacity from athletic implants and systemic coupling.</summary>
        public float AthleticOffset;

        /// <summary>Engine tick when this cached record was calculated.</summary>
        public int CalculatedTick;

        /// <summary>Timestamp of the last reactive invalidation pass (used for combat burst debounce).</summary>
        public int LastInvalidatedTick;

        /// <summary>The clean biological baseline mass capacity for the pawn.</summary>
        public float BaselineCapacity;

        /// <summary>
        /// Monotonic revision counter incremented on biological/structural invalidations and baseline shifts
        /// (never on routine TTL refreshes), keeping presentation models synchronized with Core state.
        /// </summary>
        public int StateVersion = 1;

        /// <summary>Flag indicating whether the cached evaluation is marked stale and requires recalculation.</summary>
        public bool IsStale;

        #endregion

        #region 1B. Lifecycle & Revision Helpers

        /// <summary>
        /// Advances the revision counter across the positive cyclic range <c>[1 .. int.MaxValue]</c>,
        /// strictly reserving <c>0</c> as the uninitialized/empty snapshot sentinel.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void AdvanceStateVersion()
        {
            StateVersion = (StateVersion == int.MaxValue) ? 1 : StateVersion + 1;
        }

        #endregion
    }

    #endregion

    #region 2. FRESH MASS SNAPSHOT

    /// <summary>
    /// [CACHE-02] Lightweight stack-allocated snapshot returned by Core fast-path queries to unify
    /// offset, final capacity, and revision synchronization across engine bridges and presentation caches.
    /// Resides under Source/Core/Registry/.
    /// </summary>
    public readonly struct FreshMassSnapshot
    {
        #region 2A. Constants & Fallbacks

        /// <summary>Inert default snapshot returned on fast-path misses.</summary>
        public static readonly FreshMassSnapshot Empty = new FreshMassSnapshot(0f, 0f, 0);

        #endregion

        #region 2B. Instance Fields

        /// <summary>The net solved offset from the biological baseline in kilograms.</summary>
        public readonly float Offset;

        /// <summary>Fully clamped final Caravan Mass Capacity in kilograms.</summary>
        public readonly float FinalCapacity;

        /// <summary>Monotonic Core state version at the time of evaluation.</summary>
        public readonly int StateVersion;

        #endregion

        #region 2C. Lifecycle

        /// <summary>
        /// Initializes a new instance of <see cref="FreshMassSnapshot"/>.
        /// </summary>
        public FreshMassSnapshot(float offset, float finalCapacity, int stateVersion)
        {
            Offset = offset;
            FinalCapacity = finalCapacity;
            StateVersion = stateVersion;
        }

        #endregion
    }

    #endregion
}
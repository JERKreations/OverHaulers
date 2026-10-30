namespace OverHaulers
{
    #region 1. CACHED MASS DATA CONTAINER

    /// <summary>
    /// [CACHE-01] Stores cached numerical mass capacity metrics for a pawn, including the last calculated offsets,
    /// summary breakdown metrics, and combat debounce timestamps.
    /// 100% decoupled from UI presentation models.
    /// Resides under Source/Core/Registry/.
    /// </summary>
    public class CachedMassData
    {
        #region 1A. Instance Fields

        /// <summary>The last calculated mass offset applied to the biological baseline in kilograms.</summary>
        public float Offset;

        /// <summary>Fully clamped final Caravan Mass Capacity in kilograms (kg).</summary>
        public float FinalCapacity;

        /// <summary>Total multiplier applied to the biological baseline to obtain the final capacity.</summary>
        public float TotalMultiplier;

        /// <summary>The boost to mass capacity provided by prosthetics in kilograms.</summary>
        public float ProstheticBoost;

        /// <summary>The reduction in mass capacity due to health deficits and wounds in kilograms.</summary>
        public float HealthDeficit;

        /// <summary>The contribution to mass capacity from athletic implants and systemic coupling in kilograms.</summary>
        public float AthleticOffset;

        /// <summary>Engine tick when this cached record was calculated.</summary>
        public int CalculatedTick;

        /// <summary>Timestamp of the last reactive invalidation pass (used for combat burst debounce).</summary>
        public int LastInvalidatedTick;

        /// <summary>The clean biological baseline capacity for the pawn's mass in kilograms.</summary>
        public float BaselineCapacity;

        /// <summary>Flag indicating whether the cached evaluation is marked stale and requires recalculation.</summary>
        public bool IsStale;

        #endregion
    }

    #endregion
}
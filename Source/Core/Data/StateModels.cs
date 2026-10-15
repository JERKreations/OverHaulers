using System.Runtime.InteropServices;
using Verse;

namespace OverHaulers
{
    #region 1. [PASS-02] ATOMIC TOPOLOGICAL BITMASKS

    /// <summary>
    /// [PASS-02] Byte bitmask flags representing anatomical and pathological states in the SoA Flags buffer.
    /// Packed into single-byte memory lanes for SIMD compatibility and zero-stride cache streaming.
    /// Resides under Source/Core/Data/.
    /// </summary>
    public static class PartFlags
    {
        public const byte None = 0;
        public const byte HasAddedPart = 1 << 0;
        public const byte ProstheticIsInherited = 1 << 1;
        public const byte HasAthleticImplant = 1 << 2;
        public const byte IsMissing = 1 << 3;
        public const byte HasDirectDamage = 1 << 4;
        public const byte IsNeutralized = 1 << 5;
    }

    #endregion

    #region 2. COLD PRESENTATION METADATA (Biological Def References)

    /// <summary>
    /// Memory-aligned metadata storing biological HediffDef references per body part for UI presentation.
    /// Free of managed strings and Color structs to keep Core data models decoupled from presentation rendering.
    /// Resides under Source/Core/Data/.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PartStateCold
    {
        public HediffDef ProstheticDef;
        public HediffDef AthleticImplantDef;
        public HediffDef LocalAilmentDef;

        public void Reset()
        {
            ProstheticDef = null;
            AthleticImplantDef = null;
            LocalAilmentDef = null;
        }
    }

    #endregion

    #region 3. CACHED MASS METRICS CONTAINER

    /// <summary>
    /// Stores cached numerical mass capacity metrics for a pawn, including the last calculated offsets,
    /// summary breakdown metrics, and combat debounce timestamps.
    /// 100% decoupled from UI presentation models.
    /// Resides under Source/Core/Data/.
    /// </summary>
    public class CachedMassData
    {
        public float Offset; // The last calculated offset applied to the biological baseline.
        public float FinalCapacity; // Fully clamped final Caravan Mass Capacity in kilograms (kg).
        public float TotalMultiplier; // Total multiplier applied to the biological baseline to obtain the final capacity.
        public float ProstheticBoost; // The boost to mass capacity provided by prosthetics.
        public float HealthDeficit; // The reduction in mass capacity due to health issues.
        public float AthleticOffset; // The contribution to mass capacity from athletic implants.
        public int CalculatedTick;
        public int LastInvalidatedTick; // Timestamp of the last reactive invalidation pass (used for combat burst debounce).
        public float BaselineCapacity; // The biological baseline capacity for the pawn's mass.
        public bool IsStale;
    }

    #endregion

    #region 4. DECOUPLED CAPACITY SNAPSHOT

    /// <summary>
    /// Captures a snapshot of the pawn's biological capacity state at a given moment.
    /// </summary>
    public struct BiologicalCapacitySnapshot
    {
        public float Breathing;
        public float BloodPumping;
        public float Moving;
        public float Manipulation;
        public float Consciousness;
    }

    #endregion

    #region 5. MASS CAPACITY EVALUATION RESULT

    /// <summary>
    /// Readonly value-type evaluation result containing all solved Caravan Mass Capacity metrics.
    /// Encapsulates solver execution completely within Core, eliminating leaky workspace lifetimes.
    /// Resides under Source/Core/Data/.
    /// </summary>
    public readonly struct MassCapacityEvaluationResult
    {
        public readonly float SolvedOffset;
        public readonly float BiologicalBaseline;
        public readonly float FinalCapacity;
        public readonly float TotalMultiplier;
        public readonly float CalculatedProsthetics;
        public readonly float CalculatedHealths;
        public readonly float CalculatedAthletics;
        public readonly bool IsAtSafetyFloor;

        public MassCapacityEvaluationResult(
            float solvedOffset,
            float biologicalBaseline,
            float finalCapacity,
            float totalMultiplier,
            float calculatedProsthetics,
            float calculatedHealths,
            float calculatedAthletics,
            bool isAtSafetyFloor)
        {
            SolvedOffset = solvedOffset;
            BiologicalBaseline = biologicalBaseline;
            FinalCapacity = finalCapacity;
            TotalMultiplier = totalMultiplier;
            CalculatedProsthetics = calculatedProsthetics;
            CalculatedHealths = calculatedHealths;
            CalculatedAthletics = calculatedAthletics;
            IsAtSafetyFloor = isAtSafetyFloor;
        }

        public static readonly MassCapacityEvaluationResult Fallback =
            new MassCapacityEvaluationResult(0f, 0f, 0.01f, 1f, 0f, 0f, 0f, false);
    }

    #endregion
}
using System.Runtime.InteropServices;
using Verse;

namespace OverHaulers
{
    #region 1. TOPOLOGICAL ENUMS & TAXONOMY

    /// <summary>
    /// [TOPO-01] Primary structural classification categories for skeletal body parts.
    /// Governs regional budget assignment and bilateral symmetry grouping.
    /// </summary>
    public enum PartType
    {
        None = 0,
        CorePart = 1,
        ManipulationPart = 2,
        MovingPart = 3,
        HeadPart = 4,
        DualLimb = 5
    }

    /// <summary>
    /// [TOPO-01] Aggregate counts of structural bones, root limbs, dual limbs, and head features.
    /// </summary>
    public struct PartCounts
    {
        public int CoreParts;
        public int ManipulationParts;
        public int MovingParts;
        public int DualLimbs;
        public int HeadParts;

        public int TotalManipulationRoots => ManipulationParts + DualLimbs;
        public int TotalMovingRoots => MovingParts + DualLimbs;
    }

    /// <summary>
    /// [TOPO-03] Biomechanical role assigned to a core trunk part during axial load distribution.
    /// </summary>
    public enum TorsoRole
    {
        Peripheral = 0,
        AnchorBridge = 1,
        AxialColumn = 2
    }

    /// <summary>
    /// [TOPO-04] Which weighting curve a limb part's mass share was computed with (diagnostic-facing).
    /// </summary>
    public enum LimbWeightModel
    {
        NotApplicable = 0,
        GeometricDecay = 1,
        HpRatio = 2,
        ExplicitOverride = 3
    }

    /// <summary>
    /// [TOPO-SUB-02] Transient graph compilation struct tracking limb depth, root indices, and sibling counts.
    /// </summary>
    public struct PartTopologyInfo
    {
        public PartType Type;
        public int ParentIndex;
        public int RootPartIndex;
        public int SiblingCount;
        public int Depth;
        public float UnnormalizedLimbWeight;
        public float StaticWeightFactor;

        public float SafeSiblingDivisor => SiblingCount <= 1 ? 1.0f : (float)SiblingCount;
    }

    #endregion

    #region 2. [PASS-02] ATOMIC TOPOLOGICAL BITMASKS

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

    #region 3. COLD PRESENTATION METADATA (Biological Def References)

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

    #region 4. CACHED MASS METRICS CONTAINER

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

    #region 5. DECOUPLED CAPACITY SNAPSHOT

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

    #region 6. MASS CAPACITY EVALUATION RESULT

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
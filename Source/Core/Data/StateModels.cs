using System.Runtime.InteropServices;
using UnityEngine;

namespace OverHaulers
{
    #region 1. [PASS-02] ATOMIC TOPOLOGICAL BITMASKS

    /// <summary>
    /// [PASS-02] Byte bitmask flags representing anatomical and pathological states in the SoA Flags buffer.
    /// Packed into single-byte memory lanes for SIMD compatibility and zero-stride cache streaming.
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

    #region 2. COLD PRESENTATION METADATA (Managed UI Buffer)

    /// <summary>
    /// Memory-aligned metadata representing the presentation state of a body part.
    /// Populated only during UI inspection passes to ensure mathematical passes remain 100% unmanaged.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PartStateCold
    {
        public string ProstheticName;
        public Color ProstheticColor;

        public string AthleticImplantName;
        public Color AthleticImplantColor;

        public string LocalAilmentName;
        public Color LocalAilmentColor;

        public void Reset()
        {
            ProstheticName = null;
            ProstheticColor = Color.white;

            AthleticImplantName = null;
            AthleticImplantColor = Color.white;

            LocalAilmentName = null;
            LocalAilmentColor = Color.white;
        }
    }

    #endregion

    #region 3. CACHED MASS METRICS CONTAINER

    /// <summary>
    /// Stores cached mass capacity metrics for a pawn, including the last calculated offsets,
    /// detailed UI models, and combat debounce timestamps.
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
        public int FullModelCalculatedTick;
        public int LastInvalidatedTick; // Timestamp of the last reactive invalidation pass (used for combat burst debounce).
        public MassCapacityModel FullModel; // The detailed mass capacity model for the pawn.
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
}
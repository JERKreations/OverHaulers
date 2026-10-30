using System.Runtime.InteropServices;
using Verse;

namespace OverHaulers
{
    #region 1. [PASS-02] ATOMIC TOPOLOGICAL BITMASKS

    /// <summary>
    /// [PASS-02] Byte bitmask flags representing anatomical and pathological states in the SoA Flags buffer.
    /// Packed into single-byte memory lanes for SIMD compatibility and zero-stride cache streaming.
    /// Resides under Source/Core/Workspaces/.
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

    #region 2. COLD PRESENTATION METADATA

    /// <summary>
    /// Memory-aligned metadata storing biological HediffDef references per body part for UI presentation.
    /// Free of managed strings and Color structs to keep Core data models decoupled from presentation rendering.
    /// Resides under Source/Core/Workspaces/.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PartStateCold
    {
        #region 2A. Instance Fields
        public HediffDef ProstheticDef;
        public HediffDef AthleticImplantDef;
        public HediffDef LocalAilmentDef;
        #endregion

        #region 2B. Lifecycle
        /// <summary>
        /// Clears all cold biological definition references.
        /// </summary>
        public void Reset()
        {
            ProstheticDef = null;
            AthleticImplantDef = null;
            LocalAilmentDef = null;
        }
        #endregion
    }

    #endregion
}
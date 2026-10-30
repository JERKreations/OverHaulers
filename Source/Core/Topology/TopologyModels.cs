namespace OverHaulers
{
    #region 1. TOPOLOGICAL TAXONOMY ENUMS

    /// <summary>
    /// [TOPO-01] Primary structural classification categories for skeletal body parts.
    /// Governs regional budget assignment and bilateral symmetry grouping.
    /// Resides under Source/Core/Topology/.
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
    /// [TOPO-03] Biomechanical role assigned to a core trunk part during axial load distribution.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public enum TorsoRole
    {
        Peripheral = 0,
        AnchorBridge = 1,
        AxialColumn = 2
    }

    /// <summary>
    /// [TOPO-04] Which weighting curve a limb part's mass share was computed with (diagnostic-facing).
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public enum LimbWeightModel
    {
        NotApplicable = 0,
        GeometricDecay = 1,
        HpRatio = 2,
        ExplicitOverride = 3
    }

    #endregion

    #region 2. TOPOLOGICAL GRAPH STRUCTS

    /// <summary>
    /// [TOPO-01] Aggregate counts of structural bones, root limbs, dual limbs, and head features.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public struct PartCounts
    {
        #region 2A. Instance Fields
        public int CoreParts;
        public int ManipulationParts;
        public int MovingParts;
        public int DualLimbs;
        public int HeadParts;
        #endregion

        #region 2B. Encapsulated Accessors
        public int TotalManipulationRoots => ManipulationParts + DualLimbs;
        public int TotalMovingRoots => MovingParts + DualLimbs;
        #endregion
    }

    /// <summary>
    /// [TOPO-SUB-02] Transient graph compilation struct tracking limb depth, root indices, and sibling counts.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public struct PartTopologyInfo
    {
        #region 2A. Instance Fields
        public PartType Type;
        public int ParentIndex;
        public int RootPartIndex;
        public int SiblingCount;
        public int Depth;
        public float UnnormalizedLimbWeight;
        public float StaticWeightFactor;
        #endregion

        #region 2B. Encapsulated Accessors
        public float SafeSiblingDivisor => SiblingCount <= 1 ? 1.0f : (float)SiblingCount;
        #endregion
    }

    #endregion
}
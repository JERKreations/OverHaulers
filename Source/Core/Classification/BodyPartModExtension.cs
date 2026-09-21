using Verse;

namespace OverHaulers
{
    /// <summary>
    /// XML DefModExtension allowing modders to explicitly override body part classifications,
    /// axial load roles, relative weight budgets, or limb depth decay for custom alien races and creatures.
    /// Resides under Source/Core/Classification/.
    /// </summary>
    public class BodyPartModExtension : DefModExtension
    {
        #region 1. CLASSIFICATION & ROLE OVERRIDES

        /// <summary>
        /// Explicit anatomical classification override (CorePart, ManipulationPart, MovingPart, HeadPart, None).
        /// Default: None (defers to standard tag and ancestry heuristics).
        /// </summary>
        public PartType partType = PartType.None;

        /// <summary>
        /// Explicit torso load-bearing role (AxialColumn, AnchorBridge, Peripheral).
        /// Set to AxialColumn to treat custom spine/carapace bones as the primary vertical load-bearing column.
        /// </summary>
        public TorsoRole torsoRole = TorsoRole.Peripheral;

        /// <summary>
        /// Explicitly treats this part as a metabolic organ, bypassing anatomical mass budget calculations.
        /// </summary>
        public bool isOrgan = false;

        #endregion

        #region 2. WEIGHT & DECAY OVERRIDES

        /// <summary>
        /// Explicit relative weight multiplier for Torso/Core bones.
        /// Set >= 0.0f to override the automated HitPoint density calculation.
        /// </summary>
        public float relativeTorsoWeight = -1f;

        /// <summary>
        /// Explicit relative weight multiplier for Limb (Arm/Leg) segments.
        /// Set >= 0.0f to override automated depth decay.
        /// </summary>
        public float relativeLimbWeight = -1f;

        /// <summary>
        /// If true, exempts this limb segment and its children from geometric depth decay (Decay^Depth).
        /// Ideal for uniform hydraulic pistons, tentacles, or non-tapering alien appendages.
        /// </summary>
        public bool ignoreLimbDepthDecay = false;

        #endregion
    }
}
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace OverHaulers
{
    #region 1. [TOPO-03] IMMUTABLE SPECIES TOPOLOGY TEMPLATE

    /// <summary>
    /// [TOPO-03] Immutable, precompiled structural topology template for a species <see cref="BodyDef"/>.
    /// Stores flat contiguous primitive arrays, 1D ancestral jump matrices, and flat child buffer spans.
    /// Eliminates all jagged arrays (int[][]) for maximum cache locality and lock-free thread safety.
    /// Resides under Source/Core/Topology/.
    /// </summary>
    public class SpeciesTopologyTemplate
    {
        #region 1. TOPOLOGY METADATA & CONSTANTS

        /// <summary>Number of PartType enum slots (0..5) used as the stride multiplier for 1D jump matrix calculations.</summary>
        public const int PartTypeStride = 6;

        /// <summary>Total count of body parts defined in the species BodyDef.</summary>
        public int PartCount { get; }

        /// <summary>Direct pointer to the root core BodyPartRecord (e.g. Torso, Shell, Mechanical Thorax).</summary>
        public BodyPartRecord CorePart { get; }

        /// <summary>Direct pointer to the root cranial BodyPartRecord (e.g. Head, Insect Head, Mechanical Head).</summary>
        public BodyPartRecord HeadPart { get; set; }

        /// <summary>Primary BodyPartDef for manipulation limbs (e.g. Shoulder/Arm, Wing, Blade, Tentacle).</summary>
        public BodyPartDef PrimaryManipulationDef { get; set; }

        /// <summary>Primary BodyPartDef for locomotion limbs (e.g. Leg, Mechanical Leg, Hoof, Tread).</summary>
        public BodyPartDef PrimaryMovingDef { get; set; }

        /// <summary>Primary BodyPartDef for dual-purpose limbs (e.g. Insect Leg, Paw, Front Leg).</summary>
        public BodyPartDef PrimaryDualDef { get; set; }

        /// <summary>Aggregate counts of structural bones, root limbs, dual limbs, and head features.</summary>
        public PartCounts Counts { get; set; }

        #endregion

        #region 2. FLAT TOPOLOGICAL VECTORS & 1D JUMP STRIDE MATRICES

        /// <summary>Direct O(1) dictionary mapping a live BodyPartRecord pointer to its precompiled slot index.</summary>
        public Dictionary<BodyPartRecord, int> PartToIndex { get; } = new Dictionary<BodyPartRecord, int>();

        /// <summary>Flat indexed array of live BodyPartRecord pointers matching BodyDef.AllParts order.</summary>
        public BodyPartRecord[] IndexedParts { get; }

        /// <summary>Part slot indices pre-sorted parent-before-child (topological depth order) for single-pass forward sweeps.</summary>
        public int[] DepthSortedIndices { get; }

        /// <summary>
        /// Part slot indices pre-sorted in canonical top-down anatomical order:
        /// Head -> Neck/Torso/Spine -> Organs -> Arms -> Legs -> Dual Limbs.
        /// </summary>
        public int[] CanonicalDisplayIndices { get; set; }

        /// <summary>Precompiled anatomical classification category per part index.</summary>
        public PartType[] PartTypes { get; }

        /// <summary>Parent slot index per part index (-1 for root).</summary>
        public int[] ParentIndices { get; }

        /// <summary>
        /// Flat 1D jump matrix of size (PartCount * 6) mapping (partIndex * 6 + targetPartType) 
        /// to the slot index of the nearest ancestor of that category (-1 if none).
        /// </summary>
        public int[] NearestAncestorMatrix { get; set; }

        /// <summary>
        /// Flat array mapping partIndex to the slot index of the nearest structural non-organ ancestor.
        /// </summary>
        public int[] NearestStructuralAncestor { get; set; }

        /// <summary>Flat contiguous buffer storing all child part slot indices across the entire body.</summary>
        public int[] ChildBuffer { get; set; }

        /// <summary>Starting offset into <see cref="ChildBuffer"/> for each part index.</summary>
        public int[] ChildOffsets { get; set; }

        /// <summary>Total number of direct child parts for each part index.</summary>
        public int[] ChildCounts { get; set; }

        #endregion

        #region 3. PARALLEL 3-CHANNEL LOAD-BEARING WEIGHT VECTORS

        /// <summary>
        /// Static budget share (0.0 to 1.0) contributed by this part toward the Torso/Core regional budget.
        /// </summary>
        public float[] WeightCore { get; }

        /// <summary>
        /// Static budget share (0.0 to 1.0) contributed by this part toward the Manipulation regional budget.
        /// </summary>
        public float[] WeightManipulation { get; }

        /// <summary>
        /// Static budget share (0.0 to 1.0) contributed by this part toward the Locomotion/Moving regional budget.
        /// </summary>
        public float[] WeightMoving { get; }

        /// <summary>
        /// Unified combined weight factor across active channels (used for diagnostics and X-Ray previews).
        /// </summary>
        public float[] StaticWeightFactors { get; }

        #endregion

        #region 3B. DIAGNOSTIC GROUND-TRUTH & WEIGHT-MODEL METADATA

        /// <summary>Raw BodyPartDef.hitPoints per part slot (ground-truth vanilla data, for external diagnostics).</summary>
        public int[] HitPoints { get; }

        /// <summary>Comma-joined BodyPartTagDef.defName list per part slot (empty string if untagged).</summary>
        public string[] TagsDisplay { get; }

        /// <summary>Comma-joined BodyPartGroupDef.defName list per part slot (empty string if ungrouped).</summary>
        public string[] GroupsDisplay { get; }

        /// <summary>Same-type unforked chain depth from the limb branch root (0 for non-limb parts).</summary>
        public int[] Depth { get; }

        /// <summary>Sibling count sharing this part's (Type, RootPartIndex, Depth) bucket (1 for non-limb parts).</summary>
        public int[] SiblingCount { get; }

        /// <summary>Slot index of this limb branch's root part (-1 for non-limb parts).</summary>
        public int[] RootPartIndex { get; }

        /// <summary>Which weighting curve was actually used to compute this part's limb mass share.</summary>
        public LimbWeightModel[] WeightModels { get; }

        /// <summary>True if excluded from mass budget as a metabolic/vital organ (ModExtension.isOrgan or a vital capacity source tag).</summary>
        public bool[] IsOrgan { get; }

        /// <summary>
        /// True if this part was classified as a limb type by tag/ancestry but reassigned back to CorePart by the
        /// anchor-bridge heuristic (e.g. Clavicle-like connective bones). Flags the fragile reclassification path
        /// for audit visibility.
        /// </summary>
        public bool[] WasReclaimedAsAnchor { get; }

        #endregion

        #region 4. CONSTRUCTOR & INITIALIZATION

        /// <summary>
        /// Instantiates and allocates flat contiguous arrays for a species topology template matching the given BodyDef.
        /// </summary>
        /// <param name="bodyDef">The target species BodyDef to blueprint.</param>
        /// <remarks>
        /// This constructor precomputes and allocates all necessary arrays for efficient topology queries.
        /// </remarks>
        public SpeciesTopologyTemplate(BodyDef bodyDef)
        {
            int currentPartsCount = bodyDef.AllParts.Count;
            int currentMax;
            
            // Atomic CAS loop: updates the global high-water mark of body part counts across all loaded species.
            // This maximum is used by WorkspacePool to pre-size thread-local Structure-of-Arrays buffers without runtime reallocation.
            do
            {
                currentMax = TopologyLayoutCompiler.globalMaxPartCount;
                if (currentPartsCount <= currentMax) break;
            }
            // Attempt to update the global maximum part count if the current body has more parts.
            while (System.Threading.Interlocked.CompareExchange(
                ref TopologyLayoutCompiler.globalMaxPartCount, 
                currentPartsCount, 
                currentMax) != currentMax);

            // Update the global maximum part body definition name if the current body has the new maximum part count.
            if (TopologyLayoutCompiler.globalMaxPartCount == currentPartsCount)
            {
                TopologyLayoutCompiler.globalMaxPartBodyDefName = bodyDef.defName ?? "Unknown";
            }

            PartCount = bodyDef.AllParts.Count;
            CorePart = bodyDef.corePart;

            IndexedParts = new BodyPartRecord[PartCount];
            DepthSortedIndices = new int[PartCount];
            CanonicalDisplayIndices = new int[PartCount];
            PartTypes = new PartType[PartCount];
            ParentIndices = new int[PartCount];

            NearestAncestorMatrix = new int[PartCount * PartTypeStride];
            NearestStructuralAncestor = new int[PartCount];
            ChildOffsets = new int[PartCount];
            ChildCounts = new int[PartCount];

            WeightCore = new float[PartCount];
            WeightManipulation = new float[PartCount];
            WeightMoving = new float[PartCount];
            StaticWeightFactors = new float[PartCount];

            HitPoints = new int[PartCount];
            TagsDisplay = new string[PartCount];
            GroupsDisplay = new string[PartCount];
            Depth = new int[PartCount];
            SiblingCount = new int[PartCount];
            RootPartIndex = new int[PartCount];
            WeightModels = new LimbWeightModel[PartCount];
            IsOrgan = new bool[PartCount];
            WasReclaimedAsAnchor = new bool[PartCount];

            // Initialize part indices and related metadata.
            for (int i = 0; i < PartCount; i++)
            {
                IndexedParts[i] = bodyDef.AllParts[i];
                PartToIndex[bodyDef.AllParts[i]] = i;

                BodyPartRecord part = bodyDef.AllParts[i];
                HitPoints[i] = part.def != null ? part.def.hitPoints : 0;
                TagsDisplay[i] = (part.def?.tags != null && part.def.tags.Count > 0)
                    ? string.Join(", ", part.def.tags.ConvertAll(t => t.defName))
                    : string.Empty;
                GroupsDisplay[i] = (part.groups != null && part.groups.Count > 0)
                    ? string.Join(", ", part.groups.ConvertAll(g => g.defName))
                    : string.Empty;
                RootPartIndex[i] = -1;
                SiblingCount[i] = 1;
            }
        }

        #endregion

        #region 5. O(1) FLATTENED LOOKUP HELPERS

        /// <summary>
        /// Resolves the precompiled slot index for a live <see cref="BodyPartRecord"/> in O(1) time.
        /// </summary>
        /// <param name="part">The body part record to resolve.</param>
        /// <returns>The precompiled slot index of the part, or -1 if not found.</returns>
        /// <remarks>
        /// This method performs the lookup in O(1) time using the precompiled PartToIndex dictionary.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetPartIndex(BodyPartRecord part)
        {
            if (part == null) return -1;
            return PartToIndex.TryGetValue(part, out int index) ? index : -1;
        }

        /// <summary>
        /// Retrieves the slot index of the nearest ancestor belonging to a specific anatomical category
        /// in O(1) time via flat 1D stride index arithmetic.
        /// </summary>
        /// <param name="partIndex">The target part's slot index.</param>
        /// <param name="targetType">The anatomical category to locate in the ancestral chain.</param>
        /// <returns>The slot index of the ancestor, or -1 if none exists.</returns>
        /// <remarks>
        /// This method performs the lookup in O(1) time using the precompiled NearestAncestorMatrix.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetNearestAncestor(int partIndex, PartType targetType)
        {
            if (NearestAncestorMatrix == null || partIndex < 0 || partIndex >= PartCount) return -1;
            return NearestAncestorMatrix[(partIndex * PartTypeStride) + (int)targetType];
        }

        /// <summary>
        /// Retrieves the direct child slot index at a specific child offset for a parent part.
        /// </summary>
        /// <param name="parentIndex">The parent part's slot index.</param>
        /// <param name="childSlot">The child index offset (0 to ChildCounts[parentIndex] - 1).</param>
        /// <returns>The slot index of the child part.</returns>
        /// <remarks>
        /// This method performs the lookup in O(1) time using the precompiled ChildBuffer and ChildOffsets arrays.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetChildIndex(int parentIndex, int childSlot)
        {
            if (ChildBuffer == null || parentIndex < 0 || parentIndex >= PartCount) return -1;
            return ChildBuffer[ChildOffsets[parentIndex] + childSlot];
        }

        /// <summary>
        /// Resolves the precompiled PartType category for a body part in O(1) time.
        /// </summary>
        /// <param name="part">The body part to query.</param>
        /// <returns>The PartType of the body part, or PartType.None if not found.</returns>
        /// <remarks>
        /// This method performs the lookup in O(1) time using the precompiled PartTypes array.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PartType GetPartType(BodyPartRecord part)
        {
            int idx = GetPartIndex(part);
            return idx >= 0 ? PartTypes[idx] : PartType.None;
        }

        /// <summary>
        /// True if the part is a structural limb (Manipulation, Moving, or Dual).
        /// Agnostic to modded alien races and untagged intermediate bones.
        /// </summary>
        /// <param name="part">The body part to check.</param>
        /// <returns>True if the body part is a structural limb; otherwise, false.</returns>
        /// <remarks>
        /// This method relies on the precompiled PartType and RootPartIndex arrays for efficient lookup.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsLimb(BodyPartRecord part)
        {
            int idx = GetPartIndex(part);
            if (idx < 0) return false;
            PartType t = PartTypes[idx];
            return t == PartType.ManipulationPart || t == PartType.MovingPart || t == PartType.DualLimb;
        }

        /// <summary>
        /// Checks if a part belongs to a specific limb branch root in O(1) time.
        /// </summary>
        /// <param name="part">The body part to check.</param>
        /// <param name="limbRoot">The root of the limb branch.</param>
        /// <returns>True if the body part is a descendant of the specified limb root; otherwise, false.</returns>
        /// <remarks>
        /// The RootPartIndex array is precomputed to allow this check to be done in constant time using precompiled topology data.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDescendantOfLimbRoot(BodyPartRecord part, BodyPartRecord limbRoot)
        {
            int partIdx = GetPartIndex(part);
            int rootIdx = GetPartIndex(limbRoot);
            if (partIdx < 0 || rootIdx < 0) return false;
            return RootPartIndex[partIdx] == rootIdx;
        }

        #endregion
    }

    #endregion
}
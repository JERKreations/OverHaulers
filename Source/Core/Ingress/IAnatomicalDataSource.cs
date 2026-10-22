using Verse;

namespace OverHaulers
{
    #region 1. [PASS-01] DATA SOURCE CONTRACT

    /// <summary>
    /// [PASS-01] Ingress contract isolating mathematical solvers and topology compilers
    /// from live RimWorld Pawn entity reference mutations.
    /// Implemented by stack-allocated struct wrappers (PawnAnatomicalSource, SandboxAnatomicalSource, BoxedDataSourceWrapper).
    /// Resides under Source/Core/Ingress/.
    /// </summary>
    public interface IAnatomicalDataSource
    {
        #region 1A. METADATA & TOPOLOGY PROPERTIES

        /// <summary>Unique entity ID (positive integer for pawns).</summary>
        int EntityId { get; }

        /// <summary>Human-readable label for diagnostics and UI presentation.</summary>
        string EntityLabel { get; }

        /// <summary>Target species BodyDef defining the anatomical structure.</summary>
        BodyDef BodyDef { get; }

        /// <summary>Physical biological body size scalar.</summary>
        float BaseBodySize { get; }

        /// <summary>True if the data source contains valid biological state data for solving.</summary>
        bool IsValidBiologicalState { get; }

        #endregion

        #region 1B. INGRESS & CAPACITY METHODS

        /// <summary>
        /// Reads the biological performance level of a target pawn capacity (0.0 to 1.0+).
        /// </summary>
        /// <param name="capacity">The pawn capacity to evaluate.</param>
        /// <returns>The biological performance level of the specified pawn capacity, ranging from 0.0 to 1.0+.</returns>
        float GetCapacityLevel(PawnCapacityDef capacity);

        /// <summary>
        /// Resolves the intrinsic biological or prosthetic efficiency rating of a specific body part.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <param name="defaultRatedEfficiency">The fallback rated XML efficiency if unmodded.</param>
        /// <returns>The uninjured efficiency rating (e.g. 0.25 for training, 1.25 for standard bionic, 1.35 for masterwork).</returns>
        float GetPartEfficiency(BodyPartRecord part, float defaultRatedEfficiency);

        /// <summary>
        /// Translates pathology, wounds, and prosthetic modifications into workspace arrays.
        /// </summary>
        /// <param name="workspace">The anatomical workspace to populate with pathology data.</param>
        /// <param name="populateColdMetadata">Indicates whether UI-related properties should be compiled during ingress.</param>
        void IngressPathology(AnatomicalWorkspace workspace, bool populateColdMetadata);

        /// <summary>
        /// Resolves the clean baseline mass capacity in kg before physiological modifications.
        /// </summary>
        float ResolveBaselineCapacity();

        #endregion
    }

    #endregion

    #region 2. [ING-02] ZERO-ALLOCATION STRUCT ADAPTER

    /// <summary>
    /// [ING-02] Zero-allocation stack wrapper adapting an <see cref="IAnatomicalDataSource"/> reference
    /// into the unboxed struct solver pipeline. Declared as a namespace-level struct to ensure strict C# 7.3 compliance.
    /// Resides under Source/Core/Ingress/.
    /// </summary>
    internal readonly struct BoxedDataSourceWrapper : IAnatomicalDataSource
    {
        private readonly IAnatomicalDataSource inner;

        public BoxedDataSourceWrapper(IAnatomicalDataSource inner)
        {
            this.inner = inner;
        }

        public int EntityId => inner != null ? inner.EntityId : 0;
        public string EntityLabel => inner != null ? inner.EntityLabel : "Unknown";
        public BodyDef BodyDef => inner != null ? inner.BodyDef : null;
        public float BaseBodySize => inner != null ? inner.BaseBodySize : 1.0f;
        public bool IsValidBiologicalState => inner != null && inner.IsValidBiologicalState;

        public float GetCapacityLevel(PawnCapacityDef capacity)
        {
            return inner != null ? inner.GetCapacityLevel(capacity) : 1.0f;
        }

        public float GetPartEfficiency(BodyPartRecord part, float defaultRatedEfficiency)
        {
            return inner != null ? inner.GetPartEfficiency(part, defaultRatedEfficiency) : defaultRatedEfficiency;
        }

        public void IngressPathology(AnatomicalWorkspace workspace, bool populateColdMetadata)
        {
            inner?.IngressPathology(workspace, populateColdMetadata);
        }

        public float ResolveBaselineCapacity()
        {
            return inner != null ? inner.ResolveBaselineCapacity() : 0f;
        }
    }

    #endregion
}
using Verse;

namespace OverHaulers
{
    #region 1. [PASS-01] DATA SOURCE CONTRACT

    /// <summary>
    /// [PASS-01] Ingress contract isolating mathematical solvers and topology compilers
    /// from live RimWorld Pawn entity reference mutations.
    /// Implemented by struct wrappers (PawnAnatomicalSource, SandboxAnatomicalSource).
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
        /// Resolves the final current biological or prosthetic efficiency rating of a specific body part.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <param name="defaultRatedEfficiency">The fallback rated XML efficiency if unmodded.</param>
        /// <returns>The final native efficiency, including part health and local efficiency modifiers.</returns>
        float GetPartEfficiency(BodyPartRecord part, float defaultRatedEfficiency);

        /// <summary>
        /// Resolves the maximum health points for a body part on the target entity.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <returns>The maximum health points for the specified body part.</returns>
        float GetPartMaxHealth(BodyPartRecord part);

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
}
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [PASS-01] Ingress contract isolating mathematical solvers and topology compilers
    /// from live RimWorld Pawn entity reference mutations.
    /// Implemented by stack-allocated struct wrappers (PawnAnatomicalSource).
    /// </summary>
    public interface IAnatomicalDataSource
    {
        #region 1. METADATA & TOPOLOGY PROPERTIES

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

        #region 2. INGRESS & CAPACITY METHODS

        /// <summary>
        /// Reads the biological performance level of a target pawn capacity (0.0 to 1.0+).
        /// </summary>
        /// <param name="capacity">The pawn capacity to evaluate.</param>
        /// <returns>The biological performance level of the specified pawn capacity, ranging from 0.0 to 1.0+.</returns>
        float GetCapacityLevel(PawnCapacityDef capacity);

        /// <summary>
        /// Translates pathology, wounds, and prosthetic modifications into workspace arrays.
        /// </summary>
        /// <param name="workspace">The anatomical workspace to populate with pathology data.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI-related properties should be compiled during ingress.</param>
        void IngressPathology(AnatomicalWorkspace workspace, bool shouldCompileUIProperties);

        /// <summary>
        /// Resolves the clean baseline mass capacity in kg before physiological modifications.
        /// </summary>
        float ResolveBaselineCapacity();

        #endregion
    }
}
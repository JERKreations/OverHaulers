using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [PASS-01] Live pawn anatomical source translating live Pawn health trackers into the solver workspace.
    /// Stack-allocated struct wrapper with 0 bytes GC allocation overhead.
    /// Resides under Source/Core/Ingress/.
    /// </summary>
    public readonly struct PawnAnatomicalSource : IAnatomicalDataSource
    {
        #region 1. FIELDS & BACKING STATE

        private readonly Pawn pawn;

        #endregion

        #region 2. LIFECYCLE: CONSTRUCTOR

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PawnAnatomicalSource(Pawn pawn)
        {
            this.pawn = pawn;
        }

        #endregion

        #region 3. PROPERTIES & ENCAPSULATED ACCESSORS

        /// <summary>
        /// Direct access to the underlying pawn entity instance.
        /// </summary>
        public Pawn RawPawn
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => pawn;
        }

        /// <summary>
        /// Unique ThingIDNumber of the pawn, used for registry caching and hashing.
        /// </summary>
        public int EntityId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => pawn?.thingIDNumber ?? 0;
        }

        /// <summary>
        /// Retrieves the body definition of the pawn, if available.
        /// </summary>
        public BodyDef BodyDef
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => pawn?.RaceProps?.body;
        }

        /// <summary>
        /// Retrieves the safe base body size of the pawn, accounting for potential broken third-party getters.
        /// </summary>
        public float BaseBodySize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MedicalClassifier.GetSafeBodySize(pawn);
        }

        /// <summary>
        /// Determines whether the pawn is in a valid biological state. Defensively checks for null reference.
        /// </summary>
        public bool IsValidBiologicalState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => pawn != null && pawn.HasValidBiologicalState();
        }

        /// <summary>
        /// Label identifier for diagnostic and logging purposes.
        /// Note: Not inlined due to JIT exception-handling block constraints.
        /// </summary>
        public string EntityLabel
        {
            get
            {
                if (pawn == null) return "Unknown";

                // Fast-path: If this is our headless sandbox dummy, avoid triggering third-party label patches
                if (pawn.thingIDNumber == HeadlessPawnShell.HeadlessPawnThingId)
                {
                    return pawn.def?.label ?? "SandboxPawn";
                }

                try
                {
                    // Attempt to retrieve the short-capped label for the pawn, handling any potential exceptions.
                    return pawn.LabelShortCap.ToString();
                }
                catch
                {
                    return pawn.def?.label ?? "Pawn";
                }
            }
        }

        #endregion

        #region 4. CANONICAL INGRESS & BASELINE RESOLUTION API

        /// <summary>
        /// Retrieves the biological performance level of the specified pawn capacity.
        /// </summary>
        /// <param name="capacity">The pawn capacity to evaluate.</param>
        /// <returns>The biological performance level of the specified pawn capacity, ranging from 0.0 to 1.0+.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCapacityLevel(PawnCapacityDef capacity)
        {
            return MedicalClassifier.GetCapacityLevel(pawn, capacity);
        }

        /// <summary>
        /// Resolves the intrinsic biological or prosthetic efficiency of a specific body part on the live pawn.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <param name="defaultRatedEfficiency">The fallback rated XML efficiency if unmodded.</param>
        /// <returns>The uninjured runtime efficiency rating.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetPartEfficiency(BodyPartRecord part, float defaultRatedEfficiency)
        {
            return MedicalClassifier.GetSafePartEfficiency(pawn, part, defaultRatedEfficiency);
        }

        /// <summary>
        /// Resolves the maximum health points for a body part on the live pawn.
        /// </summary>
        /// <param name="part">The body part record to evaluate.</param>
        /// <returns>The maximum health points of the body part.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetPartMaxHealth(BodyPartRecord part)
        {
            if (part == null || pawn == null) return 1.0f;
            return part.def.GetMaxHealth(pawn);
        }

        /// <summary>
        /// Populates the specified anatomical workspace with the pawn's pathology data, including wounds, diseases, and prosthetic modifications.
        /// </summary>
        /// <param name="workspace">The anatomical workspace to populate with pathology data.</param>
        /// <param name="populateColdMetadata">Indicates whether UI-related properties should be compiled during ingress.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IngressPathology(AnatomicalWorkspace workspace, bool populateColdMetadata)
        {
            List<Hediff> safeHediffs = pawn?.health?.hediffSet?.hediffs;
            if (safeHediffs != null)
            {
                AnatomicalIngressSolver.IngressLivePawnPathology(pawn, safeHediffs, workspace, populateColdMetadata, this);
            }
        }

        /// <summary>
        /// Resolves the clean baseline mass capacity in kg before physiological modifications.
        /// Universally queries SpeciesBaselineCalibration.
        /// </summary>
        /// <returns>The baseline mass capacity in kilograms before any physiological modifications are applied.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ResolveBaselineCapacity()
        {
            if (pawn == null) return 0f;
            return SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
        }

        #endregion
    }
}
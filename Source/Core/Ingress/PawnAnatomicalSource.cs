using System.Collections.Generic;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [PASS-01] Live pawn anatomical source translating live Pawn health trackers into the solver workspace.
    /// Stack-allocated struct wrapper with 0 bytes GC allocation overhead.
    /// </summary>
    public readonly struct PawnAnatomicalSource : IAnatomicalDataSource
    {
        #region 1. FIELDS & CONSTRUCTOR

        private readonly Pawn pawn;

        public PawnAnatomicalSource(Pawn pawn)
        {
            this.pawn = pawn;
        }

        public Pawn RawPawn => pawn;

        #endregion

        #region 2. PROPERTIES

        public int EntityId => pawn?.thingIDNumber ?? 0;

        public string EntityLabel
        {
            get
            {
                if (pawn == null) return "Unknown";
                
                // Fast-path: If this is our headless sandbox dummy, avoid triggering third-party label patches
                if (pawn.thingIDNumber == SandboxPawnHarness.SandboxPawnThingId)
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

        // Retrieves the body definition of the pawn, if available.
        public BodyDef BodyDef => pawn?.RaceProps?.body;

        // Retrieves the safe base body size of the pawn, accounting for potential broken body size getters.
        public float BaseBodySize => MedicalClassifier.GetSafeBodySize(pawn);

        // Determines whether the pawn is in a valid biological state. Defensively checks for null pawn reference.
        public bool IsValidBiologicalState => pawn != null && pawn.HasValidBiologicalState();

        #endregion

        #region 3. INGRESS & BASELINE RESOLUTION

        /// <summary>
        /// Retrieves the biological performance level of the specified pawn capacity.
        /// </summary>
        /// <param name="capacity">The pawn capacity to evaluate.</param>
        /// <returns>The biological performance level of the specified pawn capacity, ranging from 0.0 to 1.0+.</returns>
        public float GetCapacityLevel(PawnCapacityDef capacity)
        {
            return MedicalClassifier.GetCapacityLevel(pawn, capacity);
        }

        /// <summary>
        /// Populates the specified anatomical workspace with the pawn's pathology data, including wounds, diseases, and prosthetic modifications.
        /// </summary>
        /// <param name="workspace">The anatomical workspace to populate with pathology data.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI-related properties should be compiled during ingress.</param>
        public void IngressPathology(AnatomicalWorkspace workspace, bool shouldCompileUIProperties)
        {
            List<Hediff> safeHediffs = pawn?.health?.hediffSet?.hediffs;
            if (safeHediffs != null)
            {
                AnatomicalIngressSolver.IngressLivePawnPathology(pawn, safeHediffs, workspace, shouldCompileUIProperties);
            }
        }

        /// <summary>
        /// Resolves the clean baseline mass capacity in kg before physiological modifications.
        /// </summary>
        /// <returns>The baseline mass capacity in kilograms before any physiological modifications are applied.</returns>
        public float ResolveBaselineCapacity()
        {
            if (pawn == null) return 0f;

            if (IntegrationPipeline.ActiveDriver != null)
            {
                return IntegrationPipeline.ActiveDriver.ResolveDriverBaseline(pawn);
            }

            return SpeciesBaselineCalibration.ResolveNativeBaseline(pawn);
        }

        #endregion
    }
}
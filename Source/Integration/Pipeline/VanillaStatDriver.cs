using System;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-03] STANDALONE DIRECT HARMONY DRIVER

    /// <summary>
    /// A concrete implementation of the IPipelineDriver interface that provides a standalone direct Harmony driver for managing mass capacity stats
    /// within the Over Haulers mod.
    /// </summary>
    public class VanillaStatDriver : IPipelineDriver
    {
        #region FIELDS & CONSTRUCTOR

        public string DriverIdentifier => "Standalone";
        public bool IsStatDriven => true;
        public StatDef ActiveMassCapacityStat { get; private set; }
        public string UnitSuffix { get; private set; }

        /// <summary>
        /// Initializes a new instance of the VanillaStatDriver class with the specified unit suffix.
        /// </summary>
        /// <param name="unitSuffix">The unit suffix to be used for displaying mass capacity values.</param>
        public VanillaStatDriver(string unitSuffix)
        {
            UnitSuffix = unitSuffix;
        }

        #endregion

        #region INITIALIZATION & CLEANUP

        /// <summary>
        /// Initializes the VanillaStatDriver, setting up the active mass capacity stat and logging the direct fallback activation.
        /// </summary>
        /// <param name="harmony">The Harmony instance used for patching.</param>
        public void Initialize(Harmony harmony)
        {
            ActiveMassCapacityStat = GetOrCreateStandaloneStat();
            if (ActiveMassCapacityStat != null)
            {
                ActiveMassCapacityStat.showOnPawns = true;
            }
            OHLog.Integration.DirectFallbackActive(UnitSuffix);
        }

        /// <summary>
        /// Cleans up the VanillaStatDriver, resetting the active mass capacity stat's visibility on pawns.
        /// </summary>
        public void Cleanup()
        {
            if (ActiveMassCapacityStat != null)
            {
                ActiveMassCapacityStat.showOnPawns = false;
            }
        }

        /// <summary>
        /// Dynamically registers the 'OverHaulers_CaravanMassCapacity' StatDef into DefDatabase at runtime.
        /// Binds its workerClass to <see cref="MassCapacityStatWorker"/> to give vanilla pawns a full
        /// InfoCard breakdown that vanilla RimWorld otherwise completely lacks.
        /// </summary>
        /// <returns>The standalone mass capacity stat, or null if it could not be created.</returns>
        private StatDef GetOrCreateStandaloneStat()
        {
            StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail("OverHaulers_CaravanMassCapacity");
            if (stat != null) return stat;

            try
            {
                // Create a new standalone mass capacity stat if it does not already exist.
                // workerClass binds directly to MassCapacityStatWorker to drive InfoCard breakdowns and hyperlinks.
                stat = new StatDef
                {
                    defName = "OverHaulers_CaravanMassCapacity",
                    label = "OverHaulers_StatLabel".Translate().ToString(),
                    description = "OverHaulers_StatDesc".Translate().ToString(),
                    category = DefDatabase<StatCategoryDef>.GetNamedSilentFail("BasicsPawn"),
                    displayPriorityInCategory = 80,
                    toStringStyle = ToStringStyle.FloatTwo,
                    formatString = "{0} " + "kg".Translate(),
                    showOnPawns = true,
                    workerClass = typeof(MassCapacityStatWorker)
                };

                DefDatabase<StatDef>.Add(stat);
                return stat;
            }
            catch (Exception ex)
            {
                OHLog.Integration.Warn("GetOrCreateStandaloneStat", ex, "Failed to create standalone StatDef 'OverHaulers_CaravanMassCapacity'.");
            }

            return null;
        }

        #endregion

        #region BASELINE & EGRESS DELIVERY

        /// <summary>
        /// Resolves the standalone baseline mass capacity for the specified pawn, querying the cached baseline first
        /// before delegating to the species calibration engine.
        /// </summary>
        /// <param name="pawn">The pawn for which to resolve the baseline mass capacity.</param>
        /// <returns>The resolved baseline mass capacity in kg.</returns>
        public float ResolveDriverBaseline(Pawn pawn)
        {
            if (pawn == null) return 0f;

            if (PawnDataRegistry.TryGetCachedBaseline(pawn.thingIDNumber, out float cachedBaseline))
            {
                return cachedBaseline;
            }

            return SpeciesBaselineCalibration.ResolveSpeciesBaseline(pawn);
        }

        /// <summary>
        /// Handles the postfix logic for the mass utility capacity calculation, directly offsetting MassUtility.Capacity.
        /// </summary>
        /// <param name="pawn">The pawn for which the mass utility capacity is being calculated.</param>
        /// <param name="result">The current result of the mass utility capacity calculation, which can be modified.</param>
        /// <param name="explanation">A StringBuilder containing the explanation for the mass utility capacity calculation, which can be appended to.</param>
        public void OnMassUtilityCapacityPostfix(Pawn pawn, ref float result, StringBuilder explanation)
        {
            // BREAKPOINT ANCHOR: Dummy Evaluation Bypass
            if (SpeciesBaselineCalibration.IsResolvingBaseline) return;

            // INGRESS GATE: Completely skip non-caravan species during live play
            if (pawn == null || !PawnDataRegistry.CanCarryCaravanMass(pawn)) return;

            float cleanBiologicalBaseline = ResolveDriverBaseline(pawn);
            float calculatedOffset = PawnDataRegistry.GetOffset(pawn, cleanBiologicalBaseline);

            result += calculatedOffset;

            // EGRESS CLAMP: Guarantee the actual game result obeys the safety floor
            result = MassCapacitySolver.EnforceSafetyFloor(result, pawn.LabelShortCap);
        }

        #endregion
    }

    #endregion
}
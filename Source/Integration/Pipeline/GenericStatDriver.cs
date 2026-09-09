using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OverHaulers
{
    #region 1. [INT-04] GENERIC STAT-DRIVEN PIPELINE DRIVER

    /// <summary>
    /// A generic implementation of a stat-driven pipeline driver for handling mass capacity calculations in RimWorld.
    /// </summary>
    public class GenericStatDriver : IPipelineDriver
    {
        #region 1. FIELDS & CONSTRUCTOR

        public string DriverIdentifier { get; }
        public bool IsStatDriven => true;
        public StatDef ActiveMassCapacityStat { get; }
        public string UnitSuffix { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GenericStatDriver"/> class with the specified identifier, target stat, and unit suffix.
        /// </summary>
        /// <param name="identifier">The unique identifier for this driver.</param>
        /// <param name="targetStat">The StatDef representing the mass capacity stat to be used by this driver.</param>
        /// <param name="unitSuffix">The unit suffix to display alongside the calculated mass capacity.</param>
        public GenericStatDriver(string identifier, StatDef targetStat, string unitSuffix)
        {
            DriverIdentifier = identifier;
            ActiveMassCapacityStat = targetStat;
            UnitSuffix = unitSuffix;
        }

        #endregion

        #region 2. INITIALIZATION & CLEANUP

        /// <summary>
        /// Initializes the generic stat driver, setting up the necessary stat parts and logging integration details.
        /// </summary>
        /// <param name="harmony">The Harmony instance used for patching, if needed.</param>
        public void Initialize(Harmony harmony)
        {
            if (ActiveMassCapacityStat == null) return;

            if (string.IsNullOrEmpty(ActiveMassCapacityStat.label))
            {
                ActiveMassCapacityStat.label = "OverHaulers_StatLabel".Translate().ToString();
            }

            if (string.IsNullOrEmpty(ActiveMassCapacityStat.description))
            {
                ActiveMassCapacityStat.description = "OverHaulers_StatDesc".Translate().ToString();
            }

            RegisterStatPart();
            OHLog.Integration.StatDrivenBound(DriverIdentifier, ActiveMassCapacityStat.defName, UnitSuffix);
        }

        /// <summary>
        /// Cleans up the generic stat driver, removing any registered stat parts from the active mass capacity stat.
        /// </summary>
        public void Cleanup()
        {
            if (ActiveMassCapacityStat?.parts != null)
            {
                ActiveMassCapacityStat.parts.RemoveAll(p => p is MassCapacityStatPart);
            }
        }

        /// <summary>
        /// Registers the mass capacity stat part with the active mass capacity stat, ensuring it is properly integrated into the stat system.
        /// </summary>
        private void RegisterStatPart()
        {
            if (ActiveMassCapacityStat.parts == null)
            {
                ActiveMassCapacityStat.parts = new List<StatPart>();
            }

            for (int i = 0; i < ActiveMassCapacityStat.parts.Count; i++)
            {
                if (ActiveMassCapacityStat.parts[i] is MassCapacityStatPart)
                {
                    return;
                }
            }

            ActiveMassCapacityStat.parts.Add(new MassCapacityStatPart { parentStat = ActiveMassCapacityStat });
        }

        #endregion

        #region 3. BASELINE & EGRESS DELIVERY

        /// <summary>
        /// Resolves the original baseline mass capacity for the specified pawn, taking into account cached values and archetype calibration.
        /// </summary>
        /// <param name="pawn">The pawn for which to resolve the original baseline mass capacity.</param>
        /// <returns>The resolved original baseline mass capacity for the specified pawn.</returns>
        public float ResolveOriginalBaseline(Pawn pawn)
        {
            if (pawn == null) return 0f;

            if (PawnDataRegistry.TryGetCachedBaseline(pawn.thingIDNumber, out float cachedBaseline))
            {
                return cachedBaseline;
            }

            return ModpackBaselineCalibration.ResolveArchetypeCalibratedBaseline(pawn);
        }

        /// <summary>
        /// Handles the postfix logic for the mass utility capacity calculation, allowing for additional modifications or explanations to be appended.
        /// </summary>
        /// <param name="pawn">The pawn for which the mass utility capacity is being calculated.</param>
        /// <param name="result">The current result of the mass utility capacity calculation, which can be modified.</param>
        /// <param name="explanation">A StringBuilder containing the explanation for the mass utility capacity calculation, which can be appended to.</param>
        public void OnMassUtilityCapacityPostfix(Pawn pawn, ref float result, StringBuilder explanation)
        {
            // Direct numerical offset is handled via StatPart; no postfix modification needed
        }

        #endregion
    }

    #endregion
}
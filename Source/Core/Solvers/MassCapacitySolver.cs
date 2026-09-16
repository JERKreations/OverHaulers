using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [PASS-03 & PASS-04] CORE MATHEMATICAL MASS CAPACITY SOLVER ENGINE
    /// Stateless computational engine executing vector mathematics across parallel contiguous SoA arrays.
    /// Operates on contiguous memory streams off the main thread to calculate biological Caravan Mass Capacity offsets (kg).
    /// </summary>
    public static class MassCapacitySolver
    {
        #region 1. CONSTANTS & FALLBACK METRICS

        /// <summary>
        /// Default fallback Caravan Mass Capacity offset in kilograms (0.0 kg) returned during invalid biological states,
        /// missing topological species templates, or solver exceptions to maintain engine stability without zero-division or desyncs.
        /// </summary>
        public const float FallbackMassOffsetKg = 0.0f;

        #endregion

        #region 2. REGIONAL DEFICITS DATA LAYOUT & ADAPTERS

        /// <summary>
        /// Zero-allocation stack wrapper adapting an IAnatomicalDataSource reference into the struct solver pipeline.
        /// </summary>
        private readonly struct BoxedDataSourceWrapper : IAnatomicalDataSource
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

            public float GetCapacityLevel(PawnCapacityDef capacity) => inner != null ? inner.GetCapacityLevel(capacity) : 1.0f;
            public void IngressPathology(AnatomicalWorkspace workspace, bool shouldCompileUIProperties) => inner?.IngressPathology(workspace, shouldCompileUIProperties);
            public float ResolveBaselineCapacity() => inner != null ? inner.ResolveBaselineCapacity() : 0f;
        }

        /// <summary>
        /// Represents the raw and clamped deficits for different anatomical regions of a pawn, used in the mass capacity calculations.
        /// </summary>
        private struct RegionalDeficits
        {
            public float RawCoreNegative;
            public float RawManipulationNegative;
            public float RawMovingNegative;

            public float ClampedCoreDeficit;
            public float ClampedManipulationDeficit;
            public float ClampedMovingDeficit;

            public float TotalClampedDeficit => ClampedCoreDeficit + ClampedManipulationDeficit + ClampedMovingDeficit;

            /// <summary>
            /// Retrieves the ratio of the clamped deficit to the raw negative value for the specified anatomical part type.
            /// </summary>
            /// <param name="type">The anatomical part type for which to retrieve the ratio.</param>
            /// <returns>The ratio of the clamped deficit to the raw negative value for the specified part type.</returns>
            public float GetRatio(PartType type)
            {
                if (type == PartType.CorePart) return RawCoreNegative > 0f ? (ClampedCoreDeficit / RawCoreNegative) : 1.0f;
                if (type == PartType.ManipulationPart) return RawManipulationNegative > 0f ? (ClampedManipulationDeficit / RawManipulationNegative) : 1.0f;
                if (type == PartType.MovingPart) return RawMovingNegative > 0f ? (ClampedMovingDeficit / RawMovingNegative) : 1.0f;
                if (type == PartType.DualLimb)
                {
                    float rManip = RawManipulationNegative > 0f ? (ClampedManipulationDeficit / RawManipulationNegative) : 1.0f;
                    float rMove = RawMovingNegative > 0f ? (ClampedMovingDeficit / RawMovingNegative) : 1.0f;
                    return (rManip + rMove) * 0.5f;
                }
                return 1.0f;
            }
        }

        /// <summary>
        /// Stack-allocated accumulator aggregating positive and negative systemic deltas across anatomical regions.
        /// Consolidates capacity coupling math with 0 bytes GC allocation.
        /// </summary>
        private struct SystemicRegionalDeltas
        {
            public float TorsoPositive;
            public float ArmPositive;
            public float LegPositive;
            public float DualPositive;

            public float TorsoNegative;
            public float ArmNegative;
            public float LegNegative;
            public float DualNegative;

            /// <summary>
            /// Accumulates positive and negative regional deltas for a single biological capacity.
            /// </summary>
            /// <param name="snappedCapacity">The snapped mass capacity value for the current calculation.</param>
            /// <param name="posTorso">The positive contribution factor for the torso.</param>
            /// <param name="posArm">The positive contribution factor for the arms.</param>
            /// <param name="posLeg">The positive contribution factor for the legs.</param>
            /// <param name="defTorso">The negative contribution factor for the torso.</param>
            /// <param name="defArm">The negative contribution factor for the arms.</param>
            /// <param name="defLeg">The negative contribution factor for the legs.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Accumulate(
                float snappedCapacity,
                float posTorso, float posArm, float posLeg,
                float defTorso, float defArm, float defLeg)
            {
                float delta = snappedCapacity - 1.0f;
                if (delta > 0f)
                {
                    TorsoPositive += delta * posTorso;
                    ArmPositive += delta * posArm;
                    LegPositive += delta * posLeg;
                }
                else if (delta < 0f)
                {
                    float neg = -delta;
                    TorsoNegative += neg * defTorso;
                    ArmNegative += neg * defArm;
                    LegNegative += neg * defLeg;
                }
            }

            /// <summary>
            /// Averages manipulation and moving deltas to derive bilateral dual-limb contributions.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void FinalizeDualLimbs()
            {
                DualPositive = (ArmPositive + LegPositive) * 0.5f;
                DualNegative = (ArmNegative + LegNegative) * 0.5f;
            }

            /// <summary>
            /// Retrieves the regional positive and negative deltas for a specific part type in constant time.
            /// </summary>
            /// <param name="type">The part type for which to retrieve the deltas.</param>
            /// <param name="positiveDelta">The output positive delta for the specified part type.</param>
            /// <param name="negativeDelta">The output negative delta for the specified part type.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void GetDeltas(PartType type, out float positiveDelta, out float negativeDelta)
            {
                switch (type)
                {
                    case PartType.CorePart:
                        positiveDelta = TorsoPositive;
                        negativeDelta = TorsoNegative;
                        break;
                    case PartType.ManipulationPart:
                        positiveDelta = ArmPositive;
                        negativeDelta = ArmNegative;
                        break;
                    case PartType.MovingPart:
                        positiveDelta = LegPositive;
                        negativeDelta = LegNegative;
                        break;
                    case PartType.DualLimb:
                        positiveDelta = DualPositive;
                        negativeDelta = DualNegative;
                        break;
                    default:
                        positiveDelta = 0f;
                        negativeDelta = 0f;
                        break;
                }
            }
        }

        #endregion

        #region 3. PRIMARY PUBLIC SOLVER ENTRY POINTS

        /// <summary>
        /// [PASS-03 & PASS-04] Primary solver calculating the final Caravan Mass Capacity offset in kilograms (kg) for an abstract data source.
        /// Constrained to struct to guarantee zero heap allocations, eliminate interface boxing, and ensure C# 7.3/9.0 compatibility.
        /// Operates with complete thread isolation and encapsulated workspace lifecycle management.
        /// </summary>
        /// <typeparam name="TSource">The concrete anatomical data source struct type implementing IAnatomicalDataSource.</typeparam>
        /// <param name="source">The anatomical data source representing the pawn.</param>
        /// <param name="baselineCapacity">The baseline mass capacity of the pawn.</param>
        /// <param name="biologicalBaseline">The biological baseline mass capacity of the pawn (output).</param>
        /// <param name="settings">The settings influencing the mass capacity calculation.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI-related properties should be compiled.</param>
        /// <param name="workspace">The anatomical workspace used during the calculation (output).</param>
        /// <returns>The calculated mass capacity offset for the pawn.</returns>
        public static float SolveMassCapacityOffset<TSource>(
            TSource source, 
            float baselineCapacity, 
            out float biologicalBaseline, 
            Settings settings,
            bool shouldCompileUIProperties,
            out AnatomicalWorkspace workspace) where TSource : struct, IAnatomicalDataSource
        {
            workspace = null;
            biologicalBaseline = 0f;

            // Early exit if the source is invalid (struct is guaranteed non-null).
            if (!source.IsValidBiologicalState)
            {
                return FallbackMassOffsetKg;
            }

            bool isSuccessful = false;

            try
            {
                // Initialize the workspace and prepare for mass capacity calculation.
                workspace = WorkspacePool.GetWorkspace();
                biologicalBaseline = baselineCapacity;

                SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(source.BodyDef);
                if (template == null)
                {
                    return FallbackMassOffsetKg;
                }

                CompilePathology(source, template, workspace, shouldCompileUIProperties);

                PartCounts partCounts = template.Counts;

                BiologicalCapacitySnapshot snapshot;
                snapshot.Breathing = source.GetCapacityLevel(PawnCapacityDefOf.Breathing);
                snapshot.BloodPumping = source.GetCapacityLevel(PawnCapacityDefOf.BloodPumping);
                snapshot.Moving = source.GetCapacityLevel(PawnCapacityDefOf.Moving);
                snapshot.Manipulation = source.GetCapacityLevel(PawnCapacityDefOf.Manipulation);
                snapshot.Consciousness = source.GetCapacityLevel(PawnCapacityDefOf.Consciousness);

                SystemicEvaluationContext context = SystemicEvaluationContext.CreateFromSnapshot(
                    biologicalBaseline, partCounts, settings, snapshot);

                float totalPlayableBudget = Mathf.Max(0f, biologicalBaseline - context.CapacityFloor);

                // PASS 03: Parallel vector calculations across contiguous SoA arrays (Smooth, unclamped)
                SolveRawPartOffsets(workspace, context, totalPlayableBudget);

                // PASS 04: Regional budget clamping and proportional deficit scaling
                RegionalDeficits deficits = CalculateClampedGroupBudgetDeficits(workspace, context, totalPlayableBudget, out float totalPositiveBoosts);

                // The net biological delta: positive boosts minus clamped deficits
                float netBiologicalOffset = totalPositiveBoosts - deficits.TotalClampedDeficit;

                // Populate visual nodes with the exact matching deltas
                PopulateVisualNodeOffsets(workspace, in deficits);

                isSuccessful = true;
                return netBiologicalOffset;
            }
            catch (Exception ex)
            {
                string sourceLabel = source.EntityLabel ?? "Unknown";
                OHLog.Solver.Warn(sourceLabel, ex, "Failed to solve mass capacity offset for pawn.");
                return FallbackMassOffsetKg;
            }
            finally
            {
                // Ensure the workspace is cleared if the calculation was not successful.
                if (!isSuccessful)
                {
                    workspace?.Clear();
                    workspace = null;
                }
            }
        }

        /// <summary>
        /// [PASS-03 & PASS-04] Interface forwarder adapting interface-typed callers into the unified struct solver.
        /// Preserves DRY principles and prevents code duplication.
        /// </summary>
        /// <param name="source">The anatomical data source for which to solve the mass capacity offset.</param>
        /// <param name="baselineCapacity">The baseline mass capacity of the source.</param>
        /// <param name="biologicalBaseline">The biological baseline mass capacity of the source (output).</param>
        /// <param name="settings">The settings influencing the mass capacity calculation.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI-related properties should be compiled.</param>
        /// <param name="workspace">The anatomical workspace used during the calculation (output).</param>
        /// <returns>The calculated mass capacity offset for the source.</returns>
        public static float SolveMassCapacityOffset(
            IAnatomicalDataSource source, 
            float baselineCapacity, 
            out float biologicalBaseline, 
            Settings settings,
            bool shouldCompileUIProperties,
            out AnatomicalWorkspace workspace)
        {
            if (source == null)
            {
                biologicalBaseline = 0f;
                workspace = null;
                return FallbackMassOffsetKg;
            }

            return SolveMassCapacityOffset(
                new BoxedDataSourceWrapper(source), 
                baselineCapacity, 
                out biologicalBaseline, 
                settings, 
                shouldCompileUIProperties, 
                out workspace
            );
        }

        /// <summary>
        /// Solves the mass capacity offset for the given pawn.
        /// Binds directly to the generic value-type pipeline to achieve zero heap allocations.
        /// </summary>
        /// <param name="pawn">The pawn for which to solve the mass capacity offset.</param>
        /// <param name="baselineCapacity">The baseline mass capacity of the pawn.</param>
        /// <param name="biologicalBaseline">The biological baseline mass capacity of the pawn (output).</param>
        /// <param name="settings">The settings influencing the mass capacity calculation.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI-related properties should be compiled.</param>
        /// <param name="workspace">The anatomical workspace used during the calculation (output).</param>
        /// <returns>The calculated mass capacity offset for the pawn.</returns>
        public static float SolveMassCapacityOffset(
            Pawn pawn, 
            float baselineCapacity, 
            out float biologicalBaseline, 
            Settings settings,
            bool shouldCompileUIProperties,
            out AnatomicalWorkspace workspace)
        {
            if (pawn == null)
            {
                biologicalBaseline = 0f;
                workspace = null;
                return FallbackMassOffsetKg;
            }

            return SolveMassCapacityOffset(
                new PawnAnatomicalSource(pawn), 
                baselineCapacity, 
                out biologicalBaseline, 
                settings, 
                shouldCompileUIProperties, 
                out workspace
            );
        }

        #endregion

        #region 4. PATHOLOGY COMPILATION & WORKSPACE PREPARATION

        /// <summary>
        /// Compiles the pathology information for the given anatomical workspace based on the source data and template.
        /// Dispatches directly without interface boxing.
        /// </summary>
        /// <typeparam name="TSource">The concrete anatomical data source struct type implementing IAnatomicalDataSource.</typeparam>
        /// <param name="source">The anatomical data source providing pathology information.</param>
        /// <param name="template">The species topology template defining the anatomical structure.</param>
        /// <param name="workspace">The anatomical workspace to populate with pathology data.</param>
        /// <param name="shouldCompileUIProperties">Indicates whether UI-related properties should be compiled.</param>
        private static void CompilePathology<TSource>(
            TSource source, 
            SpeciesTopologyTemplate template, 
            AnatomicalWorkspace workspace, 
            bool shouldCompileUIProperties) where TSource : struct, IAnatomicalDataSource
        {
            int partCount = template.PartCount;
            workspace.InitializeForPawn(template);
            workspace.EnsureArraySizes(partCount);

            workspace.ResetAllSlots(partCount);
            source.IngressPathology(workspace, shouldCompileUIProperties);
        }

        #endregion

        #region 5. [PASS-03] STREAMING SoA VECTOR MATHEMATICS

        /// <summary>
        /// Solves the raw part offsets across contiguous SoA memory lanes.
        /// Wounds, amputations, and kinetic systemic strain accumulate smoothly without premature part-level clamping.
        /// </summary>
        /// <param name="workspace">The anatomical workspace containing part states and efficiencies.</param>
        /// <param name="context">The systemic evaluation context containing scaling factors and budget information.</param>
        /// <param name="totalPlayableBudget">The total playable budget available for allocation across all regions.</param>
        private static void SolveRawPartOffsets(
            AnatomicalWorkspace workspace,
            in SystemicEvaluationContext context,
            float totalPlayableBudget)
        {
            int partCount = workspace.PartCount;

            var healths = workspace.HealthFractions;
            var effs = workspace.EfficiencyRatings;
            var staticWeights = workspace.StaticWeightFactors;
            var calcProsthetics = workspace.CalculatedProsthetics;
            var calcAthletics = workspace.CalculatedAthletics;
            var calcHealths = workspace.CalculatedHealths;

            PartType[] partTypes = workspace.PartTypes;
            int[] parentIndices = workspace.ParentIndices;

            #region 5A. Bilateral Root Symmetry Evaluation
            int totalRootArms = context.Counts.TotalManipulationRoots;
            int totalRootLegs = context.Counts.TotalMovingRoots;
            int intactRootArms = 0;
            int intactRootLegs = 0;

            // Initialize counters for intact root limbs to evaluate bilateral symmetry.
            for (int i = 0; i < partCount; i++)
            {
                PartType type = partTypes[i];
                if (type == PartType.ManipulationPart || type == PartType.MovingPart || type == PartType.DualLimb)
                {
                    int parentIdx = parentIndices[i];
                    bool isRoot = parentIdx == -1 || partTypes[parentIdx] != type;
                    if (isRoot && !workspace.HasFlag(i, PartFlags.IsMissing))
                    {
                        if (type == PartType.ManipulationPart) intactRootArms++;
                        else if (type == PartType.MovingPart) intactRootLegs++;
                        else if (type == PartType.DualLimb)
                        {
                            intactRootArms++;
                            intactRootLegs++;
                        }
                    }
                }
            }

            float armRootSymmetry = totalRootArms > 0 ? Mathf.Clamp01((float)intactRootArms / totalRootArms) : 1.0f;
            float legRootSymmetry = totalRootLegs > 0 ? Mathf.Clamp01((float)intactRootLegs / totalRootLegs) : 1.0f;
            float dualRootSymmetry = (armRootSymmetry + legRootSymmetry) * 0.5f;
            #endregion

            #region 5B. Branchless Direct Systemic Regional Deltas
            SystemicRegionalDeltas regionalDeltas = default;

            // The positive and negative deltas represent the net contributions of various systemic factors
            // to the mass capacity of each anatomical region. Positive deltas enhance capacity, while
            // negative deltas indicate deficits that reduce capacity.
            if (context.EnableAthletics)
            {
                // 1. Positive and Negative Breathing Delta
                regionalDeltas.Accumulate(
                    context.SnappedBreathing,
                    context.TorsoPositiveBreathing, context.ArmPositiveBreathing, context.LegPositiveBreathing,
                    context.TorsoDeficitBreathing, context.ArmDeficitBreathing, context.LegDeficitBreathing);

                // 2. Positive and Negative Blood Pumping Delta
                regionalDeltas.Accumulate(
                    context.SnappedBloodPumping,
                    context.TorsoPositiveBlood, context.ArmPositiveBlood, context.LegPositiveBlood,
                    context.TorsoDeficitBlood, context.ArmDeficitBlood, context.LegDeficitBlood);

                // 3. Positive and Negative Moving Delta (Kinetic Chain Cascade)
                regionalDeltas.Accumulate(
                    context.SnappedMoving,
                    context.TorsoPositiveMoving, context.ArmPositiveMoving, context.LegPositiveMoving,
                    context.TorsoDeficitMoving, context.ArmDeficitMoving, context.LegDeficitMoving);

                // 4. Positive and Negative Manipulation Delta (Bilateral Synergy)
                regionalDeltas.Accumulate(
                    context.SnappedManipulation,
                    context.TorsoPositiveManipulation, context.ArmPositiveManipulation, context.LegPositiveManipulation,
                    context.TorsoDeficitManipulation, context.ArmDeficitManipulation, context.LegDeficitManipulation);

                // 5. Dual Limb Contributions (Averaged for Symmetry)
                regionalDeltas.FinalizeDualLimbs();
            }
            #endregion

            #region 5C. Contiguous Streaming Math (Smooth, Unclamped)

            // Iterate through all parts to calculate their contributions to the overall mass capacity.
            // This includes evaluating missing limbs, added prosthetics, and health impacts.
            for (int i = 0; i < partCount; i++)
            {
                PartType type = partTypes[i];

                // Skip non-contributing parts (e.g., head or undefined)
                if (type == PartType.None || type == PartType.HeadPart) 
                    continue;

                float partNormalizedWeight = staticWeights[i];
                float partPlayableBudget = partNormalizedWeight * totalPlayableBudget;

                float calculatedProsthetic = 0f;
                float calculatedAthletic = 0f;
                float calculatedHealth = 0f;

                bool hasAddedPart = workspace.HasFlag(i, PartFlags.HasAddedPart);
                bool isMissing = workspace.HasFlag(i, PartFlags.IsMissing);

                // 1. Missing Limb & Anchor Deficits
                if (isMissing)
                {
                    if (workspace.HasFlag(i, PartFlags.IsNeutralized))
                    {
                        calculatedHealth = 0f;
                    }
                    else if (context.EnablePartHealth)
                    {
                        // Clean linear deficit: 100% missing = full part budget share
                        calculatedHealth = -partPlayableBudget;
                    }
                }
                // 2. Prosthetic Efficiencies
                else if (hasAddedPart)
                {
                    if (context.EnableProsthetics)
                    {
                        // Retrieve the efficiency of the added prosthetic part
                        float efficiency = effs[i];

                        // Branch A: Positive Prosthetic Delta (Efficiency Boost)
                        if (efficiency > 1.0f)
                        {
                            float efficiencyBoost = efficiency - 1.0f;
                            float symmetryMultiplier = (type == PartType.ManipulationPart)
                                ? armRootSymmetry : (type == PartType.MovingPart)
                                ? legRootSymmetry : (type == PartType.DualLimb)
                                ? dualRootSymmetry : 1.0f;

                            calculatedProsthetic = partNormalizedWeight * efficiencyBoost * context.ProstheticAnchorScale * symmetryMultiplier;
                        }
                        // Branch B: Negative Prosthetic Delta (Efficiency Deficit)
                        else if (efficiency < 1.0f)
                        {
                            float efficiencyDeficit = 1.0f - efficiency;
                            calculatedProsthetic = -partPlayableBudget * efficiencyDeficit;
                        }
                    }
                }
                // 3. Organic Part Wounds (Clean linear proportion: 50% HP = 50% budget loss)
                else if (context.EnablePartHealth && healths[i] < 1.0f)
                {
                    float healthLoss = 1.0f - healths[i];
                    calculatedHealth = -partPlayableBudget * healthLoss;
                }

                // 4. Granular Systemic Athletic Coupling (Kinetic Strain)
                if (context.EnableAthletics && !isMissing)
                {
                    regionalDeltas.GetDeltas(type, out float regionalPositiveDelta, out float regionalNegativeDelta);

                    // Apply regional positive delta if applicable
                    if (regionalPositiveDelta > 0f && !hasAddedPart)
                    {
                        calculatedAthletic += partNormalizedWeight * regionalPositiveDelta * context.AthleticAnchorScale;
                    }

                    // Apply regional negative delta if applicable
                    if (regionalNegativeDelta > 0f)
                    {
                        calculatedAthletic -= partPlayableBudget * regionalNegativeDelta * context.AthleticScalingMultiplier;
                    }
                }

                calcProsthetics[i] = calculatedProsthetic;
                calcAthletics[i] = calculatedAthletic;
                calcHealths[i] = calculatedHealth;
            }
            #endregion
        }

        #endregion

        #region 6. [PASS-04] REGIONAL BUDGET NORMALIZATION & UI PROJECTION

        /// <summary>
        /// [PASS-04] Normalizes accumulated regional deficits against the Safety Floor window.
        /// If raw physical wounds + kinetic strain exceed the region's assigned slice, GetRatio() scales
        /// all negative contributors proportionally so the region collapses cleanly onto its budget ceiling.
        /// </summary>
        /// <param name="workspace">The anatomical workspace containing part states and efficiencies.</param>
        /// <param name="context">The systemic evaluation context containing scaling factors and budget information.</param>
        /// <param name="totalPlayableBudget">The total playable budget available for allocation across all regions.</param>
        /// <param name="totalPositiveBoosts">Outputs the total positive boosts accumulated from all parts.</param>
        /// <returns>A RegionalDeficits object containing the calculated deficits for each anatomical region.</returns>
        /// <remarks>
        /// This method ensures that the calculated deficits for each anatomical region do not exceed the allocated budget.
        /// It takes into account both positive boosts and negative contributions from prosthetics, athletics, and health.
        /// </remarks>
        private static RegionalDeficits CalculateClampedGroupBudgetDeficits(
            AnatomicalWorkspace workspace,
            in SystemicEvaluationContext context,
            float totalPlayableBudget,
            out float totalPositiveBoosts)
        {
            RegionalDeficits deficits = new RegionalDeficits();

            int partCount = workspace.PartCount;
            var calcProsthetics = workspace.CalculatedProsthetics;
            var calcAthletics = workspace.CalculatedAthletics;
            var calcHealths = workspace.CalculatedHealths;

            PartType[] partTypes = workspace.PartTypes;

            float maxTorsoNegative = totalPlayableBudget * context.GetNormalizedBudget(PartType.CorePart);
            float maxArmNegative = totalPlayableBudget * context.GetNormalizedBudget(PartType.ManipulationPart);
            float maxLegNegative = totalPlayableBudget * context.GetNormalizedBudget(PartType.MovingPart);

            totalPositiveBoosts = 0f;

            // Iterate through all parts to calculate their individual contributions to the overall deficits.
            for (int i = 0; i < partCount; i++)
            {
                PartType type = partTypes[i];

                // Skip parts that are not relevant for mass capacity calculations, such as None or HeadPart.
                if (type == PartType.None || type == PartType.HeadPart) 
                    continue;

                float partPositive = 0f;
                if (calcProsthetics[i] > 0f) partPositive += calcProsthetics[i];
                if (calcAthletics[i] > 0f) partPositive += calcAthletics[i];
                totalPositiveBoosts += partPositive;

                float partNegative = 0f;
                if (calcProsthetics[i] < 0f) partNegative += Math.Abs(calcProsthetics[i]);
                if (calcAthletics[i] < 0f) partNegative += Math.Abs(calcAthletics[i]);
                if (calcHealths[i] < 0f) partNegative += Math.Abs(calcHealths[i]);

                // Distribute negative contributions to the appropriate regional deficits based on part type.
                switch (type)
                {
                    case PartType.CorePart:
                        deficits.RawCoreNegative += partNegative;
                        break;
                    case PartType.ManipulationPart:
                        deficits.RawManipulationNegative += partNegative;
                        break;
                    case PartType.MovingPart:
                        deficits.RawMovingNegative += partNegative;
                        break;
                    case PartType.DualLimb:
                        deficits.RawManipulationNegative += partNegative * 0.5f;
                        deficits.RawMovingNegative += partNegative * 0.5f;
                        break;
                }
            }

            deficits.ClampedCoreDeficit = Mathf.Min(deficits.RawCoreNegative, maxTorsoNegative);
            deficits.ClampedManipulationDeficit = Mathf.Min(deficits.RawManipulationNegative, maxArmNegative);
            deficits.ClampedMovingDeficit = Mathf.Min(deficits.RawMovingNegative, maxLegNegative);

            return deficits;
        }

        /// <summary>
        /// Populates the visual node offsets for each part in the anatomical workspace based on the calculated regional deficits.
        /// </summary>
        /// <param name="workspace">The anatomical workspace containing part states and efficiencies.</param>
        /// <param name="deficits">The calculated regional deficits used to adjust visual node offsets.</param>
        private static void PopulateVisualNodeOffsets(
            AnatomicalWorkspace workspace,
            in RegionalDeficits deficits)
        {
            int partCount = workspace.PartCount;

            var calcProsthetics = workspace.CalculatedProsthetics;
            var calcAthletics = workspace.CalculatedAthletics;
            var calcHealths = workspace.CalculatedHealths;
            var calcTotals = workspace.CalculatedTotals;

            PartType[] partTypes = workspace.PartTypes;

            // Iterate through all parts to adjust their visual node offsets based on the regional deficits.
            for (int i = 0; i < partCount; i++)
            {
                PartType type = partTypes[i];
                float ratio = deficits.GetRatio(type);

                float finalProsthetic = calcProsthetics[i];
                if (finalProsthetic < 0f) finalProsthetic *= ratio;

                float finalAthletic = calcAthletics[i];
                if (finalAthletic < 0f) finalAthletic *= ratio;

                float finalHealth = calcHealths[i];
                if (finalHealth < 0f) finalHealth *= ratio;

                calcProsthetics[i] = finalProsthetic;
                calcAthletics[i] = finalAthletic;
                calcHealths[i] = finalHealth;

                calcTotals[i] = finalProsthetic + finalAthletic + finalHealth;
            }
        }

        #endregion

        #region 7. SAFETY FLOOR CLAMPING ENDPOINT

        /// <summary>
        /// Ensures that the final capacity does not fall below the defined safety floor.
        /// </summary>
        /// <param name="finalCapacity">The calculated final capacity to be clamped.</param>
        /// <param name="targetLabel">The label of the target entity for logging purposes.</param>
        /// <returns>The clamped final capacity, ensuring it meets the safety floor requirement.</returns>
        /// <remarks>
        /// This method will check the final capacity against a predefined safety floor (0.01f) and clamp it if necessary.
        /// It also logs the clamping action for performance tracking purposes.
        /// </remarks>
        public static float EnforceSafetyFloor(float finalCapacity, string targetLabel = "Pawn")
        {
            // If the compatibility safety floor setting is disabled, bypass the safety floor enforcement.
            if (OverHaulers.settings != null && !OverHaulers.settings.compatibilitySafetyFloor)
            {
                return finalCapacity;
            }

            // If the safety floor enforcement is enabled, check against the minimum threshold and clamp if necessary.
            if (finalCapacity < 0.01f)
            {
                float clampedValue = 0.01f;
                OHLog.Performance.RecordSafetyFloorClamped(targetLabel, finalCapacity, clampedValue);
                return clampedValue;
            }

            return finalCapacity;
        }

        #endregion
    }
}
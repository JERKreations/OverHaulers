using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [PASS-03 & PASS-04] CORE MATHEMATICAL MASS CAPACITY SOLVER ENGINE
    /// Stateless computational engine executing vector mathematics across parallel contiguous SoA arrays.
    /// Operates on contiguous memory streams to calculate biological Caravan Mass Capacity offsets (kg).
    /// Manages workspace lease lifecycles internally to guarantee zero memory leaks across all callers.
    /// Strictly decoupled from UI and Presentation layers.
    /// Resides under Source/Core/Solvers/.
    /// </summary>
    public static class MassCapacitySolver
    {
        #region 1. CONSTANTS & FALLBACK METRICS

        /// <summary>
        /// [SOLV-02] Absolute safety floor preventing negative or zero carrying capacity divisions.
        /// </summary>
        public const float MinimumAllowedCapacityKg = 0.01f;

        /// <summary>
        /// Default fallback Caravan Mass Capacity offset in kilograms (0.0 kg) returned during invalid biological states,
        /// missing topological species templates, or solver exceptions to maintain engine stability without zero-division or desyncs.
        /// </summary>
        public const float FallbackMassOffsetKg = 0.0f;

        #endregion

        #region 2. INTERNAL REGIONAL DEFICITS & DELTA STRUCTS

        /// <summary>
        /// Represents the raw and clamped deficits for different anatomical regions of a pawn, used in mass capacity calculations.
        /// Tracks the ratio of clamped capacity to raw health loss to scale regional deficits proportionally.
        /// </summary>
        internal struct RegionalDeficits
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
            /// Ensures deficits collapse smoothly onto their allocated budget ceiling without sharp threshold steps.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        /// Consolidates biological capacity coupling math with 0 bytes GC allocation.
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

        #region 3. PUBLIC CANONICAL SOLVER API

        /// <summary>
        /// [SOLV-10] Primary canonical numerical solver calculating Caravan Mass Capacity metrics for a data source.
        /// Constrained to struct to guarantee zero heap allocations, eliminate interface boxing, and ensure strict C# 7.3 compatibility.
        /// Encapsulates the thread-local workspace lease completely within its execution scope using <see cref="WorkspacePool.WorkspaceLease"/>.
        /// Consumes <see cref="OverHaulers.settings"/> directly as the single source of truth for runtime configuration.
        /// </summary>
        /// <typeparam name="TSource">The concrete anatomical data source struct type implementing <see cref="IAnatomicalDataSource"/>.</typeparam>
        /// <param name="source">The anatomical data source representing the pawn.</param>
        /// <param name="baselineCapacity">The baseline mass capacity of the pawn in kilograms before physiological modifications.</param>
        /// <returns>A populated <see cref="MassCapacityEvaluationResult"/> value containing all solved metrics.</returns>
        public static MassCapacityEvaluationResult EvaluateMassCapacity<TSource>(
            TSource source,
            float baselineCapacity) where TSource : struct, IAnatomicalDataSource
        {
            if (!source.IsValidBiologicalState)
            {
                return MassCapacityEvaluationResult.Fallback;
            }

            try
            {
                using (WorkspacePool.WorkspaceLease lease = WorkspacePool.LeaseWorkspace())
                {
                    return SolveSource(source, baselineCapacity, lease.Workspace, populateColdMetadata: false);
                }
            }
            catch (Exception ex)
            {
                string sourceLabel = source.EntityLabel ?? "Unknown";
                OHLog.Solver.Warn(sourceLabel, ex, "Failed to evaluate mass capacity for pawn.");
                return MassCapacityEvaluationResult.Fallback;
            }
        }

        #endregion

        #region 4. SECONDARY ENTRY POINTS & SAFETY CLAMPING

        /// <summary>
        /// Evaluates numerical mass capacity metrics for a live <see cref="Pawn"/> instance.
        /// Zero heap allocation pass-through wrapping the pawn into a stack-allocated <see cref="PawnAnatomicalSource"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MassCapacityEvaluationResult EvaluateMassCapacityForPawn(
            Pawn pawn,
            float baselineCapacity)
        {
            if (pawn == null)
            {
                return MassCapacityEvaluationResult.Fallback;
            }

            return EvaluateMassCapacity(new PawnAnatomicalSource(pawn), baselineCapacity);
        }

        /// <summary>
        /// Evaluates numerical mass capacity metrics for an abstract <see cref="IAnatomicalDataSource"/> instance.
        /// Wraps the interface into a stack-allocated struct to satisfy generic constraints without boxing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MassCapacityEvaluationResult EvaluateMassCapacityForSource(
            IAnatomicalDataSource source,
            float baselineCapacity)
        {
            if (source == null || !source.IsValidBiologicalState)
            {
                return MassCapacityEvaluationResult.Fallback;
            }

            return EvaluateMassCapacity(new BoxedDataSourceWrapper(source), baselineCapacity);
        }

        /// <summary>
        /// Ensures that the final calculated capacity does not fall below the configured safety floor.
        /// Serves as the single canonical implementation for floor enforcement and telemetry recording.
        /// </summary>
        /// <param name="finalCapacity">The calculated final capacity in kilograms to be clamped.</param>
        /// <param name="targetLabel">The label of the target entity for diagnostic logging purposes.</param>
        /// <returns>The clamped final capacity in kilograms, ensuring it meets the minimum threshold.</returns>
        public static float EnforceSafetyFloor(float finalCapacity, string targetLabel = "Pawn")
        {
            if (OverHaulers.settings != null && !OverHaulers.settings.compatibilitySafetyFloor)
            {
                return finalCapacity;
            }

            if (finalCapacity < MinimumAllowedCapacityKg)
            {
                float clampedValue = MinimumAllowedCapacityKg;
                OHLog.Performance.RecordSafetyFloorClamped(targetLabel, finalCapacity, clampedValue);
                return clampedValue;
            }

            return finalCapacity;
        }

        /// <summary>
        /// Evaluates and clamps the final capacity for a pawn against the safety floor.
        /// Zero-allocation pass-through hand-off to <see cref="EnforceSafetyFloor"/>.
        /// </summary>
        /// <param name="finalCapacity">The calculated final caravan mass capacity in kilograms to be clamped.</param>
        /// <param name="pawn">The pawn entity being evaluated.</param>
        /// <returns>The clamped final capacity in kilograms.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EnforceSafetyFloorForPawn(float finalCapacity, Pawn pawn)
        {
            if (finalCapacity < MinimumAllowedCapacityKg)
            {
                string targetLabel = pawn != null
                    ? (UnityData.IsInMainThread ? pawn.LabelShortCap.ToString() : pawn.def?.label ?? "Pawn")
                    : "Pawn";

                return EnforceSafetyFloor(finalCapacity, targetLabel);
            }

            return finalCapacity;
        }

        #endregion

        #region 5. ALGORITHMIC SOLVER PIPELINE

        /// <summary>
        /// [SOLV-11] Ingress compiler bridge translating anatomical source data into workspace lanes
        /// and executing the canonical internal solver pipeline.
        /// </summary>
        internal static MassCapacityEvaluationResult SolveSource<TSource>(
            TSource source,
            float biologicalBaseline,
            AnatomicalWorkspace workspace,
            bool populateColdMetadata) where TSource : struct, IAnatomicalDataSource
        {
            SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(source.BodyDef);
            if (template == null)
            {
                return new MassCapacityEvaluationResult(
                    FallbackMassOffsetKg,
                    biologicalBaseline,
                    biologicalBaseline,
                    1f,
                    0f,
                    0f,
                    0f,
                    false);
            }

            // Ingress sweep populates SoA contiguous parallel arrays
            CompilePathology(source, template, workspace, populateColdMetadata);

            PartCounts partCounts = template.Counts;

            BiologicalCapacitySnapshot snapshot;
            snapshot.Breathing = source.GetCapacityLevel(PawnCapacityDefOf.Breathing);
            snapshot.BloodPumping = source.GetCapacityLevel(PawnCapacityDefOf.BloodPumping);
            snapshot.Moving = source.GetCapacityLevel(PawnCapacityDefOf.Moving);
            snapshot.Manipulation = source.GetCapacityLevel(PawnCapacityDefOf.Manipulation);
            snapshot.Consciousness = (workspace != null && workspace.CachedConsciousness >= 0f)
                ? workspace.CachedConsciousness
                : source.GetCapacityLevel(PawnCapacityDefOf.Consciousness);

            // Instantiate systemic evaluation context
            SystemicEvaluationContext context = SystemicEvaluationContext.CreateFromSnapshot(
                biologicalBaseline, partCounts, OverHaulers.settings, snapshot);

            // BREAKPOINT ANCHOR: Execute canonical numerical pipeline
            return SolveInternal(workspace, in context, biologicalBaseline, source.EntityLabel);
        }

        /// <summary>
        /// [SOLV-12] Canonical Single Source of Truth (SSoT) executing the numerical solving pipeline.
        /// Operates directly on contiguous memory in the leased <see cref="AnatomicalWorkspace"/>.
        /// </summary>
        internal static MassCapacityEvaluationResult SolveInternal(
            AnatomicalWorkspace workspace,
            in SystemicEvaluationContext context,
            float biologicalBaseline,
            string entityLabel)
        {
            float totalPlayableBudget = Math.Max(0f, biologicalBaseline - context.CapacityFloor);

            // PASS 03: Parallel vector calculations across contiguous SoA arrays (Smooth, unclamped)
            SolveRawPartOffsets(workspace, context, totalPlayableBudget);

            // PASS 04: Regional budget clamping and proportional deficit scaling
            RegionalDeficits deficits = CalculateClampedGroupBudgetDeficits(
                workspace, context, totalPlayableBudget, out float totalPositiveBoosts);

            // The net biological delta: positive boosts minus clamped deficits
            float netBiologicalOffset = totalPositiveBoosts - deficits.TotalClampedDeficit;

            // Normalize workspace calculated totals with the exact matching deficits
            NormalizeWorkspaceCalculatedTotals(workspace, in deficits);

            // Extract summary breakdown metrics directly from contiguous arrays
            float prostheticBoost = 0f;
            float healthDeficit = 0f;
            float athleticOffset = 0f;
            for (int i = 0; i < workspace.PartCount; i++)
            {
                if (workspace.CalculatedProsthetics[i] > 0f) prostheticBoost += workspace.CalculatedProsthetics[i];
                if (workspace.CalculatedHealths[i] < 0f) healthDeficit += workspace.CalculatedHealths[i];
                athleticOffset += workspace.CalculatedAthletics[i];
            }

            float finalCapacity = EnforceSafetyFloor(biologicalBaseline + netBiologicalOffset, entityLabel);
            float totalMultiplier = biologicalBaseline > 0f ? (finalCapacity / biologicalBaseline) : 1f;
            bool isAtSafetyFloor = finalCapacity <= MinimumAllowedCapacityKg;

            return new MassCapacityEvaluationResult(
                netBiologicalOffset,
                biologicalBaseline,
                finalCapacity,
                totalMultiplier,
                prostheticBoost,
                healthDeficit,
                athleticOffset,
                isAtSafetyFloor
            );
        }

        /// <summary>
        /// Compiles pathology data into the workspace. Dispatches directly without interface boxing.
        /// </summary>
        private static void CompilePathology<TSource>(
            TSource source,
            SpeciesTopologyTemplate template,
            AnatomicalWorkspace workspace,
            bool populateColdMetadata) where TSource : struct, IAnatomicalDataSource
        {
            int partCount = template.PartCount;
            workspace.InitializeForPawn(template);
            workspace.EnsureArraySizes(partCount);

            workspace.ResetAllSlots(partCount);
            source.IngressPathology(workspace, populateColdMetadata);
        }

        #endregion

        #region 6. [PASS-03] STREAMING SoA VECTOR MATHEMATICS

        /// <summary>
        /// Solves the raw part offsets across contiguous SoA memory lanes.
        /// Wounds, amputations, and kinetic systemic strain accumulate smoothly without premature part-level clamping.
        /// </summary>
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

            #region 6A. Bilateral Root Symmetry Evaluation
            int totalRootArms = context.Counts.TotalManipulationRoots;
            int totalRootLegs = context.Counts.TotalMovingRoots;
            int intactRootArms = 0;
            int intactRootLegs = 0;

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

            float armRootSymmetry = totalRootArms > 0 ? Math.Min(1f, Math.Max(0f, (float)intactRootArms / totalRootArms)) : 1.0f;
            float legRootSymmetry = totalRootLegs > 0 ? Math.Min(1f, Math.Max(0f, (float)intactRootLegs / totalRootLegs)) : 1.0f;
            float dualRootSymmetry = (armRootSymmetry + legRootSymmetry) * 0.5f;
            #endregion

            #region 6B. Branchless Direct Systemic Regional Deltas
            SystemicRegionalDeltas regionalDeltas = default(SystemicRegionalDeltas);

            if (context.EnableAthletics)
            {
                regionalDeltas.Accumulate(
                    context.SnappedBreathing,
                    context.TorsoPositiveBreathing, context.ArmPositiveBreathing, context.LegPositiveBreathing,
                    context.TorsoDeficitBreathing, context.ArmDeficitBreathing, context.LegDeficitBreathing);

                regionalDeltas.Accumulate(
                    context.SnappedBloodPumping,
                    context.TorsoPositiveBlood, context.ArmPositiveBlood, context.LegPositiveBlood,
                    context.TorsoDeficitBlood, context.ArmDeficitBlood, context.LegDeficitBlood);

                regionalDeltas.Accumulate(
                    context.SnappedMoving,
                    context.TorsoPositiveMoving, context.ArmPositiveMoving, context.LegPositiveMoving,
                    context.TorsoDeficitMoving, context.ArmDeficitMoving, context.LegDeficitMoving);

                regionalDeltas.Accumulate(
                    context.SnappedManipulation,
                    context.TorsoPositiveManipulation, context.ArmPositiveManipulation, context.LegPositiveManipulation,
                    context.TorsoDeficitManipulation, context.ArmDeficitManipulation, context.LegDeficitManipulation);

                regionalDeltas.FinalizeDualLimbs();
            }
            #endregion

            #region 6C. Contiguous Streaming Math (Smooth, Unclamped)
            for (int i = 0; i < partCount; i++)
            {
                PartType type = partTypes[i];

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
                        calculatedHealth = -partPlayableBudget;
                    }
                }
                // 2. Prosthetic Efficiencies
                else if (hasAddedPart)
                {
                    if (context.EnableProsthetics)
                    {
                        float efficiency = effs[i];

                        if (efficiency > 1.0f)
                        {
                            float efficiencyBoost = efficiency - 1.0f;
                            float symmetryMultiplier = (type == PartType.ManipulationPart)
                                ? armRootSymmetry : (type == PartType.MovingPart)
                                ? legRootSymmetry : (type == PartType.DualLimb)
                                ? dualRootSymmetry : 1.0f;

                            calculatedProsthetic = partNormalizedWeight * efficiencyBoost * context.ProstheticAnchorScale * symmetryMultiplier;
                        }
                        else if (efficiency < 1.0f)
                        {
                            float efficiencyDeficit = 1.0f - efficiency;
                            calculatedProsthetic = -partPlayableBudget * efficiencyDeficit;
                        }
                    }
                }
                // 3. Organic Part Wounds
                else if (context.EnablePartHealth && healths[i] < 1.0f)
                {
                    float healthLoss = 1.0f - healths[i];
                    calculatedHealth = -partPlayableBudget * healthLoss;
                }

                // 4. Granular Systemic Athletic Coupling
                if (context.EnableAthletics && !isMissing)
                {
                    regionalDeltas.GetDeltas(type, out float regionalPositiveDelta, out float regionalNegativeDelta);

                    if (regionalPositiveDelta > 0f && !hasAddedPart)
                    {
                        calculatedAthletic += partNormalizedWeight * regionalPositiveDelta * context.AthleticAnchorScale;
                    }

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

        #region 7. [PASS-04] REGIONAL BUDGET NORMALIZATION & SCALING

        /// <summary>
        /// [PASS-04] Normalizes accumulated regional deficits against the Safety Floor window.
        /// If raw physical wounds + kinetic strain exceed the region's assigned slice, scales all negative contributors proportionally.
        /// </summary>
        /// <param name="workspace">The anatomical workspace containing calculated part vectors.</param>
        /// <param name="context">The systemic evaluation context containing regional budget thresholds.</param>
        /// <param name="totalPlayableBudget">The total playable capacity budget above the safety floor.</param>
        /// <param name="totalPositiveBoosts">Outputs the aggregate positive capacity boosts across all regions in kilograms.</param>
        /// <returns>A populated <see cref="RegionalDeficits"/> structure with clamped regional ceilings and scaling ratios.</returns>
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

            for (int i = 0; i < partCount; i++)
            {
                PartType type = partTypes[i];

                if (type == PartType.None || type == PartType.HeadPart)
                    continue;

                float partPositive = 0f;
                if (calcProsthetics[i] > 0f) partPositive += calcProsthetics[i];
                if (calcAthletics[i] > 0f) partPositive += calcAthletics[i];
                totalPositiveBoosts += partPositive;

                float partNegative = 0f;
                if (calcProsthetics[i] < 0f) partNegative -= calcProsthetics[i];
                if (calcAthletics[i] < 0f) partNegative -= calcAthletics[i];
                if (calcHealths[i] < 0f) partNegative -= calcHealths[i];

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

            deficits.ClampedCoreDeficit = Math.Min(deficits.RawCoreNegative, maxTorsoNegative);
            deficits.ClampedManipulationDeficit = Math.Min(deficits.RawManipulationNegative, maxArmNegative);
            deficits.ClampedMovingDeficit = Math.Min(deficits.RawMovingNegative, maxLegNegative);

            return deficits;
        }

        /// <summary>
        /// Normalizes the calculated totals for each part in the anatomical workspace based on the regional deficit ratios.
        /// Scales negative prosthetics, athletic strain, and injury deficits proportionally to prevent regional budget overruns.
        /// </summary>
        /// <param name="workspace">The anatomical workspace containing calculated part vectors.</param>
        /// <param name="deficits">The calculated regional deficit scaling ratios.</param>
        private static void NormalizeWorkspaceCalculatedTotals(
            AnatomicalWorkspace workspace,
            in RegionalDeficits deficits)
        {
            int partCount = workspace.PartCount;

            var calcProsthetics = workspace.CalculatedProsthetics;
            var calcAthletics = workspace.CalculatedAthletics;
            var calcHealths = workspace.CalculatedHealths;
            var calcTotals = workspace.CalculatedTotals;

            PartType[] partTypes = workspace.PartTypes;

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
    }
}
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
        #region 1. [SOLV-01] CONSTANTS & FALLBACK METRICS

        /// <summary>
        /// [SOLV-01] Hard technical compatibility safety floor preventing negative or zero mass capacity divisions.
        /// Protects RimWorld caravan and hauling routines from division-by-zero crashes.
        /// </summary>
        public const float HardCompatibilitySafetyFloorKg = 0.01f;

        /// <summary>
        /// Default fallback Caravan Mass Capacity offset in kilograms (0.0 kg) returned during invalid biological states,
        /// missing topological species templates, or solver exceptions to maintain engine stability without zero-division or desyncs.
        /// </summary>
        public const float FallbackMassOffsetKg = 0.0f;

        #endregion

        #region 2. [SOLV-02] INTERNAL REGIONAL DEFICITS & DELTA STRUCTS

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
                    return GetDualLimbRatio(0.5f, 0.5f);
                }
                return 1.0f;
            }

            /// <summary>
            /// Computes the exact weighted clamping ratio for a dual-purpose limb given its manipulation and moving budget shares.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float GetDualLimbRatio(float manipShare, float moveShare)
            {
                float rManip = RawManipulationNegative > 0f ? (ClampedManipulationDeficit / RawManipulationNegative) : 1.0f;
                float rMove = RawMovingNegative > 0f ? (ClampedMovingDeficit / RawMovingNegative) : 1.0f;
                return (rManip * manipShare) + (rMove * moveShare);
            }
        }

        /// <summary>
        /// Value-type accumulator aggregating positive and negative systemic deltas across anatomical regions.
        /// </summary>
        internal struct SystemicRegionalDeltas
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
            /// <param name="snappedCapacity">The capacity fraction evaluated for the biological capacity.</param>
            /// <param name="posTorso">Positive scaling weight applied to the Torso/Core region.</param>
            /// <param name="posArm">Positive scaling weight applied to the Manipulation region.</param>
            /// <param name="posLeg">Positive scaling weight applied to the Locomotion region.</param>
            /// <param name="defTorso">Deficit scaling weight applied to the Torso/Core region.</param>
            /// <param name="defArm">Deficit scaling weight applied to the Manipulation region.</param>
            /// <param name="defLeg">Deficit scaling weight applied to the Locomotion region.</param>
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
                    float torsoStep = Math.Min(1.0f, neg * defTorso);
                    float armStep = Math.Min(1.0f, neg * defArm);
                    float legStep = Math.Min(1.0f, neg * defLeg);

                    TorsoNegative += (1.0f - TorsoNegative) * torsoStep;
                    ArmNegative += (1.0f - ArmNegative) * armStep;
                    LegNegative += (1.0f - LegNegative) * legStep;
                }
            }

            /// <summary>
            /// Blends manipulation and moving deltas by the specified shares to derive dual-limb contributions.
            /// </summary>
            /// <param name="manipShare">The share of the manipulation delta to apply.</param>
            /// <param name="moveShare">The share of the moving delta to apply.</param>
            /// <param name="positiveDelta">Outputs the blended positive delta for the dual-limb.</param>
            /// <param name="negativeDelta">Outputs the blended negative delta for the dual-limb.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void GetDualLimbDeltas(float manipShare, float moveShare, out float positiveDelta, out float negativeDelta)
            {
                positiveDelta = (ArmPositive * manipShare) + (LegPositive * moveShare);
                negativeDelta = (ArmNegative * manipShare) + (LegNegative * moveShare);
            }

            /// <summary>
            /// Retrieves the regional positive and negative deltas for a specific part type in constant time.
            /// </summary>
            /// <param name="type">The anatomical part type to retrieve deltas for.</param>
            /// <param name="positiveDelta">Outputs the aggregate positive regional delta.</param>
            /// <param name="negativeDelta">Outputs the aggregate negative regional delta.</param>
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

            /// <summary>
            /// Estimates the average systemic capacity delta (across Breathing, Blood Pumping, Moving, and Manipulation)
            /// for a group header presentation preview.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float GetEstimatedEfficiencyDelta(PartType type, bool allowDeficits)
            {
                GetDeltas(type, out float regionalPositive, out float regionalNegative);
                float effectiveNegative = allowDeficits ? regionalNegative : 0f;
                return (regionalPositive * 0.25f) - effectiveNegative;
            }
        }

        #endregion

        #region 3. [SOLV-03] STATIC CONFIGURATION & CANONICAL SOLVER API

        /// <summary>
        /// Thread-safe active configuration snapshot pushed from the Settings layer.
        /// Eliminates upward dependencies on Settings.
        /// </summary>
        private static SolverConfiguration activeConfig = CoreDefaults.CreateDefaultSolverConfiguration();

        /// <summary>Gets a read-only reference to the active solver configuration.</summary>
        public static ref readonly SolverConfiguration ActiveConfiguration => ref activeConfig;

        /// <summary>
        /// Updates the active solver configuration snapshot.
        /// Single canonical entry point for push configuration from higher layers; the snapshot is range-checked here, so Core never trusts the caller.
        /// </summary>
        /// <param name="newConfig">The new solver configuration struct snapshot.</param>
        public static void UpdateConfiguration(in SolverConfiguration newConfig)
        {
            activeConfig = newConfig.Sanitized(out bool wasModified);
            if (wasModified)
            {
                OHLog.Warn("Config.SolverSanitized", null, "Solver configuration contained out-of-range or non-finite values; they were clamped to their supported bounds.");
            }
        }

        /// <summary>
        /// [SOLV-03] Primary canonical numerical solver calculating Caravan Mass Capacity metrics for a data source.
        /// The struct constraint avoids boxing the data source during constrained interface calls.
        /// Workspace initialization, growth, or exceptional paths may allocate.
        /// Encapsulates the thread-local workspace lease completely within its execution scope using <see cref="WorkspacePool.WorkspaceLease"/>.
        /// Consumes active configuration pushed from Settings as the single source of truth for runtime configuration.
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
                return CreateNeutralFallback(baselineCapacity);
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
                OHLog.Warn("Solver.EvaluateMassCapacity", ex, $"Failed to evaluate mass capacity for pawn '{sourceLabel}'.");
                return CreateNeutralFallback(baselineCapacity);
            }
        }

        #endregion

        #region 4. [SOLV-04] SECONDARY ENTRY POINTS & SAFETY CLAMPING

        /// <summary>
        /// Evaluates numerical mass capacity metrics for a live <see cref="Pawn"/> instance.
        /// Wraps the pawn in a <see cref="PawnAnatomicalSource"/> value and delegates to the canonical solver.
        /// </summary>
        /// <param name="pawn">The pawn instance to evaluate.</param>
        /// <param name="baselineCapacity">The baseline mass capacity for the pawn.</param>
        /// <returns>The evaluated mass capacity result for the pawn.</returns>
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
        /// Ensures that the final calculated capacity does not fall below the hard technical compatibility safety floor (0.01 kg).
        /// Serves as the single canonical implementation for hard floor enforcement and telemetry recording.
        /// </summary>
        /// <param name="finalCapacity">The calculated final capacity in kilograms to be clamped.</param>
        /// <param name="targetLabel">The label of the target entity for diagnostic logging purposes.</param>
        /// <returns>The clamped final capacity in kilograms, ensuring it meets the minimum threshold.</returns>
        public static float EnforceHardCompatibilitySafetyFloor(float finalCapacity, string targetLabel = "Pawn", int pawnId = 0)
        {
            if (!activeConfig.EnableHardCompatibilitySafetyFloor)
            {
                return finalCapacity;
            }

            if (finalCapacity < HardCompatibilitySafetyFloorKg)
            {
                float clampedValue = HardCompatibilitySafetyFloorKg;
                PerformanceReport.RecordHardSafetyFloorClamped(targetLabel, finalCapacity, clampedValue, pawnId);
                return clampedValue;
            }

            return finalCapacity;
        }

        /// <summary>
        /// Evaluates and clamps the final capacity for a pawn against the hard technical compatibility safety floor.
        /// Delegates to <see cref="EnforceHardCompatibilitySafetyFloor"/> when the floor applies.
        /// </summary>
        /// <param name="finalCapacity">The calculated final caravan mass capacity in kilograms to be clamped.</param>
        /// <param name="pawn">The pawn entity being evaluated.</param>
        /// <returns>The clamped final capacity in kilograms.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EnforceHardCompatibilitySafetyFloorForPawn(float finalCapacity, Pawn pawn)
        {
            if (finalCapacity < HardCompatibilitySafetyFloorKg && activeConfig.EnableHardCompatibilitySafetyFloor)
            {
                string targetLabel = pawn != null
                    ? (UnityData.IsInMainThread ? pawn.LabelShortCap.ToString() : pawn.def?.label ?? "Pawn")
                    : "Pawn";
                int pawnId = pawn != null ? pawn.thingIDNumber : 0;

                return EnforceHardCompatibilitySafetyFloor(finalCapacity, targetLabel, pawnId);
            }

            return finalCapacity;
        }

        #endregion

        #region 5. [SOLV-05] ALGORITHMIC SOLVER PIPELINE

        /// <summary>
        /// Constructs a neutral fallback result preserving <paramref name="baselineCapacity"/> when valid,
        /// avoiding collapsing an uncompilable or non-biological pawn to 0.01 kg.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MassCapacityEvaluationResult CreateNeutralFallback(float baselineCapacity)
        {
            return baselineCapacity > 0f
                ? new MassCapacityEvaluationResult(FallbackMassOffsetKg, baselineCapacity, baselineCapacity, 1f, 0f, 0f, 0f)
                : MassCapacityEvaluationResult.Fallback;
        }

        /// <summary>
        /// [SOLV-05] Ingress compiler bridge translating anatomical source data into workspace lanes
        /// and executing the canonical internal solver pipeline.
        /// </summary>
        /// <param name="source">The anatomical data source representing the pawn.</param>
        /// <param name="biologicalBaseline">The baseline biological mass capacity for the pawn.</param>
        /// <param name="workspace">The anatomical workspace containing contiguous SoA arrays for the pawn.</param>
        /// <param name="populateColdMetadata">Flag indicating whether to populate cold metadata during compilation.</param>
        /// <returns>The evaluated mass capacity result for the pawn.</returns>
        internal static MassCapacityEvaluationResult SolveSource<TSource>(
            TSource source,
            float biologicalBaseline,
            AnatomicalWorkspace workspace,
            bool populateColdMetadata) where TSource : struct, IAnatomicalDataSource
        {
            SpeciesTopologyTemplate template = TopologyLayoutCompiler.GetOrCreateTopologyTemplate(source.BodyDef);
            if (template == null)
            {
                return CreateNeutralFallback(biologicalBaseline);
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

            // Instantiate systemic evaluation context from active configuration snapshot
            SystemicEvaluationContext context = SystemicEvaluationContext.CreateFromSnapshot(
                biologicalBaseline, partCounts, in activeConfig, in snapshot);

            // BREAKPOINT ANCHOR: Execute canonical numerical pipeline
            return SolveInternal(workspace, in context, biologicalBaseline, source.EntityLabel, source.EntityId);
        }

        /// <summary>
        /// [SOLV-05] Canonical Single Source of Truth (SSoT) executing the numerical solving pipeline.
        /// Operates directly on contiguous memory in the leased <see cref="AnatomicalWorkspace"/>.
        /// </summary>
        /// <param name="workspace">The anatomical workspace containing contiguous SoA arrays for the pawn.</param>
        /// <param name="context">The systemic evaluation context derived from the active configuration snapshot.</param>
        /// <param name="biologicalBaseline">The baseline biological mass capacity for the pawn.</param>
        /// <param name="entityLabel">The label of the pawn entity.</param>
        /// <param name="entityId">The unique identifier of the pawn entity.</param>
        internal static MassCapacityEvaluationResult SolveInternal(
            AnatomicalWorkspace workspace,
            in SystemicEvaluationContext context,
            float biologicalBaseline,
            string entityLabel,
            int entityId = 0)
        {
            // When Physical Deficits is disabled, the playable deficit budget is zeroed out,
            // ensuring no negative penalties (wounds, missing limbs, sub-par prosthetics, or athletic strain)
            // can reduce capacity below baseline. Positive prosthetic and athletic boosts remain fully active.
            float totalPlayableBudget = context.EnablePartHealth
                ? Math.Max(0f, biologicalBaseline - context.ConfigurablePercentileFloorKg)
                : 0f;

            // PASS 03: Parallel vector calculations across contiguous SoA arrays (Smooth, unclamped)
            SolveRawPartOffsets(workspace, context, totalPlayableBudget);

            // PASS 04: Regional budget clamping and proportional deficit scaling
            RegionalDeficits deficits = CalculateClampedGroupBudgetDeficits(
                workspace, context, totalPlayableBudget, out float totalPositiveBoosts);

            // The net biological delta: positive boosts minus clamped deficits
            float netBiologicalOffset = totalPositiveBoosts - deficits.TotalClampedDeficit;

            // Normalize workspace calculated totals and aggregate summary breakdown metrics in a single streaming loop
            NormalizeAndAggregateMetrics(
                workspace,
                in context,
                in deficits,
                out float prostheticBoost,
                out float healthDeficit,
                out float athleticOffset);

            float finalCapacity = EnforceHardCompatibilitySafetyFloor(biologicalBaseline + netBiologicalOffset, entityLabel, entityId);
            float totalMultiplier = biologicalBaseline > 0f ? (finalCapacity / biologicalBaseline) : 1f;

            return new MassCapacityEvaluationResult(
                netBiologicalOffset,
                biologicalBaseline,
                finalCapacity,
                totalMultiplier,
                prostheticBoost,
                healthDeficit,
                athleticOffset
            );
        }

        /// <summary>
        /// Normalizes calculated totals and aggregates breakdown metrics in a single contiguous memory pass.
        /// Eliminates a redundant array traversal across active workspace slots.
        /// </summary>
        /// <param name="workspace">The anatomical workspace containing contiguous SoA arrays for the pawn.</param>
        /// <param name="context">The systemic evaluation context derived from the active configuration snapshot.</param>
        /// <param name="deficits">The regional deficits calculated for the pawn.</param>
        /// <param name="prostheticBoost">
        /// Output parameter for the aggregated positive prosthetic boost.
        /// Negative prosthetic deficits remain in per-part vectors and net offset.
        /// </param>
        /// <param name="healthDeficit">Output parameter for the aggregated health deficit.</param>
        /// <param name="athleticOffset">Output parameter for the aggregated athletic offset.</param>
        private static void NormalizeAndAggregateMetrics(
            AnatomicalWorkspace workspace,
            in SystemicEvaluationContext context,
            in RegionalDeficits deficits,
            out float prostheticBoost,
            out float healthDeficit,
            out float athleticOffset)
        {
            int partCount = workspace.PartCount;

            var calcProsthetics = workspace.CalculatedProsthetics;
            var calcAthletics = workspace.CalculatedAthletics;
            var calcHealths = workspace.CalculatedHealths;
            var calcTotals = workspace.CalculatedTotals;
            PartType[] partTypes = workspace.PartTypes;
            float[] weightManip = workspace.WeightManipulation;
            float[] weightMove = workspace.WeightMoving;
            float normManip = context.GetNormalizedBudget(PartType.ManipulationPart);
            float normMove = context.GetNormalizedBudget(PartType.MovingPart);

            prostheticBoost = 0f;
            healthDeficit = 0f;
            athleticOffset = 0f;

            for (int i = 0; i < partCount; i++)
            {
                PartType type = partTypes[i];
                float ratio;
                if (type == PartType.DualLimb)
                {
                    GetDualLimbShares(i, weightManip, weightMove, normManip, normMove, out float manipShare, out float moveShare);
                    ratio = deficits.GetDualLimbRatio(manipShare, moveShare);
                }
                else
                {
                    ratio = deficits.GetRatio(type);
                }

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

                if (finalProsthetic > 0f) prostheticBoost += finalProsthetic;
                if (finalHealth < 0f) healthDeficit += finalHealth;
                athleticOffset += finalAthletic;
            }
        }

        /// <summary>
        /// Compiles pathology data into the workspace. Dispatches directly without interface boxing.
        /// </summary>
        /// <typeparam name="TSource">The concrete anatomical data source struct type.</typeparam>
        /// <param name="source">The anatomical data source representing the pawn.</param>
        /// <param name="template">The precompiled species topology template.</param>
        /// <param name="workspace">The active leased workspace to populate.</param>
        /// <param name="populateColdMetadata">If true, captures Def references for UI presentation.</param>
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
        /// [SOLV-SSOT] Compiles regional positive and negative athletic deltas from systemic capacities
        /// and configured regional weights. Shared between the solver vector pass and UI group summaries.
        /// </summary>
        internal static SystemicRegionalDeltas ComputeRegionalAthleticDeltas(in SystemicEvaluationContext context)
        {
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

                float normManip = context.GetNormalizedBudget(PartType.ManipulationPart);
                float normMove = context.GetNormalizedBudget(PartType.MovingPart);
                GetDualLimbShares(-1, null, null, normManip, normMove, out float manipShare, out float moveShare);
                regionalDeltas.GetDualLimbDeltas(manipShare, moveShare, out regionalDeltas.DualPositive, out regionalDeltas.DualNegative);
            }

            return regionalDeltas;
        }

        /// <summary>
        /// Solves the raw part offsets across contiguous SoA memory lanes.
        /// Wounds, amputations, sub-par prosthetics, and natural athletics evaluate uniformly against equal underlying limb weights.
        /// Boosted replacements (&gt; 100%) on multi-limbed species (&gt; 2 roots) are ranked by boost magnitude so the first two
        /// boosted branches always receive the canonical 2-limb anchor (<c>112.5% + 112.5% = 125%</c>) regardless of limb order,
        /// while subsequent boosted branches follow <see cref="SolverConfiguration.LimbPairDecayFactor"/>.
        /// </summary>
        /// <param name="workspace">The anatomical workspace containing contiguous SoA arrays for the pawn.</param>
        /// <param name="context">The systemic evaluation context derived from the active configuration snapshot.</param>
        /// <param name="totalPlayableBudget">The total playable mass capacity budget for the pawn.</param>
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
            int[] rootIndices = workspace.RootPartIndices;
            float[] weightManip = workspace.WeightManipulation;
            float[] weightMove = workspace.WeightMoving;
            float normManip = context.GetNormalizedBudget(PartType.ManipulationPart);
            float normMove = context.GetNormalizedBudget(PartType.MovingPart);

            #region 6A. Bilateral Symmetry & Multi-Limb Boost Detection
            // BREAKPOINT ANCHOR: Bilateral Limb Symmetry & Multi-Limb Boost Gate
            float totalManipWeight = 0f;
            float intactManipWeight = 0f;
            float totalMoveWeight = 0f;
            float intactMoveWeight = 0f;
            bool hasBoostedManipCandidate = false;
            bool hasBoostedMoveCandidate = false;

            for (int i = 0; i < partCount; i++)
            {
                PartType type = partTypes[i];
                if (type == PartType.ManipulationPart || type == PartType.MovingPart || type == PartType.DualLimb)
                {
                    bool missing = workspace.HasFlag(i, PartFlags.IsMissing);
                    float wManip = weightManip[i];
                    float wMove = weightMove[i];

                    if (wManip > 0f)
                    {
                        totalManipWeight += wManip;
                        if (!missing) intactManipWeight += wManip;
                    }
                    if (wMove > 0f)
                    {
                        totalMoveWeight += wMove;
                        if (!missing) intactMoveWeight += wMove;
                    }

                    if (!missing && context.EnableProsthetics && workspace.HasFlag(i, PartFlags.HasAddedPart) && effs[i] > 1.0f)
                    {
                        if (wManip > 0f) hasBoostedManipCandidate = true;
                        if (wMove > 0f) hasBoostedMoveCandidate = true;
                    }
                }
            }

            float armBilateralSymmetry = totalManipWeight > 0f ? Math.Min(1f, Math.Max(0f, intactManipWeight / totalManipWeight)) : 1.0f;
            float legBilateralSymmetry = totalMoveWeight > 0f ? Math.Min(1f, Math.Max(0f, intactMoveWeight / totalMoveWeight)) : 1.0f;

            bool applyManipPairScaling = hasBoostedManipCandidate && context.Counts.TotalManipulationRoots > 2;
            bool applyMovePairScaling = hasBoostedMoveCandidate && context.Counts.TotalMovingRoots > 2;

            float[] manipMultipliers = applyManipPairScaling ? workspace.BranchManipMultipliers : null;
            float[] moveMultipliers = applyMovePairScaling ? workspace.BranchMoveMultipliers : null;

            if (applyManipPairScaling && manipMultipliers != null)
            {
                ComputeChannelPairMultipliers(
                    workspace,
                    effs,
                    weightManip,
                    rootIndices,
                    partCount,
                    context.Counts.TotalManipulationRoots,
                    context.LimbPairDecayFactor,
                    manipMultipliers);
            }

            if (applyMovePairScaling && moveMultipliers != null)
            {
                ComputeChannelPairMultipliers(
                    workspace,
                    effs,
                    weightMove,
                    rootIndices,
                    partCount,
                    context.Counts.TotalMovingRoots,
                    context.LimbPairDecayFactor,
                    moveMultipliers);
            }
            #endregion

            #region 6B. Branchless Direct Systemic Regional Deltas
            SystemicRegionalDeltas regionalDeltas = ComputeRegionalAthleticDeltas(in context);
            #endregion

            #region 6C. Contiguous Streaming Math (Smooth, Unclamped)
            // BREAKPOINT ANCHOR: Contiguous SoA Math Streaming Loop
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
                            int rootIdx = (rootIndices != null && rootIndices[i] >= 0) ? rootIndices[i] : i;

                            if (type == PartType.ManipulationPart)
                            {
                                float pairScale = manipMultipliers != null ? manipMultipliers[rootIdx] : 1.0f;
                                calculatedProsthetic = (weightManip[i] * normManip * pairScale)
                                    * efficiencyBoost * context.ProstheticAnchorScale * armBilateralSymmetry;
                            }
                            else if (type == PartType.MovingPart)
                            {
                                float pairScale = moveMultipliers != null ? moveMultipliers[rootIdx] : 1.0f;
                                calculatedProsthetic = (weightMove[i] * normMove * pairScale)
                                    * efficiencyBoost * context.ProstheticAnchorScale * legBilateralSymmetry;
                            }
                            else if (type == PartType.DualLimb)
                            {
                                float manipPairScale = manipMultipliers != null ? manipMultipliers[rootIdx] : 1.0f;
                                float movePairScale = moveMultipliers != null ? moveMultipliers[rootIdx] : 1.0f;
                                float manipContrib = (weightManip[i] * normManip * manipPairScale) * armBilateralSymmetry;
                                float moveContrib = (weightMove[i] * normMove * movePairScale) * legBilateralSymmetry;
                                calculatedProsthetic = (manipContrib + moveContrib) * efficiencyBoost * context.ProstheticAnchorScale;
                            }
                            else
                            {
                                calculatedProsthetic = partNormalizedWeight * efficiencyBoost * context.ProstheticAnchorScale;
                            }
                        }
                        else if (efficiency < 1.0f)
                        {
                            float efficiencyDeficit = 1.0f - efficiency;
                            calculatedProsthetic = -partPlayableBudget * efficiencyDeficit;
                        }
                    }
                    else if (context.EnablePartHealth && healths[i] < 1.0f)
                    {
                        float healthLoss = 1.0f - healths[i];
                        calculatedProsthetic = -partPlayableBudget * healthLoss;
                    }
                }
                // 3. Organic Part Wounds
                else if (context.EnablePartHealth && healths[i] < 1.0f)
                {
                    float healthLoss = 1.0f - healths[i];
                    calculatedHealth = -partPlayableBudget * healthLoss;
                }

                // 4. Granular Systemic Athletic Coupling (natural parts only)
                if (context.EnableAthletics && !isMissing && !hasAddedPart)
                {
                    float regionalPositiveDelta;
                    float regionalNegativeDelta;
                    if (type == PartType.DualLimb)
                    {
                        GetDualLimbShares(i, weightManip, weightMove, normManip, normMove, out float manipShare, out float moveShare);
                        regionalDeltas.GetDualLimbDeltas(manipShare, moveShare, out regionalPositiveDelta, out regionalNegativeDelta);
                    }
                    else
                    {
                        regionalDeltas.GetDeltas(type, out regionalPositiveDelta, out regionalNegativeDelta);
                    }

                    if (regionalPositiveDelta > 0f)
                    {
                        calculatedAthletic += partNormalizedWeight * regionalPositiveDelta * context.AthleticAnchorScale;
                    }

                    if (regionalNegativeDelta > 0f)
                    {
                        // Systemic strain depletes only the remaining healthy tissue fraction within the part's playable budget,
                        // keeping total negative offsets inside the playable budget without saturating the configurable floor early.
                        calculatedAthletic -= partPlayableBudget * healths[i] * regionalNegativeDelta;
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
            float[] weightManip = workspace.WeightManipulation;
            float[] weightMove = workspace.WeightMoving;

            float normTorso = context.GetNormalizedBudget(PartType.CorePart);
            float normManip = context.GetNormalizedBudget(PartType.ManipulationPart);
            float normMove = context.GetNormalizedBudget(PartType.MovingPart);

            float maxTorsoNegative = totalPlayableBudget * normTorso;
            float maxArmNegative = totalPlayableBudget * normManip;
            float maxLegNegative = totalPlayableBudget * normMove;

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
                        GetDualLimbShares(i, weightManip, weightMove, normManip, normMove, out float manipShare, out float moveShare);
                        deficits.RawManipulationNegative += partNegative * manipShare;
                        deficits.RawMovingNegative += partNegative * moveShare;
                        break;
                }
            }

            deficits.ClampedCoreDeficit = Math.Min(deficits.RawCoreNegative, maxTorsoNegative);
            deficits.ClampedManipulationDeficit = Math.Min(deficits.RawManipulationNegative, maxArmNegative);
            deficits.ClampedMovingDeficit = Math.Min(deficits.RawMovingNegative, maxLegNegative);

            return deficits;
        }

        /// <summary>
        /// Resolves the normalized manipulation and moving budget fractions for a specific dual-limb part slot.
        /// </summary>
        /// <param name="partIndex">The index of the dual-limb part slot being evaluated.</param>
        /// <param name="weightManip">The array of manipulation weights for each part slot.</param>
        /// <param name="weightMove">The array of moving weights for each part slot.</param>
        /// <param name="normManip">The normalized manipulation budget fraction.</param>
        /// <param name="normMove">The normalized moving budget fraction.</param>
        /// <param name="manipShare">Output parameter for the manipulation share of the dual-limb part.</param>
        /// <param name="moveShare">Output parameter for the moving share of the dual-limb part.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetDualLimbShares(
            int partIndex,
            float[] weightManip,
            float[] weightMove,
            float normManip,
            float normMove,
            out float manipShare,
            out float moveShare)
        {
            if (weightManip != null && weightMove != null)
            {
                float manipWeight = weightManip[partIndex] * normManip;
                float moveWeight = weightMove[partIndex] * normMove;
                float combined = manipWeight + moveWeight;
                if (combined > 0f)
                {
                    manipShare = manipWeight / combined;
                    moveShare = moveWeight / combined;
                    return;
                }
            }

            float budgetSum = normManip + normMove;
            if (budgetSum > 0f)
            {
                manipShare = normManip / budgetSum;
                moveShare = normMove / budgetSum;
                return;
            }

            manipShare = 0.5f;
            moveShare = 0.5f;
        }

        #endregion

        #region 8. PRIVATE MULTI-LIMB RANKING HELPERS

        /// <summary>
        /// Computes zero-allocation, order-independent prosthetic pair multipliers per limb branch root for a single channel.
        /// Boosted limb branches (&gt; 100%) are ranked descending by weighted efficiency boost so the top two boosted branches
        /// receive the canonical 2-limb anchor (<c>totalRoots / 2</c>, matching <c>112.5% + 112.5% = 125%</c>) regardless of slot order,
        /// while subsequent boosted branches scale geometrically by <see cref="SolverConfiguration.LimbPairDecayFactor"/>.
        /// </summary>
        private static void ComputeChannelPairMultipliers(
            AnatomicalWorkspace workspace,
            float[] effs,
            float[] channelWeights,
            int[] rootIndices,
            int partCount,
            int totalChannelRoots,
            float pairDecayFactor,
            float[] branchMultipliers)
        {
            for (int i = 0; i < partCount; i++)
            {
                branchMultipliers[i] = 0f;
            }

            for (int i = 0; i < partCount; i++)
            {
                float w = channelWeights[i];
                if (w > 0f &&
                    !workspace.HasFlag(i, PartFlags.IsMissing) &&
                    workspace.HasFlag(i, PartFlags.HasAddedPart) &&
                    effs[i] > 1.0f)
                {
                    int rootIdx = (rootIndices != null && rootIndices[i] >= 0) ? rootIndices[i] : i;
                    branchMultipliers[rootIdx] += (effs[i] - 1.0f) * w;
                }
            }

            int[] sortScratch = workspace.BranchSortScratch;
            int boostedCount = 0;
            for (int i = 0; i < partCount; i++)
            {
                if (branchMultipliers[i] > 0f)
                {
                    sortScratch[boostedCount++] = i;
                }
            }

            // Insertion sort descending by boost score (branch count <= total roots; deterministic tie-break by root slot index)
            for (int i = 1; i < boostedCount; i++)
            {
                int keyRoot = sortScratch[i];
                float keyScore = branchMultipliers[keyRoot];
                int j = i - 1;
                while (j >= 0 && branchMultipliers[sortScratch[j]] < keyScore)
                {
                    sortScratch[j + 1] = sortScratch[j];
                    j--;
                }
                sortScratch[j + 1] = keyRoot;
            }

            int baseRoots = Math.Min(2, totalChannelRoots);
            float baseAnchorScale = (float)totalChannelRoots / (baseRoots <= 1 ? 1.0f : (float)baseRoots);

            for (int rank = 0; rank < boostedCount; rank++)
            {
                int rootIdx = sortScratch[rank];
                int pairIndex = rank / 2;
                branchMultipliers[rootIdx] = baseAnchorScale * Pass04_AppendageWeightSubCompiler.FastIntPow(pairDecayFactor, pairIndex);
            }
        }

        #endregion
    }
}
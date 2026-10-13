using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [PASS-03 & PASS-04] CORE MATHEMATICAL MASS CAPACITY SOLVER ENGINE
    /// Stateless computational engine executing vector mathematics across parallel contiguous SoA arrays.
    /// Operates on contiguous memory streams off the main thread to calculate biological Caravan Mass Capacity offsets (kg).
    /// Resides under Source/Core/Solvers/.
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

        #region 2. INTERNAL REGIONAL DEFICITS & DELTA STRUCTS

        /// <summary>
        /// Zero-allocation stack wrapper adapting an <see cref="IAnatomicalDataSource"/> reference into the struct solver pipeline.
        /// Prevents virtual interface dispatch and boxing when non-struct sources are supplied.
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
        /// Tracks the ratio of clamped capacity to raw health loss to scale regional deficits proportionally.
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
            /// Ensures deficits collapse smoothly onto their allocated budget ceiling without sharp threshold steps.
            /// </summary>
            /// <param name="type">The anatomical part type for which to retrieve the ratio.</param>
            /// <returns>The ratio of the clamped deficit to the raw negative value for the specified part type (defaults to 1.0f if no deficits exist).</returns>
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
            /// <param name="snappedCapacity">The snapped biological capacity level being evaluated (1.0f = baseline).</param>
            /// <param name="posTorso">The positive coupling weight for the torso.</param>
            /// <param name="posArm">The positive coupling weight for the arms.</param>
            /// <param name="posLeg">The positive coupling weight for the legs.</param>
            /// <param name="defTorso">The negative deficit coupling weight for the torso.</param>
            /// <param name="defArm">The negative deficit coupling weight for the arms.</param>
            /// <param name="defLeg">The negative deficit coupling weight for the legs.</param>
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
            /// <param name="positiveDelta">Outputs the positive capacity delta for the specified region.</param>
            /// <param name="negativeDelta">Outputs the negative capacity delta for the specified region.</param>
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

        #region 3. PRIMARY PUBLIC APIS & CANONICAL ENTRY POINTS

        /// <summary>
        /// [PASS-03 & PASS-04] Primary canonical solver calculating the final Caravan Mass Capacity offset in kilograms (kg) for an abstract data source.
        /// Constrained to struct to guarantee zero heap allocations, eliminate interface boxing, and ensure strict C# 7.3 compatibility.
        /// Operates with complete thread isolation and encapsulated workspace lifecycle management.
        /// </summary>
        /// <typeparam name="TSource">The concrete anatomical data source struct type implementing <see cref="IAnatomicalDataSource"/>.</typeparam>
        /// <param name="source">The anatomical data source representing the pawn.</param>
        /// <param name="baselineCapacity">The baseline mass capacity of the pawn in kilograms before physiological modifications.</param>
        /// <param name="biologicalBaseline">Outputs the resolved biological baseline mass capacity of the pawn in kilograms.</param>
        /// <param name="settings">The mod settings influencing scaling, budget weights, and safety thresholds.</param>
        /// <param name="shouldCompileUIProperties">True if human-readable labels, ailments, and colors should be stamped onto cold workspace state.</param>
        /// <param name="workspace">Outputs the leased anatomical workspace populated during the calculation.</param>
        /// <returns>The calculated physical mass capacity offset in kilograms (kg), where 0.0f indicates parity with baseline.</returns>
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
                // Initialize workspace from thread-local pool
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
                snapshot.Consciousness = (workspace != null && workspace.CachedConsciousness >= 0f)
                    ? workspace.CachedConsciousness
                    : source.GetCapacityLevel(PawnCapacityDefOf.Consciousness);

                SystemicEvaluationContext context = SystemicEvaluationContext.CreateFromSnapshot(
                    biologicalBaseline, partCounts, settings, snapshot);

                float totalPlayableBudget = Math.Max(0f, biologicalBaseline - context.CapacityFloor);

                // PASS 03: Parallel vector calculations across contiguous SoA arrays (Smooth, unclamped)
                SolveRawPartOffsets(workspace, context, totalPlayableBudget);

                // PASS 04: Regional budget clamping and proportional deficit scaling
                RegionalDeficits deficits = CalculateClampedGroupBudgetDeficits(workspace, context, totalPlayableBudget, out float totalPositiveBoosts);

                // The net biological delta: positive boosts minus clamped deficits
                float netBiologicalOffset = totalPositiveBoosts - deficits.TotalClampedDeficit;

                // Normalize workspace calculated totals with the exact matching deficits
                NormalizeWorkspaceCalculatedTotals(workspace, in deficits);

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
                // Ensure the workspace is released back to the pool if an unhandled exception aborted calculation
                if (!isSuccessful && workspace != null)
                {
                    WorkspacePool.ReleaseWorkspace(workspace);
                    workspace = null;
                }
            }
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

            if (finalCapacity < 0.01f)
            {
                float clampedValue = 0.01f;
                OHLog.Performance.RecordSafetyFloorClamped(targetLabel, finalCapacity, clampedValue);
                return clampedValue;
            }

            return finalCapacity;
        }

        #endregion

        #region 4. SECONDARY ENTRY POINTS (PASS-THROUGHS & HAND-OFFS)

        /// <summary>
        /// [PASS-03 & PASS-04] Interface adapter solving mass capacity offset for an <see cref="IAnatomicalDataSource"/> instance.
        /// Thin pass-through wrapping the interface into a <see cref="BoxedDataSourceWrapper"/> to execute the canonical generic solver.
        /// </summary>
        /// <param name="source">The anatomical data source representing the entity.</param>
        /// <param name="baselineCapacity">The baseline mass capacity in kilograms before physiological modifications.</param>
        /// <param name="biologicalBaseline">Outputs the resolved biological baseline mass capacity in kilograms.</param>
        /// <param name="settings">The mod settings influencing scaling, budget weights, and safety thresholds.</param>
        /// <param name="shouldCompileUIProperties">True if human-readable labels, ailments, and colors should be stamped onto cold workspace state.</param>
        /// <param name="workspace">Outputs the leased anatomical workspace populated during calculation.</param>
        /// <returns>The calculated physical mass capacity offset in kilograms (kg).</returns>
        public static float SolveMassCapacityOffsetForSource(
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
        /// Solves the mass capacity offset for a live <see cref="Pawn"/> instance.
        /// Thin pass-through wrapping the pawn into a stack-allocated <see cref="PawnAnatomicalSource"/> to execute the canonical generic solver with zero heap allocations.
        /// </summary>
        /// <param name="pawn">The live pawn entity being evaluated.</param>
        /// <param name="baselineCapacity">The baseline mass capacity of the pawn in kilograms.</param>
        /// <param name="biologicalBaseline">Outputs the resolved biological baseline mass capacity in kilograms.</param>
        /// <param name="settings">The mod settings influencing scaling, budget weights, and safety thresholds.</param>
        /// <param name="shouldCompileUIProperties">True if human-readable labels and cold presentation metadata should be extracted.</param>
        /// <param name="workspace">Outputs the leased anatomical workspace populated during calculation.</param>
        /// <returns>The calculated physical mass capacity offset in kilograms (kg).</returns>
        public static float SolveMassCapacityOffsetForPawn(
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

        /// <summary>
        /// Evaluates and clamps the final capacity for a pawn against the safety floor.
        /// Acts as a zero-allocation pass-through hand-off to <see cref="EnforceSafetyFloor"/>,
        /// lazily resolving the pawn label string only when safety floor clamping is actively triggered.
        /// </summary>
        /// <param name="finalCapacity">The calculated final caravan mass capacity in kilograms to be clamped.</param>
        /// <param name="pawn">The pawn entity being evaluated.</param>
        /// <returns>The clamped final capacity in kilograms.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EnforceSafetyFloorForPawn(float finalCapacity, Pawn pawn)
        {
            if (finalCapacity < 0.01f)
            {
                string targetLabel = pawn != null
                    ? (UnityData.IsInMainThread ? pawn.LabelShortCap.ToString() : pawn.def?.label ?? "Pawn")
                    : "Pawn";

                return EnforceSafetyFloor(finalCapacity, targetLabel);
            }

            return finalCapacity;
        }

        #endregion

        #region 5. PATHOLOGY COMPILATION & WORKSPACE INGRESS

        /// <summary>
        /// Compiles pathology data into the workspace. Dispatches directly without interface boxing.
        /// </summary>
        /// <typeparam name="TSource">The concrete anatomical data source struct type.</typeparam>
        /// <param name="source">The data source containing entity health and hediff information.</param>
        /// <param name="template">The precompiled species topology template.</param>
        /// <param name="workspace">The destination anatomical workspace.</param>
        /// <param name="shouldCompileUIProperties">True if human-readable labels and cold metadata should be extracted.</param>
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

        #region 6. [PASS-03] STREAMING SoA VECTOR MATHEMATICS

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
            SystemicRegionalDeltas regionalDeltas = default;

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
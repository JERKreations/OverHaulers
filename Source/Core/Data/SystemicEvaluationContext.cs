using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [SEC-01] IMMUTABLE SYSTEMIC EVALUATION CONTEXT
    /// Value-type context capturing biological capacities, category weight budgets,
    /// and pre-calculated anchor scales for a single calculation pass.
    /// Used as a value type in off-thread calculations.
    /// Resides under Source/Core/Data/.
    /// </summary>
    public struct SystemicEvaluationContext
    {
        #region 1. CONTEXT FIELDS & CAPACITY READERS

        // Systemic Part Counts
        public PartCounts Counts;

        // Biological Capacity Snapshots
        public float SnappedBreathing;
        public float SnappedBloodPumping;
        public float SnappedMoving;
        public float SnappedManipulation;
        public float SnappedConsciousness;

        public float ConfigurablePercentileFloorKg;

        // Regional Budgets
        public float BudgetCorePart;
        public float BudgetManipulationPart;
        public float BudgetMovingPart;
        public float TotalMassImpactWeight;

        // Toggles
        public bool EnableProsthetics;
        public bool EnableAthletics;
        public bool EnablePartHealth;

        // Scaling Multipliers
        public float AthleticScalingMultiplier;

        // Pre-Calculated Anchor Scales
        public float ProstheticAnchorScale;
        public float AthleticAnchorScale;

        // [SEC-03] Positive Coupling Weights
        public float TorsoPositiveBreathing;
        public float TorsoPositiveBlood;
        public float TorsoPositiveMoving;
        public float TorsoPositiveManipulation;

        public float ArmPositiveBreathing;
        public float ArmPositiveBlood;
        public float ArmPositiveMoving;
        public float ArmPositiveManipulation;

        public float LegPositiveBreathing;
        public float LegPositiveBlood;
        public float LegPositiveMoving;
        public float LegPositiveManipulation;

        // [SEC-04] Negative Deficit Coupling Weights
        public float TorsoDeficitBreathing;
        public float TorsoDeficitBlood;
        public float TorsoDeficitMoving;
        public float TorsoDeficitManipulation;

        public float ArmDeficitBreathing;
        public float ArmDeficitBlood;
        public float ArmDeficitMoving;
        public float ArmDeficitManipulation;

        public float LegDeficitBreathing;
        public float LegDeficitBlood;
        public float LegDeficitMoving;
        public float LegDeficitManipulation;

        public float AthleticMultiplier;

        /// <summary>
        /// Reads the snapshot level of the specified biological capacity from the context.
        /// </summary>
        /// <param name="capacity">The pawn capacity definition to read.</param>
        /// <returns>The recorded capacity level, defaulting to 1.0f if unmapped.</returns>
        public float GetCapacityLevel(PawnCapacityDef capacity)
        {
            if (capacity == PawnCapacityDefOf.Breathing) return SnappedBreathing;
            if (capacity == PawnCapacityDefOf.BloodPumping) return SnappedBloodPumping;
            if (capacity == PawnCapacityDefOf.Moving) return SnappedMoving;
            if (capacity == PawnCapacityDefOf.Manipulation) return SnappedManipulation;
            if (capacity == PawnCapacityDefOf.Consciousness) return SnappedConsciousness;
            return 1.0f;
        }

        /// <summary>
        /// Retrieves the normalized budget share (0.00 to 1.00) for a target anatomical region.
        /// </summary>
        /// <param name="type">The target anatomical region.</param>
        /// <returns>The normalized budget share for the specified region.</returns>
        public float GetNormalizedBudget(PartType type)
        {
            if (TotalMassImpactWeight <= 0f) return 0f;
            if (type == PartType.CorePart) return BudgetCorePart / TotalMassImpactWeight;
            if (type == PartType.ManipulationPart) return BudgetManipulationPart / TotalMassImpactWeight;
            if (type == PartType.MovingPart) return BudgetMovingPart / TotalMassImpactWeight;
            if (type == PartType.DualLimb) return (BudgetManipulationPart + BudgetMovingPart) / TotalMassImpactWeight;
            return 0f;
        }

        #endregion

        #region 2. STATELESS SNAPSHOT FACTORY

        /// <summary>
        /// Creates a new systemic evaluation context from the given snapshot and immutable solver configuration.
        /// Completely decoupled from Settings class references.
        /// </summary>
        /// <param name="cleanBaseline">The baseline value for clean scaling.</param>
        /// <param name="partCounts">The counts of various body parts.</param>
        /// <param name="config">The immutable solver configuration snapshot.</param>
        /// <param name="snapshot">The snapshot of biological capacities.</param>
        /// <returns>A new instance of SystemicEvaluationContext initialized with the provided data.</returns>
        public static SystemicEvaluationContext CreateFromSnapshot(
            float cleanBaseline, 
            in PartCounts partCounts, 
            in SolverConfiguration config,
            in BiologicalCapacitySnapshot snapshot)
        {
            SystemicEvaluationContext context = new SystemicEvaluationContext();

            context.Counts = partCounts;

            context.SnappedBreathing = snapshot.Breathing;
            context.SnappedBloodPumping = snapshot.BloodPumping;
            context.SnappedMoving = snapshot.Moving;
            context.SnappedManipulation = snapshot.Manipulation;
            context.SnappedConsciousness = snapshot.Consciousness;

            context.ConfigurablePercentileFloorKg = cleanBaseline * config.ConfigurablePercentileFloor;

            context.EnableProsthetics = config.EnableProsthetics;
            context.EnableAthletics = config.EnableAthletics;
            context.EnablePartHealth = config.EnablePartHealth;

            context.AthleticScalingMultiplier = config.AthleticScaling;

            context.ProstheticAnchorScale = cleanBaseline * config.ProstheticScaling * config.ProstheticImpactConstant;
            context.AthleticAnchorScale = cleanBaseline * config.AthleticScaling * config.AthleticImpactConstant;

            // [SEC-03] Positive Weights
            context.TorsoPositiveBreathing = config.TorsoPositiveBreathing;
            context.TorsoPositiveBlood = config.TorsoPositiveBlood;
            context.TorsoPositiveMoving = config.TorsoPositiveMoving;
            context.TorsoPositiveManipulation = config.TorsoPositiveManipulation;

            context.ArmPositiveBreathing = config.ArmPositiveBreathing;
            context.ArmPositiveBlood = config.ArmPositiveBlood;
            context.ArmPositiveMoving = config.ArmPositiveMoving;
            context.ArmPositiveManipulation = config.ArmPositiveManipulation;

            context.LegPositiveBreathing = config.LegPositiveBreathing;
            context.LegPositiveBlood = config.LegPositiveBlood;
            context.LegPositiveMoving = config.LegPositiveMoving;
            context.LegPositiveManipulation = config.LegPositiveManipulation;

            // [SEC-04] Negative Deficit Weights
            context.TorsoDeficitBreathing = config.TorsoDeficitBreathing;
            context.TorsoDeficitBlood = config.TorsoDeficitBlood;
            context.TorsoDeficitMoving = config.TorsoDeficitMoving;
            context.TorsoDeficitManipulation = config.TorsoDeficitManipulation;

            context.ArmDeficitBreathing = config.ArmDeficitBreathing;
            context.ArmDeficitBlood = config.ArmDeficitBlood;
            context.ArmDeficitMoving = config.ArmDeficitMoving;
            context.ArmDeficitManipulation = config.ArmDeficitManipulation;

            context.LegDeficitBreathing = config.LegDeficitBreathing;
            context.LegDeficitBlood = config.LegDeficitBlood;
            context.LegDeficitMoving = config.LegDeficitMoving;
            context.LegDeficitManipulation = config.LegDeficitManipulation;

            // Canonical Regional Budget Redistribution & Normalization (SSoT)
            ResolveNormalizedBudgets(
                in partCounts,
                in config,
                out context.BudgetCorePart,
                out context.BudgetManipulationPart,
                out context.BudgetMovingPart);

            float totalBudget = context.BudgetCorePart + context.BudgetManipulationPart + context.BudgetMovingPart;
            context.TotalMassImpactWeight = totalBudget > 0f ? totalBudget : 1.0f;

            // Calculate athletic multiplier based on capacity deviations
            if (!context.EnableAthletics)
            {
                context.AthleticMultiplier = 1.0f;
            }
            else
            {
                float athleticDeltaSum = 0f;

                // Sum deviations of athletic capacities
                for (int i = 0; i < MedicalClassifier.AthleticCapacities.Length; i++)
                {
                    PawnCapacityDef capacity = MedicalClassifier.AthleticCapacities[i];
                    float level = context.GetCapacityLevel(capacity);
                    athleticDeltaSum += (level - 1.0f) * 0.25f;
                }

                context.AthleticMultiplier = 1.0f + athleticDeltaSum;

                // Clamp non-positive aggregate efficiency to 0%
                if (context.AthleticMultiplier < 0f)
                {
                    context.AthleticMultiplier = 0f;
                }
            }

            return context;
        }

        /// <summary>
        /// [SOLV-SSOT] Canonical Single Source of Truth for regional mass budget redistribution and normalization.
        /// Consumed symmetrically by <see cref="Pass04_AppendageWeightSubCompiler"/>, <see cref="CreateFromSnapshot"/>,
        /// Test Bench X-Ray views, and topology exporters.
        /// </summary>
        /// <param name="counts">The part counts for the current pawn.</param>
        /// <param name="config">The solver configuration containing budget and share settings.</param>
        /// <param name="normTorso">Normalized torso budget output.</param>
        /// <param name="normManip">Normalized manipulation budget output.</param>
        /// <param name="normMove">Normalized moving budget output.</param>
        public static void ResolveNormalizedBudgets(
            in PartCounts counts,
            in SolverConfiguration config,
            out float normTorso,
            out float normManip,
            out float normMove)
        {
            float budgetTorso = config.BudgetCorePart;
            float budgetManip = config.BudgetManipulationPart;
            float budgetMove = config.BudgetMovingPart;

            bool hasCore = counts.CoreParts > 0;
            bool hasManip = counts.TotalManipulationRoots > 0;
            bool hasMove = counts.TotalMovingRoots > 0;

            // 1. Missing Manipulation Limbs -> configurable split between Torso and Moving Limbs
            if (!hasManip && budgetManip > 0f)
            {
                if (hasCore && hasMove)
                {
                    float torsoShare = config.MissingArmsTorsoShare;
                    budgetTorso += budgetManip * torsoShare;
                    budgetMove += budgetManip * (1.0f - torsoShare);
                }
                else if (hasCore)
                {
                    budgetTorso += budgetManip;
                }
                else if (hasMove)
                {
                    budgetMove += budgetManip;
                }
                budgetManip = 0f;
            }

            // 2. Missing Moving Limbs -> configurable split between Torso and Manipulation Limbs
            if (!hasMove && budgetMove > 0f)
            {
                if (hasCore && hasManip)
                {
                    float torsoShare = config.MissingLegsTorsoShare;
                    budgetTorso += budgetMove * torsoShare;
                    budgetManip += budgetMove * (1.0f - torsoShare);
                }
                else if (hasCore)
                {
                    budgetTorso += budgetMove;
                }
                else if (hasManip)
                {
                    budgetManip += budgetMove;
                }
                budgetMove = 0f;
            }

            // 3. Defensive fallback for exotic archetypes with CoreParts == 0 via mod extensions
            if (!hasCore && budgetTorso > 0f)
            {
                if (hasManip && hasMove)
                {
                    float limbSum = budgetManip + budgetMove;
                    if (limbSum > 0f)
                    {
                        budgetManip += budgetTorso * (budgetManip / limbSum);
                        budgetMove += budgetTorso * (budgetMove / limbSum);
                    }
                    else
                    {
                        budgetManip += budgetTorso * 0.5f;
                        budgetMove += budgetTorso * 0.5f;
                    }
                }
                else if (hasManip)
                {
                    budgetManip += budgetTorso;
                }
                else if (hasMove)
                {
                    budgetMove += budgetTorso;
                }
                budgetTorso = 0f;
            }

            float totalBudget = budgetTorso + budgetManip + budgetMove;
            if (totalBudget <= 0f) totalBudget = 1.0f;

            normTorso = budgetTorso / totalBudget;
            normManip = budgetManip / totalBudget;
            normMove = budgetMove / totalBudget;
        }

        #endregion
    }
}
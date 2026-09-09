using RimWorld;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// Immutable value-type context capturing biological capacities, category weight budgets,
    /// and pre-calculated anchor scales for a single calculation pass.
    /// Passes on the stack with 0 bytes GC allocation for high-performance off-thread calculations.
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

        public float CapacityFloor;

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
        public float PartHealthScalingMultiplier;
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

        /// <summary>
        /// Retrieves the positive systemic coupling weight for a region and capacity index (0=Breathing, 1=Blood, 2=Moving, 3=Manipulation).
        /// </summary>
        /// <param name="type">The target anatomical region.</param>
        /// <param name="capacityIndex">The capacity index (0=Breathing, 1=Blood, 2=Moving, 3=Manipulation).</param>
        /// <returns>The positive systemic coupling weight for the specified region and capacity index.</returns>
        public float GetPositiveCouplingWeight(PartType type, int capacityIndex)
        {
            if (type == PartType.CorePart)
            {
                if (capacityIndex == 0) return TorsoPositiveBreathing;
                if (capacityIndex == 1) return TorsoPositiveBlood;
                if (capacityIndex == 2) return TorsoPositiveMoving;
                return TorsoPositiveManipulation;
            }
            if (type == PartType.ManipulationPart)
            {
                if (capacityIndex == 0) return ArmPositiveBreathing;
                if (capacityIndex == 1) return ArmPositiveBlood;
                if (capacityIndex == 2) return ArmPositiveMoving;
                return ArmPositiveManipulation;
            }
            if (type == PartType.MovingPart)
            {
                if (capacityIndex == 0) return LegPositiveBreathing;
                if (capacityIndex == 1) return LegPositiveBlood;
                if (capacityIndex == 2) return LegPositiveMoving;
                return LegPositiveManipulation;
            }
            if (type == PartType.DualLimb)
            {
                float armWeight = GetPositiveCouplingWeight(PartType.ManipulationPart, capacityIndex);
                float legWeight = GetPositiveCouplingWeight(PartType.MovingPart, capacityIndex);
                return (armWeight + legWeight) * 0.5f;
            }
            return 1.0f;
        }

        /// <summary>
        /// Retrieves the negative deficit coupling weight for a region and capacity index (0=Breathing, 1=Blood, 2=Moving, 3=Manipulation).
        /// </summary>
        /// <param name="type">The target anatomical region.</param>
        /// <param name="capacityIndex">The capacity index (0=Breathing, 1=Blood, 2=Moving, 3=Manipulation).</param>
        /// <returns>The negative deficit coupling weight for the specified region and capacity index.</returns>
        public float GetCouplingWeight(PartType type, int capacityIndex)
        {
            if (type == PartType.CorePart)
            {
                if (capacityIndex == 0) return TorsoDeficitBreathing;
                if (capacityIndex == 1) return TorsoDeficitBlood;
                if (capacityIndex == 2) return TorsoDeficitMoving;
                return TorsoDeficitManipulation;
            }
            if (type == PartType.ManipulationPart)
            {
                if (capacityIndex == 0) return ArmDeficitBreathing;
                if (capacityIndex == 1) return ArmDeficitBlood;
                if (capacityIndex == 2) return ArmDeficitMoving;
                return ArmDeficitManipulation;
            }
            if (type == PartType.MovingPart)
            {
                if (capacityIndex == 0) return LegDeficitBreathing;
                if (capacityIndex == 1) return LegDeficitBlood;
                if (capacityIndex == 2) return LegDeficitMoving;
                return LegDeficitManipulation;
            }
            if (type == PartType.DualLimb)
            {
                float armCoupling = GetCouplingWeight(PartType.ManipulationPart, capacityIndex);
                float legCoupling = GetCouplingWeight(PartType.MovingPart, capacityIndex);
                return (armCoupling + legCoupling) * 0.5f;
            }
            return 1.0f;
        }

        #endregion

        #region 2. STATELESS SNAPSHOT FACTORY

        /// <summary>
        /// Creates a new systemic evaluation context from the given snapshot and settings.
        /// </summary>
        /// <param name="cleanBaseline">The baseline value for clean scaling.</param>
        /// <param name="partCounts">The counts of various body parts.</param>
        /// <param name="settings">The systemic evaluation settings.</param>
        /// <param name="snapshot">The snapshot of biological capacities.</param>
        /// <returns>A new instance of SystemicEvaluationContext initialized with the provided data.</returns>
        public static SystemicEvaluationContext CreateFromSnapshot(
            float cleanBaseline, 
            in PartCounts partCounts, 
            Settings settings,
            in BiologicalCapacitySnapshot snapshot)
        {
            SystemicEvaluationContext context = new SystemicEvaluationContext();
            if (settings == null) return context;

            context.Counts = partCounts;

            context.SnappedBreathing = snapshot.Breathing;
            context.SnappedBloodPumping = snapshot.BloodPumping;
            context.SnappedMoving = snapshot.Moving;
            context.SnappedManipulation = snapshot.Manipulation;
            context.SnappedConsciousness = snapshot.Consciousness;

            context.CapacityFloor = cleanBaseline * settings.massCapacityFloor;

            context.EnableProsthetics = settings.enableProsthetics;
            context.EnableAthletics = settings.enableAthletics;
            context.EnablePartHealth = settings.enablePartHealth;

            context.AthleticScalingMultiplier = settings.athleticScaling;

            context.ProstheticAnchorScale = cleanBaseline * settings.prostheticScaling * settings.prostheticImpactConstant;
            context.AthleticAnchorScale = cleanBaseline * settings.athleticScaling * settings.athleticImpactConstant;

            // [SEC-03] Positive Weights
            context.TorsoPositiveBreathing = settings.torsoPositiveBreathing;
            context.TorsoPositiveBlood = settings.torsoPositiveBlood;
            context.TorsoPositiveMoving = settings.torsoPositiveMoving;
            context.TorsoPositiveManipulation = settings.torsoPositiveManipulation;

            context.ArmPositiveBreathing = settings.armPositiveBreathing;
            context.ArmPositiveBlood = settings.armPositiveBlood;
            context.ArmPositiveMoving = settings.armPositiveMoving;
            context.ArmPositiveManipulation = settings.armPositiveManipulation;

            context.LegPositiveBreathing = settings.legPositiveBreathing;
            context.LegPositiveBlood = settings.legPositiveBlood;
            context.LegPositiveMoving = settings.legPositiveMoving;
            context.LegPositiveManipulation = settings.legPositiveManipulation;

            // [SEC-04] Negative Deficit Weights
            context.TorsoDeficitBreathing = settings.torsoDeficitBreathing;
            context.TorsoDeficitBlood = settings.torsoDeficitBlood;
            context.TorsoDeficitMoving = settings.torsoDeficitMoving;
            context.TorsoDeficitManipulation = settings.torsoDeficitManipulation;

            context.ArmDeficitBreathing = settings.armDeficitBreathing;
            context.ArmDeficitBlood = settings.armDeficitBlood;
            context.ArmDeficitMoving = settings.armDeficitMoving;
            context.ArmDeficitManipulation = settings.armDeficitManipulation;

            context.LegDeficitBreathing = settings.legDeficitBreathing;
            context.LegDeficitBlood = settings.legDeficitBlood;
            context.LegDeficitMoving = settings.legDeficitMoving;
            context.LegDeficitManipulation = settings.legDeficitManipulation;

            context.BudgetCorePart = settings.GetBudget(PartType.CorePart);
            context.BudgetManipulationPart = settings.GetBudget(PartType.ManipulationPart);
            context.BudgetMovingPart = settings.GetBudget(PartType.MovingPart);

            // Redistribute manipulation and moving budgets if there are no corresponding roots
            if (partCounts.TotalManipulationRoots == 0 && context.BudgetManipulationPart > 0f)
            {
                context.BudgetCorePart += context.BudgetManipulationPart * 0.67f;
                context.BudgetMovingPart += context.BudgetManipulationPart * 0.33f;
                context.BudgetManipulationPart = 0f;
            }

            // Redistribute moving budget if there are no corresponding roots
            if (partCounts.TotalMovingRoots == 0 && context.BudgetMovingPart > 0f)
            {
                context.BudgetCorePart += context.BudgetMovingPart;
                context.BudgetMovingPart = 0f;
            }

            // Calculate total mass impact weight
            context.TotalMassImpactWeight = context.BudgetCorePart + context.BudgetManipulationPart + context.BudgetMovingPart;

            // Ensure total mass impact weight is positive
            if (context.TotalMassImpactWeight <= 0f)
            {
                context.TotalMassImpactWeight = 1.0f;
            }

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

                // Ensure multiplier is never non-positive
                if (context.AthleticMultiplier <= 0f)
                {
                    context.AthleticMultiplier = 1.0f;
                }
            }

            return context;
        }

        #endregion
    }
}
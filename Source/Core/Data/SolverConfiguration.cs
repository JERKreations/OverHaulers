namespace OverHaulers
{
    /// <summary>
    /// [CORE-CONF-01] IMMUTABLE VALUE-TYPE SOLVER CONFIGURATION
    /// Pure data transfer snapshot providing tuning parameters to Core mathematical solvers and topology compilers.
    /// Resides under Source/Core/Data/.
    /// </summary>
    public readonly struct SolverConfiguration
    {
        #region 1. INSTANCE FIELDS & IMMUTABLE BACKING STATE

        public readonly float MassCapacityFloor;
        public readonly bool EnableProsthetics;
        public readonly bool EnableAthletics;
        public readonly bool EnablePartHealth;
        public readonly bool CompatibilitySafetyFloor;

        public readonly float AthleticScaling;
        public readonly float AthleticImpactConstant;
        public readonly float ProstheticScaling;
        public readonly float ProstheticImpactConstant;

        public readonly float TorsoAxialBias;
        public readonly float TorsoHpSensitivity;
        public readonly float LimbDepthDecayFactor;

        public readonly float TorsoPositiveBreathing;
        public readonly float TorsoPositiveBlood;
        public readonly float TorsoPositiveMoving;
        public readonly float TorsoPositiveManipulation;

        public readonly float ArmPositiveBreathing;
        public readonly float ArmPositiveBlood;
        public readonly float ArmPositiveMoving;
        public readonly float ArmPositiveManipulation;

        public readonly float LegPositiveBreathing;
        public readonly float LegPositiveBlood;
        public readonly float LegPositiveMoving;
        public readonly float LegPositiveManipulation;

        public readonly float TorsoDeficitBreathing;
        public readonly float TorsoDeficitBlood;
        public readonly float TorsoDeficitMoving;
        public readonly float TorsoDeficitManipulation;

        public readonly float ArmDeficitBreathing;
        public readonly float ArmDeficitBlood;
        public readonly float ArmDeficitMoving;
        public readonly float ArmDeficitManipulation;

        public readonly float LegDeficitBreathing;
        public readonly float LegDeficitBlood;
        public readonly float LegDeficitMoving;
        public readonly float LegDeficitManipulation;

        public readonly float BudgetCorePart;
        public readonly float BudgetManipulationPart;
        public readonly float BudgetMovingPart;

        #endregion

        #region 2. LIFECYCLE: CONSTRUCTOR

        public SolverConfiguration(
            float massCapacityFloor,
            bool enableProsthetics,
            bool enableAthletics,
            bool enablePartHealth,
            bool compatibilitySafetyFloor,
            float athleticScaling,
            float athleticImpactConstant,
            float prostheticScaling,
            float prostheticImpactConstant,
            float torsoAxialBias,
            float torsoHpSensitivity,
            float limbDepthDecayFactor,
            float torsoPosBreathe, float torsoPosBlood, float torsoPosMove, float torsoPosManip,
            float armPosBreathe,   float armPosBlood,   float armPosMove,   float armPosManip,
            float legPosBreathe,   float legPosBlood,   float legPosMove,   float legPosManip,
            float torsoDefBreathe, float torsoDefBlood, float torsoDefMove, float torsoDefManip,
            float armDefBreathe,   float armDefBlood,   float armDefMove,   float armDefManip,
            float legDefBreathe,   float legDefBlood,   float legDefMove,   float legDefManip,
            float budgetCore,      float budgetManip,   float budgetMove)
        {
            MassCapacityFloor = massCapacityFloor;
            EnableProsthetics = enableProsthetics;
            EnableAthletics = enableAthletics;
            EnablePartHealth = enablePartHealth;
            CompatibilitySafetyFloor = compatibilitySafetyFloor;

            AthleticScaling = athleticScaling;
            AthleticImpactConstant = athleticImpactConstant;
            ProstheticScaling = prostheticScaling;
            ProstheticImpactConstant = prostheticImpactConstant;

            TorsoAxialBias = torsoAxialBias;
            TorsoHpSensitivity = torsoHpSensitivity;
            LimbDepthDecayFactor = limbDepthDecayFactor;

            TorsoPositiveBreathing = torsoPosBreathe;
            TorsoPositiveBlood = torsoPosBlood;
            TorsoPositiveMoving = torsoPosMove;
            TorsoPositiveManipulation = torsoPosManip;

            ArmPositiveBreathing = armPosBreathe;
            ArmPositiveBlood = armPosBlood;
            ArmPositiveMoving = armPosMove;
            ArmPositiveManipulation = armPosManip;

            LegPositiveBreathing = legPosBreathe;
            LegPositiveBlood = legPosBlood;
            LegPositiveMoving = legPosMove;
            LegPositiveManipulation = legPosManip;

            TorsoDeficitBreathing = torsoDefBreathe;
            TorsoDeficitBlood = torsoDefBlood;
            TorsoDeficitMoving = torsoDefMove;
            TorsoDeficitManipulation = torsoDefManip;

            ArmDeficitBreathing = armDefBreathe;
            ArmDeficitBlood = armDefBlood;
            ArmDeficitMoving = armDefMove;
            ArmDeficitManipulation = armDefManip;

            LegDeficitBreathing = legDefBreathe;
            LegDeficitBlood = legDefBlood;
            LegDeficitMoving = legDefMove;
            LegDeficitManipulation = legDefManip;

            BudgetCorePart = budgetCore;
            BudgetManipulationPart = budgetManip;
            BudgetMovingPart = budgetMove;
        }

        #endregion
    }
}
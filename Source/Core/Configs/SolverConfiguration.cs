namespace OverHaulers
{
    /// <summary>
    /// [CORE-CONF-01] IMMUTABLE VALUE-TYPE SOLVER CONFIGURATION
    /// Pure data transfer snapshot providing tuning parameters to Core mathematical solvers and topology compilers.
    /// Resides under Source/Core/Configs/.
    /// </summary>
    public readonly struct SolverConfiguration
    {
        #region 1. INSTANCE FIELDS & IMMUTABLE BACKING STATE

        public readonly float ConfigurablePercentileFloor;
        public readonly bool EnableProsthetics;
        public readonly bool EnableAthletics;
        public readonly bool EnablePartHealth;
        public readonly bool EnableHardCompatibilitySafetyFloor;

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
            float configurablePercentileFloor,
            bool enableProsthetics,
            bool enableAthletics,
            bool enablePartHealth,
            bool enableHardCompatibilitySafetyFloor,
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
            ConfigurablePercentileFloor = configurablePercentileFloor;
            EnableProsthetics = enableProsthetics;
            EnableAthletics = enableAthletics;
            EnablePartHealth = enablePartHealth;
            EnableHardCompatibilitySafetyFloor = enableHardCompatibilitySafetyFloor;

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

        #region 3. SANITISATION

        /// <summary>
        /// Returns a copy with every tunable clamped to its <see cref="CoreDefaults"/> range, so Core never depends on the caller having validated.
        /// Out-of-range values (infinities included) resolve to the bound they exceeded; NaN resolves to the default.
        /// </summary>
        /// <param name="wasModified">True when at least one value had to be adjusted.</param>
        /// <returns>A configuration guaranteed to be finite and within range.</returns>
        public SolverConfiguration Sanitized(out bool wasModified)
        {
            bool c = false;
            SolverConfiguration result = new SolverConfiguration(
                ConfigBounds.ClampFloat(ConfigurablePercentileFloor, CoreDefaults.ConfigurablePercentileFloor, CoreDefaults.ConfigurablePercentileFloorMin, CoreDefaults.ConfigurablePercentileFloorMax, ref c),
                EnableProsthetics,
                EnableAthletics,
                EnablePartHealth,
                EnableHardCompatibilitySafetyFloor,
                ConfigBounds.ClampFloat(AthleticScaling, CoreDefaults.AthleticScaling, CoreDefaults.AthleticScalingMin, CoreDefaults.AthleticScalingMax, ref c),
                ConfigBounds.ClampFloat(AthleticImpactConstant, CoreDefaults.AthleticImpactConstant, CoreDefaults.AthleticImpactConstantMin, CoreDefaults.AthleticImpactConstantMax, ref c),
                ConfigBounds.ClampFloat(ProstheticScaling, CoreDefaults.ProstheticScaling, CoreDefaults.ProstheticScalingMin, CoreDefaults.ProstheticScalingMax, ref c),
                ConfigBounds.ClampFloat(ProstheticImpactConstant, CoreDefaults.ProstheticImpactConstant, CoreDefaults.ProstheticImpactConstantMin, CoreDefaults.ProstheticImpactConstantMax, ref c),
                ConfigBounds.ClampFloat(TorsoAxialBias, CoreDefaults.TorsoAxialBias, CoreDefaults.TorsoAxialBiasMin, CoreDefaults.TorsoAxialBiasMax, ref c),
                ConfigBounds.ClampFloat(TorsoHpSensitivity, CoreDefaults.TorsoHpSensitivity, CoreDefaults.TorsoHpSensitivityMin, CoreDefaults.TorsoHpSensitivityMax, ref c),
                ConfigBounds.ClampFloat(LimbDepthDecayFactor, CoreDefaults.LimbDepthDecayFactor, CoreDefaults.LimbDepthDecayFactorMin, CoreDefaults.LimbDepthDecayFactorMax, ref c),
                ConfigBounds.ClampPercent(TorsoPositiveBreathing, CoreDefaults.TorsoPositiveBreathing, ref c),
                ConfigBounds.ClampPercent(TorsoPositiveBlood, CoreDefaults.TorsoPositiveBlood, ref c),
                ConfigBounds.ClampPercent(TorsoPositiveMoving, CoreDefaults.TorsoPositiveMoving, ref c),
                ConfigBounds.ClampPercent(TorsoPositiveManipulation, CoreDefaults.TorsoPositiveManipulation, ref c),
                ConfigBounds.ClampPercent(ArmPositiveBreathing, CoreDefaults.ArmPositiveBreathing, ref c),
                ConfigBounds.ClampPercent(ArmPositiveBlood, CoreDefaults.ArmPositiveBlood, ref c),
                ConfigBounds.ClampPercent(ArmPositiveMoving, CoreDefaults.ArmPositiveMoving, ref c),
                ConfigBounds.ClampPercent(ArmPositiveManipulation, CoreDefaults.ArmPositiveManipulation, ref c),
                ConfigBounds.ClampPercent(LegPositiveBreathing, CoreDefaults.LegPositiveBreathing, ref c),
                ConfigBounds.ClampPercent(LegPositiveBlood, CoreDefaults.LegPositiveBlood, ref c),
                ConfigBounds.ClampPercent(LegPositiveMoving, CoreDefaults.LegPositiveMoving, ref c),
                ConfigBounds.ClampPercent(LegPositiveManipulation, CoreDefaults.LegPositiveManipulation, ref c),
                ConfigBounds.ClampPercent(TorsoDeficitBreathing, CoreDefaults.TorsoDeficitBreathing, ref c),
                ConfigBounds.ClampPercent(TorsoDeficitBlood, CoreDefaults.TorsoDeficitBlood, ref c),
                ConfigBounds.ClampPercent(TorsoDeficitMoving, CoreDefaults.TorsoDeficitMoving, ref c),
                ConfigBounds.ClampPercent(TorsoDeficitManipulation, CoreDefaults.TorsoDeficitManipulation, ref c),
                ConfigBounds.ClampPercent(ArmDeficitBreathing, CoreDefaults.ArmDeficitBreathing, ref c),
                ConfigBounds.ClampPercent(ArmDeficitBlood, CoreDefaults.ArmDeficitBlood, ref c),
                ConfigBounds.ClampPercent(ArmDeficitMoving, CoreDefaults.ArmDeficitMoving, ref c),
                ConfigBounds.ClampPercent(ArmDeficitManipulation, CoreDefaults.ArmDeficitManipulation, ref c),
                ConfigBounds.ClampPercent(LegDeficitBreathing, CoreDefaults.LegDeficitBreathing, ref c),
                ConfigBounds.ClampPercent(LegDeficitBlood, CoreDefaults.LegDeficitBlood, ref c),
                ConfigBounds.ClampPercent(LegDeficitMoving, CoreDefaults.LegDeficitMoving, ref c),
                ConfigBounds.ClampPercent(LegDeficitManipulation, CoreDefaults.LegDeficitManipulation, ref c),
                ConfigBounds.ClampPercent(BudgetCorePart, CoreDefaults.AnatomyWeightTorso, ref c),
                ConfigBounds.ClampPercent(BudgetManipulationPart, CoreDefaults.AnatomyWeightArm, ref c),
                ConfigBounds.ClampPercent(BudgetMovingPart, CoreDefaults.AnatomyWeightLeg, ref c));

            wasModified = c;
            return result;
        }

        #endregion
    }
}
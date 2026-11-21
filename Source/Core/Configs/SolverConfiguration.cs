namespace OverHaulers
{
    /// <summary>
    /// [CORE-CONF-01] IMMUTABLE VALUE-TYPE SOLVER CONFIGURATION
    /// Pure data transfer snapshot providing tuning parameters to Core mathematical solvers and topology compilers.
    /// Ordered strictly in accordance with [SEC-01] through [SEC-07] domain taxonomy.
    /// Resides under Source/Core/Configs/.
    /// </summary>
    public readonly struct SolverConfiguration
    {
        #region 1. INSTANCE FIELDS & IMMUTABLE BACKING STATE

        // [SEC-01] Global Modifiers & Scaling Intensity
        public readonly bool EnableProsthetics;
        public readonly bool EnableAthletics;
        public readonly bool EnablePartHealth;
        public readonly float ProstheticScaling;
        public readonly float AthleticScaling;
        public readonly float ConfigurablePercentileFloor;
        public readonly bool EnableHardCompatibilitySafetyFloor;

        // [SEC-02] Core Body Group Budgets & Redistribution
        public readonly float BudgetCorePart;
        public readonly float BudgetManipulationPart;
        public readonly float BudgetMovingPart;
        public readonly float MissingArmsTorsoShare;
        public readonly float MissingLegsTorsoShare;

        // [SEC-03] Positive Systemic Coupling Weights
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

        // [SEC-04] Negative Deficit Coupling Weights
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

        // [SEC-05] Torso Macro Topology
        public readonly float TorsoAxialBias;
        public readonly float TorsoHpSensitivity;

        // [SEC-06] Limb Branch Topology & Pair Scaling
        public readonly float LimbDepthDecayFactor;
        public readonly float LimbPairDecayFactor;

        // [SEC-07] Structural Scaling Constants
        public readonly float ProstheticImpactConstant;
        public readonly float AthleticImpactConstant;

        #endregion

        #region 2. LIFECYCLE: CONSTRUCTOR

        public SolverConfiguration(
            // [SEC-01] Global Modifiers & Scaling Intensity
            bool enableProsthetics,
            bool enableAthletics,
            bool enablePartHealth,
            float prostheticScaling,
            float athleticScaling,
            float configurablePercentileFloor,
            bool enableHardCompatibilitySafetyFloor,

            // [SEC-02] Core Body Group Budgets & Redistribution
            float budgetCore,
            float budgetManip,
            float budgetMove,
            float missingArmsTorsoShare,
            float missingLegsTorsoShare,

            // [SEC-03] Positive Systemic Coupling Weights
            float torsoPosBreathe, float torsoPosBlood, float torsoPosMove, float torsoPosManip,
            float armPosBreathe,   float armPosBlood,   float armPosMove,   float armPosManip,
            float legPosBreathe,   float legPosBlood,   float legPosMove,   float legPosManip,

            // [SEC-04] Negative Deficit Coupling Weights
            float torsoDefBreathe, float torsoDefBlood, float torsoDefMove, float torsoDefManip,
            float armDefBreathe,   float armDefBlood,   float armDefMove,   float armDefManip,
            float legDefBreathe,   float legDefBlood,   float legDefMove,   float legDefManip,

            // [SEC-05] Torso Macro Topology
            float torsoAxialBias,
            float torsoHpSensitivity,

            // [SEC-06] Limb Branch Topology & Pair Scaling
            float limbDepthDecayFactor,
            float limbPairDecayFactor,

            // [SEC-07] Structural Scaling Constants
            float prostheticImpactConstant,
            float athleticImpactConstant)
        {
            // [SEC-01]
            EnableProsthetics = enableProsthetics;
            EnableAthletics = enableAthletics;
            EnablePartHealth = enablePartHealth;
            ProstheticScaling = prostheticScaling;
            AthleticScaling = athleticScaling;
            ConfigurablePercentileFloor = configurablePercentileFloor;
            EnableHardCompatibilitySafetyFloor = enableHardCompatibilitySafetyFloor;

            // [SEC-02]
            BudgetCorePart = budgetCore;
            BudgetManipulationPart = budgetManip;
            BudgetMovingPart = budgetMove;
            MissingArmsTorsoShare = missingArmsTorsoShare;
            MissingLegsTorsoShare = missingLegsTorsoShare;

            // [SEC-03]
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

            // [SEC-04]
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

            // [SEC-05]
            TorsoAxialBias = torsoAxialBias;
            TorsoHpSensitivity = torsoHpSensitivity;

            // [SEC-06]
            LimbDepthDecayFactor = limbDepthDecayFactor;
            LimbPairDecayFactor = limbPairDecayFactor;

            // [SEC-07]
            ProstheticImpactConstant = prostheticImpactConstant;
            AthleticImpactConstant = athleticImpactConstant;
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
                // [SEC-01]
                EnableProsthetics,
                EnableAthletics,
                EnablePartHealth,
                ConfigBounds.ClampFloat(ProstheticScaling, CoreDefaults.ProstheticScaling, CoreDefaults.ProstheticScalingMin, CoreDefaults.ProstheticScalingMax, ref c),
                ConfigBounds.ClampFloat(AthleticScaling, CoreDefaults.AthleticScaling, CoreDefaults.AthleticScalingMin, CoreDefaults.AthleticScalingMax, ref c),
                ConfigBounds.ClampFloat(ConfigurablePercentileFloor, CoreDefaults.ConfigurablePercentileFloor, CoreDefaults.ConfigurablePercentileFloorMin, CoreDefaults.ConfigurablePercentileFloorMax, ref c),
                EnableHardCompatibilitySafetyFloor,

                // [SEC-02]
                ConfigBounds.ClampPercent(BudgetCorePart, CoreDefaults.AnatomyWeightTorso, ref c),
                ConfigBounds.ClampPercent(BudgetManipulationPart, CoreDefaults.AnatomyWeightArm, ref c),
                ConfigBounds.ClampPercent(BudgetMovingPart, CoreDefaults.AnatomyWeightLeg, ref c),
                ConfigBounds.ClampPercent(MissingArmsTorsoShare, CoreDefaults.MissingArmsTorsoShare, ref c),
                ConfigBounds.ClampPercent(MissingLegsTorsoShare, CoreDefaults.MissingLegsTorsoShare, ref c),

                // [SEC-03]
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

                // [SEC-04]
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

                // [SEC-05]
                ConfigBounds.ClampFloat(TorsoAxialBias, CoreDefaults.TorsoAxialBias, CoreDefaults.TorsoAxialBiasMin, CoreDefaults.TorsoAxialBiasMax, ref c),
                ConfigBounds.ClampFloat(TorsoHpSensitivity, CoreDefaults.TorsoHpSensitivity, CoreDefaults.TorsoHpSensitivityMin, CoreDefaults.TorsoHpSensitivityMax, ref c),

                // [SEC-06]
                ConfigBounds.ClampFloat(LimbDepthDecayFactor, CoreDefaults.LimbDepthDecayFactor, CoreDefaults.LimbDepthDecayFactorMin, CoreDefaults.LimbDepthDecayFactorMax, ref c),
                ConfigBounds.ClampFloat(LimbPairDecayFactor, CoreDefaults.LimbPairDecayFactor, CoreDefaults.LimbPairDecayFactorMin, CoreDefaults.LimbPairDecayFactorMax, ref c),

                // [SEC-07]
                ConfigBounds.ClampFloat(ProstheticImpactConstant, CoreDefaults.ProstheticImpactConstant, CoreDefaults.ProstheticImpactConstantMin, CoreDefaults.ProstheticImpactConstantMax, ref c),
                ConfigBounds.ClampFloat(AthleticImpactConstant, CoreDefaults.AthleticImpactConstant, CoreDefaults.AthleticImpactConstantMin, CoreDefaults.AthleticImpactConstantMax, ref c)
            );

            wasModified = c;
            return result;
        }

        #endregion
    }
}
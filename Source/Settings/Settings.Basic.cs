using Verse;

namespace OverHaulers
{
    public partial class Settings
    {
        #region 1. [SEC-01] GLOBAL MODIFIERS & SCALING FIELDS

        public bool enableProsthetics = CoreDefaults.EnableProsthetics;
        public bool enableAthletics = CoreDefaults.EnableAthletics;
        public bool enablePartHealth = CoreDefaults.EnablePartHealth;

        public float prostheticScaling = CoreDefaults.ProstheticScaling;
        public float athleticScaling = CoreDefaults.AthleticScaling;
        public float configurablePercentileFloor = CoreDefaults.ConfigurablePercentileFloor;

        public bool enableHardCompatibilitySafetyFloor = CoreDefaults.EnableHardCompatibilitySafetyFloor;

        #endregion

        #region 2. [SEC-02] CORE BODY GROUP BUDGET FIELDS

        public float anatomyWeightTorso = CoreDefaults.AnatomyWeightTorso;
        public float anatomyWeightArm = CoreDefaults.AnatomyWeightArm;
        public float anatomyWeightLeg = CoreDefaults.AnatomyWeightLeg;

        public float missingArmsTorsoShare = CoreDefaults.MissingArmsTorsoShare;
        public float missingLegsTorsoShare = CoreDefaults.MissingLegsTorsoShare;

        /// <summary>
        /// Retrieves the budget value for the specified body part type.
        /// </summary>
        /// <param name="type">The type of body part.</param>
        /// <returns>The budget value associated with the specified body part type.</returns>
        public float GetBudget(PartType type)
        {
            switch (type)
            {
                case PartType.CorePart: return anatomyWeightTorso;
                case PartType.ManipulationPart: return anatomyWeightArm;
                case PartType.MovingPart: return anatomyWeightLeg;
                default: return 0f;
            }
        }

        #endregion

        #region 3. [SEC-01..02] RESET

        /// <summary>
        /// Resets all global settings to their default values.
        /// </summary>
        public void ResetGlobals()
        {
            prostheticScaling = CoreDefaults.ProstheticScaling;
            athleticScaling = CoreDefaults.AthleticScaling;
            configurablePercentileFloor = CoreDefaults.ConfigurablePercentileFloor;
            enableHardCompatibilitySafetyFloor = CoreDefaults.EnableHardCompatibilitySafetyFloor;
            enableProsthetics = CoreDefaults.EnableProsthetics;
            enableAthletics = CoreDefaults.EnableAthletics;
            enablePartHealth = CoreDefaults.EnablePartHealth;
            NotifyValuesReset();
        }

        /// <summary>
        /// Resets all body part-related settings to their default values.
        /// </summary>
        public void ResetBodyParts()
        {
            anatomyWeightTorso = CoreDefaults.AnatomyWeightTorso;
            anatomyWeightArm = CoreDefaults.AnatomyWeightArm;
            anatomyWeightLeg = CoreDefaults.AnatomyWeightLeg;
            missingArmsTorsoShare = CoreDefaults.MissingArmsTorsoShare;
            missingLegsTorsoShare = CoreDefaults.MissingLegsTorsoShare;
            NotifyValuesReset();
        }

        #endregion

        #region 4. [SEC-01..02] SERIALIZATION & SANITISATION

        /// <summary>
        /// Exposes the basic settings for serialization using the Scribe system.
        /// </summary>
        private void ExposeBasic()
        {
            Scribe_Values.Look(ref enableProsthetics, "enableProsthetics", CoreDefaults.EnableProsthetics);
            Scribe_Values.Look(ref enableAthletics, "enableAthletics", CoreDefaults.EnableAthletics);
            Scribe_Values.Look(ref enablePartHealth, "enablePartHealth", CoreDefaults.EnablePartHealth);
            Scribe_Values.Look(ref prostheticScaling, "prostheticScaling", CoreDefaults.ProstheticScaling);
            Scribe_Values.Look(ref athleticScaling, "athleticScaling", CoreDefaults.AthleticScaling);
            Scribe_Values.Look(ref configurablePercentileFloor, "configurablePercentileFloor", CoreDefaults.ConfigurablePercentileFloor);
            Scribe_Values.Look(ref enableHardCompatibilitySafetyFloor, "enableHardCompatibilitySafetyFloor", CoreDefaults.EnableHardCompatibilitySafetyFloor);

            Scribe_Values.Look(ref anatomyWeightTorso, "anatomyWeightTorso", CoreDefaults.AnatomyWeightTorso);
            Scribe_Values.Look(ref anatomyWeightArm, "anatomyWeightArm", CoreDefaults.AnatomyWeightArm);
            Scribe_Values.Look(ref anatomyWeightLeg, "anatomyWeightLeg", CoreDefaults.AnatomyWeightLeg);

            Scribe_Values.Look(ref missingArmsTorsoShare, "missingArmsTorsoShare", CoreDefaults.MissingArmsTorsoShare);
            Scribe_Values.Look(ref missingLegsTorsoShare, "missingLegsTorsoShare", CoreDefaults.MissingLegsTorsoShare);
        }

        /// <summary>
        /// Sanitizes and applies the basic settings from the provided solver configuration. This includes scaling factors, body part weights, and missing part shares.
        /// </summary>
        /// <param name="solver">The solver configuration containing the basic settings to be sanitized and applied.</param>
        private void SanitizeBasic(in SolverConfiguration solver)
        {
            prostheticScaling = solver.ProstheticScaling;
            athleticScaling = solver.AthleticScaling;
            configurablePercentileFloor = solver.ConfigurablePercentileFloor;

            anatomyWeightTorso = solver.BudgetCorePart;
            anatomyWeightArm = solver.BudgetManipulationPart;
            anatomyWeightLeg = solver.BudgetMovingPart;
            NormalizeLoadedBodyWeights();

            missingArmsTorsoShare = solver.MissingArmsTorsoShare;
            missingLegsTorsoShare = solver.MissingLegsTorsoShare;
        }

        /// <summary>
        /// Normalizes the loaded body part weights to ensure they sum to 1. If the sum is too small or zero, default values are used.
        /// </summary>
        private void NormalizeLoadedBodyWeights()
        {
            float sum = anatomyWeightTorso + anatomyWeightArm + anatomyWeightLeg;
            if (sum <= 0.0001f)
            {
                anatomyWeightTorso = CoreDefaults.AnatomyWeightTorso;
                anatomyWeightArm = CoreDefaults.AnatomyWeightArm;
                anatomyWeightLeg = CoreDefaults.AnatomyWeightLeg;
                return;
            }

            if (System.Math.Abs(sum - 1.0f) > 0.0001f)
            {
                anatomyWeightTorso = (float)System.Math.Round(anatomyWeightTorso / sum, 2, System.MidpointRounding.ToEven);
                anatomyWeightArm = (float)System.Math.Round(anatomyWeightArm / sum, 2, System.MidpointRounding.ToEven);
                anatomyWeightLeg = (float)System.Math.Round(1.0f - anatomyWeightTorso - anatomyWeightArm, 2, System.MidpointRounding.ToEven);
                if (anatomyWeightLeg < 0f) anatomyWeightLeg = 0f;
            }
        }

        #endregion
    }
}

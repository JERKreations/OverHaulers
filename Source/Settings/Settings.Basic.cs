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
            SettingsViewUtilities.ClearInputBuffers();
        }

        /// <summary>
        /// Resets all body part-related settings to their default values.
        /// </summary>
        public void ResetBodyParts()
        {
            anatomyWeightTorso = CoreDefaults.AnatomyWeightTorso;
            anatomyWeightArm = CoreDefaults.AnatomyWeightArm;
            anatomyWeightLeg = CoreDefaults.AnatomyWeightLeg;
            SettingsViewUtilities.ClearInputBuffers();
        }

        #endregion

        #region 4. [SEC-01..02] SERIALIZATION & SANITISATION

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
        }

        private void SanitizeBasic()
        {
            prostheticScaling = Sane(prostheticScaling, CoreDefaults.ProstheticScaling, CoreDefaults.ProstheticScalingMin, CoreDefaults.ProstheticScalingMax);
            athleticScaling = Sane(athleticScaling, CoreDefaults.AthleticScaling, CoreDefaults.AthleticScalingMin, CoreDefaults.AthleticScalingMax);
            configurablePercentileFloor = Sane(configurablePercentileFloor, CoreDefaults.ConfigurablePercentileFloor, CoreDefaults.ConfigurablePercentileFloorMin, CoreDefaults.ConfigurablePercentileFloorMax);

                        anatomyWeightTorso = SanePercent(anatomyWeightTorso, CoreDefaults.AnatomyWeightTorso);
                        anatomyWeightArm = SanePercent(anatomyWeightArm, CoreDefaults.AnatomyWeightArm);
                        anatomyWeightLeg = SanePercent(anatomyWeightLeg, CoreDefaults.AnatomyWeightLeg);
                    }

        #endregion
    }
}

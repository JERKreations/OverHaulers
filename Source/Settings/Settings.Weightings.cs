using Verse;

namespace OverHaulers
{
    public partial class Settings
    {
        #region 1. [SEC-03] POSITIVE SYSTEMIC COUPLING FIELDS

        public const bool DefaultLinkSystemicWeights = true;

        public bool linkSystemicWeights = DefaultLinkSystemicWeights;

        public float torsoPositiveBreathing = CoreDefaults.TorsoPositiveBreathing;
        public float torsoPositiveBlood = CoreDefaults.TorsoPositiveBlood;
        public float torsoPositiveMoving = CoreDefaults.TorsoPositiveMoving;
        public float torsoPositiveManipulation = CoreDefaults.TorsoPositiveManipulation;

        public float armPositiveBreathing = CoreDefaults.ArmPositiveBreathing;
        public float armPositiveBlood = CoreDefaults.ArmPositiveBlood;
        public float armPositiveMoving = CoreDefaults.ArmPositiveMoving;
        public float armPositiveManipulation = CoreDefaults.ArmPositiveManipulation;

        public float legPositiveBreathing = CoreDefaults.LegPositiveBreathing;
        public float legPositiveBlood = CoreDefaults.LegPositiveBlood;
        public float legPositiveMoving = CoreDefaults.LegPositiveMoving;
        public float legPositiveManipulation = CoreDefaults.LegPositiveManipulation;

        #endregion

        #region 2. [SEC-04] NEGATIVE SYSTEMIC COUPLING FIELDS

        public float torsoDeficitBreathing = CoreDefaults.TorsoDeficitBreathing;
        public float torsoDeficitBlood = CoreDefaults.TorsoDeficitBlood;
        public float torsoDeficitMoving = CoreDefaults.TorsoDeficitMoving;
        public float torsoDeficitManipulation = CoreDefaults.TorsoDeficitManipulation;

        public float armDeficitBreathing = CoreDefaults.ArmDeficitBreathing;
        public float armDeficitBlood = CoreDefaults.ArmDeficitBlood;
        public float armDeficitMoving = CoreDefaults.ArmDeficitMoving;
        public float armDeficitManipulation = CoreDefaults.ArmDeficitManipulation;

        public float legDeficitBreathing = CoreDefaults.LegDeficitBreathing;
        public float legDeficitBlood = CoreDefaults.LegDeficitBlood;
        public float legDeficitMoving = CoreDefaults.LegDeficitMoving;
        public float legDeficitManipulation = CoreDefaults.LegDeficitManipulation;

        #endregion

        #region 3. [SEC-05] TORSO MACRO TOPOLOGY FIELDS

        public float torsoAxialBias = CoreDefaults.TorsoAxialBias;
        public float torsoHpSensitivity = CoreDefaults.TorsoHpSensitivity;

        #endregion

        #region 4. [SEC-06] LIMB DEPTH DECAY FIELDS

        public float limbDepthDecayFactor = CoreDefaults.LimbDepthDecayFactor;

        #endregion

        #region 5. [SEC-03..06] RESET

        /// <summary>
        /// Resets all positive torso-related settings to their default values.
        /// </summary>
        public void ResetTorsoPositives()
        {
            torsoPositiveBreathing = CoreDefaults.TorsoPositiveBreathing;
            torsoPositiveBlood = CoreDefaults.TorsoPositiveBlood;
            torsoPositiveMoving = CoreDefaults.TorsoPositiveMoving;
            torsoPositiveManipulation = CoreDefaults.TorsoPositiveManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        /// <summary>
        /// Resets all positive arm-related settings to their default values.
        /// </summary>
        public void ResetArmPositives()
        {
            armPositiveBreathing = CoreDefaults.ArmPositiveBreathing;
            armPositiveBlood = CoreDefaults.ArmPositiveBlood;
            armPositiveMoving = CoreDefaults.ArmPositiveMoving;
            armPositiveManipulation = CoreDefaults.ArmPositiveManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        /// <summary>
        /// Resets all positive leg-related settings to their default values.
        /// </summary>
        public void ResetLegPositives()
        {
            legPositiveBreathing = CoreDefaults.LegPositiveBreathing;
            legPositiveBlood = CoreDefaults.LegPositiveBlood;
            legPositiveMoving = CoreDefaults.LegPositiveMoving;
            legPositiveManipulation = CoreDefaults.LegPositiveManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        /// <summary>
        /// Resets all negative torso-related settings to their default values.
        /// </summary>
        public void ResetTorsoDeficits()
        {
            torsoDeficitBreathing = CoreDefaults.TorsoDeficitBreathing;
            torsoDeficitBlood = CoreDefaults.TorsoDeficitBlood;
            torsoDeficitMoving = CoreDefaults.TorsoDeficitMoving;
            torsoDeficitManipulation = CoreDefaults.TorsoDeficitManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        /// <summary>
        /// Resets all negative arm-related settings to their default values.
        /// </summary>
        public void ResetArmDeficits()
        {
            armDeficitBreathing = CoreDefaults.ArmDeficitBreathing;
            armDeficitBlood = CoreDefaults.ArmDeficitBlood;
            armDeficitMoving = CoreDefaults.ArmDeficitMoving;
            armDeficitManipulation = CoreDefaults.ArmDeficitManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        /// <summary>
        /// Resets all negative leg-related settings to their default values.
        /// </summary>
        public void ResetLegDeficits()
        {
            legDeficitBreathing = CoreDefaults.LegDeficitBreathing;
            legDeficitBlood = CoreDefaults.LegDeficitBlood;
            legDeficitMoving = CoreDefaults.LegDeficitMoving;
            legDeficitManipulation = CoreDefaults.LegDeficitManipulation;
            SettingsViewUtilities.ClearInputBuffers();
        }

        /// <summary>
        /// Resets all systemic torso-related settings to their default values.
        /// </summary>
        public void ResetTorsoSystemic()
        {
            ResetTorsoPositives();
            ResetTorsoDeficits();
        }

        /// <summary>
        /// Resets all systemic arm-related settings to their default values.
        /// </summary>
        public void ResetArmSystemic()
        {
            ResetArmPositives();
            ResetArmDeficits();
        }

        /// <summary>
        /// Resets all systemic leg-related settings to their default values.
        /// </summary>
        public void ResetLegSystemic()
        {
            ResetLegPositives();
            ResetLegDeficits();
        }

        /// <summary>
        /// Resets all systemic weightings to their default values.
        /// </summary>
        public void ResetSystemicWeightings()
        {
            linkSystemicWeights = DefaultLinkSystemicWeights;
            ResetTorsoSystemic();
            ResetArmSystemic();
            ResetLegSystemic();
        }

        /// <summary>
        /// Resets all torso part-related settings to their default values.
        /// </summary>
        public void ResetTorsoParts()
        {
            torsoAxialBias = CoreDefaults.TorsoAxialBias;
            torsoHpSensitivity = CoreDefaults.TorsoHpSensitivity;
            SettingsViewUtilities.ClearInputBuffers();
        }

        /// <summary>
        /// Resets all limb decay-related settings to their default values.
        /// </summary>
        public void ResetLimbDecay()
        {
            limbDepthDecayFactor = CoreDefaults.LimbDepthDecayFactor;
            SettingsViewUtilities.ClearInputBuffers();
        }

        #endregion

        #region 6. [SEC-03..06] SERIALIZATION & SANITISATION

        private void ExposeWeightings()
        {
            // [SEC-03] Master lock for symmetric/individual systemic editing
            Scribe_Values.Look(ref linkSystemicWeights, "linkSystemicWeights", DefaultLinkSystemicWeights);

            Scribe_Values.Look(ref torsoPositiveBreathing, "torsoPositiveBreathing", CoreDefaults.TorsoPositiveBreathing);
            Scribe_Values.Look(ref torsoPositiveBlood, "torsoPositiveBlood", CoreDefaults.TorsoPositiveBlood);
            Scribe_Values.Look(ref torsoPositiveMoving, "torsoPositiveMoving", CoreDefaults.TorsoPositiveMoving);
            Scribe_Values.Look(ref torsoPositiveManipulation, "torsoPositiveManipulation", CoreDefaults.TorsoPositiveManipulation);
            Scribe_Values.Look(ref armPositiveBreathing, "armPositiveBreathing", CoreDefaults.ArmPositiveBreathing);
            Scribe_Values.Look(ref armPositiveBlood, "armPositiveBlood", CoreDefaults.ArmPositiveBlood);
            Scribe_Values.Look(ref armPositiveMoving, "armPositiveMoving", CoreDefaults.ArmPositiveMoving);
            Scribe_Values.Look(ref armPositiveManipulation, "armPositiveManipulation", CoreDefaults.ArmPositiveManipulation);
            Scribe_Values.Look(ref legPositiveBreathing, "legPositiveBreathing", CoreDefaults.LegPositiveBreathing);
            Scribe_Values.Look(ref legPositiveBlood, "legPositiveBlood", CoreDefaults.LegPositiveBlood);
            Scribe_Values.Look(ref legPositiveMoving, "legPositiveMoving", CoreDefaults.LegPositiveMoving);
            Scribe_Values.Look(ref legPositiveManipulation, "legPositiveManipulation", CoreDefaults.LegPositiveManipulation);

            Scribe_Values.Look(ref torsoDeficitBreathing, "torsoDeficitBreathing", CoreDefaults.TorsoDeficitBreathing);
            Scribe_Values.Look(ref torsoDeficitBlood, "torsoDeficitBlood", CoreDefaults.TorsoDeficitBlood);
            Scribe_Values.Look(ref torsoDeficitMoving, "torsoDeficitMoving", CoreDefaults.TorsoDeficitMoving);
            Scribe_Values.Look(ref torsoDeficitManipulation, "torsoDeficitManipulation", CoreDefaults.TorsoDeficitManipulation);
            Scribe_Values.Look(ref armDeficitBreathing, "armDeficitBreathing", CoreDefaults.ArmDeficitBreathing);
            Scribe_Values.Look(ref armDeficitBlood, "armDeficitBlood", CoreDefaults.ArmDeficitBlood);
            Scribe_Values.Look(ref armDeficitMoving, "armDeficitMoving", CoreDefaults.ArmDeficitMoving);
            Scribe_Values.Look(ref armDeficitManipulation, "armDeficitManipulation", CoreDefaults.ArmDeficitManipulation);
            Scribe_Values.Look(ref legDeficitBreathing, "legDeficitBreathing", CoreDefaults.LegDeficitBreathing);
            Scribe_Values.Look(ref legDeficitBlood, "legDeficitBlood", CoreDefaults.LegDeficitBlood);
            Scribe_Values.Look(ref legDeficitMoving, "legDeficitMoving", CoreDefaults.LegDeficitMoving);
            Scribe_Values.Look(ref legDeficitManipulation, "legDeficitManipulation", CoreDefaults.LegDeficitManipulation);

            Scribe_Values.Look(ref torsoAxialBias, "torsoAxialBias", CoreDefaults.TorsoAxialBias);
            Scribe_Values.Look(ref torsoHpSensitivity, "torsoHpSensitivity", CoreDefaults.TorsoHpSensitivity);
            Scribe_Values.Look(ref limbDepthDecayFactor, "limbDepthDecayFactor", CoreDefaults.LimbDepthDecayFactor);
        }

        private void SanitizeWeightings()
        {
            torsoPositiveBreathing = SanePercent(torsoPositiveBreathing, CoreDefaults.TorsoPositiveBreathing);
            torsoPositiveBlood = SanePercent(torsoPositiveBlood, CoreDefaults.TorsoPositiveBlood);
            torsoPositiveMoving = SanePercent(torsoPositiveMoving, CoreDefaults.TorsoPositiveMoving);
            torsoPositiveManipulation = SanePercent(torsoPositiveManipulation, CoreDefaults.TorsoPositiveManipulation);
            armPositiveBreathing = SanePercent(armPositiveBreathing, CoreDefaults.ArmPositiveBreathing);
            armPositiveBlood = SanePercent(armPositiveBlood, CoreDefaults.ArmPositiveBlood);
            armPositiveMoving = SanePercent(armPositiveMoving, CoreDefaults.ArmPositiveMoving);
            armPositiveManipulation = SanePercent(armPositiveManipulation, CoreDefaults.ArmPositiveManipulation);
            legPositiveBreathing = SanePercent(legPositiveBreathing, CoreDefaults.LegPositiveBreathing);
            legPositiveBlood = SanePercent(legPositiveBlood, CoreDefaults.LegPositiveBlood);
            legPositiveMoving = SanePercent(legPositiveMoving, CoreDefaults.LegPositiveMoving);
            legPositiveManipulation = SanePercent(legPositiveManipulation, CoreDefaults.LegPositiveManipulation);

            torsoDeficitBreathing = SanePercent(torsoDeficitBreathing, CoreDefaults.TorsoDeficitBreathing);
            torsoDeficitBlood = SanePercent(torsoDeficitBlood, CoreDefaults.TorsoDeficitBlood);
            torsoDeficitMoving = SanePercent(torsoDeficitMoving, CoreDefaults.TorsoDeficitMoving);
            torsoDeficitManipulation = SanePercent(torsoDeficitManipulation, CoreDefaults.TorsoDeficitManipulation);
            armDeficitBreathing = SanePercent(armDeficitBreathing, CoreDefaults.ArmDeficitBreathing);
            armDeficitBlood = SanePercent(armDeficitBlood, CoreDefaults.ArmDeficitBlood);
            armDeficitMoving = SanePercent(armDeficitMoving, CoreDefaults.ArmDeficitMoving);
            armDeficitManipulation = SanePercent(armDeficitManipulation, CoreDefaults.ArmDeficitManipulation);
            legDeficitBreathing = SanePercent(legDeficitBreathing, CoreDefaults.LegDeficitBreathing);
            legDeficitBlood = SanePercent(legDeficitBlood, CoreDefaults.LegDeficitBlood);
            legDeficitMoving = SanePercent(legDeficitMoving, CoreDefaults.LegDeficitMoving);
            legDeficitManipulation = SanePercent(legDeficitManipulation, CoreDefaults.LegDeficitManipulation);

            torsoAxialBias = Sane(torsoAxialBias, CoreDefaults.TorsoAxialBias, CoreDefaults.TorsoAxialBiasMin, CoreDefaults.TorsoAxialBiasMax);
            torsoHpSensitivity = Sane(torsoHpSensitivity, CoreDefaults.TorsoHpSensitivity, CoreDefaults.TorsoHpSensitivityMin, CoreDefaults.TorsoHpSensitivityMax);
            limbDepthDecayFactor = Sane(limbDepthDecayFactor, CoreDefaults.LimbDepthDecayFactor, CoreDefaults.LimbDepthDecayFactorMin, CoreDefaults.LimbDepthDecayFactorMax);
        }

        #endregion
    }
}

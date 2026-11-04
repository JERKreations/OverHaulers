using Verse;

namespace OverHaulers
{
    public partial class Settings
    {
        #region 1. [SEC-07] STRUCTURAL SCALING CONSTANT FIELDS

        public float prostheticImpactConstant = CoreDefaults.ProstheticImpactConstant;
        public float athleticImpactConstant = CoreDefaults.AthleticImpactConstant;

        #endregion

        #region 2. [SEC-07] RESET

        /// <summary>
        /// Resets all scaling constant-related settings to their default values.
        /// </summary>
        public void ResetScalingConstants()
        {
            prostheticImpactConstant = CoreDefaults.ProstheticImpactConstant;
            athleticImpactConstant = CoreDefaults.AthleticImpactConstant;
            NotifyValuesReset();
        }

        #endregion

        #region 3. [SEC-07] SERIALIZATION & SANITISATION

        private void ExposeScaling()
        {
            Scribe_Values.Look(ref prostheticImpactConstant, "prostheticImpactConstant", CoreDefaults.ProstheticImpactConstant);
            Scribe_Values.Look(ref athleticImpactConstant, "athleticImpactConstant", CoreDefaults.AthleticImpactConstant);
        }

        private void SanitizeScaling(in SolverConfiguration solver)
        {
            prostheticImpactConstant = solver.ProstheticImpactConstant;
            athleticImpactConstant = solver.AthleticImpactConstant;
        }

        #endregion
    }
}

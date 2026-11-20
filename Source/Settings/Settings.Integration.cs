using System.Collections.Generic;
using Verse;

namespace OverHaulers
{
    public partial class Settings
    {
        #region 1. [SEC-08] COMPATIBILITY & STAT ADOPTION FIELDS

        /// <summary>Selected presentation key in the UI.</summary>
        public string selectedPresentationKey = IntegrationPipeline.PresentationKeyAuto;

        /// <summary>When true, overrides automatic biological factor selection for the active CIL-adopted stat.</summary>
        public bool useCustomCilNeutralization = false;

        /// <summary>Explicit capacity factor defNames to neutralize when <see cref="useCustomCilNeutralization"/> is true.</summary>
        public List<string> cilNeutralizedFactors = new List<string>();

        #endregion

        #region 2. [SEC-08] RESET

        /// <summary>Clears custom per-factor neutralization overrides for CIL-discovered stats.</summary>
        public void ResetCustomCilNeutralization()
        {
            useCustomCilNeutralization = false;
            if (cilNeutralizedFactors == null) cilNeutralizedFactors = new List<string>();
            else cilNeutralizedFactors.Clear();
        }

        /// <summary>Resets compatibility-related settings to their default values.</summary>
        public void ResetCompatibility()
        {
            selectedPresentationKey = IntegrationPipeline.PresentationKeyAuto;
            ResetCustomCilNeutralization();
        }

        #endregion

        #region 3. [SEC-08] SERIALIZATION & SANITISATION

        /// <summary>Exposes the integration settings for serialization.</summary>
        private void ExposeIntegration()
        {
            Scribe_Values.Look(ref selectedPresentationKey, "selectedPresentationKey", IntegrationPipeline.PresentationKeyAuto);
            Scribe_Values.Look(ref useCustomCilNeutralization, "useCustomCilNeutralization", false);
            Scribe_Collections.Look(ref cilNeutralizedFactors, "cilNeutralizedFactors", LookMode.Value);
            if (cilNeutralizedFactors == null) cilNeutralizedFactors = new List<string>();
        }

        /// <summary>Sanitizes the integration settings to ensure they are within valid bounds and defaults.</summary>
        private void SanitizeIntegration()
        {
            if (string.IsNullOrEmpty(selectedPresentationKey)) selectedPresentationKey = IntegrationPipeline.PresentationKeyAuto;

            if (cilNeutralizedFactors == null)
            {
                cilNeutralizedFactors = new List<string>();
            }
            else
            {
                cilNeutralizedFactors.RemoveAll(string.IsNullOrEmpty);
            }

            if (!selectedPresentationKey.StartsWith(IntegrationPipeline.PresentationKeyCilPrefix))
            {
                ResetCustomCilNeutralization();
            }
        }

        #endregion
    }
}
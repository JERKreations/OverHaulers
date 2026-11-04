using Verse;

namespace OverHaulers
{
    public partial class Settings
    {
        #region 1. [SEC-08] COMPATIBILITY & STAT ADOPTION FIELDS

        /// <summary>Selected presentation key in the UI.</summary>
        public string selectedPresentationKey = IntegrationPipeline.PresentationKeyAuto;

        #endregion

        #region 2. [SEC-08] RESET

        /// <summary>Resets compatibility-related settings to their default values.</summary>
        public void ResetCompatibility()
        {
            selectedPresentationKey = IntegrationPipeline.PresentationKeyAuto;
            SettingsViewUtilities.ClearInputBuffers();
        }

        #endregion

        #region 3. [SEC-08] SERIALIZATION & SANITISATION

        private void ExposeIntegration()
        {
            Scribe_Values.Look(ref selectedPresentationKey, "selectedPresentationKey", IntegrationPipeline.PresentationKeyAuto);

            // Backward-compatibility migration: check for legacy 'selectedDriverKey'
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                string legacyKey = null;
                Scribe_Values.Look(ref legacyKey, "selectedDriverKey");
                if (!string.IsNullOrEmpty(legacyKey) && selectedPresentationKey == IntegrationPipeline.PresentationKeyAuto)
                {
                    selectedPresentationKey = (legacyKey == "STANDALONE") ? IntegrationPipeline.PresentationKeyNative : legacyKey;
                }
            }
        }

        private void SanitizeIntegration()
        {
            if (string.IsNullOrEmpty(selectedPresentationKey)) selectedPresentationKey = IntegrationPipeline.PresentationKeyAuto;
        }

        #endregion
    }
}

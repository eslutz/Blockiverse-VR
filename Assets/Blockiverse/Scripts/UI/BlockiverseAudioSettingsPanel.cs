using Blockiverse.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // Audio/feedback settings screen: volume sliders and feedback toggles bound directly to
    // BlockiverseFeedbackSettings (settings persist via BlockiverseSettingsPersistence's
    // app-pause snapshot). The Close button is a BlockiverseActionMenu wired by the bootstrapper.
    [DisallowMultipleComponent]
    public sealed class BlockiverseAudioSettingsPanel : MonoBehaviour
    {
        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;
        [SerializeField] Slider masterVolumeSlider;
        [SerializeField] Slider effectsVolumeSlider;
        [SerializeField] Slider uiVolumeSlider;
        [SerializeField] Slider weatherVolumeSlider;
        [SerializeField] Slider musicVolumeSlider;
        [SerializeField] Slider hapticIntensitySlider;
        [SerializeField] Toggle muteAllToggle;
        [SerializeField] Toggle hapticsToggle;
        [SerializeField] Toggle reducedFlashToggle;
        [SerializeField] Toggle reducedParticlesToggle;

        bool wired;

        public void Configure(
            BlockiverseFeedbackSettings settings,
            Slider masterVolume,
            Slider effectsVolume,
            Slider uiVolume,
            Slider weatherVolume,
            Slider musicVolume,
            Slider hapticIntensity,
            Toggle muteAll,
            Toggle haptics,
            Toggle reducedFlash,
            Toggle reducedParticles)
        {
            Unwire();
            feedbackSettings = settings;
            masterVolumeSlider = masterVolume;
            effectsVolumeSlider = effectsVolume;
            uiVolumeSlider = uiVolume;
            weatherVolumeSlider = weatherVolume;
            musicVolumeSlider = musicVolume;
            hapticIntensitySlider = hapticIntensity;
            muteAllToggle = muteAll;
            hapticsToggle = haptics;
            reducedFlashToggle = reducedFlash;
            reducedParticlesToggle = reducedParticles;
            Wire();
        }

        void OnEnable()
        {
            ResolveReferences();
            Wire();
            RefreshControlsFromSettings();
        }

        void OnDisable()
        {
            Unwire();
        }

        void ResolveReferences()
        {
            if (feedbackSettings == null)
                feedbackSettings = FindFirstObjectByType<BlockiverseFeedbackSettings>(FindObjectsInactive.Include);
        }

        // Pushes the live setting values into the controls (without re-firing the listeners).
        public void RefreshControlsFromSettings()
        {
            if (feedbackSettings == null)
                return;

            masterVolumeSlider?.SetValueWithoutNotify(feedbackSettings.MasterVolume);
            effectsVolumeSlider?.SetValueWithoutNotify(feedbackSettings.EffectsVolume);
            uiVolumeSlider?.SetValueWithoutNotify(feedbackSettings.UiVolume);
            weatherVolumeSlider?.SetValueWithoutNotify(feedbackSettings.WeatherVolume);
            musicVolumeSlider?.SetValueWithoutNotify(feedbackSettings.MusicVolume);
            hapticIntensitySlider?.SetValueWithoutNotify(feedbackSettings.HapticIntensity);
            muteAllToggle?.SetIsOnWithoutNotify(feedbackSettings.MuteAll);
            hapticsToggle?.SetIsOnWithoutNotify(feedbackSettings.HapticsEnabled);
            reducedFlashToggle?.SetIsOnWithoutNotify(feedbackSettings.ReducedFlash);
            reducedParticlesToggle?.SetIsOnWithoutNotify(feedbackSettings.ReducedParticles);
        }

        void Wire()
        {
            if (wired)
                return;

            masterVolumeSlider?.onValueChanged.AddListener(OnMasterVolumeChanged);
            effectsVolumeSlider?.onValueChanged.AddListener(OnEffectsVolumeChanged);
            uiVolumeSlider?.onValueChanged.AddListener(OnUiVolumeChanged);
            weatherVolumeSlider?.onValueChanged.AddListener(OnWeatherVolumeChanged);
            musicVolumeSlider?.onValueChanged.AddListener(OnMusicVolumeChanged);
            hapticIntensitySlider?.onValueChanged.AddListener(OnHapticIntensityChanged);
            muteAllToggle?.onValueChanged.AddListener(OnMuteAllChanged);
            hapticsToggle?.onValueChanged.AddListener(OnHapticsChanged);
            reducedFlashToggle?.onValueChanged.AddListener(OnReducedFlashChanged);
            reducedParticlesToggle?.onValueChanged.AddListener(OnReducedParticlesChanged);
            wired = true;
        }

        void Unwire()
        {
            if (!wired)
                return;

            masterVolumeSlider?.onValueChanged.RemoveListener(OnMasterVolumeChanged);
            effectsVolumeSlider?.onValueChanged.RemoveListener(OnEffectsVolumeChanged);
            uiVolumeSlider?.onValueChanged.RemoveListener(OnUiVolumeChanged);
            weatherVolumeSlider?.onValueChanged.RemoveListener(OnWeatherVolumeChanged);
            musicVolumeSlider?.onValueChanged.RemoveListener(OnMusicVolumeChanged);
            hapticIntensitySlider?.onValueChanged.RemoveListener(OnHapticIntensityChanged);
            muteAllToggle?.onValueChanged.RemoveListener(OnMuteAllChanged);
            hapticsToggle?.onValueChanged.RemoveListener(OnHapticsChanged);
            reducedFlashToggle?.onValueChanged.RemoveListener(OnReducedFlashChanged);
            reducedParticlesToggle?.onValueChanged.RemoveListener(OnReducedParticlesChanged);
            wired = false;
        }

        void OnMasterVolumeChanged(float value) { if (feedbackSettings != null) feedbackSettings.MasterVolume = value; }
        void OnEffectsVolumeChanged(float value) { if (feedbackSettings != null) feedbackSettings.EffectsVolume = value; }
        void OnUiVolumeChanged(float value) { if (feedbackSettings != null) feedbackSettings.UiVolume = value; }
        void OnWeatherVolumeChanged(float value) { if (feedbackSettings != null) feedbackSettings.WeatherVolume = value; }
        void OnMusicVolumeChanged(float value) { if (feedbackSettings != null) feedbackSettings.MusicVolume = value; }
        void OnHapticIntensityChanged(float value) { if (feedbackSettings != null) feedbackSettings.HapticIntensity = value; }
        void OnMuteAllChanged(bool value) { if (feedbackSettings != null) feedbackSettings.MuteAll = value; }
        void OnHapticsChanged(bool value) { if (feedbackSettings != null) feedbackSettings.HapticsEnabled = value; }
        void OnReducedFlashChanged(bool value) { if (feedbackSettings != null) feedbackSettings.ReducedFlash = value; }
        void OnReducedParticlesChanged(bool value) { if (feedbackSettings != null) feedbackSettings.ReducedParticles = value; }
    }
}

using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of BlockiverseAudioSettingsPanel: sliders and toggles bind directly to
    // BlockiverseFeedbackSettings properties with no local state (persistence rides
    // BlockiverseSettingsPersistence's app-pause snapshot), values are pushed down with
    // SetValueWithoutNotify so a refresh never echoes through the change handlers, and the
    // panel itself plays no feedback cues — show/hide cues come from the host. The Close row
    // replaces the uGUI panel's separate action-menu close button and routes the same
    // canonical action id.
    [UiToolkitScreen(
        MenuActions.AudioSettingsScreen,
        "Assets/Blockiverse/UI/Documents/AudioSettingsScreen.uxml",
        800, 1428, UiToolkitPlacementProfile.Menu)]
    public sealed class AudioSettingsScreenController : UiToolkitScreenController
    {
        // Requested table entries (not yet in "UI"); UiText.Get falls back to the key string
        // until they land, so the rows are labelled either way.
        const string MuteAllLabelKey = "ui.generated.audio_feedback.mute_all";
        const string HapticsLabelKey = "ui.generated.audio_feedback.haptics";
        const string ReducedFlashLabelKey = "ui.generated.audio_feedback.reduced_flash";
        const string ReducedParticlesLabelKey = "ui.generated.audio_feedback.reduced_particles";
        const string ClassicBlockSoundsLabelKey = "ui.generated.audio_feedback.classic_block_sounds";

        Slider masterVolumeSlider;
        Slider effectsVolumeSlider;
        Slider uiVolumeSlider;
        Slider weatherVolumeSlider;
        Slider musicVolumeSlider;
        Slider hapticStrengthSlider;
        Toggle muteAllToggle;
        Toggle hapticsToggle;
        Toggle reducedFlashToggle;
        Toggle reducedParticlesToggle;
        Toggle classicBlockSoundsToggle;
        Button closeButton;

        BlockiverseFeedbackSettings feedbackSettings;

        public override string ScreenId => MenuActions.AudioSettingsScreen;

        // Mirrors the uGUI panel's late-configuration seam: the settings component may not
        // exist when this screen is built, so a null must never overwrite a good reference.
        public void ConfigureFeedbackSettings(BlockiverseFeedbackSettings settings)
        {
            if (settings == null)
                return;

            feedbackSettings = settings;

            if (IsBound)
                RefreshControlsFromSettings();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            masterVolumeSlider = Require<Slider>(root, "bv-audio-master-volume", ref allFound);
            effectsVolumeSlider = Require<Slider>(root, "bv-audio-effects-volume", ref allFound);
            uiVolumeSlider = Require<Slider>(root, "bv-audio-ui-volume", ref allFound);
            weatherVolumeSlider = Require<Slider>(root, "bv-audio-weather-volume", ref allFound);
            musicVolumeSlider = Require<Slider>(root, "bv-audio-music-volume", ref allFound);
            hapticStrengthSlider = Require<Slider>(root, "bv-audio-haptic-strength", ref allFound);
            muteAllToggle = Require<Toggle>(root, "bv-audio-mute-all", ref allFound);
            hapticsToggle = Require<Toggle>(root, "bv-audio-haptics", ref allFound);
            reducedFlashToggle = Require<Toggle>(root, "bv-audio-reduced-flash", ref allFound);
            reducedParticlesToggle = Require<Toggle>(root, "bv-audio-reduced-particles", ref allFound);
            classicBlockSoundsToggle = Require<Toggle>(root, "bv-audio-classic-block-sounds", ref allFound);
            closeButton = Require<Button>(root, "bv-audio-close", ref allFound);

            ApplyToggleLabels();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            if (masterVolumeSlider != null)
                masterVolumeSlider.RegisterValueChangedCallback(OnMasterVolumeChanged);

            if (effectsVolumeSlider != null)
                effectsVolumeSlider.RegisterValueChangedCallback(OnEffectsVolumeChanged);

            if (uiVolumeSlider != null)
                uiVolumeSlider.RegisterValueChangedCallback(OnUiVolumeChanged);

            if (weatherVolumeSlider != null)
                weatherVolumeSlider.RegisterValueChangedCallback(OnWeatherVolumeChanged);

            if (musicVolumeSlider != null)
                musicVolumeSlider.RegisterValueChangedCallback(OnMusicVolumeChanged);

            if (hapticStrengthSlider != null)
                hapticStrengthSlider.RegisterValueChangedCallback(OnHapticIntensityChanged);

            if (muteAllToggle != null)
                muteAllToggle.RegisterValueChangedCallback(OnMuteAllChanged);

            if (hapticsToggle != null)
                hapticsToggle.RegisterValueChangedCallback(OnHapticsChanged);

            if (reducedFlashToggle != null)
                reducedFlashToggle.RegisterValueChangedCallback(OnReducedFlashChanged);

            if (reducedParticlesToggle != null)
                reducedParticlesToggle.RegisterValueChangedCallback(OnReducedParticlesChanged);

            if (classicBlockSoundsToggle != null)
                classicBlockSoundsToggle.RegisterValueChangedCallback(OnClassicBlockSoundsChanged);

            if (closeButton != null)
                closeButton.clicked += OnCloseClicked;

            // Toggle labels are cached dynamic text (UiText), so they must re-resolve on
            // locale change; the statically bound labels update natively.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (masterVolumeSlider != null)
                masterVolumeSlider.UnregisterValueChangedCallback(OnMasterVolumeChanged);

            if (effectsVolumeSlider != null)
                effectsVolumeSlider.UnregisterValueChangedCallback(OnEffectsVolumeChanged);

            if (uiVolumeSlider != null)
                uiVolumeSlider.UnregisterValueChangedCallback(OnUiVolumeChanged);

            if (weatherVolumeSlider != null)
                weatherVolumeSlider.UnregisterValueChangedCallback(OnWeatherVolumeChanged);

            if (musicVolumeSlider != null)
                musicVolumeSlider.UnregisterValueChangedCallback(OnMusicVolumeChanged);

            if (hapticStrengthSlider != null)
                hapticStrengthSlider.UnregisterValueChangedCallback(OnHapticIntensityChanged);

            if (muteAllToggle != null)
                muteAllToggle.UnregisterValueChangedCallback(OnMuteAllChanged);

            if (hapticsToggle != null)
                hapticsToggle.UnregisterValueChangedCallback(OnHapticsChanged);

            if (reducedFlashToggle != null)
                reducedFlashToggle.UnregisterValueChangedCallback(OnReducedFlashChanged);

            if (reducedParticlesToggle != null)
                reducedParticlesToggle.UnregisterValueChangedCallback(OnReducedParticlesChanged);

            if (classicBlockSoundsToggle != null)
                classicBlockSoundsToggle.UnregisterValueChangedCallback(OnClassicBlockSoundsChanged);

            if (closeButton != null)
                closeButton.clicked -= OnCloseClicked;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            masterVolumeSlider = null;
            effectsVolumeSlider = null;
            uiVolumeSlider = null;
            weatherVolumeSlider = null;
            musicVolumeSlider = null;
            hapticStrengthSlider = null;
            muteAllToggle = null;
            hapticsToggle = null;
            reducedFlashToggle = null;
            reducedParticlesToggle = null;
            classicBlockSoundsToggle = null;
            closeButton = null;
        }

        // Mirrors the uGUI panel's OnEnable resolve-and-refresh, keyed on routed visibility:
        // the settings reference arrives late (the rig is built after the panels), so it is
        // resolved lazily rather than required at attach time.
        protected override void OnShown()
        {
            ResolveFeedbackSettings();
            RefreshControlsFromSettings();
        }

        // Pushes the live setting values into the controls without re-firing the change
        // handlers — an echo would rewrite settings mid-refresh.
        public void RefreshControlsFromSettings()
        {
            if (feedbackSettings == null)
                return;

            if (masterVolumeSlider != null)
                masterVolumeSlider.SetValueWithoutNotify(feedbackSettings.MasterVolume);

            if (effectsVolumeSlider != null)
                effectsVolumeSlider.SetValueWithoutNotify(feedbackSettings.EffectsVolume);

            if (uiVolumeSlider != null)
                uiVolumeSlider.SetValueWithoutNotify(feedbackSettings.UiVolume);

            if (weatherVolumeSlider != null)
                weatherVolumeSlider.SetValueWithoutNotify(feedbackSettings.WeatherVolume);

            if (musicVolumeSlider != null)
                musicVolumeSlider.SetValueWithoutNotify(feedbackSettings.MusicVolume);

            if (hapticStrengthSlider != null)
                hapticStrengthSlider.SetValueWithoutNotify(feedbackSettings.HapticIntensity);

            if (muteAllToggle != null)
                muteAllToggle.SetValueWithoutNotify(feedbackSettings.MuteAll);

            if (hapticsToggle != null)
                hapticsToggle.SetValueWithoutNotify(feedbackSettings.HapticsEnabled);

            if (reducedFlashToggle != null)
                reducedFlashToggle.SetValueWithoutNotify(feedbackSettings.ReducedFlash);

            if (reducedParticlesToggle != null)
                reducedParticlesToggle.SetValueWithoutNotify(feedbackSettings.ReducedParticles);

            if (classicBlockSoundsToggle != null)
                classicBlockSoundsToggle.SetValueWithoutNotify(feedbackSettings.ClassicBlockSoundsEnabled);
        }

        // Public handler seams. The ChangeEvent callbacks delegate here; EditMode tests call
        // them directly because a VisualElement tree with no panel never dispatches
        // ChangeEvents (BaseField only sends them while attached to a panel).
        public void ApplyMasterVolume(float value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.MasterVolume = value;
        }

        public void ApplyEffectsVolume(float value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.EffectsVolume = value;
        }

        public void ApplyUiVolume(float value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.UiVolume = value;
        }

        public void ApplyWeatherVolume(float value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.WeatherVolume = value;
        }

        public void ApplyMusicVolume(float value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.MusicVolume = value;
        }

        public void ApplyHapticIntensity(float value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.HapticIntensity = value;
        }

        public void ApplyMuteAll(bool value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.MuteAll = value;
        }

        public void ApplyHapticsEnabled(bool value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.HapticsEnabled = value;
        }

        public void ApplyReducedFlash(bool value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.ReducedFlash = value;
        }

        public void ApplyReducedParticles(bool value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.ReducedParticles = value;
        }

        public void ApplyClassicBlockSounds(bool value)
        {
            BlockiverseFeedbackSettings settings = ResolveFeedbackSettings();
            if (settings != null)
                settings.ClassicBlockSoundsEnabled = value;
        }

        // The uGUI close flow ends in the same action id (settings persistence rides the
        // app-pause snapshot, so closing only routes).
        public void RequestClose() => DispatchAction(MenuActions.AudioSettingsClose);

        BlockiverseFeedbackSettings ResolveFeedbackSettings()
        {
            if (feedbackSettings == null)
                feedbackSettings = BlockiverseSceneLookup.Find<BlockiverseFeedbackSettings>(FindObjectsInactive.Include);

            return feedbackSettings;
        }

        void ApplyToggleLabels()
        {
            if (muteAllToggle != null)
                muteAllToggle.label = UiText.Get(MuteAllLabelKey);

            if (hapticsToggle != null)
                hapticsToggle.label = UiText.Get(HapticsLabelKey);

            if (reducedFlashToggle != null)
                reducedFlashToggle.label = UiText.Get(ReducedFlashLabelKey);

            if (reducedParticlesToggle != null)
                reducedParticlesToggle.label = UiText.Get(ReducedParticlesLabelKey);

            if (classicBlockSoundsToggle != null)
                classicBlockSoundsToggle.label = UiText.Get(ClassicBlockSoundsLabelKey);
        }

        void OnSelectedLocaleChanged(Locale locale) => ApplyToggleLabels();

        void OnMasterVolumeChanged(ChangeEvent<float> evt) => ApplyMasterVolume(evt.newValue);

        void OnEffectsVolumeChanged(ChangeEvent<float> evt) => ApplyEffectsVolume(evt.newValue);

        void OnUiVolumeChanged(ChangeEvent<float> evt) => ApplyUiVolume(evt.newValue);

        void OnWeatherVolumeChanged(ChangeEvent<float> evt) => ApplyWeatherVolume(evt.newValue);

        void OnMusicVolumeChanged(ChangeEvent<float> evt) => ApplyMusicVolume(evt.newValue);

        void OnHapticIntensityChanged(ChangeEvent<float> evt) => ApplyHapticIntensity(evt.newValue);

        void OnMuteAllChanged(ChangeEvent<bool> evt) => ApplyMuteAll(evt.newValue);

        void OnHapticsChanged(ChangeEvent<bool> evt) => ApplyHapticsEnabled(evt.newValue);

        void OnReducedFlashChanged(ChangeEvent<bool> evt) => ApplyReducedFlash(evt.newValue);

        void OnReducedParticlesChanged(ChangeEvent<bool> evt) => ApplyReducedParticles(evt.newValue);

        void OnClassicBlockSoundsChanged(ChangeEvent<bool> evt) => ApplyClassicBlockSounds(evt.newValue);

        void OnCloseClicked() => RequestClose();
    }
}

using Blockiverse.Gameplay;
using Blockiverse.VR;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class BlockiverseComfortMenu : MonoBehaviour
    {
        [SerializeField] Canvas canvas;
        // Legacy: kept for compatibility; locomotionMode toggle replaces this.
        [SerializeField] Toggle teleportToggle;
        [SerializeField] Toggle glideToggle;
        [SerializeField] Toggle smoothTurnToggle;
        [SerializeField] Slider snapTurnSlider;
        [SerializeField] Toggle vignetteToggle;
        [SerializeField] Slider vignetteStrengthSlider;
        [SerializeField] BlockiverseComfortSettings settings;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        UnityAction<bool> toggleChanged;
        UnityAction<float> sliderChanged;
        Toggle registeredTeleportToggle;
        Toggle registeredGlideToggle;
        Toggle registeredSmoothTurnToggle;
        Toggle registeredVignetteToggle;
        Slider registeredSnapTurnSlider;
        Slider registeredVignetteStrengthSlider;

        public bool IsVisible => canvas != null && canvas.enabled;

        public void Configure(Canvas targetCanvas, BlockiverseComfortSettings comfortSettings)
        {
            canvas = targetCanvas;
            settings = comfortSettings;
            Hide(playFeedback: false);
        }

        public void ConfigureControls(
            Toggle targetGlideToggle,
            Toggle targetTeleportToggle,
            Toggle targetSmoothTurnToggle,
            Slider targetSnapTurnSlider,
            Toggle targetVignetteToggle = null,
            Slider targetVignetteStrengthSlider = null)
        {
            glideToggle = targetGlideToggle;
            teleportToggle = targetTeleportToggle;
            smoothTurnToggle = targetSmoothTurnToggle;
            snapTurnSlider = targetSnapTurnSlider;
            vignetteToggle = targetVignetteToggle;
            vignetteStrengthSlider = targetVignetteStrengthSlider;
            RegisterControlCallbacks();
            ApplyControls();
        }

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            BlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void Show()
        {
            if (canvas != null)
            {
                canvas.enabled = true;
                PlayFeedback(BlockiverseAudioCue.UiConfirm);
            }
        }

        public void Hide()
        {
            Hide(playFeedback: true);
        }

        void Hide(bool playFeedback)
        {
            if (canvas != null)
            {
                canvas.enabled = false;
                if (playFeedback)
                    PlayFeedback(BlockiverseAudioCue.UiCancel);
            }
        }

        public void ToggleVisible()
        {
            if (IsVisible)
                Hide();
            else
                Show();
        }

        public void ApplyControls()
        {
            if (settings == null)
                return;

            // Locomotion mode: Glide/Teleport are mutually exclusive.
            // Glide toggle takes priority over the legacy teleport toggle.
            if (glideToggle != null)
            {
                settings.LocomotionMode = glideToggle.isOn
                    ? BlockiverseLocomotionMode.Glide
                    : BlockiverseLocomotionMode.Teleport;
            }
            else if (teleportToggle != null)
            {
                settings.LocomotionMode = teleportToggle.isOn
                    ? BlockiverseLocomotionMode.Teleport
                    : BlockiverseLocomotionMode.Glide;
            }

            if (smoothTurnToggle != null)
                settings.SmoothTurnEnabled = smoothTurnToggle.isOn;

            if (snapTurnSlider != null)
                settings.SnapTurnDegrees = snapTurnSlider.value;

            if (vignetteToggle != null)
                settings.VignetteEnabled = vignetteToggle.isOn;

            if (vignetteStrengthSlider != null)
                settings.VignetteStrength = vignetteStrengthSlider.value;
        }

        void Awake()
        {
            RegisterControlCallbacks();
            ApplyControls();
        }

        void OnDestroy()
        {
            UnregisterControlCallbacks();
        }

        void RegisterControlCallbacks()
        {
            toggleChanged ??= _ => ApplyControlsWithFeedback();
            sliderChanged ??= _ => ApplyControlsWithFeedback();

            RegisterToggleCallback(teleportToggle, ref registeredTeleportToggle);
            RegisterToggleCallback(glideToggle, ref registeredGlideToggle);
            RegisterToggleCallback(smoothTurnToggle, ref registeredSmoothTurnToggle);
            RegisterToggleCallback(vignetteToggle, ref registeredVignetteToggle);
            RegisterSliderCallback(snapTurnSlider, ref registeredSnapTurnSlider);
            RegisterSliderCallback(vignetteStrengthSlider, ref registeredVignetteStrengthSlider);
        }

        void RegisterToggleCallback(Toggle targetToggle, ref Toggle registeredToggle)
        {
            if (registeredToggle == targetToggle)
                return;

            if (registeredToggle != null)
                registeredToggle.onValueChanged.RemoveListener(toggleChanged);

            registeredToggle = targetToggle;

            if (registeredToggle != null)
                registeredToggle.onValueChanged.AddListener(toggleChanged);
        }

        void RegisterSliderCallback(Slider targetSlider, ref Slider registeredSlider)
        {
            if (registeredSlider == targetSlider)
                return;

            if (registeredSlider != null)
                registeredSlider.onValueChanged.RemoveListener(sliderChanged);

            registeredSlider = targetSlider;

            if (registeredSlider != null)
                registeredSlider.onValueChanged.AddListener(sliderChanged);
        }

        void UnregisterControlCallbacks()
        {
            if (registeredTeleportToggle != null)
                registeredTeleportToggle.onValueChanged.RemoveListener(toggleChanged);

            if (registeredGlideToggle != null)
                registeredGlideToggle.onValueChanged.RemoveListener(toggleChanged);

            if (registeredSmoothTurnToggle != null)
                registeredSmoothTurnToggle.onValueChanged.RemoveListener(toggleChanged);

            if (registeredVignetteToggle != null)
                registeredVignetteToggle.onValueChanged.RemoveListener(toggleChanged);

            if (registeredSnapTurnSlider != null)
                registeredSnapTurnSlider.onValueChanged.RemoveListener(sliderChanged);

            if (registeredVignetteStrengthSlider != null)
                registeredVignetteStrengthSlider.onValueChanged.RemoveListener(sliderChanged);

            registeredTeleportToggle = null;
            registeredGlideToggle = null;
            registeredSmoothTurnToggle = null;
            registeredVignetteToggle = null;
            registeredSnapTurnSlider = null;
            registeredVignetteStrengthSlider = null;
        }

        void ApplyControlsWithFeedback()
        {
            ApplyControls();
            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            DiscoverFeedback();
            audioCuePlayer?.PlayCue(cue);
            interactionHaptics?.PlayUiTick();
        }

        void DiscoverFeedback()
        {
            if (!Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (interactionHaptics == null)
                interactionHaptics = FindFirstObjectByType<BlockiverseInteractionHaptics>();
        }
    }
}

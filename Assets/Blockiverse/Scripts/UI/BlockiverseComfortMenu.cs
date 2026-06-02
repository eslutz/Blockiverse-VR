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
        [SerializeField] Toggle glideToggle;
        [SerializeField] Toggle teleportToggle;
        [SerializeField] Toggle smoothTurnToggle;
        [SerializeField] Slider snapTurnSlider;
        [SerializeField] Toggle vignetteToggle;
        [SerializeField] Slider vignetteStrengthSlider;
        [SerializeField] BlockiverseComfortSettings settings;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        // Locomotion mode toggles use dedicated callbacks so each can enforce mutual exclusion.
        UnityAction<bool> onGlideChanged;
        UnityAction<bool> onTeleportChanged;
        Toggle registeredGlideToggle;
        Toggle registeredTeleportToggle;

        // Remaining controls share the same handler.
        UnityAction<bool> toggleChanged;
        UnityAction<float> sliderChanged;
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
            SyncTogglesToSettings();
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

        void Awake()
        {
            RegisterControlCallbacks();
            SyncTogglesToSettings();
        }

        void OnDestroy()
        {
            UnregisterControlCallbacks();
        }

        void RegisterControlCallbacks()
        {
            // Glide and Teleport are a radio pair: selecting one deselects the other.
            onGlideChanged ??= OnGlideToggled;
            onTeleportChanged ??= OnTeleportToggled;
            RegisterLocomotionToggle(glideToggle, ref registeredGlideToggle, onGlideChanged);
            RegisterLocomotionToggle(teleportToggle, ref registeredTeleportToggle, onTeleportChanged);

            toggleChanged ??= _ => ApplyOtherControlsWithFeedback();
            sliderChanged ??= _ => ApplyOtherControlsWithFeedback();
            RegisterToggleCallback(smoothTurnToggle, ref registeredSmoothTurnToggle);
            RegisterToggleCallback(vignetteToggle, ref registeredVignetteToggle);
            RegisterSliderCallback(snapTurnSlider, ref registeredSnapTurnSlider);
            RegisterSliderCallback(vignetteStrengthSlider, ref registeredVignetteStrengthSlider);
        }

        void OnGlideToggled(bool isOn)
        {
            if (settings == null) return;
            if (isOn)
            {
                settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
                // Turn off the other button without re-triggering its callback.
                teleportToggle?.SetIsOnWithoutNotify(false);
            }
            else
            {
                // Prevent both-off: switching Glide off implicitly selects Teleport.
                settings.LocomotionMode = BlockiverseLocomotionMode.Teleport;
                teleportToggle?.SetIsOnWithoutNotify(true);
            }
            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        void OnTeleportToggled(bool isOn)
        {
            if (settings == null) return;
            if (isOn)
            {
                settings.LocomotionMode = BlockiverseLocomotionMode.Teleport;
                glideToggle?.SetIsOnWithoutNotify(false);
            }
            else
            {
                // Prevent both-off: switching Teleport off implicitly selects Glide.
                settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
                glideToggle?.SetIsOnWithoutNotify(true);
            }
            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        void ApplyOtherControlsWithFeedback()
        {
            if (settings == null) return;

            if (smoothTurnToggle != null)
                settings.SmoothTurnEnabled = smoothTurnToggle.isOn;

            if (snapTurnSlider != null)
                settings.SnapTurnDegrees = snapTurnSlider.value;

            if (vignetteToggle != null)
                settings.VignetteEnabled = vignetteToggle.isOn;

            if (vignetteStrengthSlider != null)
                settings.VignetteStrength = vignetteStrengthSlider.value;

            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        // Pushes current settings values onto the toggle/slider widgets without triggering callbacks.
        void SyncTogglesToSettings()
        {
            if (settings == null) return;

            bool isGlide = settings.LocomotionMode == BlockiverseLocomotionMode.Glide;
            glideToggle?.SetIsOnWithoutNotify(isGlide);
            teleportToggle?.SetIsOnWithoutNotify(!isGlide);

            smoothTurnToggle?.SetIsOnWithoutNotify(settings.SmoothTurnEnabled);

            if (snapTurnSlider != null)
            {
                snapTurnSlider.SetValueWithoutNotify(settings.SnapTurnDegrees);
            }

            vignetteToggle?.SetIsOnWithoutNotify(settings.VignetteEnabled);

            if (vignetteStrengthSlider != null)
                vignetteStrengthSlider.SetValueWithoutNotify(settings.VignetteStrength);
        }

        void RegisterLocomotionToggle(Toggle target, ref Toggle registered, UnityAction<bool> action)
        {
            if (registered == target) return;
            registered?.onValueChanged.RemoveListener(action);
            registered = target;
            registered?.onValueChanged.AddListener(action);
        }

        void RegisterToggleCallback(Toggle targetToggle, ref Toggle registeredToggle)
        {
            if (registeredToggle == targetToggle) return;
            registeredToggle?.onValueChanged.RemoveListener(toggleChanged);
            registeredToggle = targetToggle;
            registeredToggle?.onValueChanged.AddListener(toggleChanged);
        }

        void RegisterSliderCallback(Slider targetSlider, ref Slider registeredSlider)
        {
            if (registeredSlider == targetSlider) return;
            registeredSlider?.onValueChanged.RemoveListener(sliderChanged);
            registeredSlider = targetSlider;
            registeredSlider?.onValueChanged.AddListener(sliderChanged);
        }

        void UnregisterControlCallbacks()
        {
            registeredGlideToggle?.onValueChanged.RemoveListener(onGlideChanged);
            registeredTeleportToggle?.onValueChanged.RemoveListener(onTeleportChanged);
            registeredSmoothTurnToggle?.onValueChanged.RemoveListener(toggleChanged);
            registeredVignetteToggle?.onValueChanged.RemoveListener(toggleChanged);
            registeredSnapTurnSlider?.onValueChanged.RemoveListener(sliderChanged);
            registeredVignetteStrengthSlider?.onValueChanged.RemoveListener(sliderChanged);

            registeredGlideToggle = null;
            registeredTeleportToggle = null;
            registeredSmoothTurnToggle = null;
            registeredVignetteToggle = null;
            registeredSnapTurnSlider = null;
            registeredVignetteStrengthSlider = null;
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

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
        [SerializeField] Toggle teleportToggle;
        [SerializeField] Toggle smoothTurnToggle;
        [SerializeField] Slider snapTurnSlider;
        [SerializeField] BlockiverseComfortSettings settings;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        UnityAction<bool> toggleChanged;
        UnityAction<float> sliderChanged;
        Toggle registeredTeleportToggle;
        Toggle registeredSmoothTurnToggle;
        Slider registeredSnapTurnSlider;

        public bool IsVisible => canvas != null && canvas.enabled;

        public void Configure(Canvas targetCanvas, BlockiverseComfortSettings comfortSettings)
        {
            canvas = targetCanvas;
            settings = comfortSettings;
            Hide(playFeedback: false);
        }

        public void ConfigureControls(
            Toggle targetTeleportToggle,
            Toggle targetSmoothTurnToggle,
            Slider targetSnapTurnSlider)
        {
            teleportToggle = targetTeleportToggle;
            smoothTurnToggle = targetSmoothTurnToggle;
            snapTurnSlider = targetSnapTurnSlider;
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

            if (teleportToggle != null)
                settings.TeleportEnabled = teleportToggle.isOn;

            if (smoothTurnToggle != null)
                settings.SmoothTurnEnabled = smoothTurnToggle.isOn;

            if (snapTurnSlider != null)
                settings.SnapTurnDegrees = snapTurnSlider.value;
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
            RegisterToggleCallback(smoothTurnToggle, ref registeredSmoothTurnToggle);
            RegisterSliderCallback(snapTurnSlider, ref registeredSnapTurnSlider);
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

            if (registeredSmoothTurnToggle != null)
                registeredSmoothTurnToggle.onValueChanged.RemoveListener(toggleChanged);

            if (registeredSnapTurnSlider != null)
                registeredSnapTurnSlider.onValueChanged.RemoveListener(sliderChanged);

            registeredTeleportToggle = null;
            registeredSmoothTurnToggle = null;
            registeredSnapTurnSlider = null;
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

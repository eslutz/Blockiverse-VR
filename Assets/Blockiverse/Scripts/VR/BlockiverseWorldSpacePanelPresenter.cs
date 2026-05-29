using Blockiverse.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.VR
{
    public sealed class BlockiverseWorldSpacePanelPresenter : MonoBehaviour
    {
        [SerializeField] Canvas targetCanvas;
        [SerializeField] Transform headset;
        [SerializeField] float distanceMeters = 1.2f;
        [SerializeField] float horizontalOffsetMeters;
        [SerializeField] float verticalOffsetMeters = -0.1f;
        [SerializeField] float pitchDegrees;
        [SerializeField] float panelScale = 0.002f;
        [SerializeField] bool recenterOnShow = true;
        [SerializeField] bool showOnStart;
        [SerializeField] bool playShowFeedback;
        [SerializeField] BlockiverseAudioCue showFeedbackCue = BlockiverseAudioCue.UiConfirm;
        [SerializeField] bool playHideFeedback;
        [SerializeField] BlockiverseAudioCue hideFeedbackCue = BlockiverseAudioCue.UiCancel;
        [SerializeField] bool hapticOnShow = true;
        [SerializeField] bool hapticOnHide = true;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        CreativeHotbar hotbar;
        bool subscribedToHotbarSelection;

        public Canvas TargetCanvas => targetCanvas;
        public bool IsVisible => targetCanvas != null && targetCanvas.enabled;
        public bool ShowOnStart => showOnStart;
        public bool PlaysShowFeedback => playShowFeedback;
        public bool PlaysHideFeedback => playHideFeedback;
        public BlockiverseAudioCue ShowFeedbackCue => showFeedbackCue;
        public BlockiverseAudioCue HideFeedbackCue => hideFeedbackCue;

        public void Configure(
            Canvas canvas,
            Transform targetHeadset,
            float distance,
            float horizontalOffset,
            float verticalOffset,
            float pitch,
            float scale = 0.002f,
            bool recenterWhenShown = true,
            bool showWhenStarted = false)
        {
            targetCanvas = canvas;
            headset = targetHeadset;
            distanceMeters = distance;
            horizontalOffsetMeters = horizontalOffset;
            verticalOffsetMeters = verticalOffset;
            pitchDegrees = pitch;
            panelScale = scale;
            recenterOnShow = recenterWhenShown;
            showOnStart = showWhenStarted;
            DiscoverHotbarSelection();
            SubscribeHotbarSelectionFeedback();
        }

        public void ConfigureFeedback(
            BlockiverseAudioCue targetShowCue,
            BlockiverseAudioCue targetHideCue,
            bool enableShowFeedback = true,
            bool enableHideFeedback = true)
        {
            playShowFeedback = enableShowFeedback;
            playHideFeedback = enableHideFeedback;
            showFeedbackCue = targetShowCue;
            hideFeedbackCue = targetHideCue;
        }

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            BlockiverseInteractionHaptics targetInteractionHaptics,
            BlockiverseAudioCue targetShowCue,
            BlockiverseAudioCue targetHideCue,
            bool enableShowFeedback = true,
            bool enableHideFeedback = true)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
            ConfigureFeedback(targetShowCue, targetHideCue, enableShowFeedback, enableHideFeedback);
        }

        public void Show()
        {
            EnsureCanvas();
            bool wasVisible = IsVisible;

            if (recenterOnShow)
                Recenter();

            if (targetCanvas != null)
            {
                targetCanvas.enabled = true;
                if (!wasVisible)
                    PlayFeedback(showFeedbackCue, playShowFeedback, hapticOnShow);
            }
        }

        public void Hide()
        {
            EnsureCanvas();
            bool wasVisible = IsVisible;

            if (targetCanvas != null)
            {
                targetCanvas.enabled = false;
                if (wasVisible)
                    PlayFeedback(hideFeedbackCue, playHideFeedback, hapticOnHide);
            }
        }

        public void ToggleVisible()
        {
            EnsureCanvas();

            if (targetCanvas != null && targetCanvas.enabled)
                Hide();
            else
                Show();
        }

        public void Recenter()
        {
            Transform target = headset != null ? headset : Camera.main != null ? Camera.main.transform : null;

            if (target == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.ProjectOnPlane(target.up, Vector3.up);

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 position = target.position
                + forward * Mathf.Max(0.1f, distanceMeters)
                + right * horizontalOffsetMeters
                + Vector3.up * verticalOffsetMeters;

            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(pitchDegrees, 0.0f, 0.0f));
            transform.localScale = Vector3.one * panelScale;
        }

        void Awake()
        {
            EnsureCanvas();
            DiscoverHotbarSelection();
        }

        void OnEnable()
        {
            DiscoverHotbarSelection();
            SubscribeHotbarSelectionFeedback();
        }

        void OnDisable()
        {
            UnsubscribeHotbarSelectionFeedback();
        }

        void Start()
        {
            if (showOnStart)
                Show();
        }

        void EnsureCanvas()
        {
            if (targetCanvas == null)
                targetCanvas = GetComponent<Canvas>();
        }

        void DiscoverHotbarSelection()
        {
            if (hotbar == null)
                TryGetComponent(out hotbar);
        }

        void SubscribeHotbarSelectionFeedback()
        {
            if (subscribedToHotbarSelection || hotbar == null)
                return;

            hotbar.SelectionChanged.AddListener(OnHotbarSelectionChanged);
            subscribedToHotbarSelection = true;
        }

        void UnsubscribeHotbarSelectionFeedback()
        {
            if (!subscribedToHotbarSelection || hotbar == null)
                return;

            hotbar.SelectionChanged.RemoveListener(OnHotbarSelectionChanged);
            subscribedToHotbarSelection = false;
        }

        void OnHotbarSelectionChanged()
        {
            if (IsVisible)
                PlayFeedback(BlockiverseAudioCue.UiSelect, playAudio: false, playHaptic: true);
        }

        void PlayFeedback(BlockiverseAudioCue cue, bool playAudio, bool playHaptic)
        {
            if (!playAudio && !playHaptic)
                return;

            DiscoverFeedback();

            if (playAudio)
                audioCuePlayer?.PlayCue(cue);

            if (playHaptic)
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

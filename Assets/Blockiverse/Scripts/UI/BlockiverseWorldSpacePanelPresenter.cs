using System;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class BlockiverseWorldSpacePanelPresenter : MonoBehaviour
    {
        public const string ControllerMappingPopupSeenPrefKey = "Blockiverse.ControllerMappingPopupSeen";

        [SerializeField] Canvas targetCanvas;
        [SerializeField] GameObject targetRoot;
        [SerializeField] Transform placementRoot;
        [SerializeField] Transform headset;
        [SerializeField] float distanceMeters = 1.2f;
        [SerializeField] float horizontalOffsetMeters;
        [SerializeField] float verticalOffsetMeters = -0.1f;
        [SerializeField] float pitchDegrees;
        [SerializeField] float panelScale = 0.002f;
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] bool recenterOnShow = true;
        [SerializeField] BlockiversePanelPlacementMode placementMode = BlockiversePanelPlacementMode.RecenterOnShow;
        [SerializeField] float followYawThresholdDegrees = BlockiversePanelPlacement.DefaultFollowYawThresholdDegrees;
        [SerializeField] float followDistanceThresholdMeters = BlockiversePanelPlacement.DefaultFollowDistanceThresholdMeters;
        [SerializeField] float followSmoothingSeconds = BlockiversePanelPlacement.DefaultFollowSmoothingSeconds;
        Pose worldFixedPose;
        bool hasWorldFixedPose;
        bool followGliding;
        [SerializeField] bool showOnStart;
        [SerializeField] bool playShowFeedback;
        [SerializeField] BlockiverseAudioCue showFeedbackCue = BlockiverseAudioCue.UiConfirm;
        [SerializeField] bool playHideFeedback;
        [SerializeField] BlockiverseAudioCue hideFeedbackCue = BlockiverseAudioCue.UiCancel;
        [SerializeField] bool hapticOnShow = true;
        [SerializeField] bool hapticOnHide = true;
        [SerializeField] string showOnStartPlayerPrefsKey;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] MonoBehaviour serializedInteractionHaptics;
        IBlockiverseInteractionHaptics interactionHaptics;

        CreativeHotbar hotbar;
        bool subscribedToHotbarSelection;
        bool hasVisibilityCommand;
        [SerializeField] bool usesSharedCompositionRoot;
        float lastAppliedPanelScale = -1.0f;

        public Canvas TargetCanvas => targetCanvas;
        public GameObject TargetRoot => ResolveTargetRoot();
        public Transform PlacementRoot => placementRoot != null ? placementRoot : transform;
        public bool IsVisible
        {
            get
            {
                if (targetCanvas != null)
                    return targetCanvas.enabled;

                GameObject root = ResolveTargetRoot();
                return root != null && root.activeSelf;
            }
        }
        public bool ShowOnStart => showOnStart;
        public bool PlaysShowFeedback => playShowFeedback;
        public bool PlaysHideFeedback => playHideFeedback;
        public BlockiverseAudioCue ShowFeedbackCue => showFeedbackCue;
        public BlockiverseAudioCue HideFeedbackCue => hideFeedbackCue;
        public string ShowOnStartPlayerPrefsKey => showOnStartPlayerPrefsKey;
        public bool UsesSharedCompositionRoot => usesSharedCompositionRoot;
        public BlockiversePanelPlacementMode PlacementMode => placementMode;
        public bool HasWorldFixedPose => hasWorldFixedPose;
        public Pose WorldFixedPose => worldFixedPose;
        // True while a lazy follow is still gliding toward its target.
        public bool IsFollowGliding => followGliding;

        // Selects how this panel is placed. WorldFixed requires a pose via SetWorldFixedPose;
        // LazyFollow drives placement from LateUpdate while visible.
        public void SetPlacementMode(BlockiversePanelPlacementMode mode)
        {
            placementMode = mode;
            followGliding = false;
        }

        // Pins the panel to an explicit world pose (title mini-world fixture). Applies
        // immediately if the panel is visible; otherwise on the next Show.
        public void SetWorldFixedPose(Pose pose)
        {
            worldFixedPose = pose;
            hasWorldFixedPose = true;
            if (placementMode == BlockiversePanelPlacementMode.WorldFixed && IsVisible)
                ApplyWorldFixedPose();
        }

        public void ConfigureFollow(float yawThresholdDegrees, float distanceThresholdMeters, float smoothingSeconds)
        {
            followYawThresholdDegrees = Mathf.Max(0.0f, yawThresholdDegrees);
            followDistanceThresholdMeters = Mathf.Max(0.0f, distanceThresholdMeters);
            followSmoothingSeconds = Mathf.Max(0.0f, smoothingSeconds);
        }

        public void Configure(
            Canvas canvas,
            Transform targetHeadset,
            float distance,
            float horizontalOffset,
            float verticalOffset,
            float pitch,
            float scale = 0.002f,
            bool recenterWhenShown = true,
            bool showWhenStarted = false,
            string showWhenStartedPlayerPrefsKey = null)
        {
            targetCanvas = canvas;
            targetRoot = canvas != null ? canvas.gameObject : gameObject;
            placementRoot = transform;
            headset = targetHeadset;
            distanceMeters = distance;
            horizontalOffsetMeters = horizontalOffset;
            verticalOffsetMeters = verticalOffset;
            pitchDegrees = pitch;
            panelScale = scale;
            recenterOnShow = recenterWhenShown;
            showOnStart = showWhenStarted;
            showOnStartPlayerPrefsKey = showWhenStartedPlayerPrefsKey;
            usesSharedCompositionRoot = false;
            DiscoverHotbarSelection();
            SubscribeHotbarSelectionFeedback();
        }


        public void ConfigureSharedCompositionTarget(GameObject root, Transform sharedPlacementRoot)
        {
            targetCanvas = null;
            targetRoot = root != null ? root : gameObject;
            placementRoot = sharedPlacementRoot != null ? sharedPlacementRoot : transform;
            usesSharedCompositionRoot = sharedPlacementRoot != null && placementRoot != transform;
            DiscoverHotbarSelection();
            SubscribeHotbarSelectionFeedback();
        }

        public void ConfigureComfortSettings(BlockiverseComfortSettings settings)
        {
            comfortSettings = settings;
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
            IBlockiverseInteractionHaptics targetInteractionHaptics,
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
            Show(recenterPlacement: true);
        }

        public void Show(bool recenterPlacement)
        {
            EnsureCanvas();
            hasVisibilityCommand = true;
            bool wasVisible = IsVisible;

            switch (placementMode)
            {
                case BlockiversePanelPlacementMode.WorldFixed:
                    // Fixture pose: never derived from the headset. Fall back to a one-time
                    // recenter only when no fixture pose has been supplied yet.
                    if (hasWorldFixedPose)
                        ApplyWorldFixedPose();
                    else if (recenterPlacement && recenterOnShow)
                        Recenter();
                    break;
                case BlockiversePanelPlacementMode.LazyFollow:
                    // Snap to the follow target on first show so the panel opens in front of
                    // the player; subsequent navigation keeps the pose and LateUpdate glides.
                    if (recenterPlacement && recenterOnShow)
                    {
                        Recenter();
                        followGliding = false;
                    }
                    break;
                default:
                    if (recenterPlacement && recenterOnShow)
                        Recenter();
                    break;
            }

            GameObject root = ResolveTargetRoot();
            if (root != null && !root.activeSelf)
                root.SetActive(true);

            if (targetCanvas != null)
                targetCanvas.enabled = true;
            else if (root != null)
                root.SetActive(true);

            if (!wasVisible && IsVisible)
                PlayFeedback(showFeedbackCue, playShowFeedback, hapticOnShow);
        }

        public void Hide()
        {
            EnsureCanvas();
            hasVisibilityCommand = true;
            bool wasVisible = IsVisible;

            GameObject root = ResolveTargetRoot();
            if (targetCanvas != null)
                targetCanvas.enabled = false;
            else if (root != null)
                root.SetActive(false);

            if (wasVisible)
            {
                MarkShowOnStartSeen();
                PlayFeedback(hideFeedbackCue, playHideFeedback, hapticOnHide);
            }
        }

        public void ToggleVisible()
        {
            EnsureCanvas();

            if (IsVisible)
                Hide();
            else
                Show();
        }

        public void Recenter()
        {
            Transform target = ResolveHeadTarget();

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

            Transform root = PlacementRoot;
            if (Application.isPlaying && root.parent != null)
            {
                root.SetParent(null, true);
            }
            root.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(pitchDegrees, 0.0f, 0.0f));
            float resolvedScale = ResolvePanelScale();
            root.localScale = Vector3.one * resolvedScale;
            lastAppliedPanelScale = resolvedScale;
        }

        public void ApplyPlacementFrom(BlockiverseWorldSpacePanelPresenter source)
        {
            if (source == null)
                return;

            EnsureCanvas();
            Transform root = PlacementRoot;
            Transform sourceRoot = source.PlacementRoot;
            root.SetPositionAndRotation(sourceRoot.position, sourceRoot.rotation);
            root.localScale = sourceRoot.localScale;
            lastAppliedPanelScale = ResolvePanelScale();
        }

        void Awake()
        {
            if (interactionHaptics == null && serializedInteractionHaptics is IBlockiverseInteractionHaptics haptics)
                interactionHaptics = haptics;
            ApplyDefaultStartGateKey();
            EnsureCanvas();
            DiscoverHotbarSelection();
            DiscoverComfortSettings();
        }

        void OnEnable()
        {
            DiscoverHotbarSelection();
            DiscoverComfortSettings();
            SubscribeHotbarSelectionFeedback();
        }

        void OnDisable()
        {
            UnsubscribeHotbarSelectionFeedback();
        }

        void Start()
        {
            ApplyDefaultStartGateKey();
            if (showOnStart && ShouldShowOnStart())
            {
                Show();
                return;
            }

            if (!hasVisibilityCommand)
                HideTargetWithoutFeedback();
        }

        void Update()
        {
            if (!IsVisible)
                return;

            if (placementMode == BlockiversePanelPlacementMode.WorldFixed)
            {
                // Comfort scale changes still apply, but at the fixture pose.
                if (hasWorldFixedPose && !Mathf.Approximately(lastAppliedPanelScale, ResolvePanelScale()))
                    ApplyWorldFixedPose();
                return;
            }

            if (!recenterOnShow)
                return;

            if (!Mathf.Approximately(lastAppliedPanelScale, ResolvePanelScale()))
                Recenter();
        }

        void LateUpdate()
        {
            if (placementMode != BlockiversePanelPlacementMode.LazyFollow || !IsVisible)
                return;

            Transform target = ResolveHeadTarget();
            if (target == null)
                return;

            Transform root = PlacementRoot;
            var current = new Pose(root.position, root.rotation);
            Pose followTarget = BlockiversePanelPlacement.FollowTargetPose(
                target.position, target.forward, distanceMeters, horizontalOffsetMeters, verticalOffsetMeters);

            if (!followGliding && BlockiversePanelPlacement.ShouldRecenter(
                    current, target.position, target.forward, distanceMeters,
                    followYawThresholdDegrees, followDistanceThresholdMeters))
            {
                followGliding = true;
            }

            if (!followGliding)
                return;

            Pose next = BlockiversePanelPlacement.SmoothToward(current, followTarget, followSmoothingSeconds, Time.deltaTime);
            root.SetPositionAndRotation(next.position, next.rotation);

            if (Vector3.Distance(next.position, followTarget.position) < 0.01f &&
                Quaternion.Angle(next.rotation, followTarget.rotation) < 0.5f)
            {
                root.SetPositionAndRotation(followTarget.position, followTarget.rotation);
                followGliding = false;
            }
        }

        // Places the panel at its fixture pose (WorldFixed mode).
        void ApplyWorldFixedPose()
        {
            EnsureCanvas();
            Transform root = PlacementRoot;
            if (Application.isPlaying && root.parent != null)
                root.SetParent(null, true);
            root.SetPositionAndRotation(worldFixedPose.position, worldFixedPose.rotation);
            float resolvedScale = ResolvePanelScale();
            root.localScale = Vector3.one * resolvedScale;
            lastAppliedPanelScale = resolvedScale;
        }

        Transform ResolveHeadTarget() =>
            headset != null ? headset : Camera.main != null ? Camera.main.transform : null;

        void EnsureCanvas()
        {
            if (targetCanvas == null && targetRoot == null)
                targetCanvas = GetComponent<Canvas>();

            if (targetRoot == null)
                targetRoot = targetCanvas != null ? targetCanvas.gameObject : gameObject;

            if (placementRoot == null)
                placementRoot = transform;
        }

        GameObject ResolveTargetRoot()
        {
            if (targetRoot != null)
                return targetRoot;

            return targetCanvas != null ? targetCanvas.gameObject : gameObject;
        }

        void HideTargetWithoutFeedback()
        {
            if (targetCanvas != null)
                targetCanvas.enabled = false;
            else
            {
                GameObject root = ResolveTargetRoot();
                if (root != null)
                    root.SetActive(false);
            }
        }

        void DiscoverHotbarSelection()
        {
            if (hotbar == null)
                TryGetComponent(out hotbar);
        }

        void DiscoverComfortSettings()
        {
            if (comfortSettings == null && Application.isPlaying)
                comfortSettings = FindFirstObjectByType<BlockiverseComfortSettings>(FindObjectsInactive.Include);
        }

        float ResolvePanelScale()
        {
            if (usesSharedCompositionRoot)
                return 1.0f;

            DiscoverComfortSettings();
            return panelScale * (comfortSettings != null ? comfortSettings.UiScale : 1.0f);
        }

        bool ShouldShowOnStart()
        {
            return string.IsNullOrEmpty(showOnStartPlayerPrefsKey) ||
                   PlayerPrefs.GetInt(showOnStartPlayerPrefsKey, 0) == 0;
        }

        void ApplyDefaultStartGateKey()
        {
            if (!string.IsNullOrEmpty(showOnStartPlayerPrefsKey))
                return;

            if (string.Equals(gameObject.name, "Controller Mapping Popup", StringComparison.Ordinal))
                showOnStartPlayerPrefsKey = ControllerMappingPopupSeenPrefKey;
        }

        void MarkShowOnStartSeen()
        {
            if (string.IsNullOrEmpty(showOnStartPlayerPrefsKey))
                return;

            PlayerPrefs.SetInt(showOnStartPlayerPrefsKey, 1);
            PlayerPrefs.Save();
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

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue, playAudio, playHaptic);
        }
    }
}

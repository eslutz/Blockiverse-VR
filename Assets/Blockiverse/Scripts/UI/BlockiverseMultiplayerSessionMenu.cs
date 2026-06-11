using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.VR;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class BlockiverseMultiplayerSessionMenu : MonoBehaviour
    {
        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] Button hostButton;
        [SerializeField] Button joinButton;
        [SerializeField] Button stopButton;
        [SerializeField] TMP_InputField addressInput;
        [SerializeField] TMP_Text statusText;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        UnityAction hostClicked;
        UnityAction joinClicked;
        UnityAction stopClicked;
        Button registeredHostButton;
        Button registeredJoinButton;
        Button registeredStopButton;
        BlockiverseConnectionState lastDisplayedState;
        NetworkSessionMode lastDisplayedMode;
        string lastDisplayedDisconnectReason = string.Empty;
        bool lastAppliedCanStart;
        bool lastAppliedCanStop;
        Canvas[] sessionEndedCanvases;
        BaseRaycaster[] sessionEndedRaycasters;

        public BlockiverseNetworkSession Session => session;
        public TMP_Text StatusText => statusText;
        public TMP_InputField AddressInput => addressInput;
        public Button HostButton => hostButton;
        public Button JoinButton => joinButton;
        public Button StopButton => stopButton;
        public bool IsShowingSessionEndedMessage => session != null &&
            session.CurrentState == BlockiverseConnectionState.Disconnected &&
            session.HasConnectedAsClient;

        public void Configure(BlockiverseNetworkSession targetSession)
        {
            session = targetSession;
            RefreshStatus();
        }

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            BlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void ConfigureControls(
            Button targetHostButton,
            Button targetJoinButton,
            Button targetStopButton,
            TMP_InputField targetAddressInput,
            TMP_Text targetStatusText)
        {
            hostButton = targetHostButton;
            joinButton = targetJoinButton;
            stopButton = targetStopButton;
            addressInput = targetAddressInput;
            statusText = targetStatusText;
            RegisterControlCallbacks();
            ApplyDefaultAddressText();
            RefreshStatus();
        }

        public void StartLanHost()
        {
            if (session == null)
            {
                SetStatus("LAN session is unavailable.");
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            bool started = session.StartHost();
            SetStatus(started
                ? "Starting LAN host..."
                : $"Unable to start LAN host. {DescribeSessionState()}");
            PlayFeedback(started ? BlockiverseAudioCue.UiConfirm : BlockiverseAudioCue.UiCancel);
            RefreshControls();
        }

        public void JoinLanSession()
        {
            if (session == null)
            {
                SetStatus("LAN session is unavailable.");
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            string address = ResolveJoinAddress();
            bool started = session.StartClient(address);
            SetStatus(started
                ? $"Joining LAN session at {address}:{session.Config.Port}..."
                : $"Unable to join LAN session at {address}:{session.Config.Port}. {DescribeSessionState()}");
            PlayFeedback(started ? BlockiverseAudioCue.UiConfirm : BlockiverseAudioCue.UiCancel);
            RefreshControls();
        }

        public void StopSession()
        {
            if (session == null)
            {
                SetStatus("LAN session is unavailable.");
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            bool wasActive = session.NetworkManager.IsListening || session.NetworkManager.ShutdownInProgress;
            session.StopSession();
            SetStatus(DescribeStopSessionResult(wasActive));
            PlayFeedback(BlockiverseAudioCue.UiCancel);
            RefreshControls();
        }

        public string ResolveJoinAddress()
        {
            if (addressInput == null || string.IsNullOrWhiteSpace(addressInput.text))
                return BlockiverseNetworkConfig.DefaultAddress;

            return addressInput.text.Trim();
        }

        public void RefreshStatus()
        {
            ApplyDefaultAddressText();

            if (session == null)
            {
                SetStatus("LAN session is unavailable.");
                RefreshControls();
                return;
            }

            SetStatus(DescribeSessionState());
            EnsureSessionEndedMenuAvailable();
            RefreshControls();
            lastDisplayedState = session.CurrentState;
            lastDisplayedMode = session.CurrentMode;
            lastDisplayedDisconnectReason = session.LastDisconnectReason;
        }

        void Awake()
        {
            DiscoverSession();
            RegisterControlCallbacks();
            ApplyDefaultAddressText();
            RefreshStatus();
        }

        float nextSessionSearchTime;

        // The rig-prefab panel cannot serialize a reference to the scene's network manager;
        // discover the session at runtime instead (throttled — scene walks are not per-frame work).
        void DiscoverSession()
        {
            if (session != null || !Application.isPlaying || Time.unscaledTime < nextSessionSearchTime)
                return;

            nextSessionSearchTime = Time.unscaledTime + 1.0f;
            session = FindFirstObjectByType<BlockiverseNetworkSession>(FindObjectsInactive.Include);
        }

        void Update()
        {
            if (session == null)
            {
                DiscoverSession();
                if (session != null)
                    RefreshStatus();
                return;
            }

            if (lastDisplayedState != session.CurrentState ||
                lastDisplayedMode != session.CurrentMode ||
                lastDisplayedDisconnectReason != session.LastDisconnectReason)
            {
                RefreshStatus();
                return;
            }

            // NetworkManager listening/shutdown flags can flip without a CurrentState transition
            // (e.g. ShutdownInProgress clearing after a host disconnect), so the control gating is
            // still polled — but only re-applied when the derived values change, to avoid dirtying
            // Button state every frame.
            ComputeControlState(out bool canStart, out bool canStop);
            if (canStart != lastAppliedCanStart || canStop != lastAppliedCanStop)
                RefreshControls();
        }

        void OnDestroy()
        {
            UnregisterControlCallbacks();
        }

        void ApplyDefaultAddressText()
        {
            if (addressInput != null && string.IsNullOrWhiteSpace(addressInput.text))
                addressInput.text = BlockiverseNetworkConfig.DefaultAddress;
        }

        string DescribeSessionState()
        {
            if (session == null)
                return "LAN session is unavailable.";

            return session.CurrentState switch
            {
                BlockiverseConnectionState.StartingHost => "Starting LAN host...",
                BlockiverseConnectionState.Hosting => session.LastStopRequestSucceeded
                    ? $"Hosting LAN session on {session.Config.ListenAddress}:{session.Config.Port}."
                    : DescribeStopSessionResult(wasActive: true),
                BlockiverseConnectionState.StartingClient => $"Joining LAN session at {ResolveJoinAddress()}:{session.Config.Port}...",
                BlockiverseConnectionState.ConnectedClient => $"Connected to LAN session at {ResolveJoinAddress()}:{session.Config.Port}.",
                BlockiverseConnectionState.Disconnecting => "Stopping LAN session...",
                BlockiverseConnectionState.Disconnected => DescribeDisconnectedState(),
                BlockiverseConnectionState.Failed => DescribeFailedState(),
                _ => $"LAN session stopped. Join address defaults to {BlockiverseNetworkConfig.DefaultAddress}.",
            };
        }

        string DescribeStopSessionResult(bool wasActive)
        {
            if (session == null)
                return "LAN session is unavailable.";

            if (!session.LastStopRequestSucceeded)
            {
                return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                    ? "Unable to stop LAN session."
                    : $"Unable to stop LAN session. {session.LastDisconnectReason}";
            }

            return wasActive ? "Stopping LAN session..." : "LAN session stopped.";
        }

        string DescribeDisconnectedState()
        {
            if (!IsShowingSessionEndedMessage)
                return DescribeUnableToReachHostState();

            string reconnectMessage =
                $"LAN session ended because the host disconnected. Use Join to reconnect to {ResolveJoinAddress()}:{session.Config.Port} when the LAN host is available again.";

            return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                ? reconnectMessage
                : $"{reconnectMessage} Last disconnect: {session.LastDisconnectReason}";
        }

        string DescribeUnableToReachHostState()
        {
            string retryMessage =
                $"Unable to reach LAN session at {ResolveJoinAddress()}:{session.Config.Port}. Check that the host is on the same LAN and try Join again.";

            return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                ? retryMessage
                : $"{retryMessage} Last disconnect: {session.LastDisconnectReason}";
        }

        string DescribeFailedState()
        {
            return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                ? "LAN session failed."
                : session.LastDisconnectReason;
        }

        void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        void ComputeControlState(out bool canStart, out bool canStop)
        {
            canStart = session != null &&
                !session.NetworkManager.IsListening &&
                !session.NetworkManager.ShutdownInProgress;
            canStop = session != null &&
                (session.NetworkManager.IsListening || session.NetworkManager.ShutdownInProgress);
        }

        void RefreshControls()
        {
            ComputeControlState(out bool canStart, out bool canStop);
            lastAppliedCanStart = canStart;
            lastAppliedCanStop = canStop;

            if (hostButton != null)
                hostButton.interactable = canStart;

            if (joinButton != null)
                joinButton.interactable = canStart;

            if (stopButton != null)
                stopButton.interactable = canStop;

            if (addressInput != null)
                addressInput.interactable = canStart;
        }

        void EnsureSessionEndedMenuAvailable()
        {
            if (!IsShowingSessionEndedMessage)
                return;

            // The panel's parent hierarchy is stable at runtime, so the allocating parent
            // lookups run once and the results are reused on later state transitions.
            sessionEndedCanvases ??= GetComponentsInParent<Canvas>(true);
            foreach (Canvas canvas in sessionEndedCanvases)
                canvas.enabled = true;

            // BaseRaycaster covers both the legacy GraphicRaycaster and XRI's
            // TrackedDeviceGraphicRaycaster used by the world-space VR menus.
            sessionEndedRaycasters ??= GetComponentsInParent<BaseRaycaster>(true);
            foreach (BaseRaycaster raycaster in sessionEndedRaycasters)
                raycaster.enabled = true;
        }

        void RegisterControlCallbacks()
        {
            hostClicked ??= StartLanHost;
            joinClicked ??= JoinLanSession;
            stopClicked ??= StopSession;

            RegisterButtonCallback(hostButton, ref registeredHostButton, hostClicked);
            RegisterButtonCallback(joinButton, ref registeredJoinButton, joinClicked);
            RegisterButtonCallback(stopButton, ref registeredStopButton, stopClicked);
        }

        static void RegisterButtonCallback(Button targetButton, ref Button registeredButton, UnityAction action)
        {
            if (registeredButton == targetButton)
                return;

            if (registeredButton != null)
                registeredButton.onClick.RemoveListener(action);

            registeredButton = targetButton;

            if (registeredButton != null)
                registeredButton.onClick.AddListener(action);
        }

        void UnregisterControlCallbacks()
        {
            if (registeredHostButton != null)
                registeredHostButton.onClick.RemoveListener(hostClicked);

            if (registeredJoinButton != null)
                registeredJoinButton.onClick.RemoveListener(joinClicked);

            if (registeredStopButton != null)
                registeredStopButton.onClick.RemoveListener(stopClicked);

            registeredHostButton = null;
            registeredJoinButton = null;
            registeredStopButton = null;
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

using Blockiverse.Gameplay;
using Blockiverse.MetaPlatform;
using Blockiverse.Networking;
using Blockiverse.VR;
using TMPro;
using UnityEngine;
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
        [SerializeField] BlockiverseWorldSessionController worldSessionController;
        [SerializeField] BlockiverseMenuController menuController;
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
        bool enteredGameplayForCurrentSession;
        bool sessionEndedRouteRequested;

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
            if (session != targetSession)
            {
                enteredGameplayForCurrentSession = false;
                sessionEndedRouteRequested = false;
            }

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

        public void ConfigureWorldSessionController(BlockiverseWorldSessionController controller)
        {
            worldSessionController = controller;
        }

        public void ConfigureMenuController(BlockiverseMenuController controller)
        {
            menuController = controller;
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
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            if (!TrySuspendSinglePlayerSessionForMultiplayer())
                return;

            bool started = session.StartHost();
            SetStatus(started
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStartingHost)
                : BlockiverseLocalization.Format(BlockiverseLocalization.Keys.LanStartHostFailed, DescribeSessionState()));
            PlayFeedback(started ? BlockiverseAudioCue.UiConfirm : BlockiverseAudioCue.UiCancel);
            RefreshStatus();
        }

        public void JoinLanSession()
        {
            if (session == null)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            string address = ResolveJoinAddress();
            if (!TrySuspendSinglePlayerSessionForMultiplayer())
                return;

            bool started = session.StartClient(address);
            SetStatus(started
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.LanJoining, address, session.Config.Port)
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanJoinFailed,
                    address,
                    session.Config.Port,
                    DescribeSessionState()));
            PlayFeedback(started ? BlockiverseAudioCue.UiConfirm : BlockiverseAudioCue.UiCancel);
            RefreshStatus();
        }

        public void StopSession()
        {
            if (session == null)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
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
                enteredGameplayForCurrentSession = false;
                sessionEndedRouteRequested = false;
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                RefreshControls();
                return;
            }

            SetStatus(DescribeSessionState());
            if (!IsShowingSessionEndedMessage)
                sessionEndedRouteRequested = false;

            TryEnterGameplayForConnectedSession();
            EnsureSessionEndedMenuAvailable();
            RefreshControls();
            lastDisplayedState = session.CurrentState;
            lastDisplayedMode = session.CurrentMode;
            lastDisplayedDisconnectReason = session.LastDisconnectReason;
        }

        void Awake()
        {
            DiscoverSession();
            DiscoverWorldSessionController();
            DiscoverMenuController();
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

        void DiscoverWorldSessionController()
        {
            if (worldSessionController != null)
                return;

            worldSessionController = FindFirstObjectByType<BlockiverseWorldSessionController>(FindObjectsInactive.Include);
        }

        void DiscoverMenuController()
        {
            if (menuController != null)
                return;

            menuController = FindFirstObjectByType<BlockiverseMenuController>(FindObjectsInactive.Include);
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

            if (IsShowingSessionEndedMessage)
                EnsureSessionEndedMenuAvailable();

            // NetworkManager listening/shutdown flags can flip without a CurrentState transition
            // (e.g. ShutdownInProgress clearing after a host disconnect), so the control gating is
            // still polled — but only re-applied when the derived values change, to avoid dirtying
            // Button state every frame.
            ComputeControlState(out bool canStart, out bool canStop);
            if (canStart != lastAppliedCanStart || canStop != lastAppliedCanStop)
                RefreshControls();
        }

        bool TrySuspendSinglePlayerSessionForMultiplayer()
        {
            DiscoverWorldSessionController();

            if (worldSessionController == null)
                return true;

            if (worldSessionController.TrySuspendActiveSessionForMultiplayer(out string failureReason))
                return true;

            SetStatus(string.IsNullOrWhiteSpace(failureReason)
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusSuspendSinglePlayerFailed)
                : failureReason);
            PlayFeedback(BlockiverseAudioCue.UiCancel);
            RefreshControls();
            return false;
        }

        void TryEnterGameplayForConnectedSession()
        {
            if (session == null)
                return;

            if (!IsGameplaySessionState(session.CurrentState))
            {
                enteredGameplayForCurrentSession = false;
                return;
            }

            if (enteredGameplayForCurrentSession)
                return;

            DiscoverMenuController();
            if (menuController == null)
                return;

            enteredGameplayForCurrentSession = true;
            menuController.EnterGameplay();
        }

        static bool IsGameplaySessionState(BlockiverseConnectionState state)
        {
            return state == BlockiverseConnectionState.Hosting ||
                state == BlockiverseConnectionState.ConnectedClient;
        }

        void OnDestroy()
        {
            UnregisterControlCallbacks();
        }

        void ApplyDefaultAddressText()
        {
            if (addressInput == null)
                return;

            if (addressInput.placeholder is TMP_Text placeholder)
                placeholder.text = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanJoinAddressPlaceholder);
        }

        string DescribeSessionState()
        {
            if (session == null)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable);

            return session.CurrentState switch
            {
                BlockiverseConnectionState.StartingHost => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStartingHost),
                BlockiverseConnectionState.Hosting => session.LastStopRequestSucceeded
                    ? BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.LanHosting,
                        DescribeHostJoinAddresses(),
                        session.Config.Port)
                    : DescribeStopSessionResult(wasActive: true),
                BlockiverseConnectionState.StartingClient => BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanJoining,
                    ResolveJoinAddress(),
                    session.Config.Port),
                BlockiverseConnectionState.ConnectedClient => BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanConnected,
                    ResolveJoinAddress(),
                    session.Config.Port),
                BlockiverseConnectionState.Disconnecting => DescribeStoppingState(),
                BlockiverseConnectionState.Disconnected => DescribeDisconnectedState(),
                BlockiverseConnectionState.Failed => DescribeFailedState(),
                _ => BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanStoppedWithDefault,
                    BlockiverseNetworkConfig.DefaultAddress),
            };
        }

        string DescribeStopSessionResult(bool wasActive)
        {
            if (session == null)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable);

            if (!session.LastStopRequestSucceeded)
            {
                return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStopFailed)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.LanStopFailedWithReason,
                        session.LastDisconnectReason);
            }

            if (session.LastStopForcedAfterPreparationFailure)
            {
                return BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanStoppingWithoutShutdownSave,
                    session.LastDisconnectReason);
            }

            return wasActive
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStopping)
                : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStopped);
        }

        string DescribeStoppingState()
        {
            if (session != null && session.LastStopForcedAfterPreparationFailure)
            {
                return BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanStoppingWithoutShutdownSave,
                    session.LastDisconnectReason);
            }

            return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStopping);
        }

        string DescribeDisconnectedState()
        {
            if (!IsShowingSessionEndedMessage)
                return DescribeUnableToReachHostState();

            string reconnectMessage =
                BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanHostDisconnected,
                    ResolveJoinAddress(),
                    session.Config.Port);

            return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                ? reconnectMessage
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanLastDisconnect,
                    reconnectMessage,
                    session.LastDisconnectReason);
        }

        string DescribeUnableToReachHostState()
        {
            string retryMessage =
                BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanUnableToReach,
                    ResolveJoinAddress(),
                    session.Config.Port);

            return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                ? retryMessage
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanLastDisconnect,
                    retryMessage,
                    session.LastDisconnectReason);
        }

        string DescribeHostJoinAddresses()
        {
            if (session == null)
                return BlockiverseNetworkConfig.DefaultAddress;

            string listenAddress = session.Config.ListenAddress;
            return BlockiverseLanAddressUtility.IsWildcardListenAddress(listenAddress)
                ? BlockiverseLanAddressUtility.DescribeLocalIPv4Addresses(BlockiverseNetworkConfig.DefaultAddress)
                : listenAddress.Trim();
        }

        string DescribeFailedState()
        {
            return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanFailed)
                : session.LastDisconnectReason;
        }

        void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = AppendAgePolicyNotice(message);
        }

        static string AppendAgePolicyNotice(string message)
        {
            BlockiverseUserAgeCategoryState ageState = BlockiverseUserAgeCategoryService.Current;
            if (BlockiversePlatformFeaturePolicy.CanUseMetaSocialFeature(ageState.Category))
                return message;

            return $"{message}\nMeta social features use fallback identity and avatar behavior for this account.";
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
            if (sessionEndedRouteRequested)
                return;

            DiscoverMenuController();
            if (menuController != null && menuController.ShowLanMultiplayerScreen())
                sessionEndedRouteRequested = true;
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
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }
    }
}

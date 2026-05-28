using Blockiverse.Networking;
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
        [SerializeField] InputField addressInput;
        [SerializeField] Text statusText;

        UnityAction hostClicked;
        UnityAction joinClicked;
        UnityAction stopClicked;
        Button registeredHostButton;
        Button registeredJoinButton;
        Button registeredStopButton;
        BlockiverseConnectionState lastDisplayedState;
        NetworkSessionMode lastDisplayedMode;
        string lastDisplayedDisconnectReason = string.Empty;

        public BlockiverseNetworkSession Session => session;
        public Text StatusText => statusText;
        public InputField AddressInput => addressInput;
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

        public void ConfigureControls(
            Button targetHostButton,
            Button targetJoinButton,
            Button targetStopButton,
            InputField targetAddressInput,
            Text targetStatusText)
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
                return;
            }

            bool started = session.StartHost();
            SetStatus(started
                ? "Starting LAN host..."
                : $"Unable to start LAN host. {DescribeSessionState()}");
            RefreshControls();
        }

        public void JoinLanSession()
        {
            if (session == null)
            {
                SetStatus("LAN session is unavailable.");
                return;
            }

            string address = ResolveJoinAddress();
            bool started = session.StartClient(address);
            SetStatus(started
                ? $"Joining LAN session at {address}:{session.Config.Port}..."
                : $"Unable to join LAN session at {address}:{session.Config.Port}. {DescribeSessionState()}");
            RefreshControls();
        }

        public void StopSession()
        {
            if (session == null)
            {
                SetStatus("LAN session is unavailable.");
                return;
            }

            bool wasActive = session.NetworkManager.IsListening || session.NetworkManager.ShutdownInProgress;
            session.StopSession();
            SetStatus(DescribeStopSessionResult(wasActive));
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
            RegisterControlCallbacks();
            ApplyDefaultAddressText();
            RefreshStatus();
        }

        void Update()
        {
            if (session == null)
                return;

            if (lastDisplayedState != session.CurrentState ||
                lastDisplayedMode != session.CurrentMode ||
                lastDisplayedDisconnectReason != session.LastDisconnectReason)
                RefreshStatus();
            else
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

        void RefreshControls()
        {
            bool canStart = session != null &&
                !session.NetworkManager.IsListening &&
                !session.NetworkManager.ShutdownInProgress;
            bool canStop = session != null &&
                (session.NetworkManager.IsListening || session.NetworkManager.ShutdownInProgress);

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

            foreach (Canvas canvas in GetComponentsInParent<Canvas>(true))
                canvas.enabled = true;

            foreach (GraphicRaycaster raycaster in GetComponentsInParent<GraphicRaycaster>(true))
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
    }
}

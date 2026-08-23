using System;
using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Blockiverse.Core;

namespace Blockiverse.UI
{
    public sealed class BlockiverseMultiplayerSessionMenu : MonoBehaviour
    {
        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] Button hostButton;
        [SerializeField] Button joinButton;
        [SerializeField] Button reconnectButton;
        [SerializeField] Button stopButton;
        [SerializeField] TMP_InputField addressInput;
        [SerializeField] TMP_Text statusText;
        [SerializeField] Image statusBadge;
        [SerializeField] BlockiverseWorldSessionController worldSessionController;
        [SerializeField] BlockiverseMenuController menuController;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseLanDiscovery discovery;
        [SerializeField] Button[] discoveryButtons = Array.Empty<Button>();
        [SerializeField] TMP_Text[] discoveryLabels = Array.Empty<TMP_Text>();
        [SerializeField] TMP_Text discoveryStatusText;
        IBlockiverseInteractionHaptics interactionHaptics;
        // One closure per slot, created once at configure time: rebuilding them on every refresh
        // would leak listeners onto the buttons.
        readonly List<UnityAction> discoveryClicked = new();
        readonly List<Button> registeredDiscoveryButtons = new();
        BlockiverseWorldSpacePanelPresenter panelPresenter;
        bool discoveryListening;

        UnityAction hostClicked;
        UnityAction joinClicked;
        UnityAction reconnectClicked;
        UnityAction stopClicked;
        Button registeredHostButton;
        Button registeredJoinButton;
        Button registeredReconnectButton;
        Button registeredStopButton;
        BlockiverseConnectionState lastDisplayedState;
        NetworkSessionMode lastDisplayedMode;
        string lastDisplayedDisconnectReason = string.Empty;
        bool lastAppliedCanStart;
        bool lastAppliedCanStop;
        bool enteredGameplayForCurrentSession;
        bool sessionEndedRouteRequested;

        public BlockiverseNetworkSession Session => session;
        public BlockiverseLanDiscovery Discovery => discovery;
        public string LastJoinAddress { get; private set; }
        public IReadOnlyList<BlockiverseDiscoveredSession> DiscoveredSessions =>
            discovery != null ? discovery.DiscoveredSessions : Array.Empty<BlockiverseDiscoveredSession>();
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
            IBlockiverseInteractionHaptics targetInteractionHaptics)
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

        public void ConfigureStatusBadge(Image badge)
        {
            statusBadge = badge;
            UpdateStatusBadge();
        }

        /// <summary>
        /// Wires the LAN discovery list. Optional: with no discovery component the menu behaves
        /// exactly as before, and manual address entry stays the way in.
        /// </summary>
        public void ConfigureDiscovery(
            BlockiverseLanDiscovery targetDiscovery,
            Button[] targetDiscoveryButtons,
            TMP_Text[] targetDiscoveryLabels,
            TMP_Text targetDiscoveryStatusText)
        {
            UnregisterDiscoveryCallbacks();

            if (discovery != null)
                discovery.DiscoveredSessionsChanged -= RefreshDiscoveryList;

            discovery = targetDiscovery;
            discoveryButtons = targetDiscoveryButtons ?? Array.Empty<Button>();
            discoveryLabels = targetDiscoveryLabels ?? Array.Empty<TMP_Text>();
            discoveryStatusText = targetDiscoveryStatusText;

            if (discovery != null)
            {
                discovery.Configure(session);
                discovery.DiscoveredSessionsChanged += RefreshDiscoveryList;
            }

            RegisterDiscoveryCallbacks();
            RefreshDiscoveryList();
        }

        void RegisterDiscoveryCallbacks()
        {
            // Clear-then-add. onClick listeners added here are runtime-only (they do not survive
            // serialization), so this runs again in Awake for buttons wired by the bootstrapper —
            // and adding without removing first is how a menu button ends up firing N times.
            UnregisterDiscoveryCallbacks();

            for (int index = 0; index < discoveryButtons.Length; index++)
            {
                Button button = discoveryButtons[index];
                if (button == null)
                {
                    discoveryClicked.Add(null);
                    registeredDiscoveryButtons.Add(null);
                    continue;
                }

                int slot = index;
                UnityAction callback = () => JoinDiscoveredSession(slot);
                button.onClick.AddListener(callback);
                discoveryClicked.Add(callback);
                registeredDiscoveryButtons.Add(button);
            }
        }

        void UnregisterDiscoveryCallbacks()
        {
            for (int index = 0; index < registeredDiscoveryButtons.Count && index < discoveryClicked.Count; index++)
            {
                Button button = registeredDiscoveryButtons[index];
                UnityAction callback = discoveryClicked[index];

                if (button != null && callback != null)
                    button.onClick.RemoveListener(callback);
            }

            registeredDiscoveryButtons.Clear();
            discoveryClicked.Clear();
        }

        /// <summary>Joins the session in a discovery slot. Ignores an empty or stale slot.</summary>
        public void JoinDiscoveredSession(int slot)
        {
            IReadOnlyList<BlockiverseDiscoveredSession> sessions = DiscoveredSessions;

            if (slot < 0 || slot >= sessions.Count)
                return;

            BlockiverseDiscoveredSession discovered = sessions[slot];

            if (session == null)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            if (!TryAdoptDiscoveredPort(discovered))
            {
                // A session started between listing and clicking; the refresh shows what
                // actually happened rather than dialling a port we could not apply.
                RefreshStatus();
                return;
            }

            // The address field is filled in as well as joined, so a failed auto-join leaves the
            // player one Join press away from retrying rather than back at a blank field.
            if (addressInput != null)
                addressInput.text = discovered.Address;

            LastJoinAddress = discovered.Address;
            JoinSessionInternal(discovered.Address);
        }

        /// <summary>
        /// Applies a discovered host's advertised game port to the session config before joining.
        /// Without this, StartClient dials the locally configured port — and that port is also
        /// signed into the approval payload, so a host on a non-default port would be listed with
        /// the right port and then refuse the join as an invalid payload.
        /// </summary>
        bool TryAdoptDiscoveredPort(BlockiverseDiscoveredSession discovered)
        {
            if (session == null)
                return false;

            if (discovered.Port == 0 || discovered.Port == session.Config.Port)
                return true;

            try
            {
                session.Configure(session.Config.WithPort(discovered.Port));
                return true;
            }
            catch (InvalidOperationException)
            {
                // Config is immutable while a session is live.
                return false;
            }
        }

        public void RefreshDiscoveryList()
        {
            IReadOnlyList<BlockiverseDiscoveredSession> sessions = DiscoveredSessions;

            for (int index = 0; index < discoveryButtons.Length; index++)
            {
                Button button = discoveryButtons[index];
                TMP_Text label = index < discoveryLabels.Length ? discoveryLabels[index] : null;
                bool hasSession = index < sessions.Count;

                if (button != null)
                {
                    if (button.gameObject.activeSelf != hasSession)
                        button.gameObject.SetActive(hasSession);

                    // A full session stays listed but unjoinable — seeing it greyed out explains
                    // more than it silently vanishing.
                    button.interactable = hasSession && sessions[index].HasRoom;
                }

                if (label == null || !hasSession)
                    continue;

                BlockiverseDiscoveredSession discovered = sessions[index];
                label.text = discovered.HasRoom
                    ? BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.LanDiscoveryEntry,
                        discovered.HostName,
                        discovered.Address,
                        discovered.Port,
                        discovered.PlayerCount,
                        discovered.MaxPlayers)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.LanDiscoveryEntryFull,
                        discovered.HostName,
                        discovered.Address,
                        discovered.Port);
            }

            if (discoveryStatusText == null)
                return;

            if (discovery == null)
            {
                discoveryStatusText.text = BlockiverseLocalization.Text(
                    BlockiverseLocalization.Keys.LanDiscoveryUnavailable);
                return;
            }

            if (sessions.Count > 0)
            {
                discoveryStatusText.text = string.Empty;
                return;
            }

            discoveryStatusText.text = BlockiverseLocalization.Text(
                discovery.IsListening
                    ? BlockiverseLocalization.Keys.LanDiscoverySearching
                    : BlockiverseLocalization.Keys.LanDiscoveryNoneFound);
        }

        public void ConfigureControls(
            Button targetHostButton,
            Button targetJoinButton,
            Button targetReconnectButton,
            Button targetStopButton,
            TMP_InputField targetAddressInput,
            TMP_Text targetStatusText)
        {
            hostButton = targetHostButton;
            joinButton = targetJoinButton;
            reconnectButton = targetReconnectButton;
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
            LastJoinAddress = address;
            JoinSessionInternal(address);
        }

        public void ReconnectLanSession()
        {
            if (session == null)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            if (string.IsNullOrWhiteSpace(LastJoinAddress))
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStoppedWithDefault));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            JoinSessionInternal(LastJoinAddress);
        }

        void JoinSessionInternal(string address)
        {
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
            DiscoverDiscovery();
            RegisterControlCallbacks();
            RegisterDiscoveryCallbacks();
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
            RefreshDiscoveryListening();

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

        void OnEnable()
        {
            DiscoverDiscovery();

            if (discovery == null)
                return;

            discovery.Configure(session);
            discovery.DiscoveredSessionsChanged -= RefreshDiscoveryList;
            discovery.DiscoveredSessionsChanged += RefreshDiscoveryList;
            RefreshDiscoveryList();
        }

        void OnDisable()
        {
            // Backstop only. The panel is normally hidden by disabling its Canvas, which does not
            // deactivate this GameObject, so browsing is driven by RefreshDiscoveryListening from
            // Update instead — see the note there.
            StopDiscoveryListening();

            if (discovery != null)
                discovery.DiscoveredSessionsChanged -= RefreshDiscoveryList;
        }

        /// <summary>
        /// Starts and stops browsing with the panel actually being on screen.
        ///
        /// This deliberately does not use OnEnable/OnDisable: BlockiverseWorldSpacePanelPresenter
        /// hides a panel by disabling its Canvas and leaves the GameObject active, so those
        /// callbacks fire once at scene load and never again. Keying off them left the UDP browse
        /// socket and its receive loop running for the whole session — on a headset, for a player
        /// who is out building.
        /// </summary>
        void RefreshDiscoveryListening()
        {
            if (discovery == null)
                return;

            bool panelVisible = ResolvePresenter() is { } presenter ? presenter.IsVisible : isActiveAndEnabled;

            if (panelVisible == discoveryListening)
                return;

            if (panelVisible)
                StartDiscoveryListening();
            else
                StopDiscoveryListening();
        }

        void StartDiscoveryListening()
        {
            if (discovery == null)
                return;

            discovery.Configure(session);
            // Opening the panel is the natural retry point for a socket that failed to bind
            // earlier — the failure latches so it cannot spin, but it should not be permanent.
            discovery.ResetSocketFailure();
            discovery.StartListening();
            discoveryListening = true;
            RefreshDiscoveryList();
        }

        void StopDiscoveryListening()
        {
            if (discovery != null && discoveryListening)
                discovery.StopListening();

            discoveryListening = false;
        }

        BlockiverseWorldSpacePanelPresenter ResolvePresenter()
        {
            if (panelPresenter == null)
                panelPresenter = GetComponent<BlockiverseWorldSpacePanelPresenter>();

            return panelPresenter;
        }

        void DiscoverDiscovery()
        {
            if (discovery != null)
                return;

            discovery = FindFirstObjectByType<BlockiverseLanDiscovery>(FindObjectsInactive.Include);
        }

        void OnDestroy()
        {
            StopDiscoveryListening();
            UnregisterControlCallbacks();
            UnregisterDiscoveryCallbacks();

            if (discovery != null)
                discovery.DiscoveredSessionsChanged -= RefreshDiscoveryList;
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
                    DescribeDisconnectReason(session.LastDisconnectReason));
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
                    DescribeDisconnectReason(session.LastDisconnectReason));
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
            if (string.IsNullOrWhiteSpace(session.LastDisconnectReason))
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanFailed);

            return DescribeDisconnectReason(session.LastDisconnectReason);
        }

        /// <summary>
        /// Turns a Netcode disconnect reason into player-facing text. A refused join arrives as a
        /// <see cref="BlockiverseJoinRejectionReason"/> name, which says something actionable
        /// ("both headsets need the same build") rather than an enum the player cannot act on.
        /// Anything else — a transport error, a host-side message — passes through unchanged.
        /// </summary>
        public static string DescribeDisconnectReason(string disconnectReason)
        {
            if (string.IsNullOrWhiteSpace(disconnectReason))
                return string.Empty;

            if (!Enum.TryParse(disconnectReason.Trim(), out BlockiverseJoinRejectionReason rejectionReason))
                return disconnectReason;

            return rejectionReason switch
            {
                BlockiverseJoinRejectionReason.ProtocolMismatch =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanRejectedProtocolMismatch),
                BlockiverseJoinRejectionReason.GameVersionMismatch =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanRejectedGameVersionMismatch),
                BlockiverseJoinRejectionReason.BlockRegistryMismatch =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanRejectedBlockRegistryMismatch),
                BlockiverseJoinRejectionReason.ItemRegistryMismatch =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanRejectedItemRegistryMismatch),
                BlockiverseJoinRejectionReason.RecipeRegistryMismatch =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanRejectedRecipeRegistryMismatch),
                BlockiverseJoinRejectionReason.UnsupportedWorldVersion =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanRejectedUnsupportedWorldVersion),
                BlockiverseJoinRejectionReason.SessionFull =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanRejectedSessionFull),
                BlockiverseJoinRejectionReason.InvalidJoinPayload =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanRejectedInvalidJoinPayload),
                _ => disconnectReason,
            };
        }

        void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = AppendAgePolicyNotice(message);

            UpdateStatusBadge();
        }

        // Connection-state colors for the LAN status badge. Green = live session, amber =
        // transitioning, red = failed/disconnected, grey = idle/unavailable. The badge gives a
        // glanceable indicator next to the verbose status text.
        static readonly Color BadgeConnectedColor = new(0.30f, 0.78f, 0.36f, 1.0f);
        static readonly Color BadgeTransitionColor = new(0.95f, 0.74f, 0.24f, 1.0f);
        static readonly Color BadgeFailedColor = new(0.90f, 0.32f, 0.28f, 1.0f);
        static readonly Color BadgeIdleColor = new(0.55f, 0.58f, 0.62f, 1.0f);

        void UpdateStatusBadge()
        {
            if (statusBadge == null)
                return;

            statusBadge.color = ResolveStatusBadgeColor();
        }

        Color ResolveStatusBadgeColor()
        {
            if (session == null)
                return BadgeIdleColor;

            switch (session.CurrentState)
            {
                case BlockiverseConnectionState.Hosting:
                case BlockiverseConnectionState.ConnectedClient:
                    return BadgeConnectedColor;
                case BlockiverseConnectionState.StartingHost:
                case BlockiverseConnectionState.StartingClient:
                case BlockiverseConnectionState.Disconnecting:
                    return BadgeTransitionColor;
                case BlockiverseConnectionState.Failed:
                    return BadgeFailedColor;
                case BlockiverseConnectionState.Disconnected:
                    return IsShowingSessionEndedMessage ? BadgeFailedColor : BadgeIdleColor;
                default:
                    return BadgeIdleColor;
            }
        }

        static string AppendAgePolicyNotice(string message)
        {
            if (BlockiverseMetaSocialPolicy.CanUseMetaSocialFeature)
                return message;

            return BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.LanAgePolicyNotice, message);
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

            if (reconnectButton != null)
                reconnectButton.interactable = canStart && !string.IsNullOrWhiteSpace(LastJoinAddress);

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
            
            if (enteredGameplayForCurrentSession)
            {
                if (menuController != null)
                {
                    menuController.ShowTitleScreen();
                    sessionEndedRouteRequested = true;
                    if (worldSessionController != null)
                        worldSessionController.TrySuspendActiveSessionForMultiplayer(out _);
                }
            }
            else if (menuController != null && menuController.ShowLanMultiplayerScreen())
            {
                sessionEndedRouteRequested = true;
            }

            RestoreInteractableMenuSurface();
        }

        void RestoreInteractableMenuSurface()
        {
            gameObject.SetActive(true);

            foreach (Canvas canvas in GetComponentsInParent<Canvas>(includeInactive: true))
            {
                canvas.gameObject.SetActive(true);
                canvas.enabled = true;
            }

            foreach (GraphicRaycaster raycaster in GetComponentsInParent<GraphicRaycaster>(includeInactive: true))
                raycaster.enabled = true;
        }

        void RegisterControlCallbacks()
        {
            hostClicked ??= StartLanHost;
            joinClicked ??= JoinLanSession;
            reconnectClicked ??= ReconnectLanSession;
            stopClicked ??= StopSession;

            RegisterButtonCallback(hostButton, ref registeredHostButton, hostClicked);
            RegisterButtonCallback(joinButton, ref registeredJoinButton, joinClicked);
            RegisterButtonCallback(reconnectButton, ref registeredReconnectButton, reconnectClicked);
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

            if (registeredReconnectButton != null)
                registeredReconnectButton.onClick.RemoveListener(reconnectClicked);

            if (registeredStopButton != null)
                registeredStopButton.onClick.RemoveListener(stopClicked);

            registeredHostButton = null;
            registeredJoinButton = null;
            registeredReconnectButton = null;
            registeredStopButton = null;
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }
    }
}

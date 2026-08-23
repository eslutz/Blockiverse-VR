using System;
using System.Collections.Generic;
using System.Globalization;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of BlockiverseMultiplayerSessionMenu (matrix row 14). The uGUI component
    // stays in the scene as the development fallback; this controller mirrors its flow against
    // the same dependencies (BlockiverseNetworkSession, BlockiverseLanDiscovery,
    // BlockiverseWorldSessionController, the bookmark store and the auth gate) and the same
    // ui.status.lan.* copy, resolved through UiText for byte-parity with the uGUI shim.
    //
    // The three hazards this port exists to not lose (matrix §4 items 4, 7, 8):
    //  - Discovery browsing is keyed on ROUTED VISIBILITY (OnShown/OnHidden), never
    //    OnEnable/OnDisable — a UI Toolkit screen hides by collapsing its root, so Unity
    //    lifecycle callbacks fire once at scene load; keying on them left a UDP browse socket
    //    open for a whole headset session in the uGUI era.
    //  - A discovered host's advertised port is adopted into the session config BEFORE joining,
    //    and the join dials the explicit host:port. The port is signed into the approval
    //    payload, so skipping either half makes the host refuse the join it just advertised.
    //  - Discovery-slot click closures are registered with exact-subscription bookkeeping
    //    (clear-then-add against remembered buttons); rebuilding them per refresh makes a slot
    //    fire N times per click with no exception and no warning.
    [UiToolkitScreen(
        MenuActions.LanMultiplayerScreen,
        "Assets/Blockiverse/UI/Documents/LanMultiplayerScreen.uxml",
        1100,
        990,
        UiToolkitPlacementProfile.Menu)]
    public sealed class LanMultiplayerScreenController : UiToolkitScreenController
    {
        public const int DiscoverySlotCount = 4;

        // Saved-server rows shown at once. The store keeps up to 16 most-recent-first; four
        // rows cover the recent rotation without growing the panel, and an older bookmark
        // resurfaces the moment it is rejoined by address.
        public const int BookmarkSlotCount = 4;

        // Table keys shared with the uGUI panel — the copy contract. Values must match
        // BlockiverseLocalization.Keys verbatim; the keys are wire-stable per CLAUDE.md, and
        // duplicating the strings here keeps this assembly's screens off the uGUI shim.
        static class Keys
        {
            public const string Unavailable = "ui.status.lan.unavailable";
            public const string StartingHost = "ui.status.lan.starting_host";
            public const string StartHostFailed = "ui.status.lan.start_host_failed";
            public const string Joining = "ui.status.lan.joining";
            public const string JoinFailed = "ui.status.lan.join_failed";
            public const string Hosting = "ui.status.lan.hosting";
            public const string Connected = "ui.status.lan.connected";
            public const string Stopping = "ui.status.lan.stopping";
            public const string Stopped = "ui.status.lan.stopped";
            public const string StoppedWithDefault = "ui.status.lan.stopped_with_default";
            public const string StopFailed = "ui.status.lan.stop_failed";
            public const string StopFailedWithReason = "ui.status.lan.stop_failed_with_reason";
            public const string StoppingWithoutShutdownSave = "ui.status.lan.stopping_without_shutdown_save";
            public const string HostDisconnected = "ui.status.lan.host_disconnected";
            public const string UnableToReach = "ui.status.lan.unable_to_reach";
            public const string LastDisconnect = "ui.status.lan.last_disconnect";
            public const string AgePolicyNotice = "ui.status.lan.age_policy_notice";
            public const string Failed = "ui.status.lan.failed";
            public const string RejectedProtocolMismatch = "ui.status.lan.rejected.protocol_mismatch";
            public const string RejectedGameVersionMismatch = "ui.status.lan.rejected.game_version_mismatch";
            public const string RejectedBlockRegistryMismatch = "ui.status.lan.rejected.block_registry_mismatch";
            public const string RejectedItemRegistryMismatch = "ui.status.lan.rejected.item_registry_mismatch";
            public const string RejectedRecipeRegistryMismatch = "ui.status.lan.rejected.recipe_registry_mismatch";
            public const string RejectedUnsupportedWorldVersion = "ui.status.lan.rejected.unsupported_world_version";
            public const string RejectedSessionFull = "ui.status.lan.rejected.session_full";
            public const string RejectedInvalidJoinPayload = "ui.status.lan.rejected.invalid_join_payload";
            public const string DiscoverySearching = "ui.status.lan.discovery.searching";
            public const string DiscoveryNoneFound = "ui.status.lan.discovery.none_found";
            public const string DiscoveryUnavailable = "ui.status.lan.discovery.unavailable";
            public const string SavedServers = "ui.generated.lan.saved_servers";
            public const string DiscoveryEntry = "ui.generated.lan.discovery_entry";
            public const string DiscoveryEntryFull = "ui.generated.lan.discovery_entry_full";
            public const string JoinAddressPlaceholder = "ui.generated.lan.join_address_placeholder";
            public const string JoinSecretPlaceholder = "ui.generated.lan.join_secret_placeholder";
            public const string SuspendSinglePlayerFailed = "ui.status.world.suspend_single_player_failed";

            // Requested new entries (no existing key carries these uGUI-hardcoded labels).
            // Until they land in the table UiText.Get falls back to the key string.
            public const string HostAction = "ui.action.lan.host";
            public const string JoinAction = "ui.action.lan.join";
            public const string StopAction = "ui.action.lan.stop";
            public const string EncryptionToggle = "ui.generated.lan.encryption_toggle";
        }

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] BlockiverseWorldSessionController worldSessionController;
        [SerializeField] BlockiverseLanDiscovery discovery;
        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        TextField addressField;
        TextField secretField;
        Toggle encryptionToggle;
        Button hostButton;
        Button joinButton;
        Button reconnectButton;
        Button stopButton;
        Button closeButton;
        Label statusLabel;
        Label discoveryStatusLabel;
        readonly List<Button> discoveryButtons = new();

        // One closure per slot, registered clear-then-add against the exact buttons it was added
        // to: rebuilding closures on every refresh leaks handlers onto the buttons.
        readonly List<Action> discoveryClicked = new();
        readonly List<Button> registeredDiscoveryButtons = new();

        // Saved servers (the bookmark store), same slot mechanics as discovery.
        Label bookmarkHeadingLabel;
        readonly List<Button> bookmarkButtons = new();
        readonly List<Action> bookmarkClicked = new();
        readonly List<Button> registeredBookmarkButtons = new();

        bool discoveryListening;
        float nextSessionSearchTime;
        BlockiverseConnectionState lastDisplayedState;
        NetworkSessionMode lastDisplayedMode;
        string lastDisplayedDisconnectReason = string.Empty;
        bool lastAppliedCanStart;
        bool lastAppliedCanStop;
        bool enteredGameplayForCurrentSession;
        bool sessionEndedRouteRequested;

        public override string ScreenId => MenuActions.LanMultiplayerScreen;

        public BlockiverseNetworkSession Session => session;
        public BlockiverseLanDiscovery Discovery => discovery;
        public string LastJoinAddress { get; private set; }
        public IReadOnlyList<BlockiverseDiscoveredSession> DiscoveredSessions =>
            discovery != null ? discovery.DiscoveredSessions : Array.Empty<BlockiverseDiscoveredSession>();
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

        // The slot buttons come from the document, so unlike the uGUI Configure this only swaps
        // the discovery component. Configuring while routed-visible starts browsing immediately —
        // the polled visibility check that covered that case in the uGUI menu no longer exists.
        public void ConfigureDiscovery(BlockiverseLanDiscovery targetDiscovery)
        {
            if (discovery != null)
            {
                if (discoveryListening)
                    discovery.StopListening();

                discovery.DiscoveredSessionsChanged -= RefreshDiscoveryList;
            }

            discoveryListening = false;
            discovery = targetDiscovery;

            if (discovery != null)
            {
                discovery.Configure(session);
                discovery.DiscoveredSessionsChanged -= RefreshDiscoveryList;
                discovery.DiscoveredSessionsChanged += RefreshDiscoveryList;
            }

            if (IsVisible)
                StartDiscoveryListening();

            RefreshDiscoveryList();
        }

        protected override void OnAwake()
        {
            DiscoverSession();
            DiscoverWorldSessionController();
            DiscoverDiscovery();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            addressField = Require<TextField>(root, "bv-lan-address", ref allFound);
            secretField = Require<TextField>(root, "bv-lan-secret", ref allFound);
            encryptionToggle = Require<Toggle>(root, "bv-lan-encryption", ref allFound);
            hostButton = Require<Button>(root, "bv-lan-host", ref allFound);
            joinButton = Require<Button>(root, "bv-lan-join", ref allFound);
            reconnectButton = Require<Button>(root, "bv-lan-reconnect", ref allFound);
            stopButton = Require<Button>(root, "bv-lan-stop", ref allFound);
            closeButton = Require<Button>(root, "bv-lan-close", ref allFound);
            statusLabel = Require<Label>(root, "bv-lan-status", ref allFound);
            discoveryStatusLabel = Require<Label>(root, "bv-lan-discovery-status", ref allFound);

            discoveryButtons.Clear();
            for (int slot = 0; slot < DiscoverySlotCount; slot++)
                discoveryButtons.Add(Require<Button>(root, $"bv-lan-discovery-slot-{slot + 1}", ref allFound));

            bookmarkHeadingLabel = Require<Label>(root, "bv-lan-bookmark-heading", ref allFound);
            bookmarkButtons.Clear();
            for (int slot = 0; slot < BookmarkSlotCount; slot++)
                bookmarkButtons.Add(Require<Button>(root, $"bv-lan-bookmark-slot-{slot + 1}", ref allFound));

            ApplyRuntimeTexts();
            RefreshStatus();
            RefreshDiscoveryList();
            RefreshBookmarkList();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            if (hostButton != null)
                hostButton.clicked += StartLanHost;

            if (joinButton != null)
                joinButton.clicked += JoinLanSession;

            if (reconnectButton != null)
                reconnectButton.clicked += ReconnectLanSession;

            if (stopButton != null)
                stopButton.clicked += StopSession;

            if (closeButton != null)
                closeButton.clicked += OnCloseClicked;

            RegisterDiscoveryCallbacks();
            RegisterBookmarkCallbacks();

            // Dynamic text set through UiText goes stale on a live language switch; static
            // labels update through their native bindings and need nothing here.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (hostButton != null)
                hostButton.clicked -= StartLanHost;

            if (joinButton != null)
                joinButton.clicked -= JoinLanSession;

            if (reconnectButton != null)
                reconnectButton.clicked -= ReconnectLanSession;

            if (stopButton != null)
                stopButton.clicked -= StopSession;

            if (closeButton != null)
                closeButton.clicked -= OnCloseClicked;

            UnregisterDiscoveryCallbacks();
            UnregisterBookmarkCallbacks();

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            addressField = null;
            secretField = null;
            encryptionToggle = null;
            hostButton = null;
            joinButton = null;
            reconnectButton = null;
            stopButton = null;
            closeButton = null;
            statusLabel = null;
            discoveryStatusLabel = null;
            discoveryButtons.Clear();
            bookmarkHeadingLabel = null;
            bookmarkButtons.Clear();
        }

        // Routed visibility is the browse lifecycle (matrix §4 item 7). OnEnable/OnDisable fire
        // once at scene load because screens hide by collapsing their root, so keying the socket
        // on them would leave a UDP receive loop running for the whole session.
        protected override void OnShown()
        {
            // The store may have gained entries since the screen last showed, and it exists
            // independently of discovery (its whole point is servers discovery cannot find).
            RefreshBookmarkList();

            DiscoverDiscovery();

            if (discovery == null)
                return;

            discovery.Configure(session);
            discovery.DiscoveredSessionsChanged -= RefreshDiscoveryList;
            discovery.DiscoveredSessionsChanged += RefreshDiscoveryList;
            StartDiscoveryListening();
        }

        protected override void OnHidden()
        {
            StopDiscoveryListening();
        }

        void OnDestroy()
        {
            StopDiscoveryListening();

            if (discovery != null)
                discovery.DiscoveredSessionsChanged -= RefreshDiscoveryList;
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
            // element state every frame.
            ComputeControlState(out bool canStart, out bool canStop);
            if (canStart != lastAppliedCanStart || canStop != lastAppliedCanStop)
                RefreshControls();
        }

        // ── Host / join / stop flow (ported verbatim from the uGUI menu) ─────────────

        public void StartLanHost()
        {
            if (session == null)
            {
                SetStatus(UiText.Get(Keys.Unavailable));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            if (!TrySuspendSinglePlayerSessionForMultiplayer())
                return;

            bool started = session.StartHost();
            SetStatus(started
                ? UiText.Get(Keys.StartingHost)
                : UiText.Format(Keys.StartHostFailed, DescribeSessionState()));
            PlayFeedback(started ? BlockiverseAudioCue.UiConfirm : BlockiverseAudioCue.UiCancel);
            RefreshStatus();
        }

        public void JoinLanSession()
        {
            if (session == null)
            {
                SetStatus(UiText.Get(Keys.Unavailable));
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
                SetStatus(UiText.Get(Keys.Unavailable));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            if (string.IsNullOrWhiteSpace(LastJoinAddress))
            {
                SetStatus(UiText.Get(Keys.StoppedWithDefault));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            JoinSessionInternal(LastJoinAddress);
        }

        public void StopSession()
        {
            if (session == null)
            {
                SetStatus(UiText.Get(Keys.Unavailable));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            bool wasActive = session.NetworkManager.IsListening || session.NetworkManager.ShutdownInProgress;
            session.StopSession();
            SetStatus(DescribeStopSessionResult(wasActive));
            PlayFeedback(BlockiverseAudioCue.UiCancel);
            RefreshControls();
        }

        void JoinSessionInternal(string address)
        {
            if (!TrySuspendSinglePlayerSessionForMultiplayer())
                return;

            // The field carries "host" or "host:port". A dedicated server on a non-default port is
            // unreachable otherwise, and one field is one pass with the system keyboard rather
            // than two, which matters in a headset.
            if (!BlockiverseServerAddress.TryParse(address, out BlockiverseServerAddress parsed))
            {
                SetStatus(UiText.Format(
                    Keys.JoinFailed, address, PortText(session.Config.Port), DescribeSessionState()));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                RefreshStatus();
                return;
            }

            // Applied before StartClient: the transport reads the port from the session config.
            // Always update to parsed.Port, INCLUDING the default inferred for a host-only address.
            // After joining host-a:7788, joining host-b without a port must reset to 7777, not stay on 7788.
            if (parsed.Port != session.Config.Port)
                session.Configure(session.Config.WithPort(parsed.Port));

            address = parsed.Host;
            ApplySecurityForJoin(parsed);
            bool started = session.StartClient(address);
            SetStatus(started
                ? UiText.Format(Keys.Joining, address, PortText(session.Config.Port))
                : UiText.Format(
                    Keys.JoinFailed,
                    address,
                    PortText(session.Config.Port),
                    DescribeSessionState()));
            // Remembered only on a successful start, so a typo never enters the list. The typed
            // secret is stored with the bookmark (null when the field is empty, which preserves a
            // previously stored one rather than wiping it on a plain re-join).
            if (started)
            {
                BlockiverseServerBookmarkStore.Remember(
                    parsed.ToString(),
                    secret: string.IsNullOrEmpty(TypedSecretOrNull()) ? null : TypedSecretOrNull(),
                    useTls: encryptionToggle != null ? encryptionToggle.value : (bool?)null);
                // The remembered list changed order (or gained a row); the saved-servers rows
                // must reflect it before the player next looks at them.
                RefreshBookmarkList();
            }

            PlayFeedback(started ? BlockiverseAudioCue.UiConfirm : BlockiverseAudioCue.UiCancel);
            RefreshStatus();
        }

        // Servers this player has joined before, most recent first. LAN discovery cannot find a
        // server across the internet, which is the case a dedicated server exists for.
        public static IReadOnlyList<BlockiverseServerBookmark> RememberedServers() =>
            BlockiverseServerBookmarkStore.Load();

        public void JoinRememberedServer(int index)
        {
            IReadOnlyList<BlockiverseServerBookmark> servers = BlockiverseServerBookmarkStore.Load();
            if (index < 0 || index >= servers.Count || servers[index] == null)
            {
                SetStatus(UiText.Get(Keys.Unavailable));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return;
            }

            string address = servers[index].address;
            if (addressField != null)
                addressField.SetValueWithoutNotify(address);
            // Seed the security controls from the bookmark so what is about to be used is what
            // the player sees, and a re-join with a changed password is one edit away.
            if (secretField != null)
                secretField.SetValueWithoutNotify(servers[index].secret ?? string.Empty);
            if (encryptionToggle != null)
                encryptionToggle.SetValueWithoutNotify(servers[index].useTls);

            LastJoinAddress = address;
            JoinSessionInternal(address);
        }

        string TypedSecretOrNull()
        {
            string typed = secretField != null ? secretField.value : null;
            return string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
        }

        // Sets up the join-secret answer and transport security for one join. The typed secret
        // wins over the bookmarked one so a changed server password is a matter of retyping it,
        // and both fall through to empty, which leaves the challenge unanswered and the server
        // free to say "secret required". TLS prefers the toggle, falling back to the bookmark:
        // pinned CA when the operator supplied one, the shipped public roots otherwise.
        void ApplySecurityForJoin(BlockiverseServerAddress parsed)
        {
            BlockiverseServerBookmark bookmark = BlockiverseServerBookmarkStore.Find(parsed.ToString());

            var authGate = session.GetComponent<BlockiverseServerAuthGate>();
            if (authGate != null)
                authGate.ConfigureClientSecret(TypedSecretOrNull() ?? bookmark?.secret ?? string.Empty);

            bool useTls = encryptionToggle != null
                ? encryptionToggle.value
                : bookmark != null && bookmark.useTls;
            string serverName = !string.IsNullOrWhiteSpace(bookmark?.tlsServerName)
                ? bookmark.tlsServerName.Trim()
                : parsed.Host;
            string caBundle = !string.IsNullOrWhiteSpace(bookmark?.tlsPinnedCaPem)
                ? bookmark.tlsPinnedCaPem
                : BlockiverseTrustedRoots.CaBundlePem;

            try
            {
                session.ConfigureClientTransportSecurity(useTls, serverName, caBundle);
            }
            catch (InvalidOperationException)
            {
                // A live session's transport is immutable; StartClient will fail on its own
                // terms and the status text explains it.
            }
        }

        public string ResolveJoinAddress()
        {
            if (addressField == null || string.IsNullOrWhiteSpace(addressField.value))
                return BlockiverseNetworkConfig.DefaultAddress;

            return addressField.value.Trim();
        }

        // ── Discovery (matrix §4 items 4 and 8) ──────────────────────────────────────

        /// <summary>Joins the session in a discovery slot. Ignores an empty or stale slot.</summary>
        public void JoinDiscoveredSession(int slot)
        {
            IReadOnlyList<BlockiverseDiscoveredSession> sessions = DiscoveredSessions;

            if (slot < 0 || slot >= sessions.Count)
                return;

            BlockiverseDiscoveredSession discovered = sessions[slot];

            if (session == null)
            {
                SetStatus(UiText.Get(Keys.Unavailable));
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

            // Joined with the EXPLICIT host:port, not the bare address: a bare address means
            // "default port" to JoinSessionInternal, which would immediately undo the port this
            // method just adopted from the beacon. The address field is filled in as well, so a
            // failed auto-join leaves the player one Join press away from retrying rather than
            // back at a blank field.
            string joinTarget = discovered.Port != 0
                ? $"{discovered.Address}:{PortText(discovered.Port)}"
                : discovered.Address;
            if (addressField != null)
                addressField.SetValueWithoutNotify(joinTarget);

            LastJoinAddress = joinTarget;
            JoinSessionInternal(joinTarget);
        }

        // Applies a discovered host's advertised game port to the session config before joining.
        // Without this, StartClient dials the locally configured port — and that port is also
        // signed into the approval payload, so a host on a non-default port would be listed with
        // the right port and then refuse the join as an invalid payload.
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

            for (int index = 0; index < discoveryButtons.Count; index++)
            {
                Button button = discoveryButtons[index];
                if (button == null)
                    continue;

                bool hasSession = index < sessions.Count;
                button.style.display = hasSession ? DisplayStyle.Flex : DisplayStyle.None;
                // A full session stays listed but unjoinable — seeing it greyed out explains
                // more than it silently vanishing.
                button.SetEnabled(hasSession && sessions[index].HasRoom);

                if (!hasSession)
                    continue;

                BlockiverseDiscoveredSession discovered = sessions[index];
                button.text = discovered.HasRoom
                    ? UiText.Format(
                        Keys.DiscoveryEntry,
                        discovered.HostName,
                        discovered.Address,
                        PortText(discovered.Port),
                        discovered.PlayerCount,
                        discovered.MaxPlayers)
                    : UiText.Format(
                        Keys.DiscoveryEntryFull,
                        discovered.HostName,
                        discovered.Address,
                        PortText(discovered.Port));
            }

            if (discoveryStatusLabel == null)
                return;

            if (discovery == null)
            {
                discoveryStatusLabel.text = UiText.Get(Keys.DiscoveryUnavailable);
                return;
            }

            if (sessions.Count > 0)
            {
                discoveryStatusLabel.text = string.Empty;
                return;
            }

            discoveryStatusLabel.text = UiText.Get(discovery.IsListening
                ? Keys.DiscoverySearching
                : Keys.DiscoveryNoneFound);
        }

        // The bookmark menu (voxel_survival_menus: "rejoining is one tap"). Rows mirror the
        // store's most-recent-first order, so row index == store index and a click routes
        // straight through JoinRememberedServer. Rows render identifiers (nickname/address),
        // which are pre-stringified invariant and deliberately unlocalized.
        public void RefreshBookmarkList()
        {
            IReadOnlyList<BlockiverseServerBookmark> servers = BlockiverseServerBookmarkStore.Load();
            ComputeControlState(out bool canStart, out _);
            int visibleCount = 0;

            for (int index = 0; index < bookmarkButtons.Count; index++)
            {
                Button button = bookmarkButtons[index];
                if (button == null)
                    continue;

                BlockiverseServerBookmark bookmark = index < servers.Count ? servers[index] : null;
                bool hasBookmark = bookmark != null && !string.IsNullOrWhiteSpace(bookmark.address);
                button.style.display = hasBookmark ? DisplayStyle.Flex : DisplayStyle.None;
                button.SetEnabled(hasBookmark && canStart);

                if (!hasBookmark)
                    continue;

                visibleCount++;
                // "Nickname — address" when the operator named the server; the bare address
                // otherwise (Remember defaults the nickname to the address).
                button.text = !string.IsNullOrWhiteSpace(bookmark.nickname) &&
                    !string.Equals(bookmark.nickname, bookmark.address, StringComparison.OrdinalIgnoreCase)
                    ? bookmark.nickname + " — " + bookmark.address
                    : bookmark.address;
            }

            if (bookmarkHeadingLabel != null)
                bookmarkHeadingLabel.style.display = visibleCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RegisterBookmarkCallbacks()
        {
            UnregisterBookmarkCallbacks();

            for (int index = 0; index < bookmarkButtons.Count; index++)
            {
                Button button = bookmarkButtons[index];
                if (button == null)
                {
                    bookmarkClicked.Add(null);
                    registeredBookmarkButtons.Add(null);
                    continue;
                }

                int slot = index;
                Action callback = () => JoinRememberedServer(slot);
                button.clicked += callback;
                bookmarkClicked.Add(callback);
                registeredBookmarkButtons.Add(button);
            }
        }

        void UnregisterBookmarkCallbacks()
        {
            for (int index = 0; index < registeredBookmarkButtons.Count && index < bookmarkClicked.Count; index++)
            {
                Button button = registeredBookmarkButtons[index];
                Action callback = bookmarkClicked[index];

                if (button != null && callback != null)
                    button.clicked -= callback;
            }

            registeredBookmarkButtons.Clear();
            bookmarkClicked.Clear();
        }

        void RegisterDiscoveryCallbacks()
        {
            // Clear-then-add: adding without removing first is how a menu button ends up firing
            // N times per click.
            UnregisterDiscoveryCallbacks();

            for (int index = 0; index < discoveryButtons.Count; index++)
            {
                Button button = discoveryButtons[index];
                if (button == null)
                {
                    discoveryClicked.Add(null);
                    registeredDiscoveryButtons.Add(null);
                    continue;
                }

                int slot = index;
                Action callback = () => JoinDiscoveredSession(slot);
                button.clicked += callback;
                discoveryClicked.Add(callback);
                registeredDiscoveryButtons.Add(button);
            }
        }

        void UnregisterDiscoveryCallbacks()
        {
            for (int index = 0; index < registeredDiscoveryButtons.Count && index < discoveryClicked.Count; index++)
            {
                Button button = registeredDiscoveryButtons[index];
                Action callback = discoveryClicked[index];

                if (button != null && callback != null)
                    button.clicked -= callback;
            }

            registeredDiscoveryButtons.Clear();
            discoveryClicked.Clear();
        }

        void StartDiscoveryListening()
        {
            if (discovery == null || discoveryListening)
                return;

            discovery.Configure(session);
            // Opening the screen is the natural retry point for a socket that failed to bind
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

        // ── Status copy (byte-parity with the uGUI menu via the shared table entries) ─

        public void RefreshStatus()
        {
            ApplyPlaceholders();

            if (session == null)
            {
                enteredGameplayForCurrentSession = false;
                sessionEndedRouteRequested = false;
                SetStatus(UiText.Get(Keys.Unavailable));
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

        string DescribeSessionState()
        {
            if (session == null)
                return UiText.Get(Keys.Unavailable);

            return session.CurrentState switch
            {
                BlockiverseConnectionState.StartingHost => UiText.Get(Keys.StartingHost),
                BlockiverseConnectionState.Hosting => session.LastStopRequestSucceeded
                    ? UiText.Format(
                        Keys.Hosting,
                        DescribeHostJoinAddresses(),
                        PortText(session.Config.Port))
                    : DescribeStopSessionResult(wasActive: true),
                BlockiverseConnectionState.StartingClient => UiText.Format(
                    Keys.Joining,
                    ResolveJoinAddress(),
                    PortText(session.Config.Port)),
                BlockiverseConnectionState.ConnectedClient => UiText.Format(
                    Keys.Connected,
                    ResolveJoinAddress(),
                    PortText(session.Config.Port)),
                BlockiverseConnectionState.Disconnecting => DescribeStoppingState(),
                BlockiverseConnectionState.Disconnected => DescribeDisconnectedState(),
                BlockiverseConnectionState.Failed => DescribeFailedState(),
                _ => UiText.Format(
                    Keys.StoppedWithDefault,
                    BlockiverseNetworkConfig.DefaultAddress),
            };
        }

        string DescribeStopSessionResult(bool wasActive)
        {
            if (session == null)
                return UiText.Get(Keys.Unavailable);

            if (!session.LastStopRequestSucceeded)
            {
                return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                    ? UiText.Get(Keys.StopFailed)
                    : UiText.Format(Keys.StopFailedWithReason, session.LastDisconnectReason);
            }

            if (session.LastStopForcedAfterPreparationFailure)
                return UiText.Format(Keys.StoppingWithoutShutdownSave, session.LastDisconnectReason);

            return wasActive
                ? UiText.Get(Keys.Stopping)
                : UiText.Get(Keys.Stopped);
        }

        string DescribeStoppingState()
        {
            if (session != null && session.LastStopForcedAfterPreparationFailure)
                return UiText.Format(Keys.StoppingWithoutShutdownSave, session.LastDisconnectReason);

            return UiText.Get(Keys.Stopping);
        }

        string DescribeDisconnectedState()
        {
            if (!IsShowingSessionEndedMessage)
                return DescribeUnableToReachHostState();

            string reconnectMessage = UiText.Format(
                Keys.HostDisconnected,
                ResolveJoinAddress(),
                PortText(session.Config.Port));

            return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                ? reconnectMessage
                : UiText.Format(
                    Keys.LastDisconnect,
                    reconnectMessage,
                    DescribeDisconnectReason(session.LastDisconnectReason));
        }

        string DescribeUnableToReachHostState()
        {
            string retryMessage = UiText.Format(
                Keys.UnableToReach,
                ResolveJoinAddress(),
                PortText(session.Config.Port));

            return string.IsNullOrWhiteSpace(session.LastDisconnectReason)
                ? retryMessage
                : UiText.Format(
                    Keys.LastDisconnect,
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
                return UiText.Get(Keys.Failed);

            return DescribeDisconnectReason(session.LastDisconnectReason);
        }

        // Turns a Netcode disconnect reason into player-facing text. A refused join arrives as a
        // BlockiverseJoinRejectionReason name, which says something actionable ("both headsets
        // need the same build") rather than an enum the player cannot act on. Anything else — a
        // transport error, a host-side message — passes through unchanged.
        public static string DescribeDisconnectReason(string disconnectReason)
        {
            if (string.IsNullOrWhiteSpace(disconnectReason))
                return string.Empty;

            if (!Enum.TryParse(disconnectReason.Trim(), out BlockiverseJoinRejectionReason rejectionReason))
                return disconnectReason;

            return rejectionReason switch
            {
                BlockiverseJoinRejectionReason.ProtocolMismatch => UiText.Get(Keys.RejectedProtocolMismatch),
                BlockiverseJoinRejectionReason.GameVersionMismatch => UiText.Get(Keys.RejectedGameVersionMismatch),
                BlockiverseJoinRejectionReason.BlockRegistryMismatch => UiText.Get(Keys.RejectedBlockRegistryMismatch),
                BlockiverseJoinRejectionReason.ItemRegistryMismatch => UiText.Get(Keys.RejectedItemRegistryMismatch),
                BlockiverseJoinRejectionReason.RecipeRegistryMismatch => UiText.Get(Keys.RejectedRecipeRegistryMismatch),
                BlockiverseJoinRejectionReason.UnsupportedWorldVersion => UiText.Get(Keys.RejectedUnsupportedWorldVersion),
                BlockiverseJoinRejectionReason.SessionFull => UiText.Get(Keys.RejectedSessionFull),
                BlockiverseJoinRejectionReason.InvalidJoinPayload => UiText.Get(Keys.RejectedInvalidJoinPayload),
                _ => disconnectReason,
            };
        }

        void SetStatus(string message)
        {
            if (statusLabel != null)
                statusLabel.text = AppendAgePolicyNotice(message);

            UpdateStatusTone();
        }

        // The uGUI panel's status badge (green/amber/red/grey Image tint) maps onto the
        // Hearthstone status modifiers instead of custom colours: confirmed = live session,
        // refused (ochre) = transitioning, rejected = failed or session-ended, bare = idle.
        // The status sentence itself is the accompanying word the signal system requires.
        void UpdateStatusTone()
        {
            if (statusLabel == null)
                return;

            bool connected = false;
            bool transitioning = false;
            bool failed = false;

            if (session != null)
            {
                switch (session.CurrentState)
                {
                    case BlockiverseConnectionState.Hosting:
                    case BlockiverseConnectionState.ConnectedClient:
                        connected = true;
                        break;
                    case BlockiverseConnectionState.StartingHost:
                    case BlockiverseConnectionState.StartingClient:
                    case BlockiverseConnectionState.Disconnecting:
                        transitioning = true;
                        break;
                    case BlockiverseConnectionState.Failed:
                        failed = true;
                        break;
                    case BlockiverseConnectionState.Disconnected:
                        failed = IsShowingSessionEndedMessage;
                        break;
                }
            }

            statusLabel.EnableInClassList("hs-status--confirmed", connected);
            statusLabel.EnableInClassList("hs-status--refused", transitioning);
            statusLabel.EnableInClassList("hs-status--rejected", failed);
        }

        static string AppendAgePolicyNotice(string message)
        {
            if (BlockiverseMetaSocialPolicy.CanUseMetaSocialFeature)
                return message;

            return UiText.Format(Keys.AgePolicyNotice, message);
        }

        // Ports and player counts are identifiers on the wire: pre-stringified invariant so
        // 7999 can never render as "7,999" in any locale.
        static string PortText(ushort port) => port.ToString(CultureInfo.InvariantCulture);

        // ── Control gating ───────────────────────────────────────────────────────────

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
                hostButton.SetEnabled(canStart);

            if (joinButton != null)
                joinButton.SetEnabled(canStart);

            if (reconnectButton != null)
                reconnectButton.SetEnabled(canStart && !string.IsNullOrWhiteSpace(LastJoinAddress));

            if (stopButton != null)
                stopButton.SetEnabled(canStop);

            if (addressField != null)
                addressField.SetEnabled(canStart);

            // Bookmark rows share the join gate: rejoining is meaningless mid-session.
            foreach (Button button in bookmarkButtons)
            {
                if (button != null && button.style.display != DisplayStyle.None)
                    button.SetEnabled(canStart);
            }
        }

        // ── Routing side-effects (ported latches; verbs go through the menu controller) ─

        bool TrySuspendSinglePlayerSessionForMultiplayer()
        {
            DiscoverWorldSessionController();

            if (worldSessionController == null)
                return true;

            if (worldSessionController.TrySuspendActiveSessionForMultiplayer(out string failureReason))
                return true;

            SetStatus(string.IsNullOrWhiteSpace(failureReason)
                ? UiText.Get(Keys.SuspendSinglePlayerFailed)
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

            BlockiverseMenuController controller = MenuController;
            if (controller == null)
                return;

            enteredGameplayForCurrentSession = true;
            controller.EnterGameplay();
        }

        static bool IsGameplaySessionState(BlockiverseConnectionState state)
        {
            return state == BlockiverseConnectionState.Hosting ||
                state == BlockiverseConnectionState.ConnectedClient;
        }

        // Host vanished while this peer was a client: route somewhere interactive, once per
        // episode. The uGUI menu also re-enabled its canvases here; a routed UI Toolkit screen
        // becomes visible through the host's router push, so no equivalent is needed.
        void EnsureSessionEndedMenuAvailable()
        {
            if (!IsShowingSessionEndedMessage)
                return;
            if (sessionEndedRouteRequested)
                return;

            BlockiverseMenuController controller = MenuController;

            if (enteredGameplayForCurrentSession)
            {
                if (controller != null)
                {
                    controller.ShowTitleScreen();
                    sessionEndedRouteRequested = true;
                    DiscoverWorldSessionController();
                    if (worldSessionController != null)
                        worldSessionController.TrySuspendActiveSessionForMultiplayer(out _);
                }
            }
            else if (controller != null && controller.ShowLanMultiplayerScreen())
            {
                sessionEndedRouteRequested = true;
            }
        }

        void OnCloseClicked()
        {
            // Same routing the bootstrapper wires for the uGUI close button: a persistent
            // listener on BlockiverseMenuController, not an action id through HandleAction.
            BlockiverseMenuController controller = MenuController;
            if (controller != null)
                controller.CloseLanMultiplayerScreen();
        }

        // ── Runtime text ─────────────────────────────────────────────────────────────

        void ApplyRuntimeTexts()
        {
            ApplyPlaceholders();

            if (hostButton != null)
                hostButton.text = UiText.Get(Keys.HostAction);

            if (joinButton != null)
                joinButton.text = UiText.Get(Keys.JoinAction);

            if (stopButton != null)
                stopButton.text = UiText.Get(Keys.StopAction);

            if (encryptionToggle != null)
                encryptionToggle.label = UiText.Get(Keys.EncryptionToggle);

            if (bookmarkHeadingLabel != null)
                bookmarkHeadingLabel.text = UiText.Get(Keys.SavedServers);
        }

        void ApplyPlaceholders()
        {
            if (addressField != null)
                addressField.textEdition.placeholder = UiText.Get(Keys.JoinAddressPlaceholder);

            if (secretField != null)
                secretField.textEdition.placeholder = UiText.Get(Keys.JoinSecretPlaceholder);
        }

        void OnSelectedLocaleChanged(Locale locale)
        {
            // UiText subscribes its cache flush before this controller registers, so these
            // re-reads already resolve against the new locale's table.
            ApplyRuntimeTexts();
            RefreshStatus();
            RefreshDiscoveryList();
        }

        // ── Dependency discovery (rig-prefab panels cannot serialize scene references) ─

        void DiscoverSession()
        {
            if (session != null || !Application.isPlaying || Time.unscaledTime < nextSessionSearchTime)
                return;

            nextSessionSearchTime = Time.unscaledTime + 1.0f;
            session = FindFirstObjectByType<BlockiverseNetworkSession>(FindObjectsInactive.Include);
        }

        void DiscoverWorldSessionController()
        {
            if (worldSessionController != null || !Application.isPlaying)
                return;

            worldSessionController = FindFirstObjectByType<BlockiverseWorldSessionController>(FindObjectsInactive.Include);
        }

        void DiscoverDiscovery()
        {
            if (discovery != null || !Application.isPlaying)
                return;

            discovery = FindFirstObjectByType<BlockiverseLanDiscovery>(FindObjectsInactive.Include);
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }
    }
}

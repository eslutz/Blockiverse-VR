using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Blockiverse.Core;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Blockiverse.Networking
{
    public delegate bool BlockiverseNetworkSessionPreparationHandler(out string failureReason);

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    [RequireComponent(typeof(UnityTransport))]
    public sealed class BlockiverseNetworkSession : MonoBehaviour
    {
        // Bumped to 2 when the payload gained the §5 compatibility fields (game version, world
        // save schema, registry hashes). A version-1 peer is refused with ProtocolMismatch.
        public const int ApprovalPayloadProtocolVersion = 2;
        public const int ForcedHostShutdownPreparationFailureThreshold = 2;
        const string ApprovalPayloadRulesetVersion = "voxel-networking-1";
        const string ApprovalPayloadSessionMode = "lan_host_authoritative";
        const string ApprovalPayloadVoiceMode = "meta_quest_party_chat_external";
        const char ApprovalPayloadSeparator = '|';
        const string ApprovalPayloadMagic = "blockiverse_lan";
        const int ApprovalPayloadFieldCount = 12;
        const int ApprovalPayloadPartCount = ApprovalPayloadFieldCount + 1;
        // Body runs ~200 bytes (three 32-char hashes plus metadata) and the signature adds 44.
        const int ApprovalPayloadMaxBytes = 1024;

        [SerializeField]
        BlockiverseNetworkConfig config = BlockiverseNetworkConfig.Default;

        [SerializeField]
        NetworkManager networkManager;

        [SerializeField]
        UnityTransport unityTransport;

        [SerializeField]
        bool useEncryptedTransport;

        [SerializeField]
        string transportServerCommonName = "blockiverse-lan";

        [SerializeField, TextArea(4, 12)]
        string serverCertificatePem;

        [SerializeField, TextArea(4, 12)]
        string serverPrivateKeyPem;

        [SerializeField, TextArea(4, 12)]
        string clientCaCertificatePem;

        /// <summary>
        /// How long a peer departure waits before it is announced. A guest cannot tell "someone
        /// left" from "the world is closing" at the instant Netcode reports it — during a host
        /// shutdown it is told the other guests disconnected before its own disconnect arrives.
        /// Waiting a moment lets this seat's own disconnect withdraw the announcement instead.
        /// Short enough to read as immediate; far longer than a LAN round trip, which is what has
        /// to land inside it. If it ever does not, the announcement is merely spurious, which is
        /// the behaviour this replaced.
        /// </summary>
        public const float PeerDepartureSettleSeconds = 0.35f;

        bool subscribed;
        bool stopRequestedByLocalSession;
        int consecutiveHostShutdownPreparationFailures;

        // Which remote peers this seat believes are in the session, so join/leave notifications
        // describe real arrivals and departures rather than raw Netcode callbacks.
        readonly BlockiversePeerPresence peerPresence = new BlockiversePeerPresence();

        // Departures wait this long before they are announced, so that a departure which is really
        // the session ending can be withdrawn. See PeerDepartureSettleSeconds.
        readonly List<(ulong clientId, float announceAtUnscaledTime)> pendingDepartureAnnouncements = new();
        readonly List<ulong> departureAnnouncementScratch = new();

        public BlockiverseConnectionState CurrentState { get; private set; } = BlockiverseConnectionState.Stopped;
        public NetworkSessionMode CurrentMode { get; private set; } = NetworkSessionMode.Offline;
        public string LastDisconnectReason { get; private set; } = string.Empty;
        public bool HasConnectedAsClient { get; private set; }
        public bool LastStopRequestSucceeded { get; private set; } = true;
        public bool LastStopForcedAfterPreparationFailure { get; private set; }
        public int ConsecutiveHostShutdownPreparationFailures => consecutiveHostShutdownPreparationFailures;
        public NetworkManager NetworkManager => ResolveNetworkManager();
        public UnityTransport UnityTransport => ResolveUnityTransport();
        public BlockiverseNetworkConfig Config => config;
        public bool IsTransportEncryptionRequested => useEncryptedTransport;
        public bool IsTransportEncryptionConfigured => HasTransportEncryptionSecrets();
        public ulong LocalClientId => networkManager != null ? networkManager.LocalClientId : 0;
        public bool IsServer => networkManager != null && networkManager.IsServer;

        public bool TryResolvePlayerHeadWorldPosition(ulong clientId, out Vector3 position)
        {
            position = default;
            if (networkManager == null || !networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                return false;

            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null)
                return false;

            BlockiverseNetworkAvatarRig avatarRig = playerObject.GetComponent<BlockiverseNetworkAvatarRig>();
            Transform headTransform = avatarRig?.HeadAnchor != null ? avatarRig.HeadAnchor : playerObject.transform;
            position = headTransform.position;
            return true;
        }

        public IEnumerable<ulong> ConnectedClientIds => networkManager != null ? networkManager.ConnectedClientsIds : Array.Empty<ulong>();

        public event BlockiverseNetworkSessionPreparationHandler HostStartPreparing;
        public event BlockiverseNetworkSessionPreparationHandler HostShutdownPreparing;
        public Action<ulong> ClientConnected;
        public Action<ulong> ClientDisconnected;

        void Awake()
        {
            ResolveDependencies();
            Subscribe();
        }

        void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            pendingDepartureAnnouncements.Clear();
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        public void Configure(BlockiverseNetworkConfig newConfig)
        {
            ResolveDependencies();
            if (networkManager.IsListening)
                throw new InvalidOperationException("Cannot change multiplayer config while a session is active.");

            config = newConfig;
            ApplyConnectionApprovalSettings();
        }

        public void ConfigureTransportSecurity(
            bool enabled,
            string serverCertificate,
            string serverPrivateKey,
            string serverCommonName,
            string clientCaCertificate = null)
        {
            ResolveDependencies();
            if (networkManager.IsListening)
                throw new InvalidOperationException("Cannot change multiplayer transport security while a session is active.");

            useEncryptedTransport = enabled;
            serverCertificatePem = serverCertificate;
            serverPrivateKeyPem = serverPrivateKey;
            transportServerCommonName = string.IsNullOrWhiteSpace(serverCommonName)
                ? "blockiverse-lan"
                : serverCommonName.Trim();
            clientCaCertificatePem = string.IsNullOrWhiteSpace(clientCaCertificate)
                ? serverCertificate
                : clientCaCertificate;
        }

        public bool StartHost()
        {
            if (!PrepareToStart(NetworkSessionMode.Host))
                return false;

            if (!ApplyTransportSecurity(NetworkSessionMode.Host))
                return false;

            if (!RunPreparation(HostStartPreparing, "Unable to prepare LAN host session."))
            {
                MarkFailed(LastDisconnectReason);
                return false;
            }

            CurrentState = BlockiverseConnectionState.StartingHost;
            ApplyConnectionData(config.Address, config.ListenAddress);

            bool started = networkManager.StartHost();
            if (!started)
                MarkFailed("Failed to start host session.");

            return started;
        }

        public bool StartClient(string address)
        {
            if (!PrepareToStart(NetworkSessionMode.Client))
                return false;

            if (!ApplyTransportSecurity(NetworkSessionMode.Client))
                return false;

            string targetAddress = string.IsNullOrWhiteSpace(address) ? config.Address : address;
            CurrentState = BlockiverseConnectionState.StartingClient;
            ApplyConnectionData(targetAddress, null);

            bool started = networkManager.StartClient();
            if (!started)
                MarkFailed($"Failed to start client session for {targetAddress}:{config.Port}.");

            return started;
        }

        public void StopSession()
        {
            ResolveDependencies();
            LastStopRequestSucceeded = true;

            if (!networkManager.IsListening && !networkManager.ShutdownInProgress)
            {
                CurrentMode = NetworkSessionMode.Offline;
                CurrentState = BlockiverseConnectionState.Stopped;
                HasConnectedAsClient = false;
                stopRequestedByLocalSession = false;
                consecutiveHostShutdownPreparationFailures = 0;
                LastStopForcedAfterPreparationFailure = false;
                return;
            }

            if (CurrentMode == NetworkSessionMode.Host &&
                networkManager.IsListening &&
                !RunPreparation(HostShutdownPreparing, "Unable to prepare LAN host shutdown."))
            {
                consecutiveHostShutdownPreparationFailures++;
                if (consecutiveHostShutdownPreparationFailures < ForcedHostShutdownPreparationFailureThreshold)
                {
                    LastStopRequestSucceeded = false;
                    LastStopForcedAfterPreparationFailure = false;
                    CurrentState = BlockiverseConnectionState.Hosting;
                    stopRequestedByLocalSession = false;
                    return;
                }

                LastStopForcedAfterPreparationFailure = true;
            }
            else
            {
                consecutiveHostShutdownPreparationFailures = 0;
                LastStopForcedAfterPreparationFailure = false;
            }

            CurrentState = BlockiverseConnectionState.Disconnecting;
            stopRequestedByLocalSession = true;
            networkManager.Shutdown();
        }

        bool PrepareToStart(NetworkSessionMode mode)
        {
            ResolveDependencies();
            Subscribe();

            if (networkManager.IsListening || networkManager.ShutdownInProgress)
                return false;

            LastDisconnectReason = string.Empty;
            CurrentMode = mode;
            HasConnectedAsClient = false;
            LastStopRequestSucceeded = true;
            LastStopForcedAfterPreparationFailure = false;
            consecutiveHostShutdownPreparationFailures = 0;
            stopRequestedByLocalSession = false;
            return true;
        }

        public byte[] CreateApprovalPayload()
        {
            return BuildApprovalPayload(config);
        }

        public bool ValidateConnectionRequest(byte[] payload, int connectedPlayerCount, out string failureReason)
        {
            bool approved = ValidateConnectionRequest(
                payload,
                connectedPlayerCount,
                out BlockiverseJoinRejectionReason rejectionReason);
            failureReason = approved ? string.Empty : rejectionReason.ToString();
            return approved;
        }

        public bool ValidateConnectionRequest(
            byte[] payload,
            int connectedPlayerCount,
            out BlockiverseJoinRejectionReason rejectionReason)
        {
            // Capacity is checked first: a full session should say so even when the joining build
            // is also incompatible, because "come back later" is the more actionable message.
            if (connectedPlayerCount >= config.MaxPlayers)
            {
                rejectionReason = BlockiverseJoinRejectionReason.SessionFull;
                return false;
            }

            rejectionReason = ValidateApprovalPayload(payload, config);
            return rejectionReason == BlockiverseJoinRejectionReason.None;
        }

        bool RunPreparation(
            BlockiverseNetworkSessionPreparationHandler preparationHandlers,
            string defaultFailureReason)
        {
            if (preparationHandlers == null)
                return true;

            foreach (BlockiverseNetworkSessionPreparationHandler handler in preparationHandlers.GetInvocationList())
            {
                try
                {
                    if (handler(out string failureReason))
                        continue;

                    LastDisconnectReason = string.IsNullOrWhiteSpace(failureReason)
                        ? defaultFailureReason
                        : failureReason;
                    return false;
                }
                catch (Exception exception)
                {
                    LastDisconnectReason = $"{defaultFailureReason} exception={exception.GetType().Name}";
                    return false;
                }
            }

            return true;
        }

        void ApplyConnectionData(string address, string listenAddress)
        {
            unityTransport.SetConnectionData(address, config.Port, listenAddress);
            networkManager.NetworkConfig.NetworkTransport = unityTransport;
            ApplyConnectionApprovalSettings();
        }

        bool ApplyTransportSecurity(NetworkSessionMode mode)
        {
            if (!useEncryptedTransport)
            {
                unityTransport.UseEncryption = false;
                return true;
            }

            if (!HasTransportEncryptionSecrets())
            {
                MarkFailed("Encrypted LAN transport requires server certificate, private key, and client CA certificate.");
                return false;
            }

            unityTransport.UseEncryption = true;
            if (mode == NetworkSessionMode.Host)
                unityTransport.SetServerSecrets(serverCertificatePem, serverPrivateKeyPem);
            else
                unityTransport.SetClientSecrets(transportServerCommonName, clientCaCertificatePem);

            return true;
        }

        bool HasTransportEncryptionSecrets() =>
            !string.IsNullOrWhiteSpace(serverCertificatePem) &&
            !string.IsNullOrWhiteSpace(serverPrivateKeyPem) &&
            !string.IsNullOrWhiteSpace(transportServerCommonName) &&
            !string.IsNullOrWhiteSpace(clientCaCertificatePem);

        void ApplyConnectionApprovalSettings()
        {
            if (networkManager?.NetworkConfig == null)
                return;

            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.ConnectionData = BuildApprovalPayload(config);
            networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        }

        void HandleConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            int connectedPlayerCount = networkManager != null ? networkManager.ConnectedClientsIds.Count : 0;
            bool approved = ValidateConnectionRequest(
                request.Payload,
                connectedPlayerCount,
                out BlockiverseJoinRejectionReason rejectionReason);
            response.Approved = approved;
            response.CreatePlayerObject = approved;
            // Netcode delivers this to the refused client as NetworkManager.DisconnectReason; the
            // session menu maps the enum name to localized text.
            response.Reason = approved ? string.Empty : rejectionReason.ToString();
            response.Pending = false;

            if (!approved)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Networking,
                    $"Refused LAN join reason={rejectionReason} connectedPlayers={connectedPlayerCount} maxPlayers={config.MaxPlayers}",
                    this);
            }
        }

        void MarkFailed(string reason)
        {
            peerPresence.Clear();
            pendingDepartureAnnouncements.Clear();
            LastDisconnectReason = reason;
            CurrentMode = NetworkSessionMode.Offline;
            CurrentState = BlockiverseConnectionState.Failed;
            HasConnectedAsClient = false;
            stopRequestedByLocalSession = false;
        }

        void HandleServerStarted()
        {
            if (CurrentMode == NetworkSessionMode.Host)
                CurrentState = BlockiverseConnectionState.Hosting;
        }

        void HandleClientStarted()
        {
            if (CurrentMode == NetworkSessionMode.Client)
                CurrentState = BlockiverseConnectionState.StartingClient;
        }

        void HandleClientConnected(ulong clientId)
        {
            if (networkManager == null || clientId != networkManager.LocalClientId)
                return;

            if (CurrentMode == NetworkSessionMode.Host)
            {
                CurrentState = BlockiverseConnectionState.Hosting;
                return;
            }

            HasConnectedAsClient = true;
            CurrentState = BlockiverseConnectionState.ConnectedClient;
        }

        void HandleClientDisconnected(ulong clientId)
        {
            if (networkManager == null || (networkManager.IsServer && clientId != networkManager.LocalClientId))
                return;

            if (CurrentState == BlockiverseConnectionState.Failed)
                return;

            LastDisconnectReason = ResolveDisconnectReason();

            if (CurrentState != BlockiverseConnectionState.Disconnecting || !stopRequestedByLocalSession)
                CurrentState = BlockiverseConnectionState.Disconnected;
        }

        /// <summary>
        /// Netcode's connection event carries the four cases the legacy callbacks cannot express:
        /// the local seat connecting or disconnecting, and a remote peer arriving or leaving.
        /// The peer cases are what reach a non-host client at all — <c>OnClientConnectedCallback</c>
        /// and <c>OnClientDisconnectCallback</c> only ever name the local client there, so a client
        /// seat is otherwise never told that anyone else came or went.
        /// </summary>
        void HandleConnectionEvent(NetworkManager manager, ConnectionEventData connectionEvent)
        {
            ulong localClientId = manager != null ? manager.LocalClientId : 0;

            switch (connectionEvent.EventType)
            {
                case ConnectionEvent.ClientConnected:
                    if (connectionEvent.ClientId == localClientId)
                    {
                        // We are the one who just connected. Anyone already here is tracked but
                        // not announced — they did not arrive, we did.
                        if (connectionEvent.PeerClientIds.IsCreated)
                        {
                            foreach (ulong peerId in connectionEvent.PeerClientIds)
                                peerPresence.AddKnownPeer(peerId, localClientId);
                        }

                        return;
                    }

                    AnnouncePeerConnected(connectionEvent.ClientId, localClientId);
                    return;

                case ConnectionEvent.PeerConnected:
                    AnnouncePeerConnected(connectionEvent.ClientId, localClientId);
                    return;

                case ConnectionEvent.ClientDisconnected:
                    if (connectionEvent.ClientId == localClientId)
                    {
                        // Our own session ended; the peers did not each leave, so say nothing.
                        peerPresence.Clear();
                        pendingDepartureAnnouncements.Clear();
                        return;
                    }

                    AnnouncePeerDisconnected(connectionEvent.ClientId, localClientId);
                    return;

                case ConnectionEvent.PeerDisconnected:
                    AnnouncePeerDisconnected(connectionEvent.ClientId, localClientId);
                    return;
            }
        }

        void AnnouncePeerConnected(ulong clientId, ulong localClientId)
        {
            if (peerPresence.TryAddPeer(clientId, localClientId) && IsSessionLive)
                ClientConnected?.Invoke(clientId);
        }

        void AnnouncePeerDisconnected(ulong clientId, ulong localClientId)
        {
            // A join refused during approval is disconnected without ever being present, so
            // TryRemovePeer reports no change and no departure is queued.
            if (!peerPresence.TryRemovePeer(clientId, localClientId) || !IsSessionLive)
                return;

            pendingDepartureAnnouncements.Add((clientId, Time.unscaledTime + PeerDepartureSettleSeconds));
        }

        /// <summary>
        /// Releases departures that have outlived the settle window, and drops every pending one
        /// the moment this seat stops being in a live session.
        ///
        /// This is what makes a guest seat agree with the host about what happened. When a host
        /// stops, Netcode disconnects the guests one at a time, and each disconnect is broadcast to
        /// the guests still connected (NetworkConnectionManager.OnClientDisconnectFromServer). A
        /// guest therefore hears that the others left a fraction of a second before it is
        /// disconnected itself — true message by message, but wrong as a description: nobody left,
        /// the world closed. Only this seat's own disconnect distinguishes the two, and it has not
        /// arrived yet, so the decision has to wait rather than be made on the spot.
        /// </summary>
        void FlushPendingDepartureAnnouncements()
        {
            if (pendingDepartureAnnouncements.Count == 0)
                return;

            if (!IsSessionLive)
            {
                pendingDepartureAnnouncements.Clear();
                return;
            }

            float now = Time.unscaledTime;
            int due = 0;
            while (due < pendingDepartureAnnouncements.Count &&
                   now >= pendingDepartureAnnouncements[due].announceAtUnscaledTime)
                due++;

            if (due == 0)
                return;

            // Copy out before raising: a handler may stop the session, which clears the list.
            departureAnnouncementScratch.Clear();
            for (int index = 0; index < due; index++)
                departureAnnouncementScratch.Add(pendingDepartureAnnouncements[index].clientId);

            pendingDepartureAnnouncements.RemoveRange(0, due);

            foreach (ulong clientId in departureAnnouncementScratch)
                ClientDisconnected?.Invoke(clientId);

            departureAnnouncementScratch.Clear();
        }

        void Update()
        {
            FlushPendingDepartureAnnouncements();
        }

        /// <summary>
        /// Whether peer arrivals and departures are worth telling the player about. Tearing a
        /// session down disconnects every peer in turn and Netcode reports each one, but a host
        /// stopping its own world is one event, not one departure per guest. Save preparation runs
        /// before the shutdown, so those disconnects can land well after the player asked to stop.
        /// Presence is still tracked either way; only the announcement is withheld.
        ///
        /// Stated as which states are tearing down rather than which are live, so that a connection
        /// state added later (a dedicated server's, say) announces by default instead of going
        /// silently unannounced until someone notices.
        ///
        /// "Has a session started at all" is asked of <see cref="CurrentMode"/> rather than of
        /// CurrentState, because CurrentState's own default is Stopped. Denylisting Stopped would
        /// have inverted the intent above for exactly the case it was written for: a new start path
        /// that sets a mode but leaves CurrentState at its initial value would announce nothing for
        /// the whole session, with no error and every state assertion still passing. Every start
        /// path sets CurrentMode through PrepareToStart, and both stop paths return it to Offline.
        /// </summary>
        bool IsSessionLive =>
            CurrentMode != NetworkSessionMode.Offline &&
            CurrentState != BlockiverseConnectionState.Disconnecting &&
            CurrentState != BlockiverseConnectionState.Disconnected &&
            CurrentState != BlockiverseConnectionState.Failed;

        void HandleServerStopped(bool wasHost)
        {
            MarkStopped();
        }

        void HandleClientStopped(bool wasHost)
        {
            MarkStopped();
        }

        void MarkStopped()
        {
            peerPresence.Clear();
            pendingDepartureAnnouncements.Clear();
            CurrentMode = NetworkSessionMode.Offline;
            stopRequestedByLocalSession = false;

            if (CurrentState == BlockiverseConnectionState.Disconnected ||
                CurrentState == BlockiverseConnectionState.Failed)
                return;

            HasConnectedAsClient = false;
            CurrentState = BlockiverseConnectionState.Stopped;
        }

        void HandleTransportFailure()
        {
            MarkFailed("Transport failure.");
        }

        string ResolveDisconnectReason()
        {
            string reason = networkManager != null ? networkManager.DisconnectReason : string.Empty;

            if (!string.IsNullOrWhiteSpace(reason))
                return reason;

            return string.Empty;
        }

        void ResolveDependencies()
        {
            ResolveNetworkManager();
            ResolveUnityTransport();
            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = unityTransport;
            ApplyConnectionApprovalSettings();
        }

        static string[] BuildApprovalPayloadFields(BlockiverseNetworkConfig config) => new[]
        {
            ApprovalPayloadMagic,
            ApprovalPayloadProtocolVersion.ToString(CultureInfo.InvariantCulture),
            ApprovalPayloadRulesetVersion,
            config.Port.ToString(CultureInfo.InvariantCulture),
            config.MaxPlayers.ToString(CultureInfo.InvariantCulture),
            ApprovalPayloadSessionMode,
            ApprovalPayloadVoiceMode,
            LocalGameVersion,
            WorldSaveService.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
            LocalBlockRegistryHash,
            LocalItemRegistryHash,
            LocalRecipeRegistryHash,
        };

        static byte[] BuildApprovalPayload(BlockiverseNetworkConfig config)
        {
            string body = string.Join(ApprovalPayloadSeparator.ToString(), BuildApprovalPayloadFields(config));
            string signature = Convert.ToBase64String(ComputePayloadSignature(body, config.JoinCode));
            return Encoding.UTF8.GetBytes(body + ApprovalPayloadSeparator + signature);
        }

        static BlockiverseJoinRejectionReason ValidateApprovalPayload(
            byte[] payload,
            BlockiverseNetworkConfig config)
        {
            if (payload == null || payload.Length == 0 || payload.Length > ApprovalPayloadMaxBytes)
                return BlockiverseJoinRejectionReason.InvalidJoinPayload;

            string text;
            try
            {
                text = Encoding.UTF8.GetString(payload);
            }
            catch (ArgumentException)
            {
                return BlockiverseJoinRejectionReason.InvalidJoinPayload;
            }

            string[] parts = text.Split(ApprovalPayloadSeparator);

            // Checked before the part count so a peer speaking an older payload shape — which has
            // a different field count — still gets ProtocolMismatch rather than a generic
            // "malformed payload" that reads like corruption.
            if (parts.Length >= 2 &&
                parts[0] == ApprovalPayloadMagic &&
                parts[1] != ApprovalPayloadProtocolVersion.ToString(CultureInfo.InvariantCulture))
                return BlockiverseJoinRejectionReason.ProtocolMismatch;

            // parts[4] is the joiner's own configured capacity. It is deliberately not compared:
            // capacity is the host's business, enforced by the connected-player count above, and
            // a client should not have to mirror the host's setting to be let in.
            if (parts.Length != ApprovalPayloadPartCount ||
                parts[0] != ApprovalPayloadMagic ||
                parts[2] != ApprovalPayloadRulesetVersion ||
                parts[3] != config.Port.ToString(CultureInfo.InvariantCulture) ||
                parts[5] != ApprovalPayloadSessionMode ||
                parts[6] != ApprovalPayloadVoiceMode)
                return BlockiverseJoinRejectionReason.InvalidJoinPayload;

            // Signature before content comparisons: an unsigned or wrongly-signed payload is not
            // trustworthy enough to report a specific mismatch from.
            string body = string.Join(ApprovalPayloadSeparator.ToString(), parts, 0, ApprovalPayloadFieldCount);
            byte[] expected = ComputePayloadSignature(body, config.JoinCode);
            byte[] actual;
            try
            {
                actual = Convert.FromBase64String(parts[ApprovalPayloadFieldCount]);
            }
            catch (FormatException)
            {
                return BlockiverseJoinRejectionReason.InvalidJoinPayload;
            }

            if (!FixedTimeEquals(expected, actual))
                return BlockiverseJoinRejectionReason.InvalidJoinPayload;

            if (parts[7] != LocalGameVersion)
                return BlockiverseJoinRejectionReason.GameVersionMismatch;

            if (parts[8] != WorldSaveService.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture))
                return BlockiverseJoinRejectionReason.UnsupportedWorldVersion;

            if (parts[9] != LocalBlockRegistryHash)
                return BlockiverseJoinRejectionReason.BlockRegistryMismatch;

            if (parts[10] != LocalItemRegistryHash)
                return BlockiverseJoinRejectionReason.ItemRegistryMismatch;

            if (parts[11] != LocalRecipeRegistryHash)
                return BlockiverseJoinRejectionReason.RecipeRegistryMismatch;

            return BlockiverseJoinRejectionReason.None;
        }

        // Registry hashes are pure functions of the built-in registries, so they are computed once
        // per process rather than on every join attempt and every payload validation.
        static string cachedBlockRegistryHash;
        static string cachedItemRegistryHash;
        static string cachedRecipeRegistryHash;

        public static string LocalGameVersion =>
            string.IsNullOrWhiteSpace(Application.version) ? "0.0.0-dev" : Application.version;

        // Deliberately not WorldSaveService's hashes: those cover canonical string ids, which
        // is what a save stores. The wire sends integer BlockIds, so the handshake needs the
        // id→integer mapping and the definition fields peers simulate from. See
        // BlockiverseRegistryCompatibility.
        public static string LocalBlockRegistryHash =>
            cachedBlockRegistryHash ??= BlockiverseRegistryCompatibility.ComputeBlockHash(BlockRegistry.Default);

        public static string LocalItemRegistryHash =>
            cachedItemRegistryHash ??= BlockiverseRegistryCompatibility.ComputeItemHash(ItemRegistry.Default);

        public static string LocalRecipeRegistryHash =>
            cachedRecipeRegistryHash ??= BlockiverseRegistryCompatibility.ComputeRecipeHash(CraftingRecipeBook.Default);

        static byte[] ComputePayloadSignature(string body, string joinCode) =>
            BlockiverseLanPayloadSigning.ComputeSignature(body, joinCode);

        static bool FixedTimeEquals(byte[] left, byte[] right) =>
            BlockiverseLanPayloadSigning.FixedTimeEquals(left, right);

        NetworkManager ResolveNetworkManager()
        {
            if (networkManager == null)
                networkManager = GetComponent<NetworkManager>();

            if (networkManager == null)
                throw new InvalidOperationException($"{nameof(BlockiverseNetworkSession)} requires a {nameof(NetworkManager)}.");

            return networkManager;
        }

        UnityTransport ResolveUnityTransport()
        {
            if (unityTransport == null)
                unityTransport = GetComponent<UnityTransport>();

            if (unityTransport == null)
                throw new InvalidOperationException($"{nameof(BlockiverseNetworkSession)} requires a {nameof(UnityTransport)}.");

            return unityTransport;
        }

        void Subscribe()
        {
            if (subscribed || networkManager == null)
                return;

            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnClientStarted += HandleClientStarted;
            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            networkManager.OnConnectionEvent += HandleConnectionEvent;
            networkManager.OnServerStopped += HandleServerStopped;
            networkManager.OnClientStopped += HandleClientStopped;
            networkManager.OnTransportFailure += HandleTransportFailure;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || networkManager == null)
                return;

            networkManager.OnServerStarted -= HandleServerStarted;
            networkManager.OnClientStarted -= HandleClientStarted;
            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            networkManager.OnConnectionEvent -= HandleConnectionEvent;
            networkManager.OnServerStopped -= HandleServerStopped;
            networkManager.OnClientStopped -= HandleClientStopped;
            networkManager.OnTransportFailure -= HandleTransportFailure;
            subscribed = false;
        }
    }
}

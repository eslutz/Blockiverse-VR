using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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
        public const int ApprovalPayloadProtocolVersion = 1;
        public const int ForcedHostShutdownPreparationFailureThreshold = 2;
        const string ApprovalPayloadRulesetVersion = "voxel-networking-1";
        const string ApprovalPayloadSessionMode = "lan_host_authoritative";
        const string ApprovalPayloadVoiceMode = "meta_quest_party_chat_external";
        const char ApprovalPayloadSeparator = '|';

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

        bool subscribed;
        bool stopRequestedByLocalSession;
        int consecutiveHostShutdownPreparationFailures;

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
            if (connectedPlayerCount >= config.MaxPlayers)
            {
                failureReason = "SessionFull";
                return false;
            }

            if (!ValidateApprovalPayload(payload, config))
            {
                failureReason = "InvalidJoinPayload";
                return false;
            }

            failureReason = string.Empty;
            return true;
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
            bool approved = ValidateConnectionRequest(request.Payload, connectedPlayerCount, out string failureReason);
            response.Approved = approved;
            response.CreatePlayerObject = approved;
            response.Reason = approved ? string.Empty : failureReason;
            response.Pending = false;
        }

        void MarkFailed(string reason)
        {
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
            ClientConnected?.Invoke(clientId);

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
            ClientDisconnected?.Invoke(clientId);

            if (networkManager == null || (networkManager.IsServer && clientId != networkManager.LocalClientId))
                return;

            if (CurrentState == BlockiverseConnectionState.Failed)
                return;

            LastDisconnectReason = ResolveDisconnectReason();

            if (CurrentState != BlockiverseConnectionState.Disconnecting || !stopRequestedByLocalSession)
                CurrentState = BlockiverseConnectionState.Disconnected;
        }

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

        static byte[] BuildApprovalPayload(BlockiverseNetworkConfig config)
        {
            string body = string.Join(
                ApprovalPayloadSeparator.ToString(),
                "blockiverse_lan",
                ApprovalPayloadProtocolVersion.ToString(),
                ApprovalPayloadRulesetVersion,
                config.Port.ToString(),
                config.MaxPlayers.ToString(),
                ApprovalPayloadSessionMode,
                ApprovalPayloadVoiceMode);
            string signature = Convert.ToBase64String(ComputePayloadSignature(body, config.JoinCode));
            return Encoding.UTF8.GetBytes(body + ApprovalPayloadSeparator + signature);
        }

        static bool ValidateApprovalPayload(byte[] payload, BlockiverseNetworkConfig config)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 512)
                return false;

            string text;
            try
            {
                text = Encoding.UTF8.GetString(payload);
            }
            catch (ArgumentException)
            {
                return false;
            }

            string[] parts = text.Split(ApprovalPayloadSeparator);
            if (parts.Length != 8 ||
                parts[0] != "blockiverse_lan" ||
                parts[1] != ApprovalPayloadProtocolVersion.ToString() ||
                parts[2] != ApprovalPayloadRulesetVersion ||
                parts[3] != config.Port.ToString() ||
                parts[5] != ApprovalPayloadSessionMode ||
                parts[6] != ApprovalPayloadVoiceMode)
                return false;

            string body = string.Join(ApprovalPayloadSeparator.ToString(), parts, 0, 7);
            byte[] expected = ComputePayloadSignature(body, config.JoinCode);
            byte[] actual;
            try
            {
                actual = Convert.FromBase64String(parts[7]);
            }
            catch (FormatException)
            {
                return false;
            }

            return FixedTimeEquals(expected, actual);
        }

        static byte[] ComputePayloadSignature(string body, string joinCode)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(joinCode));
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        }

        static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
                diff |= left[i] ^ right[i];

            return diff == 0;
        }

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
            networkManager.OnServerStopped -= HandleServerStopped;
            networkManager.OnClientStopped -= HandleClientStopped;
            networkManager.OnTransportFailure -= HandleTransportFailure;
            subscribed = false;
        }
    }
}

using System;
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
        [SerializeField]
        BlockiverseNetworkConfig config = BlockiverseNetworkConfig.Default;

        [SerializeField]
        NetworkManager networkManager;

        [SerializeField]
        UnityTransport unityTransport;

        bool subscribed;
        bool stopRequestedByLocalSession;

        public BlockiverseConnectionState CurrentState { get; private set; } = BlockiverseConnectionState.Stopped;
        public NetworkSessionMode CurrentMode { get; private set; } = NetworkSessionMode.Offline;
        public string LastDisconnectReason { get; private set; } = string.Empty;
        public bool HasConnectedAsClient { get; private set; }
        public bool LastStopRequestSucceeded { get; private set; } = true;
        public NetworkManager NetworkManager => ResolveNetworkManager();
        public UnityTransport UnityTransport => ResolveUnityTransport();
        public BlockiverseNetworkConfig Config => config;

        public event BlockiverseNetworkSessionPreparationHandler HostStartPreparing;
        public event BlockiverseNetworkSessionPreparationHandler HostShutdownPreparing;

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
            if (ResolveNetworkManager().IsListening)
                throw new InvalidOperationException("Cannot change multiplayer config while a session is active.");

            config = newConfig;
        }

        public bool StartHost()
        {
            if (!PrepareToStart(NetworkSessionMode.Host))
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
                return;
            }

            if (CurrentMode == NetworkSessionMode.Host &&
                networkManager.IsListening &&
                !RunPreparation(HostShutdownPreparing, "Unable to prepare LAN host shutdown."))
            {
                LastStopRequestSucceeded = false;
                CurrentState = BlockiverseConnectionState.Hosting;
                stopRequestedByLocalSession = false;
                return;
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
            stopRequestedByLocalSession = false;
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

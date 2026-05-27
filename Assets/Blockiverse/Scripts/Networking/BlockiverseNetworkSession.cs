using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Blockiverse.Networking
{
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

        public BlockiverseConnectionState CurrentState { get; private set; } = BlockiverseConnectionState.Stopped;
        public NetworkSessionMode CurrentMode { get; private set; } = NetworkSessionMode.Offline;
        public string LastDisconnectReason { get; private set; } = string.Empty;
        public NetworkManager NetworkManager => ResolveNetworkManager();
        public UnityTransport UnityTransport => ResolveUnityTransport();
        public BlockiverseNetworkConfig Config => config;

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

            if (!networkManager.IsListening && !networkManager.ShutdownInProgress)
            {
                CurrentMode = NetworkSessionMode.Offline;
                CurrentState = BlockiverseConnectionState.Stopped;
                return;
            }

            CurrentState = BlockiverseConnectionState.Disconnecting;
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

            CurrentState = CurrentMode == NetworkSessionMode.Host
                ? BlockiverseConnectionState.Hosting
                : BlockiverseConnectionState.ConnectedClient;
        }

        void HandleClientDisconnected(ulong clientId)
        {
            if (networkManager == null || networkManager.IsServer && clientId != networkManager.LocalClientId)
                return;

            LastDisconnectReason = ResolveDisconnectReason();

            if (CurrentState != BlockiverseConnectionState.Disconnecting)
                CurrentState = BlockiverseConnectionState.Disconnected;
        }

        void HandleServerStopped(bool wasHost)
        {
            CurrentMode = NetworkSessionMode.Offline;
            CurrentState = BlockiverseConnectionState.Stopped;
        }

        void HandleClientStopped(bool wasHost)
        {
            CurrentMode = NetworkSessionMode.Offline;
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

            return networkManager != null ? networkManager.DisconnectEvent.ToString() : string.Empty;
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

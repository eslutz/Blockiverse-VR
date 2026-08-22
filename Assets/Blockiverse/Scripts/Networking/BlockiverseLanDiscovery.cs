using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.Networking
{
    /// <summary>
    /// UDP beacon that lets a LAN client find a host without typing an IP address — the roughest
    /// edge in the VR join flow. Netcode ships no discovery of its own.
    ///
    /// The host broadcasts once a second for as long as it is hosting, whether or not the LAN
    /// panel is open; clients browse only while that panel is open. Manual address entry remains
    /// the supported fallback: access points with client isolation drop broadcast traffic
    /// entirely, and nothing here can fix that.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseLanDiscovery : MonoBehaviour
    {
        public const float BeaconIntervalSeconds = 1.0f;

        /// <summary>How long a session stays listed after its last beacon (three missed beacons).</summary>
        public const float SessionExpirySeconds = 3.0f;

        /// <summary>Cap on listed sessions, so a noisy network cannot grow the list without bound.</summary>
        public const int MaxTrackedSessions = 8;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] ushort discoveryPort = BlockiverseLanDiscoveryBeacon.DefaultDiscoveryPort;
        [SerializeField] bool broadcastWhileHosting = true;

        readonly List<TrackedSession> trackedSessions = new();
        readonly List<BlockiverseDiscoveredSession> discoveredSessionsView = new();

        UdpClient socket;
        Task<UdpReceiveResult> pendingReceive;
        float beaconTimer;
        bool listenRequested;
        bool socketFailed;

        /// <summary>Sessions seen recently, newest beacon first. Rebuilt only when it changes.</summary>
        public IReadOnlyList<BlockiverseDiscoveredSession> DiscoveredSessions => discoveredSessionsView;

        /// <summary>True while the socket is open — for browsing, for beaconing, or both.</summary>
        public bool IsListening => socket != null;

        /// <summary>True while a client has asked to browse (i.e. the LAN menu is open).</summary>
        public bool ListenRequested => listenRequested;
        public bool IsBroadcasting { get; private set; }
        public ushort DiscoveryPort => discoveryPort;
        public int SentBeaconCount { get; private set; }
        public int ReceivedBeaconCount { get; private set; }
        public int RejectedBeaconCount { get; private set; }

        /// <summary>Raised whenever the discovered-session list changes, so UI can refresh.</summary>
        public event Action DiscoveredSessionsChanged;

        readonly struct TrackedSession
        {
            public TrackedSession(BlockiverseDiscoveredSession session, float lastSeenSeconds)
            {
                Session = session;
                LastSeenSeconds = lastSeenSeconds;
            }

            public BlockiverseDiscoveredSession Session { get; }
            public float LastSeenSeconds { get; }
        }

        public void Configure(BlockiverseNetworkSession targetSession, ushort port = 0)
        {
            session = targetSession;

            if (port != 0 && port != discoveryPort)
            {
                discoveryPort = port;
                // Rebind on the next tick: the port is part of the socket's identity.
                ReleaseSocket();
                socketFailed = false;
            }
        }

        void Awake()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();
        }

        void OnDisable()
        {
            listenRequested = false;
            ReleaseSocket();
        }

        void OnDestroy()
        {
            listenRequested = false;
            ReleaseSocket();
        }

        /// <summary>
        /// Asks for a browse socket. Called when the LAN menu opens rather than at startup: an
        /// idle listening socket costs battery and serves nobody while the player is in a world.
        /// </summary>
        public void StartListening()
        {
            listenRequested = true;
            EnsureSocket();
        }

        /// <summary>
        /// Stops browsing. The socket itself survives if this peer is still hosting — the beacon
        /// has to keep going out whether or not the LAN panel happens to be open.
        /// </summary>
        public void StopListening()
        {
            listenRequested = false;

            if (!ShouldBroadcast())
                ReleaseSocket();

            if (trackedSessions.Count == 0)
                return;

            trackedSessions.Clear();
            RebuildDiscoveredSessions();
        }

        void EnsureSocket()
        {
            if (socket != null || socketFailed)
                return;

            try
            {
                socket = new UdpClient
                {
                    EnableBroadcast = true,
                };

                // Address reuse lets a host and a client share the discovery port on one machine,
                // which is exactly what the loopback PlayMode test and a solo dev iteration do.
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
                BeginReceive();
            }
            catch (SocketException exception)
            {
                // A blocked or busy port is not fatal: manual address entry still works, so the
                // menu degrades to typing an IP rather than failing to open.
                socketFailed = true;
                DisposeSocket();
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Networking,
                    $"LAN discovery unavailable on port {discoveryPort}: {exception.SocketErrorCode}. Manual address entry is unaffected.",
                    this);
            }
        }

        void ReleaseSocket()
        {
            IsBroadcasting = false;
            beaconTimer = 0.0f;
            pendingReceive = null;
            DisposeSocket();
        }

        /// <summary>Clears the failure latch so a later attempt can retry a port that was busy.</summary>
        public void ResetSocketFailure() => socketFailed = false;

        void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Drives beaconing, receive draining, and expiry. Takes an explicit delta so tests can
        /// step it deterministically.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            bool shouldBroadcast = ShouldBroadcast();

            if (!listenRequested && !shouldBroadcast)
            {
                if (socket != null)
                    ReleaseSocket();

                return;
            }

            EnsureSocket();

            if (socket == null)
                return;

            float step = Mathf.Max(0.0f, deltaSeconds);
            DrainReceivedBeacons();

            // Re-arms a receive if anything left the loop un-armed (a fault, a rebuilt socket).
            // No-op while one is already in flight, so this cannot stack receives.
            BeginReceive();

            TickBeacon(step, shouldBroadcast);
            ExpireStaleSessions(step);
        }

        bool ShouldBroadcast() => broadcastWhileHosting && IsHostingSession();

        void TickBeacon(float deltaSeconds, bool shouldBroadcast)
        {
            IsBroadcasting = shouldBroadcast;

            if (!shouldBroadcast)
            {
                beaconTimer = 0.0f;
                return;
            }

            beaconTimer += deltaSeconds;
            if (beaconTimer < BeaconIntervalSeconds)
                return;

            beaconTimer = 0.0f;
            SendBeacon();
        }

        bool IsHostingSession() =>
            session != null &&
            session.CurrentState == BlockiverseConnectionState.Hosting &&
            session.CurrentMode == NetworkSessionMode.Host;

        void SendBeacon()
        {
            if (socket == null || session == null)
                return;

            BlockiverseNetworkConfig config = session.Config;
            int playerCount = 0;

            foreach (ulong _ in session.ConnectedClientIds)
                playerCount++;

            byte[] payload = BlockiverseLanDiscoveryBeacon.Encode(
                config.Port,
                playerCount,
                config.MaxPlayers,
                ResolveHostName(),
                config.JoinCode);

            try
            {
                socket.Send(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));
                SentBeaconCount++;
            }
            catch (SocketException exception)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Networking,
                    $"Failed to broadcast the LAN discovery beacon: {exception.SocketErrorCode}",
                    this);
            }
            catch (ObjectDisposedException)
            {
                // The socket closed underneath a queued send; the next StartListening rebuilds it.
            }
        }

        static string ResolveHostName()
        {
            string deviceName = SystemInfo.deviceName;
            return string.IsNullOrWhiteSpace(deviceName) || deviceName == SystemInfo.unsupportedIdentifier
                ? "LAN Host"
                : deviceName;
        }

        void BeginReceive()
        {
            if (socket == null || pendingReceive != null)
                return;

            try
            {
                pendingReceive = socket.ReceiveAsync();
            }
            catch (ObjectDisposedException)
            {
                pendingReceive = null;
            }
            catch (SocketException)
            {
                pendingReceive = null;
            }
        }

        // Completions are only ever observed here, on the main thread, so nothing touches Unity
        // API from the socket's thread-pool continuation.
        void DrainReceivedBeacons()
        {
            while (pendingReceive != null && pendingReceive.IsCompleted)
            {
                Task<UdpReceiveResult> completed = pendingReceive;
                pendingReceive = null;

                if (completed.IsCompletedSuccessfully)
                {
                    UdpReceiveResult result = completed.Result;
                    ApplyBeacon(result.Buffer, result.RemoteEndPoint?.Address?.ToString());
                }
                else if (completed.IsFaulted)
                {
                    // A receive faults when the socket is torn down mid-flight. Drop the socket
                    // rather than re-arming on a handle that will keep faulting; the next tick
                    // rebuilds it if browsing or beaconing is still wanted, and a bind that then
                    // fails latches through EnsureSocket so this cannot spin.
                    Exception error = completed.Exception?.GetBaseException();
                    if (error is not ObjectDisposedException)
                    {
                        BlockiverseLog.Warning(
                            BlockiverseLogCategory.Networking,
                            $"LAN discovery receive failed: {error?.Message}",
                            this);
                    }

                    ReleaseSocket();
                    return;
                }

                if (socket == null)
                    return;

                BeginReceive();
            }
        }

        /// <summary>
        /// Feeds one received beacon into the session list. The socket calls this with the
        /// datagram and its sender; the address always comes from the packet, never the payload.
        /// Public so the list's tracking and expiry behaviour can be exercised without a socket.
        /// </summary>
        public void ApplyBeacon(byte[] payload, string sourceAddress)
        {
            if (session == null)
                return;

            // A host has no use for its own beacon, and listing yourself as joinable is worse
            // than listing nothing.
            if (IsHostingSession())
                return;

            if (!BlockiverseLanDiscoveryBeacon.TryDecode(
                    payload,
                    sourceAddress,
                    session.Config.JoinCode,
                    out BlockiverseDiscoveredSession discovered))
            {
                RejectedBeaconCount++;
                return;
            }

            ReceivedBeaconCount++;
            TrackSession(discovered);
        }

        /// <summary>Ages the session list without touching the socket. Used by expiry tests.</summary>
        public void TickSessionExpiry(float deltaSeconds) => ExpireStaleSessions(Mathf.Max(0.0f, deltaSeconds));

        void TrackSession(BlockiverseDiscoveredSession discovered)
        {
            var tracked = new TrackedSession(discovered, 0.0f);

            for (int index = 0; index < trackedSessions.Count; index++)
            {
                if (!trackedSessions[index].Session.Equals(discovered))
                    continue;

                bool contentChanged =
                    trackedSessions[index].Session.PlayerCount != discovered.PlayerCount ||
                    trackedSessions[index].Session.MaxPlayers != discovered.MaxPlayers ||
                    trackedSessions[index].Session.HostName != discovered.HostName;

                trackedSessions[index] = tracked;

                if (contentChanged)
                    RebuildDiscoveredSessions();

                return;
            }

            if (trackedSessions.Count >= MaxTrackedSessions)
                return;

            trackedSessions.Add(tracked);
            RebuildDiscoveredSessions();
        }

        void ExpireStaleSessions(float deltaSeconds)
        {
            bool removedAny = false;

            for (int index = trackedSessions.Count - 1; index >= 0; index--)
            {
                float age = trackedSessions[index].LastSeenSeconds + deltaSeconds;

                if (age >= SessionExpirySeconds)
                {
                    trackedSessions.RemoveAt(index);
                    removedAny = true;
                    continue;
                }

                trackedSessions[index] = new TrackedSession(trackedSessions[index].Session, age);
            }

            if (removedAny)
                RebuildDiscoveredSessions();
        }

        void RebuildDiscoveredSessions()
        {
            discoveredSessionsView.Clear();

            foreach (TrackedSession tracked in trackedSessions)
                discoveredSessionsView.Add(tracked.Session);

            DiscoveredSessionsChanged?.Invoke();
        }

        void DisposeSocket()
        {
            if (socket == null)
                return;

            try
            {
                socket.Dispose();
            }
            catch (SocketException)
            {
                // Nothing useful to do while tearing down.
            }

            socket = null;
        }
    }
}

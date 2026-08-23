using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Blockiverse.Core;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Networking
{
    /// <summary>
    /// Answers a join-secret challenge with the player's Meta identity proof. Implemented in
    /// Blockiverse.MetaPlatform (which may reference Networking; the reverse is layered out) and
    /// registered on <see cref="BlockiverseServerAuthGate.IdentityProofSource"/> at client boot.
    /// </summary>
    public interface IBlockiverseIdentityProofSource
    {
        /// <summary>Calls back with (metaUserId, nonce), or (0, null) when unavailable.</summary>
        void RequestProof(Action<ulong, string> completed);
    }

    /// <summary>
    /// Post-connect authentication gate. Lives on the network stack next to the other sync
    /// components and is inert until configured, so LAN sessions are byte-identical to before it
    /// existed. Two independent requirements compose:
    ///
    /// - A join secret ("are you invited"): the server sends a random nonce, the client answers
    ///   HMAC(secret, nonce || clientId) (see <see cref="BlockiverseServerAuthProtocol"/>), and
    ///   verification is constant-time. The nonce makes a captured exchange worthless -- no
    ///   replay, no offline dictionary attack from one observation.
    /// - A platform identity ("who are you"): the client sends its Meta user id plus a one-shot
    ///   proof nonce from the Platform SDK; the server validates it against Meta's endpoint via
    ///   an injected validator, giving revocable per-account bans instead of a spoofable local
    ///   GUID.
    ///
    /// World state (late-join snapshots, survival channels) is withheld until authorization;
    /// clients that fail or stall are disconnected. The challenge is deliberately NOT in the
    /// connection-approval payload: approval is a single client-to-server message with no round
    /// trip, so a server nonce cannot reach the client before it, and a static signature over the
    /// predictable payload would be replayable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseServerAuthGate : MonoBehaviour
    {
        const string ChallengeMessage = "Blockiverse.Auth.Challenge";
        const string ResponseMessage = "Blockiverse.Auth.Response";
        const int MessageBytes = 512;
        const int ChallengeFlagSecretRequired = 1 << 0;
        const int ChallengeFlagIdentityRequired = 1 << 1;
        const int MaxIdentityNonceChars = 256;
        public const float DefaultResponseTimeoutSeconds = 10.0f;

        /// <summary>Disconnect reasons surfaced to the failing client.</summary>
        public const string ReasonSecretRequired = "ServerSecretRequired";
        public const string ReasonSecretRejected = "ServerSecretRejected";
        public const string ReasonIdentityRequired = "ServerIdentityRequired";
        public const string ReasonIdentityRejected = "ServerIdentityRejected";
        public const string ReasonBanned = "BannedFromServer";

        /// <summary>Server-side identity validation: (metaUserId, nonce, completed(valid)).
        /// May complete on any thread; the gate marshals back to the main thread.</summary>
        public delegate void IdentityValidator(ulong metaUserId, string nonce, Action<bool> completed);

        /// <summary>Client-side proof source, registered by the platform layer at boot.</summary>
        public static IBlockiverseIdentityProofSource IdentityProofSource;

        [SerializeField] BlockiverseNetworkSession session;

        string serverSecret = string.Empty;
        bool serverRequiresSecret;
        bool serverRequiresIdentity;
        IdentityValidator identityValidator;
        Func<string, bool> identityBanCheck;
        float responseTimeoutSeconds = DefaultResponseTimeoutSeconds;
        string clientSecret = string.Empty;

        sealed class PendingChallenge
        {
            public byte[] Nonce;
            public double Deadline;
            public bool AwaitingIdentityValidation;
            public ulong ClaimedMetaUserId;
        }

        readonly Dictionary<ulong, PendingChallenge> pendingByClientId = new();
        readonly HashSet<ulong> authorizedClientIds = new();
        readonly Dictionary<ulong, ulong> metaUserIdByClientId = new();
        readonly ConcurrentQueue<Action> mainThreadActions = new();
        NetworkManager subscribedNetworkManager;
        bool messagesRegistered;

        /// <summary>Raised on the server when a client completes the challenge.</summary>
        public event Action<ulong> ClientAuthorized;

        /// <summary>Operator-side configuration. An empty secret disables the secret check.</summary>
        public void ConfigureServer(string secret, bool required, float timeoutSeconds = DefaultResponseTimeoutSeconds)
        {
            serverSecret = secret ?? string.Empty;
            serverRequiresSecret = required && !string.IsNullOrEmpty(serverSecret);
            responseTimeoutSeconds = Mathf.Max(1.0f, timeoutSeconds);
        }

        /// <summary>
        /// Requires clients to prove a Meta account. The validator calls Meta's nonce-validation
        /// endpoint; the ban check (id form "meta:&lt;userId&gt;") runs after a valid proof so a
        /// banned account is named as banned, not as invalid.
        /// </summary>
        public void ConfigureIdentityRequirement(
            bool required, IdentityValidator validator, Func<string, bool> banCheck = null)
        {
            serverRequiresIdentity = required && validator != null;
            identityValidator = validator;
            identityBanCheck = banCheck;
        }

        /// <summary>The secret this client presents when challenged. Empty means "none".</summary>
        public void ConfigureClientSecret(string secret)
        {
            clientSecret = secret ?? string.Empty;
        }

        bool GateActive => serverRequiresSecret || serverRequiresIdentity;

        /// <summary>
        /// True when this client may receive world state. Always true while the gate is disabled,
        /// so existing callers need no knowledge of whether a secret is configured.
        /// </summary>
        public bool IsClientAuthorized(ulong clientId) =>
            !GateActive || authorizedClientIds.Contains(clientId);

        /// <summary>Verified Meta user id for an authorized client, when identity is required.</summary>
        public bool TryGetMetaUserId(ulong clientId, out ulong metaUserId) =>
            metaUserIdByClientId.TryGetValue(clientId, out metaUserId);

        void Awake()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();
        }

        void OnDestroy() => Unsubscribe();

        void Update()
        {
            EnsureSubscribed();
            RegisterMessageHandlers();

            while (mainThreadActions.TryDequeue(out Action action))
                action();

            ExpireUnansweredChallenges();
        }

        NetworkManager ResolveNetworkManagerOrNull()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            return session != null ? session.NetworkManager : null;
        }

        void EnsureSubscribed()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || subscribedNetworkManager == networkManager)
                return;

            Unsubscribe();
            subscribedNetworkManager = networkManager;
            subscribedNetworkManager.OnClientConnectedCallback += HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            subscribedNetworkManager.OnServerStopped += HandleSessionStopped;
            subscribedNetworkManager.OnClientStopped += HandleSessionStopped;
        }

        void Unsubscribe()
        {
            UnregisterMessageHandlers();

            if (subscribedNetworkManager == null)
                return;

            subscribedNetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            subscribedNetworkManager.OnServerStopped -= HandleSessionStopped;
            subscribedNetworkManager.OnClientStopped -= HandleSessionStopped;
            subscribedNetworkManager = null;
        }

        void RegisterMessageHandlers()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (messagesRegistered ||
                networkManager == null ||
                !networkManager.IsListening ||
                networkManager.CustomMessagingManager == null)
            {
                return;
            }

            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ChallengeMessage, HandleChallengeMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ResponseMessage, HandleResponseMessage);
            messagesRegistered = true;
        }

        void UnregisterMessageHandlers()
        {
            if (!messagesRegistered)
                return;

            messagesRegistered = false;

            if (subscribedNetworkManager == null || subscribedNetworkManager.CustomMessagingManager == null)
                return;

            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ChallengeMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ResponseMessage);
        }

        void HandleClientConnected(ulong clientId)
        {
            RegisterMessageHandlers();

            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || !networkManager.IsServer)
                return;

            // A host's own local client is the operator; challenging it would be a lock on the
            // inside of the front door.
            if (clientId == networkManager.LocalClientId)
            {
                authorizedClientIds.Add(clientId);
                return;
            }

            if (!GateActive)
            {
                authorizedClientIds.Add(clientId);
                ClientAuthorized?.Invoke(clientId);
                return;
            }

            var pending = new PendingChallenge
            {
                Nonce = BlockiverseServerAuthProtocol.CreateNonce(),
                Deadline = Time.unscaledTimeAsDouble + responseTimeoutSeconds,
            };
            pendingByClientId[clientId] = pending;

            int flags = (serverRequiresSecret ? ChallengeFlagSecretRequired : 0) |
                        (serverRequiresIdentity ? ChallengeFlagIdentityRequired : 0);

            using var writer = new FastBufferWriter(MessageBytes, Unity.Collections.Allocator.Temp);
            writer.WriteValueSafe(flags);
            writer.WriteValueSafe(BlockiverseServerAuthProtocol.NonceBytes);
            foreach (byte b in pending.Nonce)
                writer.WriteByteSafe(b);
            networkManager.CustomMessagingManager.SendNamedMessage(ChallengeMessage, clientId, writer);
        }

        void HandleClientDisconnected(ulong clientId)
        {
            pendingByClientId.Remove(clientId);
            authorizedClientIds.Remove(clientId);
            metaUserIdByClientId.Remove(clientId);
        }

        void HandleSessionStopped(bool _)
        {
            pendingByClientId.Clear();
            authorizedClientIds.Clear();
            metaUserIdByClientId.Clear();
            UnregisterMessageHandlers();
        }

        void HandleChallengeMessage(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            // Only the server may challenge.
            if (networkManager == null || senderClientId != NetworkManager.ServerClientId)
                return;

            reader.ReadValueSafe(out int flags);
            reader.ReadValueSafe(out int nonceLength);
            if (nonceLength != BlockiverseServerAuthProtocol.NonceBytes)
                return;

            var nonce = new byte[nonceLength];
            for (int i = 0; i < nonceLength; i++)
                reader.ReadByteSafe(out nonce[i]);

            bool identityWanted = (flags & ChallengeFlagIdentityRequired) != 0;

            if (identityWanted && IdentityProofSource != null)
            {
                // Proof fetch is a Platform SDK round trip; answer when it lands. The source may
                // call back off the main thread, so the send is queued rather than direct.
                ulong localClientId = networkManager.LocalClientId;
                IdentityProofSource.RequestProof((metaUserId, proofNonce) =>
                    mainThreadActions.Enqueue(() =>
                        SendChallengeResponse(nonce, localClientId, metaUserId, proofNonce)));
                return;
            }

            SendChallengeResponse(nonce, networkManager.LocalClientId, 0, null);
        }

        void SendChallengeResponse(byte[] nonce, ulong localClientId, ulong metaUserId, string proofNonce)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || !networkManager.IsListening ||
                networkManager.CustomMessagingManager == null)
            {
                return;
            }

            byte[] secretResponse = BlockiverseServerAuthProtocol.ComputeResponse(
                clientSecret, nonce, localClientId);

            using var writer = new FastBufferWriter(MessageBytes, Unity.Collections.Allocator.Temp);
            if (secretResponse == null)
            {
                // No secret configured on this client. An explicit zero-length answer lets the
                // server name the real problem (secret REQUIRED) instead of timing the client out
                // into a generic disconnect.
                writer.WriteValueSafe(0);
            }
            else
            {
                writer.WriteValueSafe(secretResponse.Length);
                foreach (byte b in secretResponse)
                    writer.WriteByteSafe(b);
            }

            bool hasIdentity = metaUserId != 0 && !string.IsNullOrEmpty(proofNonce);
            writer.WriteValueSafe(hasIdentity);
            if (hasIdentity)
            {
                writer.WriteValueSafe(metaUserId);
                writer.WriteValueSafe(proofNonce);
            }

            networkManager.CustomMessagingManager.SendNamedMessage(
                ResponseMessage, NetworkManager.ServerClientId, writer);
        }

        void HandleResponseMessage(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || !networkManager.IsServer)
                return;

            if (!pendingByClientId.TryGetValue(senderClientId, out PendingChallenge pending) ||
                pending.AwaitingIdentityValidation)
            {
                return;
            }

            reader.ReadValueSafe(out int responseLength);

            if (serverRequiresSecret)
            {
                if (responseLength == 0)
                {
                    Reject(networkManager, senderClientId, ReasonSecretRequired, "presented no secret");
                    return;
                }

                if (responseLength != BlockiverseServerAuthProtocol.ResponseBytes)
                {
                    Reject(networkManager, senderClientId, ReasonSecretRejected, "sent a malformed response");
                    return;
                }

                var response = new byte[responseLength];
                for (int i = 0; i < responseLength; i++)
                    reader.ReadByteSafe(out response[i]);

                if (!BlockiverseServerAuthProtocol.VerifyResponse(
                        serverSecret, pending.Nonce, senderClientId, response))
                {
                    Reject(networkManager, senderClientId, ReasonSecretRejected, "presented a wrong secret");
                    return;
                }
            }
            else if (responseLength > 0)
            {
                // Skip an unrequested secret block so the identity fields parse from the right
                // offset.
                if (responseLength != BlockiverseServerAuthProtocol.ResponseBytes)
                {
                    Reject(networkManager, senderClientId, ReasonSecretRejected, "sent a malformed response");
                    return;
                }

                for (int i = 0; i < responseLength; i++)
                    reader.ReadByteSafe(out byte _);
            }

            if (!serverRequiresIdentity)
            {
                Authorize(senderClientId);
                return;
            }

            reader.ReadValueSafe(out bool hasIdentity);
            if (!hasIdentity)
            {
                Reject(networkManager, senderClientId, ReasonIdentityRequired,
                    "presented no platform identity proof");
                return;
            }

            reader.ReadValueSafe(out ulong metaUserId);
            reader.ReadValueSafe(out string proofNonce);
            if (metaUserId == 0 || string.IsNullOrEmpty(proofNonce) || proofNonce.Length > MaxIdentityNonceChars)
            {
                Reject(networkManager, senderClientId, ReasonIdentityRejected,
                    "presented a malformed identity proof");
                return;
            }

            pending.AwaitingIdentityValidation = true;
            pending.ClaimedMetaUserId = metaUserId;

            // The validator's HTTP round trip may finish on any thread and after the client
            // disconnected; the queued completion re-checks pending state on the main thread.
            identityValidator(metaUserId, proofNonce, valid =>
                mainThreadActions.Enqueue(() => CompleteIdentityValidation(senderClientId, valid)));
        }

        void CompleteIdentityValidation(ulong clientId, bool valid)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || !networkManager.IsServer)
                return;

            if (!pendingByClientId.TryGetValue(clientId, out PendingChallenge pending) ||
                !pending.AwaitingIdentityValidation)
            {
                return;
            }

            if (!valid)
            {
                Reject(networkManager, clientId, ReasonIdentityRejected,
                    $"claimed Meta user {pending.ClaimedMetaUserId} but the proof did not validate");
                return;
            }

            if (identityBanCheck != null && identityBanCheck($"meta:{pending.ClaimedMetaUserId}"))
            {
                Reject(networkManager, clientId, ReasonBanned,
                    $"is banned (meta:{pending.ClaimedMetaUserId})");
                return;
            }

            metaUserIdByClientId[clientId] = pending.ClaimedMetaUserId;
            BlockiverseLog.Info(
                BlockiverseLogCategory.Networking,
                $"Client {clientId} verified as Meta user {pending.ClaimedMetaUserId}.");
            Authorize(clientId);
        }

        void Authorize(ulong clientId)
        {
            pendingByClientId.Remove(clientId);
            authorizedClientIds.Add(clientId);
            BlockiverseLog.Info(
                BlockiverseLogCategory.Networking,
                $"Client {clientId} passed the join challenge.");
            ClientAuthorized?.Invoke(clientId);
        }

        void ExpireUnansweredChallenges()
        {
            if (pendingByClientId.Count == 0)
                return;

            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || !networkManager.IsServer)
                return;

            double now = Time.unscaledTimeAsDouble;
            List<ulong> expired = null;
            foreach (KeyValuePair<ulong, PendingChallenge> entry in pendingByClientId)
            {
                if (entry.Value.Deadline <= now)
                    (expired ??= new List<ulong>()).Add(entry.Key);
            }

            if (expired == null)
                return;

            foreach (ulong clientId in expired)
            {
                Reject(networkManager, clientId,
                    serverRequiresSecret ? ReasonSecretRequired : ReasonIdentityRequired,
                    "did not answer the join challenge in time");
            }
        }

        void Reject(NetworkManager networkManager, ulong clientId, string reason, string detail)
        {
            pendingByClientId.Remove(clientId);
            BlockiverseLog.Warning(
                BlockiverseLogCategory.Networking,
                $"Disconnecting client {clientId}: {detail}.");
            networkManager.DisconnectClient(clientId, reason);
        }
    }
}

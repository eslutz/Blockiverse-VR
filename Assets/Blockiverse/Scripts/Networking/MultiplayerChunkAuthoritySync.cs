using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blockiverse.Core;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Unity.Collections;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;
using ProfilerMarker = Unity.Profiling.ProfilerMarker;

namespace Blockiverse.Networking
{
    public enum BlockMutationSubmissionKind
    {
        CreativeDirect,
        SurvivalCommand,
        WorldSimulation,
    }

    public readonly struct ChunkAuthoritySyncDiagnostics
    {
        public ChunkAuthoritySyncDiagnostics(MultiplayerChunkAuthoritySync sync)
        {
            SentMutationRequestCount = sync.SentMutationRequestCount;
            ReceivedMutationRequestCount = sync.ReceivedMutationRequestCount;
            RateLimitedMutationRequestCount = sync.RateLimitedMutationRequestCount;
            BroadcastDeltaCount = sync.BroadcastDeltaCount;
            AppliedRemoteDeltaCount = sync.AppliedRemoteDeltaCount;
            AppliedChunkDeltaCount = sync.AppliedChunkDeltaCount;
            IgnoredOutOfOrderChunkDeltaCount = sync.IgnoredOutOfOrderChunkDeltaCount;
            SentLateJoinSnapshotCount = sync.SentLateJoinSnapshotCount;
            SentSnapshotBatchCount = sync.SentSnapshotBatchCount;
            AppliedSnapshotBatchCount = sync.AppliedSnapshotBatchCount;
            SentResyncRequestCount = sync.SentResyncRequestCount;
            ServedResyncRequestCount = sync.ServedResyncRequestCount;
            TimedOutMutationRequestCount = sync.TimedOutMutationRequestCount;
            RefusedPendingLimitMutationCount = sync.RefusedPendingLimitMutationCount;
            OutOfReachRejectedMutationCount = sync.OutOfReachRejectedMutationCount;
            SentEnvironmentSnapshotCount = sync.SentEnvironmentSnapshotCount;
            AppliedEnvironmentSnapshotCount = sync.AppliedEnvironmentSnapshotCount;
            AppliedGenerationSnapshotCount = sync.AppliedGenerationSnapshotCount;
            AppliedSnapshotBlockCount = sync.AppliedSnapshotBlockCount;
            ReceivedMutationRejectionCount = sync.ReceivedMutationRejectionCount;
            ConflictRejectedMutationCount = sync.ConflictRejectedMutationCount;
            AcceptedMutationResponseCount = sync.AcceptedMutationResponseCount;
            PendingMutationRequestCount = sync.PendingMutationRequestCount;
            LastSentMutationRequestId = sync.LastSentMutationRequestId;
            LastReceivedMutationRequestId = sync.LastReceivedMutationRequestId;
            LastCompletedMutationRequestId = sync.LastCompletedMutationRequestId;
            LastBroadcastChunkDeltaSequence = sync.LastBroadcastChunkDeltaSequence;
            LastAppliedChunkDeltaSequence = sync.LastAppliedChunkDeltaSequence;
        }

        public int SentMutationRequestCount { get; }
        public int ReceivedMutationRequestCount { get; }
        public int RateLimitedMutationRequestCount { get; }
        public int BroadcastDeltaCount { get; }
        public int AppliedRemoteDeltaCount { get; }
        public int AppliedChunkDeltaCount { get; }
        public int IgnoredOutOfOrderChunkDeltaCount { get; }
        public int SentLateJoinSnapshotCount { get; }
        public int SentSnapshotBatchCount { get; }
        public int AppliedSnapshotBatchCount { get; }
        public int SentResyncRequestCount { get; }
        public int ServedResyncRequestCount { get; }
        public int TimedOutMutationRequestCount { get; }
        public int RefusedPendingLimitMutationCount { get; }
        public int OutOfReachRejectedMutationCount { get; }
        public int SentEnvironmentSnapshotCount { get; }
        public int AppliedEnvironmentSnapshotCount { get; }
        public int AppliedGenerationSnapshotCount { get; }
        public int AppliedSnapshotBlockCount { get; }
        public int ReceivedMutationRejectionCount { get; }
        public int ConflictRejectedMutationCount { get; }
        public int AcceptedMutationResponseCount { get; }
        public int PendingMutationRequestCount { get; }
        public uint LastSentMutationRequestId { get; }
        public uint LastReceivedMutationRequestId { get; }
        public uint LastCompletedMutationRequestId { get; }
        public uint LastBroadcastChunkDeltaSequence { get; }
        public uint LastAppliedChunkDeltaSequence { get; }
    }

    [DisallowMultipleComponent]
    public sealed class MultiplayerChunkAuthoritySync : MonoBehaviour
    {
        const string MutationRequestMessage = "Blockiverse.ChunkAuthority.MutationRequest";
        const string MutationDeltaMessage = "Blockiverse.ChunkAuthority.MutationDelta";
        const string ChunkSnapshotMessage = "Blockiverse.ChunkAuthority.ChunkSnapshot";
        const string ChunkSnapshotBatchMessage = "Blockiverse.ChunkAuthority.ChunkSnapshotBatch";
        const string MutationResultMessage = "Blockiverse.ChunkAuthority.MutationResult";
        const string EnvironmentSnapshotMessage = "Blockiverse.ChunkAuthority.EnvironmentSnapshot";
        const string ResyncRequestMessage = "Blockiverse.ChunkAuthority.ResyncRequest";
        const int MutationRequestMessageBytes = 128;
        const int MutationDeltaMessageBytes = 160;
        const int MutationResultMessageBytes = 128;
        const int ResyncRequestMessageBytes = 16;
        public const int WorldSnapshotHeaderBytes = 80;
        public const int EnvironmentSnapshotBytes = 20;
        public const float EnvironmentResyncIntervalSeconds = 5.0f;
        const int SnapshotHeaderBytes = WorldSnapshotHeaderBytes;
        // The wire format is 3 ints of position plus 1 int of block id.
        // 12 (position) + 4 (block id) + 4 (block state, schema v1).
        //
        // The state is carried per block rather than as a separate sparse list because the batches
        // are ordered against the header by ReliableFragmentedSequenced, and a second message type
        // would need its own ordering guarantee against the completion routine. The cost is 25% on
        // a one-time late-join transfer whose size is already warned about above
        // LateJoinSnapshotWarningBlockCount.
        public const int SnapshotBlockBytes = 20;
        // snapshotId + batchIndex + batchCount + blockCount.
        public const int SnapshotBatchHeaderBytes = 16;

        /// <summary>
        /// Blocks per late-join batch. Unity Transport sizes its fragmentation stage to
        /// <c>MaxPayloadSize</c>, so one giant snapshot message is silently undeliverable once a
        /// played world accumulates edits — fluid flow, crop growth and snow settle all count as
        /// changed blocks. Batching keeps every message far below that ceiling regardless of how
        /// much the world has changed. 200 blocks ≈ 3.2 KB of payload.
        /// </summary>
        public const int SnapshotBatchMaxBlocks = 200;

        /// <summary>Changed-block count above which the host warns that late join is getting expensive (ruleset §14).</summary>
        public const int LateJoinSnapshotWarningBlockCount = 10_000;

        /// <summary>
        /// Batches queued per frame while streaming a late-join snapshot. The transport's send
        /// queue is bounded (`MaxPacketQueueSize`, 256 by default here) and each ~3.2 KB batch
        /// fragments into several packets, so a synchronous burst over a large world overflows
        /// the queue and *drops* batches. A dropped batch is invisible to the sender: the client
        /// simply never reaches `batchCount`, stalls, resyncs, and receives the same oversized
        /// burst again — an infinite loop rather than an error. Pacing keeps each frame's burst
        /// far below the queue and lets the transport drain between frames.
        /// </summary>
        public const int SnapshotBatchesPerFrame = 8;

        const int HostMutationRateLimitMaxRequests = 30;
        const double HostMutationRateLimitWindowSeconds = 1.0d;
        // A resync re-sends the whole world, so it is budgeted far more tightly than edits.
        const int HostResyncRateLimitMaxRequests = 2;
        const double HostResyncRateLimitWindowSeconds = 10.0d;

        /// <summary>Maximum unanswered client mutation requests before new edits are refused locally (ruleset §14).</summary>
        public const int MaxPendingMutationRequests = 64;

        /// <summary>How long a pending request may go unanswered before the client gives up on it (ruleset §7.5).</summary>
        public const float PendingMutationRequestTimeoutSeconds = 1.5f;

        /// <summary>How long a sequence gap may persist before the client asks the host to resync.</summary>
        public const float ChunkDeltaGapTimeoutSeconds = 1.5f;

        /// <summary>Minimum spacing between client-initiated resync requests.</summary>
        public const float ResyncRequestCooldownSeconds = 5.0f;

        /// <summary>
        /// How long a snapshot may sit waiting for the rest of its batches before the client gives
        /// up and asks for a fresh one. Reliable delivery makes a lost batch unlikely, but a
        /// malformed or rejected batch would otherwise leave the client waiting forever with no
        /// world and no way to ask again.
        /// </summary>
        public const float SnapshotBatchStallTimeoutSeconds = 20.0f;

        /// <summary>Buffered out-of-order deltas retained before the client gives up and resyncs.</summary>
        public const int MaxBufferedChunkDeltas = 256;

        static readonly ProfilerMarker TrySubmitMutationMarker = new("Blockiverse.ChunkAuthority.TrySubmitMutation");
        static readonly ProfilerMarker HandleMutationRequestMarker = new("Blockiverse.ChunkAuthority.HandleMutationRequest");
        static readonly ProfilerMarker HandleMutationDeltaMarker = new("Blockiverse.ChunkAuthority.HandleMutationDelta");
        static readonly ProfilerMarker HandleSnapshotMarker = new("Blockiverse.ChunkAuthority.HandleSnapshot");
        static readonly ProfilerMarker GenerateSnapshotWorldMarker = new("Blockiverse.ChunkAuthority.GenerateSnapshotWorld");
        static readonly ProfilerMarker FinalizeSnapshotMarker = new("Blockiverse.ChunkAuthority.FinalizeSnapshot");
        static readonly ProfilerMarker SendMutationRequestMarker = new("Blockiverse.ChunkAuthority.SendMutationRequest");
        static readonly ProfilerMarker BroadcastDeltaMarker = new("Blockiverse.ChunkAuthority.BroadcastDelta");
        static readonly ProfilerMarker SendLateJoinSnapshotMarker = new("Blockiverse.ChunkAuthority.SendLateJoinSnapshot");
        static readonly ProfilerMarker SendEnvironmentSnapshotMarker = new("Blockiverse.ChunkAuthority.SendEnvironmentSnapshot");
        static readonly ProfilerMarker BroadcastEnvironmentSnapshotMarker = new("Blockiverse.ChunkAuthority.BroadcastEnvironmentSnapshot");
        static readonly ProfilerMarker TryApplyChunkDeltaMarker = new("Blockiverse.ChunkAuthority.TryApplyChunkDelta");
        static readonly ProfilerMarker ApplyBufferedChunkDeltasMarker = new("Blockiverse.ChunkAuthority.ApplyBufferedChunkDeltas");

        // WeatherState (int) + ticksInCurrentState (int) + weatherRng (uint) + totalElapsedTicks (long) = 20 bytes
        const int EnvironmentSnapshotMessageBytes = EnvironmentSnapshotBytes;

        [SerializeField] BlockiverseNetworkSession session;
        IMultiplayerWorldContext worldManager;

        readonly Dictionary<uint, PendingMutationRequest> pendingMutationRequests = new();
        readonly List<uint> expiredMutationRequestScratch = new();
        readonly PerClientRequestRateLimiter hostMutationRateLimiter =
            new(HostMutationRateLimitMaxRequests, HostMutationRateLimitWindowSeconds);
        readonly PerClientRequestRateLimiter hostResyncRateLimiter =
            new(HostResyncRateLimitMaxRequests, HostResyncRateLimitWindowSeconds);
        readonly List<PendingChunkDeltaMessage> bufferedChunkDeltas = new();
        // Reused by SendToRemoteClients so each broadcast avoids a per-delta list allocation.
        readonly List<ulong> remoteClientIdsScratch = new();
        readonly ChunkDeltaLog chunkDeltaLog = new();
        // In-flight paced snapshot sends, keyed by receiving client so a resync supersedes the
        // stream it replaces instead of interleaving two snapshots on the same connection.
        readonly Dictionary<ulong, Coroutine> snapshotSendRoutines = new();
        NetworkManager subscribedNetworkManager;
        BlockMutationAuthority mutationAuthority;
        uint nextMutationRequestId = 1;
        uint nextSnapshotId = 1;
        bool messagesRegistered;
        bool hasHostGenerationSnapshotForSession;
        float environmentResyncTimer;
        float chunkDeltaGapTimer;
        float resyncRequestCooldownTimer;
        float clientRecoveryElapsedSeconds;
        Func<double> hostMutationTimeProvider;

        public ChunkAuthorityBoundary CurrentBoundary { get; private set; } = ChunkAuthorityBoundary.ForHost();
        public BlockMutationAuthority MutationAuthority => ResolveMutationAuthority();
        public BlockMutationResult LastMutationResult { get; private set; }
        public bool IsClientRequestMode => IsActiveClientOnly() && CurrentBoundary.MustRequestMutations;
        public ChunkAuthoritySyncDiagnostics Diagnostics => new(this);
        internal int SentMutationRequestCount { get; private set; }
        internal int ReceivedMutationRequestCount { get; private set; }
        internal int RateLimitedMutationRequestCount { get; private set; }
        internal int BroadcastDeltaCount { get; private set; }
        internal int AppliedRemoteDeltaCount { get; private set; }
        internal int AppliedChunkDeltaCount { get; private set; }
        internal int IgnoredOutOfOrderChunkDeltaCount { get; private set; }
        internal int SentLateJoinSnapshotCount { get; private set; }
        internal int SentSnapshotBatchCount { get; private set; }
        internal int AppliedSnapshotBatchCount { get; private set; }
        internal int SentResyncRequestCount { get; private set; }
        internal int ServedResyncRequestCount { get; private set; }
        internal int TimedOutMutationRequestCount { get; private set; }
        internal int RefusedPendingLimitMutationCount { get; private set; }
        internal int OutOfReachRejectedMutationCount { get; private set; }
        internal int SentEnvironmentSnapshotCount { get; private set; }
        internal int AppliedEnvironmentSnapshotCount { get; private set; }
        internal int AppliedGenerationSnapshotCount { get; private set; }
        internal int AppliedSnapshotBlockCount { get; private set; }
        internal int ReceivedMutationRejectionCount { get; private set; }
        internal int ConflictRejectedMutationCount { get; private set; }
        internal int AcceptedMutationResponseCount { get; private set; }
        internal int PendingMutationRequestCount => pendingMutationRequests.Count;
        internal uint LastSentMutationRequestId { get; private set; }
        internal uint LastReceivedMutationRequestId { get; private set; }
        internal uint LastCompletedMutationRequestId { get; private set; }
        internal uint LastBroadcastChunkDeltaSequence { get; private set; }
        internal uint LastAppliedChunkDeltaSequence { get; private set; }
        public IReadOnlyList<ChunkDelta> RecordedChunkDeltas => chunkDeltaLog.Deltas;
        public bool HasHostGenerationSnapshotForSession => hasHostGenerationSnapshotForSession;
        double HostMutationTimeSeconds => hostMutationTimeProvider?.Invoke() ?? Time.unscaledTimeAsDouble;

        enum ChunkDeltaApplyState
        {
            Applied,
            IgnoredStale,
            WaitingForEarlierDelta
        }

        public readonly struct WorldSnapshotHeader
        {
            public WorldSnapshotHeader(
                CreativeWorldGenerationPreset generationPreset,
                int width,
                int height,
                int depth,
                int chunkSize,
                int seed,
                int groundHeight,
                BlockPosition spawnPosition,
                uint hostDeltaSequence,
                int changedBlockCount,
                uint snapshotId = 0,
                int batchCount = 0)
            {
                GenerationPreset = generationPreset;
                Width = width;
                Height = height;
                Depth = depth;
                ChunkSize = chunkSize;
                Seed = seed;
                GroundHeight = groundHeight;
                SpawnPosition = spawnPosition;
                HostDeltaSequence = hostDeltaSequence;
                ChangedBlockCount = changedBlockCount;
                SnapshotId = snapshotId;
                BatchCount = batchCount;
            }

            public CreativeWorldGenerationPreset GenerationPreset { get; }
            public int Width { get; }
            public int Height { get; }
            public int Depth { get; }
            public int ChunkSize { get; }
            public int Seed { get; }
            public int GroundHeight { get; }
            public BlockPosition SpawnPosition { get; }
            public uint HostDeltaSequence { get; }
            public int ChangedBlockCount { get; }

            /// <summary>Identifies this snapshot so batches from a superseded one can be discarded.</summary>
            public uint SnapshotId { get; }

            /// <summary>How many <c>ChunkSnapshotBatch</c> messages follow this header.</summary>
            public int BatchCount { get; }
        }

        public readonly struct EnvironmentSnapshotState
        {
            public EnvironmentSnapshotState(
                WeatherState weatherState,
                int weatherTicks,
                uint weatherRngState,
                long worldTimeTicks)
            {
                WeatherState = weatherState;
                WeatherTicks = weatherTicks;
                WeatherRngState = weatherRngState;
                WorldTimeTicks = worldTimeTicks;
            }

            public WeatherState WeatherState { get; }
            public int WeatherTicks { get; }
            public uint WeatherRngState { get; }
            public long WorldTimeTicks { get; }
        }

        public void Configure(BlockiverseNetworkSession targetSession, IMultiplayerWorldContext targetWorldManager)
        {
            UnsubscribeNetworkCallbacks();
            session = targetSession;
            worldManager = targetWorldManager;
            if (worldManager != null)
                worldManager.ConfigureAuthoritySync(this);
            SubscribeNetworkCallbacks();
            RefreshAuthorityBoundary();
        }

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
            SubscribeNetworkCallbacks();
            RefreshAuthorityBoundary();

            // Resume polling a snapshot that was still in flight when the component was disabled.
            if (pendingSnapshot != null && snapshotRoutine == null)
                snapshotRoutine = StartCoroutine(CompleteSnapshotWhenReady());
        }

        void OnDisable()
        {
            StopAllSnapshotSends();

            // Stop the snapshot poll explicitly and clear the handle: a stale non-null handle
            // would block StartSnapshotGeneration's null-check forever after a re-enable.
            if (snapshotRoutine != null)
            {
                StopCoroutine(snapshotRoutine);
                snapshotRoutine = null;
            }

            UnsubscribeNetworkCallbacks();
        }

        void OnDestroy()
        {
            UnsubscribeNetworkCallbacks();

            if (authGate != null && authGateSubscribed)
            {
                authGate.ClientAuthorized -= HandleClientAuthorized;
                authGateSubscribed = false;
            }
        }

        void Update()
        {
            TickEnvironmentResync(Time.unscaledDeltaTime);
            TickChunkDeltaRecovery(Time.unscaledDeltaTime);
        }

        public void TickEnvironmentResync(float deltaSeconds)
        {
            RefreshAuthorityBoundary();
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer ||
                !CurrentBoundary.CanServeLateJoinSync ||
                // Count REMOTE peers, not entries. A host appears in its own ConnectedClientsIds,
                // a dedicated server does not, so `<= 1` silently means "never resync" for a
                // server with exactly one player -- weather and time then drift uncorrected.
                CountRemoteClients(networkManager) < 1)
            {
                environmentResyncTimer = 0.0f;
                return;
            }

            environmentResyncTimer += Mathf.Max(0.0f, deltaSeconds);
            if (environmentResyncTimer < EnvironmentResyncIntervalSeconds)
                return;

            environmentResyncTimer = 0.0f;
            BroadcastEnvironmentSnapshot();
        }

        public BlockMutationResult TrySubmitMutation(
            BlockMutationRequest request,
            out SetBlockCommand appliedCommand,
            out bool requestSentToHost,
            BlockMutationSubmissionKind submissionKind = BlockMutationSubmissionKind.CreativeDirect)
        {
            using ProfilerMarker.AutoScope scope = TrySubmitMutationMarker.Auto();

            appliedCommand = null;
            requestSentToHost = false;
            RefreshAuthorityBoundary();

            if (submissionKind == BlockMutationSubmissionKind.CreativeDirect &&
                worldManager != null &&
                !CreativePermissionPolicy.CanSubmitDirectCreativeMutation(worldManager.GameMode))
            {
                LastMutationResult = BlockMutationResult.Reject(
                    BlockMutationRejectionReason.GameModeForbidsDirectMutation,
                    ChunkCoordinate.FromBlockPosition(request.Position, ResolveMutationChunkSize()),
                    "Survival worlds accept block edits only through validated survival commands.");
                return LastMutationResult;
            }

            if (IsClientRequestMode)
            {
                if (!hasHostGenerationSnapshotForSession)
                {
                    int chunkSize = worldManager != null && worldManager.World != null
                        ? worldManager.World.ChunkSize
                        : 16;
                    LastMutationResult = BlockMutationResult.Reject(
                        BlockMutationRejectionReason.HostOnlyAuthorityOperation,
                        ChunkCoordinate.FromBlockPosition(request.Position, chunkSize),
                        "Client is waiting for the host-owned world generation snapshot before sending chunk mutations.");
                    return LastMutationResult;
                }

                // Ruleset §14: a client that keeps editing while the host is unreachable would
                // otherwise grow an unbounded pending set that no reply will ever drain.
                if (pendingMutationRequests.Count >= MaxPendingMutationRequests)
                {
                    RefusedPendingLimitMutationCount++;
                    LastMutationResult = BlockMutationResult.Reject(
                        BlockMutationRejectionReason.PendingRequestLimitReached,
                        ChunkCoordinate.FromBlockPosition(request.Position, ResolveMutationChunkSize()),
                        "Too many block edits are still awaiting host validation.");
                    return LastMutationResult;
                }

                uint requestId = AllocateMutationRequestId();
                SendMutationRequest(requestId, request);
                requestSentToHost = true;
                LastMutationResult = BlockMutationResult.RequestSent(
                    ChunkCoordinate.FromBlockPosition(request.Position, ResolveWorld().ChunkSize),
                    requestId);
                return LastMutationResult;
            }

            BlockMutationAuthority authority = ResolveMutationAuthority();
            BlockMutationResult result = authority.TryCommit(request, out appliedCommand);
            LastMutationResult = result;

            if (result.Accepted)
                BroadcastDelta(result.Change);

            return result;
        }

        public BlockMutationResult TrySubmitMutation(
            BlockPosition position,
            BlockId newBlock,
            out SetBlockCommand appliedCommand,
            out bool requestSentToHost,
            BlockMutationSubmissionKind submissionKind = BlockMutationSubmissionKind.CreativeDirect)
        {
            RefreshAuthorityBoundary();
            VoxelWorld world = ResolveWorld();
            var request = world.Bounds.Contains(position)
                ? new BlockMutationRequest(CurrentBoundary.LocalClientId, position, newBlock, world.GetBlock(position))
                : new BlockMutationRequest(CurrentBoundary.LocalClientId, position, newBlock);
            return TrySubmitMutation(request, out appliedCommand, out requestSentToHost, submissionKind);
        }

        public bool CanSaveMultiplayerWorld()
        {
            RefreshAuthorityBoundary();
            return CurrentBoundary.CanSaveMultiplayerWorld;
        }

        void HandleServerStarted()
        {
            RefreshAuthorityBoundary();
            hostMutationRateLimiter.Clear();
            hostResyncRateLimiter.Clear();
            ResetHostChunkDeltaLog();
            RegisterMessageHandlers();
        }

        void HandleClientStarted()
        {
            RefreshAuthorityBoundary();

            if (CurrentBoundary.MustRequestMutations)
            {
                hasHostGenerationSnapshotForSession = false;
                ResetClientChunkDeltaState();
                ResetPendingMutationRequests();
                ResetClientRecoveryState();
            }

            RegisterMessageHandlers();
        }

        void HandleClientConnected(ulong clientId)
        {
            RefreshAuthorityBoundary();
            RegisterMessageHandlers();

            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsServer ||
                clientId == networkManager.LocalClientId ||
                !CurrentBoundary.CanServeLateJoinSync)
            {
                return;
            }

            // On a secret-protected server the world snapshot is withheld until the client passes
            // the join-secret challenge -- otherwise an unauthenticated connection receives the
            // entire world before being disconnected, and the secret protects nothing that
            // matters. The gate raises ClientAuthorized (subscribed below) once the challenge
            // completes, and disconnects the client itself on failure or timeout.
            BlockiverseServerAuthGate gate = ResolveAuthGateOrNull();
            if (gate != null && !gate.IsClientAuthorized(clientId))
                return;

            SendLateJoinSnapshot(clientId);
            SendEnvironmentSnapshot(clientId);
        }

        BlockiverseServerAuthGate authGate;
        bool authGateSubscribed;

        /// <summary>Server-side only: a client mid-challenge (or failed) may not act.</summary>
        bool IsSenderUnauthorized(ulong senderClientId)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || !networkManager.IsServer)
                return false;

            BlockiverseServerAuthGate gate = ResolveAuthGateOrNull();
            return gate != null && !gate.IsClientAuthorized(senderClientId);
        }

        BlockiverseServerAuthGate ResolveAuthGateOrNull()
        {
            if (authGate == null)
                authGate = GetComponent<BlockiverseServerAuthGate>();

            if (authGate != null && !authGateSubscribed)
            {
                authGate.ClientAuthorized += HandleClientAuthorized;
                authGateSubscribed = true;
            }

            return authGate;
        }

        void HandleClientAuthorized(ulong clientId)
        {
            RefreshAuthorityBoundary();

            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsServer ||
                clientId == networkManager.LocalClientId ||
                !CurrentBoundary.CanServeLateJoinSync ||
                !IsClientConnected(networkManager, clientId))
            {
                return;
            }

            SendLateJoinSnapshot(clientId);
            SendEnvironmentSnapshot(clientId);
        }

        void HandleServerStopped(bool wasHost)
        {
            StopAllSnapshotSends();
            hostMutationRateLimiter.Clear();
            hostResyncRateLimiter.Clear();
            UnregisterMessageHandlers();
            RefreshAuthorityBoundary();
        }

        void HandleClientStopped(bool wasHost)
        {
            hasHostGenerationSnapshotForSession = false;
            // Drop any in-flight snapshot generation; its completion routine self-terminates
            // via the null pendingSnapshot, so no StopCoroutine is needed here.
            ObserveAbandonedSnapshotTask(pendingSnapshot);
            pendingSnapshot = null;
            snapshotRoutine = null;
            StopAllSnapshotSends();
            hostMutationRateLimiter.Clear();
            hostResyncRateLimiter.Clear();
            ResetClientChunkDeltaState();
            ResetPendingMutationRequests();
            ResetClientRecoveryState();
            UnregisterMessageHandlers();
            RefreshAuthorityBoundary();
        }

        void HandleClientDisconnected(ulong clientId)
        {
            hostMutationRateLimiter.RemoveClient(clientId);
            hostResyncRateLimiter.RemoveClient(clientId);
            StopSnapshotSend(clientId);
        }

        void StopSnapshotSend(ulong clientId)
        {
            if (!snapshotSendRoutines.TryGetValue(clientId, out Coroutine routine))
                return;

            if (routine != null)
                StopCoroutine(routine);

            snapshotSendRoutines.Remove(clientId);
        }

        // Paced sends outlive a single frame, so every path that ends the session has to stop
        // them; otherwise a coroutine keeps writing to a transport that is shutting down.
        void StopAllSnapshotSends()
        {
            foreach (KeyValuePair<ulong, Coroutine> pending in snapshotSendRoutines)
            {
                if (pending.Value != null)
                    StopCoroutine(pending.Value);
            }

            snapshotSendRoutines.Clear();
        }

        void HandleMutationRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            using ProfilerMarker.AutoScope scope = HandleMutationRequestMarker.Auto();

            // A client that has not passed the join-secret challenge gets no say in the world.
            // Silent drop, not a response: the gate is about to disconnect it anyway, and a reply
            // channel to an unauthenticated peer is attack surface.
            if (IsSenderUnauthorized(senderClientId))
                return;

            RefreshAuthorityBoundary();

            if (!CurrentBoundary.OwnsMutationValidation)
            {
                LastMutationResult = BlockMutationResult.Reject(
                    BlockMutationRejectionReason.HostOnlyAuthorityOperation,
                    default,
                    "Only the host can validate client block mutation requests.");
                return;
            }

            if (!hostMutationRateLimiter.TryConsume(senderClientId, HostMutationTimeSeconds))
            {
                RateLimitedMutationRequestCount++;
                return;
            }

            // Malformed payloads (negative block ids) are dropped — the same posture as an
            // unregistered message; nothing legitimate sends them.
            if (!TryReadMutationRequest(senderClientId, ref reader, out uint requestId, out BlockMutationRequest request))
                return;

            ReceivedMutationRequestCount++;
            LastReceivedMutationRequestId = requestId;

            // Survival worlds accept edits only through validated survival commands
            // (harvest/place/till/…). The raw creative channel would bypass inventory,
            // tool-tier, durability, and world-mode permissions, so it is denied for
            // everyone, including the host.
            if (worldManager != null && !CreativePermissionPolicy.CanSubmitDirectCreativeMutation(worldManager.GameMode))
            {
                BlockMutationResult gameModeRejection = BlockMutationResult.Reject(
                    BlockMutationRejectionReason.GameModeForbidsDirectMutation,
                    ChunkCoordinate.FromBlockPosition(request.Position, ResolveWorld().ChunkSize),
                    "Survival worlds accept block edits only through validated survival commands.",
                    requestId);
                LastMutationResult = gameModeRejection;
                SendMutationResult(senderClientId, requestId, request, gameModeRejection);
                return;
            }
            // Ruleset §16: the host does not take a client's word for where it is standing. A
            // modified client could otherwise edit anywhere in the world from spawn.
            if (!IsWithinHostValidatedReach(senderClientId, request.Position))
            {
                OutOfReachRejectedMutationCount++;
                BlockMutationResult reachRejection = BlockMutationResult.Reject(
                    BlockMutationRejectionReason.OutOfReach,
                    ChunkCoordinate.FromBlockPosition(request.Position, ResolveWorld().ChunkSize),
                    "Block is out of the requesting player's interaction reach.",
                    requestId);
                LastMutationResult = reachRejection;
                SendMutationResult(senderClientId, requestId, request, reachRejection);
                return;
            }

            BlockMutationResult result = ResolveMutationAuthority().TryCommit(request, out _).WithRpcRequestId(requestId);
            LastMutationResult = result;

            if (result.Accepted)
            {
                BroadcastDelta(result.Change, request.RequestingClientId, requestId);
            }
            else
            {
                if (result.RejectionReason == BlockMutationRejectionReason.ExpectedBlockMismatch)
                    ConflictRejectedMutationCount++;

                SendMutationResult(senderClientId, requestId, request, result);
            }
        }

        void HandleMutationDeltaMessage(ulong senderClientId, FastBufferReader reader)
        {
            using ProfilerMarker.AutoScope scope = HandleMutationDeltaMarker.Auto();

            RefreshAuthorityBoundary();

            if (senderClientId != CurrentBoundary.HostClientId || !CurrentBoundary.MustRequestMutations)
                return;

            ChunkDelta delta = ReadMutationDelta(ref reader, out ulong requestingClientId, out uint requestId);

            if (!hasHostGenerationSnapshotForSession)
            {
                BufferChunkDeltaMessage(new PendingChunkDeltaMessage(requestingClientId, requestId, delta));
                return;
            }

            ApplyChunkDeltaMessageOrBuffer(new PendingChunkDeltaMessage(requestingClientId, requestId, delta));
            ApplyBufferedChunkDeltas();
        }

        void HandleChunkSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            using ProfilerMarker.AutoScope scope = HandleSnapshotMarker.Auto();

            RefreshAuthorityBoundary();

            if (senderClientId != CurrentBoundary.HostClientId || !CurrentBoundary.MustRequestMutations)
                return;

            // The header arrives on its own; changed blocks follow as bounded batch messages.
            // World generation starts immediately and runs on a background task — regenerating a
            // full survival world synchronously would stall the VR main thread for seconds — so
            // generation and batch transfer overlap.
            if (!TryReadWorldSnapshotHeader(ref reader, out WorldSnapshotHeader header))
                return;

            var settings = new WorldGenerationSettings(
                header.Width,
                header.Height,
                header.Depth,
                header.ChunkSize,
                header.Seed,
                header.GroundHeight,
                header.SpawnPosition);
            StartSnapshotGeneration(
                header.GenerationPreset,
                settings,
                header.HostDeltaSequence,
                header.SnapshotId,
                header.BatchCount,
                header.ChangedBlockCount);
        }

        void HandleChunkSnapshotBatchMessage(ulong senderClientId, FastBufferReader reader)
        {
            using ProfilerMarker.AutoScope scope = HandleSnapshotMarker.Auto();

            RefreshAuthorityBoundary();

            if (senderClientId != CurrentBoundary.HostClientId || !CurrentBoundary.MustRequestMutations)
                return;

            if (reader.Length - reader.Position < SnapshotBatchHeaderBytes)
                return;

            reader.ReadValueSafe(out uint snapshotId);
            reader.ReadValueSafe(out int batchIndex);
            reader.ReadValueSafe(out int batchCount);
            reader.ReadValueSafe(out int blockCount);

            PendingWorldSnapshot snapshot = pendingSnapshot;

            // Batches from a superseded snapshot (or arriving with no snapshot in flight) are
            // dropped: the header that replaced it also reset the block set they belong to.
            if (snapshot == null || snapshot.SnapshotId != snapshotId)
                return;

            if (batchIndex < 0 ||
                batchCount != snapshot.BatchCount ||
                blockCount < 0 ||
                blockCount > SnapshotBatchMaxBlocks ||
                reader.Length - reader.Position < blockCount * SnapshotBlockBytes)
            {
                return;
            }

            for (int index = 0; index < blockCount; index++)
            {
                BlockPosition position = ReadBlockPosition(ref reader);
                reader.ReadValueSafe(out int blockId);
                reader.ReadValueSafe(out int blockState);
                snapshot.Blocks.Add((position, blockId, blockState));
            }

            snapshot.ReceivedBatchCount++;
            snapshot.WaitingForBatchesSeconds = 0.0f;
            AppliedSnapshotBatchCount++;
        }

        // The in-flight late-join snapshot. Only the newest one matters: a fresh snapshot
        // replaces the pending entry and the completion routine drops superseded results.
        sealed class PendingWorldSnapshot
        {
            public CreativeWorldGenerationPreset Preset;
            public WorldGenerationSettings Settings;
            public uint HostDeltaSequence;
            public uint SnapshotId;
            public int BatchCount;
            public int ReceivedBatchCount;
            public float WaitingForBatchesSeconds;
            public List<(BlockPosition position, int blockId, int blockState)> Blocks;
            public Task<GeneratedSnapshotWorld> GenerationTask;

            public bool HasAllBatches => ReceivedBatchCount >= BatchCount;
        }

        sealed class GeneratedSnapshotWorld
        {
            public BlockRegistry Registry;
            public VoxelWorld World;
            public IReadOnlyList<StructureContainerLoot> ContainerLoot;
        }

        PendingWorldSnapshot pendingSnapshot;
        Coroutine snapshotRoutine;

        void StartSnapshotGeneration(
            CreativeWorldGenerationPreset preset,
            WorldGenerationSettings settings,
            uint hostDeltaSequence,
            uint snapshotId,
            int batchCount,
            int changedBlockCount)
        {
            // A newer snapshot supersedes any in-flight generation; observe the abandoned
            // task so a failed run cannot surface later as an UnobservedTaskException.
            ObserveAbandonedSnapshotTask(pendingSnapshot);

            pendingSnapshot = new PendingWorldSnapshot
            {
                Preset = preset,
                Settings = settings,
                HostDeltaSequence = hostDeltaSequence,
                SnapshotId = snapshotId,
                BatchCount = batchCount,
                ReceivedBatchCount = 0,
                Blocks = new List<(BlockPosition position, int blockId, int blockState)>(changedBlockCount),
                // World generation is pure C# over engine-free types, safe off the main thread.
                GenerationTask = Task.Run(() => GenerateSnapshotWorld(preset, settings)),
            };

            if (snapshotRoutine == null)
                snapshotRoutine = StartCoroutine(CompleteSnapshotWhenReady());
        }

        static void ObserveAbandonedSnapshotTask(PendingWorldSnapshot snapshot)
        {
            if (snapshot == null || snapshot.GenerationTask == null)
                return;

            // The continuation runs on a thread pool thread; the Unity debug sink is thread-safe.
            _ = snapshot.GenerationTask.ContinueWith(
                task => BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    $"Abandoned world snapshot generation faulted: {task.Exception?.GetBaseException()}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        static GeneratedSnapshotWorld GenerateSnapshotWorld(
            CreativeWorldGenerationPreset preset,
            WorldGenerationSettings settings)
        {
            using ProfilerMarker.AutoScope scope = GenerateSnapshotWorldMarker.Auto();

            BlockRegistry registry = BlockRegistry.Default;
            GeneratedCreativeWorld generated = WorldSaveGeneration.GenerateWorld(preset, registry, settings);
            return new GeneratedSnapshotWorld
            {
                Registry = generated.Registry,
                World = generated.World,
                ContainerLoot = generated.ContainerLoot,
            };
        }

        IEnumerator CompleteSnapshotWhenReady()
        {
            while (true)
            {
                PendingWorldSnapshot current = pendingSnapshot;

                if (current == null)
                {
                    snapshotRoutine = null;
                    yield break;
                }

                // Both halves must land: the regenerated base world and every changed-block batch.
                // Finalizing on generation alone would apply a partial delta set and leave the
                // client quietly diverged from the host.
                if (current.GenerationTask.IsCompleted && current.HasAllBatches)
                {
                    pendingSnapshot = null;
                    snapshotRoutine = null;
                    FinalizeSnapshot(current);
                    yield break;
                }

                yield return null;
            }
        }

        void FinalizeSnapshot(PendingWorldSnapshot snapshot)
        {
            using ProfilerMarker.AutoScope scope = FinalizeSnapshotMarker.Auto();

            if (snapshot.GenerationTask.IsFaulted)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    "Failed to regenerate the host world snapshot on the client.",
                    snapshot.GenerationTask.Exception?.GetBaseException(),
                    this);
                return;
            }

            GeneratedSnapshotWorld generated = snapshot.GenerationTask.Result;
            worldManager.InitializeGeneratedWorld(
                generated.Registry,
                snapshot.Settings,
                generated.World,
                snapshot.Preset,
                generated.ContainerLoot);

            // Batch the renderer rebuild: applying the snapshot block-by-block would otherwise
            // rebuild every dirty chunk mesh once per block (O(blocks × rebuild)).
            foreach ((BlockPosition position, int blockId, int blockState) in snapshot.Blocks)
            {
                if (blockId < 0)
                    continue;

                ApplyAuthoritativeBlock(
                    position, new BlockId(blockId), trackChange: false, rebuildRenderer: false,
                    blockState: blockState);
                AppliedSnapshotBlockCount++;
            }

            if (snapshot.Blocks.Count > 0 && worldManager.Renderer != null)
                worldManager.Renderer.RebuildDirty();

            LastAppliedChunkDeltaSequence = snapshot.HostDeltaSequence;
            hasHostGenerationSnapshotForSession = true;
            AppliedGenerationSnapshotCount++;
            ApplyBufferedChunkDeltas();
        }

        void HandleMutationResultMessage(ulong senderClientId, FastBufferReader reader)
        {
            RefreshAuthorityBoundary();

            if (senderClientId != CurrentBoundary.HostClientId || !CurrentBoundary.MustRequestMutations)
                return;

            reader.ReadValueSafe(out uint requestId);
            BlockPosition position = ReadBlockPosition(ref reader);
            reader.ReadValueSafe(out int rejectionReason);
            reader.ReadValueSafe(out bool hasAuthoritativeBlock);
            reader.ReadValueSafe(out int authoritativeBlock);
            reader.ReadValueSafe(out int authoritativeBlockState);

            ChunkCoordinate chunk = ChunkCoordinate.FromBlockPosition(position, ResolveWorld().ChunkSize);
            LastMutationResult = BlockMutationResult.Reject(
                (BlockMutationRejectionReason)rejectionReason,
                chunk,
                "Host rejected the block mutation request.",
                requestId);
            ReceivedMutationRejectionCount++;
            TryCompletePendingMutationRequest(CurrentBoundary.LocalClientId, requestId);

            if (hasAuthoritativeBlock)
            {
                // The rejection correction is the third path that overwrites a client block, and it
                // must carry state for the same reason the other two do — otherwise a rejected
                // request silently strips the Persistent bit off whatever was already there.
                ApplyAuthoritativeBlock(
                    position, new BlockId(authoritativeBlock), trackChange: false,
                    blockState: authoritativeBlockState);
            }
        }

        void SendMutationRequest(uint requestId, BlockMutationRequest request)
        {
            using ProfilerMarker.AutoScope scope = SendMutationRequestMarker.Auto();

            NetworkManager networkManager = ResolveNetworkManager();
            RegisterMessageHandlers();
            pendingMutationRequests[requestId] = new PendingMutationRequest(request, clientRecoveryElapsedSeconds);
            LastSentMutationRequestId = requestId;

            var writer = new FastBufferWriter(MutationRequestMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(requestId);
                WriteBlockPosition(ref writer, request.Position);
                writer.WriteValueSafe(request.NewBlock.Value);
                writer.WriteValueSafe(request.HasExpectedCurrentBlock);
                writer.WriteValueSafe(request.ExpectedCurrentBlock.Value);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MutationRequestMessage,
                    NetworkManager.ServerClientId,
                    writer);
                SentMutationRequestCount++;
            }
            finally
            {
                writer.Dispose();
            }
        }

        void BroadcastDelta(BlockChange change, ulong requestingClientId = 0, uint requestId = 0)
        {
            using ProfilerMarker.AutoScope scope = BroadcastDeltaMarker.Auto();

            RefreshAuthorityBoundary();

            if (!CurrentBoundary.CanBroadcastDeltas)
                return;

            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
                return;

            RegisterMessageHandlers();

            var writer = new FastBufferWriter(MutationDeltaMessageBytes, Allocator.Temp);

            try
            {
                VoxelWorld world = ResolveWorld();
                ChunkDelta delta = chunkDeltaLog.Record(
                    change, world.ChunkSize, world.GetBlockState(change.Position));
                LastBroadcastChunkDeltaSequence = delta.SequenceId;
                writer.WriteValueSafe(requestingClientId);
                writer.WriteValueSafe(requestId);
                WriteChunkDelta(ref writer, delta);
                SendToRemoteClients(MutationDeltaMessage, writer);
                BroadcastDeltaCount++;
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendMutationResult(ulong clientId, uint requestId, BlockMutationRequest request, BlockMutationResult result)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer ||
                clientId == networkManager.LocalClientId)
            {
                return;
            }

            RegisterMessageHandlers();
            VoxelWorld world = ResolveWorld();
            BlockPosition position = request.Position;
            bool hasAuthoritativeBlock = world.Bounds.Contains(position);
            BlockId authoritativeBlock = hasAuthoritativeBlock ? world.GetBlock(position) : default;
            int authoritativeBlockState = hasAuthoritativeBlock ? world.GetBlockState(position) : BlockState.Default;
            var writer = new FastBufferWriter(MutationResultMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(requestId);
                WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe((int)result.RejectionReason);
                writer.WriteValueSafe(hasAuthoritativeBlock);
                writer.WriteValueSafe(authoritativeBlock.Value);
                writer.WriteValueSafe(authoritativeBlockState);
                networkManager.CustomMessagingManager.SendNamedMessage(MutationResultMessage, clientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        uint AllocateMutationRequestId()
        {
            uint requestId = nextMutationRequestId++;

            if (nextMutationRequestId == 0)
                nextMutationRequestId = 1;

            return requestId;
        }

        bool TryCompletePendingMutationRequest(ulong requestingClientId, uint requestId)
        {
            if (requestId == 0 || requestingClientId != CurrentBoundary.LocalClientId)
                return false;
            if (!pendingMutationRequests.Remove(requestId))
                return false;

            LastCompletedMutationRequestId = requestId;
            return true;
        }

        // Cleared alongside the delta bookkeeping whenever a session starts or ends, so a stale
        // resync cooldown from a previous session cannot suppress the first recovery of the next.
        void ResetClientRecoveryState()
        {
            clientRecoveryElapsedSeconds = 0.0f;
            resyncRequestCooldownTimer = 0.0f;
            chunkDeltaGapTimer = 0.0f;
        }

        void ResetPendingMutationRequests()
        {
            pendingMutationRequests.Clear();
            nextMutationRequestId = 1;
            LastSentMutationRequestId = 0;
            LastCompletedMutationRequestId = 0;
        }

        void ResetClientChunkDeltaState()
        {
            bufferedChunkDeltas.Clear();
            LastAppliedChunkDeltaSequence = 0;
            chunkDeltaGapTimer = 0.0f;
        }

        /// <summary>
        /// Client-side recovery clock (ruleset §7.5 "Missing sequence recovery"). Advances the
        /// pending-request ages and the sequence-gap timer, and asks the host for a fresh world
        /// snapshot when either stalls. Exposed and driven by an explicit delta so tests can run
        /// it deterministically, mirroring <see cref="TickEnvironmentResync"/>.
        /// </summary>
        public void TickChunkDeltaRecovery(float deltaSeconds)
        {
            float step = Mathf.Max(0.0f, deltaSeconds);
            clientRecoveryElapsedSeconds += step;

            if (resyncRequestCooldownTimer > 0.0f)
                resyncRequestCooldownTimer = Mathf.Max(0.0f, resyncRequestCooldownTimer - step);

            RefreshAuthorityBoundary();

            if (!IsActiveClientOnly() || !CurrentBoundary.MustRequestMutations)
            {
                chunkDeltaGapTimer = 0.0f;
                return;
            }

            // Both are evaluated: || would short-circuit and leave the stall timer frozen for as
            // long as requests keep ageing out.
            bool requestsTimedOut = ExpireTimedOutMutationRequests();
            bool snapshotStalled = HasStalledSnapshotTransfer(step);
            bool needsResync = requestsTimedOut || snapshotStalled;

            // A buffered delta means an earlier sequence never arrived. Reliable delivery makes
            // that rare, but a handler that threw on the host, or a snapshot that landed stale,
            // leaves the client permanently one sequence behind with no way back on its own.
            if (hasHostGenerationSnapshotForSession && bufferedChunkDeltas.Count > 0)
            {
                chunkDeltaGapTimer += step;
                if (chunkDeltaGapTimer >= ChunkDeltaGapTimeoutSeconds)
                    needsResync = true;
            }
            else
            {
                chunkDeltaGapTimer = 0.0f;
            }

            if (needsResync)
                RequestWorldResync();
        }

        // A snapshot whose generation has finished but whose batches stopped arriving will never
        // complete on its own — the completion routine waits on both halves by design.
        bool HasStalledSnapshotTransfer(float deltaSeconds)
        {
            PendingWorldSnapshot snapshot = pendingSnapshot;

            if (snapshot == null || snapshot.HasAllBatches)
                return false;

            snapshot.WaitingForBatchesSeconds += deltaSeconds;

            if (snapshot.WaitingForBatchesSeconds < SnapshotBatchStallTimeoutSeconds)
                return false;

            BlockiverseLog.Warning(
                BlockiverseLogCategory.Networking,
                $"World snapshot {snapshot.SnapshotId} stalled after {snapshot.ReceivedBatchCount}/{snapshot.BatchCount} batches; requesting a fresh one.",
                this);

            ObserveAbandonedSnapshotTask(snapshot);
            pendingSnapshot = null;
            return true;
        }

        // Returns true when at least one request aged out, which is treated as evidence that the
        // client's view may have drifted from the host's.
        bool ExpireTimedOutMutationRequests()
        {
            if (pendingMutationRequests.Count == 0)
                return false;

            expiredMutationRequestScratch.Clear();

            foreach (KeyValuePair<uint, PendingMutationRequest> pending in pendingMutationRequests)
            {
                if (clientRecoveryElapsedSeconds - pending.Value.CreatedAtSeconds >= PendingMutationRequestTimeoutSeconds)
                    expiredMutationRequestScratch.Add(pending.Key);
            }

            if (expiredMutationRequestScratch.Count == 0)
                return false;

            foreach (uint requestId in expiredMutationRequestScratch)
            {
                pendingMutationRequests.Remove(requestId);
                TimedOutMutationRequestCount++;
            }

            expiredMutationRequestScratch.Clear();
            return true;
        }

        /// <summary>
        /// Asks the host to re-send the authoritative world. The client discards its own delta
        /// bookkeeping and stops accepting local edits until the replacement snapshot lands, so
        /// nothing is applied on top of a world it can no longer prove is in sync.
        /// </summary>
        public void RequestWorldResync()
        {
            if (resyncRequestCooldownTimer > 0.0f)
                return;

            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null || !networkManager.IsListening || networkManager.IsServer)
                return;

            // Captured before the reset below zeroes it — it tells the host how far this client
            // had actually got, which is the useful number in a desync report.
            uint lastAppliedSequence = LastAppliedChunkDeltaSequence;

            resyncRequestCooldownTimer = ResyncRequestCooldownSeconds;
            hasHostGenerationSnapshotForSession = false;
            ResetClientChunkDeltaState();
            ResetPendingMutationRequests();
            RegisterMessageHandlers();

            var writer = new FastBufferWriter(ResyncRequestMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(lastAppliedSequence);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    ResyncRequestMessage,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
                SentResyncRequestCount++;
            }
            finally
            {
                writer.Dispose();
            }

            BlockiverseLog.Warning(
                BlockiverseLogCategory.Networking,
                "Requested a world resync from the host after a chunk delta gap or repeated request timeouts.",
                this);
        }

        void HandleResyncRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (IsSenderUnauthorized(senderClientId))
                return;

            RefreshAuthorityBoundary();

            if (!CurrentBoundary.CanServeLateJoinSync)
                return;

            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null || !networkManager.IsServer || senderClientId == networkManager.LocalClientId)
                return;

            uint clientLastAppliedSequence = 0;
            if (reader.Length - reader.Position >= sizeof(uint))
                reader.ReadValueSafe(out clientLastAppliedSequence);

            // Resending the world is far more expensive than an edit, so it gets its own, much
            // tighter budget than the mutation limiter.
            if (!hostResyncRateLimiter.TryConsume(senderClientId, HostMutationTimeSeconds))
                return;

            BlockiverseLog.Warning(
                BlockiverseLogCategory.Networking,
                $"Serving a world resync clientId={senderClientId} clientSequence={clientLastAppliedSequence} hostSequence={chunkDeltaLog.LastSequenceId}",
                this);

            SendLateJoinSnapshot(senderClientId);
            SendEnvironmentSnapshot(senderClientId);
            ServedResyncRequestCount++;
        }

        void ResetHostChunkDeltaLog()
        {
            chunkDeltaLog.Clear();
            LastBroadcastChunkDeltaSequence = 0;
        }

        // Sends the header first, then the changed blocks as a sequence of bounded batches.
        // ReliableFragmentedSequenced preserves order, so the client always sees the header
        // before the batches it describes.
        void SendLateJoinSnapshot(ulong clientId)
        {
            using ProfilerMarker.AutoScope scope = SendLateJoinSnapshotMarker.Auto();

            IReadOnlyCollection<BlockChange> changedBlocks = ResolveWorld().GetChangedBlocks();
            int blockCount = changedBlocks.Count;
            int batchCount = (blockCount + SnapshotBatchMaxBlocks - 1) / SnapshotBatchMaxBlocks;
            uint snapshotId = AllocateSnapshotId();

            if (blockCount > LateJoinSnapshotWarningBlockCount)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Networking,
                    $"Late-join snapshot is large: changedBlocks={blockCount} batches={batchCount} clientId={clientId}. " +
                    "Consider compacting the world delta set.",
                    this);
            }

            NetworkManager networkManager = ResolveNetworkManager();
            var headerWriter = new FastBufferWriter(SnapshotHeaderBytes, Allocator.Temp);

            try
            {
                WriteWorldSnapshotHeader(ref headerWriter, blockCount, snapshotId, batchCount);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    ChunkSnapshotMessage,
                    clientId,
                    headerWriter,
                    NetworkDelivery.ReliableFragmentedSequenced);
                SentLateJoinSnapshotCount++;
            }
            finally
            {
                headerWriter.Dispose();
            }

            if (batchCount == 0)
                return;

            // Copy the changed set now: the paced send spans frames and the world keeps
            // mutating underneath it. The header has already committed to this count, so the
            // batches must describe the same set the header announced.
            //
            // Block state is captured HERE for the same reason, not read from the world inside the
            // coroutine: a leaf placed or broken mid-send would otherwise pair one frame's state
            // with another frame's block.
            VoxelWorld snapshotWorld = ResolveWorld();
            var blocks = new List<(BlockChange Change, int State)>(changedBlocks.Count);
            foreach (BlockChange changed in changedBlocks)
                blocks.Add((changed, snapshotWorld.GetBlockState(changed.Position)));

            if (snapshotSendRoutines.TryGetValue(clientId, out Coroutine existing) && existing != null)
                StopCoroutine(existing);

            snapshotSendRoutines[clientId] = StartCoroutine(
                SendSnapshotBatches(clientId, snapshotId, batchCount, blocks));
        }

        // Paced so a large world cannot overflow the transport's send queue in one frame; see
        // SnapshotBatchesPerFrame for why a dropped batch is worse than a slow one.
        IEnumerator SendSnapshotBatches(
            ulong clientId, uint snapshotId, int batchCount, List<(BlockChange Change, int State)> blocks)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            int sentThisFrame = 0;

            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                // The client can leave mid-stream; sending to a gone client is pointless and
                // the remaining batches belong to nobody.
                if (networkManager == null ||
                    !networkManager.IsListening ||
                    !networkManager.IsServer ||
                    !IsClientConnected(networkManager, clientId))
                {
                    break;
                }

                int offset = batchIndex * SnapshotBatchMaxBlocks;
                int count = Math.Min(SnapshotBatchMaxBlocks, blocks.Count - offset);
                var writer = new FastBufferWriter(
                    SnapshotBatchHeaderBytes + count * SnapshotBlockBytes,
                    Allocator.Temp);

                try
                {
                    writer.WriteValueSafe(snapshotId);
                    writer.WriteValueSafe(batchIndex);
                    writer.WriteValueSafe(batchCount);
                    writer.WriteValueSafe(count);

                    for (int index = offset; index < offset + count; index++)
                    {
                        WriteBlockPosition(ref writer, blocks[index].Change.Position);
                        writer.WriteValueSafe(blocks[index].Change.NewBlock.Value);
                        writer.WriteValueSafe(blocks[index].State);
                    }

                    networkManager.CustomMessagingManager.SendNamedMessage(
                        ChunkSnapshotBatchMessage,
                        clientId,
                        writer,
                        NetworkDelivery.ReliableFragmentedSequenced);
                    SentSnapshotBatchCount++;
                }
                finally
                {
                    writer.Dispose();
                }

                if (++sentThisFrame < SnapshotBatchesPerFrame)
                    continue;

                sentThisFrame = 0;
                yield return null;
            }

            snapshotSendRoutines.Remove(clientId);
        }

        /// <summary>
        /// Host-side reach gate for a client-requested edit. Returns true when the host cannot
        /// resolve the requester's head — an unspawned or just-connected player must not have its
        /// legitimate edits dropped because presence data has not arrived yet — and the check is
        /// skipped entirely for the host's own edits, which never travel over the wire.
        /// </summary>
        bool IsWithinHostValidatedReach(ulong clientId, BlockPosition position)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null || clientId == networkManager.LocalClientId)
                return true;

            if (session == null || !session.TryResolvePlayerHeadWorldPosition(clientId, out Vector3 headPosition))
                return true;

            return BlockiverseInteractionLimits.IsWithinReach(
                headPosition.x,
                headPosition.y,
                headPosition.z,
                position.X,
                position.Y,
                position.Z,
                BlockiverseInteractionLimits.MaxHostValidatedReachMeters);
        }

        static bool IsClientConnected(NetworkManager networkManager, ulong clientId)
        {
            foreach (ulong connected in networkManager.ConnectedClientsIds)
            {
                if (connected == clientId)
                    return true;
            }

            return false;
        }

        uint AllocateSnapshotId()
        {
            uint snapshotId = nextSnapshotId++;

            if (nextSnapshotId == 0)
                nextSnapshotId = 1;

            return snapshotId;
        }

        void SendEnvironmentSnapshot(ulong clientId)
        {
            if (worldManager == null) return;

            using ProfilerMarker.AutoScope scope = SendEnvironmentSnapshotMarker.Auto();

            var writer = new FastBufferWriter(EnvironmentSnapshotMessageBytes, Allocator.Temp);
            try
            {
                WriteEnvironmentSnapshot(ref writer, BuildEnvironmentSnapshot());

                ResolveNetworkManager().CustomMessagingManager.SendNamedMessage(
                    EnvironmentSnapshotMessage,
                    clientId,
                    writer);
                SentEnvironmentSnapshotCount++;
            }
            finally
            {
                writer.Dispose();
            }
        }

        void BroadcastEnvironmentSnapshot()
        {
            using ProfilerMarker.AutoScope scope = BroadcastEnvironmentSnapshotMarker.Auto();

            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || !networkManager.IsServer)
                return;

            remoteClientIdsScratch.Clear();
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId != networkManager.LocalClientId)
                    remoteClientIdsScratch.Add(clientId);
            }

            foreach (ulong clientId in remoteClientIdsScratch)
                SendEnvironmentSnapshot(clientId);
        }

        EnvironmentSnapshotState BuildEnvironmentSnapshot()
        {
            WeatherSyncState weather = worldManager.GetWeatherSyncState();
            long worldTimeTicks = worldManager.WorldTimeClock != null
                ? worldManager.WorldTimeClock.TotalElapsedTicks
                : 0L;

            return new EnvironmentSnapshotState(weather.State, weather.Ticks, weather.RngState, worldTimeTicks);
        }

        void HandleEnvironmentSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != CurrentBoundary.HostClientId || !CurrentBoundary.MustRequestMutations)
                return;

            if (!TryReadEnvironmentSnapshot(ref reader, out EnvironmentSnapshotState snapshot))
                return;

            if (worldManager != null)
            {
                // Both helpers buffer-and-defer if the services are not yet initialized, so weather
                // ticks/RNG and world time survive regardless of message ordering relative to the
                // generation snapshot.
                worldManager.RestoreWeatherSyncState(
                    new WeatherSyncState(
                        snapshot.WeatherState,
                        snapshot.WeatherTicks,
                        snapshot.WeatherRngState),
                    preserveForNextWorldInitialization: !hasHostGenerationSnapshotForSession);
                worldManager.RestoreWorldTimeTicks(snapshot.WorldTimeTicks);
            }

            AppliedEnvironmentSnapshotCount++;
        }

        void ApplyAuthoritativeBlock(
            BlockPosition position,
            BlockId block,
            bool trackChange = true,
            bool rebuildRenderer = true,
            int blockState = BlockState.Default)
        {
            VoxelWorld world = ResolveWorld();

            if (!world.Bounds.Contains(position))
                return;

            world.SetBlock(position, block, trackChange);

            // After SetBlock, which clears state for the position it writes. Mirroring the host's
            // state is what keeps the lockstep world simulation running from identical inputs on
            // every peer: leaf decay reads BlockState.Persistent, and a client that saw only the
            // block id would delete a hand-built hedge the host keeps, with no delta to repair it.
            if (blockState != BlockState.Default)
                world.SetBlockState(position, blockState);
            if (rebuildRenderer && worldManager.Renderer != null)
                worldManager.Renderer.RebuildDirty();
        }

        ChunkDeltaApplyState TryApplyChunkDelta(ChunkDelta delta)
        {
            using ProfilerMarker.AutoScope scope = TryApplyChunkDeltaMarker.Auto();

            if (delta.SequenceId == NextChunkDeltaSequence(LastAppliedChunkDeltaSequence))
            {
                ApplyAuthoritativeBlock(
                    delta.Change.Position, delta.Change.NewBlock, trackChange: false,
                    blockState: delta.NewBlockState);
                LastAppliedChunkDeltaSequence = delta.SequenceId;
                AppliedChunkDeltaCount++;
                return ChunkDeltaApplyState.Applied;
            }

            if (delta.SequenceId == LastAppliedChunkDeltaSequence ||
                (LastAppliedChunkDeltaSequence != uint.MaxValue &&
                 delta.SequenceId < LastAppliedChunkDeltaSequence))
            {
                IgnoredOutOfOrderChunkDeltaCount++;
                return ChunkDeltaApplyState.IgnoredStale;
            }

            return ChunkDeltaApplyState.WaitingForEarlierDelta;
        }

        void ApplyChunkDeltaMessageOrBuffer(PendingChunkDeltaMessage message)
        {
            ChunkDeltaApplyState applyState = TryApplyChunkDelta(message.Delta);

            if (applyState == ChunkDeltaApplyState.WaitingForEarlierDelta)
            {
                BufferChunkDeltaMessage(message);
                return;
            }

            if (applyState == ChunkDeltaApplyState.IgnoredStale)
                return;

            CompleteAppliedChunkDeltaMessage(message);
        }

        void CompleteAppliedChunkDeltaMessage(PendingChunkDeltaMessage message)
        {
            bool completedLocalRequest = TryCompletePendingMutationRequest(message.RequestingClientId, message.RequestId);
            LastMutationResult = BlockMutationResult.Accept(
                message.Delta.Change,
                message.Delta.Chunk,
                completedLocalRequest ? message.RequestId : 0);
            AppliedRemoteDeltaCount++;

            if (completedLocalRequest)
                AcceptedMutationResponseCount++;
        }

        void ApplyBufferedChunkDeltas()
        {
            if (bufferedChunkDeltas.Count == 0)
                return;

            using ProfilerMarker.AutoScope scope = ApplyBufferedChunkDeltasMarker.Auto();

            bool madeProgress;

            do
            {
                madeProgress = false;

                for (int index = 0; index < bufferedChunkDeltas.Count; index++)
                {
                    PendingChunkDeltaMessage message = bufferedChunkDeltas[index];
                    ChunkDeltaApplyState applyState = TryApplyChunkDelta(message.Delta);

                    if (applyState == ChunkDeltaApplyState.WaitingForEarlierDelta)
                        continue;

                    bufferedChunkDeltas.RemoveAt(index);
                    madeProgress = true;

                    if (applyState == ChunkDeltaApplyState.Applied)
                        CompleteAppliedChunkDeltaMessage(message);

                    break;
                }
            }
            while (madeProgress && bufferedChunkDeltas.Count > 0);
        }

        void BufferChunkDeltaMessage(PendingChunkDeltaMessage message)
        {
            for (int index = 0; index < bufferedChunkDeltas.Count; index++)
            {
                if (bufferedChunkDeltas[index].Delta.SequenceId == message.Delta.SequenceId)
                    return;
            }

            // A buffer this deep means the missing sequence is never going to arrive on its own.
            // Drop the backlog and go straight to a resync rather than growing without bound.
            if (bufferedChunkDeltas.Count >= MaxBufferedChunkDeltas)
            {
                RequestWorldResync();
                return;
            }

            bufferedChunkDeltas.Add(message);
        }

        // Remote peers only: excludes this process's own client id, which exists on a host and
        // does not on a dedicated server.
        static int CountRemoteClients(NetworkManager networkManager)
        {
            if (networkManager == null)
                return 0;

            int remote = 0;
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId != networkManager.LocalClientId)
                    remote++;
            }

            return remote;
        }

        void SendToRemoteClients(string messageName, FastBufferWriter writer)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            remoteClientIdsScratch.Clear();

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId != networkManager.LocalClientId)
                    remoteClientIdsScratch.Add(clientId);
            }

            if (remoteClientIdsScratch.Count > 0)
                networkManager.CustomMessagingManager.SendNamedMessage(messageName, remoteClientIdsScratch, writer);
        }

        void RefreshAuthorityBoundary()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager != null &&
                networkManager.IsListening &&
                networkManager.IsClient &&
                !networkManager.IsServer)
            {
                ulong localClientId = networkManager.LocalClientId != NetworkManager.ServerClientId
                    ? networkManager.LocalClientId
                    : NetworkManager.ServerClientId + 1;
                CurrentBoundary = ChunkAuthorityBoundary.ForClient(localClientId, NetworkManager.ServerClientId);
            }
            else
            {
                ulong hostClientId = networkManager != null ? networkManager.LocalClientId : 0;
                CurrentBoundary = ChunkAuthorityBoundary.ForHost(hostClientId);
            }

            mutationAuthority = null;
        }

        BlockMutationAuthority ResolveMutationAuthority()
        {
            if (mutationAuthority == null)
                mutationAuthority = new BlockMutationAuthority(ResolveWorld(), ResolveRegistry(), CurrentBoundary);

            return mutationAuthority;
        }

        VoxelWorld ResolveWorld()
        {
            ResolveWorldManager();

            if (worldManager.World == null)
            {
                if (CurrentBoundary.MustRequestMutations)
                    throw new InvalidOperationException("Client chunk state must be received from the host before authoritative chunk operations.");

                worldManager.InitializeDefaultWorld();
            }

            return worldManager.World ?? throw new InvalidOperationException("Multiplayer chunk authority requires a voxel world.");
        }

        int ResolveMutationChunkSize()
        {
            return worldManager != null && worldManager.World != null
                ? worldManager.World.ChunkSize
                : 16;
        }

        BlockRegistry ResolveRegistry()
        {
            ResolveWorldManager();

            if (worldManager.Registry == null)
            {
                if (CurrentBoundary.MustRequestMutations)
                    return BlockRegistry.Default;

                worldManager.InitializeDefaultWorld();
            }

            return worldManager.Registry ?? throw new InvalidOperationException("Multiplayer chunk authority requires a block registry.");
        }

        void ResolveReferences()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            ResolveWorldManager();
        }

        void ResolveWorldManager()
        {
            if (worldManager == null)
            {
                var managers = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var mgr in managers)
                {
                    if (mgr is IMultiplayerWorldContext context)
                    {
                        worldManager = context;
                        break;
                    }
                }
            }

            if (worldManager != null)
                worldManager.ConfigureAuthoritySync(this);
        }

        bool IsActiveClientOnly()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            return networkManager != null &&
                   networkManager.IsListening &&
                   networkManager.IsClient &&
                   !networkManager.IsServer;
        }

        void SubscribeNetworkCallbacks()
        {
            ResolveReferences();
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null || subscribedNetworkManager == networkManager)
                return;

            subscribedNetworkManager = networkManager;
            subscribedNetworkManager.OnServerStarted += HandleServerStarted;
            subscribedNetworkManager.OnClientStarted += HandleClientStarted;
            subscribedNetworkManager.OnClientConnectedCallback += HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            subscribedNetworkManager.OnServerStopped += HandleServerStopped;
            subscribedNetworkManager.OnClientStopped += HandleClientStopped;
            RegisterMessageHandlers();
        }

        void UnsubscribeNetworkCallbacks()
        {
            UnregisterMessageHandlers();

            if (subscribedNetworkManager == null)
                return;

            subscribedNetworkManager.OnServerStarted -= HandleServerStarted;
            subscribedNetworkManager.OnClientStarted -= HandleClientStarted;
            subscribedNetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            subscribedNetworkManager.OnServerStopped -= HandleServerStopped;
            subscribedNetworkManager.OnClientStopped -= HandleClientStopped;
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

            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(MutationRequestMessage, HandleMutationRequestMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(MutationDeltaMessage, HandleMutationDeltaMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ChunkSnapshotMessage, HandleChunkSnapshotMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ChunkSnapshotBatchMessage, HandleChunkSnapshotBatchMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(MutationResultMessage, HandleMutationResultMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(EnvironmentSnapshotMessage, HandleEnvironmentSnapshotMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ResyncRequestMessage, HandleResyncRequestMessage);
            messagesRegistered = true;
        }

        void UnregisterMessageHandlers()
        {
            if (!messagesRegistered ||
                subscribedNetworkManager == null ||
                subscribedNetworkManager.CustomMessagingManager == null)
            {
                messagesRegistered = false;
                return;
            }

            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MutationRequestMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MutationDeltaMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ChunkSnapshotMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ChunkSnapshotBatchMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MutationResultMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(EnvironmentSnapshotMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ResyncRequestMessage);
            messagesRegistered = false;
        }

        NetworkManager ResolveNetworkManager()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null)
                throw new InvalidOperationException("Multiplayer chunk authority requires a network session.");

            return networkManager;
        }

        NetworkManager ResolveNetworkManagerOrNull()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            return session != null ? session.NetworkManager : null;
        }

        static void WriteBlockPosition(ref FastBufferWriter writer, BlockPosition position)
        {
            writer.WriteValueSafe(position.X);
            writer.WriteValueSafe(position.Y);
            writer.WriteValueSafe(position.Z);
        }

        static BlockPosition ReadBlockPosition(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int x);
            reader.ReadValueSafe(out int y);
            reader.ReadValueSafe(out int z);
            return new BlockPosition(x, y, z);
        }

        static void WriteBlockChange(ref FastBufferWriter writer, BlockChange change)
        {
            WriteBlockPosition(ref writer, change.Position);
            writer.WriteValueSafe(change.PreviousBlock.Value);
            writer.WriteValueSafe(change.NewBlock.Value);
        }

        static uint NextChunkDeltaSequence(uint sequenceId)
        {
            using ProfilerMarker.AutoScope scope = TryApplyChunkDeltaMarker.Auto();
            uint nextSequenceId = sequenceId + 1;
            return nextSequenceId == 0 ? 1 : nextSequenceId;
        }

        void WriteWorldSnapshotHeader(
            ref FastBufferWriter writer,
            int changedBlockCount,
            uint snapshotId,
            int batchCount)
        {
            VoxelWorld world = ResolveWorld();
            WorldGenerationSettings settings = worldManager.Settings;
            int groundHeight = settings != null
                ? settings.GroundHeight
                : Math.Max(1, Math.Min(world.Bounds.Height - 1, world.Bounds.Height / 2));
            BlockPosition spawnPosition = settings != null
                ? settings.SpawnPosition
                : new BlockPosition(world.Bounds.Width / 2, Math.Min(world.Bounds.Height - 1, groundHeight + 1), world.Bounds.Depth / 2);

            WriteWorldSnapshotHeader(
                ref writer,
                new WorldSnapshotHeader(
                    worldManager.GenerationPreset,
                    world.Bounds.Width,
                    world.Bounds.Height,
                    world.Bounds.Depth,
                    world.ChunkSize,
                    world.Seed,
                    groundHeight,
                    spawnPosition,
                    chunkDeltaLog.LastSequenceId,
                    changedBlockCount,
                    snapshotId,
                    batchCount));
        }

        public static void WriteWorldSnapshotHeader(ref FastBufferWriter writer, WorldSnapshotHeader header)
        {
            writer.WriteValueSafe((int)header.GenerationPreset);
            writer.WriteValueSafe(header.Width);
            writer.WriteValueSafe(header.Height);
            writer.WriteValueSafe(header.Depth);
            writer.WriteValueSafe(header.ChunkSize);
            writer.WriteValueSafe(header.Seed);
            writer.WriteValueSafe(header.GroundHeight);
            WriteBlockPosition(ref writer, header.SpawnPosition);
            writer.WriteValueSafe(header.HostDeltaSequence);
            writer.WriteValueSafe(header.ChangedBlockCount);
            writer.WriteValueSafe(header.SnapshotId);
            writer.WriteValueSafe(header.BatchCount);
        }

        public static bool TryReadWorldSnapshotHeader(ref FastBufferReader reader, out WorldSnapshotHeader header)
        {
            header = default;

            // 48 bytes of world metadata plus snapshotId and batchCount.
            if (reader.Length - reader.Position < 56)
                return false;

            reader.ReadValueSafe(out int generationPreset);
            reader.ReadValueSafe(out int width);
            reader.ReadValueSafe(out int height);
            reader.ReadValueSafe(out int depth);
            reader.ReadValueSafe(out int chunkSize);
            reader.ReadValueSafe(out int seed);
            reader.ReadValueSafe(out int groundHeight);
            BlockPosition spawnPosition = ReadBlockPosition(ref reader);
            reader.ReadValueSafe(out uint hostDeltaSequence);
            reader.ReadValueSafe(out int changedBlockCount);
            reader.ReadValueSafe(out uint snapshotId);
            reader.ReadValueSafe(out int batchCount);

            if (generationPreset < 0 ||
                generationPreset > (int)CreativeWorldGenerationPreset.VoidBuilder ||
                width <= 0 ||
                height <= 0 ||
                depth <= 0 ||
                chunkSize <= 0 ||
                groundHeight < 1 ||
                groundHeight >= height ||
                changedBlockCount < 0 ||
                batchCount < 0 ||
                batchCount > changedBlockCount)
            {
                return false;
            }

            var bounds = new WorldBounds(width, height, depth);
            if (!bounds.Contains(spawnPosition))
                return false;

            header = new WorldSnapshotHeader(
                (CreativeWorldGenerationPreset)generationPreset,
                width,
                height,
                depth,
                chunkSize,
                seed,
                groundHeight,
                spawnPosition,
                hostDeltaSequence,
                changedBlockCount,
                snapshotId,
                batchCount);
            return true;
        }

        public static void WriteEnvironmentSnapshot(ref FastBufferWriter writer, EnvironmentSnapshotState snapshot)
        {
            writer.WriteValueSafe((int)snapshot.WeatherState);
            writer.WriteValueSafe(snapshot.WeatherTicks);
            writer.WriteValueSafe(snapshot.WeatherRngState);
            writer.WriteValueSafe(snapshot.WorldTimeTicks);
        }

        public static bool TryReadEnvironmentSnapshot(ref FastBufferReader reader, out EnvironmentSnapshotState snapshot)
        {
            snapshot = default;

            if (reader.Length - reader.Position < EnvironmentSnapshotBytes)
                return false;

            reader.ReadValueSafe(out int weatherState);
            reader.ReadValueSafe(out int weatherTicks);
            reader.ReadValueSafe(out uint weatherRngState);
            reader.ReadValueSafe(out long worldTimeTicks);

            if (weatherState < 0 ||
                weatherState > (int)WeatherState.Fog ||
                weatherTicks < 0 ||
                worldTimeTicks < 0)
            {
                return false;
            }

            snapshot = new EnvironmentSnapshotState(
                (WeatherState)weatherState,
                weatherTicks,
                weatherRngState,
                worldTimeTicks);
            return true;
        }

        static ChunkDelta ReadMutationDelta(ref FastBufferReader reader, out ulong requestingClientId, out uint requestId)
        {
            reader.ReadValueSafe(out requestingClientId);
            reader.ReadValueSafe(out requestId);
            return ReadChunkDelta(ref reader);
        }

        static void WriteChunkCoordinate(ref FastBufferWriter writer, ChunkCoordinate chunk)
        {
            writer.WriteValueSafe(chunk.X);
            writer.WriteValueSafe(chunk.Y);
            writer.WriteValueSafe(chunk.Z);
        }

        static ChunkCoordinate ReadChunkCoordinate(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int x);
            reader.ReadValueSafe(out int y);
            reader.ReadValueSafe(out int z);
            return new ChunkCoordinate(x, y, z);
        }

        static void WriteChunkDelta(ref FastBufferWriter writer, ChunkDelta delta)
        {
            writer.WriteValueSafe(delta.SequenceId);
            WriteChunkCoordinate(ref writer, delta.Chunk);
            WriteBlockChange(ref writer, delta.Change);
            // Written OUTSIDE WriteBlockChange: state belongs to the world at a position, not to
            // the change record, and WriteBlockChange is shared with messages that carry no world.
            writer.WriteValueSafe(delta.NewBlockState);
        }

        static ChunkDelta ReadChunkDelta(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out uint sequenceId);
            ChunkCoordinate chunk = ReadChunkCoordinate(ref reader);
            BlockChange change = ReadBlockChange(ref reader);
            reader.ReadValueSafe(out int newBlockState);
            return new ChunkDelta(sequenceId, chunk, change, newBlockState);
        }

        static BlockChange ReadBlockChange(ref FastBufferReader reader)
        {
            BlockPosition position = ReadBlockPosition(ref reader);
            reader.ReadValueSafe(out int previousBlock);
            reader.ReadValueSafe(out int newBlock);
            return new BlockChange(position, new BlockId(previousBlock), new BlockId(newBlock));
        }

        // Returns false (and a default request) when the payload carries negative block ids —
        // the BlockId constructor would throw inside the message pump otherwise.
        static bool TryReadMutationRequest(
            ulong requestingClientId,
            ref FastBufferReader reader,
            out uint requestId,
            out BlockMutationRequest request)
        {
            reader.ReadValueSafe(out requestId);
            BlockPosition position = ReadBlockPosition(ref reader);
            reader.ReadValueSafe(out int newBlock);
            reader.ReadValueSafe(out bool hasExpectedCurrentBlock);
            reader.ReadValueSafe(out int expectedCurrentBlock);

            if (newBlock < 0 || (hasExpectedCurrentBlock && expectedCurrentBlock < 0))
            {
                request = default;
                return false;
            }

            request = hasExpectedCurrentBlock
                ? new BlockMutationRequest(requestingClientId, position, new BlockId(newBlock), new BlockId(expectedCurrentBlock))
                : new BlockMutationRequest(requestingClientId, position, new BlockId(newBlock));
            return true;
        }

        // A client mutation request awaiting a host answer. The timestamp is measured against the
        // client's own recovery clock (advanced by TickChunkDeltaRecovery) rather than wall time,
        // so timeout behaviour is deterministic and testable without a live session.
        readonly struct PendingMutationRequest
        {
            public PendingMutationRequest(BlockMutationRequest request, float createdAtSeconds)
            {
                Request = request;
                CreatedAtSeconds = createdAtSeconds;
            }

            public BlockMutationRequest Request { get; }
            public float CreatedAtSeconds { get; }
        }

        readonly struct PendingChunkDeltaMessage
        {
            public PendingChunkDeltaMessage(ulong requestingClientId, uint requestId, ChunkDelta delta)
            {
                RequestingClientId = requestingClientId;
                RequestId = requestId;
                Delta = delta;
            }

            public ulong RequestingClientId { get; }
            public uint RequestId { get; }
            public ChunkDelta Delta { get; }
        }
    }
}
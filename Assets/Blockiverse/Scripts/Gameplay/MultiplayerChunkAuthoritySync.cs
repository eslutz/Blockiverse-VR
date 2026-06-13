using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Gameplay
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
        const string MutationResultMessage = "Blockiverse.ChunkAuthority.MutationResult";
        const string EnvironmentSnapshotMessage = "Blockiverse.ChunkAuthority.EnvironmentSnapshot";
        const int MutationRequestMessageBytes = 128;
        const int MutationDeltaMessageBytes = 160;
        const int MutationResultMessageBytes = 128;
        public const int WorldSnapshotHeaderBytes = 80;
        public const int EnvironmentSnapshotBytes = 20;
        public const float EnvironmentResyncIntervalSeconds = 5.0f;
        const int SnapshotHeaderBytes = WorldSnapshotHeaderBytes;
        const int SnapshotBlockBytes = 32;
        const int HostMutationRateLimitMaxRequests = 30;
        const double HostMutationRateLimitWindowSeconds = 1.0d;
        // WeatherState (int) + ticksInCurrentState (int) + weatherRng (uint) + totalElapsedTicks (long) = 20 bytes
        const int EnvironmentSnapshotMessageBytes = EnvironmentSnapshotBytes;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] CreativeWorldManager worldManager;

        readonly Dictionary<uint, BlockMutationRequest> pendingMutationRequests = new();
        readonly PerClientRequestRateLimiter hostMutationRateLimiter =
            new(HostMutationRateLimitMaxRequests, HostMutationRateLimitWindowSeconds);
        readonly List<PendingChunkDeltaMessage> bufferedChunkDeltas = new();
        // Reused by SendToRemoteClients so each broadcast avoids a per-delta list allocation.
        readonly List<ulong> remoteClientIdsScratch = new();
        readonly ChunkDeltaLog chunkDeltaLog = new();
        NetworkManager subscribedNetworkManager;
        BlockMutationAuthority mutationAuthority;
        uint nextMutationRequestId = 1;
        bool messagesRegistered;
        bool hasHostGenerationSnapshotForSession;
        float environmentResyncTimer;
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
                int changedBlockCount)
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

        public void Configure(BlockiverseNetworkSession targetSession, CreativeWorldManager targetWorldManager)
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
        }

        void Update()
        {
            TickEnvironmentResync(Time.unscaledDeltaTime);
        }

        public void TickEnvironmentResync(float deltaSeconds)
        {
            RefreshAuthorityBoundary();
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer ||
                !CurrentBoundary.CanServeLateJoinSync ||
                networkManager.ConnectedClientsIds.Count <= 1)
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

            SendLateJoinSnapshot(clientId);
            SendEnvironmentSnapshot(clientId);
        }

        void HandleServerStopped(bool wasHost)
        {
            hostMutationRateLimiter.Clear();
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
            hostMutationRateLimiter.Clear();
            ResetClientChunkDeltaState();
            ResetPendingMutationRequests();
            UnregisterMessageHandlers();
            RefreshAuthorityBoundary();
        }

        void HandleClientDisconnected(ulong clientId)
        {
            hostMutationRateLimiter.RemoveClient(clientId);
        }

        void HandleMutationRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
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
            RefreshAuthorityBoundary();

            if (senderClientId != CurrentBoundary.HostClientId || !CurrentBoundary.MustRequestMutations)
                return;

            // Read the entire payload inside the handler (the reader is only valid here), then
            // hand world generation to a background task: regenerating a full survival world
            // synchronously would stall the VR main thread for seconds.
            if (!TryReadWorldSnapshotHeader(ref reader, out WorldSnapshotHeader header))
                return;

            var blocks = new List<(BlockPosition position, int blockId)>(header.ChangedBlockCount);
            for (int index = 0; index < header.ChangedBlockCount; index++)
            {
                BlockPosition position = ReadBlockPosition(ref reader);
                reader.ReadValueSafe(out int blockId);
                blocks.Add((position, blockId));
            }

            var settings = new WorldGenerationSettings(
                header.Width,
                header.Height,
                header.Depth,
                header.ChunkSize,
                header.Seed,
                header.GroundHeight,
                header.SpawnPosition);
            StartSnapshotGeneration(header.GenerationPreset, settings, header.HostDeltaSequence, blocks);
        }

        // The in-flight late-join snapshot. Only the newest one matters: a fresh snapshot
        // replaces the pending entry and the completion routine drops superseded results.
        sealed class PendingWorldSnapshot
        {
            public CreativeWorldGenerationPreset Preset;
            public WorldGenerationSettings Settings;
            public uint HostDeltaSequence;
            public List<(BlockPosition position, int blockId)> Blocks;
            public Task<GeneratedSnapshotWorld> GenerationTask;
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
            List<(BlockPosition position, int blockId)> blocks)
        {
            // A newer snapshot supersedes any in-flight generation; observe the abandoned
            // task so a faulted run cannot surface later as an UnobservedTaskException.
            ObserveAbandonedSnapshotTask(pendingSnapshot);

            pendingSnapshot = new PendingWorldSnapshot
            {
                Preset = preset,
                Settings = settings,
                HostDeltaSequence = hostDeltaSequence,
                Blocks = blocks,
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

                if (current.GenerationTask.IsCompleted)
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
                new GeneratedCreativeWorld(generated.Registry, snapshot.Settings, generated.World, snapshot.Preset, generated.ContainerLoot),
                this,
                deferInitialRendererRebuild: true);

            // Batch the renderer rebuild: applying the snapshot block-by-block would otherwise
            // rebuild every dirty chunk mesh once per block (O(blocks × rebuild)).
            foreach ((BlockPosition position, int blockId) in snapshot.Blocks)
            {
                if (blockId < 0)
                    continue;

                ApplyAuthoritativeBlock(position, new BlockId(blockId), trackChange: false, rebuildRenderer: false);
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

            ChunkCoordinate chunk = ChunkCoordinate.FromBlockPosition(position, ResolveWorld().ChunkSize);
            LastMutationResult = BlockMutationResult.Reject(
                (BlockMutationRejectionReason)rejectionReason,
                chunk,
                "Host rejected the block mutation request.",
                requestId);
            ReceivedMutationRejectionCount++;
            TryCompletePendingMutationRequest(CurrentBoundary.LocalClientId, requestId);

            if (hasAuthoritativeBlock)
                ApplyAuthoritativeBlock(position, new BlockId(authoritativeBlock), trackChange: false);
        }

        void SendMutationRequest(uint requestId, BlockMutationRequest request)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            RegisterMessageHandlers();
            pendingMutationRequests[requestId] = request;
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
                ChunkDelta delta = chunkDeltaLog.Record(change, ResolveWorld().ChunkSize);
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
            var writer = new FastBufferWriter(MutationResultMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(requestId);
                WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe((int)result.RejectionReason);
                writer.WriteValueSafe(hasAuthoritativeBlock);
                writer.WriteValueSafe(authoritativeBlock.Value);
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
        }

        void ResetHostChunkDeltaLog()
        {
            chunkDeltaLog.Clear();
            LastBroadcastChunkDeltaSequence = 0;
        }

        void SendLateJoinSnapshot(ulong clientId)
        {
            IReadOnlyCollection<BlockChange> changedBlocks = ResolveWorld().GetChangedBlocks();
            int writerSize = SnapshotHeaderBytes + changedBlocks.Count * SnapshotBlockBytes;

            var writer = new FastBufferWriter(writerSize, Allocator.Temp);

            try
            {
                WriteWorldSnapshotHeader(ref writer, changedBlocks.Count);
                foreach (BlockChange change in changedBlocks)
                {
                    WriteBlockPosition(ref writer, change.Position);
                    writer.WriteValueSafe(change.NewBlock.Value);
                }

                ResolveNetworkManager().CustomMessagingManager.SendNamedMessage(
                    ChunkSnapshotMessage,
                    clientId,
                    writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
                SentLateJoinSnapshotCount++;
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendEnvironmentSnapshot(ulong clientId)
        {
            if (worldManager == null) return;

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
            CreativeWorldManager.WeatherSyncState weather = worldManager.GetWeatherSyncState();
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
                    new CreativeWorldManager.WeatherSyncState(
                        snapshot.WeatherState,
                        snapshot.WeatherTicks,
                        snapshot.WeatherRngState),
                    preserveForNextWorldInitialization: !hasHostGenerationSnapshotForSession);
                worldManager.RestoreWorldTimeTicks(snapshot.WorldTimeTicks);
            }

            AppliedEnvironmentSnapshotCount++;
        }

        void ApplyAuthoritativeBlock(BlockPosition position, BlockId block, bool trackChange = true, bool rebuildRenderer = true)
        {
            VoxelWorld world = ResolveWorld();

            if (!world.Bounds.Contains(position))
                return;

            world.SetBlock(position, block, trackChange);
            if (rebuildRenderer && worldManager.Renderer != null)
                worldManager.Renderer.RebuildDirty();
        }

        ChunkDeltaApplyState TryApplyChunkDelta(ChunkDelta delta)
        {
            if (delta.SequenceId == NextChunkDeltaSequence(LastAppliedChunkDeltaSequence))
            {
                ApplyAuthoritativeBlock(delta.Change.Position, delta.Change.NewBlock, trackChange: false);
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

            bufferedChunkDeltas.Add(message);
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
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

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
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(MutationResultMessage, HandleMutationResultMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(EnvironmentSnapshotMessage, HandleEnvironmentSnapshotMessage);
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
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MutationResultMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(EnvironmentSnapshotMessage);
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
            uint nextSequenceId = sequenceId + 1;
            return nextSequenceId == 0 ? 1 : nextSequenceId;
        }

        void WriteWorldSnapshotHeader(ref FastBufferWriter writer, int changedBlockCount)
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
                    changedBlockCount));
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
        }

        public static bool TryReadWorldSnapshotHeader(ref FastBufferReader reader, out WorldSnapshotHeader header)
        {
            header = default;

            if (reader.Length - reader.Position < 48)
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

            if (generationPreset < 0 ||
                generationPreset > (int)CreativeWorldGenerationPreset.VoidBuilder ||
                width <= 0 ||
                height <= 0 ||
                depth <= 0 ||
                chunkSize <= 0 ||
                groundHeight < 1 ||
                groundHeight >= height ||
                changedBlockCount < 0)
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
                changedBlockCount);
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
        }

        static ChunkDelta ReadChunkDelta(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out uint sequenceId);
            ChunkCoordinate chunk = ReadChunkCoordinate(ref reader);
            BlockChange change = ReadBlockChange(ref reader);
            return new ChunkDelta(sequenceId, chunk, change);
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

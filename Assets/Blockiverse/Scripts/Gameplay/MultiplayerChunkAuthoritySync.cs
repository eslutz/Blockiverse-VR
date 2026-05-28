using System;
using System.Collections.Generic;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class MultiplayerChunkAuthoritySync : MonoBehaviour
    {
        const string MutationRequestMessage = "Blockiverse.ChunkAuthority.MutationRequest";
        const string MutationDeltaMessage = "Blockiverse.ChunkAuthority.MutationDelta";
        const string ChunkSnapshotMessage = "Blockiverse.ChunkAuthority.ChunkSnapshot";
        const string MutationResultMessage = "Blockiverse.ChunkAuthority.MutationResult";
        const int MutationRequestMessageBytes = 128;
        const int MutationDeltaMessageBytes = 160;
        const int MutationResultMessageBytes = 128;
        const int SnapshotHeaderBytes = 80;
        const int SnapshotBlockBytes = 32;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] CreativeWorldManager worldManager;

        readonly Dictionary<uint, BlockMutationRequest> pendingMutationRequests = new();
        readonly List<PendingChunkDeltaMessage> bufferedChunkDeltas = new();
        readonly ChunkDeltaLog chunkDeltaLog = new();
        NetworkManager subscribedNetworkManager;
        BlockMutationAuthority mutationAuthority;
        uint nextMutationRequestId = 1;
        bool messagesRegistered;
        bool hasHostGenerationSnapshotForSession;

        public ChunkAuthorityBoundary CurrentBoundary { get; private set; } = ChunkAuthorityBoundary.ForHost();
        public BlockMutationAuthority MutationAuthority => ResolveMutationAuthority();
        public BlockMutationResult LastMutationResult { get; private set; }
        public bool IsClientRequestMode => IsActiveClientOnly() && CurrentBoundary.MustRequestMutations;
        public int SentMutationRequestCount { get; private set; }
        public int ReceivedMutationRequestCount { get; private set; }
        public int BroadcastDeltaCount { get; private set; }
        public int AppliedRemoteDeltaCount { get; private set; }
        public int AppliedChunkDeltaCount { get; private set; }
        public int IgnoredOutOfOrderChunkDeltaCount { get; private set; }
        public int SentLateJoinSnapshotCount { get; private set; }
        public int AppliedGenerationSnapshotCount { get; private set; }
        public int AppliedSnapshotBlockCount { get; private set; }
        public int ReceivedMutationRejectionCount { get; private set; }
        public int AcceptedMutationResponseCount { get; private set; }
        public int PendingMutationRequestCount => pendingMutationRequests.Count;
        public uint LastSentMutationRequestId { get; private set; }
        public uint LastReceivedMutationRequestId { get; private set; }
        public uint LastCompletedMutationRequestId { get; private set; }
        public uint LastBroadcastChunkDeltaSequence { get; private set; }
        public uint LastAppliedChunkDeltaSequence { get; private set; }
        public IReadOnlyList<ChunkDelta> RecordedChunkDeltas => chunkDeltaLog.Deltas;
        public bool HasHostGenerationSnapshotForSession => hasHostGenerationSnapshotForSession;

        enum ChunkDeltaApplyState
        {
            Applied,
            IgnoredStale,
            WaitingForEarlierDelta
        }

        public void Configure(BlockiverseNetworkSession targetSession, CreativeWorldManager targetWorldManager)
        {
            UnsubscribeNetworkCallbacks();
            session = targetSession;
            worldManager = targetWorldManager;
            worldManager?.ConfigureAuthoritySync(this);
            SubscribeNetworkCallbacks();
            RefreshAuthorityBoundary();
        }

        public void ConfigureWorld(CreativeWorldManager targetWorldManager)
        {
            worldManager = targetWorldManager;
            worldManager?.ConfigureAuthoritySync(this);
            mutationAuthority = null;
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
        }

        void OnDisable()
        {
            UnsubscribeNetworkCallbacks();
        }

        void OnDestroy()
        {
            UnsubscribeNetworkCallbacks();
        }

        public BlockMutationResult TrySubmitMutation(
            BlockMutationRequest request,
            out SetBlockCommand appliedCommand,
            out bool requestSentToHost)
        {
            appliedCommand = null;
            requestSentToHost = false;
            RefreshAuthorityBoundary();

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
            out bool requestSentToHost)
        {
            RefreshAuthorityBoundary();
            VoxelWorld world = ResolveWorld();
            var request = world.Bounds.Contains(position)
                ? new BlockMutationRequest(CurrentBoundary.LocalClientId, position, newBlock, world.GetBlock(position))
                : new BlockMutationRequest(CurrentBoundary.LocalClientId, position, newBlock);
            return TrySubmitMutation(request, out appliedCommand, out requestSentToHost);
        }

        public bool CanSaveMultiplayerWorld()
        {
            RefreshAuthorityBoundary();
            return CurrentBoundary.CanSaveMultiplayerWorld;
        }

        void HandleServerStarted()
        {
            RefreshAuthorityBoundary();
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
        }

        void HandleServerStopped(bool wasHost)
        {
            UnregisterMessageHandlers();
            RefreshAuthorityBoundary();
        }

        void HandleClientStopped(bool wasHost)
        {
            hasHostGenerationSnapshotForSession = false;
            ResetClientChunkDeltaState();
            ResetPendingMutationRequests();
            UnregisterMessageHandlers();
            RefreshAuthorityBoundary();
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

            BlockMutationRequest request = ReadMutationRequest(senderClientId, ref reader, out uint requestId);
            ReceivedMutationRequestCount++;
            LastReceivedMutationRequestId = requestId;
            BlockMutationResult result = ResolveMutationAuthority().TryCommit(request, out _).WithRpcRequestId(requestId);
            LastMutationResult = result;

            if (result.Accepted)
            {
                BroadcastDelta(result.Change, request.RequestingClientId, requestId);
            }
            else
            {
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

            ApplyWorldSnapshotHeader(ref reader, out int blockCount);

            for (int index = 0; index < blockCount; index++)
            {
                BlockPosition position = ReadBlockPosition(ref reader);
                reader.ReadValueSafe(out int blockId);
                ApplyAuthoritativeBlock(position, new BlockId(blockId), trackChange: false);
                AppliedSnapshotBlockCount++;
            }

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
                    writer);
                SentLateJoinSnapshotCount++;
            }
            finally
            {
                writer.Dispose();
            }
        }

        void ApplyAuthoritativeBlock(BlockPosition position, BlockId block, bool trackChange = true)
        {
            VoxelWorld world = ResolveWorld();

            if (!world.Bounds.Contains(position))
                return;

            world.SetBlock(position, block, trackChange);
            worldManager.Renderer?.RebuildDirty();
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
            var clientIds = new List<ulong>();

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId != networkManager.LocalClientId)
                    clientIds.Add(clientId);
            }

            if (clientIds.Count > 0)
                networkManager.CustomMessagingManager.SendNamedMessage(messageName, clientIds, writer);
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

        BlockRegistry ResolveRegistry()
        {
            ResolveWorldManager();

            if (worldManager.Registry == null)
            {
                if (CurrentBoundary.MustRequestMutations)
                    return BlockRegistry.CreateDefault();

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

            worldManager?.ConfigureAuthoritySync(this);
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
            messagesRegistered = false;
        }

        NetworkManager ResolveNetworkManager()
        {
            return ResolveNetworkManagerOrNull() ?? throw new InvalidOperationException("Multiplayer chunk authority requires a network session.");
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

            writer.WriteValueSafe((int)worldManager.GenerationPreset);
            writer.WriteValueSafe(world.Bounds.Width);
            writer.WriteValueSafe(world.Bounds.Height);
            writer.WriteValueSafe(world.Bounds.Depth);
            writer.WriteValueSafe(world.ChunkSize);
            writer.WriteValueSafe(world.Seed);
            writer.WriteValueSafe(groundHeight);
            writer.WriteValueSafe(chunkDeltaLog.LastSequenceId);
            writer.WriteValueSafe(changedBlockCount);
        }

        void ApplyWorldSnapshotHeader(ref FastBufferReader reader, out int changedBlockCount)
        {
            reader.ReadValueSafe(out int generationPreset);
            reader.ReadValueSafe(out int width);
            reader.ReadValueSafe(out int height);
            reader.ReadValueSafe(out int depth);
            reader.ReadValueSafe(out int chunkSize);
            reader.ReadValueSafe(out int seed);
            reader.ReadValueSafe(out int groundHeight);
            reader.ReadValueSafe(out uint hostDeltaSequence);
            reader.ReadValueSafe(out changedBlockCount);

            var preset = (CreativeWorldGenerationPreset)generationPreset;
            var settings = new WorldGenerationSettings(width, height, depth, chunkSize, seed, groundHeight);
            BlockRegistry registry = BlockRegistry.CreateDefault();
            VoxelWorld world = preset == CreativeWorldGenerationPreset.FlatCreative
                ? new FlatCreativeWorldPreset(registry, settings).Generate()
                : new SurvivalLiteWorldPreset(registry, settings).Generate();
            worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, preset), this);
            LastAppliedChunkDeltaSequence = hostDeltaSequence;
            hasHostGenerationSnapshotForSession = true;
            AppliedGenerationSnapshotCount++;
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

        static BlockMutationRequest ReadMutationRequest(ulong requestingClientId, ref FastBufferReader reader, out uint requestId)
        {
            reader.ReadValueSafe(out requestId);
            BlockPosition position = ReadBlockPosition(ref reader);
            reader.ReadValueSafe(out int newBlock);
            reader.ReadValueSafe(out bool hasExpectedCurrentBlock);
            reader.ReadValueSafe(out int expectedCurrentBlock);

            if (hasExpectedCurrentBlock)
                return new BlockMutationRequest(requestingClientId, position, new BlockId(newBlock), new BlockId(expectedCurrentBlock));

            return new BlockMutationRequest(requestingClientId, position, new BlockId(newBlock));
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

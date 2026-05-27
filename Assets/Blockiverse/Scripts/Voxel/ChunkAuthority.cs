using System;

namespace Blockiverse.Voxel
{
    public enum ChunkAuthorityRole
    {
        Host,
        Client
    }

    public readonly struct ChunkAuthorityBoundary : IEquatable<ChunkAuthorityBoundary>
    {
        ChunkAuthorityBoundary(ChunkAuthorityRole role, ulong hostClientId, ulong localClientId)
        {
            Role = role;
            HostClientId = hostClientId;
            LocalClientId = localClientId;
        }

        public ChunkAuthorityRole Role { get; }
        public ulong HostClientId { get; }
        public ulong LocalClientId { get; }
        public bool IsHost => Role == ChunkAuthorityRole.Host;
        public bool OwnsChunkGeneration => IsHost;
        public bool OwnsMutationValidation => IsHost;
        public bool CanCommitMutations => IsHost;
        public bool CanBroadcastDeltas => IsHost;
        public bool CanServeLateJoinSync => IsHost;
        public bool CanSaveMultiplayerWorld => IsHost;
        public bool MustRequestMutations => !IsHost;

        public static ChunkAuthorityBoundary ForHost(ulong hostClientId = 0)
        {
            return new ChunkAuthorityBoundary(ChunkAuthorityRole.Host, hostClientId, hostClientId);
        }

        public static ChunkAuthorityBoundary ForClient(ulong localClientId, ulong hostClientId = 0)
        {
            if (localClientId == hostClientId)
                throw new ArgumentException("Client authority requires a local client ID that differs from the host.", nameof(localClientId));

            return new ChunkAuthorityBoundary(ChunkAuthorityRole.Client, hostClientId, localClientId);
        }

        public bool Equals(ChunkAuthorityBoundary other)
        {
            return Role == other.Role &&
                   HostClientId == other.HostClientId &&
                   LocalClientId == other.LocalClientId;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkAuthorityBoundary other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Role, HostClientId, LocalClientId);
        }

        public static bool operator ==(ChunkAuthorityBoundary left, ChunkAuthorityBoundary right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ChunkAuthorityBoundary left, ChunkAuthorityBoundary right)
        {
            return !left.Equals(right);
        }
    }

    public enum BlockMutationRejectionReason
    {
        None,
        ClientCannotCommitAuthoritativeState,
        PositionOutOfBounds,
        UnknownBlock,
        ExpectedBlockMismatch,
        NoChange,
        RequestSentToHost,
        HostOnlyAuthorityOperation
    }

    public readonly struct BlockMutationRequest : IEquatable<BlockMutationRequest>
    {
        public BlockMutationRequest(ulong requestingClientId, BlockPosition position, BlockId newBlock)
            : this(requestingClientId, position, newBlock, default, hasExpectedCurrentBlock: false)
        {
        }

        public BlockMutationRequest(ulong requestingClientId, BlockPosition position, BlockId newBlock, BlockId expectedCurrentBlock)
            : this(requestingClientId, position, newBlock, expectedCurrentBlock, hasExpectedCurrentBlock: true)
        {
        }

        BlockMutationRequest(
            ulong requestingClientId,
            BlockPosition position,
            BlockId newBlock,
            BlockId expectedCurrentBlock,
            bool hasExpectedCurrentBlock)
        {
            RequestingClientId = requestingClientId;
            Position = position;
            NewBlock = newBlock;
            ExpectedCurrentBlock = expectedCurrentBlock;
            HasExpectedCurrentBlock = hasExpectedCurrentBlock;
        }

        public ulong RequestingClientId { get; }
        public BlockPosition Position { get; }
        public BlockId NewBlock { get; }
        public BlockId ExpectedCurrentBlock { get; }
        public bool HasExpectedCurrentBlock { get; }

        public bool Equals(BlockMutationRequest other)
        {
            return RequestingClientId == other.RequestingClientId &&
                   Position == other.Position &&
                   NewBlock == other.NewBlock &&
                   ExpectedCurrentBlock == other.ExpectedCurrentBlock &&
                   HasExpectedCurrentBlock == other.HasExpectedCurrentBlock;
        }

        public override bool Equals(object obj)
        {
            return obj is BlockMutationRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RequestingClientId, Position, NewBlock, ExpectedCurrentBlock, HasExpectedCurrentBlock);
        }
    }

    public readonly struct BlockMutationResult
    {
        BlockMutationResult(
            bool accepted,
            BlockMutationRejectionReason rejectionReason,
            BlockChange change,
            ChunkCoordinate chunk,
            string message,
            bool pendingHostValidation = false)
        {
            Accepted = accepted;
            RejectionReason = rejectionReason;
            Change = change;
            Chunk = chunk;
            Message = message;
            PendingHostValidation = pendingHostValidation;
        }

        public bool Accepted { get; }
        public BlockMutationRejectionReason RejectionReason { get; }
        public BlockChange Change { get; }
        public ChunkCoordinate Chunk { get; }
        public string Message { get; }
        public bool PendingHostValidation { get; }

        public static BlockMutationResult Accept(BlockChange change, ChunkCoordinate chunk)
        {
            return new BlockMutationResult(
                accepted: true,
                BlockMutationRejectionReason.None,
                change,
                chunk,
                string.Empty);
        }

        public static BlockMutationResult RequestSent(ChunkCoordinate chunk)
        {
            return new BlockMutationResult(
                accepted: false,
                BlockMutationRejectionReason.RequestSentToHost,
                default,
                chunk,
                "Mutation request sent to host for authoritative validation.",
                pendingHostValidation: true);
        }

        public static BlockMutationResult Reject(
            BlockMutationRejectionReason rejectionReason,
            ChunkCoordinate chunk,
            string message)
        {
            return new BlockMutationResult(
                accepted: false,
                rejectionReason,
                default,
                chunk,
                message);
        }
    }

    public sealed class BlockMutationAuthority
    {
        readonly VoxelWorld world;
        readonly BlockRegistry registry;

        public BlockMutationAuthority(VoxelWorld voxelWorld, BlockRegistry blockRegistry, ChunkAuthorityBoundary boundary)
        {
            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            Boundary = boundary;
        }

        public ChunkAuthorityBoundary Boundary { get; }

        public static BlockMutationAuthority CreateHost(VoxelWorld voxelWorld, BlockRegistry blockRegistry, ulong hostClientId = 0)
        {
            return new BlockMutationAuthority(voxelWorld, blockRegistry, ChunkAuthorityBoundary.ForHost(hostClientId));
        }

        public static BlockMutationAuthority CreateClientProxy(
            VoxelWorld voxelWorld,
            BlockRegistry blockRegistry,
            ulong localClientId,
            ulong hostClientId = 0)
        {
            return new BlockMutationAuthority(voxelWorld, blockRegistry, ChunkAuthorityBoundary.ForClient(localClientId, hostClientId));
        }

        public BlockMutationResult ValidateHostMutation(BlockMutationRequest request)
        {
            ChunkCoordinate chunk = ChunkCoordinate.FromBlockPosition(request.Position, world.ChunkSize);

            if (!Boundary.OwnsMutationValidation)
            {
                return BlockMutationResult.Reject(
                    BlockMutationRejectionReason.HostOnlyAuthorityOperation,
                    chunk,
                    "Clients must send mutation requests to the host instead of running authoritative validation.");
            }

            return ValidateMutationRules(request);
        }

        BlockMutationResult ValidateMutationRules(BlockMutationRequest request)
        {
            ChunkCoordinate chunk = ChunkCoordinate.FromBlockPosition(request.Position, world.ChunkSize);

            if (!world.Bounds.Contains(request.Position))
            {
                return BlockMutationResult.Reject(
                    BlockMutationRejectionReason.PositionOutOfBounds,
                    chunk,
                    $"Block position is outside world bounds: {request.Position}");
            }

            if (!registry.TryGet(request.NewBlock, out _))
            {
                return BlockMutationResult.Reject(
                    BlockMutationRejectionReason.UnknownBlock,
                    chunk,
                    $"Block ID is not registered: {request.NewBlock}");
            }

            BlockId previousBlock = world.GetBlock(request.Position);

            if (request.HasExpectedCurrentBlock && previousBlock != request.ExpectedCurrentBlock)
            {
                return BlockMutationResult.Reject(
                    BlockMutationRejectionReason.ExpectedBlockMismatch,
                    chunk,
                    $"Expected block {request.ExpectedCurrentBlock} but found {previousBlock} at {request.Position}");
            }

            if (previousBlock == request.NewBlock)
            {
                return BlockMutationResult.Reject(
                    BlockMutationRejectionReason.NoChange,
                    chunk,
                    $"Block already matches requested value at {request.Position}");
            }

            return BlockMutationResult.Accept(new BlockChange(request.Position, previousBlock, request.NewBlock), chunk);
        }

        public BlockMutationResult TryCommit(BlockMutationRequest request)
        {
            return TryCommit(request, out _);
        }

        public BlockMutationResult TryCommit(BlockMutationRequest request, out SetBlockCommand appliedCommand)
        {
            appliedCommand = null;
            ChunkCoordinate chunk = ChunkCoordinate.FromBlockPosition(request.Position, world.ChunkSize);

            if (!Boundary.CanCommitMutations)
            {
                return BlockMutationResult.Reject(
                    BlockMutationRejectionReason.ClientCannotCommitAuthoritativeState,
                    chunk,
                    "Clients must request host validation instead of committing authoritative chunk mutations.");
            }

            BlockMutationResult validation = ValidateHostMutation(request);

            if (!validation.Accepted)
                return validation;

            var command = new SetBlockCommand(request.Position, request.NewBlock);
            BlockChange change = command.Execute(world);
            appliedCommand = command;
            return BlockMutationResult.Accept(change, validation.Chunk);
        }
    }
}

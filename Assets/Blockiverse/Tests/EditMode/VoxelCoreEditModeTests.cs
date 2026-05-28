using System;
using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class VoxelCoreEditModeTests
    {
        [Test]
        public void DefaultRegistryContainsOriginalM2BlockList()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            string[] expectedNames =
            {
                "Air",
                "Meadow Turf",
                "Loam",
                "Slate",
                "Timber",
                "Leafmass",
                "Clearstone",
                "Coalstone",
                "Copperstone",
                "Ironstone",
                "Workbench",
                "Torchbud",
                "Storage Crate"
            };

            CollectionAssert.AreEquivalent(expectedNames, registry.All.Select(block => block.Name));
            Assert.That(registry.Get(BlockRegistry.Air).Category, Is.EqualTo(BlockCategory.Air));
            Assert.That(registry.Get(BlockRegistry.MeadowTurf).Category, Is.EqualTo(BlockCategory.Terrain));
            Assert.That(registry.Get(BlockRegistry.Timber).Category, Is.EqualTo(BlockCategory.Organic));
            Assert.That(registry.Get(BlockRegistry.Workbench).Category, Is.EqualTo(BlockCategory.Crafted));
            Assert.That(registry.Get(BlockRegistry.Coalstone).Category, Is.EqualTo(BlockCategory.Resource));
        }

        [Test]
        public void RegistryRejectsDuplicateIds()
        {
            var registry = new BlockRegistry();
            registry.Register(new BlockDefinition(new BlockId(7), "First", BlockCategory.Terrain, isSolid: true, isRenderable: true));

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new BlockDefinition(new BlockId(7), "Second", BlockCategory.Terrain, isSolid: true, isRenderable: true)));
        }

        [Test]
        public void ChunkCoordinatesMapBoundaryAndNegativePositions()
        {
            Assert.That(ChunkCoordinate.FromBlockPosition(new BlockPosition(0, 0, 0), 16), Is.EqualTo(new ChunkCoordinate(0, 0, 0)));
            Assert.That(ChunkCoordinate.FromBlockPosition(new BlockPosition(15, 15, 15), 16), Is.EqualTo(new ChunkCoordinate(0, 0, 0)));
            Assert.That(ChunkCoordinate.FromBlockPosition(new BlockPosition(16, 16, 16), 16), Is.EqualTo(new ChunkCoordinate(1, 1, 1)));
            Assert.That(ChunkCoordinate.FromBlockPosition(new BlockPosition(-1, -1, -1), 16), Is.EqualTo(new ChunkCoordinate(-1, -1, -1)));

            Assert.That(ChunkCoordinate.LocalPositionFromBlockPosition(new BlockPosition(16, 17, 31), 16), Is.EqualTo(new BlockPosition(0, 1, 15)));
            Assert.That(ChunkCoordinate.LocalPositionFromBlockPosition(new BlockPosition(-1, -17, -32), 16), Is.EqualTo(new BlockPosition(15, 15, 0)));
        }

        [Test]
        public void BoundedWorldStoresBlocksAcrossChunkBoundaries()
        {
            var world = new VoxelWorld(new WorldBounds(32, 16, 32), chunkSize: 16, seed: 42);
            var first = new BlockPosition(15, 1, 15);
            var second = new BlockPosition(16, 1, 16);

            world.SetBlock(first, BlockRegistry.MeadowTurf);
            world.SetBlock(second, BlockRegistry.Slate);

            Assert.That(world.GetBlock(first), Is.EqualTo(BlockRegistry.MeadowTurf));
            Assert.That(world.GetBlock(second), Is.EqualTo(BlockRegistry.Slate));
            Assert.That(world.GetChunkCoordinate(first), Is.EqualTo(new ChunkCoordinate(0, 0, 0)));
            Assert.That(world.GetChunkCoordinate(second), Is.EqualTo(new ChunkCoordinate(1, 0, 1)));
        }

        [Test]
        public void UntrackedBlockMutationDoesNotRecordPersistenceChangeButQueuesRenderChange()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 42);
            var position = new BlockPosition(1, 1, 1);
            var rebuildQueue = new ChunkRebuildQueue(world);
            int eventCount = 0;
            world.BlockChanged += _ => eventCount++;

            world.SetBlock(position, BlockRegistry.Slate, trackChange: false);

            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Slate));
            Assert.That(world.GetChangedBlocks(), Is.Empty);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(rebuildQueue.Count, Is.EqualTo(1));
        }

        [Test]
        public void BoundedWorldRejectsOutOfRangeCoordinates()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 7);

            Assert.That(world.Bounds.Contains(new BlockPosition(3, 3, 3)), Is.True);
            Assert.That(world.Bounds.Contains(new BlockPosition(4, 3, 3)), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => world.GetBlock(new BlockPosition(-1, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.SetBlock(new BlockPosition(0, 4, 0), BlockRegistry.Loam));
        }

        [Test]
        public void SetBlockCommandCanUndoMutation()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 11);
            var position = new BlockPosition(1, 1, 1);
            world.SetBlock(position, BlockRegistry.Loam, trackChange: false);

            var command = new SetBlockCommand(position, BlockRegistry.Clearstone);
            BlockChange change = command.Execute(world);

            Assert.That(change.PreviousBlock, Is.EqualTo(BlockRegistry.Loam));
            Assert.That(change.NewBlock, Is.EqualTo(BlockRegistry.Clearstone));
            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Clearstone));

            command.Undo(world);

            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Loam));
        }

        [Test]
        public void HostBoundaryOwnsChunkAuthorityResponsibilities()
        {
            ChunkAuthorityBoundary host = ChunkAuthorityBoundary.ForHost(hostClientId: 0);
            ChunkAuthorityBoundary client = ChunkAuthorityBoundary.ForClient(localClientId: 7, hostClientId: 0);

            Assert.That(host.OwnsChunkGeneration, Is.True);
            Assert.That(host.OwnsMutationValidation, Is.True);
            Assert.That(host.CanCommitMutations, Is.True);
            Assert.That(host.CanBroadcastDeltas, Is.True);
            Assert.That(host.CanServeLateJoinSync, Is.True);
            Assert.That(host.CanSaveMultiplayerWorld, Is.True);
            Assert.That(host.MustRequestMutations, Is.False);

            Assert.That(client.OwnsChunkGeneration, Is.False);
            Assert.That(client.OwnsMutationValidation, Is.False);
            Assert.That(client.CanCommitMutations, Is.False);
            Assert.That(client.CanBroadcastDeltas, Is.False);
            Assert.That(client.CanServeLateJoinSync, Is.False);
            Assert.That(client.CanSaveMultiplayerWorld, Is.False);
            Assert.That(client.MustRequestMutations, Is.True);
        }

        [Test]
        public void HostAuthorityValidatesAndCommitsClientMutationRequest()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 2, seed: 17);
            var position = new BlockPosition(3, 1, 1);
            world.SetBlock(position, BlockRegistry.Loam, trackChange: false);
            BlockMutationAuthority authority = BlockMutationAuthority.CreateHost(world, registry);
            var request = new BlockMutationRequest(
                requestingClientId: 7,
                position,
                BlockRegistry.Clearstone,
                expectedCurrentBlock: BlockRegistry.Loam);

            BlockMutationResult result = authority.TryCommit(request, out SetBlockCommand command);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.None));
            Assert.That(result.Change.Position, Is.EqualTo(position));
            Assert.That(result.Change.PreviousBlock, Is.EqualTo(BlockRegistry.Loam));
            Assert.That(result.Change.NewBlock, Is.EqualTo(BlockRegistry.Clearstone));
            Assert.That(result.Chunk, Is.EqualTo(new ChunkCoordinate(1, 0, 0)));
            Assert.That(command, Is.Not.Null);
            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Clearstone));
        }

        [Test]
        public void ChunkDeltaLogReplayReconstructsAcceptedMutations()
        {
            var sourceWorld = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 2, seed: 21);
            var replayWorld = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 2, seed: 21);
            var firstPosition = new BlockPosition(1, 1, 1);
            var secondPosition = new BlockPosition(3, 1, 1);
            var log = new ChunkDeltaLog();

            ChunkDelta firstDelta = log.Record(new SetBlockCommand(firstPosition, BlockRegistry.Clearstone).Execute(sourceWorld), sourceWorld.ChunkSize);
            ChunkDelta secondDelta = log.Record(new SetBlockCommand(secondPosition, BlockRegistry.Slate).Execute(sourceWorld), sourceWorld.ChunkSize);

            ChunkDeltaLog.Replay(replayWorld, log.Deltas);

            Assert.That(firstDelta.SequenceId, Is.EqualTo(1));
            Assert.That(firstDelta.Chunk, Is.EqualTo(new ChunkCoordinate(0, 0, 0)));
            Assert.That(secondDelta.SequenceId, Is.EqualTo(2));
            Assert.That(secondDelta.Chunk, Is.EqualTo(new ChunkCoordinate(1, 0, 0)));
            Assert.That(log.LastSequenceId, Is.EqualTo(2));
            Assert.That(replayWorld.GetBlock(firstPosition), Is.EqualTo(sourceWorld.GetBlock(firstPosition)));
            Assert.That(replayWorld.GetBlock(secondPosition), Is.EqualTo(sourceWorld.GetBlock(secondPosition)));
            Assert.That(replayWorld.GetChangedBlocks(), Is.Empty);
        }

        [Test]
        public void ClientProxyCannotCommitAuthoritativeMutation()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 2, seed: 18);
            var position = new BlockPosition(1, 1, 1);
            world.SetBlock(position, BlockRegistry.Loam, trackChange: false);
            BlockMutationAuthority authority = BlockMutationAuthority.CreateClientProxy(world, registry, localClientId: 7);

            BlockMutationResult result = authority.TryCommit(
                new BlockMutationRequest(7, position, BlockRegistry.Clearstone),
                out SetBlockCommand command);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.ClientCannotCommitAuthoritativeState));
            Assert.That(command, Is.Null);
            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Loam));

            BlockMutationResult validationResult = authority.ValidateHostMutation(
                new BlockMutationRequest(7, position, BlockRegistry.Clearstone));
            Assert.That(validationResult.Accepted, Is.False);
            Assert.That(validationResult.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.HostOnlyAuthorityOperation));
        }

        [Test]
        public void HostAuthorityRejectsInvalidMutationRequestsPredictably()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 2, seed: 19);
            var position = new BlockPosition(1, 1, 1);
            world.SetBlock(position, BlockRegistry.Loam, trackChange: false);
            BlockMutationAuthority authority = BlockMutationAuthority.CreateHost(world, registry);

            Assert.That(
                authority.TryCommit(new BlockMutationRequest(7, new BlockPosition(-1, 1, 1), BlockRegistry.Slate)).RejectionReason,
                Is.EqualTo(BlockMutationRejectionReason.PositionOutOfBounds));
            Assert.That(
                authority.TryCommit(new BlockMutationRequest(7, position, new BlockId(999))).RejectionReason,
                Is.EqualTo(BlockMutationRejectionReason.UnknownBlock));
            Assert.That(
                authority.TryCommit(new BlockMutationRequest(7, position, BlockRegistry.Slate, expectedCurrentBlock: BlockRegistry.Clearstone)).RejectionReason,
                Is.EqualTo(BlockMutationRejectionReason.ExpectedBlockMismatch));
            Assert.That(
                authority.TryCommit(new BlockMutationRequest(7, position, BlockRegistry.Loam)).RejectionReason,
                Is.EqualTo(BlockMutationRejectionReason.NoChange));
            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Loam));
        }

        [Test]
        public void FlatCreativePresetCreatesBoundedSpawnSafeWorld()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(width: 16, height: 8, depth: 16, chunkSize: 16, seed: 123, groundHeight: 2);
            var preset = new FlatCreativeWorldPreset(registry, settings);

            VoxelWorld world = preset.Generate();

            Assert.That(world.Bounds, Is.EqualTo(new WorldBounds(16, 8, 16)));
            Assert.That(world.Seed, Is.EqualTo(123));
            Assert.That(world.GetBlock(new BlockPosition(0, 0, 0)), Is.EqualTo(BlockRegistry.Loam));
            Assert.That(world.GetBlock(new BlockPosition(0, 1, 0)), Is.EqualTo(BlockRegistry.MeadowTurf));
            Assert.That(world.GetBlock(new BlockPosition(0, 2, 0)), Is.EqualTo(BlockRegistry.Air));
            Assert.That(world.GetChangedBlocks(), Is.Empty);
            Assert.That(world.Bounds.Contains(settings.SpawnPosition), Is.True);
            Assert.That(world.GetBlock(settings.SpawnPosition), Is.EqualTo(BlockRegistry.Air));
        }
    }
}

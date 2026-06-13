using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class VoxelCoreEditModeTests
    {
        [Test]
        public void DefaultRegistryContainsCanonicalBlockSet()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            Assert.That(registry.All.Count, Is.EqualTo(81));
            Assert.That(registry.Get(BlockRegistry.Air).Category, Is.EqualTo(BlockCategory.Air));
            Assert.That(registry.Get(BlockRegistry.MeadowTurf).Category, Is.EqualTo(BlockCategory.Terrain));
            Assert.That(registry.Get(BlockRegistry.BranchwoodLog).Category, Is.EqualTo(BlockCategory.Organic));
            Assert.That(registry.Get(BlockRegistry.BuildTable).Category, Is.EqualTo(BlockCategory.Station));
            Assert.That(registry.Get(BlockRegistry.EmbercoalSeam).Category, Is.EqualTo(BlockCategory.Resource));
            // Fluids (§5.4): renderable non-solid sources/flow cells the player can pass through.
            Assert.That(registry.Get(BlockRegistry.Freshwater).Category, Is.EqualTo(BlockCategory.Fluid));
            Assert.That(registry.Get(BlockRegistry.Brine).Category, Is.EqualTo(BlockCategory.Fluid));
            Assert.That(registry.Get(BlockRegistry.Emberflow).Category, Is.EqualTo(BlockCategory.Fluid));
            Assert.That(registry.Get(BlockRegistry.FreshwaterFlow).Category, Is.EqualTo(BlockCategory.Fluid));
            Assert.That(registry.Get(BlockRegistry.BrineFlow).Category, Is.EqualTo(BlockCategory.Fluid));
            Assert.That(registry.Get(BlockRegistry.EmberflowFlow).Category, Is.EqualTo(BlockCategory.Fluid));
            Assert.That(registry.Get(BlockRegistry.Freshwater).IsSolid, Is.False);
            Assert.That(registry.Get(BlockRegistry.Brine).IsSolid, Is.False);
            Assert.That(registry.Get(BlockRegistry.Emberflow).IsSolid, Is.False);
            Assert.That(registry.Get(BlockRegistry.EmberflowFlow).IsSolid, Is.False);
            Assert.That(registry.Get(BlockRegistry.Freshwater).IsRenderable, Is.True);
            // Emberflow glows at the §8 canonical light level; its flow cells glow slightly dimmer.
            Assert.That(registry.Get(BlockRegistry.Emberflow).EmissiveLight, Is.EqualTo(10));
            Assert.That(registry.Get(BlockRegistry.EmberflowFlow).EmissiveLight, Is.EqualTo(9));
            Assert.That(registry.Get(BlockRegistry.Bedroll).IsSolid, Is.False);
            Assert.That(registry.Get(BlockRegistry.Graystone).DisplayKey, Is.EqualTo("block.graystone.name"));
            Assert.That(registry.All.Select(b => b.Name), Has.Member("Graystone"));
            Assert.That(registry.All.Select(b => b.Name), Has.Member("Dark Slate"));
            Assert.That(registry.All.Select(b => b.Name), Has.Member("Glowwick"));
            Assert.That(registry.All.Select(b => b.Name), Has.Member("Smooth Branchwood"));
            Assert.That(registry.All.Select(b => b.Name), Has.Member("Deep Locker"));
            Assert.That(registry.All.Select(b => b.Name), Has.Member("Bedroll"));
        }

        [Test]
        public void SharedDefaultRegistryIsStableAndMatchesFactoryContents()
        {
            BlockRegistry shared = BlockRegistry.Default;
            BlockRegistry factory = BlockRegistry.CreateDefault();

            Assert.That(BlockRegistry.Default, Is.SameAs(shared));
            Assert.That(shared.All.Count, Is.EqualTo(factory.All.Count));
            Assert.That(shared.Get(BlockRegistry.SmoothBranchwood).CanonicalId, Is.EqualTo("smooth_branchwood"));
            Assert.That(shared.Get(BlockRegistry.Bedroll).IsSolid, Is.False);
        }

        [Test]
        public void DeterministicHashGoldenValuesGuardWorldRollCompatibility()
        {
            Assert.That(DeterministicHash.Hash(0, 0, 0, 0, 0), Is.EqualTo(0xca889355u));
            Assert.That(DeterministicHash.Hash(1, 2, 3, 4, 5), Is.EqualTo(0x9db240e2u));
            Assert.That(DeterministicHash.Hash(-12345, 67, -89, 10, 42), Is.EqualTo(0x1fc14182u));
            Assert.That(DeterministicHash.Hash(2202, 15, 4, 9, BlockRegistry.Emberflow.Value), Is.EqualTo(0x8ce79b82u));

            Assert.That(DeterministicHash.UnitRoll(0, 0, 0, 0, 0, 0L), Is.EqualTo(0.5902278602588922d).Within(1e-16));
            Assert.That(DeterministicHash.UnitRoll(42, 2, 4, 6, BlockRegistry.Thornbrush.Value, 1L), Is.EqualTo(0.5138001018203795d).Within(1e-16));
            Assert.That(DeterministicHash.UnitRoll(-12345, 67, -89, 10, 42, 9876543210L), Is.EqualTo(0.7407544837333262d).Within(1e-16));
            Assert.That(DeterministicHash.UnitRoll(2202, 15, 4, 9, BlockRegistry.Emberflow.Value, 1099511627793L), Is.EqualTo(0.23445059009827673d).Within(1e-16));
        }

        [Test]
        public void BlockInteractionReachUsesNearestBlockBounds()
        {
            Vector3 origin = new(0.5f, 1.6f, 0.5f);

            Assert.That(
                CreativeInteractionController.IsBlockWithinInteractionReach(origin, new BlockPosition(6, 1, 0)),
                Is.True,
                "A block whose nearest face is within the 6 m gameplay reach should be accepted.");

            Assert.That(
                CreativeInteractionController.IsBlockWithinInteractionReach(origin, new BlockPosition(8, 1, 0)),
                Is.False,
                "Distant block commands must be rejected before the host mutates world or inventory state.");
        }

        [Test]
        public void RegistryRejectsDuplicateIds()
        {
            var registry = new BlockRegistry();
            registry.Register(new BlockDefinition(new BlockId(7), "test_first", "First", BlockCategory.Terrain, isSolid: true, isRenderable: true));

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new BlockDefinition(new BlockId(7), "test_second", "Second", BlockCategory.Terrain, isSolid: true, isRenderable: true)));
        }

        [Test]
        public void RegistryKeysBlocksByCanonicalIdNotDisplayName()
        {
            var registry = new BlockRegistry();
            registry.Register(new BlockDefinition(new BlockId(7), "test_first", "Shared Display", BlockCategory.Terrain, isSolid: true, isRenderable: true));
            registry.Register(new BlockDefinition(new BlockId(8), "test_second", "Shared Display", BlockCategory.Terrain, isSolid: true, isRenderable: true));

            Assert.That(registry.TryGetByCanonicalId("test_first", out BlockDefinition first), Is.True);
            Assert.That(first.Id, Is.EqualTo(new BlockId(7)));
            Assert.That(first.DisplayKey, Is.EqualTo("block.test_first.name"));
            Assert.That(registry.TryGetByCanonicalId("Shared Display", out _), Is.False);
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
            world.SetBlock(second, BlockRegistry.Graystone);

            Assert.That(world.GetBlock(first), Is.EqualTo(BlockRegistry.MeadowTurf));
            Assert.That(world.GetBlock(second), Is.EqualTo(BlockRegistry.Graystone));
            Assert.That(world.GetChunkCoordinate(first), Is.EqualTo(new ChunkCoordinate(0, 0, 0)));
            Assert.That(world.GetChunkCoordinate(second), Is.EqualTo(new ChunkCoordinate(1, 0, 1)));
        }

        [Test]
        public void CollectBlockPositionsFindsMultipleTargetBlocks()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 42);
            var grain = new BlockPosition(1, 1, 1);
            var berry = new BlockPosition(2, 2, 1);
            var ignored = new BlockPosition(3, 1, 1);
            var targets = new HashSet<BlockId>
            {
                BlockRegistry.GrainStalk,
                BlockRegistry.Berrybush,
            };
            var results = new HashSet<BlockPosition>();

            world.SetBlock(grain, BlockRegistry.GrainStalk);
            world.SetBlock(berry, BlockRegistry.Berrybush);
            world.SetBlock(ignored, BlockRegistry.Reedgrass);

            world.CollectBlockPositions(targets, results);

            Assert.That(results, Is.EquivalentTo(new[] { grain, berry }));
        }

        [Test]
        public void UntrackedBlockMutationDoesNotRecordPersistenceChangeButQueuesRenderChange()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 42);
            var position = new BlockPosition(1, 1, 1);
            var rebuildQueue = new ChunkRebuildQueue(world);
            int eventCount = 0;
            world.BlockChanged += _ => eventCount++;

            world.SetBlock(position, BlockRegistry.Graystone, trackChange: false);

            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Graystone));
            Assert.That(world.GetChangedBlocks(), Is.Empty);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(rebuildQueue.Count, Is.EqualTo(1));
        }

        [Test]
        public void TrackedBlockMutationKeepsOriginalBaselineAndDropsWhenReverted()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 42);
            var position = new BlockPosition(1, 1, 1);

            world.SetBlock(position, BlockRegistry.Graystone);
            world.SetBlock(position, BlockRegistry.WorkPlank);

            BlockChange delta = world.GetChangedBlocks().Single();
            Assert.That(delta.PreviousBlock, Is.EqualTo(BlockRegistry.Air));
            Assert.That(delta.NewBlock, Is.EqualTo(BlockRegistry.WorkPlank));

            world.SetBlock(position, BlockRegistry.Air);

            Assert.That(world.GetChangedBlocks(), Is.Empty);
        }

        [Test]
        public void SetBlockReconcilesTrackedChangesAgainstExistingBaseline()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 42);
            var position = new BlockPosition(1, 1, 1);

            world.SetBlock(position, BlockRegistry.Graystone, trackChange: true);
            world.SetBlock(position, BlockRegistry.WorkPlank, trackChange: true);

            BlockChange delta = world.GetChangedBlocks().Single();
            Assert.That(delta.PreviousBlock, Is.EqualTo(BlockRegistry.Air));
            Assert.That(delta.NewBlock, Is.EqualTo(BlockRegistry.WorkPlank));

            world.SetBlock(position, BlockRegistry.Air, trackChange: true);

            Assert.That(world.GetChangedBlocks(), Is.Empty);
        }

        [Test]
        public void BoundedWorldRejectsOutOfRangeCoordinates()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 7);

            Assert.That(world.Bounds.Contains(new BlockPosition(3, 3, 3)), Is.True);
            Assert.That(world.Bounds.Contains(new BlockPosition(4, 3, 3)), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => world.GetBlock(new BlockPosition(-1, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.SetBlock(new BlockPosition(0, 4, 0), BlockRegistry.LooseLoam));
        }

        [Test]
        public void SetBlockCommandCanUndoMutation()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 11);
            var position = new BlockPosition(1, 1, 1);
            world.SetBlock(position, BlockRegistry.LooseLoam, trackChange: false);

            var command = new SetBlockCommand(position, BlockRegistry.LumenQuartzCluster);
            BlockChange change = command.Execute(world);

            Assert.That(change.PreviousBlock, Is.EqualTo(BlockRegistry.LooseLoam));
            Assert.That(change.NewBlock, Is.EqualTo(BlockRegistry.LumenQuartzCluster));
            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.LumenQuartzCluster));

            command.Undo(world);

            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.LooseLoam));
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
            world.SetBlock(position, BlockRegistry.LooseLoam, trackChange: false);
            BlockMutationAuthority authority = BlockMutationAuthority.CreateHost(world, registry);
            var request = new BlockMutationRequest(
                requestingClientId: 7,
                position,
                BlockRegistry.LumenQuartzCluster,
                expectedCurrentBlock: BlockRegistry.LooseLoam);

            BlockMutationResult result = authority.TryCommit(request, out SetBlockCommand command);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.None));
            Assert.That(result.Change.Position, Is.EqualTo(position));
            Assert.That(result.Change.PreviousBlock, Is.EqualTo(BlockRegistry.LooseLoam));
            Assert.That(result.Change.NewBlock, Is.EqualTo(BlockRegistry.LumenQuartzCluster));
            Assert.That(result.Chunk, Is.EqualTo(new ChunkCoordinate(1, 0, 0)));
            Assert.That(command, Is.Not.Null);
            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
        }

        [Test]
        public void ChunkDeltaLogReplayReconstructsAcceptedMutations()
        {
            var sourceWorld = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 2, seed: 21);
            var replayWorld = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 2, seed: 21);
            var firstPosition = new BlockPosition(1, 1, 1);
            var secondPosition = new BlockPosition(3, 1, 1);
            var log = new ChunkDeltaLog();

            ChunkDelta firstDelta = log.Record(new SetBlockCommand(firstPosition, BlockRegistry.LumenQuartzCluster).Execute(sourceWorld), sourceWorld.ChunkSize);
            ChunkDelta secondDelta = log.Record(new SetBlockCommand(secondPosition, BlockRegistry.Graystone).Execute(sourceWorld), sourceWorld.ChunkSize);

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
            world.SetBlock(position, BlockRegistry.LooseLoam, trackChange: false);
            BlockMutationAuthority authority = BlockMutationAuthority.CreateClientProxy(world, registry, localClientId: 7);

            BlockMutationResult result = authority.TryCommit(
                new BlockMutationRequest(7, position, BlockRegistry.LumenQuartzCluster),
                out SetBlockCommand command);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.ClientCannotCommitAuthoritativeState));
            Assert.That(command, Is.Null);
            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.LooseLoam));

            BlockMutationResult validationResult = authority.ValidateHostMutation(
                new BlockMutationRequest(7, position, BlockRegistry.LumenQuartzCluster));
            Assert.That(validationResult.Accepted, Is.False);
            Assert.That(validationResult.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.HostOnlyAuthorityOperation));
        }

        [Test]
        public void HostAuthorityRejectsInvalidMutationRequestsPredictably()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 2, seed: 19);
            var position = new BlockPosition(1, 1, 1);
            world.SetBlock(position, BlockRegistry.LooseLoam, trackChange: false);
            BlockMutationAuthority authority = BlockMutationAuthority.CreateHost(world, registry);

            Assert.That(
                authority.TryCommit(new BlockMutationRequest(7, new BlockPosition(-1, 1, 1), BlockRegistry.Graystone)).RejectionReason,
                Is.EqualTo(BlockMutationRejectionReason.PositionOutOfBounds));
            Assert.That(
                authority.TryCommit(new BlockMutationRequest(7, position, new BlockId(999))).RejectionReason,
                Is.EqualTo(BlockMutationRejectionReason.UnknownBlock));
            Assert.That(
                authority.TryCommit(new BlockMutationRequest(7, position, BlockRegistry.Graystone, expectedCurrentBlock: BlockRegistry.LumenQuartzCluster)).RejectionReason,
                Is.EqualTo(BlockMutationRejectionReason.ExpectedBlockMismatch));
            Assert.That(
                authority.TryCommit(new BlockMutationRequest(7, position, BlockRegistry.LooseLoam)).RejectionReason,
                Is.EqualTo(BlockMutationRejectionReason.NoChange));
            Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.LooseLoam));
        }

        [Test]
        public void FlatCreativePresetCreatesBoundedSpawnSafeWorld()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(width: 16, height: 8, depth: 16, chunkSize: 16, seed: 123, groundHeight: 2);
            var preset = new FlatBuilderPreset(registry, settings);

            VoxelWorld world = preset.Generate();

            Assert.That(world.Bounds, Is.EqualTo(new WorldBounds(16, 8, 16)));
            Assert.That(world.Seed, Is.EqualTo(123));
            Assert.That(world.GetBlock(new BlockPosition(0, 0, 0)), Is.EqualTo(BlockRegistry.LooseLoam));
            Assert.That(world.GetBlock(new BlockPosition(0, 1, 0)), Is.EqualTo(BlockRegistry.MeadowTurf));
            Assert.That(world.GetBlock(new BlockPosition(0, 2, 0)), Is.EqualTo(BlockRegistry.Air));
            Assert.That(world.GetChangedBlocks(), Is.Empty);
            Assert.That(world.Bounds.Contains(settings.SpawnPosition), Is.True);
            Assert.That(world.GetBlock(settings.SpawnPosition), Is.EqualTo(BlockRegistry.Air));
        }
    }
}

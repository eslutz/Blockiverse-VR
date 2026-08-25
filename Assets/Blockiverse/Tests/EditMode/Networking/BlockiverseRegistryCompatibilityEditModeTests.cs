using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.Networking.EditMode
{
    /// <summary>
    /// The handshake hashes must reflect what the wire actually carries. Block deltas and
    /// snapshots send raw integer BlockIds, so two builds agreeing on every canonical id but
    /// assigning a different integer to one would decode every delta wrongly — silently.
    /// </summary>
    public sealed class BlockiverseRegistryCompatibilityEditModeTests
    {
        [Test]
        public void BlockHashCoversTheIntegerIdsTheWireActuallySends()
        {
            // Two registries with an identical canonical-id set, differing only in which
            // integer each id is assigned. Deltas and snapshots carry those integers, so these
            // two builds would decode each other's edits as the wrong blocks entirely.
            BlockRegistry left = BuildRegistry(stoneId: 10, dirtId: 11);
            BlockRegistry right = BuildRegistry(stoneId: 11, dirtId: 10);

            Assert.That(
                WorldSaveService.ComputeBlockRegistryHash(left),
                Is.EqualTo(WorldSaveService.ComputeBlockRegistryHash(right)),
                "The save-side hash sees only sorted canonical ids, so it cannot tell these apart — which is the gap.");

            Assert.That(
                BlockiverseRegistryCompatibility.ComputeBlockHash(left),
                Is.Not.EqualTo(BlockiverseRegistryCompatibility.ComputeBlockHash(right)),
                "The wire-side hash must reject a peer whose integer ids disagree.");
        }

        [Test]
        public void BlockHashCoversDefinitionFieldsPeersSimulateFrom()
        {
            // Same ids and integers, one property changed: a peer would render and simulate the
            // block differently while agreeing on every id.
            BlockRegistry solid = BuildRegistry(stoneId: 10, dirtId: 11);
            BlockRegistry notSolid = BuildRegistry(stoneId: 10, dirtId: 11, stoneSolid: false);

            Assert.That(
                BlockiverseRegistryCompatibility.ComputeBlockHash(solid),
                Is.Not.EqualTo(BlockiverseRegistryCompatibility.ComputeBlockHash(notSolid)));
        }

        [Test]
        public void BlockHashCoversRenderShape()
        {
            // Render shape selects the geometry each peer builds from the same delta: one peer
            // emitting a cube where the other emits a cross quad is the same class of divergence
            // as disagreeing on Category.
            BlockRegistry cube = BuildRegistry(stoneId: 10, dirtId: 11);
            BlockRegistry cross = BuildRegistry(stoneId: 10, dirtId: 11, stoneShape: BlockRenderShape.Cross);

            Assert.That(
                BlockiverseRegistryCompatibility.ComputeBlockHash(cube),
                Is.Not.EqualTo(BlockiverseRegistryCompatibility.ComputeBlockHash(cross)),
                "A peer building different geometry for the same block must be refused.");
        }

        [Test]
        public void BlockHashCoversPassability()
        {
            // Passability is a physics divergence: one peer walks through the block, the other
            // collides with it, from an identical world delta.
            BlockRegistry solidBlock = BuildRegistry(stoneId: 10, dirtId: 11);
            BlockRegistry passable = BuildRegistry(stoneId: 10, dirtId: 11, stonePassable: true);

            Assert.That(
                BlockiverseRegistryCompatibility.ComputeBlockHash(solidBlock),
                Is.Not.EqualTo(BlockiverseRegistryCompatibility.ComputeBlockHash(passable)),
                "A peer that can walk through a block the other cannot must be refused.");
        }

        [Test]
        public void BlockHashIgnoresRegistrationOrder()
        {
            // Order is not part of the contract — the same ids assigned the same integers
            // describe the same world however they were registered.
            BlockRegistry forward = BuildRegistry(stoneId: 10, dirtId: 11);
            BlockRegistry reversed = BuildRegistry(stoneId: 10, dirtId: 11, reverseRegistrationOrder: true);

            Assert.That(
                BlockiverseRegistryCompatibility.ComputeBlockHash(forward),
                Is.EqualTo(BlockiverseRegistryCompatibility.ComputeBlockHash(reversed)));
        }

        [Test]
        public void BlockHashCoversCategoryBecauseItSelectsGeometryAndHarvestRules()
        {
            // Category is the non-obvious one: it is not a label. ChunkMeshBuilder branches on
            // BlockCategory.Fluid to build fluid geometry, and mining cost and harvest
            // eligibility read it too. Two peers disagreeing would build different meshes from
            // the same authoritative delta.
            BlockRegistry terrain = BuildRegistry(stoneId: 10, dirtId: 11);
            BlockRegistry fluid = BuildRegistry(stoneId: 10, dirtId: 11, stoneCategory: BlockCategory.Fluid);

            Assert.That(
                BlockiverseRegistryCompatibility.ComputeBlockHash(terrain),
                Is.Not.EqualTo(BlockiverseRegistryCompatibility.ComputeBlockHash(fluid)));
        }

        [Test]
        public void BlockHashIgnoresDisplayNameSoACosmeticFixCannotRefuseAJoin()
        {
            BlockRegistry original = BuildRegistry(stoneId: 10, dirtId: 11);
            BlockRegistry renamed = BuildRegistry(stoneId: 10, dirtId: 11, stoneName: "Test Stone (fixed typo)");

            Assert.That(
                BlockiverseRegistryCompatibility.ComputeBlockHash(renamed),
                Is.EqualTo(BlockiverseRegistryCompatibility.ComputeBlockHash(original)),
                "A display-name change affects nothing on the wire and must not refuse a join.");
        }

        static BlockRegistry BuildRegistry(
            int stoneId,
            int dirtId,
            bool stoneSolid = true,
            bool reverseRegistrationOrder = false,
            BlockCategory stoneCategory = BlockCategory.Terrain,
            string stoneName = "Test Stone",
            BlockRenderShape stoneShape = BlockRenderShape.Cube,
            bool stonePassable = false)
        {
            var registry = new BlockRegistry();
            var stone = new BlockDefinition(
                new BlockId(stoneId), "test_stone", stoneName, stoneCategory, stoneSolid, isRenderable: true,
                renderShape: stoneShape, isPassable: stonePassable);
            var dirt = new BlockDefinition(
                new BlockId(dirtId), "test_dirt", "Test Dirt", BlockCategory.Terrain, isSolid: true, isRenderable: true);

            if (reverseRegistrationOrder)
            {
                registry.Register(dirt);
                registry.Register(stone);
            }
            else
            {
                registry.Register(stone);
                registry.Register(dirt);
            }

            return registry;
        }

        [Test]
        public void BlockHashIsStableAndDistinctFromTheSaveHash()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            string first = BlockiverseRegistryCompatibility.ComputeBlockHash(registry);
            string second = BlockiverseRegistryCompatibility.ComputeBlockHash(BlockRegistry.CreateDefault());

            Assert.That(first, Is.EqualTo(second), "The hash must be deterministic across instances.");
            Assert.That(first, Is.Not.Empty);

            // Distinct inputs, so a save-hash match must never be read as wire compatibility.
            Assert.That(first, Is.Not.EqualTo(WorldSaveService.ComputeBlockRegistryHash(registry)));
        }

        [Test]
        public void ItemAndRecipeHashesAreDeterministicAndDistinct()
        {
            string item = BlockiverseRegistryCompatibility.ComputeItemHash(ItemRegistry.Default);
            string recipe = BlockiverseRegistryCompatibility.ComputeRecipeHash(CraftingRecipeBook.Default);
            string block = BlockiverseRegistryCompatibility.ComputeBlockHash(BlockRegistry.Default);

            Assert.That(item, Is.EqualTo(BlockiverseRegistryCompatibility.ComputeItemHash(ItemRegistry.Default)));
            Assert.That(recipe, Is.EqualTo(BlockiverseRegistryCompatibility.ComputeRecipeHash(CraftingRecipeBook.Default)));

            Assert.That(item, Is.Not.EqualTo(block));
            Assert.That(recipe, Is.Not.EqualTo(block));
            Assert.That(recipe, Is.Not.EqualTo(item));
        }

        [Test]
        public void SessionPublishesTheWireHashesRatherThanTheSaveHashes()
        {
            // The handshake must not regress to the save-side hashes: they answer a different
            // question and would admit a peer whose integer ids disagree.
            Assert.That(
                BlockiverseNetworkSession.LocalBlockRegistryHash,
                Is.EqualTo(BlockiverseRegistryCompatibility.ComputeBlockHash(BlockRegistry.Default)));
            Assert.That(
                BlockiverseNetworkSession.LocalBlockRegistryHash,
                Is.Not.EqualTo(WorldSaveService.ComputeBlockRegistryHash(BlockRegistry.Default)));
            Assert.That(
                BlockiverseNetworkSession.LocalItemRegistryHash,
                Is.EqualTo(BlockiverseRegistryCompatibility.ComputeItemHash(ItemRegistry.Default)));
            Assert.That(
                BlockiverseNetworkSession.LocalRecipeRegistryHash,
                Is.EqualTo(BlockiverseRegistryCompatibility.ComputeRecipeHash(CraftingRecipeBook.Default)));
        }

        [Test]
        public void SnapshotBatchBurstStaysWellInsideTheTransportSendQueue()
        {
            // Each batch fragments into several packets, so the per-frame burst must sit far
            // below the queue depth the bootstrapper configures. A dropped batch is invisible:
            // the client stalls, resyncs, and receives the same burst again.
            const int worstCasePacketsPerBatch = 4;
            int packetsPerFrame = MultiplayerChunkAuthoritySync.SnapshotBatchesPerFrame * worstCasePacketsPerBatch;

            Assert.That(
                packetsPerFrame,
                Is.LessThan(Blockiverse.Editor.BlockiverseProjectBootstrapper.TransportMaxPacketQueueSize / 2),
                "A frame's snapshot burst should not approach the transport send-queue depth.");
            Assert.That(MultiplayerChunkAuthoritySync.SnapshotBatchesPerFrame, Is.GreaterThan(0));
        }
    }
}

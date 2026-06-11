using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class FluidFlowServiceEditModeTests
    {
        // ── FluidBlocks classification ────────────────────────────────────────

        [Test]
        public void FluidBlocksClassifyFamiliesAndCanonicalBehavior()
        {
            // Source/flow ids map onto their family and back.
            foreach (FluidFamily family in new[] { FluidFamily.Freshwater, FluidFamily.Brine, FluidFamily.Emberflow })
            {
                Assert.That(FluidBlocks.IsSource(FluidBlocks.SourceOf(family)), Is.True);
                Assert.That(FluidBlocks.IsFlow(FluidBlocks.FlowOf(family)), Is.True);
                Assert.That(FluidBlocks.TryGetFamily(FluidBlocks.SourceOf(family), out FluidFamily fromSource), Is.True);
                Assert.That(fromSource, Is.EqualTo(family));
                Assert.That(FluidBlocks.TryGetFamily(FluidBlocks.FlowOf(family), out FluidFamily fromFlow), Is.True);
                Assert.That(fromFlow, Is.EqualTo(family));
                Assert.That(FluidBlocks.IsFluid(FluidBlocks.SourceOf(family)), Is.True);
                Assert.That(FluidBlocks.IsFluid(FluidBlocks.FlowOf(family)), Is.True);
            }

            Assert.That(FluidBlocks.IsFluid(BlockRegistry.Graystone), Is.False);
            Assert.That(FluidBlocks.TryGetFamily(BlockRegistry.Air, out _), Is.False);

            // Drinkable/irrigating water covers the source and its flowing cells, nothing else.
            Assert.That(FluidBlocks.IsFreshwater(BlockRegistry.Freshwater), Is.True);
            Assert.That(FluidBlocks.IsFreshwater(BlockRegistry.FreshwaterFlow), Is.True);
            Assert.That(FluidBlocks.IsFreshwater(BlockRegistry.Brine), Is.False);
            Assert.That(FluidBlocks.IsFreshwater(BlockRegistry.Emberflow), Is.False);

            // §5.4 fluid behavior table.
            Assert.That(FluidBlocks.FlowDistance(FluidFamily.Freshwater), Is.EqualTo(8));
            Assert.That(FluidBlocks.FlowDistance(FluidFamily.Brine), Is.EqualTo(6));
            Assert.That(FluidBlocks.FlowDistance(FluidFamily.Emberflow), Is.EqualTo(4));
            Assert.That(FluidBlocks.TickCadence(FluidFamily.Freshwater), Is.EqualTo(5));
            Assert.That(FluidBlocks.TickCadence(FluidFamily.Brine), Is.EqualTo(6));
            Assert.That(FluidBlocks.TickCadence(FluidFamily.Emberflow), Is.EqualTo(12));
        }

        // ── Flow simulation ───────────────────────────────────────────────────

        [Test]
        public void FreshwaterSourceSpreadsOnItsCadenceToItsFlowDistance()
        {
            VoxelWorld world = CreateFlooredWorld();
            FluidFlowService service = CreateWiredService(world);
            var source = new BlockPosition(16, 1, 16);
            world.SetBlock(source, BlockRegistry.Freshwater);

            service.Tick(world, 4);
            Assert.That(world.GetBlock(new BlockPosition(17, 1, 16)), Is.EqualTo(BlockRegistry.Air),
                "No spread before the §5.4 freshwater cadence (every 5 ticks).");

            service.Tick(world, 5);
            Assert.That(world.GetBlock(new BlockPosition(17, 1, 16)), Is.EqualTo(BlockRegistry.FreshwaterFlow));
            Assert.That(world.GetBlock(new BlockPosition(15, 1, 16)), Is.EqualTo(BlockRegistry.FreshwaterFlow));
            Assert.That(world.GetBlock(new BlockPosition(16, 1, 17)), Is.EqualTo(BlockRegistry.FreshwaterFlow));
            Assert.That(world.GetBlock(new BlockPosition(16, 1, 15)), Is.EqualTo(BlockRegistry.FreshwaterFlow));

            // Let the pool spread out fully, then verify the §5.4 horizontal span: the source
            // plus its flowing cells cover 8 blocks; the 9th cell stays dry.
            service.Tick(world, 65);
            Assert.That(world.GetBlock(new BlockPosition(16 + 7, 1, 16)), Is.EqualTo(BlockRegistry.FreshwaterFlow));
            Assert.That(world.GetBlock(new BlockPosition(16 + 8, 1, 16)), Is.EqualTo(BlockRegistry.Air));
            Assert.That(world.GetBlock(new BlockPosition(16 - 7, 1, 16)), Is.EqualTo(BlockRegistry.FreshwaterFlow));
            Assert.That(world.GetBlock(new BlockPosition(16 - 8, 1, 16)), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void MidairSourcePoursASingleColumnAndPoolsFromTheLanding()
        {
            VoxelWorld world = CreateFlooredWorld();
            FluidFlowService service = CreateWiredService(world);
            var source = new BlockPosition(16, 5, 16);
            world.SetBlock(source, BlockRegistry.Freshwater);

            service.Tick(world, 5);
            Assert.That(world.GetBlock(new BlockPosition(16, 4, 16)), Is.EqualTo(BlockRegistry.FreshwaterFlow),
                "Fall-first: air below means the fluid pours down before spreading sideways.");
            Assert.That(world.GetBlock(new BlockPosition(17, 5, 16)), Is.EqualTo(BlockRegistry.Air));

            // Land and fully spread the floor pool.
            service.Tick(world, 80);

            // The source keeps feeding its column instead of fanning out at altitude.
            Assert.That(world.GetBlock(new BlockPosition(17, 5, 16)), Is.EqualTo(BlockRegistry.Air),
                "A fed column never spreads sideways in midair.");

            // The landing restarts the full horizontal budget: the floor pool spans 7 cells out
            // from the landing column even though the source sits far above the floor.
            Assert.That(world.GetBlock(new BlockPosition(16 + 7, 1, 16)), Is.EqualTo(BlockRegistry.FreshwaterFlow));
            Assert.That(world.GetBlock(new BlockPosition(16 + 8, 1, 16)), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void FlowRetractsFullyWhenItsSourceIsRemoved()
        {
            VoxelWorld world = CreateFlooredWorld();
            FluidFlowService service = CreateWiredService(world);
            var source = new BlockPosition(16, 1, 16);
            world.SetBlock(source, BlockRegistry.Freshwater);
            service.Tick(world, 70);

            Assert.That(CountBlocks(world, BlockRegistry.FreshwaterFlow), Is.GreaterThan(0));

            // Mining the source drops the support gradient; the pool retracts outward-in.
            world.SetBlock(source, BlockRegistry.Air);
            service.Tick(world, 170);

            Assert.That(CountBlocks(world, BlockRegistry.FreshwaterFlow), Is.Zero,
                "Flowing cells retract once their source support is gone.");
            Assert.That(service.ActiveCellCount, Is.Zero,
                "A drained pool settles the simulation back to idle.");
        }

        [Test]
        public void FreshwaterQuenchesAdjacentEmberflowSourcesIntoBlackBasalt()
        {
            VoxelWorld world = CreateFlooredWorld();
            FluidFlowService service = CreateWiredService(world);
            world.SetBlock(new BlockPosition(10, 1, 16), BlockRegistry.Emberflow);
            world.SetBlock(new BlockPosition(12, 1, 16), BlockRegistry.Freshwater);

            // Step 1 (tick 5): water spreads to x=11. Step 2 (tick 10): the new flow cell
            // touches the emberflow source and quenches it (§5.4).
            service.Tick(world, 10);

            Assert.That(world.GetBlock(new BlockPosition(10, 1, 16)), Is.EqualTo(BlockRegistry.BlackBasalt));
        }

        [Test]
        public void EmberflowBurnsAdjacentFlammableBlocksAway()
        {
            VoxelWorld world = CreateFlooredWorld();
            FluidFlowService service = CreateWiredService(world);
            world.SetBlock(new BlockPosition(15, 1, 16), BlockRegistry.BranchwoodLog);
            world.SetBlock(new BlockPosition(17, 1, 16), BlockRegistry.Leafmoss);
            world.SetBlock(new BlockPosition(16, 1, 15), BlockRegistry.WorkPlank);
            world.SetBlock(new BlockPosition(16, 1, 17), BlockRegistry.Thornbrush);
            world.SetBlock(new BlockPosition(16, 1, 16), BlockRegistry.Emberflow);

            // 80 emberflow steps; every flammable neighbour rolls the deterministic 25%
            // per-step ignition chance, so this seeded run burns all four away.
            service.Tick(world, 12 * 80);

            Assert.That(world.GetBlock(new BlockPosition(15, 1, 16)), Is.Not.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(17, 1, 16)), Is.Not.EqualTo(BlockRegistry.Leafmoss));
            Assert.That(world.GetBlock(new BlockPosition(16, 1, 15)), Is.Not.EqualTo(BlockRegistry.WorkPlank));
            Assert.That(world.GetBlock(new BlockPosition(16, 1, 17)), Is.Not.EqualTo(BlockRegistry.Thornbrush));
        }

        // ── Lockstep guarantees ───────────────────────────────────────────────

        [Test]
        public void FlowSimulationIsIndependentOfTickBatching()
        {
            VoxelWorld worldA = CreateInteractionScenario(out FluidFlowService serviceA);
            VoxelWorld worldB = CreateInteractionScenario(out FluidFlowService serviceB);

            // Peer A advances one tick at a time; peer B receives the same ticks in ragged
            // batches (frame hitches). Both must produce the identical world.
            for (long tick = 1; tick <= 240; tick++)
                serviceA.Tick(worldA, tick);

            foreach (long tick in new long[] { 4, 11, 12, 13, 24, 39, 60, 61, 120, 239, 240 })
                serviceB.Tick(worldB, tick);

            AssertWorldsMatch(worldA, worldB);
        }

        [Test]
        public void ConfigureRebuiltServiceMatchesContinuouslyRunService()
        {
            VoxelWorld worldA = CreateFlooredWorld();
            FluidFlowService serviceA = CreateWiredService(worldA);
            VoxelWorld worldB = CreateFlooredWorld();
            var initialB = new FluidFlowService();
            initialB.Configure(worldB, seed: 1, worldTick: 0);
            FluidFlowService currentB = initialB;
            // Late-join style rewiring: the lambda always routes to the current service.
            worldB.BlockChanged += change => currentB.OnBlockChanged(worldB, change);

            var source = new BlockPosition(16, 4, 16);
            worldA.SetBlock(source, BlockRegistry.Freshwater);
            worldB.SetBlock(source, BlockRegistry.Freshwater);
            serviceA.Tick(worldA, 60);
            initialB.Tick(worldB, 60);

            // Peer B reconstructs its simulation from the synced world alone (late joiner /
            // loaded save): a fresh Configure must recover identical flow bookkeeping.
            var lateJoiner = new FluidFlowService();
            lateJoiner.Configure(worldB, seed: 1, worldTick: 60);
            currentB = lateJoiner;

            worldA.SetBlock(source, BlockRegistry.Air);
            worldB.SetBlock(source, BlockRegistry.Air);
            serviceA.Tick(worldA, 200);
            lateJoiner.Tick(worldB, 200);

            AssertWorldsMatch(worldA, worldB);
            Assert.That(lateJoiner.ActiveCellCount, Is.EqualTo(serviceA.ActiveCellCount));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // 32×8×32 world with a solid graystone floor at y=0.
        static VoxelWorld CreateFlooredWorld()
        {
            var world = new VoxelWorld(new WorldBounds(32, 8, 32), chunkSize: 8, seed: 1);
            for (int x = 0; x < world.Bounds.Width; x++)
            {
                for (int z = 0; z < world.Bounds.Depth; z++)
                    world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.Graystone);
            }

            return world;
        }

        // Configures a service on the world and wires it to block changes the way
        // CreativeWorldManager does in production.
        static FluidFlowService CreateWiredService(VoxelWorld world, int seed = 1)
        {
            var service = new FluidFlowService();
            service.Configure(world, seed, worldTick: 0);
            world.BlockChanged += change => service.OnBlockChanged(world, change);
            return service;
        }

        // Freshwater pool meeting an emberflow cascade with flammables in reach — exercises
        // spreading, falling, ignition, and spread-blocking between families.
        static VoxelWorld CreateInteractionScenario(out FluidFlowService service)
        {
            VoxelWorld world = CreateFlooredWorld();
            service = CreateWiredService(world, seed: 7);

            world.SetBlock(new BlockPosition(22, 1, 16), BlockRegistry.Graystone);
            world.SetBlock(new BlockPosition(21, 1, 16), BlockRegistry.BranchwoodLog);
            world.SetBlock(new BlockPosition(22, 1, 17), BlockRegistry.Leafmoss);
            world.SetBlock(new BlockPosition(22, 2, 16), BlockRegistry.Emberflow);
            world.SetBlock(new BlockPosition(12, 1, 16), BlockRegistry.Freshwater);
            return world;
        }

        static int CountBlocks(VoxelWorld world, BlockId blockId)
        {
            int count = 0;
            for (int y = 0; y < world.Bounds.Height; y++)
            {
                for (int x = 0; x < world.Bounds.Width; x++)
                {
                    for (int z = 0; z < world.Bounds.Depth; z++)
                    {
                        if (world.GetBlock(new BlockPosition(x, y, z)) == blockId)
                            count++;
                    }
                }
            }

            return count;
        }

        static void AssertWorldsMatch(VoxelWorld expected, VoxelWorld actual)
        {
            for (int y = 0; y < expected.Bounds.Height; y++)
            {
                for (int x = 0; x < expected.Bounds.Width; x++)
                {
                    for (int z = 0; z < expected.Bounds.Depth; z++)
                    {
                        var position = new BlockPosition(x, y, z);
                        Assert.That(actual.GetBlock(position), Is.EqualTo(expected.GetBlock(position)),
                            $"Worlds diverged at {position}.");
                    }
                }
            }
        }
    }
}

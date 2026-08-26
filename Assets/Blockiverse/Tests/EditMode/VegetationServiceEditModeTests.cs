using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class VegetationServiceEditModeTests
    {
        static readonly BlockPosition BasePos = new(8, 8, 8);

        // ── §9 species mechanics ─────────────────────────────────────────────
        // Three species differ by behaviour rather than silhouette, so these assert the behaviour.

        [Test]
        public void LeanbranchLeansItsCanopyTowardWater()
        {
            // Water to the +X side only, so the lean direction is unambiguous.
            for (int dz = -2; dz <= 2; dz++)
                world.SetBlock(new BlockPosition(BasePos.X + 3, BasePos.Y - 1, BasePos.Z + dz), BlockRegistry.Freshwater, trackChange: false);

            bool found = VegetationService.TryGetLeanTowardFluid(world, BasePos, searchRadius: 4, out int leanX, out int leanZ);

            Assert.That(found, Is.True, "Water within the search radius must be found.");
            Assert.That(leanX, Is.EqualTo(1), "The lean must point toward the water, not away from it.");
            Assert.That(leanZ, Is.EqualTo(0), "Water is directly along +X, so there is no Z component.");
        }

        [Test]
        public void LeanbranchStandsStraightWithNoWaterNearby()
        {
            bool found = VegetationService.TryGetLeanTowardFluid(world, BasePos, searchRadius: 4, out _, out _);

            Assert.That(found, Is.False,
                "With no fluid in range the tree must stand straight rather than lean arbitrarily.");
        }

        [Test]
        public void FanbranchOasisGateSeesBrineAndIgnoresFreshwater()
        {
            Assert.That(VegetationService.IsNearBrine(world, BasePos, searchRadius: 5), Is.False,
                "A dry dune must not qualify.");

            // Freshwater is not an oasis: §9.5 gates Fanbranch on BRINE specifically.
            world.SetBlock(new BlockPosition(BasePos.X + 2, BasePos.Y - 1, BasePos.Z), BlockRegistry.Freshwater, trackChange: false);
            Assert.That(VegetationService.IsNearBrine(world, BasePos, searchRadius: 5), Is.False,
                "Freshwater must not qualify as an oasis, or Fanbranch stops being rare.");

            world.SetBlock(new BlockPosition(BasePos.X + 2, BasePos.Y - 1, BasePos.Z), BlockRegistry.Brine, trackChange: false);
            Assert.That(VegetationService.IsNearBrine(world, BasePos, searchRadius: 5), Is.True);
        }

        [Test]
        public void PrevailingWindIsSeedDerivedAndAxisAligned()
        {
            VegetationService.GetPrevailingWind(1234, out int windX, out int windZ);
            VegetationService.GetPrevailingWind(1234, out int againX, out int againZ);

            // Every peer regenerates terrain from the seed, so a wind that varied per call would
            // desync the canopy positions between host and client.
            Assert.That((windX, windZ), Is.EqualTo((againX, againZ)), "Wind must be stable for a seed.");
            Assert.That(System.Math.Abs(windX) + System.Math.Abs(windZ), Is.EqualTo(1),
                "Wind must be exactly one axis step; the canopy offset is a whole cell.");
        }

        [Test]
        public void FanbranchPlacesAFlatCrownRatherThanARoundCanopy()
        {
            vegetation.PlaceFanbranchTree(world, BasePos);

            int crownY = BasePos.Y + 5;
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X, crownY, BasePos.Z)),
                Is.EqualTo(BlockRegistry.Leafmoss), "The crown centre must be leaves.");
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X + 2, crownY - 1, BasePos.Z)),
                Is.EqualTo(BlockRegistry.Leafmoss), "Fronds must droop at their tips.");
            // The defining difference from a round canopy: the diagonals stay empty.
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X + 1, crownY, BasePos.Z + 1)),
                Is.Not.EqualTo(BlockRegistry.Leafmoss),
                "A fan is four arms, not a disc — diagonals must stay open or it reads as a small round tree.");
        }

        // ── §8 biome profiles ────────────────────────────────────────────────

        [Test]
        public void EveryBiomeProfileHasGroundCoverAndTundraIsNotSilentlyEmpty()
        {
            foreach (BiomeVegetationProfile profile in BiomeVegetationProfiles.All)
            {
                Assert.That(profile.GroundCoverChancePermille, Is.GreaterThan(0),
                    $"{profile.Biome} has no groundcover chance; §8 gives every biome one.");
                Assert.That(profile.GroundCover, Is.Not.Empty, $"{profile.Biome} has no groundcover species.");
                Assert.That(profile.WildPlantDensityPermille, Is.GreaterThan(0),
                    $"{profile.Biome} scatters no wild plants.");
            }

            // Tundra specifically: it previously fell through the wild-plant switch's default and
            // shipped zero plants. "No case" and "density 0" are indistinguishable in a switch,
            // which is exactly why nothing caught it.
            BiomeVegetationProfile tundra = BiomeVegetationProfiles.For(TerrainBiome.Tundra);
            Assert.That(tundra.WildPlantDensityPermille, Is.GreaterThan(0),
                "Tundra must scatter wild plants; it silently shipped none for the whole of its existence.");
            Assert.That(tundra.Biome, Is.EqualTo(TerrainBiome.Tundra), "For() must not fall back to Meadow.");
        }

        [Test]
        public void GroundCoverPickRespectsTheSurfaceAndFallsBackWithinTheBiome()
        {
            BiomeVegetationProfile tundra = BiomeVegetationProfiles.For(TerrainBiome.Tundra);

            // Snow lichen accepts snowcap turf; frost fern also accepts rootsoil. On a surface only
            // one of them accepts, the pick must still return that one rather than nothing — a
            // biome whose dominant species rejects the block must not leave the column bare.
            bool picked = BiomeVegetationProfiles.TryPickPlant(
                tundra.GroundCover, BlockRegistry.Rootsoil, roll: 0u, out BlockId plant);

            Assert.That(picked, Is.True, "A surface accepted by any species in the table must pick.");
            Assert.That(plant, Is.EqualTo(BlockRegistry.FrostFern),
                "Only frost fern accepts rootsoil in Tundra, so it must be the one chosen.");

            bool none = BiomeVegetationProfiles.TryPickPlant(
                tundra.GroundCover, BlockRegistry.PaleSand, roll: 0u, out _);
            Assert.That(none, Is.False, "No Tundra groundcover grows on pale sand, so nothing is placed.");
        }

        VoxelWorld world;
        VegetationService vegetation;

        [SetUp]
        public void SetUp()
        {
            world = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 1);
            vegetation = new VegetationService();
        }

        [Test]
        public void PlaceCrownbranchTreePlacesLogsAndLeaves()
        {
            vegetation.PlaceCrownbranchTree(world, BasePos);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X, BasePos.Y + 3, BasePos.Z)), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(CountBlocks(BlockRegistry.Leafmoss), Is.GreaterThan(0));
        }

        [Test]
        public void PlaceCrownbranchTreeTracksRuntimeChangesOnlyWhenRequested()
        {
            vegetation.PlaceCrownbranchTree(world, BasePos);
            Assert.That(world.GetChangedBlocks(), Is.Empty);

            var runtimeWorld = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 1);
            vegetation.PlaceCrownbranchTree(runtimeWorld, BasePos, trackChange: true);

            Assert.That(runtimeWorld.GetChangedBlocks(), Is.Not.Empty);
            Assert.That(HasChangedBlock(runtimeWorld, BasePos, BlockRegistry.BranchwoodLog), Is.True);
        }

        [Test]
        public void PlaceNeedlebranchTreeProducesNarrowTopWideBottom()
        {
            vegetation.PlaceNeedlebranchTree(world, BasePos);

            // Trunk exists
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(CountBlocks(BlockRegistry.Leafmoss), Is.GreaterThan(0));
        }

        [Test]
        public void PlaceScrubbranchTreeKeepsItsCanopyAbovePlayerHeight()
        {
            vegetation.PlaceScrubbranchTree(world, BasePos);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X, BasePos.Y + 3, BasePos.Z)), Is.EqualTo(BlockRegistry.BranchwoodLog),
                "A scrub tree needs four trunk blocks so its lowest canopy is not at head height.");
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X, BasePos.Y + 4, BasePos.Z)), Is.EqualTo(BlockRegistry.Leafmoss),
                "The first canopy layer must start above a standing player's head.");
        }

        [Test]
        public void PlaceWindbranchTreeHasTallerTrunkThanStandard()
        {
            var world2 = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 1);
            vegetation.PlaceCrownbranchTree(world,  BasePos);
            vegetation.PlaceWindbranchTree(world2, BasePos);

            int standardTrunkTop = FindTrunkTop(world,  BasePos);
            int tallTrunkTop     = FindTrunkTop(world2, BasePos);

            Assert.That(tallTrunkTop, Is.GreaterThan(standardTrunkTop));
        }

        [Test]
        public void TickSaplingAdvancesThroughStagesAndPlacesTree()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);

            // Stage 0 → 1
            vegetation.TickSapling(world, 1200);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S1));

            // Stage 1 → 2
            vegetation.TickSapling(world, 1200);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S2));

            // Stage 2 → full tree (sapling removed, logs placed)
            vegetation.TickSapling(world, 1200);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void TickSaplingTracksGrownTreeBlocksForPersistence()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);
            world.ClearChangedBlocks();

            vegetation.TickSapling(world, 3600);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(HasChangedBlock(world, BasePos, BlockRegistry.BranchwoodLog), Is.True);
            Assert.That(HasChangedBlock(world, new BlockPosition(BasePos.X, BasePos.Y + 4, BasePos.Z), BlockRegistry.Leafmoss), Is.True);
        }

        [Test]
        public void TickSaplingDoesNotAdvanceBeforeInterval()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);

            vegetation.TickSapling(world, 1199);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling));
        }

        [Test]
        public void SaplingProgressExportRestoreRoundTripsAccumulatedTicks()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);
            vegetation.TickSapling(world, 700); // below the 1200-tick interval: progress only

            var restored = new VegetationService();
            restored.RestoreSaplingProgress(vegetation.ExportSaplingProgress());

            // 700 saved + 500 new = 1200 → the restored service advances exactly on schedule.
            restored.TickSapling(world, 499);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling));
            restored.TickSapling(world, 1);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S1));
        }

        [Test]
        public void WildRegrowthExportRestoreRoundTripsMarkers()
        {
            var harvested = new BlockPosition(8, 9, 8);
            world.SetBlock(new BlockPosition(8, 8, 8), BlockRegistry.MeadowTurf);
            vegetation.MarkWildHarvest(BlockRegistry.Thornbrush, harvested, currentTick: 1000);
            Assert.That(vegetation.WildRegrowthQueueCount, Is.EqualTo(1));

            var restored = new VegetationService();
            restored.RestoreWildRegrowth(vegetation.ExportWildRegrowth());
            Assert.That(restored.WildRegrowthQueueCount, Is.EqualTo(1));

            // The marker keeps its absolute deadline: before it nothing happens, at it the
            // harvested plant regrows.
            restored.TickWildRegrowth(world, currentTick: 1001);
            Assert.That(world.GetBlock(harvested), Is.EqualTo(BlockRegistry.Air));
            restored.TickWildRegrowth(world, currentTick: 1000 + 100000);
            Assert.That(world.GetBlock(harvested), Is.EqualTo(BlockRegistry.Thornbrush));
        }

        [Test]
        public void TickLeafDecayWithZeroTicksIsNoop()
        {
            var isolatedLeaf = new BlockPosition(8, 16, 8);
            world.SetBlock(isolatedLeaf, BlockRegistry.Leafmoss);

            vegetation.TickLeafDecay(world, 0);

            Assert.That(world.GetBlock(isolatedLeaf), Is.EqualTo(BlockRegistry.Leafmoss));
        }

        [Test]
        public void TickLeafDecayRemovesLeafmossFarFromLogs()
        {
            // Place isolated Leafmoss with no nearby log
            var isolatedLeaf = new BlockPosition(8, 16, 8);
            world.SetBlock(isolatedLeaf, BlockRegistry.Leafmoss);

            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(isolatedLeaf), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void TickLeafDecaySparesPlayerPlacedLeafmossWithNoLogNearby()
        {
            // Identical setup to TickLeafDecayRemovesLeafmossFarFromLogs — the ONLY difference is
            // the Persistent bit, so this pair isolates exactly what the bit does. If the exemption
            // were unwired, this test fails while its twin still passes.
            var placedLeaf = new BlockPosition(8, 16, 8);
            world.SetBlock(placedLeaf, BlockRegistry.Leafmoss);
            world.SetBlockState(placedLeaf, BlockState.Persistent);

            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(placedLeaf), Is.EqualTo(BlockRegistry.Leafmoss),
                "A hand-placed leaf with no tree under it decayed. Nothing in play tells the player why.");
        }

        [Test]
        public void TickLeafDecayPreservesLeafmossNearLog()
        {
            var logPos  = new BlockPosition(8, 8, 8);
            var leafPos = new BlockPosition(8, 9, 8);
            world.SetBlock(logPos,  BlockRegistry.BranchwoodLog);
            world.SetBlock(leafPos, BlockRegistry.Leafmoss);

            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Leafmoss));
        }

        [Test]
        public void TickLeafDecayPreservesLeafmossNearStrippedLog()
        {
            var logPos  = new BlockPosition(8, 8, 8);
            var leafPos = new BlockPosition(8, 9, 8);
            world.SetBlock(logPos,  BlockRegistry.SmoothBranchwood);
            world.SetBlock(leafPos, BlockRegistry.Leafmoss);

            Assert.That(VegetationService.IsLeafSupportBlock(BlockRegistry.SmoothBranchwood), Is.True);

            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Leafmoss));
        }

        [Test]
        public void TickLeafDecayRemovesOrphanedLeafAfterLogRemovalIsMarked()
        {
            var logPos  = new BlockPosition(8, 8, 8);
            var leafPos = new BlockPosition(8, 9, 8);
            world.SetBlock(logPos,  BlockRegistry.BranchwoodLog);
            world.SetBlock(leafPos, BlockRegistry.Leafmoss);

            // First sweep: the leaf is supported and drops out of the candidate set.
            vegetation.TickLeafDecay(world, 120);
            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Leafmoss));

            // Removing the log re-marks the surrounding leaves (the runtime wires this through
            // CreativeWorldManager.OnBlockChanged); the next sweep removes the orphan.
            world.SetBlock(logPos, BlockRegistry.Air);
            vegetation.MarkLeafDecayCandidates(world, logPos);
            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void TickLeafDecayChecksNewlyPlacedLeafmossViaCandidateMark()
        {
            // Seed the candidate set with an empty world (first sweep), then place an orphan leaf.
            vegetation.TickLeafDecay(world, 120);

            var leafPos = new BlockPosition(8, 16, 8);
            world.SetBlock(leafPos, BlockRegistry.Leafmoss);
            vegetation.MarkLeafDecayCandidate(leafPos);

            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void ScanAndTrackSaplingsPreservesExistingTickAccumulators()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);
            vegetation.TickSapling(world, 1100);

            // Re-scan mid-growth; accumulated ticks must survive.
            vegetation.ScanAndTrackSaplings(world);
            vegetation.TickSapling(world, 100);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S1),
                "ScanAndTrackSaplings must not reset accumulated growth ticks for already-tracked saplings.");
        }

        [Test]
        public void TickSaplingPreservesRemainderAfterGrowthThreshold()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);

            // 1700 = 1200 + 500 remainder
            vegetation.TickSapling(world, 1700);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S1));

            // Only 700 more needed (1200 - 500 remainder) to reach S2
            vegetation.TickSapling(world, 700);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S2),
                "TickSapling must carry over remainder ticks so the next stage uses the correct threshold.");
        }

        [Test]
        public void TickSaplingUsesCorrespondingBiomeTreeVariantViaBiomeResolver()
        {
            // Biome 1 = Pinewild → ConicalTree (PlaceNeedlebranchTree places logs at trunk center column)
            vegetation.Configure((x, z) => 1); // 1 = Pinewild

            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);
            vegetation.TickSapling(world, 3600); // 3 × 1200 ticks to reach S2 + grow
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog),
                "Sapling should grow into a tree using the biome-specified variant.");
        }

        [Test]
        public void TickSaplingDefaultsToStandardTreeWhenNoBiomeResolverSet()
        {
            // No Configure call — resolver is null
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);
            vegetation.TickSapling(world, 3600);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void MarkWildHarvestAddsToRegrowthQueue()
        {
            vegetation.MarkWildHarvest(BlockRegistry.Berrybush, BasePos, currentTick: 0);
            Assert.That(vegetation.WildRegrowthQueueCount, Is.EqualTo(1));
        }

        [Test]
        public void TickWildRegrowthRestoresBlockAfterDelay()
        {
            // Plant a berrybush, mark it harvested (position is Air), tick past the delay.
            // The position's block below (BasePos.Y - 1) must be solid.
            var surface = new BlockPosition(BasePos.X, BasePos.Y - 1, BasePos.Z);
            world.SetBlock(surface, BlockRegistry.MeadowTurf);
            // BasePos itself is Air (default)

            vegetation.MarkWildHarvest(BlockRegistry.Berrybush, BasePos, currentTick: 0);

            // Berrybush delay = 48000; tick to just before — should not restore.
            vegetation.TickWildRegrowth(world, 47999);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Air));

            // Tick past delay — should restore.
            vegetation.TickWildRegrowth(world, 48001);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Berrybush));
            Assert.That(vegetation.WildRegrowthQueueCount, Is.EqualTo(0));
        }

        int CountBlocks(BlockId blockId)
        {
            int count = 0;
            for (int y = 0; y < world.Bounds.Height; y++)
            for (int z = 0; z < world.Bounds.Depth; z++)
            for (int x = 0; x < world.Bounds.Width; x++)
                if (world.GetBlock(new BlockPosition(x, y, z)) == blockId) count++;
            return count;
        }

        static bool HasChangedBlock(VoxelWorld targetWorld, BlockPosition position, BlockId newBlock)
        {
            foreach (BlockChange change in targetWorld.GetChangedBlocks())
            {
                if (change.Position == position && change.NewBlock == newBlock)
                    return true;
            }

            return false;
        }

        static int FindTrunkTop(VoxelWorld w, BlockPosition basePos)
        {
            int top = basePos.Y;
            for (int dy = 0; dy < 20; dy++)
            {
                var pos = new BlockPosition(basePos.X, basePos.Y + dy, basePos.Z);
                if (!w.Bounds.Contains(pos) || w.GetBlock(pos) != BlockRegistry.BranchwoodLog)
                    break;
                top = basePos.Y + dy;
            }
            return top;
        }
    }
}

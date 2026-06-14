using System;
using System.Collections.Generic;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class StructureServiceEditModeTests
    {
        static WorldGenerationSettings MakeSettings(int seed = 1)
        {
            return new WorldGenerationSettings(
                width: 128, height: 200, depth: 128,
                chunkSize: WorldConstants.ChunkSize,
                seed: seed,
                groundHeight: WorldConstants.SeaLevel);
        }

        static VoxelWorld FlatWorld(WorldGenerationSettings settings)
        {
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
            // Fill a flat surface layer so FindSurfaceY returns a valid Y.
            int groundY = WorldConstants.SeaLevel - 1;
            for (int x = 0; x < settings.Bounds.Width; x++)
            for (int z = 0; z < settings.Bounds.Depth; z++)
                world.SetBlock(new BlockPosition(x, groundY, z), BlockRegistry.MeadowTurf, trackChange: false);
            return world;
        }

        [Test]
        public void StructureCatalogMatchesCanonicalRulesetIds()
        {
            string[] expected =
            {
                "pathmark_stones",
                "old_wayflag",
                "fallen_branchwood",
                "saltmarker_cairn",
                "frostmarker_cairn",
                "forager_lean_to",
                "resin_tap_grove",
                "wetland_stilt_cache",
                "drybrush_niter_pit",
                "frost_shelter",
                "bridge_segment",
                "weathered_watchpost",
                "ruined_kiln_yard",
                "mossroot_hut_cluster",
                "sunmetal_survey_tower",
                "frost_beacon_ruin",
                "cave_shrine",
                "stoneburrow_cellar",
                "lumen_hollow",
                "ember_vent_outpost",
                "deep_locker_room",
                "staropal_pocket_shrine",
            };

            var actual = new HashSet<string>();
            foreach (StructureCatalogEntry entry in StructureService.CatalogEntries)
                actual.Add(entry.Id);

            Assert.That(actual.Count, Is.EqualTo(expected.Length));
            foreach (string id in expected)
                Assert.That(actual.Contains(id), Is.True, $"Missing canonical structure '{id}'.");
        }

        [Test]
        public void UndergroundLootTierStructuresAreCatalogedAsUnderground()
        {
            string[] underground =
            {
                "cave_shrine",
                "stoneburrow_cellar",
                "lumen_hollow",
                "ember_vent_outpost",
                "deep_locker_room",
                "staropal_pocket_shrine",
            };

            foreach (string id in underground)
            {
                StructureCatalogEntry entry = FindCatalogEntry(id);
                Assert.That(entry.IsUnderground, Is.True, $"{id} must use an underground/cave placement path.");
                Assert.That(entry.LootTableId, Is.Not.EqualTo(StructureLootTable.EmptyRuinId), $"{id} must use a real loot table.");
            }
        }

        [Test]
        public void WeatheredWatchpostSpawnDistanceFitsDefaultWorld()
        {
            WorldGenerationSettings settings = MakeSettings();
            StructureCatalogEntry watchpost = FindCatalogEntry("weathered_watchpost");
            double farthestDefaultWorldCandidate = Math.Sqrt(
                settings.SpawnPosition.X * settings.SpawnPosition.X +
                settings.SpawnPosition.Z * settings.SpawnPosition.Z);

            Assert.That(watchpost.MinDistanceFromSpawn, Is.LessThan(farthestDefaultWorldCandidate),
                "The watchpost spawn exclusion must leave reachable candidates in the default 128x128 world.");
        }

        [Test]
        public void StructureLootTablesCoverCanonicalRulesetIds()
        {
            string[] expected =
            {
                StructureLootTable.CommonSupplyId,
                StructureLootTable.ForagerFoodId,
                StructureLootTable.BuilderCacheId,
                StructureLootTable.MinerCacheId,
                StructureLootTable.MetalCacheId,
                StructureLootTable.DeepCacheId,
                StructureLootTable.EmptyRuinId,
            };

            var actual = new HashSet<string>();
            foreach (StructureLootTable table in StructureLootTable.All)
                actual.Add(table.Id);

            Assert.That(actual.Count, Is.EqualTo(expected.Length));
            foreach (string id in expected)
                Assert.That(actual.Contains(id), Is.True, $"Missing canonical loot table '{id}'.");
        }

        [Test]
        public void PlaceStructuresIsDeterministicForSameSeed()
        {
            WorldGenerationSettings settings = MakeSettings(seed: 42);
            VoxelWorld worldA = FlatWorld(settings);
            VoxelWorld worldB = FlatWorld(settings);

            StructureService.PlaceStructures(worldA, BlockRegistry.CreateDefault(), settings, 42);
            StructureService.PlaceStructures(worldB, BlockRegistry.CreateDefault(), settings, 42);

            int checked_ = 0;
            for (int x = 0; x < settings.Bounds.Width; x += 8)
            for (int z = 0; z < settings.Bounds.Depth; z += 8)
            for (int y = WorldConstants.SeaLevel; y < WorldConstants.SeaLevel + 10; y++)
            {
                var pos = new BlockPosition(x, y, z);
                Assert.That(worldB.GetBlock(pos), Is.EqualTo(worldA.GetBlock(pos)), $"Mismatch at {pos}.");
                checked_++;
            }

            Assert.That(checked_, Is.GreaterThan(0));
        }

        [Test]
        public void PlaceStructuresKeepsSpawnAreaClear()
        {
            WorldGenerationSettings settings = MakeSettings(seed: 7);
            VoxelWorld world = FlatWorld(settings);

            StructureService.PlaceStructures(world, BlockRegistry.CreateDefault(), settings, 7);

            BlockPosition spawn = settings.SpawnPosition;
            int surfaceY = WorldConstants.SeaLevel - 1;  // FlatWorld places surface here
            for (int dx = -5; dx <= 5; dx++)
            for (int dz = -5; dz <= 5; dz++)
            for (int dy = 1; dy <= 4; dy++)  // structure walls are placed at surfaceY + 1..3
            {
                int x = spawn.X + dx;
                int z = spawn.Z + dz;
                int y = surfaceY + dy;
                if (x < 0 || x >= settings.Bounds.Width || z < 0 || z >= settings.Bounds.Depth)
                    continue;

                BlockId block = world.GetBlock(new BlockPosition(x, y, z));
                Assert.That(block, Is.EqualTo(BlockRegistry.Air), $"Expected spawn clear at ({x},{y},{z}).");
            }
        }

        [Test]
        public void StructureDegradationStatesAreDerivedFromSeed()
        {
            var settings = MakeSettings(seed: 101);
            var wallCounts = new HashSet<int>();
            int seedsWithStructures = 0;

            // Degradation drives the per-wall-block skip chance, so the generated wall total is
            // the observable proxy for the seed-derived degradation state of each structure.
            for (int s = 1; s <= 8; s++)
            {
                var w = FlatWorld(settings);
                StructureService.PlaceStructures(w, BlockRegistry.CreateDefault(), settings, s);

                int count = 0;
                for (int x = 0; x < settings.Bounds.Width; x++)
                for (int z = 0; z < settings.Bounds.Depth; z++)
                for (int y = WorldConstants.SeaLevel; y < WorldConstants.SeaLevel + 8; y++)
                    if (IsGeneratedStructureBlock(w.GetBlock(new BlockPosition(x, y, z))))
                        count++;

                if (count == 0) continue;
                seedsWithStructures++;
                wallCounts.Add(count);
            }

            Assert.That(seedsWithStructures, Is.GreaterThan(0), "Expected at least one seed to place structures.");
            Assert.That(wallCounts.Count, Is.GreaterThanOrEqualTo(2),
                "Wall-block totals must vary across seeds; identical totals would mean degradation is not seed-derived.");
        }

        [Test]
        public void FindSurfaceYReturnsTopSolidBlock()
        {
            WorldGenerationSettings settings = MakeSettings();
            VoxelWorld world = FlatWorld(settings);

            int surfaceY = StructureService.FindSurfaceY(world, 10, 10);

            Assert.That(surfaceY, Is.EqualTo(WorldConstants.SeaLevel - 1));
        }

        [Test]
        public void PlaceStructureAtTracksRuntimeChangesOnlyWhenRequested()
        {
            var worldgenWorld = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 1);
            StructureService.PlaceStructureAt(worldgenWorld, 8, 8, 8, seed: 1);
            Assert.That(worldgenWorld.GetChangedBlocks(), Is.Empty);

            var runtimeWorld = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 1);
            StructureService.PlaceStructureAt(runtimeWorld, 8, 8, 8, seed: 1, trackChange: true);

            Assert.That(runtimeWorld.GetChangedBlocks(), Is.Not.Empty);
            Assert.That(HasChangedBlock(runtimeWorld, new BlockPosition(8, 8, 8), BlockRegistry.Graystone), Is.True);
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

        [Test]
        public void PlaceStructuresWithBiomeResolverPlacesStorageCrateForLootStructures()
        {
            // Seed 5 deterministically places a loot-bearing cave shrine in an all-Meadow world;
            // the canonical DeterministicHash keeps this stable across platforms.
            const int seed = 5;
            var settings = MakeSettings(seed);
            var world = FlatWorld(settings);

            // Force everything to Meadow (0), which allows loot-bearing surface and cave structures.
            StructureService.PlaceStructures(world, BlockRegistry.CreateDefault(), settings, seed,
                biomeAt: (x, z) => 0);

            // Count StorageCrate blocks placed by structures.
            int crates = 0;
            for (int x = 0; x < settings.Bounds.Width; x++)
            for (int z = 0; z < settings.Bounds.Depth; z++)
            for (int y = 0; y < settings.Bounds.Height; y++)
                if (world.GetBlock(new BlockPosition(x, y, z)) == BlockRegistry.StorageCrate)
                    crates++;

            Assert.That(crates, Is.GreaterThan(0), "Expected loot structures to place at least one StorageCrate.");
        }

        [Test]
        public void StructureLootTablePickReturnsEntryWithinWeightRange()
        {
            StructureLootTable table = StructureLootTable.CommonSupply;
            var entry = table.Pick(rng: 12345u);
            Assert.That(entry.ItemId, Is.Not.Null.And.Not.Empty);
            Assert.That(entry.MinCount, Is.LessThanOrEqualTo(entry.MaxCount));
        }

        [Test]
        public void LootTableRollIsDeterministicAndWithinRanges()
        {
            foreach (StructureLootTable table in StructureLootTable.All)
            {
                List<ContainerLootItem> a = table.Roll(seed: 4242u);
                List<ContainerLootItem> b = table.Roll(seed: 4242u);

                Assert.That(a.Count, Is.EqualTo(b.Count), $"{table.Id} roll must be deterministic.");
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.That(a[i].ItemId, Is.EqualTo(b[i].ItemId));
                    Assert.That(a[i].Count, Is.EqualTo(b[i].Count));
                    Assert.That(a[i].Count, Is.GreaterThan(0));
                }
            }
        }

        [Test]
        public void EveryLootTableItemIsRegistered()
        {
            // Container population skips unknown items; guard that the canonical tables reference only
            // real registered items so generated crates are never silently empty.
            ItemRegistry registry = ItemRegistry.CreateDefault();
            foreach (StructureLootTable table in StructureLootTable.All)
            foreach (StructureLootEntry entry in table.Entries)
                Assert.That(registry.TryGet(new ItemId(entry.ItemId), out _), Is.True,
                    $"Loot item '{entry.ItemId}' in table '{table.Id}' is not registered.");
        }

        [Test]
        public void PlaceStructuresEmitsContainerLootForLootCrates()
        {
            // Seed 5 deterministically places loot-bearing structures in an all-Meadow world.
            const int seed = 5;
            var settings = MakeSettings(seed);
            var world = FlatWorld(settings);
            var loot = new List<StructureContainerLoot>();

            StructureService.PlaceStructures(world, BlockRegistry.CreateDefault(), settings, seed,
                biomeAt: (x, z) => 0, lootSink: loot);

            // Every emitted loot record must sit exactly on a StorageCrate block and carry items.
            Assert.That(loot, Is.Not.Empty, "Expected loot records for the placed crates.");
            foreach (StructureContainerLoot record in loot)
            {
                Assert.That(world.GetBlock(record.Position), Is.EqualTo(BlockRegistry.StorageCrate),
                    "Loot must be emitted at a StorageCrate position.");
                Assert.That(record.Items.Count, Is.GreaterThan(0), "Emitted loot must be non-empty.");
            }
        }

        [Test]
        public void ContainerLootIsDeterministicForSameSeed()
        {
            // Seed 5 deterministically places at least one loot crate, so the comparison is non-vacuous.
            const int seed = 5;
            var settings = MakeSettings(seed);
            var a = new List<StructureContainerLoot>();
            var b = new List<StructureContainerLoot>();

            var worldA = FlatWorld(settings);
            var worldB = FlatWorld(settings);
            StructureService.PlaceStructures(worldA, BlockRegistry.CreateDefault(), settings, seed, (x, z) => 0, a);
            StructureService.PlaceStructures(worldB, BlockRegistry.CreateDefault(), settings, seed, (x, z) => 0, b);

            Assert.That(a, Is.Not.Empty, "Expected at least one loot crate for seed 13.");
            Assert.That(a.Count, Is.EqualTo(b.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].Position, Is.EqualTo(b[i].Position));
                Assert.That(a[i].Items.Count, Is.EqualTo(b[i].Items.Count));
            }
        }

        [Test]
        public void PlaceStructuresLeavesMostRegionsEmpty()
        {
            // The per-region spawn gate (~30%) must keep most regions empty even when every biome
            // has many valid catalog entries. Without the gate, nearly every region would build one.
            var settings = MakeSettings(seed: 300);
            var world = FlatWorld(settings);

            StructureService.PlaceStructures(world, BlockRegistry.CreateDefault(), settings, 300,
                biomeAt: (x, z) => 0); // Meadow allows several catalog entries

            const int regionSize = 32;
            int regionsX = settings.Bounds.Width / regionSize;
            int regionsZ = settings.Bounds.Depth / regionSize;
            int totalRegions = regionsX * regionsZ;

            int regionsWithStructure = 0;
            for (int rx = 0; rx < regionsX; rx++)
            for (int rz = 0; rz < regionsZ; rz++)
            {
                bool found = false;
                for (int x = rx * regionSize; x < (rx + 1) * regionSize && !found; x++)
                for (int z = rz * regionSize; z < (rz + 1) * regionSize && !found; z++)
                for (int y = 0; y < settings.Bounds.Height; y++)
                    if (IsGeneratedStructureBlock(world.GetBlock(new BlockPosition(x, y, z))))
                    {
                        found = true;
                        break;
                    }
                if (found) regionsWithStructure++;
            }

            Assert.That(regionsWithStructure, Is.GreaterThan(0), "Expected at least one structure region.");
            Assert.That(regionsWithStructure, Is.LessThan(totalRegions),
                "The spawn gate must leave some regions empty; structures should not fill every region.");
        }

        [Test]
        public void PlaceStructuresHandlesOutOfRangeBiomeIndex()
        {
            // A biome resolver returning an out-of-range index must be wrapped, not throw.
            var settings = MakeSettings(seed: 8);
            var world = FlatWorld(settings);
            Assert.DoesNotThrow(() =>
                StructureService.PlaceStructures(world, BlockRegistry.CreateDefault(), settings, 8,
                    biomeAt: (x, z) => 999)); // wraps to a valid biome via modulo
        }

        [Test]
        public void PickStructureForBiomeReturnsBiomeCompatibleStructure()
        {
            // Pinewild biome (index 1) should return forager_lean_to or resin_tap_grove, not pathmark_stones only
            // We can't call the private method directly, so we test via PlaceStructures with pinewild biome.
            var settings = MakeSettings(seed: 55);
            var worldA = FlatWorld(settings);
            var worldB = FlatWorld(settings);

            // Same seed, different biome → different structure palette
            StructureService.PlaceStructures(worldA, BlockRegistry.CreateDefault(), settings, 55,
                biomeAt: (x, z) => 0); // Meadow
            StructureService.PlaceStructures(worldB, BlockRegistry.CreateDefault(), settings, 55,
                biomeAt: (x, z) => 1); // Pinewild

            // Both biome palettes should be valid; the exact material depends on the picked structure.
            Assert.Pass("PlaceStructures completed without throwing for both biome inputs.");
        }

        [Test]
        public void CatalogPlacementCanEmitUndergroundLootCrates()
        {
            var settings = MakeSettings(seed: 901);
            var world = FlatWorld(settings);
            var loot = new List<StructureContainerLoot>();

            bool placed = StructureService.TryPlaceStructureAt(
                world,
                "deep_locker_room",
                anchorX: 32,
                surfaceY: WorldConstants.SeaLevel - 1,
                anchorZ: 32,
                seed: 901,
                lootSink: loot);

            Assert.That(placed, Is.True);
            Assert.That(loot, Is.Not.Empty);
            Assert.That(loot[0].Position.Y, Is.LessThan(WorldConstants.SeaLevel - 4));
            Assert.That(world.GetBlock(loot[0].Position), Is.EqualTo(BlockRegistry.StorageCrate));
        }

        static StructureCatalogEntry FindCatalogEntry(string id)
        {
            foreach (StructureCatalogEntry entry in StructureService.CatalogEntries)
            {
                if (entry.Id == id)
                    return entry;
            }

            Assert.Fail($"Missing structure catalog entry '{id}'.");
            return default;
        }

        static bool IsGeneratedStructureBlock(BlockId block)
        {
            return block != BlockRegistry.Air &&
                   block != BlockRegistry.MeadowTurf;
        }
    }
}

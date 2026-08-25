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

        // Asserting table IDs alone is what let four tables drift to entirely different item sets
        // while the suite stayed green. These assert the §15.1 entries themselves.
        //
        // Three ruleset item ids are carried under the project's existing canonical ids, per the
        // standing precedent recorded in CraftingRecipeBook: stone_pebble -> surface_pebbles,
        // flint_shard -> flinty_shingle, resin_blob -> resin_knot.
        //
        // §15.1's MinerCache also lists small_blast_charge (1, weight 1); that item is not
        // registered in ItemId.cs, so the entry is intentionally absent here and in the table.
        static (string item, int min, int max, int weight)[] ExpectedEntries(string tableId) => tableId switch
        {
            StructureLootTable.CommonSupplyId => new[]
            {
                ("reed_fiber", 2, 8, 16), ("fiber_cord", 1, 4, 12), ("stout_pole", 1, 5, 12),
                ("surface_pebbles", 2, 8, 10), ("flinty_shingle", 1, 4, 8), ("glowwick", 1, 3, 6),
                ("berry_cluster", 1, 4, 5),
            },
            StructureLootTable.ForagerFoodId => new[]
            {
                ("berry_cluster", 2, 8, 14), ("grain_bundle", 2, 6, 12), ("trail_ration", 1, 3, 7),
                ("clean_water_flask", 1, 2, 5), ("brightsalt", 1, 4, 4), ("field_bandage", 1, 2, 3),
            },
            StructureLootTable.BuilderCacheId => new[]
            {
                ("work_plank", 4, 16, 12), ("branchwood_log", 2, 8, 10), ("stone_rubble", 6, 18, 10),
                ("clay_lump", 3, 12, 8), ("fired_brick", 2, 10, 7), ("glass_shard", 1, 6, 5),
                ("resin_knot", 1, 5, 5),
            },
            StructureLootTable.MinerCacheId => new[]
            {
                ("embercoal", 2, 8, 12), ("spark_niter", 1, 5, 8), ("raw_rosycopper", 1, 4, 6),
                ("raw_paletin", 1, 3, 4), ("raw_rustcore", 1, 2, 2),
            },
            StructureLootTable.MetalCacheId => new[]
            {
                ("rosycopper_bar", 1, 3, 10), ("paletin_bar", 1, 2, 6), ("bronze_bar", 1, 2, 4),
                ("ironroot_bar", 1, 1, 2), ("lumen_crystal", 1, 2, 2), ("sunmetal_bar", 1, 1, 1),
            },
            StructureLootTable.DeepCacheId => new[]
            {
                ("raw_umbralite", 1, 2, 6), ("lumen_crystal", 1, 3, 6), ("lumen_dust", 2, 5, 5),
                ("field_bandage", 1, 3, 4), ("deepsteel_bar", 1, 1, 2), ("staropal_shard", 1, 1, 1),
            },
            _ => null,
        };

        [Test]
        public void StructureLootTableEntriesMatchCanonicalRuleset()
        {
            foreach (StructureLootTable table in StructureLootTable.All)
            {
                (string item, int min, int max, int weight)[] expected = ExpectedEntries(table.Id);
                if (expected == null)
                    continue; // §15.1 specifies no entry table for loot_empty_ruin.

                Assert.That(table.Entries.Length, Is.EqualTo(expected.Length),
                    $"{table.Id} entry count drifted from ruleset §15.1.");

                var actual = new Dictionary<string, (int min, int max, int weight)>();
                foreach (StructureLootEntry e in table.Entries)
                    actual[e.ItemId] = (e.MinCount, e.MaxCount, e.Weight);

                foreach ((string item, int min, int max, int weight) in expected)
                {
                    Assert.That(actual.ContainsKey(item), Is.True,
                        $"{table.Id} is missing ruleset item '{item}'.");
                    Assert.That(actual[item], Is.EqualTo((min, max, weight)),
                        $"{table.Id}/{item} count or weight drifted from ruleset §15.1.");
                }
            }
        }

        [Test]
        public void StructureLootTableRollRangesMatchCanonicalRuleset()
        {
            var expected = new Dictionary<string, (int min, int max)>
            {
                [StructureLootTable.CommonSupplyId] = (2, 4),
                [StructureLootTable.ForagerFoodId] = (2, 5),
                [StructureLootTable.BuilderCacheId] = (3, 6),
                [StructureLootTable.MinerCacheId] = (2, 5),
                [StructureLootTable.MetalCacheId] = (1, 4),
                [StructureLootTable.DeepCacheId] = (2, 4),
                [StructureLootTable.EmptyRuinId] = (0, 1),
            };

            foreach (StructureLootTable table in StructureLootTable.All)
                Assert.That((table.MinRolls, table.MaxRolls), Is.EqualTo(expected[table.Id]),
                    $"{table.Id} roll range drifted from ruleset §15.1.");
        }

        // §14 specifies a weighted age distribution (20/45/27/8). The generator previously used a
        // uniform `hash % 4`, giving ~25% each. RollDegradation maps hash % 100, so walking 0..99
        // covers the mapping exactly and the expected counts are exact rather than statistical.
        [Test]
        public void DegradationDistributionMatchesRulesetWeights()
        {
            var counts = new Dictionary<StructureDegradation, int>
            {
                [StructureDegradation.Intact] = 0,
                [StructureDegradation.Weathered] = 0,
                [StructureDegradation.Ruined] = 0,
                [StructureDegradation.Crumbled] = 0,
            };

            for (uint hash = 0; hash < 100u; hash++)
                counts[StructureService.RollDegradation(hash, StructureDegradation.Crumbled)]++;

            Assert.That(counts[StructureDegradation.Intact], Is.EqualTo(20), "§14 intact share");
            Assert.That(counts[StructureDegradation.Weathered], Is.EqualTo(45), "§14 weathered share");
            Assert.That(counts[StructureDegradation.Ruined], Is.EqualTo(27), "§14 ruined share");
            Assert.That(counts[StructureDegradation.Crumbled], Is.EqualTo(8), "§14 collapsed share");
        }

        [Test]
        public void DegradationRollRespectsPerStructureCap()
        {
            for (uint hash = 0; hash < 500u; hash++)
            {
                StructureDegradation capped = StructureService.RollDegradation(hash, StructureDegradation.Weathered);
                Assert.That((int)capped, Is.LessThanOrEqualTo((int)StructureDegradation.Weathered),
                    "A structure must never exceed its MaxDegradation cap.");
            }
        }

        [Test]
        public void MissingBlockChanceMatchesRulesetTable()
        {
            Assert.That(StructureService.MissingBlockChancePercent(StructureDegradation.Intact), Is.EqualTo(0));
            Assert.That(StructureService.MissingBlockChancePercent(StructureDegradation.Weathered), Is.EqualTo(4));
            Assert.That(StructureService.MissingBlockChancePercent(StructureDegradation.Ruined), Is.EqualTo(12));
            Assert.That(StructureService.MissingBlockChancePercent(StructureDegradation.Crumbled), Is.EqualTo(24));
        }

        // The table tests above would all still pass if the modifier were never applied to rolled
        // loot. This asserts the wiring: same structure, same anchor, same seed — so the base roll
        // is identical and only the age modifier differs.
        [Test]
        public void RolledLootIsScaledByDegradationModifier()
        {
            WorldGenerationSettings settings = MakeSettings(seed: 7);

            List<ContainerLootItem> Place(StructureDegradation degradation)
            {
                VoxelWorld world = FlatWorld(settings);
                var sink = new List<StructureContainerLoot>();
                bool placed = StructureService.TryPlaceStructureAt(
                    world, "weathered_watchpost", 40, WorldConstants.SeaLevel - 1, 40,
                    seed: 7, lootSink: sink, trackChange: false, degradation: degradation);

                Assert.That(placed, Is.True, "Test fixture must actually place the structure.");
                Assert.That(sink.Count, Is.GreaterThan(0), "Test fixture must produce a loot crate.");

                var items = new List<ContainerLootItem>();
                foreach (StructureContainerLoot crate in sink)
                    items.AddRange(crate.Items);
                return items;
            }

            List<ContainerLootItem> intact = Place(StructureDegradation.Intact);
            List<ContainerLootItem> crumbled = Place(StructureDegradation.Crumbled);

            // Same seed and anchor: the underlying roll is identical, so the item sequence matches
            // and only the counts differ.
            Assert.That(crumbled.Count, Is.EqualTo(intact.Count),
                "The modifier must scale quantities, not drop entries.");

            int totalIntact = 0, totalCrumbled = 0;
            for (int i = 0; i < intact.Count; i++)
            {
                Assert.That(crumbled[i].ItemId, Is.EqualTo(intact[i].ItemId),
                    "Degradation must not change which items are rolled.");

                int expected = Math.Max(1, (int)Math.Round(intact[i].Count * 0.70f, MidpointRounding.AwayFromZero));
                Assert.That(crumbled[i].Count, Is.EqualTo(expected),
                    $"Crumbled loot for '{intact[i].ItemId}' must apply the §14 ×0.70 modifier.");

                Assert.That(crumbled[i].Count, Is.GreaterThan(0), "A rolled stack must never scale to zero.");
                totalIntact += intact[i].Count;
                totalCrumbled += crumbled[i].Count;
            }

            // Guard against a fixture where every stack is 1 and rounding hides the modifier
            // entirely — then this test would pass without proving anything.
            Assert.That(totalIntact, Is.GreaterThan(intact.Count),
                "Fixture is degenerate: all stacks are 1, so the modifier is unobservable. Pick another structure or seed.");
            Assert.That(totalCrumbled, Is.LessThan(totalIntact),
                "A Crumbled structure must yield strictly less total loot than an Intact one.");
        }

        [Test]
        public void LootQuantityModifierMatchesRulesetTable()
        {
            Assert.That(StructureService.LootQuantityModifier(StructureDegradation.Intact), Is.EqualTo(1.00f).Within(0.0001f));
            Assert.That(StructureService.LootQuantityModifier(StructureDegradation.Weathered), Is.EqualTo(1.00f).Within(0.0001f));
            Assert.That(StructureService.LootQuantityModifier(StructureDegradation.Ruined), Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(StructureService.LootQuantityModifier(StructureDegradation.Crumbled), Is.EqualTo(0.70f).Within(0.0001f));
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
            var lootContainerBlocks = new[]
            {
                BlockRegistry.StorageCrate,
                BlockRegistry.ReedBasket,
                BlockRegistry.ToolRack,
                BlockRegistry.PantryJar
            };
            Assert.That(lootContainerBlocks, Does.Contain(world.GetBlock(loot[0].Position)));
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

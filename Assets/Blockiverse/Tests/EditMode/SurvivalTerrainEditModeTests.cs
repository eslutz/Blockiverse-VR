using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SurvivalTerrainEditModeTests
    {
        [Test]
        public void SurvivalTerrainSettingsUseExpectedBoundsChunkAndSeed()
        {
            const int seed = 642064;

            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalTerrain(seed);

            Assert.That(settings.Bounds, Is.EqualTo(new WorldBounds(128, 256, 128)));
            Assert.That(settings.ChunkSize, Is.EqualTo(WorldConstants.ChunkSize));
            Assert.That(settings.Seed, Is.EqualTo(seed));
            Assert.That(settings.Bounds.Contains(settings.SpawnPosition), Is.True);
        }

        [Test]
        public void SurvivalTerrainPresetIsDeterministicForSameSeed()
        {
            VoxelWorld first = GenerateSurvivalWorld(seed: 8675309);
            VoxelWorld second = GenerateSurvivalWorld(seed: 8675309);

            AssertWorldsEqual(first, second);
        }

        [Test]
        public void SurvivalTerrainPresetVariesTerrainForDifferentSeeds()
        {
            VoxelWorld first = GenerateSurvivalWorld(seed: 1401);
            VoxelWorld second = GenerateSurvivalWorld(seed: 2609);

            int differentColumns = 0;
            for (int x = 0; x < first.Bounds.Width; x += 2)
            {
                for (int z = 0; z < first.Bounds.Depth; z += 2)
                {
                    if (FindSurfaceY(first, x, z) != FindSurfaceY(second, x, z))
                        differentColumns++;
                }
            }

            Assert.That(differentColumns, Is.GreaterThan(512));
        }

        [Test]
        public void SurvivalTerrainPresetFailsFastWhenWorldHeightCannotFitTerrainBand()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 32,
                height: 64,
                depth: 32,
                chunkSize: WorldConstants.ChunkSize,
                seed: 24601,
                groundHeight: 32);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new SurvivalTerrainPreset(registry, settings).Generate());

            Assert.That(exception.Message, Does.Contain("world height"));
        }

        [Test]
        public void SurvivalTerrainPresetKeepsSpawnAreaClearAndFloored()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalTerrain(seed: 112358);
            VoxelWorld world = new SurvivalTerrainPreset(registry, settings).Generate();

            const int clearRadius = 3;
            const int headroom = 3;
            BlockPosition spawn = settings.SpawnPosition;

            for (int dx = -clearRadius; dx <= clearRadius; dx++)
            {
                for (int dz = -clearRadius; dz <= clearRadius; dz++)
                {
                    if (dx * dx + dz * dz > clearRadius * clearRadius)
                        continue;

                    int x = spawn.X + dx;
                    int z = spawn.Z + dz;
                    var floorPosition = new BlockPosition(x, spawn.Y - 1, z);
                    BlockId floorBlock = world.GetBlock(floorPosition);

                    Assert.That(registry.Get(floorBlock).IsSolid, Is.True, $"Expected solid spawn floor at {floorPosition}.");
                    Assert.That(registry.Get(floorBlock).Category, Is.EqualTo(BlockCategory.Terrain), $"Expected terrain spawn floor at {floorPosition}.");

                    var supportPosition = new BlockPosition(x, spawn.Y - 2, z);
                    Assert.That(world.GetBlock(supportPosition), Is.Not.EqualTo(BlockRegistry.Air), $"Expected solid spawn support at {supportPosition}.");

                    for (int y = spawn.Y; y <= spawn.Y + headroom; y++)
                    {
                        var clearPosition = new BlockPosition(x, y, z);
                        Assert.That(world.GetBlock(clearPosition), Is.EqualTo(BlockRegistry.Air), $"Expected clear spawn air at {clearPosition}.");
                    }
                }
            }
        }

        [Test]
        public void SurvivalTerrainPresetCarvesBoundedUndergroundCavesBelowSurface()
        {
            VoxelWorld world = GenerateSurvivalWorld(seed: 424242);
            int undergroundAir = 0;
            int undergroundSolid = 0;

            for (int x = 0; x < world.Bounds.Width; x++)
            {
                for (int z = 0; z < world.Bounds.Depth; z++)
                {
                    int surfaceY = FindSurfaceY(world, x, z);
                    for (int y = 1; y <= surfaceY - 3; y++)
                    {
                        BlockId block = world.GetBlock(new BlockPosition(x, y, z));
                        if (block == BlockRegistry.Air)
                            undergroundAir++;
                        else
                            undergroundSolid++;
                    }
                }
            }

            Assert.That(undergroundAir, Is.InRange(1500, 200000));
            Assert.That(undergroundAir, Is.LessThan(undergroundSolid / 5));
        }

        [Test]
        public void SurvivalTerrainPresetPlacesCanonicalTerrainLayers()
        {
            VoxelWorld world = GenerateSurvivalWorld(seed: 5050);

            int worldrootCount = 0;
            int deepmantleCount = 0;
            int stoneLevelCount = 0;

            for (int x = 0; x < world.Bounds.Width; x += 4)
            {
                for (int z = 0; z < world.Bounds.Depth; z += 4)
                {
                    for (int y = 0; y <= WorldConstants.BedrockTopY; y++)
                    {
                        BlockId block = world.GetBlock(new BlockPosition(x, y, z));
                        if (block == BlockRegistry.Worldroot) worldrootCount++;
                    }

                    for (int y = WorldConstants.BedrockTopY + 1; y <= 24; y++)
                    {
                        BlockId block = world.GetBlock(new BlockPosition(x, y, z));
                        if (block == BlockRegistry.Deepmantle) deepmantleCount++;
                    }

                    int surfaceY = FindSurfaceY(world, x, z);
                    for (int y = 25; y < surfaceY - 6; y++)
                    {
                        BlockId block = world.GetBlock(new BlockPosition(x, y, z));
                        if (block == BlockRegistry.Graystone || block == BlockRegistry.DarkSlate ||
                            block == BlockRegistry.WarmGranite || block == BlockRegistry.WhiteLimestone ||
                            block == BlockRegistry.BlackBasalt || block == BlockRegistry.Deepmantle)
                            stoneLevelCount++;
                    }
                }
            }

            Assert.That(worldrootCount, Is.GreaterThan(0), "Expected worldroot blocks at bedrock layer.");
            Assert.That(deepmantleCount, Is.GreaterThan(0), "Expected deepmantle blocks in deep layer.");
            Assert.That(stoneLevelCount, Is.GreaterThan(0), "Expected stone-class blocks in mid-depth layer.");
        }

        [Test]
        public void SurvivalTerrainPresetPlacesOrderedUndergroundResourceVeins()
        {
            VoxelWorld world = GenerateSurvivalWorld(seed: 97531);
            var resourceCounts = new Dictionary<BlockId, int>
            {
                { BlockRegistry.EmbercoalSeam, 0 },
                { BlockRegistry.RosycopperBloom, 0 },
                { BlockRegistry.RustcoreOre, 0 }
            };

            for (int x = 0; x < world.Bounds.Width; x++)
            {
                for (int z = 0; z < world.Bounds.Depth; z++)
                {
                    int surfaceY = FindSurfaceY(world, x, z);
                    for (int y = 0; y < world.Bounds.Height; y++)
                    {
                        BlockId block = world.GetBlock(new BlockPosition(x, y, z));
                        if (!resourceCounts.ContainsKey(block))
                            continue;

                        Assert.That(y, Is.LessThanOrEqualTo(surfaceY - 3), $"Expected resource {block} below surface at ({x}, {y}, {z}).");
                        resourceCounts[block]++;
                    }
                }
            }

            Assert.That(resourceCounts[BlockRegistry.EmbercoalSeam], Is.InRange(3000, 80000));
            Assert.That(resourceCounts[BlockRegistry.RosycopperBloom], Is.InRange(1000, 50000));
            Assert.That(resourceCounts[BlockRegistry.RustcoreOre], Is.InRange(400, 25000));
            Assert.That(resourceCounts[BlockRegistry.EmbercoalSeam], Is.GreaterThan(resourceCounts[BlockRegistry.RosycopperBloom]));
            Assert.That(resourceCounts[BlockRegistry.RosycopperBloom], Is.GreaterThan(resourceCounts[BlockRegistry.RustcoreOre]));
        }

        [Test]
        public void SurvivalTerrainPresetSurfaceHeightsStayWithinCanonicalBounds()
        {
            VoxelWorld world = GenerateSurvivalWorld(seed: 7777);

            for (int x = 0; x < world.Bounds.Width; x += 4)
            {
                for (int z = 0; z < world.Bounds.Depth; z += 4)
                {
                    int surfaceY = FindSurfaceY(world, x, z);
                    Assert.That(surfaceY, Is.InRange(40, 190), $"Surface height out of canonical range at ({x},{z}).");
                }
            }
        }

        [Test]
        public void CreativeValidationWorldUsesGeneratedSurvivalTerrain()
        {
            GeneratedCreativeWorld generatedWorld = CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 97531);
            VoxelWorld world = generatedWorld.World;

            Assert.That(generatedWorld.Settings.Bounds, Is.EqualTo(new WorldBounds(128, 256, 128)));
            Assert.That(world.Bounds, Is.EqualTo(generatedWorld.Settings.Bounds));

            int[] sampledSurfaceHeights = Enumerable.Range(0, world.Bounds.Width / 8)
                .Select(index => FindSurfaceY(world, index * 8, index * 8))
                .Distinct()
                .ToArray();

            Assert.That(sampledSurfaceHeights.Length, Is.GreaterThan(1), "Creative validation should use varied survival terrain.");
            Assert.That(CountBlocks(world, BlockRegistry.EmbercoalSeam), Is.GreaterThan(0));
            Assert.That(world.GetBlock(generatedWorld.Settings.SpawnPosition), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void FlatBuilderPresetGeneratesGroundAtSeaLevel()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(32, 128, 32, WorldConstants.ChunkSize, 1001, WorldConstants.SeaLevel);
            VoxelWorld world = new FlatBuilderPreset(registry, settings).Generate();

            int groundY = WorldConstants.SeaLevel - 1;
            for (int x = 0; x < world.Bounds.Width; x += 4)
            {
                for (int z = 0; z < world.Bounds.Depth; z += 4)
                {
                    Assert.That(world.GetBlock(new BlockPosition(x, groundY, z)), Is.EqualTo(BlockRegistry.MeadowTurf));
                    Assert.That(world.GetBlock(new BlockPosition(x, groundY - 1, z)), Is.EqualTo(BlockRegistry.LooseLoam));
                    Assert.That(world.GetBlock(new BlockPosition(x, groundY + 1, z)), Is.EqualTo(BlockRegistry.Air));
                }
            }
        }

        [Test]
        public void VoidBuilderPresetGenerates5x5PlatformAtY64()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(32, 128, 32, WorldConstants.ChunkSize, 1001, 2);
            VoxelWorld world = new VoidBuilderPreset(registry, settings).Generate();

            int cx = settings.Bounds.Width / 2;
            int cz = settings.Bounds.Depth / 2;

            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    Assert.That(world.GetBlock(new BlockPosition(cx + dx, 64, cz + dz)),
                        Is.EqualTo(BlockRegistry.WhiteLimestone),
                        $"Expected WhiteLimestone at platform offset ({dx},{dz}).");
                }
            }

            Assert.That(world.GetBlock(new BlockPosition(cx, 63, cz)), Is.EqualTo(BlockRegistry.Air));
            Assert.That(world.GetBlock(new BlockPosition(cx, 65, cz)), Is.EqualTo(BlockRegistry.Air));
        }

        static VoxelWorld GenerateSurvivalWorld(int seed)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalTerrain(seed);
            return new SurvivalTerrainPreset(registry, settings).Generate();
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

        static int FindSurfaceY(VoxelWorld world, int x, int z)
        {
            for (int y = world.Bounds.Height - 1; y >= 0; y--)
            {
                if (world.GetBlock(new BlockPosition(x, y, z)) != BlockRegistry.Air)
                    return y;
            }

            Assert.Fail($"Expected at least one solid block in column ({x}, {z}).");
            return -1;
        }

        static void AssertWorldsEqual(VoxelWorld first, VoxelWorld second)
        {
            Assert.That(second.Bounds, Is.EqualTo(first.Bounds));
            Assert.That(second.ChunkSize, Is.EqualTo(first.ChunkSize));
            Assert.That(second.Seed, Is.EqualTo(first.Seed));

            for (int y = 0; y < first.Bounds.Height; y++)
            {
                for (int x = 0; x < first.Bounds.Width; x++)
                {
                    for (int z = 0; z < first.Bounds.Depth; z++)
                    {
                        var position = new BlockPosition(x, y, z);
                        Assert.That(second.GetBlock(position), Is.EqualTo(first.GetBlock(position)), $"Mismatched block at {position}.");
                    }
                }
            }
        }
    }
}

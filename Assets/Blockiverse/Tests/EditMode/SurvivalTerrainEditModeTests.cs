using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SurvivalTerrainEditModeTests
    {
        [Test]
        public void SurvivalLiteSettingsUseExpectedBoundsChunkAndSeed()
        {
            const int seed = 642064;

            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite(seed);

            Assert.That(settings.Bounds, Is.EqualTo(new WorldBounds(128, 64, 128)));
            Assert.That(settings.ChunkSize, Is.EqualTo(16));
            Assert.That(settings.Seed, Is.EqualTo(seed));
            Assert.That(settings.Bounds.Contains(settings.SpawnPosition), Is.True);
        }

        [Test]
        public void SurvivalLitePresetIsDeterministicForSameSeed()
        {
            VoxelWorld first = GenerateSurvivalWorld(seed: 8675309);
            VoxelWorld second = GenerateSurvivalWorld(seed: 8675309);

            AssertWorldsEqual(first, second);
        }

        [Test]
        public void SurvivalLitePresetVariesTerrainForDifferentSeeds()
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
        public void SurvivalLitePresetFailsFastWhenWorldHeightCannotFitTerrainBand()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 32,
                height: 16,
                depth: 32,
                chunkSize: 16,
                seed: 24601,
                groundHeight: 8);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new SurvivalLiteWorldPreset(registry, settings).Generate());

            Assert.That(exception.Message, Does.Contain("world height"));
        }

        [Test]
        public void SurvivalLitePresetKeepsSpawnAreaClearAndFloored()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite(seed: 112358);
            VoxelWorld world = new SurvivalLiteWorldPreset(registry, settings).Generate();

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
        public void SurvivalLitePresetCarvesBoundedUndergroundCavesBelowSurface()
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

            Assert.That(undergroundAir, Is.InRange(1500, 60000));
            Assert.That(undergroundAir, Is.LessThan(undergroundSolid / 5));
        }

        [Test]
        public void SurvivalLitePresetPlacesOrderedUndergroundResourceVeins()
        {
            VoxelWorld world = GenerateSurvivalWorld(seed: 97531);
            var resourceCounts = new Dictionary<BlockId, int>
            {
                { BlockRegistry.Coalstone, 0 },
                { BlockRegistry.Copperstone, 0 },
                { BlockRegistry.Ironstone, 0 }
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

            Assert.That(resourceCounts[BlockRegistry.Coalstone], Is.InRange(3000, 20000));
            Assert.That(resourceCounts[BlockRegistry.Copperstone], Is.InRange(1000, 12000));
            Assert.That(resourceCounts[BlockRegistry.Ironstone], Is.InRange(400, 6000));
            Assert.That(resourceCounts[BlockRegistry.Coalstone], Is.GreaterThan(resourceCounts[BlockRegistry.Copperstone]));
            Assert.That(resourceCounts[BlockRegistry.Copperstone], Is.GreaterThan(resourceCounts[BlockRegistry.Ironstone]));
        }

        static VoxelWorld GenerateSurvivalWorld(int seed)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite(seed);
            return new SurvivalLiteWorldPreset(registry, settings).Generate();
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

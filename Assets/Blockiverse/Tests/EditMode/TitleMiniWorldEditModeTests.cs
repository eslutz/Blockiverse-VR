using System.Collections.Generic;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class TitleMiniWorldEditModeTests
    {
        [Test]
        public void DefaultWorldIsTheCompleteHandcraftedTitleShowcase()
        {
            GeneratedCreativeWorld generated = WorldSaveGeneration.GenerateTitleWorld();
            var blocks = new HashSet<BlockId>();

            for (int y = 0; y < generated.World.Bounds.Height; y++)
            for (int x = 0; x < generated.World.Bounds.Width; x++)
            for (int z = 0; z < generated.World.Bounds.Depth; z++)
                blocks.Add(generated.World.GetBlock(new BlockPosition(x, y, z)));

            Assert.That(generated.Settings.Bounds, Is.EqualTo(new WorldBounds(128, 128, 128)));
            Assert.That(blocks, Does.Contain(BlockRegistry.Freshwater), "The title world needs a freshwater river.");
            Assert.That(blocks, Does.Contain(BlockRegistry.Brine), "The title world needs a brine ocean.");
            Assert.That(blocks, Does.Contain(BlockRegistry.Emberflow), "The title world needs a contained emberflow feature.");

            foreach (BlockId vegetation in new[]
            {
                BlockRegistry.Berrybush, BlockRegistry.Reedgrass, BlockRegistry.Thornbrush,
                BlockRegistry.GrainStalk, BlockRegistry.MeadowTuft, BlockRegistry.WildflowerCluster,
                BlockRegistry.MossCarpet, BlockRegistry.SaltReed, BlockRegistry.DrygrassTuft,
                BlockRegistry.DuneSage, BlockRegistry.SnowLichen, BlockRegistry.FrostFern,
                BlockRegistry.WindrootShrub
            })
                Assert.That(blocks, Does.Contain(vegetation), $"Missing title-world vegetation: {vegetation}.");

            foreach (BlockId resource in new[]
            {
                BlockRegistry.EmbercoalSeam, BlockRegistry.RosycopperBloom, BlockRegistry.PaletinThread,
                BlockRegistry.NiterstonePocket, BlockRegistry.RustcoreOre, BlockRegistry.SunmetalFleck,
                BlockRegistry.LumenQuartzCluster, BlockRegistry.UmbraliteNode, BlockRegistry.StaropalGeode
            })
                Assert.That(blocks, Does.Contain(resource), $"Missing title-world resource: {resource}.");
        }

        [Test]
        public void TitleSpawnCourtyardKeepsTheFixedMenuSightlineClear()
        {
            GeneratedCreativeWorld generated = WorldSaveGeneration.GenerateTitleWorld();

            Assert.That(generated.Settings.SpawnPosition, Is.EqualTo(new BlockPosition(64, 65, 64)));

            // The fixed title menu sits directly ahead along +Z. Keep a small, level courtyard
            // around that route so the player's eyes cannot start inside a terrain ripple, river,
            // plant, or structure even though the surrounding showcase remains varied.
            for (int x = 62; x <= 66; x++)
            for (int z = 62; z <= 68; z++)
            {
                Assert.That(generated.World.GetBlock(new BlockPosition(x, 64, z)),
                    Is.EqualTo(BlockRegistry.MeadowTurf), $"Expected a level title courtyard floor at ({x},64,{z}).");

                for (int y = 65; y <= 68; y++)
                    Assert.That(generated.World.GetBlock(new BlockPosition(x, y, z)),
                        Is.EqualTo(BlockRegistry.Air), $"Expected a clear title-menu sightline at ({x},{y},{z}).");
            }
        }
    }
}

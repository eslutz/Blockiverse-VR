using Blockiverse.WorldGen;
using NUnit.Framework;
using System.Collections.Generic;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SurvivalBiomeResolverEditModeTests
    {
        const int WorldHeight = 200;

        [Test]
        public void BiomeIndexIsDeterministicForSameSeedAndPosition()
        {
            var a = new SurvivalBiomeResolver(seed: 4242, WorldHeight);
            var b = new SurvivalBiomeResolver(seed: 4242, WorldHeight);

            for (int i = 0; i < 50; i++)
            {
                int x = i * 37;
                int z = i * 53;
                Assert.That(b.BiomeIndexAt(x, z), Is.EqualTo(a.BiomeIndexAt(x, z)),
                    $"Resolver must be deterministic at ({x},{z}).");
            }
        }

        [Test]
        public void BiomeIndexIsAlwaysInValidRange()
        {
            var resolver = new SurvivalBiomeResolver(seed: 99, WorldHeight);
            for (int x = 0; x < 256; x += 8)
            for (int z = 0; z < 256; z += 8)
            {
                int biome = resolver.BiomeIndexAt(x, z);
                Assert.That(biome, Is.InRange(0, 6), $"Biome index out of range at ({x},{z}).");
            }
        }

        [Test]
        public void CanonicalBiomeIdsResolveThroughWorldGenOwnedMapping()
        {
            Assert.That(SurvivalBiomeResolver.BiomeIndexForCanonicalId("balanced"), Is.EqualTo(SurvivalBiomeResolver.AnyBiomeIndex));
            Assert.That(SurvivalBiomeResolver.BiomeIndexForCanonicalId("meadow"), Is.EqualTo(SurvivalBiomeResolver.MeadowBiomeIndex));
            Assert.That(SurvivalBiomeResolver.BiomeIndexForCanonicalId("pinewild"), Is.EqualTo(SurvivalBiomeResolver.PinewildBiomeIndex));
            Assert.That(SurvivalBiomeResolver.BiomeIndexForCanonicalId("wetland"), Is.EqualTo(SurvivalBiomeResolver.WetlandBiomeIndex));
            Assert.That(SurvivalBiomeResolver.BiomeIndexForCanonicalId("drybrush"), Is.EqualTo(SurvivalBiomeResolver.DrybrushBiomeIndex));
            Assert.That(SurvivalBiomeResolver.BiomeIndexForCanonicalId("dunes"), Is.EqualTo(SurvivalBiomeResolver.DunesBiomeIndex));
            Assert.That(SurvivalBiomeResolver.BiomeIndexForCanonicalId("tundra"), Is.EqualTo(SurvivalBiomeResolver.TundraBiomeIndex));
            Assert.That(SurvivalBiomeResolver.BiomeIndexForCanonicalId("highlands"), Is.EqualTo(SurvivalBiomeResolver.HighlandsBiomeIndex));
            Assert.That(SurvivalBiomeResolver.IsTundraBiomeIndex(SurvivalBiomeResolver.TundraBiomeIndex), Is.True);
            Assert.That(SurvivalBiomeResolver.IsTundraBiomeIndex(SurvivalBiomeResolver.MeadowBiomeIndex), Is.False);
        }

        [Test]
        public void ResolverProducesMultipleDistinctBiomes()
        {
            var resolver = new SurvivalBiomeResolver(seed: 6401, WorldHeight);
            var biomes = new HashSet<int>();
            for (int x = 0; x < 1024; x += 16)
            for (int z = 0; z < 1024; z += 16)
                biomes.Add(resolver.BiomeIndexAt(x, z));

            Assert.That(biomes.Count, Is.GreaterThan(1),
                "A large region should classify into more than one biome.");
        }

        [Test]
        public void SurfaceHeightStaysWithinWorldBounds()
        {
            var resolver = new SurvivalBiomeResolver(seed: 7, WorldHeight);
            for (int x = 0; x < 256; x += 16)
            for (int z = 0; z < 256; z += 16)
            {
                int y = resolver.SurfaceHeight(x, z);
                Assert.That(y, Is.InRange(40, WorldHeight - 1), $"Surface height out of bounds at ({x},{z}).");
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentBiomeMaps()
        {
            var a = new SurvivalBiomeResolver(seed: 1, WorldHeight);
            var b = new SurvivalBiomeResolver(seed: 2, WorldHeight);

            bool everDiffered = false;
            for (int x = 0; x < 512 && !everDiffered; x += 16)
            for (int z = 0; z < 512 && !everDiffered; z += 16)
                if (a.BiomeIndexAt(x, z) != b.BiomeIndexAt(x, z))
                    everDiffered = true;

            Assert.That(everDiffered, Is.True, "Different seeds should produce different biome maps.");
        }
    }
}

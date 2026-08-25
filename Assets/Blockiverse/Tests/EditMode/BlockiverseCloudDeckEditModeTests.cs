using System.Collections.Generic;
using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseCloudDeckEditModeTests
    {
        readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        BlockiverseCloudDeck CreateDeck(int seed = 4242)
        {
            var host = new GameObject("Cloud Deck Under Test");
            spawned.Add(host);
            BlockiverseCloudDeck deck = host.AddComponent<BlockiverseCloudDeck>();
            deck.Configure(host.transform, null, new Rect(0.0f, 0.0f, 0.1f, 0.1f), seed);
            return deck;
        }

        [Test]
        public void TheDeckSitsAboveAnythingAPlayerCanBuild()
        {
            // A deck inside world bounds is reachable, which means handling the camera being
            // INSIDE a cloud -- backface culling, fog, the lot. Sitting above the build limit
            // skips that case entirely, and that is the only reason it is not handled.
            Assert.That(BlockiverseCloudDeck.AltitudeMeters,
                Is.GreaterThan(Blockiverse.WorldGen.WorldConstants.WorldMaxY),
                "The deck must sit above WorldMaxY or players can build into it.");
        }

        [Test]
        public void CoverageProducesRoughlyThatFractionOfSky()
        {
            // The previous version of this test asserted only that clear == 0, light > 0 and
            // overcast > light. All three held while the mapping was catastrophically wrong:
            // coverage 0.05 produced 57% cloud and coverage 0.30 produced 91%, because a 3x3
            // average of uniform hashes concentrates at 0.5 with a standard deviation of 0.096,
            // so a band around the mean swallowed nearly everything. Monotonicity was true;
            // magnitude was nonsense. Clear zero passed only because it is special-cased.
            //
            // Measure the actual fraction instead.
            BlockiverseCloudDeck deck = CreateDeck();

            float Fraction(float coverage)
            {
                int occupied = 0;
                int total = 0;

                for (int z = -60; z < 60; z++)
                {
                    for (int x = -60; x < 60; x++)
                    {
                        total++;
                        if (deck.IsCloudCell(x, z, coverage))
                            occupied++;
                    }
                }

                return occupied / (float)total;
            }

            Assert.That(Fraction(0.0f), Is.EqualTo(0.0f), "Clear must be an empty sky.");

            foreach (float coverage in new[] { 0.1f, 0.3f, 0.5f, 0.7f })
            {
                float actual = Fraction(coverage);
                Assert.That(actual, Is.EqualTo(coverage).Within(0.12f),
                    $"Coverage {coverage:0.00} produced {actual:0.00} of the sky. The quantile table " +
                    "no longer matches the density field it was measured from.");
            }
        }

        [Test]
        public void CloudCellsClumpRatherThanScatter()
        {
            // A raw per-cell hash thresholded by coverage gives salt-and-pepper, which reads as
            // television static rather than weather -- the same failure the sky shader's hash had
            // one level up. The field is smoothed over a 3x3 neighbourhood to make masses; this
            // measures that the smoothing is actually doing something.
            BlockiverseCloudDeck deck = CreateDeck();

            int occupied = 0;
            int isolated = 0;

            for (int z = -25; z < 25; z++)
            {
                for (int x = -25; x < 25; x++)
                {
                    if (!deck.IsCloudCell(x, z, 0.5f))
                        continue;

                    occupied++;

                    bool hasNeighbour =
                        deck.IsCloudCell(x - 1, z, 0.5f) || deck.IsCloudCell(x + 1, z, 0.5f) ||
                        deck.IsCloudCell(x, z - 1, 0.5f) || deck.IsCloudCell(x, z + 1, 0.5f);

                    if (!hasNeighbour)
                        isolated++;
                }
            }

            Assert.That(occupied, Is.GreaterThan(0));
            Assert.That(isolated / (float)occupied, Is.LessThan(0.15f),
                $"{isolated} of {occupied} cloud cells stand alone; the field is scattering, not clumping.");
        }

        [Test]
        public void TheSameSeedAndCoordinateAlwaysGiveTheSameSky()
        {
            // Clouds are never replicated -- they are a pure function of (seed, clock), which is
            // the whole reason nothing goes on the wire. If two peers disagreed here they would
            // see different weather with no mechanism to reconcile it.
            BlockiverseCloudDeck first = CreateDeck(seed: 90210);
            BlockiverseCloudDeck second = CreateDeck(seed: 90210);
            BlockiverseCloudDeck other = CreateDeck(seed: 90211);

            int agreements = 0;
            int differences = 0;

            for (int z = -20; z < 20; z++)
            {
                for (int x = -20; x < 20; x++)
                {
                    Assert.That(first.IsCloudCell(x, z, 0.6f), Is.EqualTo(second.IsCloudCell(x, z, 0.6f)),
                        $"Same seed disagreed at ({x}, {z}).");

                    if (first.IsCloudCell(x, z, 0.6f) == other.IsCloudCell(x, z, 0.6f))
                        agreements++;
                    else
                        differences++;
                }
            }

            // Negative control: a different seed must actually produce a different sky, or the
            // agreement above would be meaningless.
            Assert.That(differences, Is.GreaterThan(0),
                $"A different seed produced an identical sky across {agreements} cells; the seed is not reaching the hash.");
        }
    }
}

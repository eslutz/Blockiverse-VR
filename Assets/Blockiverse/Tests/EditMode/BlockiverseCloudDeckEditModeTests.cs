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
        public void CoverageDrivesHowMuchOfTheSkyIsCloud()
        {
            BlockiverseCloudDeck deck = CreateDeck();

            int Occupied(float coverage)
            {
                int count = 0;
                for (int z = -30; z < 30; z++)
                {
                    for (int x = -30; x < 30; x++)
                    {
                        if (deck.IsCloudCell(x, z, coverage))
                            count++;
                    }
                }

                return count;
            }

            int clear = Occupied(0.0f);
            int light = Occupied(0.3f);
            int overcast = Occupied(1.0f);

            Assert.That(clear, Is.Zero, "Zero coverage must produce an empty sky, not sparse debris.");
            Assert.That(light, Is.GreaterThan(0), "Light cover must actually put some cloud up there.");
            Assert.That(overcast, Is.GreaterThan(light),
                "Overcast must be cloudier than light cover, or coverage is not connected to anything.");
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

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

            // Within 0.02, NOT 0.12, and the low end is checked.
            //
            // At 0.12 this test passed while the shipped table rendered 0.07% of cells for a
            // requested 1.78% — a 25x miss that reads as a completely empty sky, comfortably
            // inside a twelve-point band. A tolerance wide enough to swallow the failure mode is
            // not a test of the mapping. The table is now measured to 0.46 percentage points at
            // its worst across all ten weather states, so 0.02 is generous.
            foreach (float coverage in new[] { 0.02f, 0.1f, 0.3f, 0.5f, 0.7f, 0.9f })
            {
                float actual = Fraction(coverage);
                Assert.That(actual, Is.EqualTo(coverage).Within(0.02f),
                    $"Coverage {coverage:0.000} produced {actual:0.000} of the sky. The quantile table " +
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

        [Test]
        public void ClearWeatherIsAlmostAnEmptySkyAndPartlyCloudyIsStillMostlyBlue()
        {
            // The weather service's Clear is 0.10 and PartlyCloudy is 0.45, and feeding those to
            // the deck RAW is what Eric reported twice: "clear skies still have too much cloud
            // coverage and it quickly escalates from there". A deck cell is not a unit of sky —
            // its 5 m of thickness presents a side at grazing angles, which is where most of the
            // sky's solid angle is, and the skybox veil carries the same coverage on top.
            //
            // Note what this test does NOT do: assert monotonicity. The mapping this replaced was
            // monotone and produced 57% cloud at coverage 0.05.
            Assert.That(BlockiverseCloudDeck.DeckCoverage(0.10f), Is.LessThan(0.05f),
                "Clear must leave the sky essentially open.");
            Assert.That(BlockiverseCloudDeck.DeckCoverage(0.45f), Is.LessThan(0.30f),
                "Partly cloudy must still be mostly blue.");
            Assert.That(BlockiverseCloudDeck.DeckCoverage(1.0f), Is.EqualTo(1.0f).Within(1e-4f),
                "A thunderstorm must still be able to close the sky completely.");
            Assert.That(BlockiverseCloudDeck.DeckCoverage(0.0f), Is.EqualTo(0.0f).Within(1e-4f));
        }

        [Test]
        public void TheDeckBoundaryIsACircleAndNotTheGridSquare()
        {
            // A filled square grid has a boundary whose distance from the player swings by 41%
            // between an edge midpoint and a corner, so at a fixed altitude the rim rises and
            // falls around the compass and reads as the man-made edge it is.
            // Sampled INSIDE the dissolve band, not on the radius: out at the radius every
            // direction clamps to 1 and the three would agree however the fade were computed, so
            // the test would pass against the square it is meant to rule out.
            float alongX = BlockiverseCloudDeck.RimFade(35, 0);
            float alongZ = BlockiverseCloudDeck.RimFade(0, 35);
            float diagonal = BlockiverseCloudDeck.RimFade(25, 25); // radius 35.36

            Assert.That(alongX, Is.InRange(0.05f, 0.95f), "The sample must land inside the band.");
            Assert.That(alongZ, Is.EqualTo(alongX).Within(1e-4f));
            Assert.That(diagonal, Is.EqualTo(alongX).Within(0.02f),
                "The rim must sit at one RADIUS, not at one grid coordinate.");

            // And the grid's own corner has to be cut away, or the square is still there.
            int half = BlockiverseCloudDeck.GridCells / 2;
            Assert.That(BlockiverseCloudDeck.RimFade(half - 1, half - 1), Is.GreaterThanOrEqualTo(1.0f),
                "The corner of the grid is outside the radius and must be culled.");
        }

        [Test]
        public void CoverageThinsOutTowardTheRimInsteadOfStopping()
        {
            // Measured through REAL BUILT GEOMETRY (RebuildAt + the mesh it produces), not a
            // hand-rolled copy of the fade formula. The first version of this test recomputed
            // "Mathf.Lerp(cutoff, 1.01f, fade)" itself instead of calling into CellTop, which is
            // the actual method that applies the fade — so it verified its own arithmetic, not the
            // deck's. Proven by mutation: setting CellTop's cutoff to the un-faded baseCutoff left
            // this test green while every rim cell filled solid.
            //
            // Only the DENOMINATOR (how many grid cells exist in each ring) is computed
            // independently — that is pure geometry, not the behaviour under test.
            // SetSky takes WEATHER coverage and remaps it through DeckCoverage (^1.75) before it
            // reaches the deck, so "0.5" here does not mean 50% of cells -- it means whatever
            // DeckCoverage(0.5) computes to. Deriving the assertions from that same public,
            // separately-tested function is what keeps this correct if the exponent ever changes;
            // a hardcoded expectation here already went stale once when DeckCoverage was added.
            const float WeatherCoverage = 0.5f;
            float requestedCoverage = BlockiverseCloudDeck.DeckCoverage(WeatherCoverage);

            BlockiverseCloudDeck deck = CreateDeck();
            deck.SetSky(WeatherCoverage, Color.white, Color.gray, Color.blue);
            deck.RebuildAt(0, 0);

            Mesh mesh = deck.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;

            Assert.That(verts.Length, Is.GreaterThan(0), "The deck built nothing to inspect.");

            var occupiedCells = new System.Collections.Generic.HashSet<(int, int)>();

            for (int i = 0; i < verts.Length; i += 4)
            {
                if (norms[i].y <= 0.5f)
                    continue; // top faces only, one per occupied cell

                float centerX = (verts[i].x + verts[i + 1].x + verts[i + 2].x + verts[i + 3].x) / 4.0f;
                float centerZ = (verts[i].z + verts[i + 1].z + verts[i + 2].z + verts[i + 3].z) / 4.0f;
                int gx = Mathf.FloorToInt(centerX / BlockiverseCloudDeck.CellMeters);
                int gz = Mathf.FloorToInt(centerZ / BlockiverseCloudDeck.CellMeters);
                occupiedCells.Add((gx, gz));
            }

            (int occupied, int total) Annulus(float from, float to)
            {
                int occupied = 0;
                int total = 0;

                for (int z = -46; z < 46; z++)
                {
                    for (int x = -46; x < 46; x++)
                    {
                        float radius = Mathf.Sqrt(x * (float)x + z * (float)z);
                        if (radius < from || radius >= to)
                            continue;

                        total++;
                        if (occupiedCells.Contains((x, z)))
                            occupied++;
                    }
                }

                return (occupied, total);
            }

            (int innerOccupied, int innerTotal) = Annulus(0.0f, 18.0f);
            (int outerOccupied, int outerTotal) = Annulus(40.0f, 45.0f);

            Assert.That(innerTotal, Is.GreaterThan(0));
            Assert.That(outerTotal, Is.GreaterThan(0));

            float inner = innerOccupied / (float)innerTotal;
            float outer = outerOccupied / (float)outerTotal;

            Assert.That(inner, Is.GreaterThan(requestedCoverage * 0.7f),
                $"Requested {requestedCoverage:0.00} deck coverage; the middle measured {inner:0.00}.");
            Assert.That(outer, Is.LessThan(inner * 0.35f),
                $"The outer ring holds {outer:0.00} of its cells against {inner:0.00} in the middle. " +
                "Without a real falloff the deck ends on a line no colour fade can hide.");
        }

        [Test]
        public void TheWeatherStatesTheGameActuallyProducesLandWhereTheyAreAimed()
        {
            // The mapping end to end, at the ten coverages WeatherService can actually emit, rather
            // than at round numbers. Clear is the one that matters and the one that was broken:
            // 1.78% of cells intended, 0.07% delivered.
            BlockiverseCloudDeck deck = CreateDeck();

            float Fraction(float weather)
            {
                float target = BlockiverseCloudDeck.DeckCoverage(weather);
                int occupied = 0;
                int total = 0;

                for (int z = -60; z < 60; z++)
                {
                    for (int x = -60; x < 60; x++)
                    {
                        total++;
                        if (deck.IsCloudCell(x, z, target))
                            occupied++;
                    }
                }

                return occupied / (float)total;
            }

            foreach (float weather in new[] { 0.10f, 0.45f, 0.65f, 0.80f, 0.85f, 0.95f, 1.00f })
            {
                float target = BlockiverseCloudDeck.DeckCoverage(weather);
                float actual = Fraction(weather);
                Assert.That(actual, Is.EqualTo(target).Within(0.02f),
                    $"Weather {weather:0.00} asks the deck for {target:0.000} of the sky and gets {actual:0.000}.");
            }

            // And Clear specifically is a FEW CLOUDS, not none and not overcast — the two ways
            // this has actually been wrong.
            float clear = Fraction(0.10f);
            Assert.That(clear, Is.GreaterThan(0.005f), $"Clear rendered {clear:0.000} of the sky: an empty deck.");
            Assert.That(clear, Is.LessThan(0.05f), $"Clear rendered {clear:0.000} of the sky: too busy for a clear day.");
        }

        [Test]
        public void SideFacesSpanTheHEIGHTSTEPBetweenNeighboursNotJustEmptyGaps()
        {
            // Cell thickness varies — with the density margin taper and again with the radial rim
            // taper — so two adjacent FILLED cells routinely differ in height. A side face emitted
            // only when the neighbour is empty leaves the wall between them missing, and since the
            // far side is back-face culled you see straight through the mass.
            //
            // Detected by orientation: with the old binary test every side quad started at y = 0.
            // A side quad whose bottom edge is above zero can only come from the step-filling path.
            BlockiverseCloudDeck deck = CreateDeck();
            deck.SetSky(0.6f, Color.white, Color.gray, Color.blue);
            deck.RebuildAt(0, 0);

            Mesh mesh = deck.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;

            Assert.That(verts.Length, Is.GreaterThan(0), "The deck built nothing to inspect.");
            Assert.That(norms.Length, Is.EqualTo(verts.Length), "Normals must be written, not inferred.");

            int sideQuads = 0;
            int steppedSideQuads = 0;

            // AddQuad emits exactly four vertices per quad, in order, so groups of four are quads.
            for (int i = 0; i < verts.Length; i += 4)
            {
                if (Mathf.Abs(norms[i].y) > 0.5f)
                    continue;

                sideQuads++;

                // For a side quad the first vertex sits on the lower edge.
                if (verts[i].y > 0.01f)
                    steppedSideQuads++;
            }

            Assert.That(sideQuads, Is.GreaterThan(0), "No side faces at all; the fixture proves nothing.");
            Assert.That(steppedSideQuads, Is.GreaterThan(0),
                $"All {sideQuads} side faces start at y=0, so every height step between two filled " +
                "cells is an unsealed hole you can see straight through.");
        }

        [Test]
        public void ChangingTheSKYCOLOURRepaintsWithoutRebuildingGeometry()
        {
            // Now that the sky variant is unlit, the baked vertex colour is the deck's ONLY colour.
            // Rebuild used to be the only thing that wrote it, and drift alone forces one just
            // every 6.25 s — so the deck advanced through dawn and dusk in six-second steps.
            BlockiverseCloudDeck deck = CreateDeck();
            deck.SetSky(0.6f, Color.white, Color.gray, Color.blue);
            deck.RebuildAt(0, 0);

            Mesh mesh = deck.GetComponent<MeshFilter>().sharedMesh;
            Color[] before = mesh.colors;
            int quadsBefore = deck.BuiltQuadCount;
            int rebuildsBefore = deck.RebuildCount;

            Assert.That(before.Length, Is.GreaterThan(0));

            deck.SetSky(0.6f, Color.red, Color.magenta, Color.green);
            Color[] after = mesh.colors;

            Assert.That(deck.RebuildCount, Is.EqualTo(rebuildsBefore),
                "A colour change must not rebuild geometry.");
            Assert.That(deck.BuiltQuadCount, Is.EqualTo(quadsBefore));
            Assert.That(after.Length, Is.EqualTo(before.Length));

            int changed = 0;
            for (int i = 0; i < after.Length; i++)
            {
                if (after[i] != before[i])
                    changed++;
            }

            Assert.That(changed, Is.GreaterThan(0),
                "The vertex colours did not follow the sky, so the deck will hold whatever tint it " +
                "was built with until something forces a rebuild.");
        }

        [Test]
        public void HeadJitterOnACellBoundaryDoesNotRebuildTheDeck()
        {
            // The trigger reads FloorToInt(head.x / CellMeters). A tracked head never holds still,
            // so a player standing ON a 10 m boundary flips that index back and forth on jitter
            // alone and rebuilds the entire deck — 8,464 density evaluations plus a full mesh
            // build — every frame, silently.
            var host = new GameObject("Deck Follow Target");
            spawned.Add(host);

            var deckHost = new GameObject("Deck Under Test");
            spawned.Add(deckHost);
            BlockiverseCloudDeck deck = deckHost.AddComponent<BlockiverseCloudDeck>();
            deck.Configure(host.transform, null, new Rect(0.0f, 0.0f, 0.1f, 0.1f), 4242);
            deck.SetSky(0.5f, Color.white, Color.gray, Color.blue);

            // Exactly on a cell boundary, which is where FloorToInt is least stable.
            // Counted on CellResolveCount, NOT RebuildCount. A rebuild has a second, entirely
            // legitimate cause — wind drift crossing a cell — and EditMode's Time.deltaTime on a
            // loaded machine is large enough that sixty ticks simulate ~19 s of drift, i.e. three
            // real cell crossings. The first version of this test counted rebuilds and blamed
            // jitter for those three. Measure the thing the dead zone actually guards.
            host.transform.position = new Vector3(BlockiverseCloudDeck.CellMeters * 3.0f, 0.0f, 0.0f);
            deck.TickForTests();
            int afterFirst = deck.CellResolveCount;
            Assert.That(afterFirst, Is.EqualTo(1), "The first tick must resolve the cell exactly once.");

            for (int i = 0; i < 60; i++)
            {
                float jitter = (i % 2 == 0 ? 1.0f : -1.0f) * 0.0008f;
                host.transform.position = new Vector3(BlockiverseCloudDeck.CellMeters * 3.0f + jitter, 0.0f, 0.0f);
                deck.TickForTests();
            }

            Assert.That(deck.CellResolveCount, Is.EqualTo(afterFirst),
                $"Sub-millimetre head jitter re-resolved the deck's cell {deck.CellResolveCount - afterFirst} " +
                "extra times; without the dead zone each of those is a full rebuild.");

            // Negative half: real locomotion still has to move the deck, or the dead zone has
            // simply frozen it.
            host.transform.position = new Vector3(BlockiverseCloudDeck.CellMeters * 9.0f, 0.0f, 0.0f);
            deck.TickForTests();
            Assert.That(deck.CellResolveCount, Is.GreaterThan(afterFirst),
                "Walking a whole cell must still move the deck.");
        }

        [Test]
        public void TheDeckGrewButItsCellCountStayedBounded()
        {
            // Honest about what this is: a size bound, not a performance measurement. The grid
            // went from a filled 56x56 square to a circle inscribed in 92x92, which is 2.7x the
            // grid but only ~2.1x the live cells, and it is affordable only because density is now
            // evaluated ONCE per cell and cached instead of recomputed for the cell and each of
            // its four neighbours. The cache itself is not observable from here; what this stops
            // is someone raising GridCells again without a device capture.
            int cells = 0;
            for (int z = -46; z < 46; z++)
            {
                for (int x = -46; x < 46; x++)
                {
                    if (BlockiverseCloudDeck.RimFade(x, z) < 1.0f)
                        cells++;
                }
            }

            // The circle keeps about pi/4 of the square, so the enlargement costs well under the
            // 5x per-cell evaluation it removed.
            Assert.That(cells, Is.LessThan(56 * 56 * 5),
                $"{cells} live cells; the cache has to be buying more than the grid growth costs.");
            Assert.That(cells, Is.GreaterThan(56 * 56),
                "The deck must actually be bigger than the square it replaced.");
        }
    }
}

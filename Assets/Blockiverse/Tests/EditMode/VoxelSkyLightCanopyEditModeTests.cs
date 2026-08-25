using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // How much daylight gets through a canopy, and — the part that was wrong — WHICH leaves are
    // charged for it.
    //
    // Eric, 2026-08-25: "once you start stacking layers of canopy it still gets pretty dark pretty
    // quick in the woods, even midday on a clear day. What is the mechanism causing darker canopy
    // shadows in some areas than others?" Two mechanisms, and both are pinned here: the layer
    // product is Beer-Lambert and compounds without limit, and it used to be computed once per
    // COLUMN, so a cell inside a crown paid for the leaves below it as well.
    public sealed class VoxelSkyLightCanopyEditModeTests
    {
        static VoxelSkyLightMap Build(VoxelWorld world) =>
            new(world, BlockRegistry.CreateDefault());

        static VoxelWorld EmptyWorld() =>
            new(new WorldBounds(16, 32, 16), chunkSize: 16, seed: 1);

        static void Leaves(VoxelWorld world, int x, int z, int fromY, int toY)
        {
            for (int y = fromY; y <= toY; y++)
                world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Leafmoss, trackChange: false);
        }

        [Test]
        public void OpenSkyIsFullTransmittance()
        {
            VoxelWorld world = EmptyWorld();
            Assert.That(Build(world).SkyTransmittance(new BlockPosition(4, 8, 4)), Is.EqualTo(1.0f));
        }

        [Test]
        public void ASolidRoofStillBlacksOutCompletely()
        {
            // The floor added below must NOT leak through anything opaque. If it did, a sealed
            // room would sit at a quarter of full daylight and no test of leaves would notice.
            VoxelWorld world = EmptyWorld();
            world.SetBlock(new BlockPosition(4, 12, 4), BlockRegistry.Graystone, trackChange: false);

            Assert.That(Build(world).SkyTransmittance(new BlockPosition(4, 8, 4)), Is.EqualTo(0.0f),
                "Opaque means opaque; the diffuse floor is a canopy term, not an ambient term.");
        }

        [Test]
        public void EachExtraLeafLayerShadesLessThanTheOneBeforeIt()
        {
            // The complaint, and the fix. A pure product goes 0.45, 0.20, 0.09, 0.04 — by four
            // layers a wood at noon is darker than a cave mouth, and no per-layer transmission
            // value repairs that because halving the extinction only moves which layer count goes
            // black. Skylight is hemispherical, so it saturates instead.
            float one = Sample(1);
            float two = Sample(2);
            float four = Sample(4);
            float eight = Sample(8);

            float Sample(int layers)
            {
                VoxelWorld w = EmptyWorld();
                Leaves(w, 4, 4, 20, 20 + layers - 1);
                return Build(w).SkyTransmittance(new BlockPosition(4, 10, 4));
            }

            // Still ordered: a thick crown is darker than a thin one, or every canopy looks alike.
            Assert.That(two, Is.LessThan(one));
            Assert.That(four, Is.LessThan(two));

            // But it converges instead of collapsing.
            Assert.That(eight, Is.GreaterThanOrEqualTo(VoxelSkyLightMap.DiffuseCanopyFloor),
                $"Eight layers of canopy transmit {eight:0.000}; a wood must not go darker than the floor.");
            Assert.That(eight, Is.GreaterThan(0.2f),
                "A deep wood at noon has to stay readable in a headset.");

            // The negative half: the floor must not have flattened the whole model.
            Assert.That(one - four, Is.GreaterThan(0.15f),
                $"One layer ({one:0.00}) and four ({four:0.00}) are nearly the same; the floor has " +
                "swallowed the difference between a thin crown and a dense one.");
        }

        [Test]
        public void ACellInsideTheCanopyIsChargedOnlyForTheLeavesAboveIt()
        {
            // The bug this replaces: transmittance was one cached product per column, so the cell
            // at the TOP of a crown was charged for every leaf beneath it too. That is most of the
            // canopy — every interior and underside face the mesher bakes — coming out several
            // times darker than the model intended, which is why the woods went dark faster than
            // the per-layer number says they should.
            VoxelWorld world = EmptyWorld();
            Leaves(world, 4, 4, 20, 23);
            var map = Build(world);

            float nearTheTop = map.SkyTransmittance(new BlockPosition(4, 22, 4));   // one leaf above
            float beneathItAll = map.SkyTransmittance(new BlockPosition(4, 10, 4)); // four above

            Assert.That(nearTheTop, Is.GreaterThan(beneathItAll),
                $"A cell one leaf below the crown's top transmits {nearTheTop:0.000} and the forest " +
                $"floor transmits {beneathItAll:0.000}: the column product is being applied to both.");

            // Pinned against the single-layer answer so the test knows WHICH leaves were counted,
            // not merely that the two differ.
            VoxelWorld single = EmptyWorld();
            Leaves(single, 6, 6, 23, 23);
            float oneLayer = Build(single).SkyTransmittance(new BlockPosition(6, 10, 6));

            Assert.That(nearTheTop, Is.EqualTo(oneLayer).Within(1e-4f),
                "One leaf overhead is one leaf overhead, wherever the rest of the crown is.");
        }

        [Test]
        public void CanopyLightIsONEValueForRenderingAndGameplay()
        {
            // Eric's call, 2026-08-25: one number, not a rendering value and a gameplay value that
            // can drift apart. Simpler to reason about and to debug.
            VoxelWorld world = EmptyWorld();
            Leaves(world, 4, 4, 20, 20);
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var map = new VoxelSkyLightMap(world, registry);
            var under = new BlockPosition(4, 10, 4);

            float gameplay = VoxelLightSampler.SampleAirLight(world, registry, under, skyLight: map);
            float rendered = VoxelLightSampler.SampleSkyExposure(world, registry, under, skyLight: map);

            Assert.That(gameplay, Is.EqualTo(rendered).Within(1e-4f),
                $"Gameplay sees {gameplay:0.000} and the mesher sees {rendered:0.000}. Two values for " +
                "one physical quantity is exactly what this design decided against.");

            // And it is the FLOORED value, not the bare product — otherwise nothing was fixed.
            Assert.That(rendered, Is.GreaterThan(0.45f),
                "One leaf layer transmits 0.45 before the diffuse floor; the floor is not being applied.");
        }

        [Test]
        public void TheFloorsEffectOnFARMINGStaysBounded()
        {
            // The one value feeds crop growth, so the floor moves farming. That is accepted — but
            // only because the move is BOUNDED, and this is what makes "bounded" structural rather
            // than a claim in a comment.
            //
            // Read from FarmingService itself, never copied: a hardcoded 8 here would stop guarding
            // anything the day someone retuned the crop.
            int grain = FarmingService.MinimumLightFor(BlockRegistry.GrainStalk);
            int berry = FarmingService.MinimumLightFor(BlockRegistry.Berrybush);
            int reed = FarmingService.MinimumLightFor(BlockRegistry.Reedgrass);

            Assert.That(grain, Is.GreaterThan(0), "The crop thresholds did not resolve; this test is vacuous.");

            float Light(int layers)
            {
                VoxelWorld world = EmptyWorld();
                if (layers > 0)
                    Leaves(world, 4, 4, 20, 20 + layers - 1);

                BlockRegistry registry = BlockRegistry.CreateDefault();
                var map = new VoxelSkyLightMap(world, registry);
                return VoxelLightSampler.SampleAirLight(
                    world, registry, new BlockPosition(4, 10, 4), skyLight: map)
                    * VoxelLightSampler.MaxEmissiveLevel;
            }

            // THE BOUND. The floor's asymptote sits below the least demanding crop, so however
            // thick the canopy gets, farming under it still stops. Raising the floor for visual
            // reasons past this line opens farming under a closed forest, and this is where that
            // has to be a decision rather than a surprise.
            float asymptote = VoxelSkyLightMap.DiffuseCanopyFloor * VoxelLightSampler.MaxEmissiveLevel;
            int leastDemanding = Mathf.Min(grain, Mathf.Min(berry, reed));

            Assert.That(asymptote, Is.LessThan(leastDemanding),
                $"The canopy floor bottoms out at {asymptote:0.00} on the 0-15 scale and the least " +
                $"demanding crop needs {leastDemanding}. Nothing now stops farming under a closed canopy.");

            // And the documented boundary: a fringe of canopy farms, a forest does not.
            Assert.That(Light(1), Is.GreaterThanOrEqualTo(grain),
                "One leaf layer should clear grain — that is the accepted consequence of the floor.");
            Assert.That(Light(3), Is.LessThan(reed),
                "Three layers must clear nothing, or the canopy has stopped mattering to farming.");
        }

        [Test]
        public void ACellAboveTheWholeCanopySeesFullSky()
        {
            VoxelWorld world = EmptyWorld();
            Leaves(world, 4, 4, 20, 23);

            Assert.That(Build(world).SkyTransmittance(new BlockPosition(4, 24, 4)), Is.EqualTo(1.0f),
                "Standing on top of a tree is standing in the open.");
        }

        [Test]
        public void PlacingALeafBlockDarkensTheColumnBelowItImmediately()
        {
            // The incremental path, which is the one a player exercises. A rescan that missed the
            // new layer would leave a canopy that shades only after a reload.
            VoxelWorld world = EmptyWorld();
            Leaves(world, 4, 4, 20, 20);
            var map = Build(world);

            float before = map.SkyTransmittance(new BlockPosition(4, 10, 4));

            var added = new BlockPosition(4, 21, 4);
            world.SetBlock(added, BlockRegistry.Leafmoss, trackChange: false);
            map.ApplyChange(new BlockChange(added, BlockRegistry.Air, BlockRegistry.Leafmoss), out _, out _);

            float after = map.SkyTransmittance(new BlockPosition(4, 10, 4));

            Assert.That(after, Is.LessThan(before),
                $"Adding a leaf layer left the ground at {after:0.000}, unchanged from {before:0.000}.");
        }
    }
}

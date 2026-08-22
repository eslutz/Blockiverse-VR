using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.VR;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // The hands are the only lit thing in the game that is not on the voxel shader, so they are the
    // only thing that ignores caves. These pin the tint that replaces that missing gate.
    public sealed class BlockiverseHandLightDriverEditModeTests
    {
        [Test]
        public void EveryPlayerSeesTheSameHandColour()
        {
            // There used to be an owner/remote split. It disambiguated nothing -- your own hands
            // are always the pair attached to your view -- and it put the local player's hands in
            // a blue that did not read as a hand at all.
            var host = new GameObject("Hand Colour Rig");

            try
            {
                BlockiverseNetworkAvatarRig rig = host.AddComponent<BlockiverseNetworkAvatarRig>();
                rig.ConfigureFirstPersonFallbackVisuals(true);
                rig.SetMetaAvatarAvailable(false);

                Assert.That(
                    typeof(BlockiverseNetworkAvatarRig).GetField(
                        "ownerFallbackColor", BindingFlags.Instance | BindingFlags.NonPublic),
                    Is.Null,
                    "The owner/remote colour split should be gone, not merely unused.");

                Renderer handRenderer = rig.LeftHandAnchor.GetComponentInChildren<Renderer>(includeInactive: true);
                Color rendered = handRenderer.sharedMaterial.color;

                Assert.That(rendered.r, Is.GreaterThan(rendered.b), "Hands should read warm, not blue.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SealedCellsAreDarkerThanOpenSkyButNeverFullyBlack()
        {
            // Same sampler the voxel shader's bake uses, so the hands and the walls around them
            // agree about how dark the room is.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 32, 16), chunkSize: 16, seed: 7);

            // A sealed pocket: solid stone everywhere, one air cell in the middle.
            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 20; y++)
                {
                    for (int z = 0; z < 16; z++)
                        world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);
                }
            }

            var sealedCell = new BlockPosition(8, 8, 8);
            world.SetBlock(sealedCell, BlockRegistry.Air, trackChange: false);

            float sealedLight = VoxelLightSampler.SampleAirLight(world, registry, sealedCell);
            float openLight = VoxelLightSampler.SampleAirLight(world, registry, new BlockPosition(8, 25, 8));

            Assert.That(openLight, Is.GreaterThan(sealedLight),
                "Fixture guard: open sky must sample brighter than a sealed pocket.");
            Assert.That(sealedLight, Is.LessThan(0.5f), "A sealed stone pocket should be dark.");

            // The floor is what stops the player losing their own hands. Losing sight of your only
            // body in a dark cave is disorienting in a way the world going dark is not.
            Assert.That(BlockiverseHandLightDriver.MinimumHandLight, Is.GreaterThan(0.0f));
            Assert.That(BlockiverseHandLightDriver.MinimumHandLight, Is.LessThan(0.25f),
                "The floor must be a hint of shape, not a light source.");
        }

        [Test]
        public void SamplingIsThrottledAndBlended()
        {
            // SampleAirLight walks up to six directions for several steps per call, so it must not
            // run per hand per frame; and an unblended step pops both hands between two
            // brightnesses in a single frame when crossing a cave mouth.
            Assert.That(BlockiverseHandLightDriver.SampleIntervalSeconds, Is.GreaterThan(0.0f));
            Assert.That(BlockiverseHandLightDriver.SampleIntervalSeconds, Is.LessThanOrEqualTo(0.2f),
                "Too coarse and the hands visibly lag the player through a doorway.");
            Assert.That(BlockiverseHandLightDriver.BlendSeconds, Is.GreaterThan(0.0f));
        }

        [Test]
        public void TheFallbackHandMaterialKeepsItsShading()
        {
            // Deliberately LIT. Unlit did fix the cave brightness, but it also flattened the hands
            // into cut-outs. The cave gate comes from the driver scaling albedo instead, so the
            // shading can stay.
            var host = new GameObject("Hand Light Driver Rig");

            try
            {
                BlockiverseNetworkAvatarRig rig = host.AddComponent<BlockiverseNetworkAvatarRig>();
                rig.ConfigureFirstPersonFallbackVisuals(true);
                rig.SetMetaAvatarAvailable(false);

                Renderer handRenderer = rig.LeftHandAnchor.GetComponentInChildren<Renderer>(includeInactive: true);

                Assert.That(handRenderer, Is.Not.Null, "Fixture guard: the left hand should have a renderer.");
                Assert.That(handRenderer.sharedMaterial, Is.Not.Null);
                Assert.That(
                    handRenderer.sharedMaterial.shader.name,
                    Does.Not.Contain("Unlit"),
                    "Unlit hands read as flat cut-outs; the darkness gate belongs in the albedo.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}

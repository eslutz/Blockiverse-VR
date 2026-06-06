using System.Collections;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class LightingCyclePlayModeTests
    {
        const string BootSceneName = "Boot";

        [UnityTest]
        public IEnumerator BootSceneCreatesLightingCycleAndShadowCastingSun()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);
            yield return null;

            BlockiverseLightingCycleController controller = Object.FindFirstObjectByType<BlockiverseLightingCycleController>();

            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.Clock, Is.Not.Null);
            Assert.That(controller.SunLight, Is.Not.Null);
            Assert.That(controller.SunLight.type, Is.EqualTo(LightType.Directional));
            Assert.That(controller.SunLight.shadows, Is.EqualTo(LightShadows.Hard));
            Assert.That(RenderSettings.ambientLight.grayscale, Is.LessThan(0.35f));
        }

        [UnityTest]
        public IEnumerator TorchbudLightManagerTracksPlacedAndRemovedTorchbuds()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);
            var host = new GameObject("Torchbud Light Manager");

            try
            {
                TorchbudLightManager manager = host.AddComponent<TorchbudLightManager>();
                manager.Configure(world, registry);

                var torchPosition = new BlockPosition(3, 2, 3);
                world.SetBlock(torchPosition, BlockRegistry.Torchbud);
                yield return null;

                Assert.That(manager.ActiveLightCount, Is.EqualTo(1));
                Assert.That(manager.TryGetLight(torchPosition, out Light light), Is.True);
                Assert.That(light.type, Is.EqualTo(LightType.Point));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
                Assert.That(light.transform.position, Is.EqualTo(TorchbudLightManager.GetLightPosition(torchPosition)));

                world.SetBlock(torchPosition, BlockRegistry.Air);
                yield return null;

                Assert.That(manager.ActiveLightCount, Is.Zero);
            }
            finally
            {
                Object.Destroy(host);
            }
        }
    }
}

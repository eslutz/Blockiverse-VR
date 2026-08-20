using System.Collections;
using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
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
            Assert.That(controller.SunLight.shadows, Is.Not.EqualTo(LightShadows.None));
            Assert.That(controller.SunLight.shadowStrength, Is.GreaterThan(0.0f));
            Assert.That(RenderSettings.ambientLight.grayscale, Is.LessThan(0.35f));

            // Exactly one directional light drives both bodies — URP only ever promotes one
            // directional to the main light, so a separate moon object would silently become a
            // costly additional light (and would leak past the by-name teardown in EditMode tests).
            Light[] directionals = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Where(light => light.type == LightType.Directional)
                .ToArray();
            Assert.That(directionals, Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BootSceneRendersMoonlitNightInsteadOfDarkness()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);
            yield return null;

            BlockiverseLightingCycleController controller = Object.FindFirstObjectByType<BlockiverseLightingCycleController>();
            Assert.That(controller, Is.Not.Null);

            controller.Clock.SetNormalizedTime(0.75f);
            controller.ApplyCurrentLighting();
            yield return null;

            Assert.That(controller.IsMoonPrimary, Is.True);
            Assert.That(controller.SunLight.intensity, Is.GreaterThan(0.0f),
                "Night must be lit by the moon, not left pitch black.");

            // Lighting the ground requires the light to come from above the horizon.
            Vector3 towardLight = -(controller.SunLight.transform.rotation * Vector3.forward);
            Assert.That(towardLight.y, Is.GreaterThan(0.9f));

            Assert.That(RenderSettings.ambientLight.grayscale, Is.GreaterThan(0.02f),
                "Night ambient must stay above the near-black floor that made the world unnavigable.");
        }

        [UnityTest]
        public IEnumerator VoxelChunksCastAndReceiveShadows()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);
            yield return null;

            MeshRenderer chunkRenderer = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .FirstOrDefault(renderer => renderer.gameObject.name.StartsWith("Chunk "));

            Assert.That(chunkRenderer, Is.Not.Null, "The Boot scene should generate at least one chunk.");
            Assert.That(chunkRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
            Assert.That(chunkRenderer.receiveShadows, Is.True);
        }

        [UnityTest]
        public IEnumerator GlowwickLightManagerCreatesRealtimePointLightsForPlacedEmitters()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);
            var host = new GameObject("Glowwick Light Manager");

            try
            {
                GlowwickLightManager manager = host.AddComponent<GlowwickLightManager>();
                manager.Configure(world, registry);

                var torchPosition = new BlockPosition(3, 2, 3);
                world.SetBlock(torchPosition, BlockRegistry.Glowwick);
                yield return null;

                Assert.That(manager.ActiveLightCount, Is.EqualTo(1));
                Assert.That(manager.ActiveEmitterCount, Is.EqualTo(1));
                Assert.That(manager.IsTrackingEmitter(torchPosition), Is.True);
                Assert.That(manager.TryGetLight(torchPosition, out Light light), Is.True);
                Assert.That(light, Is.Not.Null);
                Assert.That(light.type, Is.EqualTo(LightType.Point));
                Assert.That(host.GetComponentsInChildren<Light>(includeInactive: true), Has.Length.EqualTo(1));

                world.SetBlock(torchPosition, BlockRegistry.Air);
                yield return null;

                Assert.That(manager.ActiveLightCount, Is.Zero);
                Assert.That(manager.ActiveEmitterCount, Is.Zero);
            }
            finally
            {
                Object.Destroy(host);
            }
        }
    }
}

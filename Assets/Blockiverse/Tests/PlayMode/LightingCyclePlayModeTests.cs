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
        public IEnumerator SkyFlashRaisesAmbientAndReturnsToBaseline()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);
            yield return null;

            BlockiverseLightingCycleController controller = Object.FindFirstObjectByType<BlockiverseLightingCycleController>();
            Assert.That(controller, Is.Not.Null);

            controller.Clock.SetNormalizedTime(0.75f);
            controller.ApplyCurrentLighting();
            yield return null;

            float baseline = RenderSettings.ambientLight.grayscale;

            controller.PulseSkyFlash(1.0f);
            yield return null;

            Assert.That(RenderSettings.ambientLight.grayscale, Is.GreaterThan(baseline),
                "A full-strength flash has to visibly lift ambient.");
            Assert.That(controller.ActiveSkyFlashIntensity, Is.GreaterThan(0.0f));

            // Longer than FlashDurationSeconds at any plausible frame rate.
            float deadline = Time.time + 1.0f;
            while (Time.time < deadline)
                yield return null;

            Assert.That(controller.ActiveSkyFlashIntensity, Is.EqualTo(0.0f),
                "Any residual would bleed into ambient permanently -- the term is re-added every frame.");
            Assert.That(RenderSettings.ambientLight.grayscale, Is.EqualTo(baseline).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator SkyFlashNeverFlipsTheShadowPassAtNight()
        {
            // The most valuable assertion in this file. At night the sun sits below
            // MinimumShadowCastingIntensity; if the flash modulated the SUN rather than ambient it
            // would toggle the whole shadow pass on and off for two frames -- a full shadow-caster
            // sweep over every loaded chunk, with every shadow in the scene snapping in and out.
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);
            yield return null;

            BlockiverseLightingCycleController controller = Object.FindFirstObjectByType<BlockiverseLightingCycleController>();
            Assert.That(controller, Is.Not.Null);

            controller.Clock.SetNormalizedTime(0.75f);
            controller.ApplyCurrentLighting();
            yield return null;

            LightShadows shadowsBefore = controller.SunLight.shadows;
            float intensityBefore = controller.SunLight.intensity;

            controller.PulseSkyFlash(1.0f);

            float deadline = Time.time + 0.6f;
            while (Time.time < deadline)
            {
                Assert.That(controller.SunLight.shadows, Is.EqualTo(shadowsBefore),
                    "The flash must not touch the sun's shadow mode at any point in its life.");
                Assert.That(controller.SunLight.intensity, Is.EqualTo(intensityBefore).Within(1e-4f),
                    "The flash must modulate ambient only.");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SkyFlashRefusesToStrobeOnCloselySpacedStrikes()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);
            yield return null;

            BlockiverseLightingCycleController controller = Object.FindFirstObjectByType<BlockiverseLightingCycleController>();
            Assert.That(controller, Is.Not.Null);

            controller.PulseSkyFlash(1.0f);

            // Let the flash get well past its peak but stay inside the retrigger window, so a
            // second pulse would visibly spike the intensity back up if it were allowed through.
            float settleDeadline =
                Time.time + BlockiverseLightingCycleController.MinimumFlashRetriggerSeconds * 0.6f;
            while (Time.time < settleDeadline)
                yield return null;

            float decayed = controller.ActiveSkyFlashIntensity;

            controller.PulseSkyFlash(1.0f);
            yield return null;

            Assert.That(controller.ActiveSkyFlashIntensity, Is.LessThanOrEqualTo(decayed),
                "A second strike inside the retrigger window restarted the flash -- two close " +
                "strikes would compound into a strobe.");
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

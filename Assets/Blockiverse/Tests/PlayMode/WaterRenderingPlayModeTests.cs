using System.Collections;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    // The underwater view: fog forced on while the eye is inside a fluid cell, the camera clear
    // swapped away from the skybox, and both restored on surfacing. These run on real frame timing
    // because the 0.25 s cross-fade and the clear-flag restore are frame-ordering behaviour that a
    // pure function test cannot reach.
    public sealed class WaterRenderingPlayModeTests
    {
        const int MaxBlendFrames = 60;

        GameObject managerObject;
        GameObject cameraObject;
        GameObject lightingObject;
        GameObject worldlessObject;
        bool fogWasEnabled;
        Color fogColorBefore;
        float fogDensityBefore;
        FogMode fogModeBefore;

        [SetUp]
        public void SetUp()
        {
            // Batchmode starves wall-clock deltaTime, which would leave the blend frozen near zero
            // and make every assertion below a coin flip.
            Time.captureDeltaTime = 1.0f / 60.0f;

            fogWasEnabled = RenderSettings.fog;
            fogColorBefore = RenderSettings.fogColor;
            fogDensityBefore = RenderSettings.fogDensity;
            fogModeBefore = RenderSettings.fogMode;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0.0f;

            if (managerObject != null)
                Object.Destroy(managerObject);
            if (cameraObject != null)
                Object.Destroy(cameraObject);
            if (lightingObject != null)
                Object.Destroy(lightingObject);
            if (worldlessObject != null)
                Object.Destroy(worldlessObject);

            RenderSettings.fog = fogWasEnabled;
            RenderSettings.fogColor = fogColorBefore;
            RenderSettings.fogDensity = fogDensityBefore;
            RenderSettings.fogMode = fogModeBefore;
        }

        [UnityTest]
        public IEnumerator SubmergingTheHeadRaisesUnderwaterFogAndSurfacingRestoresIt()
        {
            BlockiverseWaterView waterView = CreateFixture(withLightingCycle: true);

            MoveEyeTo(WaterCellCentre());
            yield return BlendTo(waterView, 1.0f);

            Assert.That(waterView.SubmergedBlend, Is.EqualTo(1.0f).Within(0.001f),
                "The eye is inside a freshwater cell, so the blend must reach fully submerged.");
            Assert.That(waterView.SubmergedFamily, Is.EqualTo(FluidFamily.Freshwater));
            Assert.That(RenderSettings.fog, Is.True,
                "Fog must be forced on underwater even in clear weather -- clear weather is exactly when it would otherwise be off.");
            Assert.That(RenderSettings.fogMode, Is.EqualTo(FogMode.ExponentialSquared));
            // Absolute arithmetic from the public constants — full density scaled to the
            // fixture's known 2.5 m eye depth. This pins the whole chain: the probe finds the
            // surface, the ramp scales the density, and the lighting controller applies it.
            Assert.That(RenderSettings.fogDensity,
                Is.EqualTo(ExpectedFreshwaterDensityAtFixtureDepth()).Within(0.001f),
                "2.5 m under, the applied density must be the family constant scaled by the ramp.");
            Assert.That(ExpectedFreshwaterDensityAtFixtureDepth(),
                Is.LessThan(BlockiverseWaterView.FogDensityFor(FluidFamily.Freshwater)),
                "Fixture guard: this depth must genuinely sit inside the ramp, not past its floor.");

            MoveEyeTo(AirCellCentre());
            yield return BlendTo(waterView, 0.0f);

            Assert.That(waterView.SubmergedBlend, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(RenderSettings.fog, Is.False,
                "Surfacing in clear weather must hand fog back to the weather path, which wants it off.");
        }

        [UnityTest]
        public IEnumerator UnderwaterFogAppliesWithNoWorldTimeClockInTheScene()
        {
            // ApplyCurrentLighting returns early with no clock or sun, and that is a real runtime
            // state: CreativeWorldManager.ConfigureEnvironmentServices bails the same way. If the
            // underwater branch sat below that guard, water would be perfectly clear there.
            BlockiverseWaterView waterView = CreateFixture(withLightingCycle: true, withClockAndSun: false);

            MoveEyeTo(WaterCellCentre());
            yield return BlendTo(waterView, 1.0f);

            Assert.That(RenderSettings.fog, Is.True,
                "Underwater fog must survive the clock/sun guard in BlockiverseLightingCycleController.");
            Assert.That(RenderSettings.fogDensity,
                Is.EqualTo(ExpectedFreshwaterDensityAtFixtureDepth()).Within(0.001f),
                "The clockless path must apply the same depth-ramped density as the normal one.");
        }

        [UnityTest]
        public IEnumerator EmberflowSubmersionIsDenserAndHotterThanFreshwater()
        {
            BlockiverseWaterView waterView = CreateFixture(withLightingCycle: true);

            MoveEyeTo(EmberflowCellCentre());
            yield return BlendTo(waterView, 1.0f);

            Assert.That(waterView.SubmergedFamily, Is.EqualTo(FluidFamily.Emberflow),
                "The sampled family comes from the cell the eye is in, not from whichever fluid was seen first.");
            // FULL density, exactly — emberflow is exempt from the depth ramp. Every lava pool
            // worldgen places is shallow, so ramping it made lava effectively transparent
            // everywhere it exists; molten rock is opaque at any depth.
            Assert.That(RenderSettings.fogDensity,
                Is.EqualTo(BlockiverseWaterView.FogDensityFor(FluidFamily.Emberflow)).Within(0.001f),
                "Being submerged in lava must be near-blinding at ANY depth — the ramp is a water model.");
            Assert.That(RenderSettings.fogColor.r, Is.GreaterThan(RenderSettings.fogColor.b));
        }

        [UnityTest]
        public IEnumerator CameraClearFlagsAndBackgroundAreRestoredAfterSurfacing()
        {
            BlockiverseWaterView waterView = CreateFixture(withLightingCycle: false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            Color backgroundBefore = camera.backgroundColor;

            MoveEyeTo(WaterCellCentre());
            yield return BlendTo(waterView, 1.0f);

            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor),
                "URP does not fog the skybox, so a skybox clear would leave a crisp horizon visible from the lake bed.");

            MoveEyeTo(AirCellCentre());
            yield return BlendTo(waterView, 0.0f);

            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox),
                "The clear flags the rig shipped with must come back exactly, not be left on SolidColor.");
            Assert.That(camera.backgroundColor, Is.EqualTo(backgroundBefore),
                "The background colour is restored to the cached value, not to an approximation of it.");
        }

        [UnityTest]
        public IEnumerator LoadingANewWorldWhileSubmergedClearsTheView()
        {
            // Boot.unity never unloads, so New World and Load do not fire OnDisable or OnDestroy on
            // this component -- CreativeWorldManager swaps the VoxelWorld instance underneath it.
            // Nothing else would reset the camera clear if this path did not.
            BlockiverseWaterView waterView = CreateFixture(withLightingCycle: false);
            CreativeWorldManager manager = managerObject.GetComponent<CreativeWorldManager>();
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;

            MoveEyeTo(WaterCellCentre());
            yield return BlendTo(waterView, 1.0f);

            LoadWaterFreeWorld(manager);
            yield return BlendTo(waterView, 0.0f);

            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox),
                "Loading a world where the eye is no longer underwater must hand the camera clear back.");
        }

        [UnityTest]
        public IEnumerator AManagerWithNoWorldClearsTheViewInTheSameFrame()
        {
            // The pre-first-world state: a CreativeWorldManager exists but has generated nothing.
            // Fading out over a quarter second there would tint the title menu on the way in.
            BlockiverseWaterView waterView = CreateFixture(withLightingCycle: false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;

            MoveEyeTo(WaterCellCentre());
            yield return BlendTo(waterView, 1.0f);

            // A manager that never initialized a world, handed over explicitly. Destroying the real
            // one instead would let the component re-resolve to whatever CreativeWorldManager some
            // earlier test left in the scene, which is what made an earlier version of this test
            // pass alone and fail inside the suite.
            worldlessObject = new GameObject("Worldless Manager");
            CreativeWorldManager worldless = worldlessObject.AddComponent<CreativeWorldManager>();

            Assert.That(worldless.World, Is.Null,
                "Fixture precondition: a bare CreativeWorldManager must not generate a world on Awake.");

            waterView.Configure(worldless, camera);
            yield return null;
            yield return null;

            Assert.That(waterView.SubmergedBlend, Is.EqualTo(0.0f).Within(0.001f),
                "With no world to sample, the blend snaps to zero rather than fading.");
            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox),
                "The camera clear must be handed back as soon as there is no world behind the view.");
        }

        BlockiverseWaterView CreateFixture(bool withLightingCycle, bool withClockAndSun = true)
        {
            managerObject = new GameObject("Water View World");
            CreativeWorldManager manager = CreateWorldWithFluidColumns(managerObject);

            cameraObject = new GameObject("Water View Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            MoveEyeTo(AirCellCentre());

            BlockiverseWaterView waterView = cameraObject.AddComponent<BlockiverseWaterView>();
            waterView.Configure(manager, camera);

            if (!withLightingCycle)
                return waterView;

            lightingObject = new GameObject("Water View Lighting");
            WorldTimeClock clock = withClockAndSun ? lightingObject.AddComponent<WorldTimeClock>() : null;
            Light sun = withClockAndSun ? lightingObject.AddComponent<Light>() : null;

            if (sun != null)
                sun.type = LightType.Directional;

            BlockiverseLightingCycleController lighting = lightingObject.AddComponent<BlockiverseLightingCycleController>();
            lighting.Configure(clock, sun, manager, waterView);

            return waterView;
        }

        void MoveEyeTo(Vector3 position)
        {
            cameraObject.transform.position = position;
        }

        static IEnumerator BlendTo(BlockiverseWaterView waterView, float target)
        {
            for (int frame = 0; frame < MaxBlendFrames; frame++)
            {
                if (Mathf.Approximately(waterView.SubmergedBlend, target))
                {
                    // One more frame so the lighting controller's LateUpdate publishes the fog for
                    // the settled blend value.
                    yield return null;
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Submersion blend stalled at {waterView.SubmergedBlend} instead of reaching {target} within {MaxBlendFrames} frames.");
        }

        // Cell centres, so a hysteresis nudge in either direction cannot land the probe in a
        // neighbouring cell and make the test depend on the hysteresis constant.

        // The fixture's water column spans cells y = 1..5, so its surface (top face of cell 5)
        // is at world y = 6.0 and the eye at WaterCellCentre (y = 3.5) sits exactly 2.5 m under.
        // Computed from the PUBLIC ramp constants rather than read back from the view: an
        // assertion that compares the view against itself cannot fail, and a review caught
        // exactly that shape here once already.
        static float ExpectedFreshwaterDensityAtFixtureDepth()
        {
            const float fixtureEyeDepth = 2.5f;
            float rampScale = Mathf.Lerp(
                BlockiverseWaterView.MinimumDepthDensityScale,
                1.0f,
                fixtureEyeDepth / BlockiverseWaterView.FogDepthRampMeters);
            return BlockiverseWaterView.FogDensityFor(FluidFamily.Freshwater) * rampScale;
        }

        static Vector3 WaterCellCentre() => new(1.5f, 3.5f, 1.5f);

        static Vector3 EmberflowCellCentre() => new(14.5f, 3.5f, 14.5f);

        static Vector3 AirCellCentre() => new(8.5f, 8.5f, 8.5f);

        static void LoadWaterFreeWorld(CreativeWorldManager manager)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                16, 12, 16, chunkSize: 4, seed: 72, groundHeight: 1, spawnPosition: new BlockPosition(8, 2, 8));
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);

            for (int z = 0; z < settings.Bounds.Depth; z++)
            for (int x = 0; x < settings.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.MeadowTurf, trackChange: false);

            manager.InitializeGeneratedWorld(
                new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
        }

        static CreativeWorldManager CreateWorldWithFluidColumns(GameObject managerObject)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            // The two columns sit at opposite corners, further apart than freshwater's 8-cell
            // spread, so the live FluidFlowService this manager ticks can never let them meet and
            // rewrite the cell a test is sampling.
            var settings = new WorldGenerationSettings(
                16, 12, 16, chunkSize: 4, seed: 71, groundHeight: 1, spawnPosition: new BlockPosition(8, 2, 8));
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);

            for (int z = 0; z < settings.Bounds.Depth; z++)
            for (int x = 0; x < settings.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.MeadowTurf, trackChange: false);

            for (int y = 1; y <= 5; y++)
            {
                world.SetBlock(new BlockPosition(1, y, 1), BlockRegistry.Freshwater, trackChange: false);
                world.SetBlock(new BlockPosition(14, y, 14), BlockRegistry.Emberflow, trackChange: false);
            }

            CreativeWorldManager manager = managerObject.AddComponent<CreativeWorldManager>();
            manager.InitializeGeneratedWorld(
                new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
            return manager;
        }
    }
}

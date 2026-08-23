using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blockiverse.Tests.EditMode
{
    // The world simulation must run with NO presentation component in the process at all. That is
    // the dedicated-server case (ADR 0007): Blockiverse.Gameplay is excluded from the server
    // platform, so there is no renderer, no scene lighting, no interaction rig, and nothing that
    // needs a texture atlas.
    //
    // Every test here deliberately attaches ONLY CreativeWorldManager. Do not add a presentation
    // component to make one pass — that would delete the coverage.
    public sealed class HeadlessWorldRuntimeEditModeTests
    {
        GameObject clockObject;
        GameObject worldObject;

        [SetUp]
        public void SetUp()
        {
            // The clock has to be on an ACTIVE object: ConfigureEnvironmentServices resolves it with
            // FindFirstObjectByType, which does not see inactive objects.
            clockObject = new GameObject("Headless World Time Clock");
            WorldTimeClock clock = clockObject.AddComponent<WorldTimeClock>();
            clock.Configure(dayLengthSeconds: 1200.0f, startNormalizedTime: 0.25f, timeScale: 1.0f);

            worldObject = new GameObject("Headless World");
            worldObject.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            if (worldObject != null)
                Object.DestroyImmediate(worldObject);
            if (clockObject != null)
                Object.DestroyImmediate(clockObject);
        }

        CreativeWorldManager CreateHeadlessWorld(int groundHeight = 4, int height = 32)
        {
            CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 16,
                height: height,
                depth: 16,
                chunkSize: 16,
                seed: 4242,
                groundHeight: groundHeight);

            manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                registry,
                settings,
                new FlatBuilderPreset(registry, settings).Generate(),
                CreativeWorldGenerationPreset.FlatCreative));

            return manager;
        }

        [Test]
        public void WorldInitializesWithNoPresentationComponent()
        {
            CreativeWorldManager manager = CreateHeadlessWorld();

            Assert.That(manager.Presentation, Is.Null,
                "No presentation component was attached, so the manager must resolve none. A non-null " +
                "result here means the headless path is not actually being exercised.");
            Assert.That(manager.World, Is.Not.Null, "The voxel world must exist without a renderer.");
            Assert.That(manager.Registry, Is.Not.Null);
            Assert.That(manager.Settings, Is.Not.Null);
        }

        [Test]
        public void SkyOcclusionIsOwnedBySimulationNotRenderer()
        {
            CreativeWorldManager manager = CreateHeadlessWorld();

            Assert.That(manager.SkyLight, Is.Not.Null,
                "Sky occlusion is a simulation input — crop growth and cave detection read it — so it " +
                "must exist with no renderer to build it.");
        }

        [Test]
        public void SkyOcclusionTracksBlockChangesWithNoPresentation()
        {
            CreativeWorldManager manager = CreateHeadlessWorld(groundHeight: 4);
            VoxelWorld world = manager.World;

            var openCell = new BlockPosition(5, 6, 5);
            Assert.That(manager.SkyLight.HasSkyAccess(openCell), Is.True,
                "A cell above flat ground starts under open sky.");

            // With a presentation present the renderer's rebuild queue applies changes to the sky
            // map. With none, CreativeWorldManager must do it or crop growth silently reads a stale
            // map on a dedicated server.
            world.SetBlock(new BlockPosition(5, 8, 5), BlockRegistry.CutstoneBlock);

            Assert.That(manager.SkyLight.HasSkyAccess(openCell), Is.False,
                "Placing a block overhead must occlude the cell below it in the simulation-owned sky map.");
        }

        [Test]
        public void IsHeadUndergroundAnswersWithNoPresentation()
        {
            CreativeWorldManager manager = CreateHeadlessWorld(groundHeight: 4);
            manager.World.SetBlock(new BlockPosition(5, 8, 5), BlockRegistry.CutstoneBlock);

            // Previously this read Renderer.SkyLight and so returned false on any process without a
            // renderer — silently wrong rather than unavailable.
            Assert.That(manager.IsHeadUnderground(new Vector3(5.5f, 6.5f, 5.5f)), Is.True,
                "A position with a solid block overhead is underground.");
            Assert.That(manager.IsHeadUnderground(new Vector3(1.5f, 6.5f, 1.5f)), Is.False,
                "A position under open sky is not underground.");
        }

        [Test]
        public void EnvironmentServicesExistWithNoPresentation()
        {
            CreativeWorldManager manager = CreateHeadlessWorld();

            // The trap this guards: ConfigureEnvironmentServices used to return early when it could
            // not find a WorldTimeClock, leaving weather, vegetation, farming and fluid flow
            // silently absent. A headless world that never grows anything and never changes weather
            // reports no error at all.
            Assert.That(manager.WorldTimeClock, Is.Not.Null, "The simulation needs its tick source.");
            Assert.That(manager.CurrentWeatherState, Is.Not.Null,
                "Weather must be simulated headlessly; a null state means the services were skipped.");
        }

        [Test]
        public void WorldSurvivesReinitializationWithNoPresentation()
        {
            CreativeWorldManager manager = CreateHeadlessWorld(groundHeight: 4);
            VoxelSkyLightMap first = manager.SkyLight;

            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 16, height: 32, depth: 16, chunkSize: 16, seed: 777, groundHeight: 6);
            manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                registry, settings,
                new FlatBuilderPreset(registry, settings).Generate(),
                CreativeWorldGenerationPreset.FlatCreative));

            Assert.That(manager.SkyLight, Is.Not.Null);
            Assert.That(manager.SkyLight, Is.Not.SameAs(first),
                "Replacing the world must rebuild the sky map; keeping the old one would answer " +
                "occlusion queries from the previous world's geometry.");
            Assert.That(manager.Presentation, Is.Null);
        }
    }
}

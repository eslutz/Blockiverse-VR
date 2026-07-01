using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.UI;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    public sealed class CreativeWorldSwitchStateEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
                if (target != null)
                    Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
            BlockiverseRuntimeState.Reset();
        }

        [Test]
        public void CreativeInteractionConfigureClearsUndoRedoWhenWorldChanges()
        {
            CreativeInteractionController controller = CreateRoot("Creative Controller").AddComponent<CreativeInteractionController>();
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var firstWorld = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 1);
            var secondWorld = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 2);
            var pos = new BlockPosition(0, 0, 0);
            firstWorld.SetBlock(pos, BlockRegistry.Graystone);

            controller.Configure(firstWorld, registry, null, null, null);
            Assert.That(controller.TryBreakBlock(pos), Is.True);
            Assert.That(controller.UndoHistoryCount, Is.EqualTo(1));

            controller.Configure(secondWorld, registry, null, null, null);

            Assert.That(controller.UndoHistoryCount, Is.EqualTo(0));
            Assert.That(controller.RedoHistoryCount, Is.EqualTo(0));
            Assert.That(controller.CurrentTarget, Is.Null);
        }

        [Test]
        public void CreativeToolsPanelClearsRegionHistoryAndClipboardWhenWorldChanges()
        {
            CreativeInteractionController controller = CreateRoot("Creative Controller").AddComponent<CreativeInteractionController>();
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager, controller);
            BlockiverseCreativeToolsPanel panel = CreateRoot("Creative Tools Panel").AddComponent<BlockiverseCreativeToolsPanel>();
            panel.Configure(controller, manager, null, null, null, null, null, null);

            var firstWorld = CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 21);
            var target = new BlockPosition(1, 1, 1);
            firstWorld.World.SetBlock(target, BlockRegistry.Graystone);
            manager.InitializeGeneratedWorld(firstWorld);
            controller.UpdatePreview(target, Vector3.up);
            InvokePanelUpdate(panel);
            panel.SetCornerA();
            panel.SetCornerB();

            panel.CopyRegion();
            panel.DeleteRegion();

            Assert.That(panel.HasWorldEditClipboard, Is.True);
            Assert.That(panel.WorldEditUndoCount, Is.EqualTo(1));

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 22));
            InvokePanelUpdate(panel);

            Assert.That(panel.HasWorldEditClipboard, Is.False);
            Assert.That(panel.WorldEditUndoCount, Is.EqualTo(0));
        }

        [Test]
        public void InitializeDefaultWorldBakesSpawnRegionImmediately()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);

            manager.InitializeDefaultWorld();

            Assert.That(manager.World, Is.Not.Null);
            Assert.That(manager.Renderer, Is.Not.Null);
            Assert.That(manager.Renderer.SpawnRegionReady, Is.True,
                "The title mini-world must have collidable spawn geometry before normal queued chunk draining.");
            Assert.That(
                manager.gameObject.GetComponentsInChildren<MeshFilter>(includeInactive: true),
                Is.Not.Empty,
                "The title mini-world should generate visible spawn-region meshes immediately.");
        }

        [Test]
        public void CreativeTimeSlidersAreIgnoredDuringLanSession()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);
            WorldTimeClock clock = manager.gameObject.AddComponent<WorldTimeClock>();
            clock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);

            Slider timeOfDay = CreateSlider("Time Of Day", 0.0f, 1.0f);
            Slider timeScale = CreateSlider("Time Scale", 0.0f, 5.0f);
            TMP_Text status = CreateRoot("Status").AddComponent<TextMeshProUGUI>();
            BlockiverseCreativeToolsPanel panel = CreateRoot("Creative Tools Panel").AddComponent<BlockiverseCreativeToolsPanel>();
            panel.Configure(null, manager, null, null, status, null, timeOfDay, timeScale);
            panel.ConfigureNetworkSessionActiveProvider(() => true);

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 23));
            WorldTimeClock activeClock = manager.WorldTimeClock;
            Assert.That(activeClock, Is.Not.Null);
            activeClock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);
            panel.RefreshEnvironmentControls();

            timeOfDay.value = 0.75f;
            timeScale.value = 3.0f;

            Assert.That(activeClock.NormalizedTime, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(activeClock.TimeScale, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(timeOfDay.value, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(timeScale.value, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(status.text, Is.EqualTo("Time controls are host/offline only."));
        }

        [Test]
        public void CreativeToolsPanelTogglesDayNightCycleAndWeatherOffline()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);
            WorldTimeClock clock = manager.gameObject.AddComponent<WorldTimeClock>();
            clock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);

            TMP_Text status = CreateRoot("Status").AddComponent<TextMeshProUGUI>();
            TMP_Text weather = CreateRoot("Weather").AddComponent<TextMeshProUGUI>();
            Slider timeOfDay = CreateSlider("Time Of Day", 0.0f, 1.0f);
            Slider timeScale = CreateSlider("Time Scale", 0.0f, 5.0f);
            BlockiverseCreativeToolsPanel panel = CreateRoot("Creative Tools Panel").AddComponent<BlockiverseCreativeToolsPanel>();
            panel.Configure(null, manager, null, null, status, weather, timeOfDay, timeScale);
            panel.ConfigureNetworkSessionActiveProvider(() => false);

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 26));
            WorldTimeClock activeClock = manager.WorldTimeClock;
            Assert.That(activeClock, Is.Not.Null);
            activeClock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);
            panel.RefreshEnvironmentControls();

            panel.ToggleDayNightCycle();
            float frozenTime = activeClock.NormalizedTime;
            activeClock.AdvanceRuntime(60.0f);

            Assert.That(activeClock.TimeScale, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(activeClock.NormalizedTime, Is.EqualTo(frozenTime).Within(0.0001f));
            Assert.That(status.text, Is.EqualTo("Day/night cycle paused."));

            panel.ToggleDayNightCycle();

            Assert.That(activeClock.TimeScale, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(status.text, Is.EqualTo("Day/night cycle resumed."));

            WeatherState before = manager.GetWeatherSyncState().State;
            panel.CycleWeather();

            Assert.That(manager.GetWeatherSyncState().State, Is.Not.EqualTo(before));
            Assert.That(weather.text, Does.StartWith("Weather: "));
        }

        [Test]
        public void RestoreWorldTimeTicksAlsoSyncsFluidFlowAnchor()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);
            WorldTimeClock clock = manager.gameObject.AddComponent<WorldTimeClock>();
            clock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 24));
            manager.RestoreWorldTimeTicks(1234);

            FluidFlowService fluidFlowService = GetPrivateField<FluidFlowService>(manager, "fluidFlowService");

            Assert.That(manager.WorldTimeClock.TotalElapsedTicks, Is.EqualTo(1234));
            Assert.That(GetPrivateField<long>(fluidFlowService, "lastWorldTick"), Is.EqualTo(1234),
                "Restoring saved or host world time must resume fluid flow from that tick instead of catching up from zero.");
        }

        [Test]
        public void InitializeGeneratedWorldResetsInheritedWorldClock()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);
            WorldTimeClock clock = manager.gameObject.AddComponent<WorldTimeClock>();
            clock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);
            clock.RestoreElapsedTicks(9876);

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 25));
            FluidFlowService fluidFlowService = GetPrivateField<FluidFlowService>(manager, "fluidFlowService");

            Assert.That(manager.WorldTimeClock.TotalElapsedTicks, Is.EqualTo(0),
                "Fresh generated worlds must not inherit elapsed ticks from a previous world.");
            Assert.That(GetPrivateField<long>(fluidFlowService, "lastWorldTick"), Is.EqualTo(0),
                "Fresh generated worlds must also anchor fluid flow at tick zero.");
        }

        [Test]
        public void SurvivalWorldDeniesPauseModeToggleIntoCreative()
        {
            CreativeWorldManager manager = CreateWorldManagerWithEmptyWorld(WorldGameMode.Survival);
            MultiplayerSurvivalSync sync = CreateRoot("Survival Sync").AddComponent<MultiplayerSurvivalSync>();
            sync.Configure(null, null, manager);

            Assert.That(sync.CanUseCreativeMode, Is.False);
            Assert.That(sync.CanToggleMode, Is.False);
            Assert.That(sync.ToggleMode(), Is.False);
            Assert.That(sync.CurrentMode, Is.EqualTo(PlayerModeState.Survival));

            manager.SetGameMode(WorldGameMode.Creative);

            Assert.That(sync.CanUseCreativeMode, Is.True);
            Assert.That(sync.CanToggleMode, Is.True);
            Assert.That(sync.ToggleMode(), Is.True);
            Assert.That(sync.CurrentMode, Is.EqualTo(PlayerModeState.Creative));

            manager.SetGameMode(WorldGameMode.Survival);

            Assert.That(sync.CanUseCreativeMode, Is.False);
            Assert.That(sync.CanToggleMode, Is.True, "A stale creative player mode must be allowed to switch back to survival.");
            Assert.That(sync.ToggleMode(), Is.True);
            Assert.That(sync.CurrentMode, Is.EqualTo(PlayerModeState.Survival));
            Assert.That(sync.CanToggleMode, Is.False);
        }

        [Test]
        public void SurvivalWorldRejectsHostRawCreativeMutationButAllowsSurvivalCommandMutation()
        {
            CreativeWorldManager manager = CreateWorldManagerWithEmptyWorld(WorldGameMode.Survival);
            MultiplayerChunkAuthoritySync authority = CreateRoot("Chunk Authority").AddComponent<MultiplayerChunkAuthoritySync>();
            authority.Configure(null, manager);
            var position = new BlockPosition(1, 1, 1);

            BlockMutationResult rejected = authority.TrySubmitMutation(
                position,
                BlockRegistry.WorkPlank,
                out _,
                out bool requestSentToHost);

            Assert.That(requestSentToHost, Is.False);
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.GameModeForbidsDirectMutation));
            Assert.That(manager.World.GetBlock(position), Is.EqualTo(BlockRegistry.Air));

            BlockMutationResult accepted = authority.TrySubmitMutation(
                position,
                BlockRegistry.WorkPlank,
                out _,
                out requestSentToHost,
                BlockMutationSubmissionKind.SurvivalCommand);

            Assert.That(requestSentToHost, Is.False);
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(manager.World.GetBlock(position), Is.EqualTo(BlockRegistry.WorkPlank));
        }

        GameObject CreateRoot(string name)
        {
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target;
        }

        CreativeWorldManager CreateWorldManagerWithEmptyWorld(WorldGameMode mode)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 4,
                height: 4,
                depth: 4,
                chunkSize: 2,
                seed: 37,
                groundHeight: 1,
                spawnPosition: new BlockPosition(1, 1, 1));
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);
            manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
            manager.SetGameMode(mode);
            return manager;
        }

        static void ConfigureWorldManager(
            CreativeWorldManager manager,
            CreativeInteractionController controller = null)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.ChunkAtlasMaterialPath);
            Assert.That(material, Is.Not.Null, "Creative world tests should use the committed authored chunk material.");
            manager.Configure(material, layer: -1, controller: controller);
        }

        Slider CreateSlider(string name, float min, float max)
        {
            Slider slider = CreateRoot(name).AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            return slider;
        }

        static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName} should exist.");
            return (T)field.GetValue(target);
        }

        static void InvokePanelUpdate(BlockiverseCreativeToolsPanel panel)
        {
            MethodInfo method = typeof(BlockiverseCreativeToolsPanel).GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Creative tools panel should expose the expected Unity update callback.");
            method.Invoke(panel, null);
        }
    }
}

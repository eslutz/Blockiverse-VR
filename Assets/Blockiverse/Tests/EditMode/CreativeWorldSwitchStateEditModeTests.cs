using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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

            BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);
            controller.Configure(firstWorld, registry, null, null, null);
            Assert.That(controller.TryBreakBlock(pos), Is.True);
            Assert.That(controller.UndoHistoryCount, Is.EqualTo(1));

            controller.Configure(secondWorld, registry, null, null, null);

            Assert.That(controller.UndoHistoryCount, Is.EqualTo(0));
            Assert.That(controller.RedoHistoryCount, Is.EqualTo(0));
            Assert.That(controller.CurrentTarget, Is.Null);
        }

        [Test]
        public void InitializeDefaultWorldBakesSpawnRegionImmediately()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);

            manager.InitializeDefaultWorld();

            Assert.That(manager.World, Is.Not.Null);
            Assert.That(manager.Presentation, Is.Not.Null);
            Assert.That(manager.Presentation.SpawnRegionReady, Is.True,
                "The title mini-world must have collidable spawn geometry before normal queued chunk draining.");
            Assert.That(
                manager.gameObject.GetComponentsInChildren<MeshFilter>(includeInactive: true),
                Is.Not.Empty,
                "The title mini-world should generate visible spawn-region meshes immediately.");
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
            BlockiverseWorldPresentation.Attach(manager, material, layer: -1, controller: controller);
        }

        static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName} should exist.");
            return (T)field.GetValue(target);
        }
    }
}

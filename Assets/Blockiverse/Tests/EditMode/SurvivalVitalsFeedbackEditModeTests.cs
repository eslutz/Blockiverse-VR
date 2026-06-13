using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Blockiverse.VR;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SurvivalVitalsFeedbackEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
            BlockiverseRuntimeState.Reset();
        }

        [Test]
        public void RuntimeRaisesDamageLowHealthAndDeathFeedbackEvents()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            int damageEvents = 0;
            int lowHealthEvents = 0;
            int deathEvents = 0;

            runtime.LocalPlayerDamaged += result =>
            {
                Assert.That(result.Kind, Is.EqualTo(HealthChangeKind.Damage));
                damageEvents++;
            };
            runtime.LocalPlayerLowHealth += result =>
            {
                Assert.That(result.CurrentHealth, Is.LessThanOrEqualTo(result.MaxHealth / 4));
                lowHealthEvents++;
            };
            runtime.LocalPlayerDied += () => deathEvents++;

            runtime.Vitals.ApplyDamage(20);
            runtime.Vitals.ApplyDamage(55);
            runtime.Vitals.ApplyDamage(25);

            Assert.That(damageEvents, Is.EqualTo(2), "Fatal damage should use the death channel, not a normal hurt cue.");
            Assert.That(lowHealthEvents, Is.EqualTo(1));
            Assert.That(deathEvents, Is.EqualTo(1));
        }

        [Test]
        public void FeedbackBridgePlaysVitalsAudioCues()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            SurvivalFeedbackBridge bridge = CreateBridge();
            var playedCues = new List<BlockiverseAudioCue>();

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.PlayerHurt, CreateClip("player_hurt"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.LowHealth, CreateClip("low_health"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.PlayerDeath, CreateClip("player_death"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            bridge.ConfigureVitalsFeedback(runtime, audioCuePlayer);

            runtime.Vitals.ApplyDamage(20);
            runtime.Vitals.ApplyDamage(55);
            runtime.Vitals.ApplyDamage(25);

            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.PlayerHurt,
                BlockiverseAudioCue.PlayerHurt,
                BlockiverseAudioCue.LowHealth,
                BlockiverseAudioCue.PlayerDeath
            }));
        }

        [Test]
        public void FeedbackBridgePlaysRejectCueForInventoryFullHarvest()
        {
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            SurvivalFeedbackBridge bridge = CreateBridge();
            BlockiverseSubtitleToastPanel toastPanel = CreateToastPanel(out TMP_Text toastLabel);
            var playedCues = new List<BlockiverseAudioCue>();

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.ToolWrong, CreateClip("tool_wrong"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            SetPrivateField(bridge, "audioCuePlayer", audioCuePlayer);
            bridge.ConfigureToastPanel(toastPanel);

            SurvivalCommandResult result = SurvivalCommandResult.Reject(
                SurvivalCommandKind.HarvestResource,
                SurvivalCommandFailureReason.HarvestRejected,
                harvestFailureReason: BlockHarvestFailureReason.InventoryFull);

            InvokePrivate(bridge, "OnCommandFeedback", result, new BlockPosition(1, 2, 3));

            Assert.That(playedCues, Is.EqualTo(new[] { BlockiverseAudioCue.ToolWrong }));
            Assert.That(toastPanel.IsVisible, Is.True);
            Assert.That(toastLabel.text, Is.EqualTo("Inventory full."));
        }

        [Test]
        public void InteractionHapticsRequestsVitalsPatterns()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var requestedPatterns = new List<BlockiverseHapticPattern>();

            haptics.PatternRequested += requestedPatterns.Add;
            haptics.ConfigureVitalsRuntime(runtime);

            runtime.Vitals.ApplyDamage(20);
            runtime.Vitals.ApplyDamage(55);
            runtime.Vitals.ApplyDamage(25);

            Assert.That(requestedPatterns, Has.Count.EqualTo(4));
            Assert.That(requestedPatterns[0].Amplitude, Is.EqualTo(BlockiverseHapticPattern.PlayerDamage.Amplitude));
            Assert.That(requestedPatterns[2].Amplitude, Is.EqualTo(BlockiverseHapticPattern.LowHealth.Amplitude));
            Assert.That(requestedPatterns[3].Amplitude, Is.EqualTo(BlockiverseHapticPattern.PlayerDeath.Amplitude));
        }

        [Test]
        public void RuntimeMapsConfiguredDifficultyIdsToProfiles()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();

            runtime.ConfigureDifficulty("hard");
            Assert.That(runtime.Difficulty, Is.EqualTo(SurvivalDifficultyProfile.Hard));

            runtime.ConfigureDifficulty("unknown");
            Assert.That(runtime.Difficulty, Is.EqualTo(SurvivalDifficultyProfile.Normal));
        }

        [Test]
        public void RuntimeReportsBedrollSpawnOnlyWhenAUsableBedrollExists()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            CreativeWorldManager worldManager = CreateWorldManagerWithFlatWorld();
            worldManager.World.SetBlock(new BlockPosition(2, 1, 2), BlockRegistry.Bedroll, trackChange: false);
            runtime.Configure(null, worldManager);

            Assert.That(runtime.HasBedrollSpawn, Is.True);

            worldManager.World.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.WorkPlank, trackChange: false);

            Assert.That(runtime.HasBedrollSpawn, Is.False,
                "The bedroll option must not show when the spawn cell above it is blocked.");
        }

        [Test]
        public void RuntimeTicksStarvationAndHazardsOnlyInSurvivalMode()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            CreativeWorldManager worldManager = CreateWorldManagerWithFlatWorld();
            MultiplayerSurvivalSync survivalSync = CreateSurvivalSync(worldManager);
            runtime.Configure(survivalSync, worldManager);

            runtime.SurvivalVitals.Restore(hunger: 0, thirst: 0, stamina: 100);
            InvokePrivate(runtime, "OnWorldTick", SurvivalVitals.StarvationDamageIntervalTicks);
            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(100 - SurvivalVitals.StarvationDamagePerInterval * 2));

            var headObject = new GameObject("Hazard Head");
            objectsToDestroy.Add(headObject);
            headObject.transform.position = new Vector3(2.5f, 2.5f, 2.5f);
            worldManager.World.SetBlock(new BlockPosition(2, 1, 2), BlockRegistry.Thornbrush, trackChange: false);
            SetPrivateField(runtime, "cachedHeadTransform", headObject.transform);

            InvokePrivate(runtime, "TickHazards");
            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(95));

            InvokePrivate(runtime, "TickHazards");
            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(95),
                "The scan interval/hazard cadence should prevent immediate double damage.");

            survivalSync.SetMode(PlayerModeState.Creative);
            runtime.Vitals.RestoreHealth(runtime.Vitals.MaxHealth);
            runtime.SurvivalVitals.Restore(hunger: 0, thirst: 0, stamina: 100);

            InvokePrivate(runtime, "OnWorldTick", SurvivalVitals.StarvationDamageIntervalTicks);
            InvokePrivate(runtime, "TickHazards");

            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(runtime.Vitals.MaxHealth));
        }

        [Test]
        public void RuntimeAppliesFallAndEnvironmentPressureOnlyInSurvivalMode()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            CreativeWorldManager worldManager = CreateWorldManagerWithFlatWorld();
            MultiplayerSurvivalSync survivalSync = CreateSurvivalSync(worldManager);
            runtime.Configure(survivalSync, worldManager);

            int fallDamage = runtime.ApplyFallImpact(SurvivalVitals.FallSafeDistanceMeters + 2.0f);
            Assert.That(fallDamage, Is.EqualTo(12));
            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(88));

            var blizzardExposure = new SurvivalEnvironmentExposure(
                temperatureC: -8.0f,
                skyExposed: true,
                isNight: true,
                precipitationIntensity: 1.0f,
                stormIntensity: 0.8f);
            int environmentDamage = runtime.ApplyEnvironmentExposure(
                SurvivalVitals.EnvironmentExposureDamageIntervalTicks,
                blizzardExposure);

            Assert.That(environmentDamage, Is.EqualTo(SurvivalVitals.EnvironmentExposureDamagePerInterval * 3));
            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(85));

            survivalSync.SetMode(PlayerModeState.Creative);

            Assert.That(runtime.ApplyFallImpact(20.0f), Is.EqualTo(0));
            Assert.That(
                runtime.ApplyEnvironmentExposure(SurvivalVitals.EnvironmentExposureDamageIntervalTicks, blizzardExposure),
                Is.EqualTo(0));
            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(85));
        }

        [Test]
        public void RuntimeWorldDrinkAndPlayerSaveStateRoundTrip()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            CreativeWorldManager worldManager = CreateWorldManagerWithFlatWorld();
            MultiplayerSurvivalSync survivalSync = CreateSurvivalSync(worldManager);
            runtime.Configure(survivalSync, worldManager);

            runtime.SurvivalVitals.Restore(hunger: 80, thirst: 10, stamina: 70);
            Assert.That(runtime.TryDrinkFromWorldSource(), Is.True);
            Assert.That(runtime.SurvivalVitals.Thirst, Is.EqualTo(10 + SurvivalVitalsRuntime.WorldDrinkThirstRestore));
            Assert.That(runtime.TryDrinkFromWorldSource(), Is.False);

            Assert.That(runtime.BuildPlayerSaveState(), Is.Null, "Headless contexts should not emit player presence.");

            GameObject rig = CreateRig();
            rig.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 45f, 0f));
            runtime.Vitals.ApplyDamage(25);
            runtime.SurvivalVitals.Restore(hunger: 44, thirst: 33, stamina: 22);

            SavedPlayerState saved = runtime.BuildPlayerSaveState();

            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.PositionX, Is.EqualTo(3f));
            Assert.That(saved.PositionY, Is.EqualTo(4f));
            Assert.That(saved.PositionZ, Is.EqualTo(5f));
            Assert.That(saved.YawDegrees, Is.EqualTo(45f).Within(0.001f));
            Assert.That(saved.Health, Is.EqualTo(75));
            Assert.That(saved.Hunger, Is.EqualTo(44));
            Assert.That(saved.Thirst, Is.EqualTo(33));
            Assert.That(saved.Stamina, Is.EqualTo(22));

            runtime.RestorePlayerSaveState(null);
            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(runtime.Vitals.MaxHealth));
            Assert.That(runtime.SurvivalVitals.Hunger, Is.EqualTo(runtime.SurvivalVitals.Max));
            Assert.That(runtime.SurvivalVitals.Thirst, Is.EqualTo(runtime.SurvivalVitals.Max));
            Assert.That(runtime.SurvivalVitals.Stamina, Is.EqualTo(runtime.SurvivalVitals.Max));

            runtime.RestorePlayerSaveState(saved);

            Assert.That(rig.transform.position, Is.EqualTo(new Vector3(3f, 4f, 5f)));
            Assert.That(rig.transform.eulerAngles.y, Is.EqualTo(45f).Within(0.001f));
            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(75));
            Assert.That(runtime.SurvivalVitals.Hunger, Is.EqualTo(44));
            Assert.That(runtime.SurvivalVitals.Thirst, Is.EqualTo(33));
            Assert.That(runtime.SurvivalVitals.Stamina, Is.EqualTo(22));
        }

        [Test]
        public void RespawnedPlayerSaveStateSerializesSpawnAndFullVitalsWithoutMutatingDeath()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            CreativeWorldManager worldManager = CreateWorldManagerWithFlatWorld();
            MultiplayerSurvivalSync survivalSync = CreateSurvivalSync(worldManager);
            runtime.Configure(survivalSync, worldManager);
            GameObject rig = CreateRig();
            rig.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 45f, 0f));
            runtime.SurvivalVitals.Restore(hunger: 44, thirst: 33, stamina: 22);
            runtime.Vitals.ApplyDamage(runtime.Vitals.MaxHealth);

            SavedPlayerState saved = runtime.BuildRespawnedPlayerSaveState();

            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.PositionX, Is.EqualTo(1.5f));
            Assert.That(saved.PositionY, Is.EqualTo(2f));
            Assert.That(saved.PositionZ, Is.EqualTo(1.5f));
            Assert.That(saved.YawDegrees, Is.EqualTo(45f).Within(0.001f));
            Assert.That(saved.Health, Is.EqualTo(runtime.Vitals.MaxHealth));
            Assert.That(saved.Hunger, Is.EqualTo(runtime.SurvivalVitals.Max));
            Assert.That(saved.Thirst, Is.EqualTo(runtime.SurvivalVitals.Max));
            Assert.That(saved.Stamina, Is.EqualTo(runtime.SurvivalVitals.Max));
            Assert.That(runtime.Vitals.IsDead, Is.True, "Building a respawn-safe save snapshot must not mutate live death state.");
            Assert.That(runtime.SurvivalVitals.Hunger, Is.EqualTo(44));
        }

        [Test]
        public void RuntimeSpendsStaminaOnAcceptedHarvestFeedbackOnly()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            CreativeWorldManager worldManager = CreateWorldManagerWithFlatWorld();
            MultiplayerSurvivalSync survivalSync = CreateSurvivalSync(worldManager);
            runtime.Configure(survivalSync, worldManager);

            runtime.SurvivalVitals.Restore(hunger: 100, thirst: 100, stamina: 10);

            InvokePrivate(
                survivalSync,
                "RaiseCommandFeedback",
                SurvivalCommandResult.Accept(SurvivalCommandKind.HarvestResource, requestId: 0),
                new BlockPosition(1, 2, 3));

            Assert.That(runtime.SurvivalVitals.Stamina, Is.EqualTo(10 - SurvivalVitalsRuntime.HarvestStaminaCost));

            InvokePrivate(
                survivalSync,
                "RaiseCommandFeedback",
                SurvivalCommandResult.Reject(
                    SurvivalCommandKind.HarvestResource,
                    SurvivalCommandFailureReason.HarvestRejected),
                new BlockPosition(1, 2, 3));

            Assert.That(runtime.SurvivalVitals.Stamina, Is.EqualTo(10 - SurvivalVitalsRuntime.HarvestStaminaCost));

            survivalSync.SetMode(PlayerModeState.Creative);
            InvokePrivate(
                survivalSync,
                "RaiseCommandFeedback",
                SurvivalCommandResult.Accept(SurvivalCommandKind.HarvestResource, requestId: 0),
                new BlockPosition(1, 2, 3));

            Assert.That(runtime.SurvivalVitals.Stamina, Is.EqualTo(10 - SurvivalVitalsRuntime.HarvestStaminaCost));
        }

        [Test]
        public void RuntimeRespawnsAtWorldSpawnAndResetsVitals()
        {
            SurvivalVitalsRuntime runtime = CreateVitalsRuntime();
            CreativeWorldManager worldManager = CreateWorldManagerWithFlatWorld();
            MultiplayerSurvivalSync survivalSync = CreateSurvivalSync(worldManager);
            runtime.Configure(survivalSync, worldManager);
            GameObject rig = CreateRig();

            runtime.Vitals.ApplyDamage(runtime.Vitals.MaxHealth);
            runtime.SurvivalVitals.Restore(hunger: 1, thirst: 2, stamina: 3);
            rig.transform.position = Vector3.zero;

            runtime.Respawn();

            Assert.That(runtime.Vitals.CurrentHealth, Is.EqualTo(runtime.Vitals.MaxHealth));
            Assert.That(runtime.Vitals.IsDead, Is.False);
            Assert.That(runtime.SurvivalVitals.Hunger, Is.EqualTo(runtime.SurvivalVitals.Max));
            Assert.That(runtime.SurvivalVitals.Thirst, Is.EqualTo(runtime.SurvivalVitals.Max));
            Assert.That(runtime.SurvivalVitals.Stamina, Is.EqualTo(runtime.SurvivalVitals.Max));
            Assert.That(rig.transform.position, Is.EqualTo(new Vector3(1.5f, 2f, 1.5f)));
        }

        [Test]
        public void DeathDropsInventoryIntoPersistedStorageCrate()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CreativeWorldManager worldManager = CreateWorldManagerWithFlatWorld();
            var authorityObject = new GameObject("Chunk Authority");
            objectsToDestroy.Add(authorityObject);
            MultiplayerChunkAuthoritySync authority = authorityObject.AddComponent<MultiplayerChunkAuthoritySync>();
            authority.Configure(null, worldManager);
            worldManager.ConfigureAuthoritySync(authority);

            var syncObject = new GameObject("Survival Sync");
            objectsToDestroy.Add(syncObject);
            MultiplayerSurvivalSync survivalSync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            survivalSync.Configure(null, authority, worldManager, itemRegistry, CraftingRecipeBook.CreateDefault(itemRegistry));
            survivalSync.LocalInventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 3));
            survivalSync.LocalInventory.SetSlot(1, new ItemStack(ItemId.ReedwoodFeller, 1).WithDurability(12));

            var dropPosition = new BlockPosition(2, 2, 2);
            SurvivalCommandResult result = survivalSync.TrySubmitDeathDrop(dropPosition, out bool requestSentToHost);

            Assert.That(requestSentToHost, Is.False);
            Assert.That(result.Accepted, Is.True, result.FailureReason.ToString());
            Assert.That(worldManager.World.GetBlock(dropPosition), Is.EqualTo(BlockRegistry.StorageCrate));
            Assert.That(survivalSync.LocalInventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(survivalSync.LocalInventory.GetSlot(1).IsEmpty, Is.True);
            Assert.That(worldManager.ContainerStore.TryGet(dropPosition, out Inventory deathCache), Is.True);
            Assert.That(deathCache.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 3)));
            Assert.That(deathCache.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.ReedwoodFeller, 1).WithDurability(12)));
        }

        SurvivalVitalsRuntime CreateVitalsRuntime()
        {
            var gameObject = new GameObject("Survival Vitals Runtime");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<SurvivalVitalsRuntime>();
        }

        MultiplayerSurvivalSync CreateSurvivalSync(CreativeWorldManager worldManager)
        {
            var gameObject = new GameObject("Survival Sync");
            objectsToDestroy.Add(gameObject);
            MultiplayerSurvivalSync sync = gameObject.AddComponent<MultiplayerSurvivalSync>();
            sync.Configure(null, null, worldManager);
            return sync;
        }

        GameObject CreateRig()
        {
            var rig = new GameObject(BlockiverseProject.XrRigRootName);
            rig.AddComponent<BlockiversePlayerRigAnchor>();
            objectsToDestroy.Add(rig);
            return rig;
        }

        CreativeWorldManager CreateWorldManagerWithFlatWorld()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(6, 4, 6, chunkSize: 2, seed: 59, groundHeight: 1, spawnPosition: new BlockPosition(1, 2, 1));
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
            for (int z = 0; z < settings.Bounds.Depth; z++)
            for (int x = 0; x < settings.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.MeadowTurf, trackChange: false);

            var gameObject = new GameObject("Creative World Manager");
            objectsToDestroy.Add(gameObject);
            CreativeWorldManager manager = gameObject.AddComponent<CreativeWorldManager>();
            manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
            manager.SetGameMode(WorldGameMode.Survival);
            return manager;
        }

        BlockiverseAudioCuePlayer CreateCuePlayer()
        {
            var gameObject = new GameObject("Audio Cue Player");
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<AudioSource>();
            return gameObject.AddComponent<BlockiverseAudioCuePlayer>();
        }

        SurvivalFeedbackBridge CreateBridge()
        {
            var gameObject = new GameObject("Survival Feedback Bridge");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<SurvivalFeedbackBridge>();
        }

        BlockiverseSubtitleToastPanel CreateToastPanel(out TMP_Text label)
        {
            var gameObject = new GameObject("Subtitle Toast Panel");
            objectsToDestroy.Add(gameObject);
            label = new GameObject("Toast Label").AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(gameObject.transform, worldPositionStays: false);
            var panel = gameObject.AddComponent<BlockiverseSubtitleToastPanel>();
            panel.Configure(label);
            return panel;
        }

        BlockiverseInteractionHaptics CreateHaptics()
        {
            var gameObject = new GameObject("Interaction Haptics");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<BlockiverseInteractionHaptics>();
        }

        static AudioClip CreateClip(string name)
        {
            return AudioClip.Create(name, 16, 1, 44100, false);
        }

        static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName} should exist.");
            field.SetValue(target, value);
        }

        static void InvokePrivate(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{methodName} should exist.");
            method.Invoke(target, args);
        }
    }
}

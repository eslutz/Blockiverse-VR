using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Persistence;
using Blockiverse.UI;
using Blockiverse.Voxel;
using Blockiverse.VR;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    public sealed class WorldSessionControllerEditModeTests
    {
        GameObject worldObject;
        GameObject sessionObject;
        GameObject menuObject;
        string tempRoot;

        [TearDown]
        public void TearDown()
        {
            if (sessionObject != null)
                Object.DestroyImmediate(sessionObject);
            if (menuObject != null)
                Object.DestroyImmediate(menuObject);
            if (worldObject != null)
                Object.DestroyImmediate(worldObject);
            GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
            if (sunObject != null)
                Object.DestroyImmediate(sunObject);
            if (!string.IsNullOrEmpty(tempRoot) && Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
            PlayerPrefs.DeleteKey(BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey);
            SetStaticField<string>("savesRoot", null);
        }

        [Test]
        public void AutosaveGateHelpersUseSharedSaveServiceCadence()
        {
            float beforeInterval = WorldSaveService.AutoSaveIntervalSeconds - 0.1f;
            float atInterval = WorldSaveService.AutoSaveIntervalSeconds;

            Assert.That(BlockiverseWorldSessionController.ShouldStartAutoSave(false, atInterval, 0f), Is.False);
            Assert.That(BlockiverseWorldSessionController.ShouldStartAutoSave(true, beforeInterval, 0f), Is.False);
            Assert.That(BlockiverseWorldSessionController.ShouldStartAutoSave(true, atInterval, 0f), Is.True);

            Assert.That(MultiplayerWorldPersistence.ShouldStartHostAutoSave(false, true, atInterval, 0f), Is.False);
            Assert.That(MultiplayerWorldPersistence.ShouldStartHostAutoSave(true, false, atInterval, 0f), Is.False);
            Assert.That(MultiplayerWorldPersistence.ShouldStartHostAutoSave(true, true, beforeInterval, 0f), Is.False);
            Assert.That(MultiplayerWorldPersistence.ShouldStartHostAutoSave(true, true, atInterval, 0f), Is.True);
        }

        [Test]
        public void SuspendActiveSessionForMultiplayerSavesAndClearsSinglePlayerAutosaveTarget()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-session-controller-" + System.Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(tempRoot, "Single Player.vxlworld");

            CreativeWorldManager worldManager = CreateWorldManager();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager);
            SetPrivateField(controller, "currentSavePath", savePath);
            SetPrivateField(controller, "currentWorldName", "Single Player");

            Assert.That(controller.HasActiveSession, Is.True);

            bool suspended = controller.TrySuspendActiveSessionForMultiplayer(out string failureReason);

            Assert.That(suspended, Is.True, failureReason);
            Assert.That(controller.CurrentSavePath, Is.Null);
            Assert.That(controller.HasActiveSession, Is.False);
            Assert.That(Directory.Exists(savePath), Is.True);
        }

        [Test]
        public void ApplicationPauseSavesActiveSinglePlayerSession()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-session-controller-" + System.Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(tempRoot, "Paused Single Player.vxlworld");

            CreativeWorldManager worldManager = CreateWorldManager();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager);
            SetPrivateField(controller, "currentSavePath", savePath);
            SetPrivateField(controller, "currentWorldName", "Paused Single Player");
            var editedPosition = new BlockPosition(2, 2, 2);
            worldManager.World.SetBlock(editedPosition, BlockRegistry.LumenQuartzCluster);

            InvokeUnityMessage(controller, "OnApplicationPause", true);

            WorldLoadResult result = new WorldSaveService().Load(savePath);
            Assert.That(result.Success, Is.True, result.Error);
            VoxelWorld loaded = new VoxelWorld(worldManager.World.Bounds, worldManager.World.ChunkSize, worldManager.World.Seed);
            result.ApplyTo(loaded);
            Assert.That(loaded.GetBlock(editedPosition), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
        }

        [Test]
        public void PauseSaveGameReportsSaveStatusOnPauseMenu()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-session-controller-" + System.Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(tempRoot, "Pause Save.vxlworld");

            CreativeWorldManager worldManager = CreateWorldManager();
            BlockiverseMenuController menuController = CreateMenuController();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager, menuController);
            SetPrivateField(controller, "currentSavePath", savePath);
            SetPrivateField(controller, "currentWorldName", "Pause Save");

            InvokePrivateMethod(controller, "HandleAction", MenuActions.PauseSaveGame);

            Assert.That(GetMenuControllerPrivateField<string>(menuController, "pauseStatus"), Is.EqualTo("Game saved."));
            Assert.That(Directory.Exists(savePath), Is.True);
        }

        [Test]
        public void AutoSaveCompletionReportsStatusOnPauseMenu()
        {
            CreativeWorldManager worldManager = CreateWorldManager();
            BlockiverseMenuController menuController = CreateMenuController();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager, menuController);

            SetPrivateField(controller, "autoSaveTask", Task.CompletedTask);
            SetPrivateField(controller, "autoSaveWorldName", "Autosave Success");
            InvokePrivateMethod(controller, "CompleteAutoSaveIfReady");
            Assert.That(GetMenuControllerPrivateField<string>(menuController, "pauseStatus"), Is.EqualTo("Autosaved."));

            SetPrivateField(controller, "autoSaveTask", Task.FromException(new IOException("autosave failed")));
            SetPrivateField(controller, "autoSaveWorldName", "Autosave Failure");
            LogAssert.Expect(LogType.Error, "[Blockiverse][Persistence] Failed to autosave world session name=Autosave Failure exception=IOException");
            InvokePrivateMethod(controller, "CompleteAutoSaveIfReady");
            Assert.That(GetMenuControllerPrivateField<string>(menuController, "pauseStatus"), Is.EqualTo("Autosave failed."));
        }

        [Test]
        public void SuspendActiveSessionForMultiplayerKeepsTargetWhenCurrentWorldCannotBeSaved()
        {
            const string savePath = "/tmp/blockiverse-unsaved-session.vxlworld";
            sessionObject = new GameObject("Session Controller");
            BlockiverseWorldSessionController controller = sessionObject.AddComponent<BlockiverseWorldSessionController>();
            SetPrivateField(controller, "currentSavePath", savePath);
            SetPrivateField(controller, "currentWorldName", "Unsaved Session");

            bool suspended = controller.TrySuspendActiveSessionForMultiplayer(out string failureReason);

            Assert.That(suspended, Is.False);
            Assert.That(failureReason, Is.Not.Empty);
            Assert.That(controller.CurrentSavePath, Is.EqualTo(savePath));
            Assert.That(controller.HasActiveSession, Is.True);
        }

        [Test]
        public void LoadSaveRestoresSavedWorldAndEntersActiveSession()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-session-controller-" + System.Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(tempRoot, "Loaded World.vxlworld");
            var editedPosition = new BlockPosition(4, 10, 4);

            VoxelWorld savedWorld = CreateFlatBuilderWorld(seed: 4401);
            savedWorld.SetBlock(editedPosition, BlockRegistry.LumenQuartzCluster);
            new WorldSaveService().Save(
                savePath,
                "Loaded World",
                savedWorld,
                gameMode: "creative",
                worldPreset: "flat_builder",
                textureSet: "ai_simplified");

            CreativeWorldManager worldManager = CreateWorldManager();
            BlockiverseMenuController menuController = CreateMenuController();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager, menuController);

            bool loaded = controller.LoadSave(savePath);

            Assert.That(loaded, Is.True);
            Assert.That(controller.HasActiveSession, Is.True);
            Assert.That(controller.CurrentSavePath, Is.EqualTo(savePath));
            Assert.That(worldManager.World.GetBlock(editedPosition), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
            Assert.That(worldManager.GameMode, Is.EqualTo(WorldGameMode.Creative));
            Assert.That(worldManager.TextureSet, Is.EqualTo("ai_simplified"));
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(worldManager.IsMenuWorldActive, Is.False);
            Assert.That(worldManager.GetComponent<CreativeInteractionController>().BlockEditingEnabled, Is.True);
        }

        [Test]
        public void StartInitializesReadOnlyMenuWorldBeforeAnySession()
        {
            CreativeWorldManager worldManager = CreateWorldManager();
            CreativeInteractionController interactionController = worldManager.GetComponent<CreativeInteractionController>();
            BlockiverseMenuController menuController = CreateMenuController();
            BlockiverseHudToolkitSurface hudSurface = new GameObject("Gameplay HUD").AddComponent<BlockiverseHudToolkitSurface>();
            hudSurface.transform.SetParent(menuObject.transform, worldPositionStays: false);
            hudSurface.Configure(hudSurface.gameObject.AddComponent<UIDocument>());
            hudSurface.SetVisible(true);
            menuController.ConfigureHudToolkitSurface(hudSurface);
            menuController.ShowTitleScreen();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager, menuController);

            InvokeUnityMessage(controller, "Start");

            Assert.That(worldManager.IsMenuWorldActive, Is.True);
            Assert.That(worldManager.GenerationPreset, Is.EqualTo(CreativeWorldGenerationPreset.MenuWorld));
            Assert.That(worldManager.GameMode, Is.EqualTo(WorldGameMode.Creative));
            Assert.That(worldManager.World.Seed, Is.EqualTo(WorldSaveGeneration.MenuWorldSeed));
            Assert.That(controller.HasActiveSession, Is.False);
            Assert.That(interactionController.BlockEditingEnabled, Is.False);
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
            Assert.That(hudSurface.IsVisible, Is.False);
        }

        [Test]
        public void ReturnToTitleSavesSessionAndRestoresReadOnlyMenuWorld()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-session-controller-" + System.Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(tempRoot, "Return To Title.vxlworld");

            CreativeWorldManager worldManager = CreateWorldManager();
            CreativeInteractionController interactionController = worldManager.GetComponent<CreativeInteractionController>();
            BlockiverseMenuController menuController = CreateMenuController();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager, menuController);
            SetPrivateField(controller, "currentSavePath", savePath);
            SetPrivateField(controller, "currentWorldName", "Return To Title");
            interactionController.SetBlockEditingEnabled(true);

            InvokePrivateMethod(controller, "HandleAction", MenuActions.PauseReturnToTitle);

            Assert.That(Directory.Exists(savePath), Is.True);
            Assert.That(controller.HasActiveSession, Is.False);
            Assert.That(worldManager.IsMenuWorldActive, Is.True);
            Assert.That(worldManager.World.Seed, Is.EqualTo(WorldSaveGeneration.MenuWorldSeed));
            Assert.That(interactionController.BlockEditingEnabled, Is.False);
        }

        [Test]
        public void ContinueLatestSaveLoadsMostRecentlyModifiedSlot()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-session-controller-" + System.Guid.NewGuid().ToString("N"));
            SetStaticField("savesRoot", tempRoot);
            string oldPath = Path.Combine(tempRoot, "Old World.vxlworld");
            string latestPath = Path.Combine(tempRoot, "Latest World.vxlworld");
            var editedPosition = new BlockPosition(5, 10, 5);

            VoxelWorld oldWorld = CreateFlatBuilderWorld(seed: 5101);
            oldWorld.SetBlock(editedPosition, BlockRegistry.Graystone);
            VoxelWorld latestWorld = CreateFlatBuilderWorld(seed: 5102);
            latestWorld.SetBlock(editedPosition, BlockRegistry.Glowwick);

            new WorldSaveService().Save(oldPath, "Old World", oldWorld, gameMode: "creative", worldPreset: "flat_builder");
            new WorldSaveService().Save(latestPath, "Latest World", latestWorld, gameMode: "creative", worldPreset: "flat_builder");
            WriteManifestModifiedAt(oldPath, System.DateTime.UtcNow.AddDays(-1));
            WriteManifestModifiedAt(latestPath, System.DateTime.UtcNow.AddDays(1));

            CreativeWorldManager worldManager = CreateWorldManager();
            BlockiverseMenuController menuController = CreateMenuController();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager, menuController);

            InvokePrivateMethod(controller, "ContinueLatestSave");

            Assert.That(controller.HasActiveSession, Is.True);
            Assert.That(controller.CurrentSavePath, Is.EqualTo(latestPath));
            Assert.That(worldManager.World.GetBlock(editedPosition), Is.EqualTo(BlockRegistry.Glowwick));
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
        }

        [Test]
        public void DetailsSaveResolutionUsesSummaryKeyWhenDisplayNamesCollide()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-save-details-" + System.Guid.NewGuid().ToString("N"));
            SetStaticField("savesRoot", tempRoot);
            string firstPath = Path.Combine(tempRoot, "A Shared Name.vxlworld");
            string secondPath = Path.Combine(tempRoot, "B Shared Name.vxlworld");

            new WorldSaveService().Save(firstPath, "Shared Name", CreateFlatBuilderWorld(seed: 6101), gameMode: "creative", worldPreset: "flat_builder");
            new WorldSaveService().Save(secondPath, "Shared Name", CreateFlatBuilderWorld(seed: 6102), gameMode: "creative", worldPreset: "flat_builder");

            CreativeWorldManager worldManager = CreateWorldManager();
            BlockiverseMenuController menuController = CreateMenuController();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager, menuController);
            controller.RefreshSaveList();
            WorldSaveService.WorldSaveInfo secondInfo = FindSaveInfo(secondPath);
            var selected = new WorldSaveSummary(
                secondInfo.Manifest.WorldName,
                secondInfo.Manifest.Seed.ToString(),
                secondInfo.Manifest.GameMode,
                secondInfo.Manifest.Difficulty,
                dayCount: 1,
                lastPlayedUtc: System.DateTime.Parse(secondInfo.Manifest.ModifiedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
                createdUtc: System.DateTime.Parse(secondInfo.Manifest.CreatedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind));

            object[] args = { selected, null };
            MethodInfo resolver = typeof(BlockiverseWorldSessionController).GetMethod(
                "TryResolveSavePath",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(resolver, Is.Not.Null);

            bool resolved = (bool)resolver.Invoke(controller, args);

            Assert.That(resolved, Is.True);
            Assert.That(args[1], Is.EqualTo(secondPath));
        }

        [Test]
        public void LoadSaveFailureOnLoadWorldScreenUsesVisiblePanelStatus()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-session-controller-" + System.Guid.NewGuid().ToString("N"));
            SetStaticField("savesRoot", tempRoot);
            string missingPath = Path.Combine(tempRoot, "Missing World.vxlworld");

            CreativeWorldManager worldManager = CreateWorldManager();
            BlockiverseMenuController menuController = CreateMenuController();
            BlockiverseWorldSessionController controller = CreateSessionController(worldManager, menuController);
            menuController.Router.PushScreen(new ScreenRoute(MenuActions.LoadWorldScreen, pauseGame: true));

            bool loaded = controller.LoadSave(missingPath);

            Assert.That(loaded, Is.False);
            Assert.That(GetMenuControllerPrivateField<string>(menuController, "uiToolkitLoadWorldStatus"), Does.Contain("Failed to load:"));
        }

        [Test]
        public void WorldSaveSlotServiceSanitizesDirectoriesButPreservesDisplayNames()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "blockiverse-save-slots-" + System.Guid.NewGuid().ToString("N"));

            (string firstPath, string firstName) = WorldSaveSlotService.AllocateSavePath(tempRoot, "Bad/Name");
            Directory.CreateDirectory(firstPath);
            (string secondPath, string secondName) = WorldSaveSlotService.AllocateSavePath(tempRoot, "Bad/Name");
            (string unicodePath, string unicodeName) = WorldSaveSlotService.AllocateSavePath(tempRoot, "Café Village");

            Assert.That(firstName, Is.EqualTo("Bad/Name"));
            Assert.That(Path.GetFileName(firstPath), Is.EqualTo("Bad_Name.vxlworld"));
            Assert.That(secondName, Is.EqualTo("Bad/Name (2)"));
            Assert.That(Path.GetFileName(secondPath), Is.EqualTo("Bad_Name (2).vxlworld"));
            Assert.That(unicodeName, Is.EqualTo("Café Village"));
            Assert.That(Path.GetFileName(unicodePath), Is.EqualTo("Caf_ Village.vxlworld"));
        }

        [Test]
        public void WorldSaveGenerationOwnsMenuSeedAndSizeMapping()
        {
            Assert.That(WorldSaveGeneration.FoldSeed(0x0000000100000002UL), Is.EqualTo(3));
            Assert.That(WorldSaveGeneration.SizeFor("medium"), Is.EqualTo((192, 192)));
            Assert.That(WorldSaveGeneration.SizeFor("infinite"), Is.EqualTo((256, 256)));
            Assert.That(WorldSaveGeneration.SizeFor("unknown"), Is.EqualTo((128, 128)));

            GeneratedCreativeWorld menuWorld = WorldSaveGeneration.GenerateMenuWorld();
            Assert.That(menuWorld.GenerationPreset, Is.EqualTo(CreativeWorldGenerationPreset.MenuWorld));
            Assert.That(menuWorld.World.Seed, Is.EqualTo(2019));
            Assert.That(menuWorld.World.Bounds.Width, Is.EqualTo(16));
            Assert.That(menuWorld.World.Bounds.Height, Is.EqualTo(16));
            Assert.That(menuWorld.World.Bounds.Depth, Is.EqualTo(16));
            Assert.That(menuWorld.Settings.SpawnPosition, Is.EqualTo(new BlockPosition(8, 3, 8)));
            Assert.That(menuWorld.World.GetBlock(new BlockPosition(8, 1, 8)), Is.EqualTo(BlockRegistry.MeadowTurf));
            Assert.That(menuWorld.World.GetBlock(new BlockPosition(0, 2, 8)), Is.EqualTo(BlockRegistry.CutstoneBlock));
            Assert.That(menuWorld.World.GetBlock(new BlockPosition(15, 4, 8)), Is.EqualTo(BlockRegistry.CutstoneBlock));
            Assert.That(menuWorld.World.GetBlock(new BlockPosition(8, 2, 8)), Is.EqualTo(BlockRegistry.Air));
        }

        CreativeWorldManager CreateWorldManager()
        {
            worldObject = new GameObject("World Manager");
            worldObject.SetActive(false);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.ChunkAtlasMaterialPath);
            Assert.That(material, Is.Not.Null, "Session controller tests should use the committed authored chunk material.");
            CreativeInteractionController interactionController = worldObject.AddComponent<CreativeInteractionController>();
            CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
            manager.Configure(material, layer: -1, controller: interactionController);
            manager.ConfigureBlockTextureAtlases(BlockTextureSetIds.All, LoadBlockTextureSetAtlases());
            manager.InitializeDefaultWorld();
            return manager;
        }

        static Texture2D[] LoadBlockTextureSetAtlases()
        {
            string[] textureSetIds = BlockTextureSetIds.All;
            var atlases = new Texture2D[textureSetIds.Length];
            for (int i = 0; i < textureSetIds.Length; i++)
            {
                string path = BlockVisualAtlas.AtlasPathForTextureSet(textureSetIds[i]);
                atlases[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.That(atlases[i], Is.Not.Null, $"Missing block atlas for texture set {textureSetIds[i]} at {path}.");
            }

            return atlases;
        }

        BlockiverseWorldSessionController CreateSessionController(
            CreativeWorldManager worldManager,
            BlockiverseMenuController menuController = null)
        {
            sessionObject = new GameObject("Session Controller");
            BlockiverseWorldSessionController controller = sessionObject.AddComponent<BlockiverseWorldSessionController>();
            controller.Configure(menuController, worldManager);
            return controller;
        }

        BlockiverseMenuController CreateMenuController()
        {
            PlayerPrefs.SetInt(BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey, 1);
            menuObject = new GameObject("Menu Controller");
            BlockiverseMenuController controller = menuObject.AddComponent<BlockiverseMenuController>();
            var surfaceObject = new GameObject("UI Toolkit Menu Surface");
            surfaceObject.transform.SetParent(menuObject.transform, worldPositionStays: false);
            controller.ConfigureUiToolkitMenuSurface(surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>());
            InvokeUnityMessage(controller, "Start");
            return controller;
        }

        static VoxelWorld CreateFlatBuilderWorld(int seed)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 32,
                height: 64,
                depth: 32,
                chunkSize: WorldConstants.ChunkSize,
                seed: seed,
                groundHeight: WorldSaveGeneration.FlatBuilderGroundHeight);
            return new FlatBuilderPreset(registry, settings).Generate();
        }

        static void WriteManifestModifiedAt(string savePath, System.DateTime modifiedAtUtc)
        {
            string manifestPath = Path.Combine(savePath, "manifest.json");
            VxlwManifest manifest = JsonUtility.FromJson<VxlwManifest>(File.ReadAllText(manifestPath));
            manifest.ModifiedAtUtc = modifiedAtUtc.ToString("o");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, prettyPrint: true));
        }

        static WorldSaveService.WorldSaveInfo FindSaveInfo(string savePath)
        {
            foreach (WorldSaveService.WorldSaveInfo info in WorldSaveService.EnumerateSaves(Path.GetDirectoryName(savePath)))
                if (string.Equals(info.Path, savePath, System.StringComparison.Ordinal))
                    return info;

            Assert.Fail($"Missing save info for {savePath}.");
            return null;
        }

        static void InvokePrivateMethod(BlockiverseWorldSessionController controller, string methodName, params object[] args)
        {
            MethodInfo method = typeof(BlockiverseWorldSessionController).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing private method {methodName}.");
            method.Invoke(controller, args);
        }

        static void InvokeUnityMessage(MonoBehaviour target, string methodName, params object[] args)
        {
            MethodInfo method = FindInstanceMethod(target.GetType(), methodName);
            Assert.That(method, Is.Not.Null, $"Missing Unity message {methodName} on {target.GetType().Name}");
            method.Invoke(target, args);
        }

        static MethodInfo FindInstanceMethod(System.Type type, string methodName)
        {
            for (System.Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null)
                    return method;
            }

            return null;
        }

        static void SetPrivateField<T>(BlockiverseWorldSessionController controller, string fieldName, T value)
        {
            FieldInfo field = typeof(BlockiverseWorldSessionController).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
            field.SetValue(controller, value);
        }

        static T GetMenuControllerPrivateField<T>(BlockiverseMenuController controller, string fieldName)
        {
            FieldInfo field = typeof(BlockiverseMenuController).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
            return (T)field.GetValue(controller);
        }

        static void SetStaticField<T>(string fieldName, T value)
        {
            FieldInfo field = typeof(BlockiverseWorldSessionController).GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing static field {fieldName}.");
            field.SetValue(null, value);
        }
    }
}

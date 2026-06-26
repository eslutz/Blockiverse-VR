using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.MetaAvatars;
using Blockiverse.MetaPlatform;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using Oculus.Avatar2;
using Unity.Netcode;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.UIInteraction;
using Unity.XR.CoreUtils;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static void EnsureBootScene()
        {
            GameObject rigPrefab = EnsureXrRigPrefab();
            bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(BlockiverseProject.BootScenePath) != null;
            Scene scene = sceneExists
                ? EditorSceneManager.OpenScene(BlockiverseProject.BootScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rig = EnsureBootSceneRig(scene, rigPrefab);
            EnsureBootSceneMenuSurface(scene, rig);
            EnsureBootSceneLight(scene);
            EnsureBootEventSystem(scene);
            EnsureOvrAvatarManager(scene);
            EnsureMetaPlatformCompliance(scene);
            EnsureBootSceneCreativeWorld(scene);
            EnsureBootSceneNetworkStack(scene);
            RemoveRootGameObject(scene, InteractionTestBlockName);

            EditorSceneManager.SaveScene(scene, BlockiverseProject.BootScenePath);
            EnsureBuildScenes();
        }

        // The Boot scene carries the full network/survival runtime stack (session, chunk
        // authority, survival sync, vitals, persistence) so single-player survival works and the
        // title menu's LAN entry can host/join without a scene switch.
        static void EnsureBootSceneNetworkStack(Scene scene)
        {
            GameObject playerPrefab = EnsureNetworkPlayerPrefab();
            GameObject networkManagerPrefab = EnsureNetworkManagerPrefab(playerPrefab);
            GameObject managerObject = FindRootGameObject(scene, NetworkManagerRootName);

            if (managerObject == null)
                managerObject = (GameObject)PrefabUtility.InstantiatePrefab(networkManagerPrefab, scene);

            ConfigureNetworkManagerObject(managerObject, playerPrefab);
        }

        static GameObject EnsureNetworkPlayerPrefab()
        {
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.NetworkPlayerPrefabPath);

            if (existingPrefab != null)
            {
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(BlockiverseProject.NetworkPlayerPrefabPath);

                try
                {
                    ConfigureNetworkPlayerObject(prefabContents);
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, BlockiverseProject.NetworkPlayerPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }

                return AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.NetworkPlayerPrefabPath);
            }

            GameObject player = new(NetworkPlayerPrefabName);
            ConfigureNetworkPlayerObject(player);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, BlockiverseProject.NetworkPlayerPrefabPath);
            UnityEngine.Object.DestroyImmediate(player);
            return prefab;
        }

        static void ConfigureNetworkPlayerObject(GameObject playerObject)
        {
            playerObject.name = NetworkPlayerPrefabName;
            playerObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            playerObject.transform.localScale = Vector3.one;

            NetworkObject networkObject = EnsureComponent<NetworkObject>(playerObject);
            BlockiverseNetworkAvatarRig avatarRig = EnsureComponent<BlockiverseNetworkAvatarRig>(playerObject);
            MetaHorizonAvatarProvider avatarProvider = EnsureComponent<MetaHorizonAvatarProvider>(playerObject);
            BlockiverseMetaAvatarPresenter avatarPresenter = EnsureComponent<BlockiverseMetaAvatarPresenter>(playerObject);
            MetaAvatarStreamRelay avatarStreamRelay = EnsureComponent<MetaAvatarStreamRelay>(playerObject);

            avatarPresenter.Configure(
                avatarProvider,
                avatarRig,
                null,
                null,
                null,
                MetaAvatarPresentationMode.RemoteThirdPerson);
            avatarRig.ConfigureFirstPersonFallbackVisuals(false);

            EditorUtility.SetDirty(networkObject);
            EditorUtility.SetDirty(avatarRig);
            EditorUtility.SetDirty(avatarProvider);
            EditorUtility.SetDirty(avatarPresenter);
            EditorUtility.SetDirty(avatarStreamRelay);
            EditorUtility.SetDirty(playerObject);
        }

        static GameObject EnsureNetworkManagerPrefab(GameObject playerPrefab)
        {
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.NetworkManagerPrefabPath);

            if (existingPrefab != null)
            {
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(BlockiverseProject.NetworkManagerPrefabPath);

                try
                {
                    ConfigureNetworkManagerObject(prefabContents, playerPrefab);
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, BlockiverseProject.NetworkManagerPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }

                return AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.NetworkManagerPrefabPath);
            }

            GameObject managerObject = new(NetworkManagerRootName);
            ConfigureNetworkManagerObject(managerObject, playerPrefab);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(managerObject, BlockiverseProject.NetworkManagerPrefabPath);
            UnityEngine.Object.DestroyImmediate(managerObject);
            return prefab;
        }

        static void ConfigureNetworkManagerObject(GameObject managerObject, GameObject playerPrefab)
        {
            managerObject.name = NetworkManagerRootName;
            managerObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            managerObject.transform.localScale = Vector3.one;

            UnityTransport transport = EnsureComponent<UnityTransport>(managerObject);
            transport.SetConnectionData(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultPort,
                BlockiverseNetworkConfig.DefaultListenAddress);

            NetworkManager networkManager = EnsureComponent<NetworkManager>(managerObject);
            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            RemoveGeneratedNetworkPrefabLists(networkManager.NetworkConfig);
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.TickRate = 30;

            EnsureComponent<BlockiverseNetworkSession>(managerObject);
            EnsureComponent<BlockiverseNetworkBootstrap>(managerObject);
            EnsureComponent<MultiplayerChunkAuthoritySync>(managerObject);
            EnsureComponent<MultiplayerSurvivalSync>(managerObject);
            EnsureComponent<SurvivalVitalsRuntime>(managerObject);
            EnsureComponent<MultiplayerWorldPersistence>(managerObject);
            EnsureComponent<EnvironmentDynamicsController>(managerObject);

            EditorUtility.SetDirty(transport);
            EditorUtility.SetDirty(networkManager);
            EditorUtility.SetDirty(managerObject);
        }

        static void EnsureMultiplayerTestScene(GameObject networkManagerPrefab)
        {
            bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(BlockiverseProject.MultiplayerTestScenePath) != null;
            Scene scene = sceneExists
                ? EditorSceneManager.OpenScene(BlockiverseProject.MultiplayerTestScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject managerObject = FindRootGameObject(scene, NetworkManagerRootName);

            if (managerObject == null)
                managerObject = (GameObject)PrefabUtility.InstantiatePrefab(networkManagerPrefab, scene);

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.NetworkPlayerPrefabPath);
            ConfigureNetworkManagerObject(managerObject, playerPrefab);
            EnsureBootSceneCreativeWorld(scene);
            EnsureBootSceneLight(scene);
            EnsureMultiplayerTestCamera(scene);
            EnsureMultiplayerEventSystem(scene);
            EnsureOvrAvatarManager(scene);
            RemoveRootGameObject(scene, MultiplayerSessionMenuName);

            EditorSceneManager.SaveScene(scene, BlockiverseProject.MultiplayerTestScenePath);
        }

        static void EnsureBuildScenes()
        {
            var requiredScenes = new[]
            {
                BlockiverseProject.BootScenePath
            }
                .Where(path => AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToList();

            var editorOnlyScenes = new List<EditorBuildSettingsScene>();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BlockiverseProject.MultiplayerTestScenePath) != null)
                editorOnlyScenes.Add(new EditorBuildSettingsScene(BlockiverseProject.MultiplayerTestScenePath, false));

            var existingNonRequiredScenes = EditorBuildSettings.scenes
                .Where(scene => !string.IsNullOrWhiteSpace(scene.path))
                .Where(scene => requiredScenes.All(requiredScene => requiredScene.path != scene.path))
                .Where(scene => scene.path != BlockiverseProject.MultiplayerTestScenePath)
                .Where(scene => !scene.path.StartsWith("Assets/Blockiverse/Scenes/Ray", StringComparison.Ordinal))
                .GroupBy(scene => scene.path)
                .Select(group => group.First())
                .ToList();

            EditorBuildSettings.scenes = requiredScenes
                .Concat(editorOnlyScenes)
                .Concat(existingNonRequiredScenes)
                .ToArray();
        }

        static void RemoveGeneratedNetworkPrefabLists(NetworkConfig networkConfig)
        {
            networkConfig.Prefabs.NetworkPrefabsLists.RemoveAll(prefabsList =>
                prefabsList == null || AssetDatabase.GetAssetPath(prefabsList) == DefaultNetworkPrefabsPath);
        }

        static void RemoveGeneratedDefaultNetworkPrefabs()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefaultNetworkPrefabsPath) != null)
                AssetDatabase.DeleteAsset(DefaultNetworkPrefabsPath);
        }

        static GameObject EnsureBootSceneRig(Scene scene, GameObject rigPrefab)
        {
            GameObject rig = FindRootGameObject(scene, BlockiverseProject.XrRigRootName);

            if (rig != null && PrefabUtility.GetCorrespondingObjectFromSource(rig) == rigPrefab)
            {
                EditorUtility.SetDirty(rig);
                return rig;
            }

            if (rig != null)
                UnityEngine.Object.DestroyImmediate(rig);

            GameObject rigInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            if (rigInstance != null)
            {
                rigInstance.name = BlockiverseProject.XrRigRootName;
                EditorUtility.SetDirty(rigInstance);
            }

            return rigInstance;
        }

        static void EnsureBootSceneMenuSurface(Scene scene, GameObject rig)
        {
            GameObject menuRoot = FindRootGameObject(scene, MenuWorldUiRootName);
            if (menuRoot == null)
            {
                menuRoot = new GameObject(MenuWorldUiRootName);
                SceneManager.MoveGameObjectToScene(menuRoot, scene);
            }

            menuRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            menuRoot.transform.localScale = Vector3.one;

            BlockiverseUiToolkitMenuSurface surface = EnsureUiToolkitMenuSurface(menuRoot.transform, null);
            BlockiverseMenuController controller = rig != null
                ? rig.GetComponent<BlockiverseMenuController>()
                : FindRootGameObject(scene, BlockiverseProject.XrRigRootName)?.GetComponent<BlockiverseMenuController>();
            if (controller != null)
            {
                controller.ConfigureUiToolkitMenuSurface(surface);
                EditorUtility.SetDirty(controller);
            }

            EditorUtility.SetDirty(menuRoot);
        }

        static void EnsureBootSceneLight(Scene scene)
        {
            GameObject lightObject = FindRootGameObject(scene, BlockiverseLightingRuntime.SunObjectName) ??
                                     FindRootGameObject(scene, "Bootstrap Directional Light");

            if (lightObject == null)
            {
                lightObject = new GameObject(BlockiverseLightingRuntime.SunObjectName);
                SceneManager.MoveGameObjectToScene(lightObject, scene);
            }

            lightObject.name = BlockiverseLightingRuntime.SunObjectName;
            Light light = EnsureComponent<Light>(lightObject);
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.shadows = LightShadows.None;
            light.shadowStrength = 0f;
            light.renderMode = LightRenderMode.ForcePixel;
            lightObject.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

            WorldTimeClock clock = EnsureComponent<WorldTimeClock>(lightObject);
            BlockiverseLightingCycleController controller = EnsureComponent<BlockiverseLightingCycleController>(lightObject);
            controller.Configure(clock, light);
            EditorUtility.SetDirty(lightObject);
            EditorUtility.SetDirty(clock);
            EditorUtility.SetDirty(controller);
        }

        static void EnsureBootEventSystem(Scene scene)
        {
            EnsureEventSystem(scene, BootEventSystemName);
        }

        // Native Meta Avatar SDK initialization is Quest-runtime only. Keep any editor-authored scene
        // manager inactive in editor-authored scenes so macOS/headless PlayMode tests do not
        // load avatar native libraries; MetaHorizonAvatarProvider creates the singleton on Quest.
        static void EnsureOvrAvatarManager(Scene scene)
        {
            const string AvatarManagerName = "OvrAvatarManager";

            GameObject managerObject = FindRootGameObject(scene, AvatarManagerName);
            if (managerObject != null && managerObject.activeSelf)
            {
                managerObject.SetActive(false);
                EditorUtility.SetDirty(managerObject);
            }
        }

        static void EnsureMetaPlatformCompliance(Scene scene)
        {
            const string ComplianceRootName = "Meta Platform Compliance";

            GameObject complianceObject = FindRootGameObject(scene, ComplianceRootName);
            if (complianceObject == null)
            {
                complianceObject = new GameObject(ComplianceRootName);
                SceneManager.MoveGameObjectToScene(complianceObject, scene);
            }

            BlockiverseUserAgeCategoryService service = EnsureComponent<BlockiverseUserAgeCategoryService>(complianceObject);
            BlockiverseUserAgeCategoryService[] services = scene.GetRootGameObjects()
                .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<BlockiverseUserAgeCategoryService>(true))
                .Where(existingService => existingService != service)
                .ToArray();

            foreach (BlockiverseUserAgeCategoryService duplicate in services)
                UnityEngine.Object.DestroyImmediate(duplicate);

            EditorUtility.SetDirty(service);
            EditorUtility.SetDirty(complianceObject);
        }

        static void EnsureMultiplayerTestCamera(Scene scene)
        {
            GameObject cameraObject = FindRootGameObject(scene, MultiplayerTestCameraName);

            if (cameraObject == null)
            {
                cameraObject = new GameObject(MultiplayerTestCameraName);
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
            }

            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(0.0f, 1.45f, -2.5f),
                Quaternion.identity);

            Camera camera = EnsureComponent<Camera>(cameraObject);
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100.0f;

            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(cameraObject);
        }

        static void EnsureMultiplayerEventSystem(Scene scene)
        {
            EnsureEventSystem(scene, MultiplayerEventSystemName);
        }

        static void EnsureEventSystem(Scene scene, string eventSystemName)
        {
            GameObject eventSystemObject = FindRootGameObject(scene, eventSystemName);

            if (eventSystemObject == null)
            {
                eventSystemObject = new GameObject(eventSystemName);
                SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
            }

            eventSystemObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            eventSystemObject.transform.localScale = Vector3.one;

            EventSystem eventSystem = EnsureComponent<EventSystem>(eventSystemObject);
            eventSystem.sendNavigationEvents = true;

            // Menus are authored as world-space UI Toolkit panels driven by XRI tracked-device input.
            XRUIInputModule inputModule = EnsureComponent<XRUIInputModule>(eventSystemObject);
            EnsureInputActions();
            BlockiverseXrUiInputConfigurator.Configure(
                inputModule,
                LoadInputActionReference(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.UiPress),
                LoadInputActionReference(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.UiScroll));
            PanelInputConfiguration panelInputConfiguration = EnsureComponent<PanelInputConfiguration>(eventSystemObject);
            panelInputConfiguration.panelInputRedirection = PanelInputConfiguration.PanelInputRedirection.Never;

            EnsureXrInteractionManager(scene);

            EditorUtility.SetDirty(eventSystem);
            EditorUtility.SetDirty(inputModule);
            EditorUtility.SetDirty(panelInputConfiguration);
            EditorUtility.SetDirty(eventSystemObject);
        }

        static void EnsureXrInteractionManager(Scene scene)
        {
            GameObject managerObject = FindRootGameObject(scene, XrInteractionManagerName);

            if (managerObject == null)
            {
                managerObject = new GameObject(XrInteractionManagerName);
                SceneManager.MoveGameObjectToScene(managerObject, scene);
            }

            managerObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            EnsureComponent<XRInteractionManager>(managerObject);
            EditorUtility.SetDirty(managerObject);
        }

        static void EnsureBootSceneCreativeWorld(Scene scene)
        {
            GameObject worldObject = FindRootGameObject(scene, BlockiverseProject.CreativeWorldRootName);

            if (worldObject == null)
            {
                worldObject = new GameObject(BlockiverseProject.CreativeWorldRootName);
                SceneManager.MoveGameObjectToScene(worldObject, scene);
            }

            worldObject.transform.position = Vector3.zero;
            worldObject.transform.rotation = Quaternion.identity;
            worldObject.transform.localScale = Vector3.one;

            int interactionLayer = GetInteractionLayerIndex();
            worldObject.layer = interactionLayer;

            Material worldMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.ChunkAtlasMaterialPath);
            VoxelWorldRenderer renderer = EnsureComponent<VoxelWorldRenderer>(worldObject);
            PerformanceStatsOverlay performanceOverlay = EnsureComponent<PerformanceStatsOverlay>(worldObject);
            CreativeInteractionController controller = EnsureComponent<CreativeInteractionController>(worldObject);
            CreativeWorldManager manager = EnsureComponent<CreativeWorldManager>(worldObject);
            CreativeHotbar hotbar = FindBootSceneHotbar(scene);
            performanceOverlay.Configure(renderer);
            manager.InitializeDefaultWorldOnAwake = false;
            manager.Configure(worldMaterial, interactionLayer, controller, hotbar);
            manager.ConfigureBlockTextureAtlases(BlockTextureSetIds.All, LoadBlockTextureSetAtlases());

            BlockiverseCreativeInputBridge staleWorldBridge = worldObject.GetComponent<BlockiverseCreativeInputBridge>();

            if (staleWorldBridge != null)
                UnityEngine.Object.DestroyImmediate(staleWorldBridge);

            EnsureCreativeInputBridge(scene, controller);

            EditorUtility.SetDirty(worldObject);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(performanceOverlay);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(manager);
        }

        static CreativeHotbar FindBootSceneHotbar(Scene scene)
        {
            GameObject rig = FindRootGameObject(scene, BlockiverseProject.XrRigRootName);
            return rig != null ? rig.GetComponentInChildren<CreativeHotbar>(includeInactive: true) : null;
        }

        static void EnsureCreativeInputBridge(Scene scene, CreativeInteractionController controller)
        {
            GameObject rig = FindRootGameObject(scene, BlockiverseProject.XrRigRootName);

            if (rig == null)
                return;

            BlockiverseInputRig inputRig = rig.GetComponent<BlockiverseInputRig>();

            if (inputRig == null)
                return;

            XRRayInteractor interactionRay = FindInteractionRay(rig);
            BlockiverseCreativeInputBridge bridge = EnsureComponent<BlockiverseCreativeInputBridge>(rig);
            bridge.Configure(inputRig, interactionRay, controller);
            EnsureXrRigFeedback(rig, inputRig, controller);
            EditorUtility.SetDirty(bridge);
        }

        static void RemoveRootGameObject(Scene scene, string name)
        {
            GameObject existing = FindRootGameObject(scene, name);

            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing);
        }

        static GameObject FindRootGameObject(Scene scene, string name)
        {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == name)
                    return rootObject;
            }

            return null;
        }
    }
}

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
using TMPro;
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
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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

            EnsureBootSceneRig(scene, rigPrefab);
            EnsureBootSceneLight(scene);
            EnsureBootEventSystem(scene);
            EnsureOvrAvatarManager(scene);
            EnsureMetaPlatformCompliance(scene);
            EnsureBootSceneCreativeWorld(scene);
            EnsureBootSceneNetworkStack(scene);
            RemoveRootGameObject(scene, InteractionTestBlockName);

            EditorSceneManager.SaveScene(scene, BlockiverseProject.BootScenePath);

            // Unload the Boot scene from Editor memory before modifying its file content on disk.
            // This prevents edit-time package components from re-saving generated meshes to disk
            // when Unity does a domain reload or SaveAssets.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RemoveStaleRootCompositionLayerSceneDocuments(BlockiverseProject.BootScenePath);
            EnsureBuildScenes();
        }

        // The Boot scene carries the full network/survival runtime stack (session, chunk
        // authority, survival sync, vitals, persistence) so single-player survival works and the
        // title menu's LAN entry can host/join without a scene switch.
        static void EnsureBootSceneNetworkStack(Scene scene)
        {
            GameObject playerPrefab = EnsureNetworkPlayerPrefab();
            GameObject networkManagerPrefab = EnsureNetworkManagerPrefab(playerPrefab);
            GameObject managerObject = EnsureSingleRootGameObject(scene, NetworkManagerRootName);

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
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(playerObject);
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

        // Unity Transport's defaults are tuned for a wired connection to a dedicated server, not
        // two headsets on household Wi-Fi. Every value here is set explicitly so the tuning is
        // reviewable rather than inherited, and so tests can assert it survived a bootstrap.
        public static void ConfigureUnityTransportLimits(UnityTransport transport)
        {
            // The fragmentation stage is sized from this, so it is the real ceiling on a single
            // reliable message. Late-join snapshots are batched under it (see
            // MultiplayerChunkAuthoritySync.SnapshotBatchMaxBlocks); the headroom covers
            // inventory and station snapshots for a full 44-slot inventory.
            transport.MaxPayloadSize = TransportMaxPayloadBytes;

            // Late join bursts a header plus every changed-block batch back to back. The stock
            // 128-packet queue overflows on a well-built world and drops snapshot messages.
            transport.MaxPacketQueueSize = TransportMaxPacketQueueSize;

            // Quest Wi-Fi tolerates a roaming stall better than it tolerates a false disconnect
            // mid-session, so the disconnect timeout is generous and heartbeats are frequent
            // enough to keep an idle build session alive.
            transport.HeartbeatTimeoutMS = TransportHeartbeatTimeoutMs;
            transport.ConnectTimeoutMS = TransportConnectTimeoutMs;
            transport.MaxConnectAttempts = TransportMaxConnectAttempts;
            transport.DisconnectTimeoutMS = TransportDisconnectTimeoutMs;
        }

        public const int TransportMaxPayloadBytes = 16 * 1024;
        public const int TransportMaxPacketQueueSize = 256;
        public const int TransportHeartbeatTimeoutMs = 500;
        public const int TransportConnectTimeoutMs = 1000;
        public const int TransportMaxConnectAttempts = 10;
        public const int TransportDisconnectTimeoutMs = 5000;

        static void ConfigureNetworkManagerObject(GameObject managerObject, GameObject playerPrefab)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(managerObject);
            managerObject.name = NetworkManagerRootName;
            managerObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            managerObject.transform.localScale = Vector3.one;

            UnityTransport transport = EnsureComponent<UnityTransport>(managerObject);
            transport.SetConnectionData(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultPort,
                BlockiverseNetworkConfig.DefaultListenAddress);
            ConfigureUnityTransportLimits(transport);

            NetworkManager networkManager = EnsureComponent<NetworkManager>(managerObject);
            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            RemoveGeneratedNetworkPrefabLists(networkManager.NetworkConfig);
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkManager.NetworkConfig.ConnectionApproval = true;
            // Load-bearing beyond performance: Netcode hashes TickRate into the connection config
            // hash (NetworkConfig.GetConfig writes it), and a mismatch makes Netcode drop the
            // client BEFORE connection approval -- so no BlockiverseJoinRejectionReason is produced
            // and the refusal is close to undiagnosable from either side.
            //
            // This one line is why server and client agree: the dedicated server scene instantiates
            // this same prefab through this same method. Do NOT make it per-mode configurable, and
            // if the value ever changes it must change here for both, never on one side.
            networkManager.NetworkConfig.TickRate = 30;

            EnsureComponent<BlockiverseNetworkSession>(managerObject);
            EnsureComponent<BlockiverseNetworkBootstrap>(managerObject);
            // Inert until a secret is configured; must sit on the same object as the session so
            // both syncs can resolve it with GetComponent and defer world state until authorized.
            EnsureComponent<BlockiverseServerAuthGate>(managerObject);
            EnsureComponent<MultiplayerChunkAuthoritySync>(managerObject);
            EnsureComponent<MultiplayerSurvivalSync>(managerObject);
            // Lives on the always-active NetworkManager object, not the LAN panel: the host has
            // to keep beaconing while the panel is closed and the player is out building.
            EnsureComponent<BlockiverseLanDiscovery>(managerObject);
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
            EnsureMultiplayerSessionMenu(scene, managerObject);

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

        static void EnsureBootSceneRig(Scene scene, GameObject rigPrefab)
        {
            RemoveStaleRootCompositionLayers(scene);
            EnsureSingleRootGameObject(scene, MenuCompositionCanvasName);
            GameObject rig = EnsureSingleRootGameObject(scene, BlockiverseProject.XrRigRootName);

            // Reuse an existing instance of the current rig prefab. Destroying and re-instantiating
            // unconditionally renumbers the scene's PrefabInstance fileID on every run, so a rerun
            // dirtied Boot.unity by ~230 lines even when nothing had actually changed. A prefab
            // instance already tracks the asset, so only a foreign or unpacked root needs replacing.
            if (rig != null &&
                PrefabUtility.IsPartOfPrefabInstance(rig) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rig) == AssetDatabase.GetAssetPath(rigPrefab))
            {
                rig.name = BlockiverseProject.XrRigRootName;
                RemoveGeneratedCompositionLayerSceneOverrides(rig);
                EditorUtility.SetDirty(rig);
                return;
            }

            if (rig != null)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(rig))
                {
                    PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                UnityEngine.Object.DestroyImmediate(rig);
            }

            GameObject rigInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            if (rigInstance != null)
            {
                rigInstance.name = BlockiverseProject.XrRigRootName;
                RemoveGeneratedCompositionLayerSceneOverrides(rigInstance);
                EditorUtility.SetDirty(rigInstance);
            }
        }

        static void RemoveStaleRootCompositionLayers(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null || root.name == BlockiverseProject.XrRigRootName)
                    continue;

                if (root.name == "Composition Render Scale Surface" ||
                    root.GetComponent<CompositionLayer>() != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        static void RemoveStaleRootCompositionLayerSceneDocuments(string scenePath)
        {
            if (!File.Exists(scenePath))
                return;

            string sceneYaml = File.ReadAllText(scenePath);
            string[] rawDocuments = sceneYaml.Split(new[] { "--- !u!" }, StringSplitOptions.None);
            List<string> keptDocuments = new List<string>();
            keptDocuments.Add(rawDocuments[0]); // Header segment

            int removedCount = 0;
            for (int i = 1; i < rawDocuments.Length; i++)
            {
                string document = rawDocuments[i];
                if (IsStaleRootCompositionLayerGameObjectDocument(document))
                {
                    removedCount++;
                    continue;
                }
                keptDocuments.Add("--- !u!" + document);
            }

            if (removedCount == 0)
                return;

            string cleanedSceneYaml = string.Concat(keptDocuments);
            File.WriteAllText(scenePath, cleanedSceneYaml);
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
        }

        static IReadOnlyList<string> SplitUnityYamlDocuments(string yaml)
        {
            var documents = new List<string>();
            int firstDocumentIndex = yaml.IndexOf("--- !u!", StringComparison.Ordinal);
            if (firstDocumentIndex > 0)
                documents.Add(yaml[..firstDocumentIndex]);

            foreach (Match match in Regex.Matches(yaml, @"(?ms)^--- !u!.*?(?=^--- !u!|\z)"))
                documents.Add(match.Value);

            return documents.Count > 0 ? documents : new[] { yaml };
        }

        static bool TryGetUnityYamlDocumentId(string document, out string documentId)
        {
            Match match = Regex.Match(document, @"^--- !u!\d+ &(-?\d+)", RegexOptions.Multiline);
            documentId = match.Success ? match.Groups[1].Value : string.Empty;
            return match.Success;
        }

        static bool IsStaleRootCompositionLayerGameObjectDocument(string document)
        {
            return document.Contains("m_Name: Composition Render Scale Surface", StringComparison.Ordinal) ||
                   document.Contains("m_Name: Composition Layer Plane", StringComparison.Ordinal);
        }

        static void RemoveGeneratedCompositionLayerSceneOverrides(GameObject rigInstance)
        {
            if (rigInstance == null)
                return;

            Transform cameraOffset = rigInstance.transform.Find("Camera Offset");
            Transform surface = cameraOffset != null ? cameraOffset.Find(MenuCompositionSurfaceName) : null;
            if (surface == null)
                return;

            MeshCollider meshCollider = surface.GetComponent<MeshCollider>();
            RevertPrefabObjectOverride(meshCollider);
            if (meshCollider != null && meshCollider.sharedMesh != null)
            {
                meshCollider.sharedMesh = null;
                EditorUtility.SetDirty(meshCollider);
            }

            TexturesExtension texturesExtension = surface.GetComponent<TexturesExtension>();
            RevertPrefabObjectOverride(texturesExtension);
            if (texturesExtension != null)
            {
                texturesExtension.LeftTexture = null;
                texturesExtension.RightTexture = null;
                EditorUtility.SetDirty(texturesExtension);
            }

            InteractableUIMirror mirror = surface.GetComponent<InteractableUIMirror>();
            RevertPrefabObjectOverride(mirror);

            Camera canvasCamera = surface.Find($"{MenuCompositionCanvasName}/CanvasCamera")?.GetComponent<Camera>();
            RevertPrefabObjectOverride(canvasCamera);
            if (canvasCamera != null)
            {
                canvasCamera.targetTexture = null;
                canvasCamera.enabled = false;
                canvasCamera.nearClipPlane = 0.01f;
                canvasCamera.clearFlags = CameraClearFlags.SolidColor;
                canvasCamera.backgroundColor = Color.clear;
                canvasCamera.cullingMask = 1 << GetCompositionUiLayerIndex();
                EditorUtility.SetDirty(canvasCamera);
            }
        }

        static void RevertPrefabObjectOverride(UnityEngine.Object target)
        {
            if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target))
                return;

            PrefabUtility.RevertObjectOverride(target, InteractionMode.AutomatedAction);
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
            // The sun/moon is the scene's only shadow-casting directional light.
            // BlockiverseLightingCycleController re-applies style and strength every frame.
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.8f;
            // Deliberately no per-light shadowBias here: UniversalAdditionalLightData defaults
            // usePipelineSettings = true, so URP reads m_ShadowDepthBias / m_ShadowNormalBias off
            // the pipeline asset and per-light values never reach the GPU. They are tuned in
            // ConfigureQuestUrpShadowPolicy instead.
            light.renderMode = LightRenderMode.ForcePixel;
            lightObject.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

            WorldTimeClock clock = EnsureComponent<WorldTimeClock>(lightObject);
            BlockiverseLightingCycleController controller = EnsureComponent<BlockiverseLightingCycleController>(lightObject);
            controller.Configure(clock, light);

            // Replaces Unity's stock procedural skybox, which had no time-of-day input at all and
            // rendered a noon sky at midnight because it reads the shared sun/moon light's
            // DIRECTION -- and that light is deliberately pointed down from overhead at night.
            Material sky = EnsureSkyMaterial();
            RenderSettings.skybox = sky;
            RenderSettings.sun = light;
            controller.ConfigureSky(sky);
            EditorUtility.SetDirty(lightObject);
            EditorUtility.SetDirty(clock);
            EditorUtility.SetDirty(controller);
        }

        static void EnsureBootEventSystem(Scene scene)
        {
            EnsureEventSystem(scene, BootEventSystemName);
        }

        // Native Meta Avatar SDK initialization is Quest-runtime only. The manager object is
        // authored INACTIVE with its full asset configuration serialized — shader
        // configurations (the URP-capable Style-2-Avatar-Meta path), GPU-skinning shaders,
        // and the LOD manager — so those assets ship in the player build via the scene
        // references. MetaHorizonAvatarProvider activates it on Quest, which runs
        // OvrAvatarManager.Initialize with real configuration; it stays inactive in the
        // editor so macOS/headless PlayMode tests never load avatar native libraries.
        //
        // A bare runtime OvrAvatarManager.Instantiate() must never be the primary path: its
        // fallback shader manager resolves Shader.Find("Standard"), which is stripped from
        // URP Android builds, and its runtime GpuSkinningConfiguration has null compute
        // shaders (the auto-fill is editor-only) — either alone makes every avatar
        // permanently invisible on device.
        static void EnsureOvrAvatarManager(Scene scene)
        {
            const string LegacyAvatarManagerName = "OvrAvatarManager";
            const string ShaderManagerChildName = "Style-2-Avatar-Meta";

            GameObject legacyManagerObject = FindRootGameObject(scene, LegacyAvatarManagerName);
            if (legacyManagerObject != null && legacyManagerObject.activeSelf)
            {
                legacyManagerObject.SetActive(false);
                EditorUtility.SetDirty(legacyManagerObject);
            }

            GameObject managerObject = FindRootGameObject(scene, MetaHorizonAvatarProvider.SdkManagerObjectName);
            if (managerObject == null)
            {
                managerObject = new GameObject(MetaHorizonAvatarProvider.SdkManagerObjectName);
                managerObject.SetActive(false);
                SceneManager.MoveGameObjectToScene(managerObject, scene);
            }
            else if (managerObject.activeSelf)
            {
                managerObject.SetActive(false);
                EditorUtility.SetDirty(managerObject);
            }

            // Component order mirrors Meta's AvatarSdkManager sample prefab: the manager's
            // Awake must run first on activation so its Initialize() adopts the LOD and
            // skinning components already present on the object.
            OvrAvatarManager avatarManager = EnsureComponent<OvrAvatarManager>(managerObject);
            EnsureComponent<AvatarLODManager>(managerObject);
            GpuSkinningConfiguration skinningConfiguration = EnsureComponent<GpuSkinningConfiguration>(managerObject);

            Transform shaderChild = managerObject.transform.Find(ShaderManagerChildName);
            GameObject shaderObject = shaderChild != null ? shaderChild.gameObject : null;
            if (shaderObject == null)
            {
                shaderObject = new GameObject(ShaderManagerChildName);
                shaderObject.transform.SetParent(managerObject.transform, false);
            }

            OvrAvatarShaderManagerSingle shaderManager = EnsureComponent<OvrAvatarShaderManagerSingle>(shaderObject);

            bool changed = false;
            changed |= SetSerializedReference(avatarManager, "ShaderManager", shaderManager);
            changed |= SetSerializedReference(
                shaderManager,
                "DefaultShaderConfigurationInitializer",
                LoadMetaAvatarsPackageAsset<OvrAvatarShaderConfiguration>(
                    "Scripts/Common/Shaders/Configurations/Recommended/MetaStyle2ShaderConfiguration.asset"));
            changed |= SetSerializedReference(
                shaderManager,
                "FastLoadConfigurationInitializer",
                LoadMetaAvatarsPackageAsset<OvrAvatarShaderConfiguration>(
                    "Scripts/Common/Shaders/Configurations/Recommended/MetaStyle2ShaderConfigurationVertex.asset"));
            changed |= SetSerializedReference(
                shaderManager,
                "CelShaderConfigurationInitializer",
                LoadMetaAvatarsPackageAsset<OvrAvatarShaderConfiguration>(
                    "Scripts/Common/Shaders/Configurations/Recommended/MetaShaderCelConfiguration.asset"));
            changed |= SetSerializedReference(
                skinningConfiguration,
                "_CombineMorphTargetsShader",
                LoadMetaAvatarsPackageAsset<Shader>("Scripts/Skinning/GpuSkinning/Shaders/combine-morph-targets.shader"));
            changed |= SetSerializedReference(
                skinningConfiguration,
                "_SkinToTextureShader",
                LoadMetaAvatarsPackageAsset<Shader>("Scripts/Skinning/GpuSkinning/Shaders/skin-to-texture.shader"));
            changed |= SetSerializedReference(
                skinningConfiguration,
                "_morphAndSkinningComputeShader",
                LoadMetaAvatarsPackageAsset<ComputeShader>("Scripts/Skinning/GpuSkinning/Shaders/OvrApplyMorphsAndSkinning.compute"));

            if (changed)
                EditorUtility.SetDirty(managerObject);
        }

        static T LoadMetaAvatarsPackageAsset<T>(string packageRelativePath) where T : UnityEngine.Object
        {
            string assetPath = $"Packages/com.meta.xr.sdk.avatars/{packageRelativePath}";
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
                Debug.LogWarning($"[BlockiverseProjectBootstrapper] Missing Meta Avatars package asset: {assetPath}");
            return asset;
        }

        static bool SetSerializedReference(Component component, string propertyName, UnityEngine.Object value)
        {
            var serializedComponent = new SerializedObject(component);
            SerializedProperty property = serializedComponent.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning(
                    $"[BlockiverseProjectBootstrapper] Serialized property '{propertyName}' not found on {component.GetType().Name}; " +
                    "the Meta Avatars package layout may have changed.");
                return false;
            }

            if (property.objectReferenceValue == value)
                return false;

            property.objectReferenceValue = value;
            serializedComponent.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
            return true;
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
            GameObject eventSystemObject = EnsureSingleRootGameObject(scene, eventSystemName);

            if (eventSystemObject == null)
            {
                eventSystemObject = new GameObject(eventSystemName);
                SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
            }

            eventSystemObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            eventSystemObject.transform.localScale = Vector3.one;

            EventSystem eventSystem = EnsureComponent<EventSystem>(eventSystemObject);
            // Ray-driven world-space UI: pointer events only. Navigation/Submit events
            // would let the UI Press action fire the auto-selected Button a second time.
            eventSystem.sendNavigationEvents = false;

            StandaloneInputModule legacyInputModule = eventSystemObject.GetComponent<StandaloneInputModule>();

            if (legacyInputModule != null)
                UnityEngine.Object.DestroyImmediate(legacyInputModule);

            // VR UI is driven by tracked-device rays, so replace the plain Input System module with
            // XRI's module which understands tracked-device pointer events from XRRayInteractors.
            // XRUIInputModule does not derive from InputSystemUIInputModule, so a legacy module found
            // here is always the screen-space one and is removed before adding the XR module.
            InputSystemUIInputModule legacyUiModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();

            if (legacyUiModule != null)
                UnityEngine.Object.DestroyImmediate(legacyUiModule);

            XRUIInputModule inputModule = EnsureComponent<XRUIInputModule>(eventSystemObject);
            EnsureInputActions();
            BlockiverseXrUiInputConfigurator.Configure(
                inputModule,
                LoadInputActionReference(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.UiPress),
                LoadInputActionReference(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.UiScroll));

            EnsureXrInteractionManager(scene);

            EditorUtility.SetDirty(eventSystem);
            EditorUtility.SetDirty(inputModule);
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

        static void EnsureMultiplayerSessionMenu(Scene scene, GameObject managerObject)
        {
            GameObject menuObject = FindRootGameObject(scene, MultiplayerSessionMenuName);

            if (menuObject == null)
            {
                menuObject = new GameObject(MultiplayerSessionMenuName, typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(menuObject, scene);
            }

            menuObject.transform.SetPositionAndRotation(
                new Vector3(0.0f, 1.4f, 1.8f),
                Quaternion.Euler(0.0f, 180.0f, 0.0f));
            menuObject.transform.localScale = Vector3.one * 0.003f;

            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, MultiplayerSessionMenuSize.x);
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, MultiplayerSessionMenuSize.y);

            Canvas canvas = EnsureComponent<Canvas>(menuObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            canvas.enabled = true;

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(menuObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(menuObject);

            GameObject panelObject = EnsureRectChild(menuObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            Sprite panelSprite = GetRoundedSprite();
            if (panelSprite != null)
            {
                panelImage.sprite = panelSprite;
                panelImage.type = Image.Type.Sliced;
            }
            panelImage.color = MultiplayerMenuPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "LAN Session",
                36,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(28.0f, -34.0f),
                new Vector2(500.0f, 52.0f));

            TMP_InputField addressInput = EnsureInputFieldControl(
                panelObject.transform,
                "Address Input",
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanJoinAddressPlaceholder),
                string.Empty,
                new Vector2(28.0f, -102.0f),
                new Vector2(500.0f, 58.0f));

            Button hostButton = EnsureButtonControl(
                panelObject.transform,
                "Host Button",
                "Host",
                new Vector2(28.0f, -182.0f),
                new Vector2(148.0f, 54.0f));

            Button joinButton = EnsureButtonControl(
                panelObject.transform,
                "Join Button",
                "Join",
                new Vector2(198.0f, -182.0f),
                new Vector2(148.0f, 54.0f));

            Button stopButton = EnsureButtonControl(
                panelObject.transform,
                "Stop Button",
                "Stop",
                new Vector2(368.0f, -182.0f),
                new Vector2(148.0f, 54.0f));

            TextMeshProUGUI statusText = EnsureLabel(
                panelObject.transform,
                "Status",
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStoppedWithDefault),
                22,
                TextAnchor.UpperLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(28.0f, -258.0f),
                new Vector2(500.0f, 88.0f),
                TextDimColor);

            GameObject badgeObject = EnsureRectChild(panelObject.transform, "Status Badge");
            ConfigureTopLeftRect(badgeObject.GetComponent<RectTransform>(), new Vector2(470.0f, -30.0f), new Vector2(32.0f, 32.0f));
            Image statusBadge = EnsureComponent<Image>(badgeObject);
            ApplySlicedSprite(statusBadge, GetUiSprite("multiplayer_status_badge"));
            statusBadge.color = new Color(0.55f, 0.58f, 0.62f, 1.0f);

            BlockiverseMultiplayerSessionMenu menu = EnsureComponent<BlockiverseMultiplayerSessionMenu>(menuObject);
            menu.Configure(managerObject != null ? managerObject.GetComponent<BlockiverseNetworkSession>() : null);
            menu.ConfigureControls(hostButton, joinButton, null, stopButton, addressInput, statusText);
            menu.ConfigureStatusBadge(statusBadge);

            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(menuObject);
        }

        static void EnsureBootSceneCreativeWorld(Scene scene)
        {
            GameObject worldObject = EnsureSingleRootGameObject(scene, BlockiverseProject.CreativeWorldRootName);

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
            // The presentation half of the world root. CreativeWorldManager finds it by interface,
            // so it must exist on the same object; on a dedicated server this component's whole
            // assembly is excluded and the manager simply resolves null (ADR 0007).
            BlockiverseWorldPresentation presentation = EnsureComponent<BlockiverseWorldPresentation>(worldObject);
            CreativeHotbar hotbar = FindBootSceneHotbar(scene);
            performanceOverlay.Configure(renderer);
            manager.InitializeDefaultWorldOnAwake = true;
            presentation.Configure(worldMaterial, interactionLayer, controller, hotbar);
            presentation.ConfigureBlockTextureAtlases(BlockTextureSetIds.All, LoadBlockTextureSetAtlases());

            BlockiverseCreativeInputBridge staleWorldBridge = worldObject.GetComponent<BlockiverseCreativeInputBridge>();

            if (staleWorldBridge != null)
                UnityEngine.Object.DestroyImmediate(staleWorldBridge);

            EnsureCreativeInputBridge(scene, controller);

            EditorUtility.SetDirty(worldObject);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(performanceOverlay);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(presentation);
        }

        static CreativeHotbar FindBootSceneHotbar(Scene scene)
        {
            GameObject rig = FindRootGameObject(scene, BlockiverseProject.XrRigRootName);
            Transform hotbarTransform = rig != null ? rig.transform.Find("Camera Offset/" + BlockMenuName) : null;
            return hotbarTransform != null ? hotbarTransform.GetComponent<CreativeHotbar>() : null;
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
            GameObject existing = EnsureSingleRootGameObject(scene, name);

            if (existing != null)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(existing))
                {
                    PrefabUtility.UnpackPrefabInstance(existing, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                UnityEngine.Object.DestroyImmediate(existing);
            }
        }

        static GameObject EnsureSingleRootGameObject(Scene scene, string name)
        {
            GameObject retained = null;

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name != name)
                    continue;

                if (retained == null)
                {
                    retained = rootObject;
                    continue;
                }

                if (PrefabUtility.IsPartOfPrefabInstance(rootObject))
                {
                    PrefabUtility.UnpackPrefabInstance(rootObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                UnityEngine.Object.DestroyImmediate(rootObject);
            }

            return retained;
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

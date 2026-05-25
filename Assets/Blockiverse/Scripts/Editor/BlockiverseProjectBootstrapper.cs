using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.VR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Unity.XR.CoreUtils;

namespace Blockiverse.Editor
{
    public static class BlockiverseProjectBootstrapper
    {
        static readonly string[] RequiredFolders =
        {
            "Assets/Blockiverse/Art",
            "Assets/Blockiverse/Audio",
            "Assets/Blockiverse/Materials",
            "Assets/Blockiverse/Prefabs",
            "Assets/Blockiverse/Scenes",
            "Assets/Blockiverse/Scripts",
            "Assets/Blockiverse/Settings",
            "Assets/Blockiverse/Tests/EditMode",
            "Assets/Blockiverse/Tests/PlayMode"
        };

        static readonly string[] AndroidOpenXrFeatureIds =
        {
            "com.unity.openxr.feature.metaquest",
            "com.unity.openxr.feature.input.oculustouch",
            "com.unity.openxr.feature.input.metaquestplus",
            "com.unity.openxr.feature.input.metaquestpro",
            "com.meta.openxr.feature.metaxr",
            "com.meta.openxr.feature.foveation"
        };

        const string ComfortMenuName = "Comfort Settings Menu";
        const string BlockMenuName = "Block Menu Placeholder";
        const string PointerLineName = "Ray Pointer Line";
        const string InteractionTestBlockName = "Interaction Test Block";
        static readonly Vector2 ComfortMenuSize = new(520.0f, 420.0f);
        static readonly Vector2 BlockMenuSize = new(360.0f, 260.0f);
        static readonly Color ComfortMenuPanelColor = new(0.07f, 0.08f, 0.09f, 0.92f);
        static readonly Color ComfortMenuControlColor = new(0.18f, 0.21f, 0.24f, 1.0f);
        static readonly Color ComfortMenuAccentColor = new(0.19f, 0.72f, 0.54f, 1.0f);
        static readonly Color BlockMenuPanelColor = new(0.05f, 0.12f, 0.16f, 0.94f);
        static readonly Color BlockMenuControlColor = new(0.18f, 0.31f, 0.36f, 1.0f);
        static readonly Color BlockMenuAccentColor = new(0.94f, 0.72f, 0.26f, 1.0f);
        static readonly Color PointerLineColor = new(0.36f, 0.82f, 1.0f, 0.92f);
        static readonly Color HighlightColor = new(1.0f, 0.85f, 0.18f, 1.0f);
        static readonly Color TestBlockColor = new(0.22f, 0.56f, 0.43f, 1.0f);

        [MenuItem("Blockiverse/Bootstrap Unity Quest Project")]
        public static void Run()
        {
            EnsureFolders();
            ConfigureEditorSerialization();
            ConfigureAndroidPlayer();
            ConfigureMetaProjectSettings();
            ConfigureAndroidManifest();
            ConfigureMetaRuntimeSettings();
            ConfigureUniversalRenderPipeline();
            ConfigureOpenXrForAndroid();
            EnsureInteractionLayer();
            EnsureInteractionMaterials();
            EnsureInputActions();
            EnsureXrRigPrefab();
            EnsureBootScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Blockiverse Unity/Quest bootstrap complete.");
        }

        static void EnsureFolders()
        {
            foreach (string folder in RequiredFolders)
                EnsureFolder(folder);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(folder);

            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        static void ConfigureEditorSerialization()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";
        }

        static void ConfigureAndroidPlayer()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            PlayerSettings.companyName = BlockiverseProject.CompanyName;
            PlayerSettings.productName = BlockiverseProject.ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BlockiverseProject.AndroidApplicationIdentifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.androidTVCompatibility = false;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            SetActiveInputHandlerToInputSystemOnly();

#if UNITY_2023_2_OR_NEWER
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
#endif
        }

        static void ConfigureAndroidManifest()
        {
            global::OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(silentMode: true);
        }

        static void ConfigureMetaProjectSettings()
        {
            global::OVRProjectConfig projectConfig = global::OVRProjectConfig.CachedProjectConfig;
            projectConfig.targetDeviceTypes.Clear();
            projectConfig.targetDeviceTypes.Add(global::OVRProjectConfig.DeviceType.Quest3);
            projectConfig.targetDeviceTypes.Add(global::OVRProjectConfig.DeviceType.Quest3S);
            projectConfig.handTrackingSupport = global::OVRProjectConfig.HandTrackingSupport.ControllersOnly;
            projectConfig.anchorSupport = global::OVRProjectConfig.AnchorSupport.Disabled;
            projectConfig.sharedAnchorSupport = global::OVRProjectConfig.FeatureSupport.None;
            projectConfig.bodyTrackingSupport = global::OVRProjectConfig.FeatureSupport.None;
            projectConfig.faceTrackingSupport = global::OVRProjectConfig.FeatureSupport.None;
            projectConfig.eyeTrackingSupport = global::OVRProjectConfig.FeatureSupport.None;
            projectConfig.colocationSessionSupport = global::OVRProjectConfig.FeatureSupport.None;
            projectConfig.sceneSupport = global::OVRProjectConfig.FeatureSupport.None;
            global::OVRProjectConfig.CommitProjectConfig(projectConfig);
        }

        static void ConfigureMetaRuntimeSettings()
        {
            var runtimeSettings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/Resources/OculusRuntimeSettings.asset");

            if (runtimeSettings == null)
                return;

            var serializedSettings = new SerializedObject(runtimeSettings);
            SetBoolIfPresent(serializedSettings, "requestsVisualFaceTracking", false);
            SetBoolIfPresent(serializedSettings, "requestsAudioFaceTracking", false);
            SetBoolIfPresent(serializedSettings, "enableFaceTrackingVisemesOutput", false);
            serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(runtimeSettings);
        }

        static void SetBoolIfPresent(SerializedObject serializedObject, string propertyName, bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);

            if (property != null)
                property.boolValue = value;
        }

        static void SetActiveInputHandlerToInputSystemOnly()
        {
            PlayerSettings playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>().FirstOrDefault();

            if (playerSettings == null)
                return;

            var serializedSettings = new SerializedObject(playerSettings);
            SerializedProperty activeInputHandler = serializedSettings.FindProperty("activeInputHandler");

            if (activeInputHandler == null)
                return;

            activeInputHandler.intValue = 1;
            serializedSettings.ApplyModifiedProperties();
        }

        static void ConfigureUniversalRenderPipeline()
        {
            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                BlockiverseProject.AndroidUrpAssetPath);

            if (pipelineAsset == null)
            {
                var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(
                    BlockiverseProject.AndroidUrpRendererPath);

                if (rendererData == null)
                {
                    rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    rendererData.name = "Blockiverse Android Universal Renderer";
                    AssetDatabase.CreateAsset(rendererData, BlockiverseProject.AndroidUrpRendererPath);
                }

                pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
                pipelineAsset.name = "Blockiverse Android URP Asset";
                AssetDatabase.CreateAsset(pipelineAsset, BlockiverseProject.AndroidUrpAssetPath);
            }

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
            EditorUtility.SetDirty(pipelineAsset);
        }

        static void ConfigureOpenXrForAndroid()
        {
            XRGeneralSettings androidSettings = EnsureXrGeneralSettings(BuildTargetGroup.Android);

            if (androidSettings?.Manager == null)
                throw new InvalidOperationException("Unable to create Android XR manager settings.");

            if (!XRPackageMetadataStore.AssignLoader(
                    androidSettings.Manager,
                    "UnityEngine.XR.OpenXR.OpenXRLoader",
                    BuildTargetGroup.Android))
            {
                Debug.LogWarning("OpenXR loader was already assigned or could not be reassigned for Android.");
            }

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);

            OpenXRSettings openXrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            openXrSettings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            openXrSettings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;

            foreach (string featureId in AndroidOpenXrFeatureIds)
            {
                UnityEngine.XR.OpenXR.Features.OpenXRFeature feature =
                    FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);

                if (feature == null)
                {
                    Debug.LogWarning($"OpenXR feature was not found for Android: {featureId}");
                    continue;
                }

                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }

            EditorUtility.SetDirty(openXrSettings);
        }

        static InputActionAsset EnsureInputActions()
        {
            var existingAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            if (existingAsset != null)
                return existingAsset;

            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            AddControllerMap(asset, BlockiverseInputActionNames.LeftHandMap, "<XRController>{LeftHand}");
            AddControllerMap(asset, BlockiverseInputActionNames.RightHandMap, "<XRController>{RightHand}");
            AddGameplayMap(asset);

            File.WriteAllText(BlockiverseProject.InputActionsAssetPath, asset.ToJson());
            UnityEngine.Object.DestroyImmediate(asset);

            AssetDatabase.ImportAsset(
                BlockiverseProject.InputActionsAssetPath,
                ImportAssetOptions.ForceSynchronousImport);

            var importedAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            if (importedAsset == null)
                throw new InvalidOperationException("Unable to create Blockiverse input actions asset.");

            return importedAsset;
        }

        static void EnsureInteractionMaterials()
        {
            EnsureMaterial(BlockiverseProject.PointerLineMaterialPath, PointerLineColor, preferUnlit: true);
            EnsureMaterial(BlockiverseProject.HighlightMaterialPath, HighlightColor, preferUnlit: false);
            EnsureMaterial(BlockiverseProject.TestBlockMaterialPath, TestBlockColor, preferUnlit: false);
        }

        static Material EnsureMaterial(string path, Color color, bool preferUnlit)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(FindShader(preferUnlit));
                material.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(material, path);
            }

            SetMaterialColor(material, color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Shader FindShader(bool preferUnlit)
        {
            string[] shaderNames = preferUnlit
                ? new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default", "Standard" }
                : new[] { "Universal Render Pipeline/Lit", "Standard", "Sprites/Default" };

            foreach (string shaderName in shaderNames)
            {
                Shader shader = Shader.Find(shaderName);

                if (shader != null)
                    return shader;
            }

            throw new InvalidOperationException("Unable to find a built-in shader for Blockiverse material creation.");
        }

        static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            else
                material.color = color;
        }

        static int EnsureInteractionLayer()
        {
            int existingLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

            if (existingLayer >= 0)
                return existingLayer;

            UnityEngine.Object tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")
                .FirstOrDefault();

            if (tagManagerAsset == null)
                return -1;

            var tagManager = new SerializedObject(tagManagerAsset);
            SerializedProperty layers = tagManager.FindProperty("layers");

            if (layers == null)
                return -1;

            for (int layer = 8; layer < layers.arraySize; layer++)
            {
                SerializedProperty layerName = layers.GetArrayElementAtIndex(layer);

                if (!string.IsNullOrEmpty(layerName.stringValue))
                    continue;

                layerName.stringValue = BlockiverseProject.InteractionLayerName;
                tagManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(tagManagerAsset);
                return layer;
            }

            Debug.LogWarning($"No available Unity layer slot for {BlockiverseProject.InteractionLayerName}; interaction objects will stay on their current layer.");
            return -1;
        }

        static LayerMask GetInteractionLayerMask()
        {
            int layer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);
            return layer >= 0 ? (LayerMask)(1 << layer) : Physics.DefaultRaycastLayers;
        }

        static void AddControllerMap(InputActionAsset asset, string mapName, string controllerPath)
        {
            InputActionMap map = asset.AddActionMap(mapName);
            map.AddAction(BlockiverseInputActionNames.Position, InputActionType.PassThrough, $"{controllerPath}/devicePosition", expectedControlLayout: "Vector3");
            map.AddAction(BlockiverseInputActionNames.Rotation, InputActionType.PassThrough, $"{controllerPath}/deviceRotation", expectedControlLayout: "Quaternion");
            map.AddAction(BlockiverseInputActionNames.IsTracked, InputActionType.Button, $"{controllerPath}/isTracked");
            map.AddAction(BlockiverseInputActionNames.TrackingState, InputActionType.PassThrough, $"{controllerPath}/trackingState", expectedControlLayout: "Integer");
            map.AddAction(BlockiverseInputActionNames.Select, InputActionType.Button, $"{controllerPath}/triggerPressed");
            map.AddAction(BlockiverseInputActionNames.Activate, InputActionType.Button, $"{controllerPath}/gripPressed");
            map.AddAction(BlockiverseInputActionNames.UiPress, InputActionType.Button, $"{controllerPath}/triggerPressed");
            map.AddAction(BlockiverseInputActionNames.UiScroll, InputActionType.PassThrough, $"{controllerPath}/thumbstick", expectedControlLayout: "Vector2");
            map.AddAction(BlockiverseInputActionNames.HapticDevice, InputActionType.PassThrough, $"{controllerPath}/*");
            map.AddAction(BlockiverseInputActionNames.Move, InputActionType.PassThrough, $"{controllerPath}/thumbstick", expectedControlLayout: "Vector2");
            map.AddAction(BlockiverseInputActionNames.Turn, InputActionType.PassThrough, $"{controllerPath}/thumbstick", expectedControlLayout: "Vector2");
            map.AddAction(BlockiverseInputActionNames.TeleportMode, InputActionType.Button, $"{controllerPath}/primaryButton");
            map.AddAction(BlockiverseInputActionNames.TeleportSelect, InputActionType.Button, $"{controllerPath}/triggerPressed");
        }

        static void AddGameplayMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(BlockiverseInputActionNames.GameplayMap);
            map.AddAction(BlockiverseInputActionNames.Menu, InputActionType.Button, "<XRController>{LeftHand}/menuButton");
            map.AddAction(BlockiverseInputActionNames.HeightReset, InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
        }

        static XRGeneralSettings EnsureXrGeneralSettings(BuildTargetGroup targetGroup)
        {
            Type settingsType = typeof(XRGeneralSettingsPerBuildTarget);
            MethodInfo getOrCreate = settingsType.GetMethod(
                "GetOrCreate",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (getOrCreate?.Invoke(null, null) is not XRGeneralSettingsPerBuildTarget settingsStore)
                throw new InvalidOperationException("Unable to create XR settings store.");

            if (!settingsStore.HasSettingsForBuildTarget(targetGroup))
                settingsStore.CreateDefaultSettingsForBuildTarget(targetGroup);

            if (!settingsStore.HasManagerSettingsForBuildTarget(targetGroup))
                settingsStore.CreateDefaultManagerSettingsForBuildTarget(targetGroup);

            EditorUtility.SetDirty(settingsStore);
            return settingsStore.SettingsForBuildTarget(targetGroup);
        }

        static GameObject EnsureXrRigPrefab()
        {
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            if (existingPrefab != null)
            {
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(BlockiverseProject.XrRigPrefabPath);

                try
                {
                    EnsureXrRigControllerBindings(prefabContents);
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, BlockiverseProject.XrRigPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }

                return AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            }

            GameObject rig = CreateXrRigInstance();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(rig, BlockiverseProject.XrRigPrefabPath);
            UnityEngine.Object.DestroyImmediate(rig);

            return prefab;
        }

        static void EnsureBootScene()
        {
            GameObject rigPrefab = EnsureXrRigPrefab();
            bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(BlockiverseProject.BootScenePath) != null;
            Scene scene = sceneExists
                ? EditorSceneManager.OpenScene(BlockiverseProject.BootScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureBootSceneRig(scene, rigPrefab);
            EnsureBootSceneLight(scene);
            EnsureBootSceneInteractionTestBlock(scene);

            EditorSceneManager.SaveScene(scene, BlockiverseProject.BootScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BlockiverseProject.BootScenePath, true)
            };
        }

        static void EnsureBootSceneRig(Scene scene, GameObject rigPrefab)
        {
            GameObject rig = FindRootGameObject(scene, BlockiverseProject.XrRigRootName);

            if (rig == null)
            {
                PrefabUtility.InstantiatePrefab(rigPrefab, scene);
                return;
            }

            if (rig.GetComponent<BlockiverseXRRigMarker>() == null)
                rig.AddComponent<BlockiverseXRRigMarker>();

            EnsureXrRigControllerBindings(rig);
        }

        static void EnsureBootSceneLight(Scene scene)
        {
            GameObject lightObject = FindRootGameObject(scene, "Bootstrap Directional Light");

            if (lightObject == null)
            {
                lightObject = new GameObject("Bootstrap Directional Light");
                SceneManager.MoveGameObjectToScene(lightObject, scene);
            }

            Light light = EnsureComponent<Light>(lightObject);
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            lightObject.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);
            EditorUtility.SetDirty(lightObject);
        }

        static void EnsureBootSceneInteractionTestBlock(Scene scene)
        {
            GameObject blockObject = FindRootGameObject(scene, InteractionTestBlockName);

            if (blockObject == null)
            {
                blockObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blockObject.name = InteractionTestBlockName;
                SceneManager.MoveGameObjectToScene(blockObject, scene);
            }

            blockObject.transform.position = new Vector3(0.25f, 1.25f, 2.5f);
            blockObject.transform.rotation = Quaternion.identity;
            blockObject.transform.localScale = Vector3.one * 0.45f;

            int interactionLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

            if (interactionLayer >= 0)
                blockObject.layer = interactionLayer;

            MeshRenderer renderer = EnsureComponent<MeshRenderer>(blockObject);
            EnsureComponent<BoxCollider>(blockObject);

            Material testBlockMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.TestBlockMaterialPath);
            Material highlightMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.HighlightMaterialPath);

            if (testBlockMaterial != null)
                renderer.sharedMaterial = testBlockMaterial;

            BlockiverseHighlightTarget target = EnsureComponent<BlockiverseHighlightTarget>(blockObject);
            target.Configure(renderer, highlightMaterial);

            EditorUtility.SetDirty(blockObject);
            EditorUtility.SetDirty(target);
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

        static GameObject CreateXrRigInstance()
        {
            GameObject rig = new(BlockiverseProject.XrRigRootName);
            rig.AddComponent<BlockiverseXRRigMarker>();
            InputActionAsset inputActions = EnsureInputActions();
            BlockiverseInputRig inputRig = rig.AddComponent<BlockiverseInputRig>();
            inputRig.Configure(inputActions);

            GameObject cameraOffset = new("Camera Offset");
            cameraOffset.transform.SetParent(rig.transform, false);

            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.0f, 1.6f, 0.0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500.0f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<TrackedPoseDriver>();

            XROrigin origin = rig.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            EnsureXrRigLocomotion(rig, inputRig, origin);

            CreateControllerAnchor(
                "Left Controller",
                cameraOffset.transform,
                new Vector3(-0.25f, 1.25f, 0.35f),
                inputRig,
                BlockiverseControllerRole.Left);
            CreateControllerAnchor(
                "Right Controller",
                cameraOffset.transform,
                new Vector3(0.25f, 1.25f, 0.35f),
                inputRig,
                BlockiverseControllerRole.Right);

            EnsureXrRigComfortMenu(rig, inputRig);
            EnsureXrRigInteraction(rig, inputRig);
            return rig;
        }

        static void EnsureXrRigControllerBindings(GameObject rig)
        {
            InputActionAsset inputActions = EnsureInputActions();
            BlockiverseInputRig inputRig = rig.GetComponent<BlockiverseInputRig>();

            if (inputRig == null)
                inputRig = rig.AddComponent<BlockiverseInputRig>();

            inputRig.Configure(inputActions);

            Transform cameraOffset = rig.transform.Find("Camera Offset");

            if (cameraOffset == null)
            {
                GameObject cameraOffsetObject = new("Camera Offset");
                cameraOffsetObject.transform.SetParent(rig.transform, false);
                cameraOffset = cameraOffsetObject.transform;
            }

            XROrigin origin = rig.GetComponent<XROrigin>();

            if (origin == null)
                origin = rig.AddComponent<XROrigin>();

            if (origin.CameraFloorOffsetObject == null)
                origin.CameraFloorOffsetObject = cameraOffset.gameObject;

            if (origin.Camera == null)
                origin.Camera = cameraOffset.GetComponentInChildren<Camera>(true);

            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            EnsureXrRigLocomotion(rig, inputRig, origin);

            EnsureControllerAnchor(
                "Left Controller",
                cameraOffset,
                new Vector3(-0.25f, 1.25f, 0.35f),
                inputRig,
                BlockiverseControllerRole.Left);
            EnsureControllerAnchor(
                "Right Controller",
                cameraOffset,
                new Vector3(0.25f, 1.25f, 0.35f),
                inputRig,
                BlockiverseControllerRole.Right);

            EnsureXrRigComfortMenu(rig, inputRig);
            EnsureXrRigInteraction(rig, inputRig);
        }

        static void EnsureControllerAnchor(
            string name,
            Transform parent,
            Vector3 localPosition,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            Transform existingController = parent.Find(name);

            if (existingController == null)
            {
                CreateControllerAnchor(name, parent, localPosition, inputRig, role);
                return;
            }

            ConfigureControllerAnchor(existingController.gameObject, inputRig, role);
        }

        static void CreateControllerAnchor(
            string name,
            Transform parent,
            Vector3 localPosition,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            GameObject controller = new(name);
            controller.transform.SetParent(parent, false);
            controller.transform.localPosition = localPosition;
            ConfigureControllerAnchor(controller, inputRig, role);
        }

        static void ConfigureControllerAnchor(
            GameObject controller,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            BlockiverseControllerAnchor anchor = controller.GetComponent<BlockiverseControllerAnchor>();

            if (anchor == null)
                anchor = controller.AddComponent<BlockiverseControllerAnchor>();

            anchor.Configure(inputRig, role);

            BlockiverseControllerHaptics haptics = controller.GetComponent<BlockiverseControllerHaptics>();

            if (haptics == null)
                haptics = controller.AddComponent<BlockiverseControllerHaptics>();

            haptics.Configure(role);
        }

        static void EnsureXrRigComfortMenu(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform leftController = rig.transform.Find("Camera Offset/Left Controller");

            if (leftController == null)
                return;

            BlockiverseComfortSettings settings = rig.GetComponent<BlockiverseComfortSettings>();
            BlockiverseHeightReset heightReset = rig.GetComponent<BlockiverseHeightReset>();
            GameObject menuObject = EnsureRectChild(leftController, ComfortMenuName);
            menuObject.transform.localPosition = new Vector3(0.0f, 0.12f, 0.45f);
            menuObject.transform.localRotation = Quaternion.Euler(18.0f, 0.0f, 0.0f);
            menuObject.transform.localScale = Vector3.one * 0.002f;

            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ComfortMenuSize.x);
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ComfortMenuSize.y);

            Canvas canvas = EnsureComponent<Canvas>(menuObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            canvas.enabled = false;

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(menuObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureComponent<GraphicRaycaster>(menuObject);

            GameObject panelObject = EnsureRectChild(menuObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            panelImage.color = ComfortMenuPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Comfort Settings",
                40,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(32.0f, -36.0f),
                new Vector2(460.0f, 56.0f));

            Toggle teleportToggle = EnsureToggleControl(
                panelObject.transform,
                "Teleport Toggle",
                "Teleport",
                settings == null || settings.TeleportEnabled,
                new Vector2(32.0f, -104.0f));

            Toggle smoothTurnToggle = EnsureToggleControl(
                panelObject.transform,
                "Smooth Turn Toggle",
                "Smooth Turn",
                settings != null && settings.SmoothTurnEnabled,
                new Vector2(32.0f, -176.0f));

            Slider snapTurnSlider = EnsureSnapTurnSlider(
                panelObject.transform,
                settings != null ? settings.SnapTurnDegrees : 45.0f,
                new Vector2(32.0f, -254.0f));

            Button heightResetButton = EnsureButtonControl(
                panelObject.transform,
                "Height Reset Button",
                "Reset Height",
                new Vector2(32.0f, -356.0f));

            if (heightReset != null)
            {
                RemovePersistentListeners(
                    heightResetButton.onClick,
                    heightReset,
                    nameof(BlockiverseHeightReset.ResetHeight));
                UnityEventTools.AddPersistentListener(heightResetButton.onClick, heightReset.ResetHeight);
                EditorUtility.SetDirty(heightResetButton);
            }

            BlockiverseComfortMenu menu = EnsureComponent<BlockiverseComfortMenu>(menuObject);
            menu.Configure(canvas, settings);
            menu.ConfigureControls(teleportToggle, smoothTurnToggle, snapTurnSlider);

            if (inputRig != null)
            {
                RemovePersistentListeners(
                    inputRig.MenuPressed,
                    menu,
                    nameof(BlockiverseComfortMenu.ToggleVisible));
                UnityEventTools.AddPersistentListener(inputRig.MenuPressed, menu.ToggleVisible);
                EditorUtility.SetDirty(inputRig);
            }

            EditorUtility.SetDirty(menuObject);
            EditorUtility.SetDirty(menu);
        }

        static void EnsureXrRigInteraction(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform rightController = rig.transform.Find("Camera Offset/Right Controller");
            Transform leftController = rig.transform.Find("Camera Offset/Left Controller");

            if (rightController != null)
                EnsureRayPointer(rightController);

            if (leftController != null)
                EnsureBlockMenuPlaceholder(leftController, inputRig);
        }

        static void EnsureRayPointer(Transform rightController)
        {
            Material pointerMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.PointerLineMaterialPath);
            GameObject lineObject = EnsureChild(rightController, PointerLineName);
            lineObject.transform.localPosition = Vector3.zero;
            lineObject.transform.localRotation = Quaternion.identity;
            lineObject.transform.localScale = Vector3.one;

            LineRenderer lineRenderer = EnsureComponent<LineRenderer>(lineObject);
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = 0.012f;
            lineRenderer.endWidth = 0.006f;
            lineRenderer.numCapVertices = 4;
            lineRenderer.numCornerVertices = 2;

            if (pointerMaterial != null)
                lineRenderer.sharedMaterial = pointerMaterial;

            lineRenderer.SetPosition(0, rightController.position);
            lineRenderer.SetPosition(1, rightController.position + rightController.forward * 5.0f);

            BlockiverseRayPointer pointer = EnsureComponent<BlockiverseRayPointer>(rightController.gameObject);
            pointer.Configure(rightController, lineRenderer, GetInteractionLayerMask(), 5.0f);

            EditorUtility.SetDirty(lineObject);
            EditorUtility.SetDirty(pointer);
        }

        static void EnsureBlockMenuPlaceholder(Transform leftController, BlockiverseInputRig inputRig)
        {
            GameObject menuObject = EnsureRectChild(leftController, BlockMenuName);
            menuObject.transform.localPosition = new Vector3(-0.18f, -0.08f, 0.42f);
            menuObject.transform.localRotation = Quaternion.Euler(18.0f, 24.0f, 0.0f);
            menuObject.transform.localScale = Vector3.one * 0.002f;

            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, BlockMenuSize.x);
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, BlockMenuSize.y);

            Canvas canvas = EnsureComponent<Canvas>(menuObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 12;
            canvas.enabled = false;

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(menuObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureComponent<GraphicRaycaster>(menuObject);

            GameObject panelObject = EnsureRectChild(menuObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            panelImage.color = BlockMenuPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Blocks",
                34,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(24.0f, -32.0f),
                new Vector2(300.0f, 48.0f));

            EnsureBlockMenuSwatch(panelObject.transform, "Swatch A", "Stone", BlockMenuAccentColor, new Vector2(24.0f, -92.0f));
            EnsureBlockMenuSwatch(panelObject.transform, "Swatch B", "Earth", new Color(0.50f, 0.33f, 0.22f, 1.0f), new Vector2(24.0f, -146.0f));
            EnsureBlockMenuSwatch(panelObject.transform, "Swatch C", "Glow", new Color(0.32f, 0.74f, 0.95f, 1.0f), new Vector2(24.0f, -200.0f));

            BlockiverseQuickMenuPlaceholder menu = EnsureComponent<BlockiverseQuickMenuPlaceholder>(menuObject);
            menu.Configure(canvas);

            if (inputRig != null)
            {
                RemovePersistentListeners(
                    inputRig.QuickMenuPressed,
                    menu,
                    nameof(BlockiverseQuickMenuPlaceholder.ToggleVisible));
                UnityEventTools.AddPersistentListener(inputRig.QuickMenuPressed, menu.ToggleVisible);
                EditorUtility.SetDirty(inputRig);
            }

            EditorUtility.SetDirty(menuObject);
            EditorUtility.SetDirty(menu);
        }

        static void EnsureBlockMenuSwatch(
            Transform parent,
            string name,
            string label,
            Color color,
            Vector2 anchoredPosition)
        {
            GameObject rowObject = EnsureRectChild(parent, name);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rowRect, anchoredPosition, new Vector2(300.0f, 42.0f));

            GameObject swatchObject = EnsureRectChild(rowObject.transform, "Swatch");
            RectTransform swatchRect = swatchObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(swatchRect, Vector2.zero, new Vector2(38.0f, 38.0f));
            Image swatchImage = EnsureComponent<Image>(swatchObject);
            swatchImage.color = color;

            Image rowImage = EnsureComponent<Image>(rowObject);
            rowImage.color = BlockMenuControlColor;

            EnsureLabel(
                rowObject.transform,
                "Label",
                label,
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 0.5f),
                new Vector2(52.0f, 0.0f),
                new Vector2(228.0f, 42.0f));
        }

        static Toggle EnsureToggleControl(
            Transform parent,
            string name,
            string label,
            bool isOn,
            Vector2 anchoredPosition)
        {
            GameObject toggleObject = EnsureRectChild(parent, name);
            RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(toggleRect, anchoredPosition, new Vector2(456.0f, 64.0f));

            Toggle toggle = EnsureComponent<Toggle>(toggleObject);

            GameObject backgroundObject = EnsureRectChild(toggleObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(backgroundRect, new Vector2(0.0f, -10.0f), new Vector2(44.0f, 44.0f));
            Image background = EnsureComponent<Image>(backgroundObject);
            background.color = ComfortMenuControlColor;

            GameObject checkmarkObject = EnsureRectChild(backgroundObject.transform, "Checkmark");
            RectTransform checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = Vector2.zero;
            checkmarkRect.anchorMax = Vector2.one;
            checkmarkRect.offsetMin = new Vector2(8.0f, 8.0f);
            checkmarkRect.offsetMax = new Vector2(-8.0f, -8.0f);
            Image checkmark = EnsureComponent<Image>(checkmarkObject);
            checkmark.color = ComfortMenuAccentColor;

            EnsureLabel(
                toggleObject.transform,
                "Label",
                label,
                34,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(64.0f, -4.0f),
                new Vector2(360.0f, 56.0f));

            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.isOn = isOn;
            return toggle;
        }

        static Slider EnsureSnapTurnSlider(Transform parent, float value, Vector2 anchoredPosition)
        {
            GameObject rowObject = EnsureRectChild(parent, "Snap Turn Slider");
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rowRect, anchoredPosition, new Vector2(456.0f, 88.0f));

            EnsureLabel(
                rowObject.transform,
                "Label",
                "Snap Turn",
                32,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(220.0f, 40.0f));

            GameObject sliderObject = EnsureRectChild(rowObject.transform, "Slider");
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, new Vector2(0.0f, -48.0f), new Vector2(420.0f, 32.0f));
            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 15.0f;
            slider.maxValue = 90.0f;
            slider.wholeNumbers = true;

            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 0.35f);
            backgroundRect.anchorMax = new Vector2(1.0f, 0.65f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = EnsureComponent<Image>(backgroundObject);
            background.color = ComfortMenuControlColor;

            GameObject fillAreaObject = EnsureRectChild(sliderObject.transform, "Fill Area");
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(8.0f, 0.0f);
            fillAreaRect.offsetMax = new Vector2(-8.0f, 0.0f);

            GameObject fillObject = EnsureRectChild(fillAreaObject.transform, "Fill");
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0.0f, 0.35f);
            fillRect.anchorMax = new Vector2(1.0f, 0.65f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = EnsureComponent<Image>(fillObject);
            fill.color = ComfortMenuAccentColor;

            GameObject handleAreaObject = EnsureRectChild(sliderObject.transform, "Handle Slide Area");
            RectTransform handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(8.0f, 0.0f);
            handleAreaRect.offsetMax = new Vector2(-8.0f, 0.0f);

            GameObject handleObject = EnsureRectChild(handleAreaObject.transform, "Handle");
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0.0f, 0.5f);
            handleRect.anchorMax = new Vector2(0.0f, 0.5f);
            handleRect.sizeDelta = new Vector2(32.0f, 32.0f);
            Image handle = EnsureComponent<Image>(handleObject);
            handle.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.value = value;
            return slider;
        }

        static Button EnsureButtonControl(Transform parent, string name, string label, Vector2 anchoredPosition)
        {
            GameObject buttonObject = EnsureRectChild(parent, name);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(buttonRect, anchoredPosition, new Vector2(220.0f, 54.0f));

            Image image = EnsureComponent<Image>(buttonObject);
            image.color = ComfortMenuControlColor;

            Button button = EnsureComponent<Button>(buttonObject);
            button.targetGraphic = image;

            EnsureLabel(
                buttonObject.transform,
                "Label",
                label,
                28,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            Text buttonLabel = buttonObject.transform.Find("Label").GetComponent<Text>();
            RectTransform labelRect = buttonLabel.GetComponent<RectTransform>();
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return button;
        }

        static Text EnsureLabel(
            Transform parent,
            string name,
            string label,
            int fontSize,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            GameObject labelObject = EnsureRectChild(parent, name);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = anchorMin;
            labelRect.anchorMax = anchorMax;
            labelRect.pivot = pivot;
            labelRect.anchoredPosition = anchoredPosition;
            labelRect.sizeDelta = size;

            Text text = EnsureComponent<Text>(labelObject);
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        static GameObject EnsureRectChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);

            if (existing != null)
                return existing.gameObject;

            GameObject child = new(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }

        static GameObject EnsureChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);

            if (existing != null)
                return existing.gameObject;

            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();

            if (component == null)
                component = gameObject.AddComponent<T>();

            return component;
        }

        static void ConfigureTopLeftRect(RectTransform rectTransform, Vector2 anchoredPosition, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(0.0f, 1.0f);
            rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
            rectTransform.pivot = new Vector2(0.0f, 1.0f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
        }

        static void RemovePersistentListeners(UnityEvent unityEvent, UnityEngine.Object target, string methodName)
        {
            for (int index = unityEvent.GetPersistentEventCount() - 1; index >= 0; index--)
            {
                if (unityEvent.GetPersistentTarget(index) == target &&
                    unityEvent.GetPersistentMethodName(index) == methodName)
                {
                    UnityEventTools.RemovePersistentListener(unityEvent, index);
                }
            }
        }

        static void EnsureXrRigLocomotion(GameObject rig, BlockiverseInputRig inputRig, XROrigin origin)
        {
            BlockiverseComfortSettings settings = rig.GetComponent<BlockiverseComfortSettings>();

            if (settings == null)
                settings = rig.AddComponent<BlockiverseComfortSettings>();

            if (origin != null)
                origin.CameraYOffset = settings.StandingEyeHeight;

            BlockiverseTeleportLocomotion teleport = rig.GetComponent<BlockiverseTeleportLocomotion>();

            if (teleport == null)
                teleport = rig.AddComponent<BlockiverseTeleportLocomotion>();

            teleport.Configure(origin, settings);

            BlockiverseSnapTurnLocomotion snapTurn = rig.GetComponent<BlockiverseSnapTurnLocomotion>();

            if (snapTurn == null)
                snapTurn = rig.AddComponent<BlockiverseSnapTurnLocomotion>();

            snapTurn.Configure(origin, settings);

            BlockiverseHeightReset heightReset = rig.GetComponent<BlockiverseHeightReset>();

            if (heightReset == null)
                heightReset = rig.AddComponent<BlockiverseHeightReset>();

            heightReset.Configure(origin, settings);
            inputRig.ConfigureLocomotion(teleport, snapTurn, heightReset);
        }
    }
}

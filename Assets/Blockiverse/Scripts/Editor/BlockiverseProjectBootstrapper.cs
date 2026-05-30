using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using Blockiverse.UI;
using Blockiverse.VR;
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
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

namespace Blockiverse.Editor
{
    public static class BlockiverseProjectBootstrapper
    {
        static readonly string[] RequiredFolders =
        {
            "Assets/Blockiverse/Art",
            BlockiverseProject.BrandingArtFolderPath,
            "Assets/Blockiverse/Audio",
            "Assets/Blockiverse/Materials",
            "Assets/Blockiverse/Prefabs",
            "Assets/Blockiverse/Prefabs/Networking",
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
        const string BlockMenuName = "Block Menu";
        const string SurvivalHudName = "Survival HUD";
        const string ControllerMappingPopupName = "Controller Mapping Popup";
        const string StartupLoadingOverlayName = "Startup Loading Overlay";
        const string MultiplayerSessionMenuName = "Multiplayer Session Menu";
        const string BootEventSystemName = "Boot Event System";
        const string MultiplayerEventSystemName = "Multiplayer Event System";
        const string XrInteractionManagerName = "XR Interaction Manager";
        const string MultiplayerTestCameraName = "Multiplayer Test Camera";
        const string NetworkManagerRootName = "Blockiverse Network Manager";
        const string NetworkPlayerPrefabName = "Blockiverse Network Player";
        const string DefaultNetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        const string PointerLineName = "Ray Pointer Line";
        const string InteractionRayName = "Interaction Ray";
        const string TeleportRayName = "Teleport Ray";
        const string TunnelingVignetteName = "Tunneling Vignette";
        const string TunnelingVignettePrefabPath = "Assets/Blockiverse/VR/TunnelingVignette/TunnelingVignette.prefab";
        const string InteractionTestBlockName = "Interaction Test Block";
        static readonly Vector2 ComfortMenuSize = new(520.0f, 420.0f);
        static readonly Vector2 BlockMenuSize = new(360.0f, 260.0f);
        static readonly Vector2 SurvivalHudSize = new(720.0f, 420.0f);
        static readonly Vector2 ControllerMappingPopupSize = new(620.0f, 420.0f);
        static readonly Vector2 StartupLoadingOverlaySize = new(980.0f, 552.0f);
        static readonly Vector2 MultiplayerSessionMenuSize = new(560.0f, 380.0f);
        static readonly Color ComfortMenuPanelColor = new(0.07f, 0.08f, 0.09f, 0.92f);
        static readonly Color ComfortMenuControlColor = new(0.18f, 0.21f, 0.24f, 1.0f);
        static readonly Color ComfortMenuAccentColor = new(0.19f, 0.72f, 0.54f, 1.0f);
        static readonly Color BlockMenuPanelColor = new(0.05f, 0.12f, 0.16f, 0.94f);
        static readonly Color BlockMenuControlColor = new(0.18f, 0.31f, 0.36f, 1.0f);
        static readonly Color BlockMenuAccentColor = new(0.94f, 0.72f, 0.26f, 1.0f);
        static readonly Color SurvivalHudPanelColor = new(0.06f, 0.08f, 0.10f, 0.90f);
        static readonly Color SurvivalHudSectionColor = new(0.13f, 0.18f, 0.20f, 0.95f);
        static readonly Color SurvivalHudAccentColor = new(0.21f, 0.75f, 0.57f, 1.0f);
        static readonly Color StartupOverlayPanelColor = new(0.02f, 0.04f, 0.05f, 0.96f);
        static readonly Color MultiplayerMenuPanelColor = new(0.07f, 0.09f, 0.10f, 0.94f);
        static readonly Color MultiplayerMenuInputColor = new(0.11f, 0.16f, 0.18f, 1.0f);
        static readonly Color PointerLineColor = new(0.36f, 0.82f, 1.0f, 0.92f);
        static readonly Color HighlightColor = new(1.0f, 0.85f, 0.18f, 1.0f);
        static readonly Color TestBlockColor = new(0.22f, 0.56f, 0.43f, 1.0f);

        [MenuItem("Blockiverse/Bootstrap Unity Quest Project")]
        public static void Run()
        {
            EnsureFolders();
            ConfigureEditorSerialization();
            ConfigureAndroidPlayer();
            ConfigureAppBranding();
            ConfigureMetaProjectSettings();
            ConfigureAndroidManifest();
            ConfigureMetaRuntimeSettings();
            ConfigureUniversalRenderPipeline();
            ConfigureOpenXrForAndroid();
            EnsureInteractionLayer();
            EnsureInteractionMaterials();
            EnsureInputActions();
            EnsureXrRigPrefab();
            EnsureNetworkFoundationAssets();
            EnsureBootScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, "Blockiverse Unity/Quest bootstrap complete.");
        }

        [MenuItem("Blockiverse/Bootstrap M5 Network Foundation")]
        public static void EnsureNetworkFoundationAssets()
        {
            EnsureFolders();
            DisableNetcodeDefaultNetworkPrefabs();
            GameObject playerPrefab = EnsureNetworkPlayerPrefab();
            GameObject networkManagerPrefab = EnsureNetworkManagerPrefab(playerPrefab);
            EnsureMultiplayerTestScene(networkManagerPrefab);
            EnsureBuildScenes();
            RemoveGeneratedDefaultNetworkPrefabs();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void DisableNetcodeDefaultNetworkPrefabs()
        {
            NetcodeForGameObjectsProjectSettings settings = NetcodeForGameObjectsProjectSettings.instance;
            settings.GenerateDefaultNetworkPrefabs = false;

            MethodInfo saveSettings = typeof(NetcodeForGameObjectsProjectSettings).GetMethod(
                "SaveSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            saveSettings?.Invoke(settings, null);
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

        static void ConfigureAppBranding()
        {
            EnsureAndroidStringResources();

            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockiverseProject.AppIconPath);

            if (icon == null)
            {
                BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, $"App icon asset is missing: {BlockiverseProject.AppIconPath}");
                return;
            }

            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, new[] { icon });
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
        }

        static void EnsureAndroidStringResources()
        {
            string libraryManifestPath = BlockiverseProject.AndroidBrandingLibraryPath + "/AndroidManifest.xml";
            string libraryGradlePath = BlockiverseProject.AndroidBrandingLibraryPath + "/build.gradle";
            string valuesDirectory = BlockiverseProject.AndroidBrandingLibraryPath + "/res/values";

            Directory.CreateDirectory(BlockiverseProject.AndroidBrandingLibraryPath);
            Directory.CreateDirectory(valuesDirectory);
            File.WriteAllText(
                libraryManifestPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" />\n");
            File.WriteAllText(
                libraryGradlePath,
                "apply plugin: 'com.android.library'\n\n" +
                "dependencies {\n" +
                "    implementation fileTree(dir: 'bin', include: ['*.jar'])\n" +
                "    implementation fileTree(dir: 'libs', include: ['*.jar'])\n" +
                "}\n\n" +
                "android {\n" +
                "    namespace 'dev.ericslutz.blockiversevr.branding'\n" +
                "    compileSdk 34\n" +
                "    buildToolsVersion = '36.0.0'\n\n" +
                "    defaultConfig {\n" +
                "        minSdk 32\n" +
                "        targetSdk 34\n" +
                "    }\n\n" +
                "    lint {\n" +
                "        abortOnError false\n" +
                "    }\n\n" +
                "    sourceSets {\n" +
                "        main {\n" +
                "            manifest.srcFile 'AndroidManifest.xml'\n" +
                "            res.srcDirs = ['res']\n" +
                "            assets.srcDirs = ['assets']\n" +
                "            jniLibs.srcDirs = ['libs']\n" +
                "        }\n" +
                "    }\n" +
                "}\n");
            File.WriteAllText(
                BlockiverseProject.AndroidAppStringsPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<resources>\n" +
                $"    <string name=\"app_name\">{BlockiverseProject.ProductName}</string>\n" +
                $"    <string name=\"game_view_content_description\">{BlockiverseProject.ProductName}</string>\n" +
                "</resources>\n");
            AssetDatabase.ImportAsset(libraryManifestPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(libraryGradlePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(BlockiverseProject.AndroidAppStringsPath, ImportAssetOptions.ForceSynchronousImport);
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
                BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, "OpenXR loader was already assigned or could not be reassigned for Android.");
            }

            androidSettings.Manager.automaticLoading = true;
            androidSettings.Manager.automaticRunning = true;
            EditorUtility.SetDirty(androidSettings.Manager);

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
                    BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, $"OpenXR feature was not found for Android: {featureId}");
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
            {
                EnsureInputActionSchema(existingAsset);
                return existingAsset;
            }

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

        static void EnsureInputActionSchema(InputActionAsset asset)
        {
            InputActionMap gameplayMap = asset.FindActionMap(BlockiverseInputActionNames.GameplayMap, throwIfNotFound: false);

            if (gameplayMap == null)
            {
                AddGameplayMap(asset);
                EditorUtility.SetDirty(asset);
                return;
            }

            EnsureButtonAction(
                gameplayMap,
                BlockiverseInputActionNames.Menu,
                "<XRController>{LeftHand}/menuButton");
            EnsureButtonAction(
                gameplayMap,
                BlockiverseInputActionNames.HeightReset,
                "<XRController>{LeftHand}/primaryButton");
            EnsureButtonAction(
                gameplayMap,
                BlockiverseInputActionNames.Undo,
                "<XRController>{LeftHand}/secondaryButton");
            EditorUtility.SetDirty(asset);
        }

        static void EnsureButtonAction(InputActionMap map, string actionName, string bindingPath)
        {
            InputAction action = map.FindAction(actionName, throwIfNotFound: false);

            if (action == null)
                action = map.AddAction(actionName, InputActionType.Button, bindingPath);

            bool hasBinding = action.bindings.Any(binding => binding.path == bindingPath);

            if (!hasBinding)
                action.AddBinding(bindingPath);
        }

        static void EnsureInteractionMaterials()
        {
            EnsureMaterial(BlockiverseProject.PointerLineMaterialPath, PointerLineColor, preferUnlit: true);
            EnsureMaterial(BlockiverseProject.HighlightMaterialPath, HighlightColor, preferUnlit: false);
            EnsureBlockTextureMaterial();
        }

        static void EnsureBlockTextureMaterial()
        {
            Material material = EnsureMaterial(BlockiverseProject.TestBlockMaterialPath, Color.white, preferUnlit: false);
            Texture2D authoredAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockVisualAtlas.AuthoredAtlasPath);

            if (authoredAtlas != null)
            {
                SetMaterialTexture(material, authoredAtlas);
                SetMaterialColor(material, Color.white);
            }
            else
            {
                SetMaterialColor(material, TestBlockColor);
            }

            EditorUtility.SetDirty(material);
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

        static void SetMaterialTexture(Material material, Texture texture)
        {
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);

            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
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

            BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, $"No available Unity layer slot for {BlockiverseProject.InteractionLayerName}; interaction objects will stay on their current layer.");
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
            map.AddAction(BlockiverseInputActionNames.Undo, InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
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
            EnsureBootEventSystem(scene);
            EnsureBootSceneCreativeWorld(scene);
            RemoveRootGameObject(scene, InteractionTestBlockName);

            EditorSceneManager.SaveScene(scene, BlockiverseProject.BootScenePath);
            EnsureBuildScenes();
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
            networkManager.NetworkConfig.ConnectionApproval = false;
            networkManager.NetworkConfig.TickRate = 30;

            EnsureComponent<BlockiverseNetworkSession>(managerObject);
            EnsureComponent<BlockiverseNetworkBootstrap>(managerObject);
            EnsureComponent<MultiplayerChunkAuthoritySync>(managerObject);
            EnsureComponent<MultiplayerWorldPersistence>(managerObject);

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
            EnsureMultiplayerSessionMenu(scene, managerObject);

            EditorSceneManager.SaveScene(scene, BlockiverseProject.MultiplayerTestScenePath);
        }

        static void EnsureBuildScenes()
        {
            var requiredScenes = new[]
            {
                BlockiverseProject.BootScenePath,
                BlockiverseProject.MultiplayerTestScenePath
            }
                .Where(path => AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToList();

            var existingNonRequiredScenes = EditorBuildSettings.scenes
                .Where(scene => !string.IsNullOrWhiteSpace(scene.path))
                .Where(scene => requiredScenes.All(requiredScene => requiredScene.path != scene.path))
                .GroupBy(scene => scene.path)
                .Select(group => group.First())
                .ToList();

            EditorBuildSettings.scenes = requiredScenes
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

        static void EnsureBootEventSystem(Scene scene)
        {
            EnsureEventSystem(scene, BootEventSystemName);
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

            InputField addressInput = EnsureInputFieldControl(
                panelObject.transform,
                "Address Input",
                "Host address",
                BlockiverseNetworkConfig.DefaultAddress,
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

            Text statusText = EnsureLabel(
                panelObject.transform,
                "Status",
                $"LAN session stopped. Join address defaults to {BlockiverseNetworkConfig.DefaultAddress}.",
                24,
                TextAnchor.UpperLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(28.0f, -258.0f),
                new Vector2(500.0f, 88.0f));

            BlockiverseMultiplayerSessionMenu menu = EnsureComponent<BlockiverseMultiplayerSessionMenu>(menuObject);
            menu.Configure(managerObject != null ? managerObject.GetComponent<BlockiverseNetworkSession>() : null);
            menu.ConfigureControls(hostButton, joinButton, stopButton, addressInput, statusText);

            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(menuObject);
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

            int interactionLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

            if (interactionLayer >= 0)
                worldObject.layer = interactionLayer;

            Material worldMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.TestBlockMaterialPath);
            VoxelWorldRenderer renderer = EnsureComponent<VoxelWorldRenderer>(worldObject);
            CreativeInteractionController controller = EnsureComponent<CreativeInteractionController>(worldObject);
            CreativeWorldManager manager = EnsureComponent<CreativeWorldManager>(worldObject);
            CreativeHotbar hotbar = FindBootSceneHotbar(scene);
            manager.Configure(worldMaterial, interactionLayer, controller, hotbar);

            BlockiverseCreativeInputBridge staleWorldBridge = worldObject.GetComponent<BlockiverseCreativeInputBridge>();

            if (staleWorldBridge != null)
                UnityEngine.Object.DestroyImmediate(staleWorldBridge);

            EnsureCreativeInputBridge(scene, controller);

            EditorUtility.SetDirty(worldObject);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(manager);
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
            TrackedPoseDriver poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            BlockiverseInputRig.ConfigureHeadPoseDriverActions(poseDriver);

            XROrigin origin = rig.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            inputRig.ConfigureHeadPoseDriver(poseDriver);
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

            EnsureXrRigAvatar(rig);
            EnsureXrRigComfortMenu(rig, inputRig);
            EnsureXrRigInteraction(rig, inputRig);
            EnsureXrRigTunnelingVignette(rig);
            EnsureXrRigStartupLoadingOverlay(rig);
            EnsureXrRigControllerMappingPopup(rig);
            EnsureXrRigSurvivalHud(rig);
            EnsureXrRigCreativeInputBridge(rig, inputRig);
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

            Camera xrCamera = origin.Camera;
            TrackedPoseDriver poseDriver = xrCamera != null
                ? xrCamera.GetComponent<TrackedPoseDriver>()
                : rig.GetComponentInChildren<TrackedPoseDriver>(true);

            if (poseDriver == null && xrCamera != null)
                poseDriver = xrCamera.gameObject.AddComponent<TrackedPoseDriver>();

            BlockiverseInputRig.ConfigureHeadPoseDriverActions(poseDriver);
            inputRig.ConfigureHeadPoseDriver(poseDriver);
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

            EnsureXrRigAvatar(rig);
            EnsureXrRigComfortMenu(rig, inputRig);
            EnsureXrRigInteraction(rig, inputRig);
            EnsureXrRigTunnelingVignette(rig);
            EnsureXrRigStartupLoadingOverlay(rig);
            EnsureXrRigControllerMappingPopup(rig);
            EnsureXrRigSurvivalHud(rig);
            EnsureXrRigCreativeInputBridge(rig, inputRig);
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
            // Native controller tracking: a TrackedPoseDriver drives the controller transform in
            // Update + BeforeRender, matching the head and removing the old hand-written pose.
            TrackedPoseDriver poseDriver = EnsureComponent<TrackedPoseDriver>(controller);
            BlockiverseInputRig.ConfigureControllerPoseDriverActions(poseDriver, role);
            poseDriver.enabled = true;

            BlockiverseControllerAnchor anchor = EnsureComponent<BlockiverseControllerAnchor>(controller);
            anchor.Configure(role, poseDriver);

            BlockiverseControllerHaptics haptics = EnsureComponent<BlockiverseControllerHaptics>(controller);
            haptics.Configure(role);

            EnsureControllerInteractors(controller, inputRig, role);

            EditorUtility.SetDirty(poseDriver);
            EditorUtility.SetDirty(anchor);
            EditorUtility.SetDirty(haptics);
        }

        // Builds the native interaction (UI + block targeting) and teleport rays on the right
        // controller, plus the mediator that switches between them while Teleport Mode is held.
        static XRRayInteractor EnsureControllerInteractors(
            GameObject controller,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            if (role != BlockiverseControllerRole.Right)
                return null;

            Material pointerMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.PointerLineMaterialPath);

            GameObject interactionRayObject = EnsureChild(controller.transform, InteractionRayName);
            interactionRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            interactionRayObject.SetActive(true);

            XRRayInteractor interactionRay = EnsureComponent<XRRayInteractor>(interactionRayObject);
            interactionRay.lineType = XRRayInteractor.LineType.StraightLine;
            interactionRay.enableUIInteraction = true;
            interactionRay.manipulateAttachTransform = false;
            // Empty interaction layers: the ray never selects 3D interactables (incl. the chunk
            // TeleportationArea). UI still works (separate path) and block targeting uses the
            // physics raycast via TryGetCurrent3DRaycastHit on this raycast mask.
            interactionRay.interactionLayers = 0;
            interactionRay.raycastMask = GetInteractionLayerMask();
            interactionRay.uiPressInput = MakeButtonReader("UI Press", FindRigAction(inputRig, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.UiPress));
            interactionRay.uiScrollInput = MakeVector2Reader("UI Scroll", FindRigAction(inputRig, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.UiScroll));
            ConfigureLineVisual(interactionRayObject, pointerMaterial);

            GameObject teleportRayObject = EnsureChild(controller.transform, TeleportRayName);
            teleportRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            XRRayInteractor teleportRay = EnsureComponent<XRRayInteractor>(teleportRayObject);
            teleportRay.lineType = XRRayInteractor.LineType.ProjectileCurve;
            teleportRay.enableUIInteraction = false;
            teleportRay.manipulateAttachTransform = false;
            teleportRay.raycastMask = GetInteractionLayerMask();
            teleportRay.selectInput = MakeButtonReader("Teleport Select", FindRigAction(inputRig, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.TeleportSelect));
            ConfigureLineVisual(teleportRayObject, pointerMaterial);
            teleportRayObject.SetActive(false);

            BlockiverseLocomotionRayMediator mediator = EnsureComponent<BlockiverseLocomotionRayMediator>(controller);
            mediator.Configure(inputRig, interactionRay, teleportRay, role);

            EditorUtility.SetDirty(interactionRay);
            EditorUtility.SetDirty(teleportRay);
            EditorUtility.SetDirty(mediator);
            return interactionRay;
        }

        static void ConfigureLineVisual(GameObject rayObject, Material pointerMaterial)
        {
            LineRenderer lineRenderer = EnsureComponent<LineRenderer>(rayObject);
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            if (pointerMaterial != null)
                lineRenderer.sharedMaterial = pointerMaterial;

            XRInteractorLineVisual lineVisual = EnsureComponent<XRInteractorLineVisual>(rayObject);
            lineVisual.lineWidth = 0.01f;
            lineVisual.overrideInteractorLineLength = false;
            lineVisual.stopLineAtFirstRaycastHit = true;

            EditorUtility.SetDirty(lineRenderer);
            EditorUtility.SetDirty(lineVisual);
        }

        static XRInputButtonReader MakeButtonReader(string name, InputAction action)
        {
            var reader = new XRInputButtonReader(name, inputSourceMode: XRInputButtonReader.InputSourceMode.InputAction);

            if (action != null)
                reader.inputActionPerformed = action;

            return reader;
        }

        static XRInputValueReader<Vector2> MakeVector2Reader(string name, InputAction action)
        {
            if (action == null)
                return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.Unused);

            return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.InputAction)
            {
                inputAction = action
            };
        }

        static InputAction FindRigAction(BlockiverseInputRig inputRig, string mapName, string actionName)
        {
            InputActionAsset asset = inputRig != null ? inputRig.InputActions : null;
            InputActionMap map = asset?.FindActionMap(mapName, throwIfNotFound: false);
            return map?.FindAction(actionName, throwIfNotFound: false);
        }

        static void EnsureXrRigAvatar(GameObject rig)
        {
            BlockiverseNetworkAvatarRig avatarRig = EnsureComponent<BlockiverseNetworkAvatarRig>(rig);
            MetaHorizonAvatarProvider avatarProvider = EnsureComponent<MetaHorizonAvatarProvider>(rig);
            BlockiverseMetaAvatarPresenter avatarPresenter = EnsureComponent<BlockiverseMetaAvatarPresenter>(rig);
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;
            Transform leftHand = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform rightHand = cameraOffset != null ? cameraOffset.Find("Right Controller") : null;

            avatarRig.ConfigureTrackingSources(head, leftHand, rightHand);
            avatarRig.SetMetaAvatarAvailable(false);
            avatarRig.ConfigureFallbackProxy(true);
            avatarPresenter.Configure(
                avatarProvider,
                avatarRig,
                head,
                leftHand,
                rightHand,
                MetaAvatarPresentationMode.LocalFirstPerson);
            EditorUtility.SetDirty(avatarRig);
            EditorUtility.SetDirty(avatarProvider);
            EditorUtility.SetDirty(avatarPresenter);
        }

        static void EnsureXrRigComfortMenu(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform leftController = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            BlockiverseComfortSettings settings = rig.GetComponent<BlockiverseComfortSettings>();
            BlockiverseHeightReset heightReset = rig.GetComponent<BlockiverseHeightReset>();
            GameObject menuObject = EnsureRectChildMigrated(cameraOffset, leftController, ComfortMenuName);
            menuObject.transform.localPosition = new Vector3(0.0f, 1.42f, 1.18f);
            menuObject.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
            menuObject.transform.localScale = Vector3.one * 0.002f;

            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ComfortMenuSize.x);
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ComfortMenuSize.y);

            Canvas canvas = EnsureComponent<Canvas>(menuObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

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
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(menuObject);
            presenter.Configure(canvas, head, 1.18f, 0.0f, -0.1f, 0.0f);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            if (inputRig != null)
            {
                RemovePersistentListeners(
                    inputRig.MenuPressed,
                    menu,
                    nameof(BlockiverseComfortMenu.ToggleVisible));
                RemovePersistentListeners(
                    inputRig.MenuPressed,
                    presenter,
                    nameof(BlockiverseWorldSpacePanelPresenter.ToggleVisible));
                UnityEventTools.AddPersistentListener(inputRig.MenuPressed, presenter.ToggleVisible);
                EditorUtility.SetDirty(inputRig);
            }

            EditorUtility.SetDirty(menuObject);
            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(presenter);
        }

        static void EnsureXrRigTunnelingVignette(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform headCamera = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (headCamera == null)
                return;

            Transform existing = headCamera.Find(TunnelingVignetteName);
            TunnelingVignetteController controller = existing != null
                ? existing.GetComponent<TunnelingVignetteController>()
                : null;

            if (controller == null)
            {
                GameObject vignettePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TunnelingVignettePrefabPath);

                if (vignettePrefab == null)
                {
                    BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, $"Tunneling vignette prefab not found at {TunnelingVignettePrefabPath}; skipping comfort vignette.");
                    return;
                }

                var vignetteInstance = (GameObject)PrefabUtility.InstantiatePrefab(vignettePrefab);
                vignetteInstance.transform.SetParent(headCamera, false);
                vignetteInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                vignetteInstance.name = TunnelingVignetteName;
                PrefabUtility.UnpackPrefabInstance(vignetteInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                controller = vignetteInstance.GetComponent<TunnelingVignetteController>();
            }

            if (controller == null)
                return;

            // Ease the comfort vignette in/out during every locomotion type that causes vection.
            controller.locomotionVignetteProviders.Clear();
            AddVignetteProvider(controller, rig.GetComponent<ContinuousMoveProvider>());
            AddVignetteProvider(controller, rig.GetComponent<ContinuousTurnProvider>());
            AddVignetteProvider(controller, rig.GetComponent<SnapTurnProvider>());
            AddVignetteProvider(controller, rig.GetComponent<TeleportationProvider>());

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(controller.gameObject);
        }

        static void AddVignetteProvider(TunnelingVignetteController controller, LocomotionProvider provider)
        {
            if (provider == null)
                return;

            controller.locomotionVignetteProviders.Add(new LocomotionVignetteProvider
            {
                locomotionProvider = provider,
                enabled = true,
            });
        }

        static void EnsureXrRigInteraction(GameObject rig, BlockiverseInputRig inputRig)
        {
            // The native XRRayInteractor (built alongside the controller anchor) replaces the old
            // custom ray pointer + UI pointer; strip any stale objects/scripts from older prefabs.
            RemoveStaleRayPointer(rig);
            EnsureBlockMenuPlaceholder(rig, inputRig);
        }

        static void RemoveStaleRayPointer(GameObject rig)
        {
            Transform staleLine = rig.transform.Find("Camera Offset/Right Controller/" + PointerLineName);

            if (staleLine != null)
                UnityEngine.Object.DestroyImmediate(staleLine.gameObject);

            foreach (Transform child in rig.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
        }

        static XRRayInteractor FindInteractionRay(GameObject rig)
        {
            Transform rayTransform = rig.transform.Find("Camera Offset/Right Controller/" + InteractionRayName);
            return rayTransform != null ? rayTransform.GetComponent<XRRayInteractor>() : null;
        }

        static void EnsureXrRigCreativeInputBridge(GameObject rig, BlockiverseInputRig inputRig)
        {
            XRRayInteractor interactionRay = FindInteractionRay(rig);
            BlockiverseCreativeInputBridge bridge = EnsureComponent<BlockiverseCreativeInputBridge>(rig);
            bridge.Configure(inputRig, interactionRay, null);
            EditorUtility.SetDirty(bridge);
        }

        static void EnsureBlockMenuPlaceholder(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform leftController = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            GameObject menuObject = EnsureRectChildMigrated(cameraOffset, leftController, BlockMenuName);
            menuObject.transform.localPosition = new Vector3(-0.34f, 1.32f, 1.12f);
            menuObject.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
            menuObject.transform.localScale = Vector3.one * 0.002f;

            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, BlockMenuSize.x);
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, BlockMenuSize.y);

            Canvas canvas = EnsureComponent<Canvas>(menuObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 12;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

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

            Text selectedLabel = EnsureLabel(
                panelObject.transform,
                "Selected Block",
                "Meadow Turf",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(24.0f, -82.0f),
                new Vector2(300.0f, 34.0f));

            EnsureBlockMenuSwatch(panelObject.transform, "Swatch A", "Meadow Turf", BlockMenuAccentColor, new Vector2(24.0f, -128.0f));
            EnsureBlockMenuSwatch(panelObject.transform, "Swatch B", "Loam", new Color(0.50f, 0.33f, 0.22f, 1.0f), new Vector2(24.0f, -182.0f));
            EnsureBlockMenuSwatch(panelObject.transform, "Swatch C", "Clearstone", new Color(0.32f, 0.74f, 0.95f, 1.0f), new Vector2(24.0f, -236.0f));

            CreativeHotbar menu = EnsureComponent<CreativeHotbar>(menuObject);
            menu.ConfigureDefault(selectedLabel);
            menu.ConfigureCanvas(canvas);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(menuObject);
            presenter.Configure(canvas, head, 1.12f, -0.34f, -0.18f, 0.0f);
            presenter.ConfigureFeedback(BlockiverseAudioCue.InventoryOpen, BlockiverseAudioCue.InventoryClose);

            if (inputRig != null)
            {
                RemovePersistentListeners(
                    inputRig.QuickMenuPressed,
                    menu,
                    nameof(CreativeHotbar.ToggleVisible));
                RemovePersistentListeners(
                    inputRig.QuickMenuPressed,
                    presenter,
                    nameof(BlockiverseWorldSpacePanelPresenter.ToggleVisible));
                UnityEventTools.AddPersistentListener(inputRig.QuickMenuPressed, presenter.ToggleVisible);
                EditorUtility.SetDirty(inputRig);
            }

            EditorUtility.SetDirty(menuObject);
            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(presenter);
        }

        static void EnsureXrRigStartupLoadingOverlay(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            GameObject overlayObject = EnsureRectChild(cameraOffset, StartupLoadingOverlayName);
            overlayObject.transform.localPosition = new Vector3(0.0f, 1.46f, 1.0f);
            overlayObject.transform.localRotation = Quaternion.identity;
            overlayObject.transform.localScale = Vector3.one * 0.00165f;

            RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
            overlayRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, StartupLoadingOverlaySize.x);
            overlayRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, StartupLoadingOverlaySize.y);

            Canvas canvas = EnsureComponent<Canvas>(overlayObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(overlayObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(overlayObject);

            GameObject artworkObject = EnsureRectChild(overlayObject.transform, "Artwork");
            RectTransform artworkRect = artworkObject.GetComponent<RectTransform>();
            artworkRect.anchorMin = Vector2.zero;
            artworkRect.anchorMax = Vector2.one;
            artworkRect.offsetMin = Vector2.zero;
            artworkRect.offsetMax = Vector2.zero;

            RawImage artworkImage = EnsureComponent<RawImage>(artworkObject);
            artworkImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockiverseProject.LaunchArtworkPath);
            artworkImage.color = Color.white;

            GameObject tintObject = EnsureRectChild(overlayObject.transform, "Title Tint");
            RectTransform tintRect = tintObject.GetComponent<RectTransform>();
            tintRect.anchorMin = new Vector2(0.0f, 0.0f);
            tintRect.anchorMax = new Vector2(1.0f, 0.38f);
            tintRect.offsetMin = Vector2.zero;
            tintRect.offsetMax = Vector2.zero;
            Image tintImage = EnsureComponent<Image>(tintObject);
            tintImage.color = StartupOverlayPanelColor;

            EnsureLabel(
                overlayObject.transform,
                "Title",
                BlockiverseProject.ProductName,
                72,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(58.0f, 118.0f),
                new Vector2(720.0f, 92.0f));

            EnsureLabel(
                overlayObject.transform,
                "Subtitle",
                "Survive, craft, and shape the world.",
                30,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(62.0f, 72.0f),
                new Vector2(720.0f, 48.0f));

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(overlayObject);
            presenter.Configure(
                canvas,
                head,
                1.0f,
                0.0f,
                -0.14f,
                0.0f,
                0.00165f,
                showWhenStarted: true);

            BlockiverseStartupOverlay startupOverlay = EnsureComponent<BlockiverseStartupOverlay>(overlayObject);
            startupOverlay.Configure(canvas, presenter, 2.25f, automaticHide: true);

            EditorUtility.SetDirty(artworkImage);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(startupOverlay);
            EditorUtility.SetDirty(overlayObject);
        }

        static void EnsureXrRigControllerMappingPopup(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            GameObject popupObject = EnsureRectChild(cameraOffset, ControllerMappingPopupName);
            popupObject.transform.localPosition = new Vector3(0.0f, 1.42f, 1.06f);
            popupObject.transform.localRotation = Quaternion.identity;
            popupObject.transform.localScale = Vector3.one * 0.00185f;

            RectTransform popupRect = popupObject.GetComponent<RectTransform>();
            popupRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ControllerMappingPopupSize.x);
            popupRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ControllerMappingPopupSize.y);

            Canvas canvas = EnsureComponent<Canvas>(popupObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 22;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(popupObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(popupObject);

            GameObject panelObject = EnsureRectChild(popupObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            panelImage.color = SurvivalHudPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Controller Map",
                40,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(34.0f, -28.0f),
                new Vector2(420.0f, 58.0f));

            EnsureLabel(
                panelObject.transform,
                "Mapping Text",
                "Right trigger: press UI or break blocks\nRight grip: place blocks\nLeft grip: blocks menu\nMenu: comfort settings\nRight thumbstick: snap turn\nRight A + trigger: teleport\nLeft X: reset height\nLeft Y: undo",
                24,
                TextAnchor.UpperLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(34.0f, -102.0f),
                new Vector2(552.0f, 220.0f));

            Button closeButton = EnsureButtonControl(
                panelObject.transform,
                "Close Button",
                "Close",
                new Vector2(34.0f, -342.0f),
                new Vector2(180.0f, 52.0f));

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(popupObject);
            presenter.Configure(
                canvas,
                head,
                1.06f,
                0.0f,
                -0.14f,
                0.0f,
                0.00185f,
                showWhenStarted: true);

            RemovePersistentListeners(
                closeButton.onClick,
                presenter,
                nameof(BlockiverseWorldSpacePanelPresenter.Hide));
            UnityEventTools.AddPersistentListener(closeButton.onClick, presenter.Hide);

            EditorUtility.SetDirty(closeButton);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(popupObject);
        }

        static void EnsureXrRigSurvivalHud(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            GameObject hudObject = EnsureRectChild(cameraOffset, SurvivalHudName);
            hudObject.transform.localPosition = new Vector3(0.0f, 1.38f, 1.15f);
            hudObject.transform.localRotation = Quaternion.Euler(10.0f, 0.0f, 0.0f);
            hudObject.transform.localScale = Vector3.one * 0.0016f;

            RectTransform hudRect = hudObject.GetComponent<RectTransform>();
            hudRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, SurvivalHudSize.x);
            hudRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, SurvivalHudSize.y);

            Canvas canvas = EnsureComponent<Canvas>(hudObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 9;
            canvas.enabled = true;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(hudObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(hudObject);

            GameObject panelObject = EnsureRectChild(hudObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            panelImage.color = SurvivalHudPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Survival",
                34,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(24.0f, -24.0f),
                new Vector2(280.0f, 48.0f));

            SurvivalHealthPanel healthPanel = EnsureSurvivalHealthSection(panelObject.transform);
            SurvivalInventoryPanel inventoryPanel = EnsureSurvivalInventorySection(panelObject.transform);
            SurvivalCraftingPanel craftingPanel = EnsureSurvivalCraftingSection(panelObject.transform);

            SurvivalHudController controller = EnsureComponent<SurvivalHudController>(hudObject);
            controller.Configure(inventoryPanel, craftingPanel, healthPanel);

            EditorUtility.SetDirty(hudObject);
            EditorUtility.SetDirty(controller);
        }

        static SurvivalHealthPanel EnsureSurvivalHealthSection(Transform parent)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Health", new Vector2(24.0f, -82.0f), new Vector2(206.0f, 150.0f));

            EnsureLabel(
                sectionObject.transform,
                "Label",
                "Health",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -10.0f),
                new Vector2(170.0f, 34.0f));

            Text valueLabel = EnsureLabel(
                sectionObject.transform,
                "Value",
                "100 / 100",
                28,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -48.0f),
                new Vector2(170.0f, 38.0f));

            Slider slider = EnsureHudSlider(sectionObject.transform, "Health Slider", new Vector2(16.0f, -92.0f), new Vector2(170.0f, 20.0f));

            Text stateLabel = EnsureLabel(
                sectionObject.transform,
                "State",
                "Stable",
                22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -116.0f),
                new Vector2(170.0f, 28.0f));

            SurvivalHealthPanel panel = EnsureComponent<SurvivalHealthPanel>(sectionObject);
            panel.Configure(valueLabel, slider, stateLabel);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        static SurvivalInventoryPanel EnsureSurvivalInventorySection(Transform parent)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Inventory", new Vector2(250.0f, -82.0f), new Vector2(206.0f, 300.0f));

            EnsureLabel(
                sectionObject.transform,
                "Label",
                "Inventory",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -10.0f),
                new Vector2(170.0f, 34.0f));

            Text selectedHotbarLabel = EnsureLabel(
                sectionObject.transform,
                "Selected Hotbar",
                "Hotbar 1 / 8",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -44.0f),
                new Vector2(170.0f, 28.0f));

            Text[] slotLabels = new Text[6];
            Button[] slotButtons = new Button[slotLabels.Length];

            for (int index = 0; index < slotLabels.Length; index++)
            {
                slotLabels[index] = EnsureLabel(
                    sectionObject.transform,
                    $"Slot {index + 1}",
                    "Empty",
                    18,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(16.0f, -82.0f - index * 34.0f),
                    new Vector2(170.0f, 28.0f));
                slotButtons[index] = EnsureTextButton(slotLabels[index]);
            }

            SurvivalInventoryPanel panel = EnsureComponent<SurvivalInventoryPanel>(sectionObject);
            panel.Configure(slotButtons, slotLabels, selectedHotbarLabel);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        static SurvivalCraftingPanel EnsureSurvivalCraftingSection(Transform parent)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Crafting", new Vector2(480.0f, -82.0f), new Vector2(216.0f, 300.0f));

            EnsureLabel(
                sectionObject.transform,
                "Label",
                "Crafting",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -10.0f),
                new Vector2(180.0f, 34.0f));

            Text statusLabel = EnsureLabel(
                sectionObject.transform,
                "Status",
                "Ready",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -44.0f),
                new Vector2(180.0f, 28.0f));

            Text[] recipeLabels = new Text[5];
            Button[] recipeButtons = new Button[recipeLabels.Length];

            for (int index = 0; index < recipeLabels.Length; index++)
            {
                recipeLabels[index] = EnsureLabel(
                    sectionObject.transform,
                    $"Recipe {index + 1}",
                    string.Empty,
                    15,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(16.0f, -82.0f - index * 40.0f),
                    new Vector2(180.0f, 36.0f));
                recipeButtons[index] = EnsureTextButton(recipeLabels[index]);
            }

            SurvivalCraftingPanel panel = EnsureComponent<SurvivalCraftingPanel>(sectionObject);
            panel.Configure(recipeButtons, recipeLabels, statusLabel);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        static GameObject EnsureHudSection(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject sectionObject = EnsureRectChild(parent, name);
            RectTransform sectionRect = sectionObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sectionRect, anchoredPosition, size);
            Image sectionImage = EnsureComponent<Image>(sectionObject);
            sectionImage.color = SurvivalHudSectionColor;
            return sectionObject;
        }

        static Slider EnsureHudSlider(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject sliderObject = EnsureRectChild(parent, name);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, anchoredPosition, size);

            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0.0f;
            slider.maxValue = 100.0f;
            slider.value = 100.0f;

            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = EnsureComponent<Image>(backgroundObject);
            background.color = ComfortMenuControlColor;

            GameObject fillAreaObject = EnsureRectChild(sliderObject.transform, "Fill Area");
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            GameObject fillObject = EnsureRectChild(fillAreaObject.transform, "Fill");
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = EnsureComponent<Image>(fillObject);
            fill.color = SurvivalHudAccentColor;

            slider.fillRect = fillRect;
            slider.targetGraphic = background;
            return slider;
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
            return EnsureButtonControl(parent, name, label, anchoredPosition, new Vector2(220.0f, 54.0f));
        }

        static Button EnsureButtonControl(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            GameObject buttonObject = EnsureRectChild(parent, name);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(buttonRect, anchoredPosition, size);

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

        static Button EnsureTextButton(Text label)
        {
            Button button = EnsureComponent<Button>(label.gameObject);
            label.raycastTarget = true;
            button.targetGraphic = label;
            return button;
        }

        static InputField EnsureInputFieldControl(
            Transform parent,
            string name,
            string placeholder,
            string value,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            GameObject inputObject = EnsureRectChild(parent, name);
            RectTransform inputRect = inputObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(inputRect, anchoredPosition, size);

            Image image = EnsureComponent<Image>(inputObject);
            image.color = MultiplayerMenuInputColor;

            InputField input = EnsureComponent<InputField>(inputObject);
            input.targetGraphic = image;
            input.text = value;
            input.contentType = InputField.ContentType.Standard;
            input.lineType = InputField.LineType.SingleLine;

            Text text = EnsureLabel(
                inputObject.transform,
                "Text",
                value,
                24,
                TextAnchor.MiddleLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.0f, 0.5f),
                new Vector2(18.0f, 0.0f),
                new Vector2(-36.0f, 0.0f));
            text.supportRichText = false;

            Text placeholderText = EnsureLabel(
                inputObject.transform,
                "Placeholder",
                placeholder,
                24,
                TextAnchor.MiddleLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.0f, 0.5f),
                new Vector2(18.0f, 0.0f),
                new Vector2(-36.0f, 0.0f));
            placeholderText.color = new Color(1.0f, 1.0f, 1.0f, 0.45f);

            input.textComponent = text;
            input.placeholder = placeholderText;

            // Native VR text entry: open the Quest system keyboard when the field is selected.
            BlockiverseSystemKeyboardField keyboardField = EnsureComponent<BlockiverseSystemKeyboardField>(inputObject);
            keyboardField.Configure(input);

            return input;
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

        static GameObject EnsureRectChildMigrated(Transform parent, Transform legacyParent, string name)
        {
            Transform existing = parent.Find(name);
            Transform legacy = legacyParent != null ? legacyParent.Find(name) : null;

            if (existing == null && legacy != null)
            {
                legacy.SetParent(parent, false);
                return legacy.gameObject;
            }

            if (existing != null && legacy != null && legacy != existing)
                UnityEngine.Object.DestroyImmediate(legacy.gameObject);

            return EnsureRectChild(parent, name);
        }

        static void ConfigureCanvasWorldCamera(Canvas canvas, Transform head)
        {
            if (canvas == null)
                return;

            canvas.worldCamera = head != null ? head.GetComponent<Camera>() : null;
        }

        // World-space VR canvases must be raycast by tracked-device rays, not the screen-space
        // GraphicRaycaster. Swap in XRI's TrackedDeviceGraphicRaycaster so XRRayInteractors can
        // drive buttons, toggles, sliders, and scrolling.
        static TrackedDeviceGraphicRaycaster EnsureTrackedDeviceRaycaster(GameObject canvasObject)
        {
            GraphicRaycaster legacyRaycaster = canvasObject.GetComponent<GraphicRaycaster>();

            if (legacyRaycaster != null)
                UnityEngine.Object.DestroyImmediate(legacyRaycaster);

            TrackedDeviceGraphicRaycaster raycaster = EnsureComponent<TrackedDeviceGraphicRaycaster>(canvasObject);
            EditorUtility.SetDirty(canvasObject);
            return raycaster;
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
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(rig);

            BlockiverseComfortSettings settings = rig.GetComponent<BlockiverseComfortSettings>();

            if (settings == null)
                settings = rig.AddComponent<BlockiverseComfortSettings>();

            if (origin != null)
                origin.CameraYOffset = settings.StandingEyeHeight;

            XRBodyTransformer bodyTransformer = rig.GetComponent<XRBodyTransformer>();

            if (bodyTransformer == null)
                bodyTransformer = rig.AddComponent<XRBodyTransformer>();

            bodyTransformer.xrOrigin = origin;

            LocomotionMediator mediator = rig.GetComponent<LocomotionMediator>();

            if (mediator == null)
                mediator = rig.AddComponent<LocomotionMediator>();

            TeleportationProvider teleport = rig.GetComponent<TeleportationProvider>();

            if (teleport == null)
                teleport = rig.AddComponent<TeleportationProvider>();

            teleport.mediator = mediator;
            teleport.delayTime = 0.0f;

            ContinuousMoveProvider continuousMove = rig.GetComponent<ContinuousMoveProvider>();

            if (continuousMove == null)
                continuousMove = rig.AddComponent<ContinuousMoveProvider>();

            continuousMove.mediator = mediator;
            continuousMove.forwardSource = origin != null && origin.Camera != null ? origin.Camera.transform : rig.transform;
            continuousMove.enableStrafe = true;
            continuousMove.enableFly = false;
            continuousMove.moveSpeed = settings.ContinuousMoveSpeed;

            SnapTurnProvider snapTurn = rig.GetComponent<SnapTurnProvider>();

            if (snapTurn == null)
                snapTurn = rig.AddComponent<SnapTurnProvider>();

            snapTurn.mediator = mediator;
            snapTurn.turnAmount = settings.SnapTurnDegrees;
            snapTurn.enableTurnLeftRight = true;
            snapTurn.enableTurnAround = false;
            snapTurn.delayTime = 0.0f;

            ContinuousTurnProvider continuousTurn = rig.GetComponent<ContinuousTurnProvider>();

            if (continuousTurn == null)
                continuousTurn = rig.AddComponent<ContinuousTurnProvider>();

            continuousTurn.mediator = mediator;

            BlockiverseHeightReset heightReset = rig.GetComponent<BlockiverseHeightReset>();

            if (heightReset == null)
                heightReset = rig.AddComponent<BlockiverseHeightReset>();

            heightReset.Configure(origin, settings);
            inputRig.ConfigureLocomotion(teleport, snapTurn, heightReset, continuousMove, mediator, bodyTransformer, settings, continuousTurn);

            BlockiverseAudioCuePlayer audioCuePlayer = rig.GetComponent<BlockiverseAudioCuePlayer>();
            inputRig.ConfigureTeleportFeedback(audioCuePlayer);
        }
    }
}

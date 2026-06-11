using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.MetaAvatars;
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

        // ── Game menu panel names ────────────────────────────────────────────────
        const string TitleMenuName = "Title Menu";
        const string PauseMenuName = "Pause Menu";
        const string DeathScreenName = "Death Screen";
        const string ConfirmDialogName = "Confirm Dialog";
        const string NewWorldPanelName = "New World Panel";
        const string LoadWorldPanelName = "Load World Panel";
        const string SettingsPanelName = "Settings Panel";
        const string AudioSettingsPanelName = "Audio Settings Panel";
        const string ControlsPanelName = "Controls Panel";
        const string WorldDetailsPanelName = "World Details Panel";
        const string CreativeToolsPanelName = "Creative Tools Panel";
        const string StationPanelName = "Station Panel";
        const string LanMultiplayerPanelName = "LAN Multiplayer Panel";
        const float GameMenuScale = 0.0013f;
        // All game menu panels share one world-space position; only one is visible at a time.
        static readonly Vector3 GameMenuLocalPosition = new(0.0f, 1.42f, 1.1f);
        static readonly Vector2 ActionMenuSize = new(440.0f, 540.0f);
        static readonly Vector2 ConfirmDialogSize = new(440.0f, 320.0f);
        static readonly Vector2 NewWorldPanelSize = new(620.0f, 720.0f);
        static readonly Vector2 LoadWorldPanelSize = new(620.0f, 600.0f);
        static readonly Vector2 SettingsPanelSize = new(480.0f, 300.0f);
        static readonly Vector2 StationPanelSize = new(540.0f, 620.0f);
        static readonly Vector2 LanMultiplayerPanelSize = new(620.0f, 520.0f);

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
        const string LeftAimPoseName = "Left Aim Pose";
        const string RightAimPoseName = "Right Aim Pose";
        const string TunnelingVignetteName = "Tunneling Vignette";
        const string TunnelingVignettePrefabPath = "Assets/Blockiverse/VR/TunnelingVignette/TunnelingVignette.prefab";
        const string InteractionTestBlockName = "Interaction Test Block";
        const float JumpHeightMeters = 1.3f;
        static readonly Vector2 ComfortMenuSize = new(520.0f, 580.0f);
        // Sized for the catalog browser: category/page controls, search, and a 3×4 pick grid.
        static readonly Vector2 BlockMenuSize = new(560.0f, 470.0f);
        static readonly Vector2 SurvivalHudSize = new(940.0f, 420.0f);
        static readonly Vector2 ControllerMappingPopupSize = new(620.0f, 420.0f);
        static readonly Vector2 StartupLoadingOverlaySize = new(980.0f, 552.0f);
        static readonly Vector2 MultiplayerSessionMenuSize = new(560.0f, 380.0f);
        // --- Dark-glass theme palette -------------------------------------------------------
        // All panels share a deep charcoal-glass base with a single teal accent. Controls use
        // a lighter tinted surface with hover/pressed tints via Button.ColorBlock.
        static readonly Color PanelBaseColor       = new(0.06f, 0.07f, 0.09f, 0.99f);
        static readonly Color PanelHeaderColor     = new(0.09f, 0.11f, 0.14f, 1.00f);
        static readonly Color ControlNormalColor   = new(0.16f, 0.20f, 0.24f, 1.00f);
        static readonly Color ControlHighlightColor= new(0.26f, 0.34f, 0.40f, 1.00f);
        static readonly Color ControlPressedColor  = new(0.10f, 0.14f, 0.18f, 1.00f);
        static readonly Color ControlSelectedColor = new(0.20f, 0.29f, 0.36f, 1.00f);
        static readonly Color AccentColor          = new(0.18f, 0.75f, 0.56f, 1.00f);
        static readonly Color AccentHighlightColor = new(0.24f, 0.88f, 0.66f, 1.00f);
        static readonly Color TextPrimaryColor     = new(0.95f, 0.97f, 1.00f, 1.00f);
        static readonly Color TextDimColor         = new(0.65f, 0.70f, 0.75f, 1.00f);
        static readonly Color DividerColor         = new(0.22f, 0.28f, 0.34f, 0.80f);

        // Legacy per-panel aliases used by a few callers that haven't been refactored yet.
        static readonly Color ComfortMenuPanelColor    = PanelBaseColor;
        static readonly Color ComfortMenuControlColor  = ControlNormalColor;
        static readonly Color ComfortMenuAccentColor   = AccentColor;
        static readonly Color BlockMenuPanelColor      = new(0.05f, 0.09f, 0.13f, 0.96f);
        static readonly Color SurvivalHudPanelColor    = PanelBaseColor;
        static readonly Color SurvivalHudSectionColor  = PanelHeaderColor;
        static readonly Color SurvivalHudAccentColor   = AccentColor;
        static readonly Color StartupOverlayPanelColor = new(0.02f, 0.03f, 0.04f, 0.97f);
        static readonly Color MultiplayerMenuPanelColor= PanelBaseColor;
        static readonly Color MultiplayerMenuInputColor= ControlNormalColor;
        // --- end palette --------------------------------------------------------------------
        static readonly Color PointerLineColor = new(0.36f, 0.82f, 1.0f, 0.92f);
        static readonly Color HighlightColor = new(1.0f, 0.85f, 0.18f, 1.0f);
        static readonly Color TestBlockColor = new(0.22f, 0.56f, 0.43f, 1.0f);
        static readonly (BlockiverseAudioCue Cue, string AssetName)[] AudioCueAssets =
        {
            (BlockiverseAudioCue.BlockBreak, "block_break"),
            (BlockiverseAudioCue.BlockPlace, "block_place"),
            (BlockiverseAudioCue.UiSelect, "ui_select"),
            (BlockiverseAudioCue.UiConfirm, "ui_confirm"),
            (BlockiverseAudioCue.UiCancel, "ui_cancel"),
            (BlockiverseAudioCue.InventoryOpen, "inventory_open"),
            (BlockiverseAudioCue.InventoryClose, "inventory_close"),
            (BlockiverseAudioCue.CraftSuccess, "craft_success"),
            (BlockiverseAudioCue.CraftFail, "craft_fail"),
            (BlockiverseAudioCue.ToolHitSoft, "tool_hit_soft"),
            (BlockiverseAudioCue.ToolHitStone, "tool_hit_stone"),
            (BlockiverseAudioCue.ToolWrong, "tool_wrong"),
            (BlockiverseAudioCue.PickupItem, "pickup_item"),
            (BlockiverseAudioCue.ContainerOpen, "container_open"),
            (BlockiverseAudioCue.ContainerClose, "container_close"),
            (BlockiverseAudioCue.TorchIgnite, "torch_ignite"),
            (BlockiverseAudioCue.TorchLoop, "torch_loop"),
            (BlockiverseAudioCue.CampfireLoop, "campfire_loop"),
            (BlockiverseAudioCue.RainLightLoop, "rain_light_loop"),
            (BlockiverseAudioCue.RainHeavyLoop, "rain_heavy_loop"),
            (BlockiverseAudioCue.ThunderNear, "thunder_near"),
            (BlockiverseAudioCue.ThunderFar, "thunder_far"),
            (BlockiverseAudioCue.SnowWindLoop, "snow_wind_loop"),
            (BlockiverseAudioCue.CaveAmbienceLoop, "cave_ambience_loop"),
            (BlockiverseAudioCue.DayAmbienceLoop, "day_ambience_loop"),
            (BlockiverseAudioCue.NightAmbienceLoop, "night_ambience_loop"),
            (BlockiverseAudioCue.MultiplayerJoin, "multiplayer_join"),
            (BlockiverseAudioCue.MultiplayerLeave, "multiplayer_leave"),
        };

        [MenuItem("Blockiverse/Bootstrap Unity Quest Project")]
        public static void Run()
        {
            EnsureFolders();
            EnsureTmpEssentialResources();
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

        [MenuItem("Blockiverse/Import TMP Essential Resources")]
        public static void ImportTmpEssentialResources()
        {
            EnsureTmpEssentialResources();
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
            InputActionMap leftHandMap = asset.FindActionMap(BlockiverseInputActionNames.LeftHandMap, throwIfNotFound: false);
            InputActionMap rightHandMap = asset.FindActionMap(BlockiverseInputActionNames.RightHandMap, throwIfNotFound: false);

            if (leftHandMap == null)
                AddControllerMap(asset, BlockiverseInputActionNames.LeftHandMap, "<XRController>{LeftHand}");
            else
                EnsureControllerMapSchema(leftHandMap, "<XRController>{LeftHand}");

            if (rightHandMap == null)
                AddControllerMap(asset, BlockiverseInputActionNames.RightHandMap, "<XRController>{RightHand}");
            else
                EnsureControllerMapSchema(rightHandMap, "<XRController>{RightHand}");

            RemoveAction(rightHandMap, BlockiverseInputActionNames.Jump);

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
                BlockiverseInputActionNames.Jump,
                "<XRController>{RightHand}/primaryButton");
            EnsureButtonAction(
                gameplayMap,
                BlockiverseInputActionNames.BlockEditingToggle,
                "<XRController>{RightHand}/secondaryButton");
            RemoveAction(gameplayMap, BlockiverseInputActionNames.Undo);
            RemoveActionBinding(gameplayMap, BlockiverseInputActionNames.HeightReset, "<XRController>{LeftHand}/primaryButton");
            RemoveBindingPath(asset, "<XRController>{LeftHand}/primaryButton");
            RemoveBindingPath(asset, "<XRController>{LeftHand}/secondaryButton");
            EditorUtility.SetDirty(asset);
        }

        static void RemoveAction(InputActionMap map, string actionName)
        {
            InputAction action = map?.FindAction(actionName, throwIfNotFound: false);
            action?.RemoveAction();
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

        static void EnsureControllerMapSchema(InputActionMap map, string controllerPath)
        {
            RemoveAction(map, BlockiverseInputActionNames.Jump);
            EnsureThumbstickYAction(map, BlockiverseInputActionNames.TeleportMode, controllerPath);
            EnsureThumbstickYAction(map, BlockiverseInputActionNames.TeleportSelect, controllerPath);
            RemoveActionBindingsContaining(map, BlockiverseInputActionNames.TeleportMode, "primaryButton", "triggerPressed");
            RemoveActionBindingsContaining(map, BlockiverseInputActionNames.TeleportSelect, "primaryButton", "triggerPressed");
        }

        static void EnsureThumbstickYAction(InputActionMap map, string actionName, string controllerPath)
        {
            InputAction action = map.FindAction(actionName, throwIfNotFound: false);

            if (action == null)
                action = map.AddAction(actionName, InputActionType.Button);

            string thumbstickPath = $"{controllerPath}/thumbstick/y";
            bool hasThumbstickY = action.bindings.Any(binding => binding.path == thumbstickPath);

            if (!hasThumbstickY)
                AddThumbstickYComposite(action, controllerPath);
        }

        static void AddThumbstickYComposite(InputAction action, string controllerPath)
        {
            action.AddCompositeBinding("1DAxis")
                .With("Positive", $"{controllerPath}/thumbstick/y");
        }

        // Import TextMeshPro Essential Resources once so the default font asset is available for
        // procedurally-created TMP labels. The package lives in com.unity.ugui's Package Resources
        // folder. If TMP Settings are already present the import is skipped.
        static void EnsureTmpEssentialResources()
        {
            const string projectSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

            if (File.Exists(projectSettingsPath))
                return;

            string packageFullPath = Path.GetFullPath("Packages/com.unity.ugui");

            if (!Directory.Exists(packageFullPath))
            {
                // Fall back to the cached package location.
                string[] candidates = Directory.GetDirectories(
                    Path.GetFullPath("Library/PackageCache"),
                    "com.unity.ugui*",
                    SearchOption.TopDirectoryOnly);

                if (candidates.Length > 0)
                    packageFullPath = candidates[0];
            }

            string resourcePackage = Path.Combine(packageFullPath, "Package Resources", "TMP Essential Resources.unitypackage");

            if (!File.Exists(resourcePackage))
            {
                BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, $"TMP Essential Resources package not found at '{resourcePackage}'; TMP labels will use the default fallback font.");
                return;
            }

            // Silent import (false = no dialog). This copies fonts, settings and shaders into
            // Assets/TextMesh Pro/ and makes TMP_Settings.defaultFontAsset available.
            AssetDatabase.ImportPackage(resourcePackage, false);
            AssetDatabase.Refresh();
            BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, "Imported TMP Essential Resources.");
        }

        // Migration helper: drop a stale binding (e.g. the old A-button Height Reset that is now Jump) so a
        // regenerated asset does not double-bind the control. No-op when the action/binding is already gone.
        static void RemoveActionBinding(InputActionMap map, string actionName, string bindingPath)
        {
            InputAction action = map.FindAction(actionName, throwIfNotFound: false);

            if (action == null)
                return;

            for (int index = action.bindings.Count - 1; index >= 0; index--)
            {
                if (action.bindings[index].path == bindingPath)
                    action.ChangeBinding(index).Erase();
            }
        }

        static void RemoveActionBindingsContaining(InputActionMap map, string actionName, params string[] pathSubstrings)
        {
            InputAction action = map?.FindAction(actionName, throwIfNotFound: false);

            if (action == null)
                return;

            for (int index = action.bindings.Count - 1; index >= 0; index--)
            {
                string path = action.bindings[index].path ?? string.Empty;

                if (pathSubstrings.Any(path.Contains))
                    action.ChangeBinding(index).Erase();
            }
        }

        static void RemoveBindingPath(InputActionAsset asset, string bindingPath)
        {
            foreach (InputActionMap map in asset.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    for (int index = action.bindings.Count - 1; index >= 0; index--)
                    {
                        if (action.bindings[index].path == bindingPath)
                            action.ChangeBinding(index).Erase();
                    }
                }
            }
        }

        static void EnsureInteractionMaterials()
        {
            EnsureMaterial(BlockiverseProject.PointerLineMaterialPath, PointerLineColor, preferUnlit: true);
            EnsureMaterial(BlockiverseProject.HighlightMaterialPath, HighlightColor, preferUnlit: false);
            EnsureFluidAtlasTiles();
            EnsureBlockItemIcons();
            EnsureBlockTextureMaterial();
        }

        // Every registered item needs a committed inventory icon. Block-mapped items (terrain,
        // stone, deep rock) that lack an authored icon derive one from their 16×16 block source
        // tile, so a newly registered block doesn't leave an icon gap. Additive: never overwrites
        // an authored icon.
        static void EnsureBlockItemIcons()
        {
            foreach (ItemDefinition item in ItemRegistry.CreateDefault().All)
            {
                if (item.Id.IsNone)
                    continue;

                string iconPath = $"Assets/Blockiverse/Art/Textures/Items/{item.Id.Value}.png";
                if (File.Exists(iconPath))
                    continue;

                string sourcePath = $"Assets/Blockiverse/Art/Textures/Blocks/Source/{item.Id.Value}.png";
                if (!File.Exists(sourcePath))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(iconPath));
                File.Copy(sourcePath, iconPath);
                AssetDatabase.ImportAsset(iconPath);
                BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, $"Derived item icon {item.Id.Value}.png from its block source tile.");
            }
        }

        // Atlas tile indexes assigned to the fluid blocks in BlockVisualAtlas.TileIndexByBlockId.
        const int FreshwaterAtlasTileIndex = 73;
        const int BrineAtlasTileIndex = 74;
        const int EmberflowAtlasTileIndex = 75;

        // Paints the freshwater/brine/emberflow tiles into the authored block atlas. Strictly
        // additive and deterministic: a tile is only painted while it is still blank (fully
        // transparent or one uniform placeholder color), so authored pixels are never touched
        // and reruns are no-ops.
        // The stale Python art generator (ATLAS_ROWS=7) must NOT be used for this — it would
        // regenerate the whole atlas at the old size.
        static void EnsureFluidAtlasTiles()
        {
            // Source tiles back the atlas tiles (every block needs a committed source PNG); write
            // them from the same pixel functions so source and atlas stay consistent. Flow cells
            // render with their family's source tile, so their source PNGs reuse the family pixels.
            EnsureFluidSourceTile("freshwater", FreshwaterTilePixel);
            EnsureFluidSourceTile("brine", BrineTilePixel);
            EnsureFluidSourceTile("emberflow", EmberflowTilePixel);
            EnsureFluidSourceTile("freshwater_flow", FreshwaterTilePixel);
            EnsureFluidSourceTile("brine_flow", BrineTilePixel);
            EnsureFluidSourceTile("emberflow_flow", EmberflowTilePixel);

            string path = BlockVisualAtlas.AuthoredAtlasPath;
            if (!File.Exists(path))
                return;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)) ||
                    texture.width != BlockVisualAtlas.Columns * BlockVisualAtlas.TilePixels ||
                    texture.height != BlockVisualAtlas.Rows * BlockVisualAtlas.TilePixels)
                {
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Bootstrap,
                        $"Authored block atlas at {path} is missing or not the expected size; fluid tiles were not painted.");
                    return;
                }

                bool painted = TryPaintAtlasTile(texture, FreshwaterAtlasTileIndex, FreshwaterTilePixel);
                painted |= TryPaintAtlasTile(texture, BrineAtlasTileIndex, BrineTilePixel);
                painted |= TryPaintAtlasTile(texture, EmberflowAtlasTileIndex, EmberflowTilePixel);

                if (!painted)
                    return;

                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, "Painted fluid tiles into the authored block atlas.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // Writes a 16×16 source tile PNG for a fluid block (additive — never overwrites a committed
        // source). The art-asset validation requires one source PNG per renderable block.
        static void EnsureFluidSourceTile(string canonicalId, Func<int, int, Color32> pixelAt)
        {
            string path = $"Assets/Blockiverse/Art/Textures/Blocks/Source/{canonicalId}.png";
            if (File.Exists(path))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            int size = BlockVisualAtlas.TilePixels;
            var tile = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            try
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                        tile.SetPixel(x, y, pixelAt(x, y));
                }

                tile.Apply();
                File.WriteAllBytes(path, tile.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, $"Wrote fluid source tile {canonicalId}.png.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tile);
            }
        }

        static bool TryPaintAtlasTile(Texture2D atlas, int tileIndex, Func<int, int, Color32> pixelAt)
        {
            int column = tileIndex % BlockVisualAtlas.Columns;
            int row = tileIndex / BlockVisualAtlas.Columns;
            int originX = column * BlockVisualAtlas.TilePixels;
            // Tile rows count from the top of the atlas; texture pixel rows from the bottom.
            int originY = (BlockVisualAtlas.Rows - 1 - row) * BlockVisualAtlas.TilePixels;

            if (!IsAtlasTileBlank(atlas, originX, originY))
                return false;

            for (int y = 0; y < BlockVisualAtlas.TilePixels; y++)
            {
                for (int x = 0; x < BlockVisualAtlas.TilePixels; x++)
                    atlas.SetPixel(originX + x, originY + y, pixelAt(x, y));
            }

            return true;
        }

        // Blank = fully transparent or one uniform fill. Authored 16px tiles always vary, so this
        // can never overwrite real art.
        static bool IsAtlasTileBlank(Texture2D atlas, int originX, int originY)
        {
            Color32 first = atlas.GetPixel(originX, originY);
            bool allTransparent = true;
            bool allUniform = true;

            for (int y = 0; y < BlockVisualAtlas.TilePixels; y++)
            {
                for (int x = 0; x < BlockVisualAtlas.TilePixels; x++)
                {
                    Color32 pixel = atlas.GetPixel(originX + x, originY + y);
                    allTransparent &= pixel.a == 0;
                    allUniform &= pixel.r == first.r && pixel.g == first.g && pixel.b == first.b && pixel.a == first.a;
                }
            }

            return allTransparent || allUniform;
        }

        // Calm freshwater: blue body with wave crest/trough bands and sparse sparkle pixels.
        static Color32 FreshwaterTilePixel(int x, int y)
        {
            if ((x * 7 + y * 13) % 23 == 0)
                return new Color32(208, 232, 248, 255);

            int band = (y + ((x + y * 3) % 4 == 0 ? 1 : 0)) % 4;
            if (band == 0)
                return new Color32(64, 124, 198, 255);
            if (band == 2)
                return new Color32(34, 76, 148, 255);

            return new Color32(45, 96, 172, 255);
        }

        // Brine: muted teal-green body with pale salt flecks.
        static Color32 BrineTilePixel(int x, int y)
        {
            if ((x * 11 + y * 7) % 19 == 0)
                return new Color32(226, 234, 226, 255);

            int band = (y + ((x + y * 2) % 5 == 0 ? 1 : 0)) % 4;
            if (band == 0)
                return new Color32(72, 138, 134, 255);
            if (band == 2)
                return new Color32(30, 84, 86, 255);

            return new Color32(44, 108, 106, 255);
        }

        // Emberflow: molten red-orange body bands with bright ember streaks and dark crust flecks.
        static Color32 EmberflowTilePixel(int x, int y)
        {
            if ((x * 5 + y * 11) % 29 == 0)
                return new Color32(255, 232, 150, 255);

            if ((x * 13 + y * 3) % 31 == 0)
                return new Color32(96, 28, 16, 255);

            int band = (y + ((x + y * 3) % 4 == 0 ? 1 : 0)) % 4;
            if (band == 0)
                return new Color32(244, 138, 38, 255);
            if (band == 2)
                return new Color32(168, 52, 16, 255);

            return new Color32(212, 88, 24, 255);
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
            AddThumbstickYComposite(map.AddAction(BlockiverseInputActionNames.TeleportMode, InputActionType.Button), controllerPath);
            AddThumbstickYComposite(map.AddAction(BlockiverseInputActionNames.TeleportSelect, InputActionType.Button), controllerPath);
        }

        static void AddGameplayMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(BlockiverseInputActionNames.GameplayMap);
            map.AddAction(BlockiverseInputActionNames.Menu, InputActionType.Button, "<XRController>{LeftHand}/menuButton");
            map.AddAction(BlockiverseInputActionNames.Jump, InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            map.AddAction(BlockiverseInputActionNames.BlockEditingToggle, InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
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
            EnsureOvrAvatarManager(scene);
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
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.85f;
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

        // Native Meta Avatar SDK initialization is Quest-runtime only. Keep any legacy scene
        // manager inactive in editor-authored scenes so macOS/headless PlayMode tests do not
        // load avatar native libraries; MetaHorizonAvatarProvider creates the singleton on Quest.
        static void EnsureOvrAvatarManager(Scene scene)
        {
            const string AvatarManagerName = "OvrAvatarManager";

            GameObject managerObject = FindRootGameObject(scene, AvatarManagerName);

#if UNITY_ANDROID && !UNITY_EDITOR
            if (managerObject == null)
            {
                managerObject = new GameObject(AvatarManagerName);
                SceneManager.MoveGameObjectToScene(managerObject, scene);
            }

            OvrAvatarManager manager = EnsureComponent<OvrAvatarManager>(managerObject);

            // Configure loading budgets suitable for VR. These can be tuned in the inspector
            // after the initial bootstrap.
            manager.MaxConcurrentAvatarsLoading = 4;
            manager.MaxConcurrentResourcesLoading = 2;

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(managerObject);
#else
            if (managerObject != null && managerObject.activeSelf)
            {
                managerObject.SetActive(false);
                EditorUtility.SetDirty(managerObject);
            }
#endif
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
            BlockiverseXrUiInputConfigurator.Configure(inputModule, EnsureInputActions());

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

            TextMeshProUGUI statusText = EnsureLabel(
                panelObject.transform,
                "Status",
                $"LAN session stopped. Join address defaults to {BlockiverseNetworkConfig.DefaultAddress}.",
                22,
                TextAnchor.UpperLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(28.0f, -258.0f),
                new Vector2(500.0f, 88.0f),
                TextDimColor);

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
            EnsureXrRigFeedback(rig, inputRig);
            EnsureXrRigGameMenus(rig, inputRig);
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
            EnsureXrRigFeedback(rig, inputRig);
            EnsureXrRigGameMenus(rig, inputRig);
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

        // Builds the native interaction (UI + block targeting, right hand only) and teleport rays
        // on each controller, plus the mediator that switches between them while the locomotion
        // mode is Teleport and thumbstick-forward is pushed.
        static XRRayInteractor EnsureControllerInteractors(
            GameObject controller,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            Material pointerMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.PointerLineMaterialPath);
            BlockiverseComfortSettings settings = inputRig != null ? inputRig.GetComponent<BlockiverseComfortSettings>() : null;
            string mapName = role == BlockiverseControllerRole.Left
                ? BlockiverseInputActionNames.LeftHandMap
                : BlockiverseInputActionNames.RightHandMap;
            Transform aimPose = EnsureControllerAimPose(controller.transform.parent, role);

            XRRayInteractor interactionRay = null;

            // Only the right controller carries the UI/block interaction ray.
            if (role == BlockiverseControllerRole.Right)
            {
                GameObject interactionRayObject = EnsureChild(controller.transform, InteractionRayName);
                interactionRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                interactionRayObject.SetActive(true);

                interactionRay = EnsureComponent<XRRayInteractor>(interactionRayObject);
                interactionRay.lineType = XRRayInteractor.LineType.StraightLine;
                interactionRay.enableUIInteraction = true;
                interactionRay.blockUIOnInteractableSelection = false;
                interactionRay.manipulateAttachTransform = false;
                interactionRay.rayOriginTransform = aimPose;
                // Empty interaction layers: the ray never selects 3D interactables (incl. the chunk
                // TeleportationArea). UI still works (separate path) and block targeting uses
                // TryGetCurrent3DRaycastHit on this raycast mask.
                interactionRay.interactionLayers = 0;
                interactionRay.raycastMask = GetInteractionLayerMask();
                interactionRay.uiPressInput = MakeButtonReader("UI Press", FindRigAction(inputRig, mapName, BlockiverseInputActionNames.UiPress));
                interactionRay.uiScrollInput = MakeVector2Reader("UI Scroll", FindRigAction(inputRig, mapName, BlockiverseInputActionNames.UiScroll));
                ConfigureLineVisual(interactionRayObject, pointerMaterial);
                EditorUtility.SetDirty(interactionRay);
            }

            // Both controllers get a teleport ray; the mediator activates it only in Teleport mode.
            GameObject teleportRayObject = EnsureChild(controller.transform, TeleportRayName);
            teleportRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            XRRayInteractor teleportRay = EnsureComponent<XRRayInteractor>(teleportRayObject);
            teleportRay.lineType = XRRayInteractor.LineType.ProjectileCurve;
            teleportRay.enableUIInteraction = false;
            teleportRay.manipulateAttachTransform = false;
            teleportRay.rayOriginTransform = aimPose;
            teleportRay.raycastMask = GetInteractionLayerMask();
            // Teleport on thumb-release: selectInput = thumbstick/y composite, OnSelectExited fires on release.
            teleportRay.selectInput = MakeButtonReader("Teleport Select", FindRigAction(inputRig, mapName, BlockiverseInputActionNames.TeleportSelect));
            ConfigureLineVisual(teleportRayObject, pointerMaterial);
            teleportRayObject.SetActive(false);
            EditorUtility.SetDirty(teleportRay);

            BlockiverseLocomotionRayMediator mediator = EnsureComponent<BlockiverseLocomotionRayMediator>(controller);
            mediator.Configure(inputRig, settings, interactionRay, teleportRay, role);
            EditorUtility.SetDirty(mediator);

            return interactionRay;
        }

        static Transform EnsureControllerAimPose(Transform cameraOffset, BlockiverseControllerRole role)
        {
            string aimPoseName = role == BlockiverseControllerRole.Left ? LeftAimPoseName : RightAimPoseName;
            Transform aimPose = cameraOffset != null ? cameraOffset.Find(aimPoseName) : null;

            if (aimPose == null)
            {
                GameObject aimPoseObject = EnsureChild(cameraOffset, aimPoseName);
                aimPose = aimPoseObject.transform;
            }

            aimPose.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            TrackedPoseDriver poseDriver = EnsureComponent<TrackedPoseDriver>(aimPose.gameObject);
            BlockiverseInputRig.ConfigureControllerAimPoseDriverActions(poseDriver, role);
            poseDriver.enabled = true;
            EditorUtility.SetDirty(poseDriver);
            EditorUtility.SetDirty(aimPose.gameObject);
            return aimPose;
        }

        static void ConfigureLineVisual(GameObject rayObject, Material pointerMaterial)
        {
            LineRenderer lineRenderer = EnsureComponent<LineRenderer>(rayObject);
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            if (pointerMaterial != null)
                lineRenderer.sharedMaterial = pointerMaterial;

            lineRenderer.startColor = PointerLineColor;
            lineRenderer.endColor = PointerLineColor;

            XRInteractorLineVisual lineVisual = EnsureComponent<XRInteractorLineVisual>(rayObject);
            lineVisual.lineWidth = 0.01f;
            lineVisual.overrideInteractorLineLength = false;
            lineVisual.stopLineAtFirstRaycastHit = true;

            EditorUtility.SetDirty(lineRenderer);
            EditorUtility.SetDirty(lineVisual);
        }

        // Use InputActionReference mode so the bootstrapper-assigned reader does not take ownership
        // of the action's enable/disable lifecycle. The rig enables/disables the whole
        // InputActionAsset, and InputAction mode would fight that.
        static XRInputButtonReader MakeButtonReader(string name, InputAction action)
        {
            var reader = new XRInputButtonReader(name,
                inputSourceMode: XRInputButtonReader.InputSourceMode.InputActionReference);

            if (action != null)
                reader.inputActionReferencePerformed = InputActionReference.Create(action);

            return reader;
        }

        static XRInputValueReader<Vector2> MakeVector2Reader(string name, InputAction action)
        {
            if (action == null)
                return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.Unused);

            return new XRInputValueReader<Vector2>(name,
                XRInputValueReader.InputSourceMode.InputActionReference)
            {
                inputActionReference = InputActionReference.Create(action)
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
            // The XRI input manager feeds OvrPlugin controller/HMD pose data to the avatar entity
            // so that hands track the physical controllers. It must be on the rig (or a child)
            // before the entity is instantiated at runtime.
            EnsureComponent<BlockiverseXriAvatarInputManager>(rig);

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
            menuObject.transform.localScale = Vector3.one * 0.0013f;

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
            Sprite comfortPanelSprite = GetRoundedSprite();
            if (comfortPanelSprite != null)
            {
                panelImage.sprite = comfortPanelSprite;
                panelImage.type = Image.Type.Sliced;
            }
            panelImage.color = ComfortMenuPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Comfort Settings",
                32,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(32.0f, -36.0f),
                new Vector2(460.0f, 56.0f));

            // --- Movement Mode (Glide / Teleport) ---
            EnsureLabel(panelObject.transform, "Movement Label", "Movement Mode", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(32.0f, -104.0f), new Vector2(300.0f, 36.0f));

            Toggle glideToggle = EnsureToggleControl(
                panelObject.transform,
                "Glide Toggle",
                "Glide Motion",
                settings == null || settings.LocomotionMode == BlockiverseLocomotionMode.Glide,
                new Vector2(32.0f, -148.0f));

            Toggle teleportToggle = EnsureToggleControl(
                panelObject.transform,
                "Teleport Toggle",
                "Teleport",
                settings != null && settings.LocomotionMode == BlockiverseLocomotionMode.Teleport,
                new Vector2(32.0f, -196.0f));

            // --- Turning ---
            Toggle smoothTurnToggle = EnsureToggleControl(
                panelObject.transform,
                "Smooth Turn Toggle",
                "Smooth Turn",
                settings != null && settings.SmoothTurnEnabled,
                new Vector2(32.0f, -252.0f));

            Slider snapTurnSlider = EnsureSnapTurnSlider(
                panelObject.transform,
                settings != null ? settings.SnapTurnDegrees : 45.0f,
                new Vector2(32.0f, -308.0f));

            // --- Vignette ---
            Toggle vignetteToggle = EnsureToggleControl(
                panelObject.transform,
                "Vignette Toggle",
                "Motion Vignette",
                settings == null || settings.VignetteEnabled,
                new Vector2(32.0f, -376.0f));

            Slider vignetteSlider = EnsureVignetteSlider(
                panelObject.transform,
                settings != null ? settings.VignetteStrength : 1.0f,
                new Vector2(32.0f, -430.0f));

            // --- Height Reset ---
            Button heightResetButton = EnsureButtonControl(
                panelObject.transform,
                "Height Reset Button",
                "Reset Height",
                new Vector2(32.0f, -494.0f));

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
            menu.ConfigureControls(glideToggle, teleportToggle, smoothTurnToggle, snapTurnSlider, vignetteToggle, vignetteSlider);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(menuObject);
            presenter.Configure(canvas, head, 1.3f, 0.0f, -0.06f, 0.0f, 0.0013f);
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

            BlockiverseComfortSettings vignetteSettings = rig.GetComponent<BlockiverseComfortSettings>();
            float aperture = vignetteSettings != null ? vignetteSettings.VignetteAperture : 0.85f;

            // Default parameters: aperture 0.85 is subtler than the XRI default (0.7).
            // The comfort menu's vignette strength slider adjusts this at runtime.
            controller.defaultParameters = new VignetteParameters
            {
                apertureSize = aperture,
                featheringEffect = 0.2f,
                easeInTime = 0.3f,
                easeOutTime = 0.3f,
            };

            // Ease the comfort vignette in/out during locomotion that causes vection or a viewpoint
            // jump: continuous move, continuous (smooth) turn, and teleport. Snap turn is itself a
            // discrete comfort option, so it is intentionally excluded to avoid a vignette flicker
            // on every snap.
            controller.locomotionVignetteProviders.Clear();
            AddVignetteProvider(controller, rig.GetComponent<ContinuousMoveProvider>());
            AddVignetteProvider(controller, rig.GetComponent<ContinuousTurnProvider>());
            AddVignetteProvider(controller, rig.GetComponent<TeleportationProvider>());

            BlockiverseVignetteSettingsDriver driver = EnsureComponent<BlockiverseVignetteSettingsDriver>(controller.gameObject);
            driver.Configure(vignetteSettings);

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(driver);
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

        static void EnsureXrRigFeedback(
            GameObject rig,
            BlockiverseInputRig inputRig,
            CreativeInteractionController controller = null)
        {
            BlockiverseFeedbackSettings feedbackSettings = EnsureComponent<BlockiverseFeedbackSettings>(rig);

            BlockiverseAudioCuePlayer audioCuePlayer = EnsureComponent<BlockiverseAudioCuePlayer>(rig);
            ConfigureGeneratedAudioClips(audioCuePlayer);
            audioCuePlayer.Configure(controller);
            audioCuePlayer.ConfigureFeedbackSettings(feedbackSettings);

            BlockiverseVfxPool vfxPool = EnsureComponent<BlockiverseVfxPool>(rig);
            BlockiverseVfxCuePlayer vfxCuePlayer = EnsureComponent<BlockiverseVfxCuePlayer>(rig);
            vfxCuePlayer.Configure(controller, vfxPool, feedbackSettings);

            BlockiverseInteractionHaptics interactionHaptics = EnsureComponent<BlockiverseInteractionHaptics>(rig);
            interactionHaptics.Configure(controller, FindControllerHaptics(rig, BlockiverseControllerRole.Right));
            interactionHaptics.ConfigureFeedbackSettings(feedbackSettings);

            // Survival command + multiplayer presence cues, and the weather/ambience driver —
            // both discover their runtime dependencies (sync, world manager) on enable.
            SurvivalFeedbackBridge survivalFeedback = EnsureComponent<SurvivalFeedbackBridge>(rig);
            WeatherFeedbackController weatherFeedback = EnsureComponent<WeatherFeedbackController>(rig);

            // The sparse music bed: generated tracks per context (menu/day/night/cave).
            BlockiverseMusicController musicController = EnsureComponent<BlockiverseMusicController>(rig);
            musicController.ConfigureClips(
                LoadAudioClip("music_menu"),
                LoadAudioClip("music_day"),
                LoadAudioClip("music_night"),
                LoadAudioClip("music_cave"));
            musicController.ConfigureFeedbackSettings(feedbackSettings);
            EditorUtility.SetDirty(musicController);

            // Glide footsteps + landing thump from the rig's character controller.
            BlockiverseLocomotionFeedback locomotionFeedback = EnsureComponent<BlockiverseLocomotionFeedback>(rig);
            locomotionFeedback.Configure(rig.GetComponent<CharacterController>(), audioCuePlayer);
            EditorUtility.SetDirty(locomotionFeedback);

            // Comfort + feedback settings persist across launches (PlayerPrefs).
            BlockiverseSettingsPersistence settingsPersistence = EnsureComponent<BlockiverseSettingsPersistence>(rig);
            EditorUtility.SetDirty(settingsPersistence);

            inputRig?.ConfigureTeleportFeedback(audioCuePlayer);
            ConfigurePanelFeedbackReferences(rig, audioCuePlayer, interactionHaptics);

            EditorUtility.SetDirty(feedbackSettings);
            EditorUtility.SetDirty(audioCuePlayer);
            EditorUtility.SetDirty(vfxPool);
            EditorUtility.SetDirty(vfxCuePlayer);
            EditorUtility.SetDirty(interactionHaptics);
            EditorUtility.SetDirty(survivalFeedback);
            EditorUtility.SetDirty(weatherFeedback);

            if (inputRig != null)
                EditorUtility.SetDirty(inputRig);
        }

        static void ConfigureGeneratedAudioClips(BlockiverseAudioCuePlayer audioCuePlayer)
        {
            foreach ((BlockiverseAudioCue cue, string assetName) in AudioCueAssets)
                audioCuePlayer.ConfigureClip(cue, LoadAudioClip(assetName));

            audioCuePlayer.ConfigureFootstepClips(
                LoadAudioClip("footstep_01"),
                LoadAudioClip("footstep_02"));
        }

        static AudioClip LoadAudioClip(string assetName)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Blockiverse/Audio/{assetName}.wav");
        }

        static BlockiverseControllerHaptics FindControllerHaptics(GameObject rig, BlockiverseControllerRole role)
        {
            foreach (BlockiverseControllerHaptics haptics in rig.GetComponentsInChildren<BlockiverseControllerHaptics>(true))
            {
                if (haptics.Role == role)
                    return haptics;
            }

            return rig.GetComponentInChildren<BlockiverseControllerHaptics>(true);
        }

        static void ConfigurePanelFeedbackReferences(
            GameObject rig,
            BlockiverseAudioCuePlayer audioCuePlayer,
            BlockiverseInteractionHaptics interactionHaptics)
        {
            foreach (CreativeHotbar hotbar in rig.GetComponentsInChildren<CreativeHotbar>(true))
            {
                hotbar.ConfigureFeedback(audioCuePlayer);
                EditorUtility.SetDirty(hotbar);
            }

            foreach (BlockiverseComfortMenu menu in rig.GetComponentsInChildren<BlockiverseComfortMenu>(true))
            {
                menu.ConfigureFeedback(audioCuePlayer, interactionHaptics);
                EditorUtility.SetDirty(menu);
            }

            foreach (BlockiverseWorldSpacePanelPresenter presenter in rig.GetComponentsInChildren<BlockiverseWorldSpacePanelPresenter>(true))
            {
                presenter.ConfigureFeedback(
                    audioCuePlayer,
                    interactionHaptics,
                    presenter.ShowFeedbackCue,
                    presenter.HideFeedbackCue,
                    presenter.PlaysShowFeedback,
                    presenter.PlaysHideFeedback);
                EditorUtility.SetDirty(presenter);
            }
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
            Sprite blockMenuSprite = GetRoundedSprite();
            if (blockMenuSprite != null)
            {
                panelImage.sprite = blockMenuSprite;
                panelImage.type = Image.Type.Sliced;
            }
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

            TMP_Text selectedLabel = EnsureLabel(
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

            // The browser replaced the decorative swatches; clear them out of older rigs.
            foreach (string stale in new[] { "Swatch A", "Swatch B", "Swatch C" })
            {
                Transform staleSwatch = panelObject.transform.Find(stale);
                if (staleSwatch != null)
                    UnityEngine.Object.DestroyImmediate(staleSwatch.gameObject);
            }

            // Catalog browser controls: category cycle + page label/buttons + search field.
            Button categoryButton = EnsureButtonControl(panelObject.transform, "Category Button", "Category",
                new Vector2(24.0f, -124.0f), new Vector2(150.0f, 44.0f));
            TMP_Text categoryLabel = EnsureLabel(panelObject.transform, "Category Label", "Terrain", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(190.0f, -128.0f), new Vector2(170.0f, 36.0f));

            Button prevPageButton = EnsureButtonControl(panelObject.transform, "Prev Page Button", "<",
                new Vector2(368.0f, -124.0f), new Vector2(52.0f, 44.0f));
            TMP_Text pageLabel = EnsureLabel(panelObject.transform, "Page Label", "1/1", 22,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(424.0f, -128.0f), new Vector2(56.0f, 36.0f));
            Button nextPageButton = EnsureButtonControl(panelObject.transform, "Next Page Button", ">",
                new Vector2(484.0f, -124.0f), new Vector2(52.0f, 44.0f));

            TMP_InputField searchField = EnsureInputFieldControl(panelObject.transform, "Search Field",
                "Search blocks…", string.Empty, new Vector2(24.0f, -176.0f), new Vector2(512.0f, 48.0f));

            // 12-entry grid (3 columns × 4 rows) of pick buttons.
            const int gridColumns = 3;
            const int gridEntries = 12;
            var entryButtons = new Button[gridEntries];
            var entryLabels = new TMP_Text[gridEntries];
            for (int i = 0; i < gridEntries; i++)
            {
                int column = i % gridColumns;
                int row = i / gridColumns;
                var position = new Vector2(24.0f + column * 172.0f, -236.0f - row * 54.0f);
                entryButtons[i] = EnsureButtonControl(panelObject.transform, $"Entry Button {i}", string.Empty,
                    position, new Vector2(164.0f, 46.0f));
                Transform entryLabelTransform = entryButtons[i].transform.Find("Label");
                entryLabels[i] = entryLabelTransform != null ? entryLabelTransform.GetComponent<TMP_Text>() : null;
                if (entryLabels[i] != null)
                    entryLabels[i].fontSize = 18;
            }

            CreativeHotbar menu = EnsureComponent<CreativeHotbar>(menuObject);
            menu.ConfigureFromDefaultCatalog(selectedLabel);
            menu.ConfigureCanvas(canvas);

            BlockiverseCatalogBrowserPanel browser = EnsureComponent<BlockiverseCatalogBrowserPanel>(menuObject);
            browser.Configure(menu, categoryLabel, pageLabel, searchField, entryButtons, entryLabels);
            WireButton(categoryButton, browser, nameof(BlockiverseCatalogBrowserPanel.CycleCategory), browser.CycleCategory);
            WireButton(prevPageButton, browser, nameof(BlockiverseCatalogBrowserPanel.PreviousPage), browser.PreviousPage);
            WireButton(nextPageButton, browser, nameof(BlockiverseCatalogBrowserPanel.NextPage), browser.NextPage);
            EditorUtility.SetDirty(browser);
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
            popupObject.transform.localScale = Vector3.one * 0.0013f;

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
                32,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(34.0f, -28.0f),
                new Vector2(420.0f, 58.0f));

            EnsureLabel(
                panelObject.transform,
                "Mapping Text",
                ControllerMappingText,
                20,
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
                0.0013f,
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

            BlockiverseItemIconLibrary iconLibrary = EnsureItemIconLibrary(rig);
            SurvivalHealthPanel healthPanel = EnsureSurvivalHealthSection(panelObject.transform);
            SurvivalInventoryPanel inventoryPanel = EnsureSurvivalInventorySection(panelObject.transform, iconLibrary);
            SurvivalCraftingPanel craftingPanel = EnsureSurvivalCraftingSection(panelObject.transform, iconLibrary);
            SurvivalCratePanel cratePanel = EnsureSurvivalCrateSection(panelObject.transform);

            SurvivalHudController controller = EnsureComponent<SurvivalHudController>(hudObject);
            controller.Configure(inventoryPanel, craftingPanel, healthPanel, cratePanel);

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

            TMP_Text valueLabel = EnsureLabel(
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

            TMP_Text stateLabel = EnsureLabel(
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

        // Populates the rig's item icon library from the committed item icon sprites — one entry
        // per Assets/Blockiverse/Art/Textures/Items/<canonical_id>.png, importer forced to Sprite.
        static BlockiverseItemIconLibrary EnsureItemIconLibrary(GameObject rig)
        {
            const string itemsDir = "Assets/Blockiverse/Art/Textures/Items";

            var ids = new List<string>();
            var sprites = new List<Sprite>();

            if (AssetDatabase.IsValidFolder(itemsDir))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { itemsDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                        importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.SaveAndReimport();
                    }

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null)
                        continue;

                    ids.Add(Path.GetFileNameWithoutExtension(path));
                    sprites.Add(sprite);
                }
            }

            BlockiverseItemIconLibrary library = EnsureComponent<BlockiverseItemIconLibrary>(rig);
            library.Configure(ids.ToArray(), sprites.ToArray());
            EditorUtility.SetDirty(library);
            return library;
        }

        static SurvivalInventoryPanel EnsureSurvivalInventorySection(Transform parent, BlockiverseItemIconLibrary iconLibrary)
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

            TMP_Text selectedHotbarLabel = EnsureLabel(
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

            TMP_Text[] slotLabels = new TMP_Text[6];
            Button[] slotButtons = new Button[slotLabels.Length];
            Image[] slotIcons = new Image[slotLabels.Length];

            for (int index = 0; index < slotLabels.Length; index++)
            {
                slotIcons[index] = EnsureItemIconImage(
                    sectionObject.transform,
                    $"Slot Icon {index + 1}",
                    new Vector2(16.0f, -82.0f - index * 34.0f),
                    26.0f);

                slotLabels[index] = EnsureLabel(
                    sectionObject.transform,
                    $"Slot {index + 1}",
                    "Empty",
                    18,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(48.0f, -82.0f - index * 34.0f),
                    new Vector2(140.0f, 28.0f));
                slotButtons[index] = EnsureTextButton(slotLabels[index]);
            }

            SurvivalInventoryPanel panel = EnsureComponent<SurvivalInventoryPanel>(sectionObject);
            panel.Configure(slotButtons, slotLabels, selectedHotbarLabel, slotIcons, iconLibrary);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        // Small square icon image used by inventory slots and crafting rows; hidden until a
        // sprite is assigned at runtime.
        static Image EnsureItemIconImage(Transform parent, string name, Vector2 anchoredPosition, float size)
        {
            GameObject iconObject = EnsureRectChild(parent, name);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(iconRect, anchoredPosition, new Vector2(size, size));

            Image icon = EnsureComponent<Image>(iconObject);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            return icon;
        }

        static SurvivalCraftingPanel EnsureSurvivalCraftingSection(Transform parent, BlockiverseItemIconLibrary iconLibrary)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Crafting", new Vector2(480.0f, -82.0f), new Vector2(216.0f, 340.0f));

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

            TMP_Text statusLabel = EnsureLabel(
                sectionObject.transform,
                "Status",
                "Ready",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -44.0f),
                new Vector2(180.0f, 28.0f),
                TextDimColor);

            TMP_Text[] recipeLabels = new TMP_Text[5];
            Button[] recipeButtons = new Button[recipeLabels.Length];
            Image[] recipeIcons = new Image[recipeLabels.Length];

            for (int index = 0; index < recipeLabels.Length; index++)
            {
                recipeIcons[index] = EnsureItemIconImage(
                    sectionObject.transform,
                    $"Recipe Icon {index + 1}",
                    new Vector2(16.0f, -84.0f - index * 40.0f),
                    28.0f);

                recipeLabels[index] = EnsureLabel(
                    sectionObject.transform,
                    $"Recipe {index + 1}",
                    string.Empty,
                    15,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(50.0f, -82.0f - index * 40.0f),
                    new Vector2(146.0f, 36.0f));
                recipeButtons[index] = EnsureTextButton(recipeLabels[index]);
            }

            // Mend Bench repair of the held tool (§10.7) — gated at runtime by station proximity.
            TMP_Text repairLabel = EnsureLabel(
                sectionObject.transform,
                "Repair",
                "Repair Held Tool",
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -82.0f - recipeLabels.Length * 40.0f),
                new Vector2(180.0f, 36.0f));
            Button repairButton = EnsureTextButton(repairLabel);

            SurvivalCraftingPanel panel = EnsureComponent<SurvivalCraftingPanel>(sectionObject);
            panel.Configure(recipeButtons, recipeLabels, statusLabel, recipeIcons, iconLibrary);
            panel.ConfigureRepairButton(repairButton);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        static SurvivalCratePanel EnsureSurvivalCrateSection(Transform parent)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Shared Crate", new Vector2(706.0f, -82.0f), new Vector2(216.0f, 300.0f));

            EnsureLabel(
                sectionObject.transform,
                "Label",
                "Shared Crate",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -10.0f),
                new Vector2(180.0f, 34.0f));

            TMP_Text statusLabel = EnsureLabel(
                sectionObject.transform,
                "Status",
                "Shared crate",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -44.0f),
                new Vector2(180.0f, 28.0f),
                TextDimColor);

            TMP_Text[] slotLabels = new TMP_Text[4];
            Button[] slotButtons = new Button[slotLabels.Length];
            for (int index = 0; index < slotLabels.Length; index++)
            {
                slotLabels[index] = EnsureLabel(
                    sectionObject.transform,
                    $"Slot {index + 1}",
                    "Empty",
                    15,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(16.0f, -82.0f - index * 40.0f),
                    new Vector2(180.0f, 36.0f));
                slotButtons[index] = EnsureTextButton(slotLabels[index]);
            }

            TMP_Text depositLabel = EnsureLabel(
                sectionObject.transform,
                "Deposit",
                "Deposit Held",
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -82.0f - slotLabels.Length * 40.0f),
                new Vector2(180.0f, 36.0f));
            Button depositButton = EnsureTextButton(depositLabel);

            SurvivalCratePanel panel = EnsureComponent<SurvivalCratePanel>(sectionObject);
            panel.Configure(slotButtons, slotLabels, statusLabel, depositButton);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        static GameObject EnsureHudSection(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject sectionObject = EnsureRectChild(parent, name);
            RectTransform sectionRect = sectionObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sectionRect, anchoredPosition, size);
            Image sectionImage = EnsureComponent<Image>(sectionObject);
            Sprite hudSectionSprite = GetRoundedSprite();
            if (hudSectionSprite != null)
            {
                sectionImage.sprite = hudSectionSprite;
                sectionImage.type = Image.Type.Sliced;
            }
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
            Sprite roundedSprite = GetRoundedSprite();
            if (roundedSprite != null)
            {
                background.sprite = roundedSprite;
                background.type = Image.Type.Sliced;
            }
            background.color = ControlNormalColor;

            GameObject checkmarkObject = EnsureRectChild(backgroundObject.transform, "Checkmark");
            RectTransform checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = Vector2.zero;
            checkmarkRect.anchorMax = Vector2.one;
            checkmarkRect.offsetMin = new Vector2(7.0f, 7.0f);
            checkmarkRect.offsetMax = new Vector2(-7.0f, -7.0f);
            Image checkmark = EnsureComponent<Image>(checkmarkObject);
            Sprite checkmarkSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Checkmark.psd");
            if (checkmarkSprite != null)
                checkmark.sprite = checkmarkSprite;
            checkmark.color = AccentColor;

            EnsureLabel(
                toggleObject.transform,
                "Label",
                label,
                30,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(60.0f, -4.0f),
                new Vector2(380.0f, 56.0f));

            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.colors = new ColorBlock
            {
                normalColor      = ControlNormalColor,
                highlightedColor = ControlHighlightColor,
                pressedColor     = ControlPressedColor,
                selectedColor    = ControlSelectedColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };
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
                28,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(220.0f, 40.0f),
                TextDimColor);

            GameObject sliderObject = EnsureRectChild(rowObject.transform, "Slider");
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, new Vector2(0.0f, -48.0f), new Vector2(420.0f, 36.0f));
            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 15.0f;
            slider.maxValue = 90.0f;
            slider.wholeNumbers = true;

            Sprite roundedSprite = GetRoundedSprite();

            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 0.30f);
            backgroundRect.anchorMax = new Vector2(1.0f, 0.70f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = EnsureComponent<Image>(backgroundObject);
            if (roundedSprite != null)
            {
                background.sprite = roundedSprite;
                background.type = Image.Type.Sliced;
            }
            background.color = ControlNormalColor;

            GameObject fillAreaObject = EnsureRectChild(sliderObject.transform, "Fill Area");
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(10.0f, 0.0f);
            fillAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);

            GameObject fillObject = EnsureRectChild(fillAreaObject.transform, "Fill");
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0.0f, 0.30f);
            fillRect.anchorMax = new Vector2(1.0f, 0.70f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = EnsureComponent<Image>(fillObject);
            if (roundedSprite != null)
            {
                fill.sprite = roundedSprite;
                fill.type = Image.Type.Sliced;
            }
            fill.color = AccentColor;

            GameObject handleAreaObject = EnsureRectChild(sliderObject.transform, "Handle Slide Area");
            RectTransform handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10.0f, 0.0f);
            handleAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);

            GameObject handleObject = EnsureRectChild(handleAreaObject.transform, "Handle");
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0.0f, 0.5f);
            handleRect.anchorMax = new Vector2(0.0f, 0.5f);
            handleRect.sizeDelta = new Vector2(36.0f, 36.0f);
            Image handle = EnsureComponent<Image>(handleObject);
            Sprite knobSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            if (knobSprite != null)
                handle.sprite = knobSprite;
            handle.color = TextPrimaryColor;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.colors = new ColorBlock
            {
                normalColor      = TextPrimaryColor,
                highlightedColor = AccentHighlightColor,
                pressedColor     = AccentColor,
                selectedColor    = AccentColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };
            slider.value = value;
            return slider;
        }

        static Slider EnsureVignetteSlider(Transform parent, float value, Vector2 anchoredPosition)
        {
            GameObject rowObject = EnsureRectChild(parent, "Vignette Slider");
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rowRect, anchoredPosition, new Vector2(456.0f, 88.0f));

            EnsureLabel(rowObject.transform, "Label", "Strength", 32, TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 0.0f), new Vector2(220.0f, 40.0f));

            GameObject sliderObject = EnsureRectChild(rowObject.transform, "Slider");
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, new Vector2(0.0f, -48.0f), new Vector2(420.0f, 32.0f));

            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0.0f;
            slider.maxValue = 1.0f;
            slider.wholeNumbers = false;

            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 0.35f);
            backgroundRect.anchorMax = new Vector2(1.0f, 0.65f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            EnsureComponent<Image>(backgroundObject).color = ComfortMenuControlColor;

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
            EnsureComponent<Image>(fillObject).color = ComfortMenuAccentColor;

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

            // Rounded 9-slice background using the Unity built-in UI sprite.
            Image image = EnsureComponent<Image>(buttonObject);
            Sprite roundedSprite = GetRoundedSprite();
            if (roundedSprite != null)
            {
                image.sprite = roundedSprite;
                image.type = Image.Type.Sliced;
            }
            image.color = ControlNormalColor;

            Button button = EnsureComponent<Button>(buttonObject);
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor      = ControlNormalColor,
                highlightedColor = ControlHighlightColor,
                pressedColor     = ControlPressedColor,
                selectedColor    = ControlSelectedColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };

            EnsureLabel(
                buttonObject.transform,
                "Label",
                label,
                26,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            TextMeshProUGUI buttonLabel = buttonObject.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            RectTransform labelRect = buttonLabel.GetComponent<RectTransform>();
            labelRect.offsetMin = new Vector2(8.0f, 4.0f);
            labelRect.offsetMax = new Vector2(-8.0f, -4.0f);
            return button;
        }

        static Button EnsureTextButton(TMP_Text label)
        {
            Button button = EnsureComponent<Button>(label.gameObject);
            label.raycastTarget = true;
            button.targetGraphic = label;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor      = Color.white,
                highlightedColor = AccentHighlightColor,
                pressedColor     = AccentColor,
                selectedColor    = AccentColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };
            return button;
        }

        static TMP_InputField EnsureInputFieldControl(
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

            // Remove legacy InputField if present (migration).
            InputField legacyInput = inputObject.GetComponent<InputField>();
            if (legacyInput != null)
                UnityEngine.Object.DestroyImmediate(legacyInput);

            Image image = EnsureComponent<Image>(inputObject);
            Sprite roundedSprite = GetRoundedSprite();
            if (roundedSprite != null)
            {
                image.sprite = roundedSprite;
                image.type = Image.Type.Sliced;
            }
            image.color = ControlNormalColor;

            TMP_InputField input = EnsureComponent<TMP_InputField>(inputObject);
            input.targetGraphic = image;
            input.text = value;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;

            TextMeshProUGUI textComp = EnsureLabel(
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
            textComp.richText = false;

            TextMeshProUGUI placeholderText = EnsureLabel(
                inputObject.transform,
                "Placeholder",
                placeholder,
                24,
                TextAnchor.MiddleLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.0f, 0.5f),
                new Vector2(18.0f, 0.0f),
                new Vector2(-36.0f, 0.0f),
                new Color(0.65f, 0.70f, 0.75f, 0.60f));

            input.textComponent = textComp;
            input.placeholder = placeholderText;

            // Native VR text entry: open the Quest system keyboard when the field is selected.
            BlockiverseSystemKeyboardField keyboardField = EnsureComponent<BlockiverseSystemKeyboardField>(inputObject);
            keyboardField.Configure(input);

            return input;
        }

        // Returns a TextMeshProUGUI label so the caller can set .text; also removes any legacy
        // UnityEngine.UI.Text on the same object to avoid double-rendering during migration.
        static TextMeshProUGUI EnsureLabel(
            Transform parent,
            string name,
            string label,
            int fontSize,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            Color? colorOverride = null)
        {
            GameObject labelObject = EnsureRectChild(parent, name);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = anchorMin;
            labelRect.anchorMax = anchorMax;
            labelRect.pivot = pivot;
            labelRect.anchoredPosition = anchoredPosition;
            labelRect.sizeDelta = size;

            // Remove legacy Text if present (idempotent migration).
            Text legacyText = labelObject.GetComponent<Text>();
            if (legacyText != null)
                UnityEngine.Object.DestroyImmediate(legacyText);

            TextMeshProUGUI tmp = EnsureComponent<TextMeshProUGUI>(labelObject);
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.color = colorOverride ?? TextPrimaryColor;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;

            // Map TextAnchor to TMP alignment.
            tmp.alignment = alignment switch
            {
                TextAnchor.UpperLeft    => TextAlignmentOptions.TopLeft,
                TextAnchor.UpperCenter  => TextAlignmentOptions.Top,
                TextAnchor.UpperRight   => TextAlignmentOptions.TopRight,
                TextAnchor.MiddleLeft   => TextAlignmentOptions.MidlineLeft,
                TextAnchor.MiddleCenter => TextAlignmentOptions.Midline,
                TextAnchor.MiddleRight  => TextAlignmentOptions.MidlineRight,
                TextAnchor.LowerLeft    => TextAlignmentOptions.BottomLeft,
                TextAnchor.LowerCenter  => TextAlignmentOptions.Bottom,
                TextAnchor.LowerRight   => TextAlignmentOptions.BottomRight,
                _                       => TextAlignmentOptions.MidlineLeft,
            };

            // Use the TMP default font if available. TMP_Settings.defaultFontAsset throws a
            // NullReferenceException on first run before Essential Resources are imported, so
            // guard it. The label still renders (TMP uses an internal fallback).
            try
            {
                TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
                if (defaultFont != null)
                    tmp.font = defaultFont;
            }
            catch
            {
                // TMP_Settings not yet initialized — font will be assigned on next bootstrap.
            }

            return tmp;
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

        // Returns Unity's built-in 9-slice rounded-rectangle sprite ("Background.psd").
        // When set on an Image with Image.Type.Sliced it produces rounded corners at any size.
        // Returns null when running without the UISprite built-ins (very rare; handled gracefully by callers).
        static Sprite GetRoundedSprite() => Resources.GetBuiltinResource<Sprite>("UI/Skin/Background.psd");

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

            // Collision capsule so gravity/jumping land on the voxel terrain. Added before the body
            // transformer so it auto-binds a CharacterControllerBodyManipulator when it initializes.
            CharacterController characterController = rig.GetComponent<CharacterController>();

            if (characterController == null)
                characterController = rig.AddComponent<CharacterController>();

            BlockiverseInputRig.ConfigureCharacterController(characterController);

            XRBodyTransformer bodyTransformer = rig.GetComponent<XRBodyTransformer>();

            if (bodyTransformer == null)
                bodyTransformer = rig.AddComponent<XRBodyTransformer>();

            bodyTransformer.xrOrigin = origin;

            LocomotionMediator mediator = rig.GetComponent<LocomotionMediator>();

            if (mediator == null)
                mediator = rig.AddComponent<LocomotionMediator>();

            // Gravity must exist before Jump: JumpProvider disables itself in Awake without a GravityProvider.
            GravityProvider gravityProvider = rig.GetComponent<GravityProvider>();

            if (gravityProvider == null)
                gravityProvider = rig.AddComponent<GravityProvider>();

            gravityProvider.mediator = mediator;
            gravityProvider.enabled = true;
            gravityProvider.useGravity = true;
            gravityProvider.useLocalSpaceGravity = true;
            gravityProvider.sphereCastLayerMask = GetInteractionLayerMask();
            gravityProvider.sphereCastTriggerInteraction = QueryTriggerInteraction.Ignore;

            JumpProvider jumpProvider = rig.GetComponent<JumpProvider>();

            if (jumpProvider == null)
                jumpProvider = rig.AddComponent<JumpProvider>();

            jumpProvider.mediator = mediator;
            jumpProvider.jumpHeight = JumpHeightMeters;
            jumpProvider.disableGravityDuringJump = false;
            jumpProvider.unlimitedInAirJumps = false;
            jumpProvider.inAirJumpCount = 0;

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
            inputRig.ConfigureLocomotion(teleport, snapTurn, heightReset, continuousMove, mediator, bodyTransformer, settings, continuousTurn, gravityProvider, jumpProvider, characterController);

            BlockiverseAudioCuePlayer audioCuePlayer = rig.GetComponent<BlockiverseAudioCuePlayer>();
            inputRig.ConfigureTeleportFeedback(audioCuePlayer);
        }

        // ── Game menu system ─────────────────────────────────────────────────────────────────────

        static void EnsureXrRigGameMenus(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            var (titleMenu, titlePresenter) = EnsureActionMenuPanel(
                cameraOffset, TitleMenuName, ActionMenuSize, head, buttonCount: 6, sortOrder: 25);
            var (pauseMenu, pausePresenter) = EnsureActionMenuPanel(
                cameraOffset, PauseMenuName, ActionMenuSize, head, buttonCount: 7, sortOrder: 25);
            var (deathMenu, deathPresenter) = EnsureActionMenuPanel(
                cameraOffset, DeathScreenName, ActionMenuSize, head, buttonCount: 3, sortOrder: 25);
            var (confirmMenu, confirmPresenter) = EnsureActionMenuPanel(
                cameraOffset, ConfirmDialogName, ConfirmDialogSize, head, buttonCount: 2, sortOrder: 30);

            var (newWorldPanel, newWorldPresenter) = EnsureNewWorldMenuPanel(cameraOffset, head);
            var (loadWorldPanel, loadWorldPresenter) = EnsureLoadWorldMenuPanel(cameraOffset, head);
            var (settingsMenu, settingsPresenter) = EnsureSettingsMenuPanel(cameraOffset, head);
            var (stationPanel, stationPresenter) = EnsureStationMenuPanel(cameraOffset, head);
            var (lanPresenter, lanCloseButton) = EnsureLanMultiplayerMenuPanel(cameraOffset, head);
            var (audioPanel, audioPresenter, audioCloseButton) = EnsureAudioSettingsMenuPanel(cameraOffset, head);
            var (controlsPresenter, controlsCloseButton) = EnsureControlsMenuPanel(cameraOffset, head);
            var (worldDetailsPanel, worldDetailsMenu, worldDetailsPresenter) = EnsureWorldDetailsMenuPanel(cameraOffset, head);
            var (creativeToolsPanel, creativeToolsPresenter, creativeToolsCloseButton) = EnsureCreativeToolsMenuPanel(cameraOffset, head);

            BlockiverseMenuController controller = EnsureComponent<BlockiverseMenuController>(rig);
            controller.Configure(inputRig, titleMenu, pauseMenu, deathMenu, confirmMenu,
                newWorldPanel, loadWorldPanel, settingsMenu, worldDetailsPanel, worldDetailsMenu);
            controller.ConfigurePresenters(
                titlePresenter, pausePresenter, deathPresenter, confirmPresenter,
                newWorldPresenter, loadWorldPresenter, settingsPresenter, stationPresenter,
                lanPresenter, audioPresenter, controlsPresenter, worldDetailsPresenter,
                creativeToolsPresenter);
            controller.ConfigureStationPanel(stationPanel);

            if (creativeToolsCloseButton != null)
            {
                RemovePersistentListeners(creativeToolsCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseCreativeToolsScreen));
                UnityEventTools.AddPersistentListener(creativeToolsCloseButton.onClick, controller.CloseCreativeToolsScreen);
                EditorUtility.SetDirty(creativeToolsCloseButton);
            }

            EditorUtility.SetDirty(creativeToolsPanel);

            if (lanCloseButton != null)
            {
                RemovePersistentListeners(lanCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseLanMultiplayerScreen));
                UnityEventTools.AddPersistentListener(lanCloseButton.onClick, controller.CloseLanMultiplayerScreen);
                EditorUtility.SetDirty(lanCloseButton);
            }

            if (audioCloseButton != null)
            {
                RemovePersistentListeners(audioCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseAudioSettingsScreen));
                UnityEventTools.AddPersistentListener(audioCloseButton.onClick, controller.CloseAudioSettingsScreen);
                EditorUtility.SetDirty(audioCloseButton);
            }

            if (controlsCloseButton != null)
            {
                RemovePersistentListeners(controlsCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseControlsScreen));
                UnityEventTools.AddPersistentListener(controlsCloseButton.onClick, controller.CloseControlsScreen);
                EditorUtility.SetDirty(controlsCloseButton);
            }

            EditorUtility.SetDirty(audioPanel);

            // The session coordinator implements the menu's save/load/new-world/continue verbs.
            BlockiverseWorldSessionController sessionController = EnsureComponent<BlockiverseWorldSessionController>(rig);
            EditorUtility.SetDirty(sessionController);

            if (inputRig != null)
            {
                // The controller subscribes to MenuPressed at runtime (Start → AddListener), so a
                // persistent listener here would double-fire the pause toggle. Only scrub any stale
                // persistent listener a previous bootstrap left on the prefab.
                RemovePersistentListeners(inputRig.MenuPressed, controller, nameof(BlockiverseMenuController.OnMenuPressed));
                EditorUtility.SetDirty(inputRig);
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(rig);
        }

        // Builds the LAN multiplayer panel on the rig: host/join/stop controls plus a close
        // button, presented through the same world-space presenter stack as the other menus.
        static (BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureLanMultiplayerMenuPanel(
            Transform parent,
            Transform head)
        {
            float width = LanMultiplayerPanelSize.x;
            float height = LanMultiplayerPanelSize.y;

            GameObject panelRoot = EnsureRectChild(parent, LanMultiplayerPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            EnsureLabel(
                bg.transform, "Title", "LAN Multiplayer", 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28.0f, -18.0f), new Vector2(width - 56.0f, 48.0f));

            TMP_InputField addressInput = EnsureInputFieldControl(
                bg.transform,
                "Address Input",
                "Host address",
                BlockiverseNetworkConfig.DefaultAddress,
                new Vector2(28.0f, -100.0f),
                new Vector2(width - 56.0f, 58.0f));

            Button hostButton = EnsureButtonControl(
                bg.transform, "Host Button", "Host", new Vector2(28.0f, -180.0f), new Vector2(164.0f, 54.0f));
            Button joinButton = EnsureButtonControl(
                bg.transform, "Join Button", "Join", new Vector2(228.0f, -180.0f), new Vector2(164.0f, 54.0f));
            Button stopButton = EnsureButtonControl(
                bg.transform, "Stop Button", "Stop", new Vector2(428.0f, -180.0f), new Vector2(164.0f, 54.0f));

            TextMeshProUGUI statusText = EnsureLabel(
                bg.transform, "Status",
                $"LAN session stopped. Join address defaults to {BlockiverseNetworkConfig.DefaultAddress}.",
                22, TextAnchor.UpperLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28.0f, -256.0f), new Vector2(width - 56.0f, 120.0f),
                TextDimColor);

            Button closeButton = EnsureButtonControl(
                bg.transform, "Close Button", "Close", new Vector2(28.0f, -(height - 80.0f)), new Vector2(width - 56.0f, 54.0f));

            BlockiverseMultiplayerSessionMenu menu = EnsureComponent<BlockiverseMultiplayerSessionMenu>(panelRoot);
            menu.ConfigureControls(hostButton, joinButton, stopButton, addressInput, statusText);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (presenter, closeButton);
        }

        // Builds a world-space panel canvas with a background, title label, N full-width action
        // buttons (text-button style), and a status label. Returns the wired BlockiverseActionMenu
        // and its presenter so callers can chain further configuration.
        static (BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter) EnsureActionMenuPanel(
            Transform parent,
            string name,
            Vector2 size,
            Transform head,
            int buttonCount = 5,
            int sortOrder = 25)
        {
            GameObject panelRoot = EnsureRectChild(parent, name);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = sortOrder;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            // Header divider strip
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.0f, 1.0f);
            headerRect.anchorMax = new Vector2(1.0f, 1.0f);
            headerRect.pivot = new Vector2(0.0f, 1.0f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0.0f, 72.0f);
            Image headerImage = EnsureComponent<Image>(header);
            headerImage.color = PanelHeaderColor;

            TMP_Text titleLabel = EnsureLabel(
                bg.transform, "Title", name, 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28.0f, -18.0f), new Vector2(size.x - 56.0f, 48.0f));

            var buttons = new Button[buttonCount];
            var labels = new TMP_Text[buttonCount];
            for (int i = 0; i < buttonCount; i++)
            {
                float buttonY = -100.0f - i * 58.0f;
                Button btn = EnsureButtonControl(
                    bg.transform,
                    $"Action {i + 1}",
                    string.Empty,
                    new Vector2(28.0f, buttonY),
                    new Vector2(size.x - 56.0f, 50.0f));
                Transform labelTransform = btn.transform.Find("Label");
                buttons[i] = btn;
                labels[i] = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
            }

            TMP_Text statusLabel = EnsureLabel(
                bg.transform, "Status", string.Empty, 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28.0f, -100.0f - buttonCount * 58.0f), new Vector2(size.x - 56.0f, 36.0f),
                TextDimColor);

            BlockiverseActionMenu actionMenu = EnsureComponent<BlockiverseActionMenu>(panelRoot);
            actionMenu.Configure(titleLabel, buttons, labels, statusLabel);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(actionMenu);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (actionMenu, presenter);
        }

        // Builds the New World config panel: name/seed text inputs + 5 cycle-selector rows
        // (GameMode, Difficulty, WorldSize, WorldPreset, StartingBiome) + Create/Cancel buttons.
        static (BlockiverseNewWorldPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureNewWorldMenuPanel(
            Transform parent,
            Transform head)
        {
            const float W = 620.0f;
            const float H = 720.0f;

            GameObject panelRoot = EnsureRectChild(parent, NewWorldPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            // Header
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0, 1);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0, 72);
            EnsureComponent<Image>(header).color = PanelHeaderColor;

            EnsureLabel(bg.transform, "Title", "New World", 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -18), new Vector2(W - 56, 48));

            // Name input row
            EnsureLabel(bg.transform, "Name Label", "World Name", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -96), new Vector2(150, 44), TextDimColor);
            TMP_InputField nameInput = EnsureInputFieldControl(
                bg.transform, "Name Input", "Enter name...", NewWorldConfig.DefaultName,
                new Vector2(186, -96), new Vector2(W - 214, 48));

            // Seed input row
            EnsureLabel(bg.transform, "Seed Label", "Seed", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -158), new Vector2(150, 44), TextDimColor);
            TMP_InputField seedInput = EnsureInputFieldControl(
                bg.transform, "Seed Input", "0", "0",
                new Vector2(186, -158), new Vector2(W - 214, 48));

            // 5 cycle rows: GameMode, Difficulty, WorldSize, WorldPreset, StartingBiome
            string[] rowLabels = { "Game Mode", "Difficulty", "World Size", "World Preset", "Starting Biome" };
            string[] defaultValues = { "survival", "normal", "small", "survival_terrain", "balanced" };
            const float rowStartY = -230;
            const float rowH = 56;
            var backButtons = new Button[rowLabels.Length];
            var nextButtons = new Button[rowLabels.Length];
            var valueLabels = new TMP_Text[rowLabels.Length];

            for (int i = 0; i < rowLabels.Length; i++)
            {
                float rowY = rowStartY - i * rowH;

                // Row background
                GameObject rowBg = EnsureRectChild(bg.transform, $"Row {rowLabels[i]}");
                RectTransform rowRect = rowBg.GetComponent<RectTransform>();
                ConfigureTopLeftRect(rowRect, new Vector2(28, rowY), new Vector2(W - 56, rowH - 4));
                EnsureComponent<Image>(rowBg).color = PanelHeaderColor;

                // Field name
                EnsureLabel(rowBg.transform, "Label", rowLabels[i], 20, TextAnchor.MiddleLeft,
                    Vector2.zero, Vector2.one, new Vector2(0, 0.5f),
                    new Vector2(8, 0), new Vector2(160, rowH - 4), TextDimColor);

                // Back button ◀
                backButtons[i] = EnsureButtonControl(rowBg.transform, "Back",
                    "<", new Vector2(172, -(rowH - 4) * 0.5f + (rowH - 4) * 0.5f - 22), new Vector2(44, 44));
                ConfigureTopLeftRect(
                    backButtons[i].GetComponent<RectTransform>(),
                    new Vector2(172, -((rowH - 4) / 2 - 22)), new Vector2(44, 44));

                // Value label (center)
                valueLabels[i] = EnsureLabel(rowBg.transform, "Value", defaultValues[i], 22, TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    new Vector2(0, 0), new Vector2(0, 0));
                // Position value label between the two buttons
                RectTransform valRect = valueLabels[i].GetComponent<RectTransform>();
                valRect.anchorMin = new Vector2(0, 0);
                valRect.anchorMax = new Vector2(1, 1);
                valRect.offsetMin = new Vector2(224, 0);
                valRect.offsetMax = new Vector2(-52, 0);

                // Next button ▶
                nextButtons[i] = EnsureButtonControl(rowBg.transform, "Next",
                    ">", Vector2.zero, new Vector2(44, 44));
                ConfigureTopLeftRect(
                    nextButtons[i].GetComponent<RectTransform>(),
                    new Vector2(W - 56 - 52, -((rowH - 4) / 2 - 22)), new Vector2(44, 44));
            }

            // Create / Cancel buttons
            float actionRowY = rowStartY - rowLabels.Length * rowH - 32;
            Button createButton = EnsureButtonControl(bg.transform, "Create Button", "Create World",
                new Vector2(28, actionRowY), new Vector2((W - 84) / 2, 52));
            Button cancelButton = EnsureButtonControl(bg.transform, "Cancel Button", "Cancel",
                new Vector2(28 + (W - 84) / 2 + 28, actionRowY), new Vector2((W - 84) / 2, 52));

            // Error label
            TMP_Text errorLabel = EnsureLabel(bg.transform, "Error", string.Empty, 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, actionRowY - 60), new Vector2(W - 56, 44),
                new Color(0.95f, 0.40f, 0.30f, 1.0f));

            BlockiverseNewWorldPanel panel = EnsureComponent<BlockiverseNewWorldPanel>(panelRoot);
            panel.Configure(nameInput, seedInput, backButtons, nextButtons, valueLabels,
                createButton, cancelButton, errorLabel);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (panel, presenter);
        }

        // Builds the Load World panel: up to 6 save-entry buttons + Load/Cancel footer buttons.
        static (BlockiverseLoadWorldPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureLoadWorldMenuPanel(
            Transform parent,
            Transform head)
        {
            const float W = 620.0f;
            const float H = 600.0f;
            const int MaxEntries = 6;

            GameObject panelRoot = EnsureRectChild(parent, LoadWorldPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            // Header
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0, 1);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0, 72);
            EnsureComponent<Image>(header).color = PanelHeaderColor;

            EnsureLabel(bg.transform, "Title", "Load World", 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -18), new Vector2(W - 56, 48));

            // Save entry rows
            var entryButtons = new Button[MaxEntries];
            var entryLabels = new TMP_Text[MaxEntries];
            for (int i = 0; i < MaxEntries; i++)
            {
                float rowY = -96 - i * 54;
                entryLabels[i] = EnsureLabel(bg.transform, $"Save {i + 1}", string.Empty, 20,
                    TextAnchor.MiddleLeft,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(28, rowY), new Vector2(W - 56, 48));
                entryButtons[i] = EnsureTextButton(entryLabels[i]);
            }

            // Selection label
            TMP_Text selectionLabel = EnsureLabel(bg.transform, "Selection", "No save selected", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -96 - MaxEntries * 54 - 12), new Vector2(W - 56, 36));

            // Load / Details / Cancel buttons (Details opens the §6.5 World Details screen).
            float footerY = -96 - MaxEntries * 54 - 64;
            float footerButtonWidth = (W - 112) / 3;
            Button loadButton = EnsureButtonControl(bg.transform, "Load Button", "Load World",
                new Vector2(28, footerY), new Vector2(footerButtonWidth, 52));
            Button detailsButton = EnsureButtonControl(bg.transform, "Details Button", "Details",
                new Vector2(28 + footerButtonWidth + 28, footerY), new Vector2(footerButtonWidth, 52));
            Button cancelButton = EnsureButtonControl(bg.transform, "Cancel Button", "Cancel",
                new Vector2(28 + (footerButtonWidth + 28) * 2, footerY), new Vector2(footerButtonWidth, 52));

            BlockiverseLoadWorldPanel panel = EnsureComponent<BlockiverseLoadWorldPanel>(panelRoot);
            panel.Configure(entryButtons, entryLabels, loadButton, cancelButton, selectionLabel, detailsButton);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (panel, presenter);
        }

        // Builds the Settings hub as a four-entry action menu (Comfort / Audio / Controls / Close).
        static (BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter) EnsureSettingsMenuPanel(
            Transform parent, Transform head)
        {
            // The settings hub is a plain four-entry action menu (Comfort / Audio / Controls /
            // Close, set by the menu controller at Start). Rebuild the panel from scratch so the
            // pre-hub placeholder layout (title + placeholder + lone close button) never lingers
            // in regenerated rigs.
            Transform stale = parent.Find(SettingsPanelName);
            if (stale != null && stale.Find("Panel/Placeholder") != null)
                UnityEngine.Object.DestroyImmediate(stale.gameObject);

            (BlockiverseActionMenu settingsMenu, BlockiverseWorldSpacePanelPresenter presenter) =
                EnsureActionMenuPanel(parent, SettingsPanelName, ActionMenuSize, head, buttonCount: 4, sortOrder: 25);
            settingsMenu.SetMenu("Settings", MenuActions.Settings);

            EditorUtility.SetDirty(settingsMenu);
            return (settingsMenu, presenter);
        }

        // Canonical controller mapping description, shared by the first-launch mapping popup and
        // the Settings → Controls reference screen so the two can never drift apart.
        const string ControllerMappingText =
            "Left stick: move\n" +
            "Right stick: snap turn\n" +
            "Right stick hold up: teleport aim, release to land\n" +
            "Right trigger: press UI or break blocks\n" +
            "Right grip: place or use\n" +
            "Left grip: blocks menu\n" +
            "Right A: jump\n" +
            "Right B: toggle block editing\n" +
            "Menu: pause";

        // Builds the audio/feedback settings screen: volume sliders, feedback toggles, and a
        // Close button (wired by the caller to the menu controller's close hook).
        static (BlockiverseAudioSettingsPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton)
            EnsureAudioSettingsMenuPanel(Transform parent, Transform head)
        {
            const float W = 540.0f;
            const float H = 956.0f;

            GameObject panelRoot = EnsureRectChild(parent, AudioSettingsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            EnsureLabel(bg.transform, "Title", "Audio & Feedback", 32, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -28), new Vector2(W - 56, 52));

            Slider master = EnsureSettingsSlider(bg.transform, "Master Volume Slider", "Master Volume", 1.0f, new Vector2(28, -96));
            Slider effects = EnsureSettingsSlider(bg.transform, "Effects Volume Slider", "Effects Volume", 1.0f, new Vector2(28, -192));
            Slider ui = EnsureSettingsSlider(bg.transform, "UI Volume Slider", "UI Volume", 1.0f, new Vector2(28, -288));
            Slider weather = EnsureSettingsSlider(bg.transform, "Weather Volume Slider", "Weather Volume", 1.0f, new Vector2(28, -384));
            Slider music = EnsureSettingsSlider(bg.transform, "Music Volume Slider", "Music Volume", 0.5f, new Vector2(28, -480));
            Slider hapticStrength = EnsureSettingsSlider(bg.transform, "Haptic Strength Slider", "Haptic Strength", 1.0f, new Vector2(28, -576));

            Toggle mute = EnsureToggleControl(bg.transform, "Mute All Toggle", "Mute All", false, new Vector2(28, -672));
            Toggle haptics = EnsureToggleControl(bg.transform, "Haptics Toggle", "Haptics", true, new Vector2(28, -728));
            Toggle reducedFlash = EnsureToggleControl(bg.transform, "Reduced Flash Toggle", "Reduced Flash", false, new Vector2(28, -784));
            Toggle reducedParticles = EnsureToggleControl(bg.transform, "Reduced Particles Toggle", "Reduced Particles", false, new Vector2(28, -840));

            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close",
                new Vector2(28, -902), new Vector2(160, 48));

            BlockiverseAudioSettingsPanel panel = EnsureComponent<BlockiverseAudioSettingsPanel>(panelRoot);
            panel.Configure(
                parent.GetComponentInParent<BlockiverseFeedbackSettings>(),
                master, effects, ui, weather, music, hapticStrength,
                mute, haptics, reducedFlash, reducedParticles);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);
            return (panel, presenter, closeButton);
        }

        // Builds the Creative Tools screen (§12): corner A/B region selection, fill/replace/
        // delete/copy/paste with region undo/redo, tree/ruin spawners, pick-block, and the
        // environment controls (time-of-day, day speed, weather cycle).
        static (BlockiverseCreativeToolsPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton)
            EnsureCreativeToolsMenuPanel(Transform parent, Transform head)
        {
            const float W = 560.0f;
            const float H = 820.0f;

            GameObject panelRoot = EnsureRectChild(parent, CreativeToolsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            EnsureLabel(bg.transform, "Title", "Creative Tools", 32, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -28), new Vector2(W - 56, 52));

            TMP_Text statusLabel = EnsureLabel(bg.transform, "Status", "Aim at blocks to select corners.", 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -78), new Vector2(W - 56, 34), TextDimColor);

            TMP_Text cornersLabel = EnsureLabel(bg.transform, "Corners", "A: —    B: —", 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -116), new Vector2(W - 56, 32));

            Button setAButton = EnsureButtonControl(bg.transform, "Set A Button", "Set A", new Vector2(28, -156), new Vector2(150, 48));
            Button setBButton = EnsureButtonControl(bg.transform, "Set B Button", "Set B", new Vector2(196, -156), new Vector2(150, 48));
            Button pickButton = EnsureButtonControl(bg.transform, "Pick Block Button", "Pick Block", new Vector2(364, -156), new Vector2(150, 48));

            Button fillButton = EnsureButtonControl(bg.transform, "Fill Button", "Fill", new Vector2(28, -216), new Vector2(150, 48));
            Button replaceButton = EnsureButtonControl(bg.transform, "Replace Button", "Replace", new Vector2(196, -216), new Vector2(150, 48));
            Button deleteButton = EnsureButtonControl(bg.transform, "Delete Button", "Delete", new Vector2(364, -216), new Vector2(150, 48));

            Button copyButton = EnsureButtonControl(bg.transform, "Copy Button", "Copy", new Vector2(28, -276), new Vector2(150, 48));
            Button pasteButton = EnsureButtonControl(bg.transform, "Paste Button", "Paste", new Vector2(196, -276), new Vector2(150, 48));

            Button undoButton = EnsureButtonControl(bg.transform, "Undo Button", "Undo Edit", new Vector2(28, -336), new Vector2(150, 48));
            Button redoButton = EnsureButtonControl(bg.transform, "Redo Button", "Redo Edit", new Vector2(196, -336), new Vector2(150, 48));

            Button treeButton = EnsureButtonControl(bg.transform, "Spawn Tree Button", "Spawn Tree", new Vector2(28, -396), new Vector2(150, 48));
            Button ruinButton = EnsureButtonControl(bg.transform, "Spawn Ruin Button", "Spawn Ruin", new Vector2(196, -396), new Vector2(150, 48));

            Slider timeSlider = EnsureSettingsSlider(bg.transform, "Time Of Day Slider", "Time of Day", 0.25f, new Vector2(28, -462));
            Slider speedSlider = EnsureSettingsSlider(bg.transform, "Day Speed Slider", "Day Speed", 1.0f, new Vector2(28, -558), minValue: 0.0f, maxValue: 4.0f);

            TMP_Text weatherLabel = EnsureLabel(bg.transform, "Weather Label", "Weather: Clear", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -660), new Vector2(300, 40));
            Button weatherButton = EnsureButtonControl(bg.transform, "Cycle Weather Button", "Cycle Weather", new Vector2(346, -656), new Vector2(186, 48));

            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", new Vector2(28, -730), new Vector2(150, 48));

            BlockiverseCreativeToolsPanel panel = EnsureComponent<BlockiverseCreativeToolsPanel>(panelRoot);
            panel.Configure(null, null, null, cornersLabel, statusLabel, weatherLabel, timeSlider, speedSlider);

            WireButton(setAButton, panel, nameof(BlockiverseCreativeToolsPanel.SetCornerA), panel.SetCornerA);
            WireButton(setBButton, panel, nameof(BlockiverseCreativeToolsPanel.SetCornerB), panel.SetCornerB);
            WireButton(pickButton, panel, nameof(BlockiverseCreativeToolsPanel.PickBlock), panel.PickBlock);
            WireButton(fillButton, panel, nameof(BlockiverseCreativeToolsPanel.FillRegion), panel.FillRegion);
            WireButton(replaceButton, panel, nameof(BlockiverseCreativeToolsPanel.ReplaceRegion), panel.ReplaceRegion);
            WireButton(deleteButton, panel, nameof(BlockiverseCreativeToolsPanel.DeleteRegion), panel.DeleteRegion);
            WireButton(copyButton, panel, nameof(BlockiverseCreativeToolsPanel.CopyRegion), panel.CopyRegion);
            WireButton(pasteButton, panel, nameof(BlockiverseCreativeToolsPanel.PasteRegion), panel.PasteRegion);
            WireButton(undoButton, panel, nameof(BlockiverseCreativeToolsPanel.UndoEdit), panel.UndoEdit);
            WireButton(redoButton, panel, nameof(BlockiverseCreativeToolsPanel.RedoEdit), panel.RedoEdit);
            WireButton(treeButton, panel, nameof(BlockiverseCreativeToolsPanel.SpawnTree), panel.SpawnTree);
            WireButton(ruinButton, panel, nameof(BlockiverseCreativeToolsPanel.SpawnRuin), panel.SpawnRuin);
            WireButton(weatherButton, panel, nameof(BlockiverseCreativeToolsPanel.CycleWeather), panel.CycleWeather);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);
            return (panel, presenter, closeButton);
        }

        // Replaces any previous persistent listener for the same target/method, then adds it.
        static void WireButton(Button button, UnityEngine.Object target, string methodName, UnityEngine.Events.UnityAction action)
        {
            RemovePersistentListeners(button.onClick, target, methodName);
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }

        // Builds the read-only controls reference screen (Settings → Controls).
        static (BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureControlsMenuPanel(
            Transform parent, Transform head)
        {
            const float W = 540.0f;
            const float H = 480.0f;

            GameObject panelRoot = EnsureRectChild(parent, ControlsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            EnsureLabel(bg.transform, "Title", "Controls", 32, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -28), new Vector2(W - 56, 52));

            EnsureLabel(bg.transform, "Mapping Text", ControllerMappingText, 22, TextAnchor.UpperLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -96), new Vector2(W - 56, 290));

            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close",
                new Vector2(28, -404), new Vector2(160, 48));

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);
            return (presenter, closeButton);
        }

        // Builds the World Details screen (§6.5): save metadata, a rename field, and the
        // Play/Rename/Duplicate/Delete/Back management actions.
        static (BlockiverseWorldDetailsPanel panel, BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter)
            EnsureWorldDetailsMenuPanel(Transform parent, Transform head)
        {
            const float W = 560.0f;
            const float H = 620.0f;

            GameObject panelRoot = EnsureRectChild(parent, WorldDetailsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            TMP_Text titleLabel = EnsureLabel(bg.transform, "Title", "World Details", 32, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -28), new Vector2(W - 56, 52));

            TMP_Text metadataLabel = EnsureLabel(bg.transform, "Metadata", string.Empty, 22, TextAnchor.UpperLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -96), new Vector2(W - 56, 120));

            EnsureLabel(bg.transform, "Rename Label", "Name", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -232), new Vector2(120, 40), TextDimColor);

            TMP_InputField renameField = EnsureInputFieldControl(bg.transform, "Rename Field",
                "World name", string.Empty, new Vector2(28, -274), new Vector2(W - 56, 56));

            // §6.5 management actions in two rows.
            Button playButton = EnsureButtonControl(bg.transform, "Play Button", "Play", new Vector2(28, -356), new Vector2(150, 52));
            Button renameButton = EnsureButtonControl(bg.transform, "Rename Button", "Rename", new Vector2(196, -356), new Vector2(150, 52));
            Button duplicateButton = EnsureButtonControl(bg.transform, "Duplicate Button", "Duplicate", new Vector2(364, -356), new Vector2(150, 52));
            Button deleteButton = EnsureButtonControl(bg.transform, "Delete Button", "Delete", new Vector2(28, -424), new Vector2(150, 52));
            Button backButton = EnsureButtonControl(bg.transform, "Back Button", "Back", new Vector2(196, -424), new Vector2(150, 52));

            var buttons = new[] { playButton, renameButton, duplicateButton, deleteButton, backButton };
            var labels = new TMP_Text[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                Transform labelTransform = buttons[i].transform.Find("Label");
                labels[i] = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
            }

            BlockiverseActionMenu actionMenu = EnsureComponent<BlockiverseActionMenu>(panelRoot);
            actionMenu.Configure(titleLabel, buttons, labels);
            actionMenu.SetMenu("World Details", MenuActions.WorldDetails);

            BlockiverseWorldDetailsPanel panel = EnsureComponent<BlockiverseWorldDetailsPanel>(panelRoot);
            panel.Configure(metadataLabel, renameField);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(actionMenu);
            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);
            return (panel, actionMenu, presenter);
        }

        // Generic labeled slider row for settings panels (same construction as the comfort
        // sliders, parameterized by name/label/range/position; defaults to 0–1).
        static Slider EnsureSettingsSlider(Transform parent, string name, string label, float value, Vector2 anchoredPosition, float minValue = 0.0f, float maxValue = 1.0f)
        {
            GameObject rowObject = EnsureRectChild(parent, name);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rowRect, anchoredPosition, new Vector2(484.0f, 88.0f));

            EnsureLabel(
                rowObject.transform,
                "Label",
                label,
                26,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(300.0f, 40.0f),
                TextDimColor);

            GameObject sliderObject = EnsureRectChild(rowObject.transform, "Slider");
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, new Vector2(0.0f, -46.0f), new Vector2(484.0f, 36.0f));
            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.wholeNumbers = false;

            Sprite roundedSprite = GetRoundedSprite();

            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 0.30f);
            backgroundRect.anchorMax = new Vector2(1.0f, 0.70f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = EnsureComponent<Image>(backgroundObject);
            if (roundedSprite != null)
            {
                background.sprite = roundedSprite;
                background.type = Image.Type.Sliced;
            }
            background.color = ControlNormalColor;

            GameObject fillAreaObject = EnsureRectChild(sliderObject.transform, "Fill Area");
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(10.0f, 0.0f);
            fillAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);

            GameObject fillObject = EnsureRectChild(fillAreaObject.transform, "Fill");
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0.0f, 0.30f);
            fillRect.anchorMax = new Vector2(1.0f, 0.70f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = EnsureComponent<Image>(fillObject);
            if (roundedSprite != null)
            {
                fill.sprite = roundedSprite;
                fill.type = Image.Type.Sliced;
            }
            fill.color = AccentColor;

            GameObject handleAreaObject = EnsureRectChild(sliderObject.transform, "Handle Slide Area");
            RectTransform handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10.0f, 0.0f);
            handleAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);

            GameObject handleObject = EnsureRectChild(handleAreaObject.transform, "Handle");
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0.0f, 0.5f);
            handleRect.anchorMax = new Vector2(0.0f, 0.5f);
            handleRect.sizeDelta = new Vector2(36.0f, 36.0f);
            Image handle = EnsureComponent<Image>(handleObject);
            Sprite knobSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            if (knobSprite != null)
                handle.sprite = knobSprite;
            handle.color = TextPrimaryColor;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.colors = new ColorBlock
            {
                normalColor      = TextPrimaryColor,
                highlightedColor = AccentHighlightColor,
                pressedColor     = AccentColor,
                selectedColor    = AccentColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };
            slider.value = value;
            return slider;
        }

        // Builds the smelting-station panel: up to 3 input slots, 1 fuel slot, output, progress
        // slider, deposit/collect transfer buttons, and a Close button. Used for both Clay Kiln
        // (1 input) and Bellows Forge (3).
        static (BlockiverseStationPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureStationMenuPanel(
            Transform parent, Transform head)
        {
            const float W = 540.0f;
            const float H = 620.0f;

            GameObject panelRoot = EnsureRectChild(parent, StationPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            // Header
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0, 1);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0, 72);
            EnsureComponent<Image>(header).color = PanelHeaderColor;

            TMP_Text titleLabel = EnsureLabel(bg.transform, "Title", "Station", 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -18), new Vector2(W - 56, 48));

            // Input slots (forge maximum; unused ones stay empty when model has fewer)
            var inputLabels = new TMP_Text[SmeltingStationModel.MaxInputSlots];
            string[] inputNames = { "Input Slot 1", "Input Slot 2", "Input Slot 3" };
            for (int i = 0; i < inputLabels.Length; i++)
            {
                EnsureLabel(bg.transform, $"Input Label {i + 1}", $"Input {i + 1}", 20, TextAnchor.MiddleLeft,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(28, -94 - i * 46), new Vector2(160, 40), TextDimColor);
                inputLabels[i] = EnsureLabel(bg.transform, inputNames[i], "—", 22, TextAnchor.MiddleLeft,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(200, -94 - i * 46), new Vector2(W - 228, 40));
            }

            // Fuel slot
            EnsureLabel(bg.transform, "Fuel Label", "Fuel", 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -240), new Vector2(160, 40), TextDimColor);
            TMP_Text fuelLabel = EnsureLabel(bg.transform, "Fuel Slot", "No fuel", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(200, -240), new Vector2(W - 228, 40));

            // Output slot
            EnsureLabel(bg.transform, "Output Label", "Output", 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -290), new Vector2(160, 40), TextDimColor);
            TMP_Text outputLabel = EnsureLabel(bg.transform, "Output Slot", "—", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(200, -290), new Vector2(W - 228, 40));

            // Progress slider
            Slider progressSlider = EnsureHudSlider(bg.transform, "Progress", new Vector2(28, -354), new Vector2(W - 56, 20));

            // Status label
            TMP_Text statusLabel = EnsureLabel(bg.transform, "Status", "Idle", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -394), new Vector2(W - 56, 36), TextDimColor);

            // Transfer buttons: deposit the held hotbar item as input/fuel, collect the output.
            Button depositInputButton = EnsureButtonControl(bg.transform, "Deposit Input Button", "Add Input",
                new Vector2(28, -440), new Vector2(150, 52));
            Button depositFuelButton = EnsureButtonControl(bg.transform, "Deposit Fuel Button", "Add Fuel",
                new Vector2(188, -440), new Vector2(150, 52));
            Button collectOutputButton = EnsureButtonControl(bg.transform, "Collect Output Button", "Collect",
                new Vector2(348, -440), new Vector2(150, 52));

            // Close button
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close",
                new Vector2(28, -504), new Vector2(160, 52));

            BlockiverseStationPanel stationPanel = EnsureComponent<BlockiverseStationPanel>(panelRoot);
            stationPanel.Configure(titleLabel, inputLabels, fuelLabel, outputLabel, statusLabel,
                progressSlider, closeButton);
            stationPanel.ConfigureTransferControls(depositInputButton, depositFuelButton, collectOutputButton);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.ContainerOpen, BlockiverseAudioCue.ContainerClose);

            EditorUtility.SetDirty(stationPanel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (stationPanel, presenter);
        }
    }
}

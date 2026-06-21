using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
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
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using Unity.XR.CompositionLayers.UIInteraction;
using Unity.XR.CoreUtils;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
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
            BlockiverseProject.InputActionReferencesFolderPath,
            "Assets/Blockiverse/Tests/EditMode",
            "Assets/Blockiverse/Tests/PlayMode"
        };

        static readonly string[] AndroidOpenXrFeatureIds =
        {
            "com.unity.openxr.feature.metaquest",
            "com.unity.openxr.feature.input.oculustouch",
            "com.unity.openxr.feature.input.metaquestplus",
            "com.unity.openxr.feature.input.metaquestpro",
            "com.unity.openxr.feature.compositionlayers",
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
        const float GameMenuDistanceMeters = 0.95f;
        const float GameMenuVerticalOffsetMeters = -0.38f;
        const float GameMenuPitchDegrees = 10.0f;
        // All game menu panels share one world-space position; only one is visible at a time.
        static readonly Vector3 GameMenuLocalPosition = new(0.0f, 1.05f, GameMenuDistanceMeters);
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
        const string MenuCompositionSurfaceName = "Blockiverse Menu Composition Surface";
        const string MenuCompositionCanvasName = "Blockiverse Menu Canvas";
        const string MenuCompositionCursorName = "Composition Menu Cursor";
        const string XrVisualProjectionRigName = "Blockiverse XR Visual Projection Rig";
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
        const string ControllerRayOriginName = "Ray Origin";
        const string LeftAimPoseName = "Left Aim Pose";
        const string RightAimPoseName = "Right Aim Pose";
        const string LeftRayOriginName = "Left Ray Origin";
        const string RightRayOriginName = "Right Ray Origin";
        const string TunnelingVignetteName = "Tunneling Vignette";
        const string TunnelingVignettePrefabPath = "Assets/Blockiverse/VR/TunnelingVignette/TunnelingVignette.prefab";
        const string StickDeadzoneProcessor = "stickDeadzone(min=0.2,max=0.95)";
        const string InteractionTestBlockName = "Interaction Test Block";
        const float JumpHeightMeters = 1.3f;
        const int CompositionLayerOrderHud = 5;
        const int CompositionLayerOrderMenu = 10;
        const int CompositionLayerOrderModal = 20;
        const float CompositionUiRenderScale = 2.0f;
        const float MenuPanelInset = 28.0f;
        static readonly Vector2 MenuCloseButtonSize = new(160.0f, 48.0f);
        static readonly Vector2 ComfortMenuSize = new(1040.0f, 860.0f);
        static readonly Vector2 ComfortMenuCompositionSize = ComfortMenuSize * GameMenuScale;
        // Sized for the catalog browser: category/page controls, search, and a 3×4 pick grid.
        static readonly Vector2 BlockMenuSize = new(560.0f, 470.0f);
        const float SurvivalHudScale = 0.00105f;
        static readonly Vector2 SurvivalHudSize = new(560.0f, 180.0f);
        static readonly Vector2 ControllerMappingPopupSize = new(620.0f, 420.0f);
        static readonly Vector2 StartupLoadingOverlaySize = new(980.0f, 552.0f);
        static readonly Vector2 MultiplayerSessionMenuSize = new(560.0f, 380.0f);
        // --- Dark-glass theme palette -------------------------------------------------------
        // Composition-layer UI sits over bright world content, so panels stay opaque and button
        // states use high-contrast colors that remain legible in headset captures.
        static readonly Color PanelBaseColor       = new(0.015f, 0.022f, 0.032f, 1.00f);
        static readonly Color PanelHeaderColor     = new(0.035f, 0.050f, 0.070f, 1.00f);
        static readonly Color ControlNormalColor   = new(0.060f, 0.085f, 0.115f, 1.00f);
        static readonly Color ControlHighlightColor= new(0.000f, 0.710f, 0.880f, 1.00f);
        static readonly Color ControlPressedColor  = new(0.000f, 0.380f, 0.520f, 1.00f);
        static readonly Color ControlSelectedColor = new(1.000f, 0.650f, 0.160f, 1.00f);
        static readonly Color AccentColor          = new(0.000f, 0.780f, 0.980f, 1.00f);
        static readonly Color AccentHighlightColor = new(0.000f, 0.930f, 1.000f, 1.00f);
        static readonly Color TextPrimaryColor     = new(0.95f, 0.97f, 1.00f, 1.00f);
        static readonly Color TextDimColor         = new(0.65f, 0.70f, 0.75f, 1.00f);
        static readonly Color DividerColor         = new(0.18f, 0.36f, 0.44f, 0.95f);

        // Legacy per-panel aliases used by a few callers that haven't been refactored yet.
        static readonly Color ComfortMenuPanelColor    = PanelBaseColor;
        static readonly Color ComfortMenuControlColor  = ControlNormalColor;
        static readonly Color ComfortMenuAccentColor   = AccentColor;
        static readonly Color BlockMenuPanelColor      = new(0.015f, 0.035f, 0.055f, 1.00f);
        static readonly Color SurvivalHudPanelColor    = PanelBaseColor;
        static readonly Color SurvivalHudSectionColor  = PanelHeaderColor;
        static readonly Color SurvivalHudAccentColor   = AccentColor;
        static readonly Color StartupOverlayPanelColor = new(0.02f, 0.03f, 0.04f, 0.97f);
        static readonly Color MultiplayerMenuPanelColor= PanelBaseColor;
        static readonly Color MultiplayerMenuInputColor= ControlNormalColor;
        // --- end palette --------------------------------------------------------------------
        static readonly Color PointerLineColor = new(0.36f, 0.82f, 1.0f, 0.92f);
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
            (BlockiverseAudioCue.PlayerHurt, "tool_hit_stone"),
            (BlockiverseAudioCue.LowHealth, "craft_fail"),
            (BlockiverseAudioCue.PlayerDeath, "thunder_near"),
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
            ConfigureCompositionLayerSplash();
            ConfigureMetaProjectSettings();
            ConfigureAndroidManifest();
            ConfigureMetaRuntimeSettings();
            ConfigureUniversalRenderPipeline();
            ConfigureOpenXrForAndroid();
            EnsureInteractionLayer();
            EnsureXrVisualProjectionLayer();
            EnsureCompositionUiLayer();
            EnsureInteractionMaterials();
            EnsureInputActions();
            EnsureXrRigPrefab();
            EnsureNetworkFoundationAssets();
            EnsureBootScene();
            EnsureXrVisualProjectionLayer();
            EnsureCompositionUiLayer();

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
            SaveNetcodeProjectSettings(settings);

            if (settings.GenerateDefaultNetworkPrefabs)
                throw new InvalidOperationException("Netcode default network prefab generation remained enabled after saving project settings.");
        }

        static void SaveNetcodeProjectSettings(NetcodeForGameObjectsProjectSettings settings)
        {
            MethodInfo saveSettings = typeof(NetcodeForGameObjectsProjectSettings).GetMethod(
                "SaveSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (saveSettings == null)
            {
                throw new MissingMethodException(
                    typeof(NetcodeForGameObjectsProjectSettings).FullName,
                    "SaveSettings");
            }

            try
            {
                saveSettings.Invoke(settings, null);
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(
                    "Failed to save Netcode project settings after disabling default network prefab generation.",
                    ex.InnerException ?? ex);
            }
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
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
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

            PlayerSettings.SetIcons(NamedBuildTarget.Android, new[] { icon }, IconKind.Application);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Application);
            ConfigureAndroidPlatformIcons(icon);
        }

        static void ConfigureAndroidPlatformIcons(Texture2D icon)
        {
            foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                PlatformIcon[] platformIcons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                if (platformIcons == null || platformIcons.Length == 0)
                    continue;

                foreach (PlatformIcon platformIcon in platformIcons)
                {
                    int layerCount = Math.Max(1, platformIcon.minLayerCount);
                    var layerTextures = new Texture2D[layerCount];
                    for (int i = 0; i < layerTextures.Length; i++)
                        layerTextures[i] = icon;
                    platformIcon.SetTextures(layerTextures);
                }

                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, platformIcons);
            }
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
            EnsureAndroidGameActivityLabel("Assets/Plugins/Android/AndroidManifest.xml");
        }

        static void EnsureAndroidGameActivityLabel(string manifestPath)
        {
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Android manifest was not generated.", manifestPath);

            var manifest = new XmlDocument();
            manifest.Load(manifestPath);

            var namespaceManager = new XmlNamespaceManager(manifest.NameTable);
            namespaceManager.AddNamespace("android", "http://schemas.android.com/apk/res/android");

            XmlNode activityNode = manifest.SelectSingleNode(
                "/manifest/application/activity[@android:name='com.unity3d.player.UnityPlayerGameActivity']",
                namespaceManager);

            if (activityNode == null)
                throw new InvalidOperationException("Android manifest is missing UnityPlayerGameActivity.");

            const string androidNamespace = "http://schemas.android.com/apk/res/android";
            XmlAttribute labelAttribute = activityNode.Attributes["label", androidNamespace];

            if (labelAttribute == null)
            {
                labelAttribute = manifest.CreateAttribute("android", "label", androidNamespace);
                activityNode.Attributes.Append(labelAttribute);
            }

            labelAttribute.Value = "@string/app_name";
            manifest.Save(manifestPath);
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);
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
            projectConfig.requiresSystemKeyboard = true;
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

            ConfigureQuestUrpShadowPolicy(pipelineAsset);
            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
            EditorUtility.SetDirty(pipelineAsset);
        }

        static void ConfigureQuestUrpShadowPolicy(UniversalRenderPipelineAsset pipelineAsset)
        {
            var serializedAsset = new SerializedObject(pipelineAsset);
            SetSerializedBool(serializedAsset, "m_RequireDepthTexture", false);
            SetSerializedBool(serializedAsset, "m_RequireOpaqueTexture", false);
            SetSerializedBool(serializedAsset, "m_SupportsHDR", false);
            SetSerializedInt(serializedAsset, "m_MSAA", 2);
            SetSerializedFloat(serializedAsset, "m_RenderScale", 1f);
            SetSerializedBool(serializedAsset, "m_MainLightShadowsSupported", false);
            SetSerializedInt(serializedAsset, "m_MainLightShadowmapResolution", 512);
            SetSerializedFloat(serializedAsset, "m_ShadowDistance", 0f);
            SetSerializedInt(serializedAsset, "m_AdditionalLightsRenderingMode", 0);
            SetSerializedInt(serializedAsset, "m_AdditionalLightsPerObjectLimit", 0);
            SetSerializedBool(serializedAsset, "m_UseAdaptivePerformance", true);
            serializedAsset.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetSerializedBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        static void SetSerializedInt(SerializedObject serializedObject, string propertyName, int value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.intValue = value;
        }

        static void SetSerializedFloat(SerializedObject serializedObject, string propertyName, float value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.floatValue = value;
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

            UnityEngine.XR.OpenXR.Features.OpenXRFeature metaQuestFeature =
                FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, "com.unity.openxr.feature.metaquest");
            if (metaQuestFeature != null)
            {
                var serializedFeature = new SerializedObject(metaQuestFeature);
                SerializedProperty keyboardProperty = serializedFeature.FindProperty("enableSystemKeyboard");
                if (keyboardProperty != null)
                    keyboardProperty.boolValue = true;
                else
                    BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, "Meta Quest OpenXR feature does not expose enableSystemKeyboard.");
                serializedFeature.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(metaQuestFeature);
            }
            else
            {
                BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, "Meta Quest OpenXR feature was not found; system keyboard support could not be enabled.");
            }

            EditorUtility.SetDirty(openXrSettings);
        }

        static void ConfigureCompositionLayerSplash()
        {
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = true;
            PlayerSettings.virtualRealitySplashScreen = null;

            Texture2D launchArtwork = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockiverseProject.LaunchArtworkPath);
            CompositionLayersRuntimeSettings settings = CompositionLayersRuntimeSettings.Instance;
            var serializedSettings = new SerializedObject(settings);

            SetSerializedBool(serializedSettings, "m_EmulationInStandalone", false);
            SetSerializedBool(serializedSettings, "m_EnableSplashScreen", true);
            SetSerializedObject(serializedSettings, "m_SplashImage", launchArtwork);
            SetSerializedEnum(serializedSettings, "m_BackgroundType", (int)CompositionLayersRuntimeSettings.SplashBackgroundType.SolidColor);
            SetSerializedColor(serializedSettings, "m_BackgroundColor", new Color(0.02f, 0.03f, 0.04f, 1.0f));
            SetSerializedFloat(serializedSettings, "m_SplashDuration", 1.4f);
            SetSerializedFloat(serializedSettings, "m_FadeInDuration", 0.2f);
            SetSerializedFloat(serializedSettings, "m_FadeOutDuration", 0.25f);
            SetSerializedFloat(serializedSettings, "m_FollowSpeed", 2.0f);
            SetSerializedFloat(serializedSettings, "m_FollowDistance", 2.0f);
            SetSerializedBool(serializedSettings, "m_LockToHorizon", true);
            SetSerializedEnum(serializedSettings, "m_LayerType", (int)CompositionLayersRuntimeSettings.Layer.Quad);
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            settings.QuadLayerData.Size = new Vector2(1.6f, 0.9f);
            settings.QuadLayerData.ApplyTransformScale = true;
            EditorUtility.SetDirty(settings);
        }

        static void SetSerializedEnum(SerializedObject serializedObject, string propertyName, int value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.enumValueIndex = value;
        }

        static void SetSerializedColor(SerializedObject serializedObject, string propertyName, Color value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.colorValue = value;
        }

        static void SetSerializedObject(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.objectReferenceValue = value;
        }

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

        static int EnsureInteractionLayer()
        {
            return EnsureUnityLayer(BlockiverseProject.InteractionLayerName);
        }

        static int EnsureCompositionUiLayer()
        {
            return EnsureUnityLayer(BlockiverseProject.CompositionUiLayerName, BlockiverseProject.CompositionUiLayerIndex);
        }

        static int EnsureXrVisualProjectionLayer()
        {
            return EnsureUnityLayer(BlockiverseProject.XrVisualProjectionLayerName, BlockiverseProject.XrVisualProjectionLayerIndex);
        }

        static LayerMask GetInteractionLayerMask()
        {
            return (LayerMask)BlockiverseProject.InteractionLayerMask;
        }

        static LayerMask GetVrUiRaycastLayerMask()
        {
            return (LayerMask)BlockiverseProject.VrUiRaycastLayerMask;
        }

        static int GetInteractionLayerIndex()
        {
            int layer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);
            return layer >= 0 ? layer : BlockiverseProject.InteractionLayerIndex;
        }

        static int GetCompositionUiLayerIndex()
        {
            int layer = LayerMask.NameToLayer(BlockiverseProject.CompositionUiLayerName);
            return layer >= 0 ? layer : BlockiverseProject.CompositionUiLayerIndex;
        }

        static int GetXrVisualProjectionLayerIndex()
        {
            int layer = LayerMask.NameToLayer(BlockiverseProject.XrVisualProjectionLayerName);
            return layer >= 0 ? layer : BlockiverseProject.XrVisualProjectionLayerIndex;
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
            map.AddAction(BlockiverseInputActionNames.PrimaryButton, InputActionType.Button, $"{controllerPath}/primaryButton");
            map.AddAction(BlockiverseInputActionNames.SecondaryButton, InputActionType.Button, $"{controllerPath}/secondaryButton");

            map.AddAction(BlockiverseInputActionNames.UiPress, InputActionType.Button, $"{controllerPath}/triggerPressed");
            map.AddAction(BlockiverseInputActionNames.UiScroll, InputActionType.PassThrough, $"{controllerPath}/thumbstick", expectedControlLayout: "Vector2");
            map.AddAction(BlockiverseInputActionNames.HapticDevice, InputActionType.PassThrough, $"{controllerPath}/*");
            InputAction move = map.AddAction(BlockiverseInputActionNames.Move, InputActionType.PassThrough, expectedControlLayout: "Vector2");
            move.AddBinding($"{controllerPath}/thumbstick", processors: StickDeadzoneProcessor);
            InputAction turn = map.AddAction(BlockiverseInputActionNames.Turn, InputActionType.PassThrough, expectedControlLayout: "Vector2");
            turn.AddBinding($"{controllerPath}/thumbstick", processors: StickDeadzoneProcessor);
            map.AddAction(BlockiverseInputActionNames.Sprint, InputActionType.Button, $"{controllerPath}/thumbstickClicked");
            map.AddAction(BlockiverseInputActionNames.Crouch, InputActionType.Button, $"{controllerPath}/thumbstickClicked");
            AddThumbstickYComposite(map.AddAction(BlockiverseInputActionNames.TeleportMode, InputActionType.Button), controllerPath);
            AddThumbstickYComposite(map.AddAction(BlockiverseInputActionNames.TeleportSelect, InputActionType.Button), controllerPath);
        }

        static void AddGameplayMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(BlockiverseInputActionNames.GameplayMap);
            map.AddAction(BlockiverseInputActionNames.Menu, InputActionType.Button, "<XRController>{LeftHand}/menuButton");
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

    }
}

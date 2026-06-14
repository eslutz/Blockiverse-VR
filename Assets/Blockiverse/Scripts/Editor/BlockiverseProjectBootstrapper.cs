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
        const string StickDeadzoneProcessor = "stickDeadzone(min=0.2,max=0.95)";
        const string InteractionTestBlockName = "Interaction Test Block";
        const float JumpHeightMeters = 1.3f;
        const float MenuPanelInset = 28.0f;
        static readonly Vector2 MenuCloseButtonSize = new(160.0f, 48.0f);
        static readonly Vector2 ComfortMenuSize = new(1040.0f, 860.0f);
        // Sized for the catalog browser: category/page controls, search, and a 3×4 pick grid.
        static readonly Vector2 BlockMenuSize = new(560.0f, 470.0f);
        const float SurvivalHudScale = 0.00105f;
        static readonly Vector2 SurvivalHudSize = new(560.0f, 180.0f);
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
            projectConfig.focusAware = true;
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
            SetSerializedBool(serializedAsset, "m_MainLightShadowsSupported", false);
            SetSerializedInt(serializedAsset, "m_MainLightShadowmapResolution", 512);
            SetSerializedFloat(serializedAsset, "m_ShadowDistance", 0f);
            SetSerializedInt(serializedAsset, "m_AdditionalLightsRenderingMode", 0);
            SetSerializedInt(serializedAsset, "m_AdditionalLightsPerObjectLimit", 0);
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
            if (!controllerPath.Contains("{LeftHand}", StringComparison.Ordinal))
            {
                map.AddAction(BlockiverseInputActionNames.PrimaryButton, InputActionType.Button, $"{controllerPath}/primaryButton");
                map.AddAction(BlockiverseInputActionNames.SecondaryButton, InputActionType.Button, $"{controllerPath}/secondaryButton");
            }

            map.AddAction(BlockiverseInputActionNames.UiPress, InputActionType.Button, $"{controllerPath}/triggerPressed");
            map.AddAction(BlockiverseInputActionNames.UiScroll, InputActionType.PassThrough, $"{controllerPath}/thumbstick", expectedControlLayout: "Vector2");
            map.AddAction(BlockiverseInputActionNames.HapticDevice, InputActionType.PassThrough, $"{controllerPath}/*");
            InputAction move = map.AddAction(BlockiverseInputActionNames.Move, InputActionType.PassThrough, expectedControlLayout: "Vector2");
            move.AddBinding($"{controllerPath}/thumbstick", processors: StickDeadzoneProcessor);
            InputAction turn = map.AddAction(BlockiverseInputActionNames.Turn, InputActionType.PassThrough, expectedControlLayout: "Vector2");
            turn.AddBinding($"{controllerPath}/thumbstick", processors: StickDeadzoneProcessor);
            AddThumbstickYComposite(map.AddAction(BlockiverseInputActionNames.TeleportMode, InputActionType.Button), controllerPath);
            AddThumbstickYComposite(map.AddAction(BlockiverseInputActionNames.TeleportSelect, InputActionType.Button), controllerPath);
        }

        static void AddGameplayMap(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap(BlockiverseInputActionNames.GameplayMap);
            map.AddAction(BlockiverseInputActionNames.Menu, InputActionType.Button, "<XRController>{LeftHand}/menuButton");
            map.AddAction(BlockiverseInputActionNames.Jump, InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            map.AddAction(BlockiverseInputActionNames.BlockEditingToggle, InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            map.AddAction(BlockiverseInputActionNames.Sprint, InputActionType.Button, "<XRController>{LeftHand}/thumbstickClicked");
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

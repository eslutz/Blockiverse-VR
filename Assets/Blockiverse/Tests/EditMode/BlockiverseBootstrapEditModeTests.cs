using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using Object = UnityEngine.Object;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseBootstrapEditModeTests
    {
        const string BootScenePath = "Assets/Blockiverse/Scenes/Boot.unity";
        const string XrRigPrefabPath = "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab";
        const string AndroidUrpAssetPath = "Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset";
        const string VoxelShaderPath = "Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader";
        const string PerformanceStatsOverlayPath = "Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs";
        const string BlockiverseInputRigPath = "Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs";
        const string BlockiverseControllerHapticsPath = "Assets/Blockiverse/Scripts/VR/BlockiverseControllerHaptics.cs";
        const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        const string LegacyAndroidResourcePath = "Assets/Plugins/Android/res";
        const string OculusRuntimeSettingsPath = "Assets/Resources/OculusRuntimeSettings.asset";
        const string VersionSettingsPath = "ProjectSettings/ProjectVersion.txt";
        const string ManifestPath = "Packages/manifest.json";
        const string XrGeneralSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        const string SceneBootstrapperPath = "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs";
        const string MenuBootstrapperPath = "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Menus.cs";
        const string XrRigBootstrapperPath = "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.XrRig.cs";
        static readonly string[] EngineFreeAsmdefPaths =
        {
            "Assets/Blockiverse/Scripts/Voxel/Blockiverse.Voxel.asmdef",
            "Assets/Blockiverse/Scripts/Survival/Blockiverse.Survival.asmdef",
            "Assets/Blockiverse/Scripts/SurvivalHealth/Blockiverse.Survival.Health.asmdef",
            "Assets/Blockiverse/Scripts/WorldGen/Blockiverse.WorldGen.asmdef",
        };

        [Test]
        public void UnityVersionIsPinnedToUnity6()
        {
            string versionSettings = File.ReadAllText(VersionSettingsPath);

            StringAssert.Contains("m_EditorVersion: 6000.", versionSettings);
        }

        [Test]
        public void RequiredPackagesAreDeclared()
        {
            string manifest = File.ReadAllText(ManifestPath);

            StringAssert.Contains("\"com.unity.render-pipelines.universal\"", manifest);
            StringAssert.Contains("\"com.unity.xr.openxr\"", manifest);
            StringAssert.Contains("\"com.unity.xr.meta-openxr\"", manifest);
            StringAssert.Contains("\"com.meta.xr.sdk.core\"", manifest);
            StringAssert.Contains("\"com.unity.inputsystem\"", manifest);
        }

        [Test]
        public void NetcodeDefaultNetworkPrefabGenerationIsDisabled()
        {
            Type settingsType = Type.GetType(
                "Unity.Netcode.Editor.Configuration.NetcodeForGameObjectsProjectSettings, Unity.Netcode.Editor");
            Assert.That(settingsType, Is.Not.Null);

            object instance = settingsType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object value = settingsType.GetProperty("GenerateDefaultNetworkPrefabs", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
            if (value is bool generatedDefaultPrefabs)
            {
                Assert.That(generatedDefaultPrefabs, Is.False);
                return;
            }

            string settingsYaml = File.ReadAllText("ProjectSettings/NetcodeForGameObjects.asset");
            StringAssert.Contains("GenerateDefaultNetworkPrefabs: 0", settingsYaml);
        }

        [Test]
        public void RepositoryUsesVisibleMetaFilesAndTextSerialization()
        {
            Assert.That(VersionControlSettings.mode, Is.EqualTo("Visible Meta Files"));
            Assert.That(EditorSettings.serializationMode, Is.EqualTo(SerializationMode.ForceText));
        }

        [Test]
        public void EngineFreeSimulationAssembliesRejectUnityEngineReferences()
        {
            foreach (string asmdefPath in EngineFreeAsmdefPaths)
            {
                string asmdef = File.ReadAllText(asmdefPath);
                Assert.That(asmdef, Does.Contain("\"noEngineReferences\": true"), asmdefPath);
            }
        }

        [Test]
        public void BootstrapAssetsExist()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPrefabPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AndroidUrpAssetPath), Is.Not.Null);
        }

        [Test]
        public void AndroidUrpAssetDisablesRealtimeShadowsForQuest()
        {
            string asset = File.ReadAllText(AndroidUrpAssetPath);

            StringAssert.Contains("m_MainLightShadowsSupported: 0", asset);
            StringAssert.Contains("m_ShadowDistance: 0", asset);
            StringAssert.Contains("m_AdditionalLightsRenderingMode: 0", asset);
            StringAssert.Contains("m_AdditionalLightsPerObjectLimit: 0", asset);
        }

        [Test]
        public void VoxelShaderDoesNotCompileRealtimeShadowPasses()
        {
            string shader = File.ReadAllText(VoxelShaderPath);

            Assert.That(shader, Does.Not.Contain("_MAIN_LIGHT_SHADOWS"));
            Assert.That(shader, Does.Not.Contain("_ADDITIONAL_LIGHTS"));
            Assert.That(shader, Does.Not.Contain("ShadowCaster"));
        }

        [Test]
        public void BootSceneIsFirstEnabledBuildScene()
        {
            Assert.That(EditorBuildSettings.scenes, Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(BootScenePath));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(
                EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path),
                Is.EqualTo(new[] { BootScenePath }),
                "Player builds must enable only the generated Boot scene; test scenes stay editor-only.");
        }

        [Test]
        public void GeneratedMenuLabelsAutoSizeForLocalizedText()
        {
            string menuBootstrapper = File.ReadAllText(MenuBootstrapperPath);

            StringAssert.Contains("text.enableAutoSizing = true", menuBootstrapper);
            StringAssert.Contains("text.fontSizeMin =", menuBootstrapper);
            StringAssert.Contains("TextOverflowModes.Ellipsis", menuBootstrapper);
            Assert.That(menuBootstrapper, Does.Not.Contain("TextOverflowModes.Truncate"));
        }

        [Test]
        public void GeneratedControllerMappingMentionsBothTeleportSticks()
        {
            string gameMenuBootstrapper = File.ReadAllText("Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.GameMenus.cs");

            StringAssert.Contains("Either stick hold up: teleport aim, release to land", gameMenuBootstrapper);
            Assert.That(gameMenuBootstrapper, Does.Not.Contain("Right stick hold up: teleport aim"));
        }

        [Test]
        public void GeneratedXrRigRegistersPlayerRigAnchor()
        {
            string xrRigBootstrapper = File.ReadAllText(XrRigBootstrapperPath);

            StringAssert.Contains("rig.AddComponent<BlockiversePlayerRigAnchor>()", xrRigBootstrapper);
            StringAssert.Contains("EnsureComponent<BlockiversePlayerRigAnchor>(rig)", xrRigBootstrapper);
        }

        [Test]
        public void GeneratedWorldWiresPerformanceStatsOverlay()
        {
            string sceneBootstrapper = File.ReadAllText(SceneBootstrapperPath);

            StringAssert.Contains("EnsureComponent<PerformanceStatsOverlay>(worldObject)", sceneBootstrapper);
            StringAssert.Contains("performanceOverlay.Configure(renderer)", sceneBootstrapper);
        }

        [Test]
        public void GeneratedXrRigVignetteIncludesFallsAndJumps()
        {
            string xrRigBootstrapper = File.ReadAllText(XrRigBootstrapperPath);

            StringAssert.Contains("AddVignetteProvider(controller, rig.GetComponent<GravityProvider>())", xrRigBootstrapper);
            StringAssert.Contains("AddVignetteProvider(controller, rig.GetComponent<JumpProvider>())", xrRigBootstrapper);
        }

        [Test]
        public void XrRigTurnProvidersAreSuppressedWhileTurnHandHoversUi()
        {
            string inputRig = File.ReadAllText(BlockiverseInputRigPath);

            StringAssert.Contains("leftInteractionRay", inputRig);
            StringAssert.Contains("rightInteractionRay", inputRig);
            StringAssert.Contains("UpdateTurnProviderEnabledState()", inputRig);
            StringAssert.Contains("IsActiveTurnRayOverUi()", inputRig);
            StringAssert.Contains("interactionRay.IsOverUIGameObject()", inputRig);
            StringAssert.Contains("!smoothTurn && !suppressTurnForUi", inputRig);
            StringAssert.Contains("smoothTurn && !suppressTurnForUi", inputRig);
        }

        [Test]
        public void LocomotionFeedbackUsesTeleportAndSnapTurnHapticsWithoutPerCallDeviceListAllocation()
        {
            string inputRig = File.ReadAllText(BlockiverseInputRigPath);
            string haptics = File.ReadAllText(BlockiverseControllerHapticsPath);

            StringAssert.Contains("teleportationProvider.locomotionEnded += teleportEndedHandler", inputRig);
            StringAssert.Contains("snapTurnProvider.locomotionEnded += snapTurnEndedHandler", inputRig);
            StringAssert.Contains("BlockiverseHapticPattern.TeleportLand", inputRig);
            StringAssert.Contains("BlockiverseHapticPattern.SnapTurn", inputRig);
            StringAssert.Contains("static readonly System.Collections.Generic.List<InputDevice> DeviceScratch", haptics);
            Assert.That(haptics, Does.Not.Contain("new System.Collections.Generic.List<InputDevice>()"));
        }

        [Test]
        public void PerformanceOverlayImguiPathIsDevelopmentOnly()
        {
            string overlaySource = File.ReadAllText(PerformanceStatsOverlayPath);

            StringAssert.Contains("#if !DEVELOPMENT_BUILD && !UNITY_EDITOR", overlaySource);
            StringAssert.Contains("enabled = false;", overlaySource);
            StringAssert.Contains("#if DEVELOPMENT_BUILD || UNITY_EDITOR", overlaySource);
            StringAssert.Contains("void OnGUI()", overlaySource);
            Assert.That(overlaySource, Does.Not.Contain("Debug.isDebugBuild"));
        }

        [Test]
        public void AndroidOpenXrSettingsAreConfiguredForQuest()
        {
            OpenXRSettings androidSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);

            Assert.That(androidSettings, Is.Not.Null);
            Assert.That(androidSettings.renderMode, Is.EqualTo(OpenXRSettings.RenderMode.SinglePassInstanced));
            Assert.That(androidSettings.GetFeatures(), Has.Some.Matches<UnityEngine.XR.OpenXR.Features.OpenXRFeature>(
                feature => feature.enabled && feature.GetType().Name == "MetaQuestFeature"));
        }

        [Test]
        public void AndroidXrManagerStartsOpenXrAutomatically()
        {
            Object managerSettings = AssetDatabase
                .LoadAllAssetsAtPath(XrGeneralSettingsPath)
                .FirstOrDefault(asset => asset.GetType().Name == "XRManagerSettings");

            Assert.That(managerSettings, Is.Not.Null);

            var serializedManager = new SerializedObject(managerSettings);
            Assert.That(serializedManager.FindProperty("m_AutomaticLoading")?.boolValue, Is.True);
            Assert.That(serializedManager.FindProperty("m_AutomaticRunning")?.boolValue, Is.True);

            SerializedProperty loaders = serializedManager.FindProperty("m_Loaders");
            Assert.That(loaders, Is.Not.Null);
            Assert.That(loaders.arraySize, Is.GreaterThan(0));
            Assert.That(
                Enumerable.Range(0, loaders.arraySize)
                    .Select(index => loaders.GetArrayElementAtIndex(index).objectReferenceValue)
                    .Any(loader => loader != null && loader.GetType().Name == "OpenXRLoader"),
                Is.True);
        }

        [Test]
        public void AndroidManifestUsesSingleGameActivityEntry()
        {
            Assert.That(File.Exists(AndroidManifestPath), Is.True);

            var manifest = new XmlDocument();
            manifest.Load(AndroidManifestPath);

            var namespaceManager = new XmlNamespaceManager(manifest.NameTable);
            namespaceManager.AddNamespace("android", "http://schemas.android.com/apk/res/android");

            XmlNode internetPermission = manifest.SelectSingleNode(
                "/manifest/uses-permission[@android:name='android.permission.INTERNET']",
                namespaceManager);
            Assert.That(internetPermission, Is.Not.Null);

            XmlNodeList activityNodes = manifest.SelectNodes("/manifest/application/activity", namespaceManager);
            Assert.That(activityNodes, Is.Not.Null);
            Assert.That(activityNodes, Has.Count.EqualTo(1));

            string activityName = activityNodes[0].Attributes["android:name"]?.Value;
            Assert.That(activityName, Is.EqualTo("com.unity3d.player.UnityPlayerGameActivity"));

            XmlNode supportedDevicesNode = manifest.SelectSingleNode(
                "/manifest/application/meta-data[@android:name='com.oculus.supportedDevices']",
                namespaceManager);
            Assert.That(supportedDevicesNode, Is.Not.Null);
            Assert.That(
                supportedDevicesNode.Attributes["android:value"]?.Value,
                Is.EqualTo("quest3|quest3s"));
        }

        [Test]
        public void AndroidAppIdentityAndBrandingAssetsAreConfigured()
        {
            Assert.That(PlayerSettings.productName, Is.EqualTo(BlockiverseProject.ProductName));
            Assert.That(File.Exists(BlockiverseProject.AndroidAppStringsPath), Is.True);
            Assert.That(File.ReadAllText(BlockiverseProject.AndroidAppStringsPath), Does.Contain(BlockiverseProject.ProductName));
            Assert.That(File.Exists(BlockiverseProject.AndroidBrandingLibraryPath + "/AndroidManifest.xml"), Is.True);
            Assert.That(
                File.ReadAllText(BlockiverseProject.AndroidBrandingLibraryPath + "/build.gradle"),
                Does.Contain("namespace 'dev.ericslutz.blockiversevr.branding'"));
            Assert.That(Directory.Exists(LegacyAndroidResourcePath), Is.False);

            string[] requiredBrandingAssets =
            {
                BlockiverseProject.AppIconPath,
                BlockiverseProject.LaunchArtworkPath
            };

            foreach (string assetPath in requiredBrandingAssets)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                Assert.That(texture, Is.Not.Null, $"Missing branding texture: {assetPath}");
                Assert.That(File.Exists($"{assetPath}.meta"), Is.True, $"Missing branding texture meta: {assetPath}.meta");
            }
        }

        [Test]
        public void MetaRuntimeSettingsDoNotRequestUnusedFaceTracking()
        {
            var runtimeSettings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(OculusRuntimeSettingsPath);
            Assert.That(runtimeSettings, Is.Not.Null);

            var serializedSettings = new SerializedObject(runtimeSettings);
            Assert.That(GetBool(serializedSettings, "requestsVisualFaceTracking"), Is.False);
            Assert.That(GetBool(serializedSettings, "requestsAudioFaceTracking"), Is.False);
            Assert.That(GetBool(serializedSettings, "enableFaceTrackingVisemesOutput"), Is.False);
        }

        static bool GetBool(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null);
            return property.boolValue;
        }
    }
}

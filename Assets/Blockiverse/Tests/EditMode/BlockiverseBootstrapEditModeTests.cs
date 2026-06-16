using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Blockiverse.Core;
using Blockiverse.MetaPlatform;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
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
        const string AndroidActivityTitleRuntimePath = "Assets/Blockiverse/Scripts/Core/BlockiverseAndroidActivityTitle.cs";
        const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        const string OculusProjectConfigPath = "Assets/Oculus/OculusProjectConfig.asset";
        const string LegacyAndroidResourcePath = "Assets/Plugins/Android/res";
        const string OculusRuntimeSettingsPath = "Assets/Resources/OculusRuntimeSettings.asset";
        const string VersionSettingsPath = "ProjectSettings/ProjectVersion.txt";
        const string NetcodeProjectSettingsPath = "ProjectSettings/NetcodeForGameObjects.asset";
        const string ManifestPath = "Packages/manifest.json";
        const string XrGeneralSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        const string BuildSmokePath = "Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs";
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
            Assert.That(File.Exists(NetcodeProjectSettingsPath), Is.True);
            string settings = File.ReadAllText(NetcodeProjectSettingsPath);

            StringAssert.Contains("GenerateDefaultNetworkPrefabs: 0", settings);
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
        public void RayDiagnosticWorldsAndBuildEntrypointsAreRemoved()
        {
            string sceneBootstrapper = File.ReadAllText(SceneBootstrapperPath);
            string buildSmoke = File.ReadAllText(BuildSmokePath);
            string project = File.ReadAllText("Assets/Blockiverse/Scripts/Core/BlockiverseProject.cs");

            Assert.That(AssetDatabase.FindAssets("RayDiagnostic t:Scene", new[] { "Assets/Blockiverse/Scenes" }), Is.Empty);
            Assert.That(AssetDatabase.FindAssets("BlockiverseXrUiInteractionLab t:Script", new[] { "Assets/Blockiverse/Scripts/VR" }), Is.Empty);
            Assert.That(AssetDatabase.FindAssets("BlockiverseMenuDiagnosticStartup t:Script", new[] { "Assets/Blockiverse/Scripts/VR" }), Is.Empty);
            Assert.That(project, Does.Not.Contain("RayDiagnostic"));
            Assert.That(project, Does.Not.Contain("UseXrUiInteractionLabStartupOverride"));
            Assert.That(sceneBootstrapper, Does.Not.Contain("RayDiagnostic"));
            Assert.That(sceneBootstrapper, Does.Not.Contain("XrUiInteractionLab"));
            Assert.That(sceneBootstrapper, Does.Not.Contain("MenuDiagnosticStartup"));
            Assert.That(buildSmoke, Does.Not.Contain("BuildRayDiagnostic"));
            Assert.That(
                EditorBuildSettings.scenes.Select(scene => scene.path),
                Has.None.Matches<string>(path => path.Contains("RayDiagnostic")),
                "Diagnostic stub scenes must not be part of the generated build scene list.");
        }

        [Test]
        public void BootSceneContainsOneMetaUserAgeCategoryService()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
                BlockiverseUserAgeCategoryService[] services = scene.GetRootGameObjects()
                    .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<BlockiverseUserAgeCategoryService>(true))
                    .ToArray();

                Assert.That(services, Has.Length.EqualTo(1));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
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
            StringAssert.Contains("Support stick: move", gameMenuBootstrapper);
            StringAssert.Contains("Dominant stick click: crouch", gameMenuBootstrapper);
            StringAssert.Contains("Dominant primary button: jump", gameMenuBootstrapper);
            Assert.That(gameMenuBootstrapper, Does.Not.Contain("Right stick hold up: teleport aim"));
            Assert.That(gameMenuBootstrapper, Does.Not.Contain("Right A: jump"));
        }

        [Test]
        public void GeneratedXrRigRegistersPlayerRigAnchor()
        {
            string xrRigBootstrapper = File.ReadAllText(XrRigBootstrapperPath);

            StringAssert.Contains("rig.AddComponent<BlockiversePlayerRigAnchor>()", xrRigBootstrapper);
            StringAssert.Contains("EnsureComponent<BlockiversePlayerRigAnchor>(rig)", xrRigBootstrapper);
        }

        [Test]
        public void GeneratedXrRigWiresVerboseTraceController()
        {
            string xrRigBootstrapper = File.ReadAllText(XrRigBootstrapperPath);

            StringAssert.Contains("EnsureComponent<BlockiverseVerboseTraceController>(rig)", xrRigBootstrapper);
            StringAssert.Contains("verboseTrace.Configure(inputRig, null, controller, audioCuePlayer, vfxCuePlayer, musicController, interactionHaptics)", xrRigBootstrapper);
        }

        [Test]
        public void GeneratedWorldWiresPerformanceStatsOverlay()
        {
            string sceneBootstrapper = File.ReadAllText(SceneBootstrapperPath);

            StringAssert.Contains("EnsureComponent<PerformanceStatsOverlay>(worldObject)", sceneBootstrapper);
            StringAssert.Contains("performanceOverlay.Configure(renderer)", sceneBootstrapper);
        }

        [Test]
        public void GeneratedXrRigVignetteExcludesStartupGravityAndJump()
        {
            string xrRigBootstrapper = File.ReadAllText(XrRigBootstrapperPath);

            Assert.That(
                xrRigBootstrapper,
                Does.Not.Contain("AddVignetteProvider(controller, rig.GetComponent<GravityProvider>())"),
                "Gravity can report active while the rig settles, so it must not close the startup/menu vignette.");
            Assert.That(
                xrRigBootstrapper,
                Does.Not.Contain("AddVignetteProvider(controller, rig.GetComponent<JumpProvider>())"),
                "Jump arcs should not be wired as startup/menu vignette triggers.");
        }

        [Test]
        public void XrRigTurnProvidersAreSuppressedWhileToolHandHoversUi()
        {
            string inputRig = File.ReadAllText(BlockiverseInputRigPath);

            StringAssert.Contains("leftInteractionRay", inputRig);
            StringAssert.Contains("rightInteractionRay", inputRig);
            StringAssert.Contains("UpdateTurnProviderEnabledState()", inputRig);
            StringAssert.Contains("IsActiveTurnRayOverUi()", inputRig);
            StringAssert.Contains("GetToolHand()", inputRig);
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

            UnityEngine.XR.OpenXR.Features.OpenXRFeature metaQuestFeature = androidSettings.GetFeatures()
                .FirstOrDefault(feature => feature.GetType().Name == "MetaQuestFeature");
            Assert.That(metaQuestFeature, Is.Not.Null);

            var serializedFeature = new SerializedObject(metaQuestFeature);
            SerializedProperty keyboardProperty = serializedFeature.FindProperty("enableSystemKeyboard");
            Assert.That(keyboardProperty, Is.Not.Null, "Meta Quest OpenXR feature must expose the system keyboard setting.");
            Assert.That(keyboardProperty.boolValue, Is.True,
                "Quest builds must enable the OpenXR system keyboard overlay for TouchScreenKeyboard.");
        }

        [Test]
        public void MetaProjectConfigRequiresSystemKeyboard()
        {
            string projectConfig = File.ReadAllText(OculusProjectConfigPath);

            StringAssert.Contains("focusAware: 1", projectConfig);
            StringAssert.Contains("requiresSystemKeyboard: 1", projectConfig);
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
            Assert.That(
                activityNodes[0].Attributes["android:label"]?.Value,
                Is.EqualTo("@string/app_name"),
                "Quest shell quit and unknown-source surfaces should resolve the activity title to Blockiverse VR.");

            XmlNode supportedDevicesNode = manifest.SelectSingleNode(
                "/manifest/application/meta-data[@android:name='com.oculus.supportedDevices']",
                namespaceManager);
            Assert.That(supportedDevicesNode, Is.Not.Null);
            Assert.That(
                supportedDevicesNode.Attributes["android:value"]?.Value,
                Is.EqualTo("quest3|quest3s"));

            XmlNode keyboardFeatureNode = manifest.SelectSingleNode(
                "/manifest/uses-feature[@android:name='oculus.software.overlay_keyboard']",
                namespaceManager);
            Assert.That(keyboardFeatureNode, Is.Not.Null,
                "Quest system keyboard support must be declared in the Android manifest.");
            Assert.That(keyboardFeatureNode.Attributes["android:required"]?.Value, Is.EqualTo("false"));
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
        public void AndroidRuntimeTaskTitleUsesProductName()
        {
            Assert.That(File.Exists(AndroidActivityTitleRuntimePath), Is.True,
                "The Quest quit panel can read the live Android activity/task title even when the launcher manifest label is correct.");

            Type titleType = Type.GetType("Blockiverse.Core.BlockiverseAndroidActivityTitle, Blockiverse.Core");
            Assert.That(titleType, Is.Not.Null);

            FieldInfo titleField = titleType.GetField("Title", BindingFlags.Public | BindingFlags.Static);
            Assert.That(titleField, Is.Not.Null);
            Assert.That(titleField.GetRawConstantValue(), Is.EqualTo(BlockiverseProject.ProductName));

            string source = File.ReadAllText(AndroidActivityTitleRuntimePath);
            StringAssert.Contains("RuntimeInitializeOnLoadMethod", source);
            StringAssert.Contains("setTitle", source);
            StringAssert.Contains("ActivityManager$TaskDescription", source);
            StringAssert.Contains("setTaskDescription", source);
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

        [Test]
        public void AndroidBuildTreatsUnusedMetaAvatarSamplePresetsAsOptional()
        {
            string buildSmokeSource = File.ReadAllText(BuildSmokePath);

            StringAssert.Contains("PrepareOptionalMetaAvatarSamplePresets()", buildSmokeSource);
            StringAssert.Contains(".blockiverse-no-sample-presets", buildSmokeSource);
            StringAssert.Contains("loadFallbackPreset: 1", buildSmokeSource);

            foreach (string assetPath in EnumerateBlockiverseSerializedAssets())
            {
                Assert.That(
                    File.ReadAllText(assetPath),
                    Does.Not.Contain("loadFallbackPreset: 1"),
                    $"{assetPath} enables Meta sample preset avatars; either disable it or intentionally add packaged Quest preset assets.");
            }
        }

        static bool GetBool(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null);
            return property.boolValue;
        }

        static string[] EnumerateBlockiverseSerializedAssets()
        {
            return Directory
                .GetFiles("Assets/Blockiverse", "*.*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string extension = Path.GetExtension(path);
                    return extension == ".asset" || extension == ".prefab" || extension == ".unity";
                })
                .ToArray();
        }
    }
}

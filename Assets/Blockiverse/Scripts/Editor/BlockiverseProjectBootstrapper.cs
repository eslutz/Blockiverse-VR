using System;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.VR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
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
                return existingPrefab;

            GameObject rig = CreateXrRigInstance();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(rig, BlockiverseProject.XrRigPrefabPath);
            UnityEngine.Object.DestroyImmediate(rig);

            return prefab;
        }

        static void EnsureBootScene()
        {
            GameObject rigPrefab = EnsureXrRigPrefab();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BlockiverseProject.BootScenePath) == null)
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                PrefabUtility.InstantiatePrefab(rigPrefab, scene);

                GameObject lightObject = new("Bootstrap Directional Light");
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.0f;
                lightObject.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

                EditorSceneManager.SaveScene(scene, BlockiverseProject.BootScenePath);
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BlockiverseProject.BootScenePath, true)
            };
        }

        static GameObject CreateXrRigInstance()
        {
            GameObject rig = new(BlockiverseProject.XrRigRootName);
            rig.AddComponent<BlockiverseXRRigMarker>();

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

            CreateControllerAnchor("Left Controller", cameraOffset.transform, new Vector3(-0.25f, 1.25f, 0.35f));
            CreateControllerAnchor("Right Controller", cameraOffset.transform, new Vector3(0.25f, 1.25f, 0.35f));

            return rig;
        }

        static void CreateControllerAnchor(string name, Transform parent, Vector3 localPosition)
        {
            GameObject controller = new(name);
            controller.transform.SetParent(parent, false);
            controller.transform.localPosition = localPosition;
        }
    }
}

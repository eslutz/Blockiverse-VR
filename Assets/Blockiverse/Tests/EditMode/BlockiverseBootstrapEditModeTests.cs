using System.IO;
using System.Xml;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseBootstrapEditModeTests
    {
        const string BootScenePath = "Assets/Blockiverse/Scenes/Boot.unity";
        const string XrRigPrefabPath = "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab";
        const string AndroidUrpAssetPath = "Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset";
        const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        const string OculusRuntimeSettingsPath = "Assets/Resources/OculusRuntimeSettings.asset";
        const string VersionSettingsPath = "ProjectSettings/ProjectVersion.txt";
        const string ManifestPath = "Packages/manifest.json";

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
        public void RepositoryUsesVisibleMetaFilesAndTextSerialization()
        {
            Assert.That(VersionControlSettings.mode, Is.EqualTo("Visible Meta Files"));
            Assert.That(EditorSettings.serializationMode, Is.EqualTo(SerializationMode.ForceText));
        }

        [Test]
        public void BootstrapAssetsExist()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPrefabPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AndroidUrpAssetPath), Is.Not.Null);
        }

        [Test]
        public void BootSceneIsFirstEnabledBuildScene()
        {
            Assert.That(EditorBuildSettings.scenes, Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(BootScenePath));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
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
        public void AndroidManifestUsesSingleGameActivityEntry()
        {
            Assert.That(File.Exists(AndroidManifestPath), Is.True);

            var manifest = new XmlDocument();
            manifest.Load(AndroidManifestPath);

            var namespaceManager = new XmlNamespaceManager(manifest.NameTable);
            namespaceManager.AddNamespace("android", "http://schemas.android.com/apk/res/android");

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

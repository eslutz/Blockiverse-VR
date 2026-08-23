using System;
using System.Linq;
using System.Reflection;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using NUnit.Framework;
using Oculus.Avatar2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blockiverse.Tests.MetaAvatars.EditMode
{
    /// <summary>
    /// Pins the device-critical Avatar SDK configuration: the scene-authored, asset-wired
    /// SDK manager (whose serialized references are what ship the avatar shaders in a URP
    /// Android build), the rig's XRI tracking wiring, and the tracking-space math.
    /// A bare runtime-instantiated OvrAvatarManager falls back to Shader.Find("Standard")
    /// and a shaderless GpuSkinningConfiguration — both editor-clean and device-fatal —
    /// so these must stay wired in the committed scenes.
    /// </summary>
    public sealed class MetaAvatarSdkConfigurationEditModeTests
    {
        static readonly string[] AvatarScenePaths =
        {
            "Assets/Blockiverse/Scenes/Boot.unity",
            "Assets/Blockiverse/Scenes/MultiplayerTest.unity"
        };

        GameObject testObject;

        [TearDown]
        public void TearDown()
        {
            if (testObject != null)
                UnityEngine.Object.DestroyImmediate(testObject);
        }

        [Test]
        public void TrackingSpacePoseConversionRemovesOriginPoseAndScale()
        {
            Vector3 originPosition = new(10.0f, 2.0f, -5.0f);
            Quaternion originRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            Vector3 localExpected = new(0.1f, 1.6f, 0.3f);
            Quaternion localRotationExpected = Quaternion.Euler(15.0f, 30.0f, 0.0f);

            Vector3 worldPosition = originPosition + originRotation * localExpected;
            Quaternion worldRotation = originRotation * localRotationExpected;

            BlockiverseXriInputTrackingDelegate.ComputeTrackingSpacePose(
                originPosition, originRotation, Vector3.one,
                worldPosition, worldRotation,
                out Vector3 localPosition, out Quaternion localRotation);

            Assert.That(localPosition.x, Is.EqualTo(localExpected.x).Within(1e-4f));
            Assert.That(localPosition.y, Is.EqualTo(localExpected.y).Within(1e-4f));
            Assert.That(localPosition.z, Is.EqualTo(localExpected.z).Within(1e-4f));
            Assert.That(Quaternion.Angle(localRotation, localRotationExpected), Is.LessThan(0.01f));

            // A player-size-scaled rig must hand the solver unscaled tracking data: the
            // entity root (a child of the rig) re-applies the scale.
            const float rigScale = 2.0f;
            Vector3 scaledWorldPosition = originPosition + originRotation * (localExpected * rigScale);
            BlockiverseXriInputTrackingDelegate.ComputeTrackingSpacePose(
                originPosition, originRotation, Vector3.one * rigScale,
                scaledWorldPosition, worldRotation,
                out Vector3 scaledLocalPosition, out _);

            Assert.That(scaledLocalPosition.x, Is.EqualTo(localExpected.x).Within(1e-4f));
            Assert.That(scaledLocalPosition.y, Is.EqualTo(localExpected.y).Within(1e-4f));
            Assert.That(scaledLocalPosition.z, Is.EqualTo(localExpected.z).Within(1e-4f));
        }

        [Test]
        public void PresenterRoutesMetaUserIdsThroughProvider()
        {
            testObject = new GameObject("Meta Avatar User Id Test");
            testObject.AddComponent<BlockiverseNetworkAvatarRig>();
            var provider = testObject.AddComponent<BlockiverseEditorMockMetaAvatarProvider>();
            var presenter = testObject.AddComponent<BlockiverseMetaAvatarPresenter>();

            presenter.Configure(
                provider, null, MetaAvatarTrackingSources.Empty, MetaAvatarPresentationMode.RemoteThirdPerson);

            Assert.That(presenter.TryGetLocalMetaUserId(out ulong none), Is.False);
            Assert.That(none, Is.Zero);

            provider.LocalUserId = 4242;
            Assert.That(presenter.TryGetLocalMetaUserId(out ulong resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(4242UL));

            presenter.ConfigureRemoteUserAvatar(9001);
            Assert.That(provider.RemoteUserId, Is.EqualTo(9001UL));
        }

        [Test]
        public void RigPrefabWiresXriAvatarInputManagerSources()
        {
            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab");
            Assert.That(rigPrefab, Is.Not.Null);

            var inputManager = rigPrefab.GetComponent<BlockiverseXriAvatarInputManager>();
            Assert.That(inputManager, Is.Not.Null);
            Assert.That(inputManager.TrackingOrigin, Is.SameAs(rigPrefab.transform),
                "The tracking origin must be the rig root: the avatar entity sits there at an " +
                "identity local pose, and the SDK poses joints relative to it.");
            Assert.That(inputManager.HeadSource, Is.Not.Null);
            Assert.That(inputManager.HeadSource.name, Is.EqualTo("Main Camera"));
            Assert.That(inputManager.LeftHandSource, Is.Not.Null);
            Assert.That(inputManager.LeftHandSource.name, Is.EqualTo("Left Controller"));
            Assert.That(inputManager.RightHandSource, Is.Not.Null);
            Assert.That(inputManager.RightHandSource.name, Is.EqualTo("Right Controller"));
        }

        [Test]
        public void AvatarScenesCarryConfiguredInactiveSdkManager()
        {
            DisableMetaProjectSetupBackgroundChecks();

            try
            {
                foreach (string scenePath in AvatarScenePaths)
                {
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    GameObject managerObject = scene.GetRootGameObjects()
                        .FirstOrDefault(sceneRoot => sceneRoot.name == MetaHorizonAvatarProvider.SdkManagerObjectName);

                    Assert.That(managerObject, Is.Not.Null,
                        $"{scenePath} must carry the bootstrapper-authored Avatar SDK manager.");
                    Assert.That(managerObject.activeSelf, Is.False,
                        $"{scenePath}: the SDK manager must stay inactive so desktop tests never load avatar native libraries.");

                    var avatarManager = managerObject.GetComponent<OvrAvatarManager>();
                    Assert.That(avatarManager, Is.Not.Null);
                    Assert.That(managerObject.GetComponent<AvatarLODManager>(), Is.Not.Null);

                    // Shader manager: reaching Style-2-Avatar-Meta through serialized
                    // references is what includes the URP avatar shader in device builds.
                    // Both SDK components self-repair on deserialize in the editor
                    // (OnValidate auto-fill), so in-memory non-null checks alone would pass
                    // on a broken scene while the device build — which has no OnValidate —
                    // ships Shader.Find("Standard")/null compute shaders. The references
                    // must therefore be PERSISTENT assets from the Meta package, and the
                    // GPU-skinning shader GUIDs must be present in the serialized scene
                    // text itself.
                    Assert.That(avatarManager.ShaderManager, Is.Not.Null,
                        $"{scenePath}: OvrAvatarManager.ShaderManager must be wired; the runtime fallback " +
                        "resolves Shader.Find(\"Standard\"), which is stripped from URP Android builds.");
                    var serializedShaderManager = new SerializedObject(avatarManager.ShaderManager);
                    foreach (string configurationField in new[]
                    {
                        "DefaultShaderConfigurationInitializer",
                        "FastLoadConfigurationInitializer",
                        "CelShaderConfigurationInitializer"
                    })
                    {
                        SerializedProperty property = serializedShaderManager.FindProperty(configurationField);
                        Assert.That(property, Is.Not.Null, configurationField);
                        UnityEngine.Object configuration = property.objectReferenceValue;
                        Assert.That(configuration, Is.Not.Null,
                            $"{scenePath}: {configurationField} must reference a Meta shader configuration asset.");
                        Assert.That(EditorUtility.IsPersistent(configuration), Is.True,
                            $"{scenePath}: {configurationField} must be a persistent asset, not a transient " +
                            "auto-generated configuration (whose shader is the stripped Built-in Standard).");
                        Assert.That(
                            AssetDatabase.GetAssetPath(configuration),
                            Does.StartWith("Packages/com.meta.xr.sdk.avatars/"),
                            $"{scenePath}: {configurationField} must come from the Meta Avatars package.");
                    }

                    Assert.That(managerObject.GetComponent<GpuSkinningConfiguration>(), Is.Not.Null);

                    string sceneText = System.IO.File.ReadAllText(scenePath);
                    foreach ((string shaderName, string guid) in new[]
                    {
                        ("combine-morph-targets.shader", "bef2973cf0137a04fbdd59ed0bab09bc"),
                        ("skin-to-texture.shader", "ee1f1406edf162945868fafc408691aa"),
                        ("OvrApplyMorphsAndSkinning.compute", "6593496738c8e8045badc49f0ef2917a"),
                    })
                    {
                        Assert.That(sceneText, Does.Contain(guid),
                            $"{scenePath}: the serialized scene must reference {shaderName} ({guid}) on " +
                            "GpuSkinningConfiguration — the editor-only OnValidate auto-fill masks a missing " +
                            "reference here while the device build gets null compute shaders.");
                    }
                }
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static void DisableMetaProjectSetupBackgroundChecks()
        {
            // Keep scene-opening tests isolated from Meta's background project setup.
            // Newer Meta Core families have crashed here in Linux batchmode when
            // OVRPlugin reports an unsupported 0.0.0 wrapper version.
            Type updaterType = Type.GetType("OVRProjectSetupUpdater, Oculus.VR.Editor")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("OVRProjectSetupUpdater"))
                    .FirstOrDefault(type => type != null);
            MethodInfo setupTemporaryRegistry = updaterType?.GetMethod(
                "SetupTemporaryRegistry",
                BindingFlags.Static | BindingFlags.NonPublic);
            setupTemporaryRegistry?.Invoke(null, null);
        }
    }
}

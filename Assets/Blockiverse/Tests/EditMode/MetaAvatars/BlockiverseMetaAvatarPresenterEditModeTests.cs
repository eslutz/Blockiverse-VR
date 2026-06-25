using System;
using System.Linq;
using System.Reflection;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using Oculus.Avatar2;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace Blockiverse.Tests.MetaAvatars.EditMode
{
    public sealed class BlockiverseMetaAvatarPresenterEditModeTests
    {
        GameObject root;
        GameObject head;
        GameObject leftHand;
        GameObject rightHand;
        static readonly string[] AvatarScenePaths =
        {
            "Assets/Blockiverse/Scenes/Boot.unity",
            "Assets/Blockiverse/Scenes/MultiplayerTest.unity"
        };

        [TearDown]
        public void TearDown()
        {
            Destroy(root);
            Destroy(head);
            Destroy(leftHand);
            Destroy(rightHand);
        }

        [Test]
        public void LocalFirstPersonAvatarKeepsFallbackUntilProviderReady()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(out BlockiverseNetworkAvatarRig fallbackRig, out FakeMetaAvatarProvider provider);
            var sources = CreateTrackingSources();

            presenter.Configure(provider, fallbackRig, sources, MetaAvatarPresentationMode.LocalFirstPerson);
            provider.IsAvatarReady = false;
            provider.FallbackReason = "Meta user avatar is still loading.";

            presenter.RefreshAvatarState();

            Assert.That(provider.Mode, Is.EqualTo(MetaAvatarPresentationMode.LocalFirstPerson));
            Assert.That(provider.HideFirstPersonHead, Is.True);
            Assert.That(provider.Sources.Head, Is.SameAs(head.transform));
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.True);
            Assert.That(
                fallbackRig.FallbackRoot.GetComponentsInChildren<Renderer>(includeInactive: true),
                Has.All.Matches<Renderer>(renderer => !renderer.enabled));
            Assert.That(fallbackRig.MetaAvatarAvailable, Is.False);
            Assert.That(presenter.LastFallbackReason, Is.EqualTo(provider.FallbackReason));

            provider.IsAvatarReady = true;
            presenter.RefreshAvatarState();

            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.False);
            Assert.That(fallbackRig.MetaAvatarAvailable, Is.True);
            Assert.That(presenter.LastFallbackReason, Is.Empty);
        }

        [Test]
        public void RemoteAvatarKeepsFallbackUntilStreamDataIsApplied()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(out BlockiverseNetworkAvatarRig fallbackRig, out FakeMetaAvatarProvider provider);
            var stream = new byte[] { 1, 2, 3, 4 };

            presenter.Configure(provider, fallbackRig, MetaAvatarTrackingSources.Empty, MetaAvatarPresentationMode.RemoteThirdPerson);
            presenter.RefreshAvatarState();

            Assert.That(provider.Mode, Is.EqualTo(MetaAvatarPresentationMode.RemoteThirdPerson));
            Assert.That(provider.HideFirstPersonHead, Is.False);
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.True);

            presenter.ApplyRemoteStream(stream);
            presenter.RefreshAvatarState();

            Assert.That(provider.LastAppliedStream, Is.EqualTo(stream));
            Assert.That(fallbackRig.IsUsingFallbackProxy, Is.False);
            Assert.That(fallbackRig.MetaAvatarAvailable, Is.True);
        }

        [Test]
        public void LocalStreamCaptureReturnsProviderBytesWhenAvatarIsReady()
        {
            BlockiverseMetaAvatarPresenter presenter = CreatePresenter(out BlockiverseNetworkAvatarRig fallbackRig, out FakeMetaAvatarProvider provider);
            byte[] expected = { 9, 8, 7 };

            provider.IsAvatarReady = true;
            provider.RecordedStream = expected;
            presenter.Configure(provider, fallbackRig, MetaAvatarTrackingSources.Empty, MetaAvatarPresentationMode.LocalFirstPerson);

            Assert.That(presenter.TryRecordLocalStream(out byte[] actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void AvatarStreamRelayUsesUnreliableDeliveryAndBoundedPayloads()
        {
            MethodInfo submit = typeof(MetaAvatarStreamRelay).GetMethod(
                "SubmitAvatarStreamServerRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo receive = typeof(MetaAvatarStreamRelay).GetMethod(
                "ReceiveAvatarStreamClientRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(MetaAvatarStreamMessage.MaxPayloadBytes, Is.LessThan(64 * 1024));
            Assert.That(submit, Is.Not.Null);
            Assert.That(receive, Is.Not.Null);
            Assert.That(submit.GetCustomAttribute<ServerRpcAttribute>()?.Delivery, Is.EqualTo(RpcDelivery.Unreliable));
            Assert.That(receive.GetCustomAttribute<ClientRpcAttribute>()?.Delivery, Is.EqualTo(RpcDelivery.Unreliable));
            Assert.That(
                new MetaAvatarStreamMessage(1, 0.0, new byte[MetaAvatarStreamMessage.MaxPayloadBytes + 1]).HasValidPayload,
                Is.False);
        }

        [Test]
        public void AvatarStreamMessageReusesExistingPayloadBufferWhenLengthMatches()
        {
            byte[] payload = { 1, 2, 3, 4 };
            var encoded = new MetaAvatarStreamMessage(7, 12.5, payload);

            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteNetworkSerializable(encoded);

            using var reader = new FastBufferReader(writer, Allocator.Temp);
            byte[] reusablePayload = new byte[payload.Length];
            var decoded = new MetaAvatarStreamMessage
            {
                Payload = reusablePayload,
            };
            reader.ReadNetworkSerializableInPlace(ref decoded);

            Assert.That(decoded.Payload, Is.SameAs(reusablePayload));
            CollectionAssert.AreEqual(payload, decoded.Payload);
            Assert.That(decoded.SenderClientId, Is.EqualTo(7UL));
            Assert.That(decoded.SentTime, Is.EqualTo(12.5));
        }

        [Test]
        public void AvatarEntityPresentationConfigurationDoesNotCreateNativeEntityBeforeInputIsAssigned()
        {
            root = new GameObject("Meta Avatar Entity Test");
            BlockiverseMetaAvatarEntity entity = root.AddComponent<BlockiverseMetaAvatarEntity>();

            entity.ConfigurePresentation(MetaAvatarPresentationMode.LocalFirstPerson, hideHeadForFirstPerson: true);

            Assert.That(entity.IsCreated, Is.False);
            Assert.That(entity.IsRenderableReady, Is.False);
            Assert.That(entity.InputManager, Is.Null);
            Assert.That(GetCreationInfoRenderFilters(entity).viewFlags, Is.EqualTo(CAPI.ovrAvatar2EntityViewFlags.FirstPerson));
            Assert.That(
                GetCreationInfoRenderFilters(entity).manifestationFlags,
                Is.EqualTo(CAPI.ovrAvatar2EntityManifestationFlags.Hands));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AvatarEntityResolvesInputManagerFromParentRig()
        {
            root = new GameObject("Meta Avatar Rig Test");
            TestAvatarInputManager inputManager = root.AddComponent<TestAvatarInputManager>();
            var entityObject = new GameObject("Meta Horizon Avatar Entity");
            entityObject.transform.SetParent(root.transform, false);
            BlockiverseMetaAvatarEntity entity = entityObject.AddComponent<BlockiverseMetaAvatarEntity>();

            entity.EnsureInputManager();

            Assert.That(entity.InputManager, Is.SameAs(inputManager));
        }

        [Test]
        public void AvatarEntityCreationWaitsWhenAvatarManagerIsUnavailable()
        {
            root = new GameObject("Meta Avatar Entity Creation Test");
            BlockiverseMetaAvatarEntity entity = root.AddComponent<BlockiverseMetaAvatarEntity>();
            entity.ConfigurePresentation(MetaAvatarPresentationMode.LocalFirstPerson, hideHeadForFirstPerson: true);

            Assert.That(entity.CreateConfiguredEntity(), Is.False);

            Assert.That(entity.IsCreated, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AvatarSdkSceneManagersStayInactiveForEditorPlayMode()
        {
            DisableMetaProjectSetupBackgroundChecks();

            try
            {
                foreach (string scenePath in AvatarScenePaths)
                {
                    Scene scene = OpenSceneIgnoringUnityCleanupLogs(scenePath);
                    OvrAvatarManager[] managers = scene.GetRootGameObjects()
                        .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<OvrAvatarManager>(true))
                        .ToArray();

                    Assert.That(
                        managers.Where(manager => manager.isActiveAndEnabled),
                        Is.Empty,
                        $"{scenePath} must not initialize Avatar SDK native libraries in editor PlayMode.");
                }
            }
            finally
            {
                OpenEmptySceneIgnoringUnityCleanupLogs();
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

        BlockiverseMetaAvatarPresenter CreatePresenter(
            out BlockiverseNetworkAvatarRig fallbackRig,
            out FakeMetaAvatarProvider provider)
        {
            root = new GameObject("Meta Avatar Presenter Test");
            fallbackRig = root.AddComponent<BlockiverseNetworkAvatarRig>();
            provider = root.AddComponent<FakeMetaAvatarProvider>();
            return root.AddComponent<BlockiverseMetaAvatarPresenter>();
        }

        MetaAvatarTrackingSources CreateTrackingSources()
        {
            head = new GameObject("Main Camera");
            leftHand = new GameObject("Left Controller");
            rightHand = new GameObject("Right Controller");
            return new MetaAvatarTrackingSources(head.transform, leftHand.transform, rightHand.transform);
        }

        static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
                UnityEngine.Object.DestroyImmediate(gameObject);
        }

        static void OpenEmptySceneIgnoringUnityCleanupLogs()
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        static Scene OpenSceneIgnoringUnityCleanupLogs(string scenePath)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        static CAPI.ovrAvatar2EntityFilters GetCreationInfoRenderFilters(BlockiverseMetaAvatarEntity entity)
        {
            FieldInfo creationInfoField = typeof(OvrAvatarEntity).GetField(
                "_creationInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(creationInfoField, Is.Not.Null);

            object creationInfo = creationInfoField.GetValue(entity);
            FieldInfo renderFiltersField = creationInfo.GetType().GetField("renderFilters");
            Assert.That(renderFiltersField, Is.Not.Null);
            return (CAPI.ovrAvatar2EntityFilters)renderFiltersField.GetValue(creationInfo);
        }

        sealed class FakeMetaAvatarProvider : MonoBehaviour, IBlockiverseMetaAvatarProvider
        {
            public bool IsAvatarReady { get; set; }
            public string FallbackReason { get; set; } = string.Empty;
            public bool HideFirstPersonHead { get; private set; }
            public MetaAvatarPresentationMode Mode { get; private set; }
            public MetaAvatarTrackingSources Sources { get; private set; }
            public byte[] LastAppliedStream { get; private set; } = Array.Empty<byte>();
            public byte[] RecordedStream { get; set; } = Array.Empty<byte>();

            public void Configure(MetaAvatarTrackingSources sources, MetaAvatarPresentationMode mode, bool hideFirstPersonHead)
            {
                Sources = sources;
                Mode = mode;
                HideFirstPersonHead = hideFirstPersonHead;
            }

            public void TickProvider()
            {
            }

            public bool TryRecordStream(out byte[] streamData)
            {
                streamData = RecordedStream;
                return IsAvatarReady && streamData.Length > 0;
            }

            public void ApplyStreamData(byte[] streamData)
            {
                LastAppliedStream = streamData;
                IsAvatarReady = streamData is { Length: > 0 };
            }
        }

        sealed class TestAvatarInputManager : OvrAvatarInputManagerBehavior
        {
            public override OvrAvatarInputTrackingProviderBase InputTrackingProvider => null;
            public override OvrAvatarInputControlProviderBase InputControlProvider => null;
            public override OvrAvatarBodyTrackingContextBase BodyTrackingContext => null;
            public override OvrAvatarHandTrackingPoseProviderBase HandTrackingProvider => null;
        }
    }
}

using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class BlockiverseNetworkAvatarRigEditModeTests
    {
        GameObject avatarObject;

        [TearDown]
        public void TearDown()
        {
            if (avatarObject != null)
                Object.DestroyImmediate(avatarObject);
        }

        [Test]
        public void MissingMetaAvatarUsesFallbackProxyAnchorsWithoutRenderingFirstPersonGeometry()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();

            avatarRig.SetMetaAvatarAvailable(false);

            Assert.That(avatarRig.IsUsingFallbackProxy, Is.True);
            Assert.That(avatarRig.FallbackRoot, Is.Not.Null);
            Assert.That(avatarRig.FallbackRoot.gameObject.activeSelf, Is.True);
            Assert.That(avatarRig.HeadAnchor, Is.Not.Null);
            Assert.That(avatarRig.LeftHandAnchor, Is.Not.Null);
            Assert.That(avatarRig.RightHandAnchor, Is.Not.Null);
            Renderer[] renderers = avatarRig.FallbackRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            Assert.That(renderers, Has.Length.GreaterThanOrEqualTo(4));
            Assert.That(renderers, Has.All.Matches<Renderer>(renderer => !renderer.enabled));
        }

        [Test]
        public void FirstPersonFallbackProxyRendersHandsOnly()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();

            avatarRig.ConfigureFirstPersonFallbackVisuals(true);
            avatarRig.SetMetaAvatarAvailable(false);

            Assert.That(avatarRig.IsUsingFallbackProxy, Is.True);
            Renderer[] renderers = avatarRig.FallbackRoot.GetComponentsInChildren<Renderer>(includeInactive: true);

            Assert.That(renderers, Has.Some.Matches<Renderer>(renderer =>
                renderer.transform.name == "Fallback Left Hand" && renderer.enabled));
            Assert.That(renderers, Has.Some.Matches<Renderer>(renderer =>
                renderer.transform.name == "Fallback Right Hand" && renderer.enabled));
            Assert.That(renderers, Has.None.Matches<Renderer>(renderer =>
                renderer.transform.name == "Fallback Head" && renderer.enabled));
            Assert.That(renderers, Has.None.Matches<Renderer>(renderer =>
                renderer.transform.name == "Fallback Body" && renderer.enabled));
        }

        [Test]
        public void FirstPersonFallbackHandsCanBeSuppressedWhileSystemKeyboardIsVisible()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();
            MethodInfo suppressMethod = typeof(BlockiverseNetworkAvatarRig).GetMethod(
                "SetFirstPersonFallbackVisualsSuppressed",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(suppressMethod, Is.Not.Null,
                "The local fallback hand proxy needs an explicit suppression switch for system keyboard entry.");

            avatarRig.ConfigureFirstPersonFallbackVisuals(true);
            avatarRig.SetMetaAvatarAvailable(false);

            suppressMethod.Invoke(avatarRig, new object[] { true });

            Renderer[] renderers = avatarRig.FallbackRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            Assert.That(renderers, Has.None.Matches<Renderer>(renderer =>
                renderer.transform.name == "Fallback Left Hand" && renderer.enabled));
            Assert.That(renderers, Has.None.Matches<Renderer>(renderer =>
                renderer.transform.name == "Fallback Right Hand" && renderer.enabled));
            Assert.That(avatarRig.FallbackRoot.gameObject.activeSelf, Is.True,
                "Keyboard suppression should hide local hand renderers without disabling the fallback proxy object.");

            suppressMethod.Invoke(avatarRig, new object[] { false });

            Assert.That(renderers, Has.Some.Matches<Renderer>(renderer =>
                renderer.transform.name == "Fallback Left Hand" && renderer.enabled));
            Assert.That(renderers, Has.Some.Matches<Renderer>(renderer =>
                renderer.transform.name == "Fallback Right Hand" && renderer.enabled));
        }

        [Test]
        public void AvailableMetaAvatarHidesFallbackProxy()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();

            avatarRig.SetMetaAvatarAvailable(true);

            Assert.That(avatarRig.IsUsingFallbackProxy, Is.False);
            Assert.That(avatarRig.FallbackRoot.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void AvatarPoseRpcsUseUnreliableDelivery()
        {
            MethodInfo submit = typeof(BlockiverseNetworkAvatarRig).GetMethod(
                "SubmitAvatarPoseServerRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo receive = typeof(BlockiverseNetworkAvatarRig).GetMethod(
                "ReceiveAvatarPoseClientRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(submit, Is.Not.Null);
            Assert.That(receive, Is.Not.Null);
            Assert.That(submit.GetCustomAttribute<ServerRpcAttribute>()?.Delivery, Is.EqualTo(RpcDelivery.Unreliable));
            Assert.That(receive.GetCustomAttribute<ClientRpcAttribute>()?.Delivery, Is.EqualTo(RpcDelivery.Unreliable));
        }

        [Test]
        public void LocalRigPoseUpdatesFallbackAnchors()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();
            var headPose = new Pose(new Vector3(0.0f, 1.7f, 0.1f), Quaternion.Euler(0.0f, 30.0f, 0.0f));
            var leftHandPose = new Pose(new Vector3(-0.45f, 1.1f, 0.35f), Quaternion.Euler(5.0f, 0.0f, -10.0f));
            var rightHandPose = new Pose(new Vector3(0.45f, 1.1f, 0.35f), Quaternion.Euler(5.0f, 0.0f, 10.0f));

            avatarRig.SetLocalRigPose(headPose, leftHandPose, rightHandPose);

            Assert.That(avatarRig.HeadAnchor.localPosition, Is.EqualTo(headPose.position));
            Assert.That(avatarRig.LeftHandAnchor.localPosition, Is.EqualTo(leftHandPose.position));
            Assert.That(avatarRig.RightHandAnchor.localPosition, Is.EqualTo(rightHandPose.position));
            Assert.That(avatarRig.HeadAnchor.localRotation.eulerAngles.y, Is.EqualTo(30.0f).Within(0.01f));
        }

        [Test]
        public void LightningSafetyResolvesSyncedHeadAnchorInsteadOfPlayerRoot()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();
            NetworkObject playerObject = avatarRig.gameObject.AddComponent<NetworkObject>();
            var headPose = new Pose(new Vector3(12.5f, 1.7f, -6.5f), Quaternion.identity);

            avatarRig.SetLocalRigPose(
                headPose,
                new Pose(new Vector3(-0.4f, 1.2f, 0.3f), Quaternion.identity),
                new Pose(new Vector3(0.4f, 1.2f, 0.3f), Quaternion.identity));

            Assert.That(
                BlockiverseNetworkAvatarRig.TryResolvePlayerHeadWorldPosition(playerObject, out Vector3 resolvedHead),
                Is.True);
            AssertVector3Approximately(resolvedHead, headPose.position);
            Assert.That(resolvedHead, Is.Not.EqualTo(avatarRig.transform.position));
        }

        [Test]
        public void UnspawnedSinglePlayerRigTracksLocalHeadAndHands()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();
            GameObject head = CreateTrackingSource("Single Player Head", new Vector3(0.0f, 1.72f, 0.08f));
            GameObject leftHand = CreateTrackingSource("Single Player Left Hand", new Vector3(-0.42f, 1.16f, 0.32f));
            GameObject rightHand = CreateTrackingSource("Single Player Right Hand", new Vector3(0.42f, 1.16f, 0.32f));

            try
            {
                avatarRig.ConfigureTrackingSources(head.transform, leftHand.transform, rightHand.transform);
                avatarRig.RefreshLocalTrackingPose();

                Assert.That(avatarRig.HeadAnchor.localPosition, Is.EqualTo(head.transform.position));
                Assert.That(avatarRig.LeftHandAnchor.localPosition, Is.EqualTo(leftHand.transform.position));
                Assert.That(avatarRig.RightHandAnchor.localPosition, Is.EqualTo(rightHand.transform.position));
            }
            finally
            {
                Object.DestroyImmediate(head);
                Object.DestroyImmediate(leftHand);
                Object.DestroyImmediate(rightHand);
            }
        }

        [Test]
        public void ParentedTrackingSourcesMoveAvatarRootBeforeLocalAnchorPose()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();
            GameObject rigRoot = new("XR Tracking Root");
            rigRoot.transform.SetPositionAndRotation(
                new Vector3(2.0f, 0.0f, -3.0f),
                Quaternion.Euler(0.0f, 35.0f, 0.0f));
            Transform cameraOffset = new GameObject("Camera Offset").transform;
            cameraOffset.SetParent(rigRoot.transform, worldPositionStays: false);
            Transform head = CreateTrackingChild(cameraOffset, "Main Camera", new Vector3(0.0f, 1.7f, 0.1f));
            Transform leftHand = CreateTrackingChild(cameraOffset, "Left Controller", new Vector3(-0.45f, 1.16f, 0.28f));
            Transform rightHand = CreateTrackingChild(cameraOffset, "Right Controller", new Vector3(0.45f, 1.16f, 0.28f));

            try
            {
                avatarRig.ConfigureTrackingSources(head, leftHand, rightHand);
                avatarRig.RefreshLocalTrackingPose();

                AssertVector3Approximately(avatarRig.transform.position, rigRoot.transform.position);
                AssertQuaternionYApproximately(avatarRig.transform.rotation, rigRoot.transform.rotation);
                AssertVector3Approximately(avatarRig.HeadAnchor.localPosition, head.localPosition);
                AssertVector3Approximately(avatarRig.LeftHandAnchor.localPosition, leftHand.localPosition);
                AssertVector3Approximately(avatarRig.RightHandAnchor.localPosition, rightHand.localPosition);
            }
            finally
            {
                Object.DestroyImmediate(rigRoot);
            }
        }

        [Test]
        public void HeadOnlyTrackingFallbackFindsSiblingControllersUnderCameraOffset()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();
            GameObject rigRoot = new("XR Tracking Root");
            rigRoot.transform.SetPositionAndRotation(
                new Vector3(-1.0f, 0.0f, 2.0f),
                Quaternion.Euler(0.0f, -20.0f, 0.0f));
            Transform cameraOffset = new GameObject("Camera Offset").transform;
            cameraOffset.SetParent(rigRoot.transform, worldPositionStays: false);
            Transform head = CreateTrackingChild(cameraOffset, "Main Camera", new Vector3(0.0f, 1.65f, 0.04f));
            Transform leftHand = CreateTrackingChild(cameraOffset, "Left Controller", new Vector3(-0.36f, 1.14f, 0.25f));
            Transform rightHand = CreateTrackingChild(cameraOffset, "Right Controller", new Vector3(0.36f, 1.14f, 0.25f));

            try
            {
                avatarRig.ConfigureTrackingSources(null, head, null, null);
                avatarRig.RefreshLocalTrackingPose();

                AssertVector3Approximately(avatarRig.transform.position, rigRoot.transform.position);
                AssertQuaternionYApproximately(avatarRig.transform.rotation, rigRoot.transform.rotation);
                AssertVector3Approximately(avatarRig.HeadAnchor.localPosition, head.localPosition);
                AssertVector3Approximately(avatarRig.LeftHandAnchor.localPosition, leftHand.localPosition);
                AssertVector3Approximately(avatarRig.RightHandAnchor.localPosition, rightHand.localPosition);
            }
            finally
            {
                Object.DestroyImmediate(rigRoot);
            }
        }

        BlockiverseNetworkAvatarRig CreateAvatarRig()
        {
            avatarObject = new GameObject("Network Avatar Test");
            return avatarObject.AddComponent<BlockiverseNetworkAvatarRig>();
        }

        static GameObject CreateTrackingSource(string name, Vector3 position)
        {
            GameObject source = new(name);
            source.transform.position = position;
            return source;
        }

        static Transform CreateTrackingChild(Transform parent, string name, Vector3 localPosition)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, worldPositionStays: false);
            child.localPosition = localPosition;
            return child;
        }

        static void AssertVector3Approximately(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        static void AssertQuaternionYApproximately(Quaternion actual, Quaternion expected)
        {
            Assert.That(actual.eulerAngles.y, Is.EqualTo(expected.eulerAngles.y).Within(0.001f));
        }
    }
}

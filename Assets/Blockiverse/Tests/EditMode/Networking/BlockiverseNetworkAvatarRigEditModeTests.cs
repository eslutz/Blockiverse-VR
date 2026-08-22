using System.Linq;
using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

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
        public void FallbackProxyRenderersNeverCastShadows()
        {
            // The player is a pair of floating hands with no body, so a cast shadow reads as two
            // disembodied blobs on the ground beside them. CreatePrimitive defaults shadow casting
            // ON and nothing had turned it off, unlike every other non-terrain renderer in the
            // project.
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();
            avatarRig.ConfigureFirstPersonFallbackVisuals(true);
            avatarRig.SetMetaAvatarAvailable(false);

            Renderer[] renderers = avatarRig.FallbackRoot.GetComponentsInChildren<Renderer>(includeInactive: true);

            Assert.That(renderers, Is.Not.Empty, "Fixture guard: the fallback proxy should have renderers.");
            Assert.That(
                renderers,
                Has.All.Matches<Renderer>(renderer => renderer.shadowCastingMode == ShadowCastingMode.Off),
                "No part of the bodiless player proxy may cast a shadow.");
        }

        [Test]
        public void EveryPlayerGetsTheSameWarmHandColour()
        {
            // This replaces an owner/remote colour test. The split existed to tell your hands from
            // someone else's, which it never actually did -- your own hands are always the pair
            // attached to your view -- and the owner half was a blue that did not read as a hand.
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();
            avatarRig.ConfigureFirstPersonFallbackVisuals(true);
            avatarRig.SetMetaAvatarAvailable(false);

            Assert.That(
                typeof(BlockiverseNetworkAvatarRig).GetField(
                    "remoteFallbackColor", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null,
                "The second colour should be removed, not left as a dead serialized field.");

            Renderer handRenderer = avatarRig.FallbackRoot
                .GetComponentsInChildren<Renderer>(includeInactive: true)
                .First(renderer => renderer.transform.name == "Fallback Left Hand");

            Color rendered = handRenderer.sharedMaterial.color;
            Assert.That(rendered.r, Is.GreaterThan(rendered.b), "Hands should read warm.");
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
        public void StaleStreamSwapsAvailableMetaAvatarBackToFallbackProxy()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();

            avatarRig.SetMetaAvatarAvailable(true);
            Assert.That(avatarRig.IsUsingFallbackProxy, Is.False);
            Assert.That(avatarRig.FallbackRoot.gameObject.activeSelf, Is.False);

            avatarRig.SetStreamStale(true);

            Assert.That(avatarRig.IsUsingFallbackProxy, Is.True,
                "A stale Meta avatar stream must swap back to the fallback proxy so the remote player stays visible.");
            Assert.That(avatarRig.FallbackRoot.gameObject.activeSelf, Is.True);

            avatarRig.SetStreamStale(false);

            Assert.That(avatarRig.IsUsingFallbackProxy, Is.False);
            Assert.That(avatarRig.FallbackRoot.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void AvatarPoseRpcsUseUnreliableDelivery()
        {
            MethodInfo submit = typeof(BlockiverseNetworkAvatarRig).GetMethod(
                "SubmitAvatarPoseRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo receive = typeof(BlockiverseNetworkAvatarRig).GetMethod(
                "ReceiveAvatarPoseRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(submit, Is.Not.Null);
            Assert.That(receive, Is.Not.Null);

            // Pose is disposable presentation data at 30 Hz: a dropped frame is cheaper than a
            // retransmit, and a retransmitted stale pose is actively worse than none.
            Assert.That(submit.GetCustomAttribute<RpcAttribute>()?.Delivery, Is.EqualTo(RpcDelivery.Unreliable));
            Assert.That(receive.GetCustomAttribute<RpcAttribute>()?.Delivery, Is.EqualTo(RpcDelivery.Unreliable));

            // The universal-RPC migration must not have quietly widened who can publish a pose:
            // the old [ServerRpc] required ownership by default, the new attribute does not.
            Assert.That(
                submit.GetCustomAttribute<RpcAttribute>()?.InvokePermission,
                Is.EqualTo(RpcInvokePermission.Owner));

            // The SendTo target is consumed by Netcode's ILPP and is not readable through
            // reflection, so the pinned proxy is that the legacy attributes are gone.
            Assert.That(submit.GetCustomAttribute<ServerRpcAttribute>(), Is.Null);
            Assert.That(receive.GetCustomAttribute<ClientRpcAttribute>(), Is.Null);
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

        [Test]
        public void CompressedPoseRoundTripsWithinVisualTolerance()
        {
            var expected = new BlockiverseNetworkAvatarRig.AvatarPose
            {
                Sequence = 42u,
                RootPosition = new Vector3(191.25f, 96.5f, -37.125f),
                RootRotation = Quaternion.Euler(0.0f, 137.0f, 0.0f),
                HeadLocalPosition = new Vector3(0.02f, 1.63f, 0.05f),
                HeadLocalRotation = Quaternion.Euler(18.0f, -42.0f, 6.0f),
                LeftHandLocalPosition = new Vector3(-0.38f, 1.18f, 0.28f),
                LeftHandLocalRotation = Quaternion.Euler(-70.0f, 12.0f, 95.0f),
                RightHandLocalPosition = new Vector3(0.38f, 1.18f, 0.28f),
                RightHandLocalRotation = Quaternion.Euler(33.0f, 210.0f, -15.0f),
            };

            BlockiverseNetworkAvatarRig.AvatarPose actual = RoundTrip(expected);

            Assert.That(actual.Sequence, Is.EqualTo(expected.Sequence));

            // The root carries world coordinates and is sent at full precision.
            Assert.That(actual.RootPosition, Is.EqualTo(expected.RootPosition));

            // Offsets are 16-bit fixed point over +/-4 m; sub-millimetre is far below what a
            // player can perceive on a remote avatar.
            Assert.That(
                Vector3.Distance(actual.HeadLocalPosition, expected.HeadLocalPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Distance(actual.LeftHandLocalPosition, expected.LeftHandLocalPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Distance(actual.RightHandLocalPosition, expected.RightHandLocalPosition),
                Is.LessThan(0.001f));

            Assert.That(Quaternion.Angle(actual.RootRotation, expected.RootRotation), Is.LessThan(0.5f));
            Assert.That(Quaternion.Angle(actual.HeadLocalRotation, expected.HeadLocalRotation), Is.LessThan(0.5f));
            Assert.That(Quaternion.Angle(actual.LeftHandLocalRotation, expected.LeftHandLocalRotation), Is.LessThan(0.5f));
            Assert.That(Quaternion.Angle(actual.RightHandLocalRotation, expected.RightHandLocalRotation), Is.LessThan(0.5f));
        }

        [Test]
        public void CompressedPoseIsSubstantiallySmallerThanTheUncompressedLayout()
        {
            var pose = BlockiverseNetworkAvatarRig.AvatarPose.Default;
            var writer = new FastBufferWriter(256, Allocator.Temp);

            try
            {
                writer.WriteNetworkSerializable(pose);

                // 8 uncompressed Vector3/Quaternion fields would be 112 bytes before the sequence.
                Assert.That(writer.Length, Is.LessThan(64));
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void RotationCompressionHandlesEveryDominantComponentAndDegenerateInput()
        {
            // One rotation per dominant quaternion component, so the 2-bit largest-index encoding
            // is exercised in all four branches.
            var rotations = new[]
            {
                Quaternion.identity,
                Quaternion.Euler(90.0f, 0.0f, 0.0f),
                Quaternion.Euler(0.0f, 90.0f, 0.0f),
                Quaternion.Euler(0.0f, 0.0f, 90.0f),
                Quaternion.Euler(179.0f, 0.0f, 0.0f),
                Quaternion.Euler(-120.0f, 47.0f, 88.0f),
            };

            foreach (Quaternion rotation in rotations)
            {
                Quaternion decompressed = BlockiverseNetworkAvatarRig.AvatarPose.DecompressRotation(
                    BlockiverseNetworkAvatarRig.AvatarPose.CompressRotation(rotation));

                Assert.That(
                    Quaternion.Angle(decompressed, rotation),
                    Is.LessThan(0.5f),
                    $"Rotation {rotation.eulerAngles} did not survive compression.");
            }

            // A zero quaternion cannot be normalized; it must fall back to identity rather than
            // producing NaNs that would propagate into a remote avatar's transform.
            Quaternion degenerate = BlockiverseNetworkAvatarRig.AvatarPose.DecompressRotation(
                BlockiverseNetworkAvatarRig.AvatarPose.CompressRotation(new Quaternion(0.0f, 0.0f, 0.0f, 0.0f)));

            Assert.That(float.IsNaN(degenerate.x), Is.False);
            Assert.That(Quaternion.Angle(degenerate, Quaternion.identity), Is.LessThan(0.5f));
        }

        [Test]
        public void StaleRemotePosesAreDroppedAndNewerOnesApplied()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();

            BlockiverseNetworkAvatarRig.AvatarPose first = BlockiverseNetworkAvatarRig.AvatarPose.Default;
            first.Sequence = 10u;
            first.RootPosition = new Vector3(5.0f, 0.0f, 0.0f);
            avatarRig.ApplyRemotePose(first);

            // Arrived late after overtaking: applying it would snap the remote body backwards.
            BlockiverseNetworkAvatarRig.AvatarPose stale = BlockiverseNetworkAvatarRig.AvatarPose.Default;
            stale.Sequence = 9u;
            stale.RootPosition = new Vector3(-99.0f, 0.0f, 0.0f);
            avatarRig.ApplyRemotePose(stale);

            Assert.That(CurrentTargetPose(avatarRig).RootPosition.x, Is.EqualTo(5.0f).Within(0.001f));

            BlockiverseNetworkAvatarRig.AvatarPose newer = BlockiverseNetworkAvatarRig.AvatarPose.Default;
            newer.Sequence = 11u;
            newer.RootPosition = new Vector3(7.0f, 0.0f, 0.0f);
            avatarRig.ApplyRemotePose(newer);

            Assert.That(CurrentTargetPose(avatarRig).RootPosition.x, Is.EqualTo(7.0f).Within(0.001f));
        }

        [Test]
        public void PoseSequenceComparisonSurvivesTheUnsignedWrap()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();

            BlockiverseNetworkAvatarRig.AvatarPose beforeWrap = BlockiverseNetworkAvatarRig.AvatarPose.Default;
            beforeWrap.Sequence = uint.MaxValue;
            beforeWrap.RootPosition = new Vector3(1.0f, 0.0f, 0.0f);
            avatarRig.ApplyRemotePose(beforeWrap);

            // A plain '>' would treat this as ancient and stall the avatar for the rest of the
            // session; serial-number arithmetic sees it as the next pose.
            BlockiverseNetworkAvatarRig.AvatarPose afterWrap = BlockiverseNetworkAvatarRig.AvatarPose.Default;
            afterWrap.Sequence = 1u;
            afterWrap.RootPosition = new Vector3(2.0f, 0.0f, 0.0f);
            avatarRig.ApplyRemotePose(afterWrap);

            Assert.That(CurrentTargetPose(avatarRig).RootPosition.x, Is.EqualTo(2.0f).Within(0.001f));
        }

        [Test]
        public void UnsequencedPosesAreAlwaysApplied()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();

            BlockiverseNetworkAvatarRig.AvatarPose sequenced = BlockiverseNetworkAvatarRig.AvatarPose.Default;
            sequenced.Sequence = 100u;
            avatarRig.ApplyRemotePose(sequenced);

            // Sequence 0 means "applied directly" (local rig drives, tests), not "oldest pose".
            BlockiverseNetworkAvatarRig.AvatarPose unsequenced = BlockiverseNetworkAvatarRig.AvatarPose.Default;
            unsequenced.RootPosition = new Vector3(3.0f, 0.0f, 0.0f);
            avatarRig.ApplyRemotePose(unsequenced);

            Assert.That(CurrentTargetPose(avatarRig).RootPosition.x, Is.EqualTo(3.0f).Within(0.001f));
        }

        static BlockiverseNetworkAvatarRig.AvatarPose RoundTrip(BlockiverseNetworkAvatarRig.AvatarPose pose)
        {
            var writer = new FastBufferWriter(256, Allocator.Temp);

            try
            {
                writer.WriteNetworkSerializable(pose);
                var reader = new FastBufferReader(writer, Allocator.Temp);

                try
                {
                    reader.ReadNetworkSerializable(out BlockiverseNetworkAvatarRig.AvatarPose result);
                    return result;
                }
                finally
                {
                    reader.Dispose();
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        static BlockiverseNetworkAvatarRig.AvatarPose CurrentTargetPose(BlockiverseNetworkAvatarRig avatarRig)
        {
            FieldInfo field = typeof(BlockiverseNetworkAvatarRig).GetField(
                "targetRemotePose",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "The remote pose target should remain present.");
            return (BlockiverseNetworkAvatarRig.AvatarPose)field.GetValue(avatarRig);
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

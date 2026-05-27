using Blockiverse.Networking;
using NUnit.Framework;
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
        public void MissingMetaAvatarUsesFallbackProxy()
        {
            BlockiverseNetworkAvatarRig avatarRig = CreateAvatarRig();

            avatarRig.SetMetaAvatarAvailable(false);

            Assert.That(avatarRig.IsUsingFallbackProxy, Is.True);
            Assert.That(avatarRig.FallbackRoot, Is.Not.Null);
            Assert.That(avatarRig.FallbackRoot.gameObject.activeSelf, Is.True);
            Assert.That(avatarRig.HeadAnchor, Is.Not.Null);
            Assert.That(avatarRig.LeftHandAnchor, Is.Not.Null);
            Assert.That(avatarRig.RightHandAnchor, Is.Not.Null);
            Assert.That(avatarRig.FallbackRoot.GetComponentsInChildren<Renderer>(), Has.Length.GreaterThanOrEqualTo(4));
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

        BlockiverseNetworkAvatarRig CreateAvatarRig()
        {
            avatarObject = new GameObject("Network Avatar Test");
            return avatarObject.AddComponent<BlockiverseNetworkAvatarRig>();
        }
    }
}

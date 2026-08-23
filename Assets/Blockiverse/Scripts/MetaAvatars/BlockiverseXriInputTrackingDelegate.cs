using Oculus.Avatar2;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Feeds the Meta Avatars body solver from the XRI rig's own transforms instead of
    /// OVRInput. The Avatar SDK expects poses in tracking space — relative to the entity
    /// root, which Blockiverse pins to the XR Origin (rig root, floor level) — so every
    /// pose is converted from world space into rig-local space here. This removes the
    /// dependency on OVRManager/OVRInput entirely: the rig transforms are already driven
    /// by XRI TrackedPoseDriver from the same OpenXR runtime data.
    /// </summary>
    public sealed class BlockiverseXriInputTrackingDelegate : OvrAvatarInputTrackingDelegate
    {
        readonly Transform trackingOrigin;
        readonly Transform head;
        readonly Transform leftHand;
        readonly Transform rightHand;

        public BlockiverseXriInputTrackingDelegate(Transform trackingOrigin, Transform head, Transform leftHand, Transform rightHand)
        {
            this.trackingOrigin = trackingOrigin;
            this.head = head;
            this.leftHand = leftHand;
            this.rightHand = rightHand;
        }

        public override bool GetRawInputTrackingState(out OvrAvatarInputTrackingState inputTrackingState)
        {
            inputTrackingState = default;

            if (trackingOrigin == null || head == null)
                return false;

            inputTrackingState.headsetActive = true;
            inputTrackingState.headset = ToTrackingSpace(trackingOrigin, head);

            if (leftHand != null)
            {
                inputTrackingState.leftControllerActive = true;
                inputTrackingState.leftControllerVisible = true;
                inputTrackingState.leftController = ToTrackingSpace(trackingOrigin, leftHand);
            }

            if (rightHand != null)
            {
                inputTrackingState.rightControllerActive = true;
                inputTrackingState.rightControllerVisible = true;
                inputTrackingState.rightController = ToTrackingSpace(trackingOrigin, rightHand);
            }

            return true;
        }

        static CAPI.ovrAvatar2Transform ToTrackingSpace(Transform origin, Transform source)
        {
            ComputeTrackingSpacePose(
                origin.position, origin.rotation, origin.lossyScale,
                source.position, source.rotation,
                out Vector3 localPosition, out Quaternion localRotation);
            return new CAPI.ovrAvatar2Transform(localPosition, localRotation);
        }

        /// <summary>
        /// World pose -> tracking-space pose. Division by the origin's scale mirrors
        /// Transform.InverseTransformPoint, so a player-size-scaled rig hands the solver
        /// unscaled tracking data and the entity root's own scale re-applies the size.
        /// </summary>
        public static void ComputeTrackingSpacePose(
            Vector3 originPosition,
            Quaternion originRotation,
            Vector3 originScale,
            Vector3 worldPosition,
            Quaternion worldRotation,
            out Vector3 localPosition,
            out Quaternion localRotation)
        {
            Quaternion inverseRotation = Quaternion.Inverse(originRotation);
            Vector3 rotated = inverseRotation * (worldPosition - originPosition);
            localPosition = new Vector3(
                originScale.x != 0.0f ? rotated.x / originScale.x : rotated.x,
                originScale.y != 0.0f ? rotated.y / originScale.y : rotated.y,
                originScale.z != 0.0f ? rotated.z / originScale.z : rotated.z);
            localRotation = inverseRotation * worldRotation;
        }
    }
}

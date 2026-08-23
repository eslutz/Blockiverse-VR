using Oculus.Avatar2;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// <see cref="OvrAvatarInputManagerBehavior"/> for the Blockiverse XR rig.
    ///
    /// This project drives head/hand transforms through native XRI (no OVRCameraRig or
    /// OVRManager), so tracking is fed to the Avatar SDK from the rig's own transforms via
    /// <see cref="BlockiverseXriInputTrackingDelegate"/> — poses are converted into rig-root
    /// (tracking-space) coordinates, matching the avatar entity root pinned at the rig root.
    /// Controller trigger/grip/button state comes from the Input System XR devices via
    /// <see cref="BlockiverseXriInputControlDelegate"/> so avatar fingers articulate.
    /// Body tracking is intentionally null: the SDK's own body solver synthesizes the body
    /// from head + controllers.
    ///
    /// The component must be on the same GameObject as (or reachable from)
    /// <see cref="BlockiverseMetaAvatarEntity"/> so that OvrAvatarEntity can find it
    /// through its <see cref="OvrAvatarInputManagerBehavior"/> search.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class BlockiverseXriAvatarInputManager : OvrAvatarInputManagerBehavior
    {
        [SerializeField] Transform trackingOrigin;
        [SerializeField] Transform headSource;
        [SerializeField] Transform leftHandSource;
        [SerializeField] Transform rightHandSource;

        OvrAvatarInputTrackingProviderBase trackingProvider;
        OvrAvatarInputControlProviderBase controlProvider;

        public override OvrAvatarInputTrackingProviderBase InputTrackingProvider
        {
            get
            {
                EnsureProviders();
                return trackingProvider;
            }
        }

        public override OvrAvatarInputControlProviderBase InputControlProvider
        {
            get
            {
                EnsureProviders();
                return controlProvider;
            }
        }

        // No body-tracking rig in this project; the SDK's standalone body solver runs from
        // the tracking provider's head + controller poses.
        public override OvrAvatarBodyTrackingContextBase BodyTrackingContext => null;

        // No dedicated hand-tracking (finger pose); controller shapes come from the input
        // control state via the animation system.
        public override OvrAvatarHandTrackingPoseProviderBase HandTrackingProvider => null;

        public Transform TrackingOrigin => trackingOrigin;
        public Transform HeadSource => headSource;
        public Transform LeftHandSource => leftHandSource;
        public Transform RightHandSource => rightHandSource;

        /// <summary>
        /// Wire the transforms the tracking delegate reads. The origin must be the rig root
        /// (floor-level tracking space) — the same transform the avatar entity is parented
        /// under with an identity local pose.
        /// </summary>
        public void ConfigureSources(Transform origin, Transform head, Transform leftHand, Transform rightHand)
        {
            trackingOrigin = origin;
            headSource = head;
            leftHandSource = leftHand;
            rightHandSource = rightHand;

            // Providers capture the transforms at construction; rebuild on rewire.
            trackingProvider = null;
            controlProvider = null;
        }

        void EnsureProviders()
        {
            if (trackingProvider == null && trackingOrigin != null && headSource != null)
            {
                trackingProvider = new OvrAvatarInputTrackingDelegatedProvider(
                    new BlockiverseXriInputTrackingDelegate(trackingOrigin, headSource, leftHandSource, rightHandSource));
            }

            controlProvider ??= new OvrAvatarInputControlDelegatedProvider(new BlockiverseXriInputControlDelegate());
        }
    }
}

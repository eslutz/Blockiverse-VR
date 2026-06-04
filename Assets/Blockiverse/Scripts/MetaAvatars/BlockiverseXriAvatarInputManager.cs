using Oculus.Avatar2;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Minimal <see cref="OvrAvatarInputManagerBehavior"/> for the Blockiverse XR rig.
    ///
    /// This project drives head/hand transforms through native XRI (no OVRCameraRig), so
    /// we use the Meta SDK's own OvrPluginTracking delegates for controller pose data -
    /// they read from the same OVR Plugin layer that XRI reads, so they stay in sync.
    /// Body tracking and hand tracking (finger-pose) are not used in this configuration.
    ///
    /// The component must be on the same GameObject as (or reachable from)
    /// <see cref="BlockiverseMetaAvatarEntity"/> so that OvrAvatarEntity can find it
    /// through its <see cref="OvrAvatarInputManagerBehavior"/> search.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class BlockiverseXriAvatarInputManager : OvrAvatarInputManagerBehavior
    {
        OvrAvatarInputTrackingProviderBase trackingProvider;

        public override OvrAvatarInputTrackingProviderBase InputTrackingProvider => trackingProvider;

        // No body-tracking rig in this project.
        public override OvrAvatarBodyTrackingContextBase BodyTrackingContext => null;

        // No dedicated hand-tracking (finger pose) in this project; controller shapes are
        // driven by the animation system based on button states.
        public override OvrAvatarHandTrackingPoseProviderBase HandTrackingProvider => null;

        void Awake()
        {
            // OvrPluginTracking creates a delegate that reads directly from the Quest OVR
            // Plugin, which is the same data source as the XRI TrackedPoseDriver - so the
            // avatar hands and the XRI interaction ray stay in sync without any custom bridge.
            // This only succeeds on a real Quest device (Android runtime); in the Editor it
            // returns null and the avatar falls back to the blob proxy.
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                trackingProvider = OvrPluginTracking.CreateInputTrackingProvider();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BlockiverseXriAvatarInputManager] Failed to create OvrPlugin tracking provider: {ex.Message}. Avatar will use fallback proxy.", this);
                trackingProvider = null;
            }
#endif
        }
    }
}

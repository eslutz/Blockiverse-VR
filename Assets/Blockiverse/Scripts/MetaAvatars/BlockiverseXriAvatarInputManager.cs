using Meta.XR.MultiplayerBlocks.Shared;
using Oculus.Avatar2;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Minimal <see cref="OvrAvatarInputManagerBehavior"/> for the Blockiverse XR rig.
    ///
    /// This project drives head/hand transforms through native XRI (no OVRCameraRig), so
    /// we use Meta's public multiplayer-blocks tracking delegate for controller pose data.
    /// It reads from OVRInput/OVRNodeStateProperties, which are backed by the same Quest
    /// runtime data source as XRI.
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
            // The multiplayer-blocks delegate supports a null OVRCameraRig by reading from
            // OVRInput and OVRNodeStateProperties directly. Keep this Android-only so editor
            // play mode continues to use the lightweight fallback proxy.
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                trackingProvider = new OvrAvatarInputTrackingDelegatedProvider(new InputTrackingDelegate(null));
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BlockiverseXriAvatarInputManager] Failed to create Quest tracking provider: {ex.Message}. Avatar will use fallback proxy.", this);
                trackingProvider = null;
            }
#endif
        }
    }
}

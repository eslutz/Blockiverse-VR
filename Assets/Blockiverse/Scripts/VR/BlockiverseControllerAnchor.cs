using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using Blockiverse.Core;

namespace Blockiverse.VR
{
    /// <summary>
    /// Identifies a controller anchor's hand role. Controller pose is driven natively by the
    /// <see cref="TrackedPoseDriver"/> on the same GameObject (configured by the rig), so this
    /// component only carries the role used by haptics, avatars, and interaction wiring.
    /// </summary>
    public sealed class BlockiverseControllerAnchor : MonoBehaviour
    {
        [SerializeField] BlockiverseControllerRole role;
        [SerializeField] TrackedPoseDriver poseDriver;

        public BlockiverseControllerRole Role => role;

        /// <summary>
        /// Whether the controller currently has a complete tracked pose, read from the native
        /// pose driver's tracking-state input. Rays require both position and rotation because a
        /// rotation-only controller pose leaves the ray origin at a stale position.
        /// </summary>
        public bool IsTracked
        {
            get
            {
                if (poseDriver == null || !poseDriver.enabled)
                    return false;

                InputAction trackingStateAction = poseDriver.trackingStateInput.action;

                if (trackingStateAction == null)
                    return false;

                var trackingState = (InputTrackingState)trackingStateAction.ReadValue<int>();
                const InputTrackingState requiredTracking = InputTrackingState.Position | InputTrackingState.Rotation;
                return (trackingState & requiredTracking) == requiredTracking;
            }
        }

        public void Configure(BlockiverseControllerRole controllerRole, TrackedPoseDriver controllerPoseDriver = null)
        {
            role = controllerRole;
            poseDriver = controllerPoseDriver != null ? controllerPoseDriver : poseDriver != null ? poseDriver : GetComponent<TrackedPoseDriver>();
        }
    }
}

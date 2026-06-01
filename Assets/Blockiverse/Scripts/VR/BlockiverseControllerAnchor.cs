using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

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
        /// Whether the controller is currently tracked, read from the native pose driver's
        /// tracking-state input (position or rotation reported as tracked).
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
                return (trackingState & (InputTrackingState.Position | InputTrackingState.Rotation)) != 0;
            }
        }

        public void Configure(BlockiverseControllerRole controllerRole, TrackedPoseDriver controllerPoseDriver = null)
        {
            role = controllerRole;
            poseDriver = controllerPoseDriver != null ? controllerPoseDriver : poseDriver != null ? poseDriver : GetComponent<TrackedPoseDriver>();
        }
    }
}

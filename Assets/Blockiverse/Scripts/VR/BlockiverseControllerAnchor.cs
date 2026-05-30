using UnityEngine;
using UnityEngine.InputSystem.XR;

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

        public bool IsTracked => poseDriver != null && poseDriver.enabled;

        public void Configure(BlockiverseControllerRole controllerRole, TrackedPoseDriver controllerPoseDriver = null)
        {
            role = controllerRole;
            poseDriver = controllerPoseDriver != null ? controllerPoseDriver : poseDriver != null ? poseDriver : GetComponent<TrackedPoseDriver>();
        }
    }
}

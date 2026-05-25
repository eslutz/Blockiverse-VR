using Unity.XR.CoreUtils;
using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseTeleportLocomotion : MonoBehaviour
    {
        [SerializeField] XROrigin origin;
        [SerializeField] BlockiverseComfortSettings settings;

        public void Configure(XROrigin xrOrigin, BlockiverseComfortSettings comfortSettings)
        {
            origin = xrOrigin;
            settings = comfortSettings;
        }

        public bool TryTeleportTo(Vector3 worldPosition)
        {
            if (origin == null)
                return false;

            if (settings != null && !settings.TeleportEnabled)
                return false;

            return origin.MoveCameraToWorldLocation(worldPosition);
        }
    }
}

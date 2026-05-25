using Unity.XR.CoreUtils;
using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseSnapTurnLocomotion : MonoBehaviour
    {
        const float DefaultSnapTurnDegrees = 45.0f;

        [SerializeField] XROrigin origin;
        [SerializeField] BlockiverseComfortSettings settings;

        public void Configure(XROrigin xrOrigin, BlockiverseComfortSettings comfortSettings)
        {
            origin = xrOrigin;
            settings = comfortSettings;
        }

        public void ApplySnapTurn(int direction)
        {
            if (origin == null || direction == 0)
                return;

            float degrees = settings != null ? settings.SnapTurnDegrees : DefaultSnapTurnDegrees;
            origin.RotateAroundCameraPosition(Vector3.up, Mathf.Sign(direction) * degrees);
        }
    }
}

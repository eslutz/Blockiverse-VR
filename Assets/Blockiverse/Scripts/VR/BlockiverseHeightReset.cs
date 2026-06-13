using Unity.XR.CoreUtils;
using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseHeightReset : MonoBehaviour
    {
        const float DefaultStandingEyeHeight = 1.6f;

        [SerializeField] XROrigin origin;
        [SerializeField] BlockiverseComfortSettings settings;

        public void Configure(XROrigin xrOrigin, BlockiverseComfortSettings comfortSettings)
        {
            origin = xrOrigin;
            settings = comfortSettings;
        }

        public void ResetHeight()
        {
            if (origin == null)
                return;

            ApplyStandingEyeHeight(settings != null
                ? settings.StandingEyeHeight
                : DefaultStandingEyeHeight);
        }

        public void ApplyStandingEyeHeight(float standingEyeHeight)
        {
            if (origin == null)
                return;

            origin.CameraYOffset = standingEyeHeight;

            if (origin.RequestedTrackingOriginMode != XROrigin.TrackingOriginMode.Floor)
                return;

            Transform cameraOffset = origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform
                : null;
            if (cameraOffset == null)
                return;

            float trackedEyeHeight = origin.Camera != null
                ? origin.Camera.transform.localPosition.y
                : 0.0f;
            Vector3 offset = cameraOffset.localPosition;
            offset.y = standingEyeHeight - trackedEyeHeight;
            cameraOffset.localPosition = offset;
        }
    }
}

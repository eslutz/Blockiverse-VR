using Unity.XR.CoreUtils;
using UnityEngine;
using Blockiverse.Core;

namespace Blockiverse.VR
{
    public sealed class BlockiverseHeightReset : MonoBehaviour, IBlockiverseHeightReset
    {
        const float DefaultStandingEyeHeight = BlockiverseComfortSettings.FixedStandingEyeHeight;

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

            // Real-height mode intentionally keeps the player's own tracked height: normalizing
            // the view to a standard eye height would defeat the point of the setting. Reset
            // then just clears any accumulated offset so the raw tracked height drives the view.
            if (settings != null && settings.RealPlayerHeightEnabled)
            {
                ClearCameraOffset();
                return;
            }

            ApplyStandingEyeHeight(DefaultStandingEyeHeight);
        }

        void ClearCameraOffset()
        {
            if (origin.RequestedTrackingOriginMode != XROrigin.TrackingOriginMode.Floor)
                return;

            Transform cameraOffset = origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform
                : null;
            if (cameraOffset == null)
                return;

            Vector3 offset = cameraOffset.localPosition;
            offset.y = 0.0f;
            cameraOffset.localPosition = offset;
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

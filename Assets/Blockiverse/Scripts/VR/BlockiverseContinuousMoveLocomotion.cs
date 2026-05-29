using Unity.XR.CoreUtils;
using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseContinuousMoveLocomotion : MonoBehaviour
    {
        const float StickDeadzone = 0.12f;

        [SerializeField] XROrigin origin;
        [SerializeField] BlockiverseComfortSettings settings;

        public void Configure(XROrigin xrOrigin, BlockiverseComfortSettings comfortSettings)
        {
            origin = xrOrigin;
            settings = comfortSettings;
        }

        public bool TryMove(Vector2 input, float deltaTime)
        {
            if (origin == null || settings == null || !settings.ContinuousMoveEnabled)
                return false;

            if (input.sqrMagnitude < StickDeadzone * StickDeadzone || deltaTime <= 0.0f)
                return false;

            Vector2 clampedInput = Vector2.ClampMagnitude(input, 1.0f);
            Vector3 move = GetCameraRelativeMove(clampedInput) * settings.ContinuousMoveSpeed * deltaTime;

            if (move.sqrMagnitude <= Mathf.Epsilon)
                return false;

            origin.transform.position += move;
            return true;
        }

        Vector3 GetCameraRelativeMove(Vector2 input)
        {
            Transform cameraTransform = origin.Camera != null
                ? origin.Camera.transform
                : origin.transform;

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up);

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;

            if (right.sqrMagnitude <= Mathf.Epsilon)
                right = Vector3.right;

            return (forward.normalized * input.y) + (right.normalized * input.x);
        }
    }
}

using Unity.XR.CoreUtils;
using UnityEngine;
using Blockiverse.Core;

namespace Blockiverse.VR
{
    /// <summary>
    /// Implements continuous locomotion vertical head-bobbing.
    /// When GlideStyle is set to Bobbing and locomotion is Glide (and not flying),
    /// a subtle vertical camera offset is applied to the Camera Offset object
    /// based on player movement velocity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseGlideBobController : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] XROrigin xrOrigin;
        [SerializeField] float frequency = 8.0f;
        [SerializeField] float amplitude = 0.015f;
        [SerializeField] float decaySpeed = 0.1f;

        float bobCycle;
        float lastAppliedBobY;
        float? lastExpectedLocalPosY;

        public System.Func<float> SpeedOverride { get; set; }

        public float Frequency
        {
            get => frequency;
            set => frequency = value;
        }

        public float Amplitude
        {
            get => amplitude;
            set => amplitude = value;
        }

        void Awake()
        {
            if (inputRig == null)
                inputRig = GetComponent<BlockiverseInputRig>();
            if (comfortSettings == null)
                comfortSettings = GetComponent<BlockiverseComfortSettings>();
            if (xrOrigin == null)
                xrOrigin = GetComponent<XROrigin>();
        }

        // Frame delta used to advance the bob phase and decay. Falls back to a fixed step when
        // Time.deltaTime is unavailable (EditMode tests invoking LateUpdate directly, or a paused
        // timescale) so the oscillation remains deterministic and continues to settle to zero.
        static float EffectiveDeltaTime()
        {
            float dt = Time.deltaTime;
            return dt > 0f ? dt : 1f / 60f;
        }

        void LateUpdate()
        {
            if (xrOrigin == null || xrOrigin.CameraFloorOffsetObject == null)
                return;

            float deltaTime = EffectiveDeltaTime();
            Transform cameraOffset = xrOrigin.CameraFloorOffsetObject.transform;
            Vector3 localPos = cameraOffset.localPosition;

            // Detect external height modification (e.g. height reset / eye height slider)
            if (lastExpectedLocalPosY.HasValue && !Mathf.Approximately(localPos.y, lastExpectedLocalPosY.Value))
            {
                // External script modified localPosition.y. Reset our applied offset so we
                // treat the new position as the clean base height.
                lastAppliedBobY = 0f;
            }
            else
            {
                localPos.y -= lastAppliedBobY;
            }

            // Calculate target bob offset
            float targetBobY = 0f;

            bool isBobbingEnabled = comfortSettings != null &&
                                    comfortSettings.LocomotionMode == BlockiverseLocomotionMode.Glide &&
                                    comfortSettings.GlideStyle == GlideStyle.Bobbing;

            bool isFlying = inputRig != null && inputRig.CreativeFlightLocomotionActive;

            if (isBobbingEnabled && !isFlying)
            {
                float speed = 0f;
                bool hasSpeed = false;

                if (SpeedOverride != null)
                {
                    speed = SpeedOverride();
                    hasSpeed = true;
                }
                else if (inputRig != null && inputRig.CharacterController != null)
                {
                    Vector3 velocity = inputRig.CharacterController.velocity;
                    speed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
                    hasSpeed = true;
                }

                if (hasSpeed && speed > 0.1f)
                {
                    bobCycle = (bobCycle + speed * frequency * deltaTime) % (Mathf.PI * 2f);
                    targetBobY = Mathf.Sin(bobCycle) * speed * amplitude;
                }
            }

            // Smoothly decay to the target bob offset when stationary or disabled
            float newBobY;
            if (targetBobY == 0f)
            {
                newBobY = Mathf.MoveTowards(lastAppliedBobY, 0f, decaySpeed * deltaTime);
                if (Mathf.Approximately(newBobY, 0f))
                {
                    newBobY = 0f;
                    bobCycle = 0f;
                }
            }
            else
            {
                newBobY = targetBobY;
            }

            // Apply the new bob offset
            localPos.y += newBobY;
            cameraOffset.localPosition = localPos;
            lastAppliedBobY = newBobY;
            lastExpectedLocalPosY = localPos.y;
        }
    }
}

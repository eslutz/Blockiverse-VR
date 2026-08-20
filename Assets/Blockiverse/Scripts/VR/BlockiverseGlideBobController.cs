using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using Blockiverse.Core;
using Blockiverse.Gameplay;

namespace Blockiverse.VR
{
    /// <summary>
    /// Draws the continuous-locomotion walk bob as a vertical offset on the XR Origin's camera
    /// offset. When GlideStyle is Bobbing and locomotion is Glide (and not flying), the shared
    /// <see cref="BlockiverseGaitCycle"/> phase is shaped into a subtle vertical oscillation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseGlideBobController : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] XROrigin xrOrigin;
        [SerializeField] BlockiverseGaitCycle gaitCycle;
        [SerializeField] float amplitude = 0.015f;
        [SerializeField] float speedFollowRate = 6.0f;

        static readonly List<XRInputSubsystem> ScratchInputSubsystems = new();

        readonly List<XRInputSubsystem> subscribedInputSubsystems = new();
        float lastAppliedBobY;
        float followedSpeed;
        int subsystemRescanCountdown;

        public float Amplitude
        {
            get => amplitude;
            set => amplitude = value;
        }

        public BlockiverseGaitCycle GaitCycle => gaitCycle;

        void OnEnable()
        {
            SubscribeToTrackingOriginUpdates();
        }

        void OnDisable()
        {
            foreach (XRInputSubsystem subsystem in subscribedInputSubsystems)
            {
                if (subsystem != null)
                    subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            }

            subscribedInputSubsystems.Clear();
        }

        // A recenter (Meta-button long press) or boundary recalibration makes XROrigin rewrite the
        // camera offset's Y absolutely, silently discarding any bob folded into it. Forgetting our
        // applied offset on that signal keeps the next frame from subtracting a bob that the new
        // base height no longer contains. Subsystems can start after OnEnable, so the scan retries
        // on a slow cadence from LateUpdate until one is found.
        void SubscribeToTrackingOriginUpdates()
        {
            SubsystemManager.GetSubsystems(ScratchInputSubsystems);
            foreach (XRInputSubsystem subsystem in ScratchInputSubsystems)
            {
                if (subscribedInputSubsystems.Contains(subsystem))
                    continue;

                subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                subscribedInputSubsystems.Add(subsystem);
            }
        }

        void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            ClearAppliedOffset();
        }

        void Awake()
        {
            if (inputRig == null)
                inputRig = GetComponent<BlockiverseInputRig>();
            if (comfortSettings == null)
                comfortSettings = GetComponent<BlockiverseComfortSettings>();
            if (xrOrigin == null)
                xrOrigin = GetComponent<XROrigin>();
            if (gaitCycle == null)
                gaitCycle = GetComponent<BlockiverseGaitCycle>();
        }

        // Frame delta used to advance the amplitude follower. Falls back to a fixed step when
        // Time.deltaTime is unavailable (EditMode tests invoking LateUpdate directly, or a paused
        // timescale) so the bob still settles to zero.
        static float EffectiveDeltaTime()
        {
            float dt = Time.deltaTime;
            return dt > 0f ? dt : 1f / 60f;
        }

        void LateUpdate()
        {
            if (xrOrigin == null || xrOrigin.CameraFloorOffsetObject == null)
                return;

            if (--subsystemRescanCountdown <= 0)
            {
                subsystemRescanCountdown = 60;
                SubscribeToTrackingOriginUpdates();
            }

            float deltaTime = EffectiveDeltaTime();
            Transform cameraOffset = xrOrigin.CameraFloorOffsetObject.transform;
            Vector3 localPos = cameraOffset.localPosition;

            // Strip last frame's bob to recover the base height. Other systems (crouch easing, the
            // height reset, the eye-height slider) also write this Y, but they all write a delta on
            // top of whatever is there, so subtracting our own offset recovers the base whether or
            // not they touched it. Absolute writes call ClearAppliedOffset instead.
            localPos.y -= lastAppliedBobY;

            ResolveGaitCycle();

            bool bobbingEnabled = comfortSettings != null &&
                                  comfortSettings.LocomotionMode == BlockiverseLocomotionMode.Glide &&
                                  comfortSettings.GlideStyle == GlideStyle.Bobbing;
            bool flying = inputRig != null && inputRig.CreativeFlightLocomotionActive;
            bool stepping = bobbingEnabled &&
                            !flying &&
                            BlockiverseRuntimeState.AllowWorldInput &&
                            gaitCycle != null &&
                            gaitCycle.IsStepping;

            // Follow the gait speed instead of reading it raw. Raw speed drops to zero the frame the
            // stick is released, which would snap the offset to level from wherever the curve was;
            // ramping the amplitude eases the bob in and out while leaving its shape untouched, and
            // doubles as a low-pass over the per-frame jitter in a measured speed.
            float targetSpeed = stepping && gaitCycle != null ? gaitCycle.Speed : 0.0f;
            followedSpeed = Mathf.MoveTowards(followedSpeed, targetSpeed, speedFollowRate * deltaTime);

            float newBobY = 0.0f;
            if (followedSpeed > 0.0f && gaitCycle != null)
            {
                // -cos puts the trough exactly on phase 0, which is the point the gait cycle raises
                // its footfall ahead of, so the step lands as the view drops into the low point.
                float shape = -Mathf.Cos(2f * Mathf.PI * gaitCycle.BobPhase01);
                newBobY = shape * followedSpeed * amplitude;
            }

            localPos.y += newBobY;
            cameraOffset.localPosition = localPos;
            lastAppliedBobY = newBobY;
        }

        /// <summary>
        /// Forgets the currently applied bob offset without touching the transform. Call this right
        /// after writing an absolute camera-offset height (height reset / eye-height slider) so the
        /// next frame does not subtract a bob that is no longer part of the new base height.
        /// </summary>
        public void ClearAppliedOffset()
        {
            lastAppliedBobY = 0.0f;
            followedSpeed = 0.0f;
        }

        void ResolveGaitCycle()
        {
            if (gaitCycle != null)
                return;

            gaitCycle = GetComponent<BlockiverseGaitCycle>();

            // Rigs generated before the gait cycle existed still need one; the bootstrapper puts it
            // on the prefab, this only covers a stale prefab at runtime.
            if (gaitCycle == null && Application.isPlaying)
            {
                gaitCycle = gameObject.AddComponent<BlockiverseGaitCycle>();

                if (inputRig != null)
                    gaitCycle.Configure(inputRig.CharacterController);
            }
        }
    }
}

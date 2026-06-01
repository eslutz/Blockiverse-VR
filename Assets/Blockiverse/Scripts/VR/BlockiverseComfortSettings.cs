using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseComfortSettings : MonoBehaviour
    {
        const float MinSnapTurnDegrees = 15.0f;
        const float MaxSnapTurnDegrees = 90.0f;
        const float MinStandingEyeHeight = 1.0f;
        const float MaxStandingEyeHeight = 2.2f;
        const float MinContinuousMoveSpeed = 0.5f;
        const float MaxContinuousMoveSpeed = 4.0f;
        const float MinVignetteStrength = 0.0f;
        const float MaxVignetteStrength = 1.0f;

        // Glide is the default: walking/jumping and block climbing are core Blockiverse verbs.
        [SerializeField] BlockiverseLocomotionMode locomotionMode = BlockiverseLocomotionMode.Glide;
        [SerializeField] float continuousMoveSpeed = 1.8f;
        [SerializeField] bool smoothTurnEnabled;
        [SerializeField] float snapTurnDegrees = 45.0f;
        [SerializeField] float standingEyeHeight = 1.6f;
        [SerializeField] bool vignetteEnabled = true;
        // Normalized 0–1: 1 = widest aperture (subtle), 0 = fully closed (strong).
        [SerializeField] float vignetteStrength = 1.0f;

        public BlockiverseLocomotionMode LocomotionMode
        {
            get => locomotionMode;
            set => locomotionMode = value;
        }

        // Compatibility wrappers used by existing code that predates LocomotionMode.
        public bool TeleportEnabled => locomotionMode == BlockiverseLocomotionMode.Teleport;
        public bool ContinuousMoveEnabled => locomotionMode == BlockiverseLocomotionMode.Glide;

        public float ContinuousMoveSpeed
        {
            get => continuousMoveSpeed;
            set => continuousMoveSpeed = Mathf.Clamp(value, MinContinuousMoveSpeed, MaxContinuousMoveSpeed);
        }

        public bool SmoothTurnEnabled
        {
            get => smoothTurnEnabled;
            set => smoothTurnEnabled = value;
        }

        public float SnapTurnDegrees
        {
            get => snapTurnDegrees;
            set => snapTurnDegrees = Mathf.Clamp(value, MinSnapTurnDegrees, MaxSnapTurnDegrees);
        }

        public float StandingEyeHeight
        {
            get => standingEyeHeight;
            set => standingEyeHeight = Mathf.Clamp(value, MinStandingEyeHeight, MaxStandingEyeHeight);
        }

        public bool VignetteEnabled
        {
            get => vignetteEnabled;
            set => vignetteEnabled = value;
        }

        /// <summary>
        /// Normalized vignette strength 0–1. Maps to <c>VignetteParameters.apertureSize</c> as
        /// <c>0.6f + strength * 0.4f</c> (0 = 0.6 aperture / strong; 1 = 1.0 / off).
        /// </summary>
        public float VignetteStrength
        {
            get => vignetteStrength;
            set => vignetteStrength = Mathf.Clamp(value, MinVignetteStrength, MaxVignetteStrength);
        }

        /// <summary>Aperture value for TunnelingVignetteController (0.6–1.0).</summary>
        public float VignetteAperture => vignetteEnabled ? 0.6f + vignetteStrength * 0.4f : 1.0f;

        void OnValidate()
        {
            ContinuousMoveSpeed = continuousMoveSpeed;
            SnapTurnDegrees = snapTurnDegrees;
            StandingEyeHeight = standingEyeHeight;
            VignetteStrength = vignetteStrength;
        }
    }
}

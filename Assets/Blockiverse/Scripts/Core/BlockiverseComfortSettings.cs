using UnityEngine;

namespace Blockiverse.Core
{
    public sealed class BlockiverseComfortSettings : MonoBehaviour
    {
        const float MinSnapTurnDegrees = 15.0f;
        const float MaxSnapTurnDegrees = 90.0f;
        const float MinStandingEyeHeight = 1.0f;
        const float MaxStandingEyeHeight = 2.2f;
        const float MinContinuousMoveSpeed = 0.5f;
        const float MaxContinuousMoveSpeed = 4.0f;
        const float MinContinuousTurnSpeed = 30.0f;
        const float MaxContinuousTurnSpeed = 180.0f;
        const float MinUiScale = 0.85f;
        const float MaxUiScale = 1.35f;
        const float MinVignetteStrength = 0.0f;
        const float MaxVignetteStrength = 1.0f;

        // Glide is the default: walking/jumping and block climbing are core Blockiverse verbs.
        [SerializeField] BlockiverseLocomotionMode locomotionMode = BlockiverseLocomotionMode.Glide;
        // Bobbing is the default: provides rhythmic vertical feedback during locomotion.
        [SerializeField] GlideStyle glideStyle = GlideStyle.Bobbing;
        [SerializeField] float continuousMoveSpeed = 1.8f;
        [SerializeField] bool smoothTurnEnabled;
        [SerializeField] float continuousTurnSpeed = 60.0f;
        [SerializeField] float snapTurnDegrees = 45.0f;
        [SerializeField] bool snapTurnAroundEnabled = true;
        [SerializeField] float standingEyeHeight = 1.6f;
        [SerializeField] float uiScale = 1.0f;
        // Comfort-first baseline: the motion tunneling vignette only renders during locomotion, so
        // shipping it on at a low strength reduces nausea without obscuring a static title/menu.
        [SerializeField] bool vignetteEnabled = true;
        // Normalized 0–1: 1 = strongest vignette (narrowest aperture), 0 = open. 0.5 is a medium,
        // safe default for first-time VR users.
        [SerializeField] float vignetteStrength = 0.5f;
        [SerializeField] BlockiverseControllerRole dominantHand = BlockiverseControllerRole.Right;
        [SerializeField] bool toggleToMineEnabled;

        public BlockiverseLocomotionMode LocomotionMode
        {
            get => locomotionMode;
            set => locomotionMode = value;
        }

        public GlideStyle GlideStyle
        {
            get => glideStyle;
            set => glideStyle = value;
        }

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

        public float ContinuousTurnSpeed
        {
            get => continuousTurnSpeed;
            set => continuousTurnSpeed = Mathf.Clamp(value, MinContinuousTurnSpeed, MaxContinuousTurnSpeed);
        }

        public float SnapTurnDegrees
        {
            get => snapTurnDegrees;
            set => snapTurnDegrees = Mathf.Clamp(value, MinSnapTurnDegrees, MaxSnapTurnDegrees);
        }

        public bool SnapTurnAroundEnabled
        {
            get => snapTurnAroundEnabled;
            set => snapTurnAroundEnabled = value;
        }

        public float StandingEyeHeight
        {
            get => standingEyeHeight;
            set => standingEyeHeight = Mathf.Clamp(value, MinStandingEyeHeight, MaxStandingEyeHeight);
        }

        public float UiScale
        {
            get => uiScale;
            set => uiScale = Mathf.Clamp(value, MinUiScale, MaxUiScale);
        }

        public bool VignetteEnabled
        {
            get => vignetteEnabled;
            set => vignetteEnabled = value;
        }

        public BlockiverseControllerRole DominantHand
        {
            get => dominantHand;
            set => dominantHand = value;
        }

        public bool ToggleToMineEnabled
        {
            get => toggleToMineEnabled;
            set => toggleToMineEnabled = value;
        }

        /// <summary>
        /// Normalized vignette strength 0–1. Maps to <c>VignetteParameters.apertureSize</c> as
        /// <c>1.0f - strength * 0.4f</c> (0 = 1.0 aperture / off; 1 = 0.6 / strong).
        /// </summary>
        public float VignetteStrength
        {
            get => vignetteStrength;
            set => vignetteStrength = Mathf.Clamp(value, MinVignetteStrength, MaxVignetteStrength);
        }

        /// <summary>Aperture value for TunnelingVignetteController (0.6–1.0).</summary>
        public float VignetteAperture => vignetteEnabled ? 1.0f - vignetteStrength * 0.4f : 1.0f;

        void OnValidate()
        {
            ContinuousMoveSpeed = continuousMoveSpeed;
            ContinuousTurnSpeed = continuousTurnSpeed;
            SnapTurnDegrees = snapTurnDegrees;
            StandingEyeHeight = standingEyeHeight;
            UiScale = uiScale;
            VignetteStrength = vignetteStrength;
        }
    }
}

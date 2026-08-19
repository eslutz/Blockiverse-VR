using UnityEngine;

namespace Blockiverse.Core
{
    public sealed class BlockiverseComfortSettings : MonoBehaviour
    {
        const float MinSnapTurnDegrees = 15.0f;
        const float MaxSnapTurnDegrees = 90.0f;
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
        // Smooth is the default: first-time VR users should start with stable head-relative motion.
        [SerializeField] GlideStyle glideStyle = GlideStyle.Smooth;
        [SerializeField] float continuousMoveSpeed = 1.8f;
        [SerializeField] bool smoothTurnEnabled;
        [SerializeField] float continuousTurnSpeed = 60.0f;
        [SerializeField] float snapTurnDegrees = 45.0f;
        [SerializeField] bool snapTurnAroundEnabled = true;
        [SerializeField] float uiScale = 1.0f;
        // Comfort-first baseline: the motion tunneling vignette only renders during locomotion, so
        // shipping it on at a low strength reduces nausea without obscuring a static title/menu.
        [SerializeField] bool vignetteEnabled = true;
        // Normalized 0–1: 1 = strongest vignette (narrowest aperture), 0 = open. 0.3 keeps
        // the default comfort aid present without making the aperture feel heavy.
        [SerializeField] float vignetteStrength = 0.3f;
        [SerializeField] BlockiverseControllerRole dominantHand = BlockiverseControllerRole.Right;
        [SerializeField] bool toggleToMineEnabled;
        // Off by default: every player is the same size in the world, so the same gaps fit
        // everyone and level design is predictable. Turning it on makes the player's collision
        // (and view height) follow their real tracked height, so tall players must duck or
        // crouch to fit where shorter players walk through.
        [SerializeField] bool realPlayerHeightEnabled;

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

        /// <summary>
        /// Eye height used by the fixed-size player model. This is a game constant, not a user
        /// setting: players choose between the fixed size and their real height
        /// (<see cref="RealPlayerHeightEnabled"/>) rather than dialing in an eye height, which
        /// previously let the view drift away from the collision capsule.
        /// </summary>
        public const float FixedStandingEyeHeight = 1.6f;

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
        /// When false (default) the player has a fixed in-game size: two blocks standing, one
        /// crouched, identical for everyone. When true, collision and view height follow the
        /// player's real tracked height.
        /// </summary>
        public bool RealPlayerHeightEnabled
        {
            get => realPlayerHeightEnabled;
            set => realPlayerHeightEnabled = value;
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
            UiScale = uiScale;
            VignetteStrength = vignetteStrength;
        }
    }
}

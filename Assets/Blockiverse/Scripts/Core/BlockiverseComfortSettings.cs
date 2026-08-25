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
        const float MinSwimSpeedFactor = 0.30f;
        const float MaxSwimSpeedFactor = 1.00f;

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
        // On by default, and this is the one place the game deliberately defaults AWAY from the
        // gentler option: a player who is not actively swimming sinks. Water should read as
        // something you work against rather than a floor you bob on. Turning this off restores
        // exact neutral buoyancy -- with no input the app moves the player vertically by zero -- so
        // loading a save submerged, respawning underwater, or a fluid flowing into your cell
        // produce no unrequested motion at all. That is why it sits in Comfort beside the vection
        // controls rather than in gameplay options.
        [SerializeField] bool swimPassiveSinkEnabled = true;
        // Horizontal swim speed as a fraction of walking. Slower than walking because swimming is
        // meant to feel like moving through water, not walking underwater.
        [SerializeField] float swimSpeedFactor = 0.55f;
        // Engages the tunneling vignette during passive descent exactly as it does for driven
        // vertical motion, so the one motion the player did not ask for gets the same aid.
        [SerializeField] bool swimVignetteBoost = true;

        // Defaults ON: without it a swimmer who reaches a low bank is pulled back into the water
        // by gravity resuming before their feet clear the lip, which reads as the shore being
        // broken. The switch exists because it is still an automatic vertical translation.
        [SerializeField] bool swimClimbOutEnabled = true;
        [SerializeField] bool sprintToggleEnabled;
        [SerializeField] bool placeModifierToggleEnabled;
        [SerializeField] bool crouchToggleEnabled;

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
        public const float FixedStandingEyeHeight = 1.7f;

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
        /// When false (default) sprint is click-and-hold: it is active only while the support
        /// stick click is held. When true the click toggles sprint on and off. Set independently
        /// of <see cref="CrouchToggleEnabled"/> so each can use the style that suits the player.
        /// </summary>
        public bool SprintToggleEnabled
        {
            get => sprintToggleEnabled;
            set => sprintToggleEnabled = value;
        }

        /// <summary>
        /// How the grip switches the trigger between breaking and placing. False (default) means
        /// HOLD grip to place and release to return to breaking; true means the grip press TOGGLES
        /// between the two.
        ///
        /// Same hold-versus-toggle shape as <see cref="SprintToggleEnabled"/> and
        /// <see cref="CrouchToggleEnabled"/>, resolved through the same
        /// BlockiverseInputRig.ResolveModifierActive helper so all three behave identically.
        /// </summary>
        public bool PlaceModifierToggleEnabled
        {
            get => placeModifierToggleEnabled;
            set => placeModifierToggleEnabled = value;
        }

        /// <summary>
        /// When false (default) crouch is click-and-hold: it is active only while the dominant
        /// stick click is held. When true the click toggles crouch on and off.
        /// </summary>
        public bool CrouchToggleEnabled
        {
            get => crouchToggleEnabled;
            set => crouchToggleEnabled = value;
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

        /// <summary>
        /// When true (default) the player sinks whenever they are not actively swimming. Turning it
        /// off restores exact neutral buoyancy, the comfort accommodation for unrequested vertical
        /// motion.
        /// </summary>
        public bool SwimPassiveSinkEnabled
        {
            get => swimPassiveSinkEnabled;
            set => swimPassiveSinkEnabled = value;
        }

        /// <summary>Horizontal swim speed as a fraction of the walking speed (0.30–1.00).</summary>
        public float SwimSpeedFactor
        {
            get => swimSpeedFactor;
            set => swimSpeedFactor = Mathf.Clamp(value, MinSwimSpeedFactor, MaxSwimSpeedFactor);
        }

        /// <summary>
        /// When true (default) the tunneling vignette engages during passive descent, not only
        /// during driven motion.
        /// </summary>
        public bool SwimClimbOutEnabled
        {
            get => swimClimbOutEnabled;
            set => swimClimbOutEnabled = value;
        }

        public bool SwimVignetteBoost
        {
            get => swimVignetteBoost;
            set => swimVignetteBoost = value;
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
            SwimSpeedFactor = swimSpeedFactor;
        }
    }
}

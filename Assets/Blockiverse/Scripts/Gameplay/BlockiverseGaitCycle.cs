using System;
using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// The single owner of the player's walk cycle. Consumers read <see cref="BobPhase01"/> for the
    /// position within the current step and subscribe to <see cref="Footfall"/> for step events.
    /// </summary>
    /// <remarks>
    /// Phase advances with actual horizontal travel rather than with elapsed time, so the walk bob
    /// and the footstep cues stay locked to each other at any move speed — including sprint — and
    /// both stop when the player walks into a wall instead of running against a stationary rig.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BlockiverseGaitCycle : MonoBehaviour
    {
        // One step of travel per bob cycle. Roughly a human step length, and short enough that the
        // cadence reads as walking rather than as a stride pair.
        public const float DefaultStepLengthMeters = 0.79f;
        // The head bottoms out slightly after heel strike, because weight acceptance takes about a
        // tenth of the gait cycle. Firing the cue that fraction of a step early reads as a real
        // step; landing it exactly on the low point reads as slightly late.
        public const float DefaultFootfallLeadPhase = 0.1f;
        // Constant step length means cadence scales linearly with speed, and the max move-speed
        // slider with sprint reaches 8.8 m/s — an 11 Hz cadence that would machine-gun the cue.
        // Crossings past this rate are swallowed (audio ruleset: never faster than 0.18s per step).
        public const float MinFootfallIntervalSeconds = 0.18f;
        // A frame that covers more ground than this is a teleport or a respawn, not a stride.
        public const float TeleportResetMeters = 2.0f;
        // A frame whose instantaneous speed exceeds this is a displacement, not locomotion: the
        // fastest legitimate gait is the max move-speed slider under sprint (8.8 m/s). Snap turns
        // rotate the XR origin around the camera, so a player standing physically offset from
        // play-space centre translates the rig by up to ~1.5 m in a single frame — under the
        // teleport distance guard but far above any walking speed. Short teleports land here too.
        public const float MaxStrideSpeedMetersPerSecond = 10.0f;
        // Below this the player is drifting or being nudged rather than walking.
        public const float MinStepSpeed = 0.1f;
        // Move-stick magnitude below this is not walking intent (matches the input deadzone).
        public const float MoveIntentDeadzone = 0.1f;
        // Mid-step, so the very first footfall after spawn is roughly half a step of travel out.
        const float StartPhase = 0.5f;
        const float MinStepLengthMeters = 0.05f;
        const float MaxFootfallLeadPhase = 0.45f;

        [SerializeField] CharacterController characterController;
        [SerializeField] float stepLengthMeters = DefaultStepLengthMeters;
        [SerializeField, Range(0f, MaxFootfallLeadPhase)] float footfallLeadPhase = DefaultFootfallLeadPhase;

        readonly BlockiverseGroundedProbe groundedProbe = new();
        Vector3 lastPosition;
        float stepPhase = StartPhase;
        float footfallIndex;
        float secondsSinceFootfall = float.PositiveInfinity;
        float speed;
        bool grounded;
        bool stepping;
        bool trackingPosition;
        bool seeded;

        /// <summary>Raised once per step, <see cref="FootfallLeadPhase"/> ahead of the bob trough.</summary>
        public event Action Footfall;

        public Func<bool> GroundedOverride { get; set; }

        /// <summary>
        /// Move-stick magnitude source, wired by the input rig at runtime. Smooth turns also rotate
        /// the origin around the camera, translating the rig at plausible walking speeds while the
        /// player only turns in place — locomotion the speed guard cannot distinguish from a
        /// stride. Requiring stick intent filters every origin motion the player did not ask for
        /// (turns, recenters, external corrections). Null means no source: travel alone decides.
        /// </summary>
        public Func<float> MoveIntentOverride { get; set; }

        /// <summary>
        /// Set by locomotion modes that move the rig without walking (creative flight). While true
        /// the cycle holds and no footfalls fire, even when the rig skims within grounding range.
        /// </summary>
        public bool ExternallySuppressed { get; set; }

        /// <summary>
        /// True while the cycle must not advance. External suppression only -- creative flight.
        /// </summary>
        /// <remarks>
        /// This deliberately does NOT gate on <c>BlockiverseRuntimeState.AllowWorldInput</c>. That
        /// flag means "a menu holds input focus", which is the entire title/mini-world state, so
        /// gating on it killed the walk bob and footsteps everywhere the player can walk but not
        /// build. The menus ruleset already says menus never suppress locomotion and only block
        /// editing stays gated (voxel_survival_menus.md), and sprint and crouch were fixed for the
        /// same reason -- the gait cycle was simply missed by that pass.
        ///
        /// Nothing here needs the gate as a safety net: hasMoveIntent already requires real move
        /// intent, and the displacement guard rejects any single-frame jump faster than a stride,
        /// which is what keeps scripted rig moves and teleports from counting as walking.
        /// </remarks>
        public bool IsSuppressed => ExternallySuppressed;

        /// <summary>Position within the current step. Zero sits on the walk bob's low point.</summary>
        public float BobPhase01 => stepPhase - Mathf.Floor(stepPhase);

        /// <summary>Measured horizontal speed, in metres per second.</summary>
        public float Speed => speed;

        public bool IsGrounded => grounded;

        /// <summary>True while the cycle is advancing: grounded, unsuppressed, and covering ground.</summary>
        public bool IsStepping => stepping;

        public float StepLengthMeters
        {
            get => stepLengthMeters;
            set => stepLengthMeters = Mathf.Max(MinStepLengthMeters, value);
        }

        public float FootfallLeadPhase
        {
            get => footfallLeadPhase;
            set => footfallLeadPhase = Mathf.Clamp(value, 0f, MaxFootfallLeadPhase);
        }

        public void Configure(CharacterController controller)
        {
            characterController = controller;
            groundedProbe.Configure(controller);
        }

        void OnEnable()
        {
            ResolveReferences();
            trackingPosition = false;
            seeded = false;
        }

        void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>
        /// Advances the cycle from the rig's horizontal travel since the previous call. Driven from
        /// <c>Update</c> at runtime; called directly by tests so the cycle can be stepped in EditMode.
        /// </summary>
        public void Advance(float deltaTime)
        {
            ResolveReferences();

            Vector3 position = transform.position;
            if (!trackingPosition)
            {
                lastPosition = position;
                trackingPosition = true;
            }

            Vector3 delta = position - lastPosition;
            lastPosition = position;

            grounded = ResolveGrounded();
            secondsSinceFootfall += deltaTime;

            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            float rawSpeed = deltaTime > 0f ? horizontal / deltaTime : 0f;
            bool displaced = horizontal > TeleportResetMeters || rawSpeed > MaxStrideSpeedMetersPerSecond;
            bool hasMoveIntent = MoveIntentOverride == null || MoveIntentOverride() > MoveIntentDeadzone;

            speed = displaced ? 0f : rawSpeed;
            stepping = grounded && !displaced && !IsSuppressed && hasMoveIntent && speed > MinStepSpeed;

            if (!seeded)
            {
                seeded = true;
                ReseedFootfallIndex();
            }

            if (!stepping)
            {
                // The phase itself is never snapped: the bob reads it every frame, and a jump in
                // phase is a jump in the camera. The frozen phase is what prevents a burst of stale
                // cues when stepping resumes — travel while not stepping never advances it. The
                // re-seed below is defence in depth: it pins the footfall index back to the phase
                // in case a future change lets the two drift across a break. Merely stopping keeps
                // both, so a tapped stick accumulates toward the next footfall.
                if (!grounded || displaced || IsSuppressed)
                    ReseedFootfallIndex();

                return;
            }

            stepPhase += horizontal / Mathf.Max(MinStepLengthMeters, stepLengthMeters);

            float index = Mathf.Floor(stepPhase + footfallLeadPhase);
            if (index > footfallIndex)
            {
                footfallIndex = index;

                // The rate ceiling swallows crossings rather than deferring them: at the highest
                // sprint speeds cues thin out instead of queueing into a burst.
                if (secondsSinceFootfall >= MinFootfallIntervalSeconds)
                {
                    secondsSinceFootfall = 0f;
                    Footfall?.Invoke();
                }
            }
            else if (index < footfallIndex)
            {
                // The lead was tuned down at runtime. Re-seed rather than fire a spurious cue.
                footfallIndex = index;
            }

            // float32 loses step-level precision once the phase reaches the hundreds of thousands
            // (a many-hour walking session). The bob only reads the fractional part and the index
            // only tracks crossings, so both can be rebased together without a glitch.
            float whole = Mathf.Floor(stepPhase);
            if (whole >= 1024f)
            {
                stepPhase -= whole;
                footfallIndex -= whole;
            }
        }

        void ReseedFootfallIndex()
        {
            footfallIndex = Mathf.Floor(stepPhase + footfallLeadPhase);
        }

        bool ResolveGrounded()
        {
            if (GroundedOverride != null)
                return GroundedOverride();

            groundedProbe.Configure(characterController);
            return groundedProbe.IsGrounded;
        }

        void ResolveReferences()
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>() ?? GetComponentInParent<CharacterController>();
        }
    }
}

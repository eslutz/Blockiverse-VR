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
        // Below this the player is drifting or being nudged rather than walking.
        public const float MinStepSpeed = 0.1f;
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
        /// Set by locomotion modes that move the rig without walking (creative flight). While true
        /// the cycle holds and no footfalls fire, even when the rig skims within grounding range.
        /// </summary>
        public bool ExternallySuppressed { get; set; }

        /// <summary>True while the cycle must not advance: external suppression or blocked world input.</summary>
        public bool IsSuppressed => ExternallySuppressed || !BlockiverseRuntimeState.AllowWorldInput;

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
            bool teleported = horizontal > TeleportResetMeters;

            speed = !teleported && deltaTime > 0f ? horizontal / deltaTime : 0f;
            stepping = grounded && !teleported && !IsSuppressed && speed > MinStepSpeed;

            if (!seeded)
            {
                seeded = true;
                ReseedFootfallIndex();
            }

            if (!stepping)
            {
                // The phase itself is never snapped: the bob reads it every frame, and a jump in
                // phase is a jump in the camera. Leaving the ground, teleporting, or being
                // suppressed instead re-seeds the footfall index, so travel from before the break
                // cannot burst-fire a stale cue when stepping resumes. Merely stopping keeps the
                // index too, so a tapped stick accumulates toward the next footfall.
                if (!grounded || teleported || IsSuppressed)
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

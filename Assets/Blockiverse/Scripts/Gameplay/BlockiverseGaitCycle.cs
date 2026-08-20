using System;
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
        // A frame that covers more ground than this is a teleport or a respawn, not a stride.
        public const float TeleportResetMeters = 2.0f;
        // Below this the player is drifting or being nudged rather than walking.
        public const float MinStepSpeed = 0.1f;
        // Mid-step, so the first footfall after a landing or a teleport is most of a step out
        // instead of landing on the frame movement resumes.
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
        float speed;
        bool grounded;
        bool stepping;
        bool trackingPosition;

        /// <summary>Raised once per step, <see cref="FootfallLeadPhase"/> ahead of the bob trough.</summary>
        public event Action Footfall;

        public Func<bool> GroundedOverride { get; set; }

        /// <summary>Position within the current step. Zero sits on the walk bob's low point.</summary>
        public float BobPhase01 => stepPhase - Mathf.Floor(stepPhase);

        /// <summary>Measured horizontal speed, in metres per second.</summary>
        public float Speed => speed;

        public bool IsGrounded => grounded;

        /// <summary>True while the cycle is advancing: grounded and actually covering ground.</summary>
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
            ResetCycle();
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

            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            bool teleported = horizontal > TeleportResetMeters;

            speed = !teleported && deltaTime > 0f ? horizontal / deltaTime : 0f;
            stepping = grounded && !teleported && speed > MinStepSpeed;

            if (!stepping)
            {
                // Leaving the ground or teleporting invalidates the cycle. Merely stopping does not:
                // holding the phase means a tapped stick accumulates travel toward the next footfall
                // instead of re-arming one on every restart.
                if (!grounded || teleported)
                    ResetCycle();

                return;
            }

            stepPhase += horizontal / Mathf.Max(MinStepLengthMeters, stepLengthMeters);

            float index = Mathf.Floor(stepPhase + footfallLeadPhase);
            if (index > footfallIndex)
            {
                // One cue per frame even if a long frame covered several steps.
                footfallIndex = index;
                Footfall?.Invoke();
            }
            else if (index < footfallIndex)
            {
                // The lead was tuned down at runtime. Re-seed rather than fire a spurious cue.
                footfallIndex = index;
            }
        }

        void ResetCycle()
        {
            stepPhase = StartPhase;
            footfallIndex = Mathf.Floor(StartPhase + footfallLeadPhase);
            stepping = false;
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

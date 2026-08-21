using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

namespace Blockiverse.VR
{
    // Swimming: the player sinks in water unless they act, rises on the jump input, descends on
    // crouch, and moves horizontally on the usual stick. Modelled on XRI's own JumpProvider --
    // motion is queued as an XROriginMovement, so it routes through the constrained body
    // manipulator and inherits collision, the game's fixed capsule height, and crouch semantics.
    //
    // Only VERTICAL motion is owned here. Horizontal swimming is the existing move provider with a
    // speed factor applied by the rig, so a swimmer keeps every comfort setting they already have.
    [DisallowMultipleComponent]
    public sealed class BlockiverseSwimProvider : LocomotionProvider, IGravityController
    {
        // Sampled a hand's width above the capsule base rather than at it, so standing on a seabed
        // does not read the block below the floor.
        const float FeetSampleHeightMeters = 0.10f;

        // Mid-torso. Crouch and Use My Real Height both change the capsule, so this is taken as a
        // fraction of the live capsule rather than as a fixed offset.
        const float BodySampleCapsuleFraction = 0.55f;

        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] GravityProvider gravityProvider;
        [SerializeField] BlockiverseGaitCycle gaitCycle;
        [SerializeField] Transform headTransform;

        bool gravityLockHeld;
        bool registeredAsGravityController;
        float verticalVelocity;

        public XROriginMovement transformation { get; set; } = new XROriginMovement();

        public SwimState State { get; private set; } = SwimState.Dry;

        public FluidSubmersionState Submersion { get; private set; }

        public FluidFamily Family => Submersion.Family;

        // True in the states where this provider owns vertical motion and gravity is locked off.
        public bool IsSwimming => BlockiverseSwimMotion.OwnsVerticalMotion(State);

        public float VerticalVelocity => verticalVelocity;

        // Whether this provider currently holds the gravity lock. Exposed because "the lock was
        // taken and never released" is the failure that strands a player falling at XRI's terminal
        // velocity or floating in mid-air, and it is worth being able to assert.
        public bool GravityLockHeld => gravityLockHeld;

        bool PassiveSinkEnabled =>
            inputRig == null || inputRig.ComfortSettings == null || inputRig.ComfortSettings.SwimPassiveSinkEnabled;

        float ComfortSpeedFactor =>
            inputRig != null && inputRig.ComfortSettings != null
                ? inputRig.ComfortSettings.SwimSpeedFactor
                : BlockiverseSwimMotion.DefaultSwimSpeedFactor;

        public void Configure(
            BlockiverseInputRig rig,
            CreativeWorldManager manager,
            GravityProvider gravity,
            BlockiverseGaitCycle gait,
            Transform head)
        {
            if (rig != null)
                inputRig = rig;
            if (manager != null)
                worldManager = manager;
            if (gravity != null)
                gravityProvider = gravity;
            if (gait != null)
                gaitCycle = gait;
            if (head != null)
                headTransform = head;
        }

        protected override void Awake()
        {
            base.Awake();

            // Applied last on the entry frame: gravity and jump both leave the priority at 0, so a
            // higher number means the swim motion wins before the lock has taken effect.
            transformationPriority = 1;
            ResolveDependencies();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ResolveDependencies();
            RegisterAsGravityController();

            // The walk cycle drives both the head bob and the footstep cues. Swimming is not
            // walking, so it reports "not grounded" and both stop.
            if (gaitCycle != null)
                gaitCycle.GroundedOverride = () => !IsSwimming;
        }

        protected override void OnDisable()
        {
            // Releasing the lock here is what stops a disabled provider from leaving gravity off
            // forever -- a player would hang motionless in the air with no way to fall.
            ReleaseGravityLock();
            State = SwimState.Dry;
            verticalVelocity = 0.0f;

            if (gaitCycle != null)
                gaitCycle.GroundedOverride = null;

            base.OnDisable();
        }

        void Update()
        {
            ResolveDependencies();

            // Precedence: suppressed locomotion and creative flight both outrank swimming, and both
            // must hand gravity back rather than silently keeping it locked.
            if (inputRig != null && (inputRig.LocomotionSuppressed || inputRig.CreativeFlightLocomotionActive))
            {
                ExitSwimming();
                return;
            }

            Submersion = SampleSubmersion();
            State = BlockiverseSwimMotion.ResolveState(
                Submersion.FeetSubmerged, Submersion.BodySubmerged, Submersion.HeadSubmerged);

            if (!IsSwimming)
            {
                ExitSwimming();
                return;
            }

            AcquireGravityLock();

            float target = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: ReadRiseHeld(),
                sinkHeld: ReadSinkHeld(),
                passiveSinkEnabled: PassiveSinkEnabled,
                family: Submersion.Family);

            verticalVelocity = BlockiverseSwimMotion.AdvanceVerticalVelocity(verticalVelocity, target, Time.deltaTime);

            // Re-requested every frame, and deliberately NOT gated on the return value: the
            // mediator answers false once this provider is already Moving, so treating that as
            // failure queues motion on the entry frame and never again -- the player drifts a
            // couple of millimetres and then hangs there. GravityProvider has the same shape:
            // ask to start, then check the state.
            TryStartLocomotionImmediately();

            if (locomotionState != LocomotionState.Moving)
                return;

            transformation.motion = new Vector3(0.0f, verticalVelocity * Time.deltaTime, 0.0f);
            TryQueueTransformation(transformation);
        }

        void ExitSwimming()
        {
            if (State != SwimState.Dry && State != SwimState.Wading)
                State = SwimState.Dry;

            verticalVelocity = 0.0f;
            ReleaseGravityLock();

            if (locomotionState == LocomotionState.Moving)
                TryEndLocomotion();
        }

        FluidSubmersionState SampleSubmersion()
        {
            VoxelWorld world = worldManager != null ? worldManager.World : null;
            CharacterController controller = inputRig != null ? inputRig.CharacterController : null;

            if (world == null || controller == null)
                return default;

            Bounds bounds = controller.bounds;
            var ground = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            float capsuleHeight = controller.height;

            BlockPosition feet = CreativeInteractionController.ToBlockPosition(
                ground + Vector3.up * FeetSampleHeightMeters);
            BlockPosition body = CreativeInteractionController.ToBlockPosition(
                ground + Vector3.up * (capsuleHeight * BodySampleCapsuleFraction));
            BlockPosition head = headTransform != null
                ? CreativeInteractionController.ToBlockPosition(headTransform.position)
                : body;

            return FluidSubmersion.Sample(world, feet, body, head);
        }

        // Reads the jump ACTION, not jumpProvider.enabled. Jump is gated by locomotion mode, so a
        // teleport-mode player who ends up submerged would otherwise be able to swim down and not
        // up while passive sink pulled them deeper -- an inescapable underwater state in the
        // comfort locomotion mode. The jump provider itself stays disabled while swimming, because
        // a real jump underwater is still meaningless.
        bool ReadRiseHeld()
        {
            if (inputRig == null)
                return false;

            InputAction jumpAction = inputRig.ResolveJumpActionForCurrentControls();

            return jumpAction != null && jumpAction.IsPressed();
        }

        // Crouch's only meaning underwater is "descend"; the rig skips the capsule shrink and the
        // camera drop while swimming.
        bool ReadSinkHeld() => inputRig != null && inputRig.CrouchActive;

        void ResolveDependencies()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<BlockiverseInputRig>();

            if (gravityProvider == null && inputRig != null)
                gravityProvider = inputRig.GravityProvider;

            if (gaitCycle == null)
                gaitCycle = GetComponentInParent<BlockiverseGaitCycle>();

            // Read live, never cached past a frame: New World and Load replace the VoxelWorld
            // instance whole, and a stale world would keep reporting the old lake.
            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>();

            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;
        }

        // GravityProvider only auto-populates its controller list once, from components already
        // under the mediator, and only while that list is still empty -- so in any runtime-built rig
        // it has already been filled by the time a swim provider is added. Without this explicit
        // registration the provider is never consulted and the player sinks at XRI's terminal
        // velocity instead of swimming, silently.
        void RegisterAsGravityController()
        {
            if (registeredAsGravityController || gravityProvider == null)
                return;

            if (!gravityProvider.gravityControllers.Contains(this))
                gravityProvider.gravityControllers.Add(this);

            registeredAsGravityController = true;
        }

        void AcquireGravityLock()
        {
            if (gravityLockHeld || gravityProvider == null)
                return;

            // Tracked with our own flag because TryLockGravity refuses (and warns) when the same
            // provider is already registered, so it cannot be used as its own idempotence check.
            gravityLockHeld = TryLockGravity(GravityOverride.ForcedOff);
        }

        void ReleaseGravityLock()
        {
            if (!gravityLockHeld)
                return;

            RemoveGravityLock();
            gravityLockHeld = false;
        }

        // IGravityController. canProcess follows the component, and gravityPaused mirrors the swim
        // state as defence in depth: the forced-off lock is the real mechanism, but a paused flag
        // costs nothing and covers the window before the lock is taken.
        public bool canProcess => isActiveAndEnabled;

        public bool gravityPaused => IsSwimming;

        public bool TryLockGravity(GravityOverride gravityOverride) =>
            gravityProvider != null && gravityProvider.TryLockGravity(this, gravityOverride);

        public void RemoveGravityLock()
        {
            if (gravityProvider != null)
                gravityProvider.UnlockGravity(this);
        }

        public void OnGravityLockChanged(GravityOverride gravityOverride)
        {
        }

        public void OnGroundedChanged(bool isGrounded)
        {
        }
    }
}

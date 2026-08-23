using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
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
    public sealed class BlockiverseSwimProvider : LocomotionProvider, IGravityController, ITunnelingVignetteProvider
    {
        // Sampled a hand's width above the capsule base rather than at it, so standing on a seabed
        // does not read the block below the floor.
        const float FeetSampleHeightMeters = 0.10f;

        // Mid-torso. Crouch and Use My Real Height both change the capsule, so this is taken as a
        // fraction of the live capsule rather than as a fixed offset.
        const float BodySampleCapsuleFraction = 0.55f;

        // How fast the shore lift travels. Brisk enough not to feel like being winched, slow
        // enough to read as climbing rather than teleporting.
        const float ClimbOutSpeedMetersPerSecond = 2.2f;

        // How hard the player must be pushing before a lift is considered a request.
        const float ClimbOutMoveIntentThreshold = 0.5f;

        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] GravityProvider gravityProvider;
        [SerializeField] BlockiverseGaitCycle gaitCycle;
        [SerializeField] Transform headTransform;
        [SerializeField] TunnelingVignetteController vignetteController;

        bool gravityLockHeld;
        int lastFeetCellY;
        bool headSubmergedHeld;
        bool registeredAsGravityController;
        bool vignetteEngaged;
        float verticalVelocity;

        // Null means "use the controller's own defaults", which BlockiverseVignetteSettingsDriver
        // keeps pinned to the player's comfort aperture every frame -- the same contract the move,
        // turn and teleport providers get through their LocomotionVignetteProvider entries.
        public VignetteParameters vignetteParameters => null;

        public XROriginMovement transformation { get; set; } = new XROriginMovement();

        public SwimState State { get; private set; } = SwimState.Dry;

        // Raised on every State transition, with the family of the fluid involved. Presentation
        // (audio, and anything else that reacts to entering or leaving water) subscribes here
        // rather than polling State, per voxel_audio_vfx_ruleset.md section 1: gameplay raises
        // events, presentation systems subscribe. Every assignment routes through SetState so no
        // path can change the state without announcing it.
        public event System.Action<SwimState, SwimState, FluidFamily> StateChanged;

        void SetState(SwimState next)
        {
            if (next == State)
                return;

            SwimState previous = State;
            State = next;
            ApplyGroundedOverride();
            StateChanged?.Invoke(previous, next, Submersion.Family);
        }

        // Cached so the per-transition assignment allocates nothing.
        static readonly System.Func<bool> NeverGrounded = () => false;

        // Veto grounding while swimming, and otherwise get out of the way. The gait cycle's
        // override REPLACES its ground probe rather than combining with it, so leaving a
        // `() => !IsSwimming` installed while dry asserts "grounded" through every fall.
        void ApplyGroundedOverride()
        {
            if (gaitCycle == null)
                return;

            gaitCycle.GroundedOverride = IsSwimming ? NeverGrounded : null;
        }

        public FluidSubmersionState Submersion { get; private set; }

        public FluidFamily Family => Submersion.Family;

        // True in the states where this provider owns vertical motion and gravity is locked off.
        public bool IsSwimming => BlockiverseSwimMotion.OwnsVerticalMotion(State);

        public float VerticalVelocity => verticalVelocity;

        // Whether this provider currently holds the gravity lock. Exposed because "the lock was
        // taken and never released" is the failure that strands a player falling at XRI's terminal
        // velocity or floating in mid-air, and it is worth being able to assert.
        public bool GravityLockHeld => gravityLockHeld;

        // Whether this provider is currently asking the tunneling vignette to close. Exposed for
        // the same reason as the gravity lock: it is a decision this component owns, and a comfort
        // aid that silently stops engaging is invisible until someone feels sick.
        public bool VignetteEngaged => vignetteEngaged;

        bool PassiveSinkEnabled =>
            inputRig == null || inputRig.ComfortSettings == null || inputRig.ComfortSettings.SwimPassiveSinkEnabled;

        // Passive descent is motion the player did not ask for, so it gets the same tunneling
        // vignette that driven vertical motion does -- that aid is a large part of why defaulting
        // to negative buoyancy is defensible at all.
        bool VignetteBoostEnabled =>
            inputRig == null || inputRig.ComfortSettings == null || inputRig.ComfortSettings.SwimVignetteBoost;

        bool ClimbOutEnabled =>
            inputRig == null || inputRig.ComfortSettings == null || inputRig.ComfortSettings.SwimClimbOutEnabled;

        public void ConfigureVignette(TunnelingVignetteController controller)
        {
            if (controller != null)
                vignetteController = controller;
        }

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
            ApplyGroundedOverride();
        }

        protected override void OnDisable()
        {
            // Releasing the lock here is what stops a disabled provider from leaving gravity off
            // forever -- a player would hang motionless in the air with no way to fall.
            ReleaseGravityLock();
            UpdateVignette(false);
            SetState(SwimState.Dry);
            verticalVelocity = 0.0f;
            headSubmergedHeld = false;

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
            // Depth-based: one block of water is walkable whoever you are. See the remarks on the
            // overload -- the capsule-fraction body sample put every default-height player in
            // Surfaced while ankle deep, which locks gravity off in a puddle.
            SetState(BlockiverseSwimMotion.ResolveState(Submersion, lastFeetCellY));

            if (!IsSwimming)
            {
                ExitSwimming();
                return;
            }

            AcquireGravityLock();

            // Checked before the vertical target, and while the gravity lock is still held: the
            // lift has to complete before ExitSwimming can fire, or the player is dropped halfway
            // up the bank with gravity back on and slides straight in again.
            if (TryClimbOut())
                return;

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
            // Neutral buoyancy with no input is genuinely not moving: ending locomotion there keeps
            // "Moving" meaning what it says, and stops the vignette engaging around a swimmer who
            // is holding perfectly still.
            if (Mathf.Approximately(verticalVelocity, 0.0f))
            {
                UpdateVignette(false);

                if (locomotionState == LocomotionState.Moving)
                    TryEndLocomotion();

                return;
            }

            TryStartLocomotionImmediately();

            if (locomotionState != LocomotionState.Moving)
                return;

            UpdateVignette(true);
            transformation.motion = new Vector3(0.0f, verticalVelocity * Time.deltaTime, 0.0f);
            TryQueueTransformation(transformation);
        }

        // Lifts a swimmer onto a low bank they are actively swimming toward.
        //
        // Without this, reaching the shore does not get you out: ResolveState leaves the swim
        // states the moment the BODY sample goes dry, and that sample sits about a metre above the
        // capsule base -- so the swim provider hands back control while the player's feet are still
        // roughly a metre below the bank, gravity resumes, and they fall back in. The character
        // controller cannot rescue it either: its step offset is a fraction of a metre, its step
        // assist needs to be grounded and a treading player never is, and jump is disabled while
        // swimming.
        //
        // This reverses the swim design's original "no ledge-climb assist" decision (see
        // voxel_survival_ruleset.md §5.6, "Climbing out"), which was taken on comfort
        // grounds. The argument for reversing it: the lift only ever fires while the player is
        // pushing INTO the bank, so it is redirected requested motion rather than the unrequested
        // motion that ADR rejected -- and it is capped at two blocks. See the ADR amendment.
        bool TryClimbOut()
        {
            if (!ClimbOutEnabled || inputRig == null)
                return false;

            VoxelWorld world = worldManager != null ? worldManager.World : null;
            CharacterController controller = inputRig.CharacterController;

            if (world == null || worldManager.Registry == null || controller == null)
                return false;

            if (!TryResolveForwardStep(out int forwardX, out int forwardZ))
                return false;

            Bounds bounds = controller.bounds;
            BlockPosition feet = CreativeInteractionController.ToBlockPosition(
                new Vector3(bounds.center.x, bounds.min.y + FeetSampleHeightMeters, bounds.center.z));

            if (!FluidLedge.TryResolveClimbOut(world, worldManager.Registry, feet, forwardX, forwardZ, out BlockPosition landing))
                return false;

            // Queued through the same transformation path as every other swim motion, so it
            // inherits collision, the live capsule height and crouch semantics rather than
            // teleporting the player into geometry.
            TryStartLocomotionImmediately();

            if (locomotionState != LocomotionState.Moving)
                return false;

            var destination = new Vector3(landing.X + 0.5f, landing.Y, landing.Z + 0.5f);
            Vector3 current = new(bounds.center.x, bounds.min.y, bounds.center.z);
            Vector3 step = Vector3.MoveTowards(current, destination, ClimbOutSpeedMetersPerSecond * Time.deltaTime) - current;

            verticalVelocity = 0.0f;
            UpdateVignette(true);
            transformation.motion = step;
            TryQueueTransformation(transformation);
            return true;
        }

        // Quantised to one axis. A diagonal would need both neighbouring columns checked to avoid
        // pulling the player through a corner, and the dominant axis is what the player means.
        bool TryResolveForwardStep(out int forwardX, out int forwardZ)
        {
            forwardX = 0;
            forwardZ = 0;

            Vector2 move = inputRig.MoveInput;

            if (move.sqrMagnitude < ClimbOutMoveIntentThreshold * ClimbOutMoveIntentThreshold)
                return false;

            Transform head = headTransform;

            if (head == null)
                return false;

            // Stick direction taken into world space through the head's yaw, matching how the move
            // provider interprets it.
            Vector3 forward = head.forward;
            Vector3 right = head.right;
            forward.y = 0.0f;
            right.y = 0.0f;

            Vector3 world = (forward.normalized * move.y) + (right.normalized * move.x);

            if (world.sqrMagnitude < 1e-4f)
                return false;

            if (Mathf.Abs(world.x) >= Mathf.Abs(world.z))
                forwardX = world.x >= 0.0f ? 1 : -1;
            else
                forwardZ = world.z >= 0.0f ? 1 : -1;

            return true;
        }

        void UpdateVignette(bool moving)
        {
            if (vignetteController == null)
                return;

            bool wanted = moving && VignetteBoostEnabled;

            if (wanted == vignetteEngaged)
                return;

            if (wanted)
                vignetteController.BeginTunnelingVignette(this);
            else
                vignetteController.EndTunnelingVignette(this);

            vignetteEngaged = wanted;
        }

        void ExitSwimming()
        {
            if (State != SwimState.Dry && State != SwimState.Wading)
                SetState(SwimState.Dry);

            verticalVelocity = 0.0f;
            headSubmergedHeld = false;
            ReleaseGravityLock();
            UpdateVignette(false);

            if (locomotionState == LocomotionState.Moving)
                TryEndLocomotion();
        }

        // The raw head sample is a bare cell lookup, so a head resting at the surface flips
        // Swimming/Surfaced every frame -- and passive sink means a treading player re-crosses the
        // line constantly. BlockiverseSwimMotion.ResolveHeadSubmerged exists for exactly this and
        // was never wired up; without it the distinction strobes and everything downstream strobes
        // with it (the comfort vignette, and the underwater audio bed, which needed its own release
        // window to stop clicking once per frame).
        FluidSubmersionState ApplyHeadHysteresis(in FluidSubmersionState sampled, float headWorldY)
        {
            if (!sampled.InFluid || !sampled.HasSurface || headWorldY <= float.MinValue)
            {
                headSubmergedHeld = false;
                return sampled;
            }

            // SurfaceCellY is the topmost fluid cell; the water line is its top face.
            float surfaceWorldY = sampled.SurfaceCellY + 1.0f;
            bool headSubmerged = BlockiverseSwimMotion.ResolveHeadSubmerged(
                headSubmergedHeld, headWorldY, surfaceWorldY);
            headSubmergedHeld = headSubmerged;

            if (headSubmerged == sampled.HeadSubmerged)
                return sampled;

            return new FluidSubmersionState(
                inFluid: sampled.InFluid,
                family: sampled.Family,
                immersion: headSubmerged
                    ? FluidImmersion.Head
                    : sampled.BodySubmerged ? FluidImmersion.Body : FluidImmersion.Feet,
                feetSubmerged: sampled.FeetSubmerged,
                bodySubmerged: sampled.BodySubmerged,
                headSubmerged: headSubmerged,
                hasSurface: sampled.HasSurface,
                surfaceCellY: sampled.SurfaceCellY,
                fluidBelowFeet: sampled.FluidBelowFeet);
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

            lastFeetCellY = feet.Y;
            FluidSubmersionState sampled = FluidSubmersion.Sample(world, feet, body, head);
            return ApplyHeadHysteresis(sampled, headTransform != null ? headTransform.position.y : float.MinValue);
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

            if (vignetteController == null)
                vignetteController = GetComponentInChildren<TunnelingVignetteController>(true);
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

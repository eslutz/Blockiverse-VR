using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

namespace Blockiverse.VR
{
    // Creative flight, reworked to match how swimming works.
    //
    // It used to be a plain MonoBehaviour that wrote `transform.position +=` directly, which meant
    // it bypassed the locomotion mediator and the CharacterController entirely -- so flight had NO
    // COLLISION and you passed through terrain. It also moved only along the dominant hand's aim
    // while the jump button was held, and disabled ContinuousMoveProvider outright, so the move
    // stick did nothing at all while flying.
    //
    // Now it is a LocomotionProvider like BlockiverseSwimProvider, and the controls line up with
    // swimming: stick for horizontal (through the existing move provider, so every comfort setting
    // still applies), jump/A to rise, crouch/B to descend. Vertical motion is queued as an
    // XROriginMovement, so it routes through the constrained body manipulator and inherits
    // collision and the live capsule height.
    //
    // The one deliberate difference from swimming: with no input, flight HOVERS. Swimming's
    // resting state is sinking; flight's is holding position.
    [DisallowMultipleComponent]
    public sealed class BlockiverseCreativeFlightController : LocomotionProvider, IGravityController
    {
        const float FlightSpeedBlocksPerTick = 0.10f;
        const float SprintFlightSpeedBlocksPerTick = 0.22f;
        const float DoubleClickWindowSeconds = 0.35f;
        // A press longer than this is a flight hold, not a tap.
        const float TapMaxDurationSeconds = 0.25f;

        // Matches the swim provider's ramp so rising and descending feel the same in both.
        const float VerticalAccelerationMetersPerSecondSquared = 6.0f;

        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        BlockiverseGaitCycle gaitCycle;
        [SerializeField] bool flightEnabledDefault;
        [SerializeField] Transform dominantHandAimSource;

        Transform cachedLeftHandAimSource;
        Transform cachedRightHandAimSource;

        bool hasExplicitFlightState;
        bool requestedFlightActive;
        bool providerStateInitialized;
        bool lastProviderActive;
        bool jumpHeldLastFrame;
        float jumpPressStartedAt = -1.0f;
        float lastTapEndedAt = -10.0f;
        bool lastPressWasTap;
        bool gravityLockHeld;
        bool registeredAsGravityController;
        float verticalVelocity;

        public XROriginMovement transformation { get; set; } = new XROriginMovement();

        // Exposed for the same reason the swim provider exposes it: a lock taken and never released
        // strands the player hanging in mid-air with no way to fall.
        public bool GravityLockHeld => gravityLockHeld;

        public float VerticalVelocity => verticalVelocity;

        public BlockiverseInputRig InputRig => inputRig;
        public bool IsFlightActive { get; private set; }
        public bool FlightEnabledDefault
        {
            get => flightEnabledDefault;
            set => flightEnabledDefault = value;
        }

        public static float FlightSpeedBlocksPerSecond => FlightSpeedBlocksPerTick * SimulationTime.TicksPerSecond;
        public static float SprintFlightSpeedBlocksPerSecond => SprintFlightSpeedBlocksPerTick * SimulationTime.TicksPerSecond;

        public void Configure(
            BlockiverseInputRig rig,
            CreativeWorldManager manager = null,
            MultiplayerSurvivalSync sync = null)
        {
            if (inputRig != rig)
            {
                // The cached gait belongs to the old rig; leaving it would keep suppressing the old
                // rig's cycle while the new rig's cycle never learns about flight.
                if (gaitCycle != null)
                    gaitCycle.ExternallySuppressed = false;

                gaitCycle = null;
                inputRig = rig;
                ClearCachedAimSources();
            }

            worldManager = manager != null ? manager : worldManager;
            survivalSync = sync != null ? sync : survivalSync;
            if (Application.isPlaying)
                ApplyFlightState();
        }

        protected override void Awake()
        {
            base.Awake();

            // Applied after gravity and jump, which both sit at 0, so flight's vertical motion
            // wins on the entry frame before the gravity lock has taken effect.
            transformationPriority = 1;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            DiscoverDependencies();
            RegisterAsGravityController();
            ApplyFlightState();
        }

        protected override void OnDisable()
        {
            // Releasing here is what stops a disabled provider leaving gravity off forever, which
            // would hang the player motionless in mid-air with nothing to explain why.
            ReleaseGravityLock();
            verticalVelocity = 0.0f;
            ApplyProviderState(active: false);
            IsFlightActive = false;
            base.OnDisable();
        }

        void Update()
        {
            UpdateFlightToggleInput();
            ApplyFlightState();
            TickFlightMotion(Time.deltaTime);
        }

        public void SetFlightActive(bool active)
        {
            hasExplicitFlightState = true;
            requestedFlightActive = active;
            ApplyFlightState();
        }

        public void ToggleFlightMode()
        {
            SetFlightActive(!IsFlightRequestedActive());
        }

        public void ApplyFlightState()
        {
            DiscoverDependencies();

            bool creative = IsCreativePlayer();
            if (!creative)
            {
                hasExplicitFlightState = false;
                requestedFlightActive = false;
            }

            bool active = creative && IsFlightRequestedActive();
            ApplyProviderState(active);
            IsFlightActive = active;
        }

        /// <summary>A press short enough to count as a tap; anything longer is a flight hold.</summary>
        public static bool IsTapPress(float pressDurationSeconds) =>
            pressDurationSeconds >= 0.0f && pressDurationSeconds <= TapMaxDurationSeconds;

        /// <summary>
        /// A new press completes a double-tap only when the previous press was itself a tap, so
        /// holding the button to fly can never toggle flight off mid-air.
        /// </summary>
        public static bool CompletesDoubleTap(bool previousPressWasTap, float secondsSincePreviousPress) =>
            previousPressWasTap &&
            secondsSincePreviousPress >= 0.0f &&
            secondsSincePreviousPress <= DoubleClickWindowSeconds;

        public static Vector3 ComputeFlightDisplacement(Vector3 aimForward, bool moveHeld, float deltaSeconds)
        {
            return ComputeFlightDisplacement(aimForward, moveHeld, sprintActive: false, deltaSeconds);
        }

        public static Vector3 ComputeFlightDisplacement(Vector3 aimForward, bool moveHeld, bool sprintActive, float deltaSeconds)
        {
            if (!moveHeld || deltaSeconds <= 0.0f)
                return Vector3.zero;

            if (aimForward.sqrMagnitude <= 0.0001f)
                return Vector3.zero;

            float speed = sprintActive ? SprintFlightSpeedBlocksPerSecond : FlightSpeedBlocksPerSecond;
            return aimForward.normalized * speed * deltaSeconds;
        }

        void DiscoverDependencies()
        {
            if (inputRig == null)
            {
                BlockiverseInputRig discoveredRig = GetComponent<BlockiverseInputRig>() ?? GetComponentInParent<BlockiverseInputRig>();
                if (discoveredRig != null)
                {
                    inputRig = discoveredRig;
                    ClearCachedAimSources();
                }
            }

            if (!Application.isPlaying)
                return;

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (survivalSync == null)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
        }

        bool IsCreativePlayer()
        {
            if (survivalSync != null)
                return survivalSync.CurrentMode == PlayerModeState.Creative;

            return worldManager != null && worldManager.GameMode == WorldGameMode.Creative;
        }

        bool IsFlightRequestedActive()
        {
            return hasExplicitFlightState ? requestedFlightActive : flightEnabledDefault;
        }

        void UpdateFlightToggleInput()
        {
            if (!Application.isPlaying || !BlockiverseRuntimeState.AllowWorldInput)
                return;

            InputAction jump = ResolveJumpAction();
            if (jump == null)
            {
                jumpHeldLastFrame = false;
                return;
            }

            // Edges are derived from IsPressed() polling rather than WasPressedThisFrame /
            // WasReleasedThisFrame: IsPressed is the reading that reliably drives hold-to-fly on
            // device, and depending on the edge helpers here left flight untoggleable.
            float now = Time.unscaledTime;
            bool held = jump.IsPressed();

            if (held && !jumpHeldLastFrame)
            {
                // Toggle on the second press of a double-tap. The previous press must have been a
                // quick tap, so holding the button to fly can never complete a double-tap and
                // switch flight off mid-air. Taps count in both directions, so a double-tap
                // enters flight and a second double-tap exits it (gravity then takes over).
                if (CompletesDoubleTap(lastPressWasTap, now - lastTapEndedAt))
                {
                    ToggleFlightMode();
                    lastPressWasTap = false;
                    lastTapEndedAt = -10.0f;
                }

                jumpPressStartedAt = now;
            }
            else if (!held && jumpHeldLastFrame)
            {
                float pressDuration = jumpPressStartedAt >= 0.0f ? now - jumpPressStartedAt : float.MaxValue;
                jumpPressStartedAt = -1.0f;
                lastPressWasTap = IsTapPress(pressDuration);
                lastTapEndedAt = now;
            }

            jumpHeldLastFrame = held;
        }

        void TickFlightMotion(float deltaSeconds)
        {
            // Suppression counts as inactive, exactly as it does for swimming. ApplyProviderState
            // disables the STANDARD providers while locomotion is suppressed, but this one queues
            // its own vertical XROriginMovement -- so without this it keeps displacing the rig
            // during New World and Load, while the session controller is restoring the saved
            // position or waiting on spawn collision. A creative player holding rise or descend
            // through a world load is all it takes.
            bool suppressed = inputRig != null && inputRig.LocomotionSuppressed;

            if (!Application.isPlaying || !IsFlightActive || suppressed)
            {
                ReleaseGravityLock();
                verticalVelocity = 0.0f;

                if (locomotionState == LocomotionState.Moving)
                    TryEndLocomotion();

                return;
            }

            InputAction jump = ResolveJumpAction();
            bool riseHeld = jump != null && jump.IsPressed();
            bool sinkHeld = inputRig != null && inputRig.CrouchActive;
            bool sprintActive = inputRig != null && inputRig.SprintActive;

            float target = ResolveVerticalTarget(riseHeld, sinkHeld, sprintActive);
            verticalVelocity = Mathf.MoveTowards(
                verticalVelocity, target, VerticalAccelerationMetersPerSecondSquared * deltaSeconds);

            // Hovering is genuinely not moving. Ending locomotion here keeps "Moving" meaning what
            // it says, exactly as the swim provider does at neutral buoyancy.
            if (Mathf.Approximately(verticalVelocity, 0.0f))
            {
                if (locomotionState == LocomotionState.Moving)
                    TryEndLocomotion();

                return;
            }

            // Re-requested every frame and deliberately NOT gated on the return value: the mediator
            // answers false once this provider is already Moving, so treating that as failure
            // queues motion on the entry frame and never again.
            TryStartLocomotionImmediately();

            if (locomotionState != LocomotionState.Moving)
                return;

            transformation.motion = new Vector3(0.0f, verticalVelocity * deltaSeconds, 0.0f);
            TryQueueTransformation(transformation);
        }

        // Vertical speed the player is asking for. Zero with no input: flight hovers, which is the
        // exact inverse of swimming's negative buoyancy.
        public static float ResolveVerticalTarget(bool riseHeld, bool sinkHeld, bool sprintActive)
        {
            if (riseHeld == sinkHeld)
                return 0.0f;

            float speed = sprintActive ? SprintFlightSpeedBlocksPerSecond : FlightSpeedBlocksPerSecond;
            return riseHeld ? speed : -speed;
        }

        void AcquireGravityLock()
        {
            if (gravityLockHeld || inputRig == null || inputRig.GravityProvider == null)
                return;

            gravityLockHeld = TryLockGravity(GravityOverride.ForcedOff);
        }

        void ReleaseGravityLock()
        {
            if (!gravityLockHeld)
                return;

            RemoveGravityLock();
            gravityLockHeld = false;
        }

        // GravityProvider auto-populates its controller list exactly once, from components already
        // present, so a provider added to a runtime-built rig is never consulted without this --
        // the failure ADR 0008 documents for swimming, and it would bite identically here.
        void RegisterAsGravityController()
        {
            if (registeredAsGravityController || inputRig == null || inputRig.GravityProvider == null)
                return;

            if (!inputRig.GravityProvider.gravityControllers.Contains(this))
                inputRig.GravityProvider.gravityControllers.Add(this);

            registeredAsGravityController = true;
        }

        public bool canProcess => isActiveAndEnabled;

        public bool gravityPaused => IsFlightActive;

        public bool TryLockGravity(GravityOverride gravityOverride) =>
            inputRig != null && inputRig.GravityProvider != null &&
            inputRig.GravityProvider.TryLockGravity(this, gravityOverride);

        public void RemoveGravityLock()
        {
            if (inputRig != null && inputRig.GravityProvider != null)
                inputRig.GravityProvider.UnlockGravity(this);
        }

        public void OnGravityLockChanged(GravityOverride gravityOverride)
        {
        }

        public void OnGroundedChanged(bool isGrounded)
        {
        }

        InputAction ResolveJumpAction()
        {
            return inputRig != null ? inputRig.ResolveJumpActionForCurrentControls() : null;
        }

        Vector3 ResolveFlightForward()
        {
            Transform aim = ResolveDominantHandAimSource();
            Vector3 forward = aim != null ? aim.forward : transform.forward;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        Transform ResolveDominantHandAimSource()
        {
            if (dominantHandAimSource != null)
                return dominantHandAimSource;

            BlockiverseControllerRole hand = inputRig != null
                ? inputRig.ActiveToolHand
                : BlockiverseControllerRole.Right;

            return hand == BlockiverseControllerRole.Left
                ? ResolveLeftHandAimSource()
                : ResolveRightHandAimSource();
        }

        Transform ResolveLeftHandAimSource()
        {
            if (cachedLeftHandAimSource == null)
                cachedLeftHandAimSource = ResolveControllerAimSource("Left Controller");

            return cachedLeftHandAimSource;
        }

        Transform ResolveRightHandAimSource()
        {
            if (cachedRightHandAimSource == null)
                cachedRightHandAimSource = ResolveControllerAimSource("Right Controller");

            return cachedRightHandAimSource;
        }

        // Flight travels along where the controller POINTS, which is the ray origin the pointer
        // rays use — NOT the controller transform, whose forward is the OpenXR grip pose running
        // up the handle (holding the controller level would fly the player straight up).
        //
        // Returns null until the rig has built the ray origin; ResolveFlightForward falls back to
        // the rig forward for that frame rather than caching a grip-posed source, which would
        // leave flight 90 degrees off for the rest of the session.
        Transform ResolveControllerAimSource(string controllerName)
        {
            Transform root = inputRig != null ? inputRig.transform : transform;
            Transform cameraOffset = root.Find("Camera Offset");
            Transform controller = cameraOffset != null ? cameraOffset.Find(controllerName) : null;
            return controller != null ? controller.Find("Ray Origin") : null;
        }

        void ClearCachedAimSources()
        {
            cachedLeftHandAimSource = null;
            cachedRightHandAimSource = null;
        }

        void ApplyProviderState(bool active)
        {
            if (inputRig == null)
                return;

            bool providerStateChanged = !providerStateInitialized || lastProviderActive != active;
            providerStateInitialized = true;
            lastProviderActive = active;
            inputRig.TurnWithBothHands = active;
            inputRig.CreativeFlightLocomotionActive = active;

            // Flight moves the rig without walking, and its ground-skimming still reads as
            // grounded to the gravity provider's sphere cast. Suppressing the gait cycle keeps
            // footstep cues and the walk bob out of flight entirely.
            if (gaitCycle == null)
                gaitCycle = inputRig.GetComponent<BlockiverseGaitCycle>();

            if (gaitCycle != null)
                gaitCycle.ExternallySuppressed = active;

            if (inputRig.LocomotionSuppressed)
            {
                var suppressedMove = inputRig.ContinuousMoveProvider;
                if (suppressedMove != null)
                {
                    suppressedMove.enableFly = false;
                    suppressedMove.enabled = false;
                }

                var suppressedGravity = inputRig.GravityProvider;
                if (suppressedGravity != null)
                {
                    suppressedGravity.enabled = true;
                    suppressedGravity.useGravity = false;
                }

                var suppressedJump = inputRig.JumpProvider;
                if (suppressedJump != null)
                    suppressedJump.enabled = false;

                return;
            }

            // Enabled while FLYING, and otherwise left alone. Horizontal flight is the ordinary
            // move provider, so a flying player keeps every comfort setting they already have --
            // speed, vignette, deadzone -- instead of a second bespoke path that has none of them.
            //
            // But this method runs every frame, so forcing `enabled = true` unconditionally makes
            // it fight every other owner of that provider: the teleport/glide mode machinery, and
            // the locomotion-suppression handoff during world loads. When flight is off, whoever
            // owns locomotion owns it -- RefreshLocomotionProviderState below is the restore path.
            var move = inputRig.ContinuousMoveProvider;
            if (move != null)
            {
                move.enableFly = false;

                if (active)
                    move.enabled = true;
            }

            var gravity = inputRig.GravityProvider;
            if (gravity != null)
            {
                gravity.enabled = true;

                // Still written explicitly, even though the lock below is the robust mechanism.
                // The suppression branch above sets useGravity = false and returns early, so this
                // is the ONLY thing that turns gravity back on when suppression lifts -- and world
                // loads suppress locomotion. Dropping it left gravity off for good afterwards.
                gravity.useGravity = !active;
            }

            RegisterAsGravityController();

            // The lock is what survives a comfort re-push, which re-asserts useGravity behind us.
            // Taken here rather than on the next Update, so there is no frame in which flight is
            // active and gravity is still pulling.
            if (active)
                AcquireGravityLock();
            else
                ReleaseGravityLock();

            // A real jump while flying is meaningless -- jump/A is the ascend verb here.
            var jump = inputRig.JumpProvider;
            if (jump != null)
                jump.enabled = !active && move != null && move.enabled;

            if (!active && providerStateChanged)
                inputRig.RefreshLocomotionProviderState();
        }
    }
}

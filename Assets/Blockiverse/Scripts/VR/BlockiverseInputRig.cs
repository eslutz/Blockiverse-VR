using System;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Unity.XR.CoreUtils;

namespace Blockiverse.VR
{
    [DefaultExecutionOrder(XRInteractionUpdateOrder.k_LocomotionProviders - 1)]
    public sealed class BlockiverseInputRig : MonoBehaviour, IBlockiverseInputRig
    {
        const float DefaultContinuousMoveSpeed = 1.8f;
        const float SprintMoveMultiplier = 2.2f;

        // FLIGHT CRUISE IS LAND SPRINT (Eric, 2026-08-24: "normal speed should be what land
        // sprinting speed is and flying sprint speed should be faster than that").
        //
        // Horizontal flight is the ORDINARY move provider — flight only owns vertical — so before
        // this, flying forward moved you at exactly walking pace. Expressed as a multiple of
        // SprintMoveMultiplier rather than as a literal so the two cannot drift apart, and applied
        // to the comfort-adjusted base speed so a player who slowed movement down for comfort has
        // that respected in the air too.
        const float FlightCruiseMoveMultiplier = SprintMoveMultiplier;
        const float FlightSprintMoveMultiplier = SprintMoveMultiplier * 2.0f;


        const float CrouchCameraDropMetersPerSecond = 3.0f;
        const float DefaultSnapTurnDegrees = 45.0f;
        const float DefaultContinuousTurnSpeed = 60.0f;
        const float DefaultJumpHeightMeters = 1.3f;
        const string HeadPositionPath = "<XRHMD>/centerEyePosition";
        const string HeadRotationPath = "<XRHMD>/centerEyeRotation";
        const string HeadTrackingStatePath = "<XRHMD>/trackingState";
        const string LeftControllerPositionPath = "<XRController>{LeftHand}/devicePosition";
        const string LeftControllerRotationPath = "<XRController>{LeftHand}/deviceRotation";
        const string LeftControllerTrackingStatePath = "<XRController>{LeftHand}/trackingState";
        const string RightControllerPositionPath = "<XRController>{RightHand}/devicePosition";
        const string RightControllerRotationPath = "<XRController>{RightHand}/deviceRotation";
        const string RightControllerTrackingStatePath = "<XRController>{RightHand}/trackingState";
        const string LeftAimPoseName = "Left Aim Pose";
        const string RightAimPoseName = "Right Aim Pose";
        const string LeftRayOriginName = "Left Ray Origin";
        const string RightRayOriginName = "Right Ray Origin";
        const string ControllerRayOriginName = "Ray Origin";

        [SerializeField] InputActionAsset inputActions;
        [SerializeField] TrackedPoseDriver headPoseDriver;
        [SerializeField] XRBodyTransformer bodyTransformer;
        [SerializeField] LocomotionMediator locomotionMediator;
        [SerializeField] ContinuousMoveProvider continuousMoveProvider;
        [SerializeField] TeleportationProvider teleportationProvider;
        [SerializeField] SnapTurnProvider snapTurnProvider;
        [SerializeField] ContinuousTurnProvider continuousTurnProvider;
        [SerializeField] GravityProvider gravityProvider;
        [SerializeField] JumpProvider jumpProvider;
        [SerializeField] BlockiverseSwimProvider swimProvider;
        [SerializeField] CharacterController characterController;
        BlockiverseGaitCycle wiredGaitCycle;
        BlockiversePlayerBodyManipulator playerBodyManipulator;
        float appliedCrouchCameraDrop;
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] BlockiverseHeightReset heightReset;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseControllerHaptics leftControllerHaptics;
        [SerializeField] BlockiverseControllerHaptics rightControllerHaptics;
        [SerializeField] BlockiverseFoveatedRenderingController foveatedRenderingController;
        [SerializeField] UnityEvent menuPressed = new();
        [SerializeField] UnityEvent screensPressed = new();
        [SerializeField] UnityEvent hotbarNextPressed = new();
        [SerializeField] UnityEvent hotbarPreviousPressed = new();
        [SerializeField] UnityEvent breakPressed = new();
        [SerializeField] UnityEvent breakReleased = new();
        [SerializeField] UnityEvent placePressed = new();
        [SerializeField] UnityEvent blockEditingTogglePressed = new();

        Action<LocomotionProvider> teleportEndedHandler;
        Action<LocomotionProvider> snapTurnEndedHandler;

        // Cached gameplay/hand actions — resolved once per InputActionAsset so the hot Update
        // poll avoids five string-keyed FindActionMap/FindAction lookups every frame.
        InputActionAsset cachedActionAsset;
        InputAction cachedMenuAction;
        InputAction cachedScreensAction;
        InputAction cachedHotbarNextAction;
        InputAction cachedHotbarPreviousAction;
        InputAction cachedBreakAction;
        InputAction cachedPlaceAction;
        InputAction cachedBlockEditingToggleAction;
        InputAction cachedSprintAction;
        InputAction cachedCrouchAction;
        BlockiverseControllerRole cachedDominantHand;

        // Last comfort values pushed to the XRI providers. Provider fields — and especially the
        // jump reader, whose InputActionReference is a ScriptableObject instance — must only be
        // rebuilt when a setting actually changes, never per frame.
        bool comfortApplied;
        BlockiverseLocomotionMode lastLocomotionMode;
        bool lastSmoothTurn;
        float lastMoveSpeed;
        float lastContinuousTurnSpeed;
        float lastSnapTurnDegrees;
        bool lastSnapTurnAroundEnabled;
        BlockiverseControllerRole lastDominantHand;
        bool lastTurnWithBothHands;
        bool lastSprintActive;
        bool lastSwimming;
        // Flight now changes the resolved move speed (cruise = land sprint), so it has to take
        // part in the change detection below. Without it the guard would skip the re-push and the
        // new speed would never reach the provider — the change would fail by being ignored.
        bool lastFlying;
        float lastSwimSpeedFactor = 1.0f;
        bool locomotionSuppressed;
        bool turnWithBothHands;
        bool creativeFlightLocomotionActive;
        bool sprintToggled;
        bool sprintHeld;
        bool placeModifierToggled;
        bool placeModifierHeld;
        bool crouchToggled;
        bool crouchHeld;
        XRRayInteractor leftInteractionRay;
        XRRayInteractor rightInteractionRay;

        static LayerMask? cachedTerrainLayerMask;
        static LayerMask? cachedTargetingLayerMask;

        public InputActionAsset InputActions => inputActions;
        public UnityEvent MenuPressed => menuPressed;
        public UnityEvent ScreensPressed => screensPressed;
        public UnityEvent HotbarNextPressed => hotbarNextPressed;
        public UnityEvent HotbarPreviousPressed => hotbarPreviousPressed;
        public UnityEvent BreakPressed => breakPressed;
        public UnityEvent BreakReleased => breakReleased;
        // Live held-state of the break input (hold-to-mine polls this as a release safety net).
        public bool IsBreakHeld => cachedBreakAction != null && cachedBreakAction.IsPressed();
        public UnityEvent PlacePressed => placePressed;

        /// <summary>Whether the trigger should PLACE rather than BREAK this frame.
        ///
        /// The grip no longer places directly — it selects what the trigger does, so one button
        /// covers both verbs and the placement highlight only appears when placing is actually
        /// what will happen. Hold or toggle per the comfort setting, resolved through the same
        /// helper as sprint and crouch.</summary>
        public bool PlaceModifierActive =>
            ResolveModifierActive(PlaceModifierToggleEnabled, placeModifierHeld, placeModifierToggled);
        public UnityEvent BlockEditingTogglePressed => blockEditingTogglePressed;
        public bool SprintActive => ResolveModifierActive(SprintToggleEnabled, sprintHeld, sprintToggled);
        public bool CrouchActive => ResolveModifierActive(CrouchToggleEnabled, crouchHeld, crouchToggled);

        /// <summary>
        /// The crouch button as a raw hold, ignoring the crouch-toggle comfort setting.
        /// </summary>
        /// <remarks>
        /// Vertical locomotion wants the button, not the modifier. `CrouchActive` answers "is the
        /// player crouching", which under toggle mode is a latched state a tap flips — correct for
        /// a body pose, wrong for "hold to descend". Eric reported swim descent as far weaker than
        /// ascent even though the ruleset makes sink (1.2 m/s) FASTER than rise (1.0 m/s): the
        /// speeds were never the problem, the input was, because rise reads its action raw
        /// (`jumpAction.IsPressed()`) and sink went through the toggle-aware modifier.
        /// </remarks>
        public bool CrouchHeldRaw => crouchHeld;

        bool SprintToggleEnabled => comfortSettings != null && comfortSettings.SprintToggleEnabled;
        bool PlaceModifierToggleEnabled =>
            comfortSettings != null && comfortSettings.PlaceModifierToggleEnabled;
        bool CrouchToggleEnabled => comfortSettings != null && comfortSettings.CrouchToggleEnabled;

        // Sprint and crouch are locomotion modifiers, not world-editing actions, so they follow
        // the same availability rule as movement instead of AllowWorldInput. AllowWorldInput is
        // false whenever a menu holds focus — which is the entire title mini-world — where the
        // player can walk but previously could neither sprint nor crouch.
        bool LocomotionModifiersAllowed => !locomotionSuppressed;
        public TrackedPoseDriver HeadPoseDriver => headPoseDriver;
        public XRBodyTransformer BodyTransformer => bodyTransformer;
        public LocomotionMediator LocomotionMediator => locomotionMediator;
        public ContinuousMoveProvider ContinuousMoveProvider => continuousMoveProvider;
        public TeleportationProvider TeleportationProvider => teleportationProvider;
        public SnapTurnProvider SnapTurnProvider => snapTurnProvider;
        public ContinuousTurnProvider ContinuousTurnProvider => continuousTurnProvider;
        public GravityProvider GravityProvider => gravityProvider;
        public JumpProvider JumpProvider => jumpProvider;
        public CharacterController CharacterController => characterController;
        public BlockiverseComfortSettings ComfortSettings => comfortSettings;
        public BlockiverseSwimProvider SwimProvider => swimProvider;

        // True while the swim provider owns vertical motion. Crouch and jump both change meaning
        // here, so this is read by the rig itself rather than only by the provider.
        public bool SwimLocomotionActive => swimProvider != null && swimProvider.IsSwimming;
        public BlockiverseAudioCuePlayer AudioCuePlayer => audioCuePlayer;
        public BlockiverseControllerHaptics LeftControllerHaptics => leftControllerHaptics;
        public BlockiverseControllerHaptics RightControllerHaptics => rightControllerHaptics;
        public BlockiverseFoveatedRenderingController FoveatedRenderingController => foveatedRenderingController;
        public XRRayInteractor LeftInteractionRay => leftInteractionRay;
        public XRRayInteractor RightInteractionRay => rightInteractionRay;
        public BlockiverseControllerRole ActiveMoveHand => GetMoveHand();
        public BlockiverseControllerRole ActiveTurnHand => GetTurnHand();
        public BlockiverseControllerRole ActiveToolHand => GetToolHand();
        public float MoveInputMagnitude => MoveInput.magnitude;

        // The raw stick vector, not just its length. The shore-climb assist needs a heading to
        // know which column to look at, and it only ever fires while the player is actively
        // pushing toward the bank.
        public Vector2 MoveInput
        {
            get
            {
                string mapName = GetControllerMapName(GetMoveHand());
                return TryFindAction(mapName, BlockiverseInputActionNames.Move, out InputAction moveAction)
                    ? moveAction.ReadValue<Vector2>()
                    : Vector2.zero;
            }
        }
        public bool LocomotionSuppressed
        {
            get => locomotionSuppressed;
            set
            {
                if (locomotionSuppressed == value)
                    return;

                locomotionSuppressed = value;
                comfortApplied = false;
                ApplyComfortSettingsToProviders();
                UpdateTurnProviderEnabledState();
            }
        }
        public bool TurnWithBothHands
        {
            get => turnWithBothHands;
            set
            {
                if (turnWithBothHands == value)
                    return;

                turnWithBothHands = value;
                comfortApplied = false;
                ConfigureXriProviderInputs();
            }
        }
        public bool CreativeFlightLocomotionActive
        {
            get => creativeFlightLocomotionActive;
            set
            {
                if (creativeFlightLocomotionActive == value)
                    return;

                creativeFlightLocomotionActive = value;
                comfortApplied = false;
                ApplyComfortSettingsToProviders();
                UpdateTurnProviderEnabledState();
            }
        }

        public bool TryGetActiveInteractionRayPose(out Vector3 origin, out Vector3 direction)
        {
            return TryGetInteractionRayPose(GetToolHand(), out origin, out direction);
        }

        public bool TryGetInteractionRayPose(
            BlockiverseControllerRole hand,
            out Vector3 origin,
            out Vector3 direction)
        {
            origin = default;
            direction = default;

            XRRayInteractor interactionRay = hand == BlockiverseControllerRole.Left
                ? leftInteractionRay
                : rightInteractionRay;
            Transform rayOrigin = interactionRay != null && interactionRay.rayOriginTransform != null
                ? interactionRay.rayOriginTransform
                : interactionRay != null
                    ? interactionRay.transform
                    : null;
            if (rayOrigin == null)
                return false;

            origin = rayOrigin.position;
            direction = rayOrigin.forward;
            return direction.sqrMagnitude > Mathf.Epsilon;
        }

        public void RefreshLocomotionProviderState()
        {
            comfortApplied = false;
            ApplyComfortSettingsToProviders();
            UpdateTurnProviderEnabledState();
        }

        public void Configure(InputActionAsset actions)
        {
            inputActions = actions;
            ConfigureXriProviderInputs();
            BlockiverseXrUiInputConfigurator.ConfigureAll(inputActions, GetToolHand());

            if (isActiveAndEnabled)
                inputActions?.Enable();
        }

        public void ConfigureLocomotion(
            TeleportationProvider teleport,
            SnapTurnProvider snapTurn,
            BlockiverseHeightReset reset,
            ContinuousMoveProvider continuousMove = null,
            LocomotionMediator mediator = null,
            XRBodyTransformer transformer = null,
            BlockiverseComfortSettings settings = null,
            ContinuousTurnProvider continuousTurn = null,
            GravityProvider gravity = null,
            JumpProvider jump = null,
            CharacterController controller = null)
        {
            teleportationProvider = teleport;
            snapTurnProvider = snapTurn;
            heightReset = reset;
            continuousMoveProvider = continuousMove != null ? continuousMove : continuousMoveProvider;
            locomotionMediator = mediator != null ? mediator : locomotionMediator;
            bodyTransformer = transformer != null ? transformer : bodyTransformer;
            comfortSettings = settings != null ? settings : comfortSettings;
            continuousTurnProvider = continuousTurn != null ? continuousTurn : continuousTurnProvider;
            gravityProvider = gravity != null ? gravity : gravityProvider;
            jumpProvider = jump != null ? jump : jumpProvider;
            characterController = controller != null ? controller : characterController;
            ConfigureXriLocomotionProviders();
        }

        public void ConfigureTeleportFeedback(BlockiverseAudioCuePlayer cuePlayer)
        {
            audioCuePlayer = cuePlayer;
            SubscribeLocomotionFeedback();
        }

        public void ConfigureHeadPoseDriver(TrackedPoseDriver driver)
        {
            headPoseDriver = driver;
            ConfigureHeadPoseDriverActions(headPoseDriver);
            EnableHeadPoseDriver();
        }

        public void RepairRuntimeTracking()
        {
            EnsureHeadPoseDriver();
            EnsureControllerPoseDrivers();
            RemoveStaleControllerRayOrigins();
            EnsureXriLocomotionProviders();
            EnsureRayInteractorInputs();
            EnsureFoveatedRenderingController();
            BlockiverseXrUiInputConfigurator.ConfigureAll(inputActions, GetToolHand());
        }

        public InputAction FindAction(string mapName, string actionName)
        {
            if (inputActions == null)
                throw new InvalidOperationException("Blockiverse input actions are not assigned.");

            InputActionMap map = inputActions.FindActionMap(mapName, throwIfNotFound: true);
            return map.FindAction(actionName, throwIfNotFound: true);
        }

        public InputAction ResolveJumpActionForCurrentControls()
        {
            return TryFindAction(
                GetControllerMapName(GetDominantHand()),
                BlockiverseInputActionNames.PrimaryButton,
                out InputAction jumpAction)
                    ? jumpAction
                    : null;
        }

        public static void ConfigureHeadPoseDriverActions(TrackedPoseDriver driver)
        {
            ConfigurePoseDriverActions(driver, HeadPositionPath, HeadRotationPath, HeadTrackingStatePath);
        }

        public static void ConfigureControllerPoseDriverActions(TrackedPoseDriver driver, BlockiverseControllerRole role)
        {
            if (role == BlockiverseControllerRole.Left)
            {
                ConfigurePoseDriverActions(
                    driver,
                    LeftControllerPositionPath,
                    LeftControllerRotationPath,
                    LeftControllerTrackingStatePath);
            }
            else
            {
                ConfigurePoseDriverActions(
                    driver,
                    RightControllerPositionPath,
                    RightControllerRotationPath,
                    RightControllerTrackingStatePath);
            }
        }

        public static void ConfigurePoseDriverActionReferences(
            TrackedPoseDriver driver,
            InputActionReference positionReference,
            InputActionReference rotationReference,
            InputActionReference trackingStateReference)
        {
            if (driver == null)
                return;

            if (positionReference != null && driver.positionInput.reference != positionReference)
                driver.positionInput = new InputActionProperty(positionReference);

            if (rotationReference != null && driver.rotationInput.reference != rotationReference)
                driver.rotationInput = new InputActionProperty(rotationReference);

            if (trackingStateReference != null && driver.trackingStateInput.reference != trackingStateReference)
                driver.trackingStateInput = new InputActionProperty(trackingStateReference);

            driver.ignoreTrackingState = false;
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            BlockiverseTrackedPoseDriverLifecycle.Ensure(driver);
        }

        /// <summary>
        /// Resolves whether a locomotion modifier (sprint or crouch) is active for this frame.
        /// Click-and-hold is the default and is active only while the button is held; toggle mode
        /// flips on each click and ignores how long the button is held. The two modes are
        /// mutually exclusive so a hold can never silently leave the modifier latched on.
        /// </summary>
        public static bool ResolveModifierActive(bool toggleModeEnabled, bool held, bool toggled) =>
            toggleModeEnabled ? toggled : held;

        public static float ResolveSprintMoveSpeed(float baseMoveSpeed, bool sprintActive) =>
            sprintActive ? baseMoveSpeed * SprintMoveMultiplier : baseMoveSpeed;

        /// <summary>
        /// Horizontal move speed for the frame. Flying cruises at the land sprint speed and
        /// sprints to twice that; on the ground it is the ordinary walk/sprint pair.
        /// </summary>
        public static float ResolveHorizontalMoveSpeed(float baseMoveSpeed, bool sprintActive, bool flightActive)
        {
            if (!flightActive)
                return ResolveSprintMoveSpeed(baseMoveSpeed, sprintActive);

            return baseMoveSpeed
                * (sprintActive ? FlightSprintMoveMultiplier : FlightCruiseMoveMultiplier);
        }

        static void ConfigurePoseDriverActions(
            TrackedPoseDriver driver,
            string positionPath,
            string rotationPath,
            string trackingStatePath)
        {
            if (driver == null)
                return;

            if (!HasBinding(driver.positionInput, positionPath))
            {
                driver.positionInput = new InputActionProperty(
                    new InputAction("Position", binding: positionPath, expectedControlType: "Vector3"));
            }

            if (!HasBinding(driver.rotationInput, rotationPath))
            {
                driver.rotationInput = new InputActionProperty(
                    new InputAction("Rotation", binding: rotationPath, expectedControlType: "Quaternion"));
            }

            if (!HasBinding(driver.trackingStateInput, trackingStatePath))
            {
                driver.trackingStateInput = new InputActionProperty(
                    new InputAction("Tracking State", binding: trackingStatePath, expectedControlType: "Integer"));
            }

            driver.ignoreTrackingState = false;
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            BlockiverseTrackedPoseDriverLifecycle.Ensure(driver);
        }

        void Awake()
        {
            RepairRuntimeTracking();
        }

        void Start()
        {
            // LocomotionMediator initializes its transformer during Awake; repair once more after
            // all Awake calls so GravityProvider and JumpProvider see a complete mediator.
            RepairRuntimeTracking();
        }

        void OnEnable()
        {
            RepairRuntimeTracking();
            inputActions?.Enable();
            SubscribeLocomotionFeedback();
        }

        void OnDisable()
        {
            ClearTransientSprintState();
            ClearTransientCrouchState();
            UnsubscribeLocomotionFeedback();
            inputActions?.Disable();
            DisableTrackedPoseDrivers();
        }

        void OnDestroy()
        {
            ClearTransientSprintState();
            ClearTransientCrouchState();
            UnsubscribeLocomotionFeedback();
            inputActions?.Disable();
            DisableTrackedPoseDrivers();
        }

        void Update()
        {
            RefreshCachedActions();
            WireGaitMoveIntent();
            UpdateSprintInput();
            UpdateCrouchInput();
            ApplyCrouchState();
            ApplyComfortSettingsToProviders();
            UpdateTurnProviderEnabledState();
            UpdateMenu();
            UpdateScreensPressed();
            UpdateHotbar();
            UpdateCreativeBindings();
        }

        // The gait cycle advances on rig travel, but turns rotate the origin around the camera and
        // translate the rig without the player walking. Handing it the move-stick magnitude lets it
        // demand real locomotion intent. Lazy because the gait may be added at runtime by the
        // feedback components' fallbacks.
        void WireGaitMoveIntent()
        {
            if (wiredGaitCycle != null)
                return;

            wiredGaitCycle = GetComponent<BlockiverseGaitCycle>();

            if (wiredGaitCycle != null)
                wiredGaitCycle.MoveIntentOverride = () => MoveInputMagnitude;
        }

        void RefreshCachedActions()
        {
            BlockiverseControllerRole dominantHand = GetDominantHand();

            if (cachedActionAsset == inputActions &&
                cachedDominantHand == dominantHand)
            {
                return;
            }

            cachedActionAsset = inputActions;
            cachedDominantHand = dominantHand;

            string dominantMap = GetControllerMapName(dominantHand);
            string supportMap = GetControllerMapName(OppositeHand(dominantHand));

            TryFindAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, out cachedMenuAction);
            // Support grip. Formerly the Creative quick block menu's toggle; that panel duplicated
            // the catalog screen already reachable from the wrist menu, so it was removed and this
            // binding now opens the same gameplay-screens hub the wrist gesture does — a reliable,
            // tracking-independent way in if the gesture can't be read.
            TryFindAction(supportMap, BlockiverseInputActionNames.Activate, out cachedScreensAction);
            // The support hand's two face buttons: the only gameplay inputs the shipped
            // controller mapping leaves unclaimed, so the hotbar gets them without taking a
            // binding away from anything the player already relies on. Secondary is 'next'
            // because it sits further from the thumb's rest position, matching the way the
            // dominant hand's secondary carries the less-used half of its pair.
            TryFindAction(supportMap, BlockiverseInputActionNames.SecondaryButton, out cachedHotbarNextAction);
            TryFindAction(supportMap, BlockiverseInputActionNames.PrimaryButton, out cachedHotbarPreviousAction);
            TryFindAction(dominantMap, BlockiverseInputActionNames.Select, out cachedBreakAction);
            TryFindAction(dominantMap, BlockiverseInputActionNames.Activate, out cachedPlaceAction);
            // Its own action now, rather than the raw Secondary Button passthrough: B/Y carries
            // crouch and swim-down, and the pointer toggle moved to the dominant stick click.
            TryFindAction(dominantMap, BlockiverseInputActionNames.BlockEditingToggle, out cachedBlockEditingToggleAction);
            TryFindAction(dominantMap, BlockiverseInputActionNames.Crouch, out cachedCrouchAction);
            TryFindAction(supportMap, BlockiverseInputActionNames.Sprint, out cachedSprintAction);
        }

        void UpdateSprintInput()
        {
            if (!LocomotionModifiersAllowed || cachedSprintAction == null)
            {
                ClearTransientSprintState();
                sprintToggled = false;
                return;
            }

            // Dropping out of toggle mode must not leave the player latched into a sprint they
            // can no longer switch off.
            if (!SprintToggleEnabled)
                sprintToggled = false;
            else if (cachedSprintAction.WasPressedThisFrame())
                sprintToggled = !sprintToggled;

            sprintHeld = cachedSprintAction.IsPressed();
        }

        void ClearTransientSprintState()
        {
            sprintHeld = false;
        }

        void UpdateCrouchInput()
        {
            if (!LocomotionModifiersAllowed || cachedCrouchAction == null)
            {
                ClearTransientCrouchState();
                crouchToggled = false;
                return;
            }

            if (!CrouchToggleEnabled)
                crouchToggled = false;
            else if (cachedCrouchAction.WasPressedThisFrame())
                crouchToggled = !crouchToggled;

            crouchHeld = cachedCrouchAction.IsPressed();
        }

        // Crouch has to be physically real: shrink the collision capsule so the player fits a
        // one-block opening, and lower the view by the same amount so it reads as crouching.
        // Without this the crouch toggle changed only block-placement rules and looked broken.
        void ApplyCrouchState()
        {
            // While swimming OR flying, crouch is the descend input and nothing else: shrinking the
            // capsule and dropping the camera would move the VIEW for an input that is supposed to
            // move the BODY.
            //
            // Flight was missing from this guard, and Eric found it on device — descending in
            // creative flight also crouched him, so releasing the button stood him back up and
            // shifted his view every time he stopped descending. The ruleset carve-out
            // (voxel_survival_ruleset.md §5.6) names only swimming, but it states its reason:
            // crouch's height change is suppressed "because its only meaning there is 'go down'".
            // Airborne, that reason holds exactly as well, so this follows the rule's principle
            // rather than contradicting its letter.
            //
            // Known trade, made deliberately: IsFlightActive is a persistent mode, not a descent,
            // so this also removes crouch while merely hovering — you cannot shrink the capsule to
            // fit a gap mid-flight. That costs a rare manoeuvre; leaving it in costs a view shift
            // on every descent.
            bool crouching = CrouchActive && !SwimLocomotionActive && !CreativeFlightLocomotionActive;
            bool realHeight = comfortSettings != null && comfortSettings.RealPlayerHeightEnabled;

            if (playerBodyManipulator != null)
            {
                playerBodyManipulator.Crouching = crouching;
                // Picked up live so the settings toggle applies without a restart.
                playerBodyManipulator.UseRealPlayerHeight = realHeight;
            }

            XROrigin origin = GetComponent<XROrigin>();
            Transform cameraOffset = origin != null && origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform
                : null;
            if (cameraOffset == null)
                return;

            float standingHeight = playerBodyManipulator != null
                ? playerBodyManipulator.StandingCapsuleHeightMeters
                : BlockiversePlayerBodyManipulator.DefaultStandingCapsuleHeight;
            float crouchHeight = playerBodyManipulator != null
                ? playerBodyManipulator.CrouchCapsuleHeightMeters
                : BlockiversePlayerBodyManipulator.DefaultCrouchCapsuleHeight;
            float targetDrop = crouching ? Mathf.Max(0.0f, standingHeight - crouchHeight) : 0.0f;

            if (Mathf.Approximately(targetDrop, appliedCrouchCameraDrop))
                return;

            // Ease the view change; an instant vertical jump is uncomfortable in VR.
            float smoothed = Mathf.MoveTowards(
                appliedCrouchCameraDrop,
                targetDrop,
                Mathf.Max(0.01f, Time.deltaTime) * CrouchCameraDropMetersPerSecond);

            Vector3 localPosition = cameraOffset.localPosition;
            localPosition.y += appliedCrouchCameraDrop - smoothed;
            cameraOffset.localPosition = localPosition;
            appliedCrouchCameraDrop = smoothed;
        }

        void ClearTransientCrouchState()
        {
            crouchHeld = false;
        }

        void UpdateMenu()
        {
            if (cachedMenuAction != null && cachedMenuAction.WasPressedThisFrame())
                menuPressed?.Invoke();
        }

        void UpdateScreensPressed()
        {
            if (cachedScreensAction != null && cachedScreensAction.WasPressedThisFrame())
                screensPressed?.Invoke();
        }

        // Gated on AllowWorldInput so a menu or modal that owns the ray cannot also be cycling
        // the held item behind it — the same gate the place binding uses below.
        void UpdateHotbar()
        {
            if (!BlockiverseRuntimeState.AllowWorldInput)
                return;

            if (cachedHotbarNextAction != null && cachedHotbarNextAction.WasPressedThisFrame())
                hotbarNextPressed?.Invoke();

            if (cachedHotbarPreviousAction != null && cachedHotbarPreviousAction.WasPressedThisFrame())
                hotbarPreviousPressed?.Invoke();
        }

        void UpdateCreativeBindings()
        {
            if (!BlockiverseRuntimeState.AllowWorldInput)
            {
                // Menus swallow the grip, so a held modifier would otherwise persist across the
                // whole menu session and the trigger would still be in place mode on return.
                placeModifierHeld = false;

                if (cachedBreakAction != null && cachedBreakAction.WasPressedThisFrame())
                    breakPressed?.Invoke();

                if (cachedBreakAction != null && cachedBreakAction.WasReleasedThisFrame())
                    breakReleased?.Invoke();

                return;
            }

            // The grip is a MODIFIER now, not an action. Resolve it BEFORE the trigger events fire
            // below — a listener on breakPressed reads PlaceModifierActive to decide break vs place,
            // and a same-frame grip squeeze must be visible to that read, not lag a frame behind it.
            if (cachedPlaceAction != null)
            {
                if (cachedPlaceAction.WasPressedThisFrame() && PlaceModifierToggleEnabled)
                    placeModifierToggled = !placeModifierToggled;

                placeModifierHeld = cachedPlaceAction.IsPressed();
            }
            else
            {
                placeModifierHeld = false;
            }

            // Leaving toggle mode must not strand the player latched into place mode with no
            // button that turns it off — the same guard sprint needs for the same reason.
            if (!PlaceModifierToggleEnabled)
                placeModifierToggled = false;

            if (cachedBreakAction != null && cachedBreakAction.WasPressedThisFrame())
                breakPressed?.Invoke();

            if (cachedBreakAction != null && cachedBreakAction.WasReleasedThisFrame())
                breakReleased?.Invoke();

            // PlacePressed still fires so anything that wants the raw grip press keeps working,
            // after the modifier state above so its own listeners see the same-frame value too.
            if (cachedPlaceAction != null && cachedPlaceAction.WasPressedThisFrame())
                placePressed?.Invoke();

            if (cachedBlockEditingToggleAction != null && cachedBlockEditingToggleAction.WasPressedThisFrame())
                blockEditingTogglePressed?.Invoke();
        }

        bool TryFindAction(string mapName, string actionName, out InputAction action)
        {
            action = null;

            if (inputActions == null)
                return false;

            InputActionMap map = inputActions.FindActionMap(mapName, throwIfNotFound: false);
            action = map?.FindAction(actionName, throwIfNotFound: false);
            return action != null;
        }

        void EnsureHeadPoseDriver()
        {
            Camera camera = GetComponent<XROrigin>()?.Camera;

            if (camera == null)
                camera = GetComponentInChildren<Camera>(true);

            if (headPoseDriver == null)
            {
                if (camera != null)
                    headPoseDriver = camera.GetComponent<TrackedPoseDriver>();

                if (headPoseDriver == null)
                    headPoseDriver = GetComponentInChildren<TrackedPoseDriver>(true);

                if (headPoseDriver == null && camera != null)
                    headPoseDriver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            }

            ConfigureHeadPoseDriverActions(headPoseDriver);
            EnableHeadPoseDriver();
        }

        void EnsureFoveatedRenderingController()
        {
            if (foveatedRenderingController == null)
                foveatedRenderingController = GetComponent<BlockiverseFoveatedRenderingController>();

            if (foveatedRenderingController == null)
                foveatedRenderingController = gameObject.AddComponent<BlockiverseFoveatedRenderingController>();
        }

        void EnsureControllerPoseDrivers()
        {
            foreach (BlockiverseControllerAnchor anchor in GetComponentsInChildren<BlockiverseControllerAnchor>(true))
            {
                TrackedPoseDriver driver = anchor.GetComponent<TrackedPoseDriver>();

                if (driver == null)
                    driver = anchor.gameObject.AddComponent<TrackedPoseDriver>();

                ConfigureControllerPoseDriverActions(driver, anchor.Role);
                driver.enabled = true;
                anchor.Configure(anchor.Role, driver);
            }
        }

        void RemoveStaleControllerRayOrigins()
        {
            Transform cameraOffset = ResolveCameraOffset();

            if (cameraOffset == null)
                return;

            RemoveStaleChild(cameraOffset, LeftAimPoseName);
            RemoveStaleChild(cameraOffset, RightAimPoseName);
            RemoveStaleChild(cameraOffset, LeftRayOriginName);
            RemoveStaleChild(cameraOffset, RightRayOriginName);
        }

        Transform ResolveCameraOffset()
        {
            XROrigin origin = GetComponent<XROrigin>();

            if (origin != null && origin.CameraFloorOffsetObject != null)
                return origin.CameraFloorOffsetObject.transform;

            Transform cameraOffset = transform.Find("Camera Offset");

            if (cameraOffset != null)
                return cameraOffset;

            Camera camera = origin != null && origin.Camera != null
                ? origin.Camera
                : GetComponentInChildren<Camera>(true);

            return camera != null && camera.transform.parent != null
                ? camera.transform.parent
                : null;
        }

        static void RemoveStaleChild(Transform parent, string childName)
        {
            Transform stale = parent != null ? parent.Find(childName) : null;

            if (stale != null)
            {
                if (Application.isPlaying)
                    Destroy(stale.gameObject);
                else
                    DestroyImmediate(stale.gameObject);
            }
        }

        void EnsureXriLocomotionProviders()
        {
            XROrigin origin = GetComponent<XROrigin>();

            if (origin == null)
                return;

            if (comfortSettings == null)
                comfortSettings = GetComponent<BlockiverseComfortSettings>();

            if (comfortSettings == null)
                comfortSettings = gameObject.AddComponent<BlockiverseComfortSettings>();

            if (bodyTransformer == null)
                bodyTransformer = GetComponent<XRBodyTransformer>();

            if (bodyTransformer == null)
                bodyTransformer = gameObject.AddComponent<XRBodyTransformer>();

            bodyTransformer.xrOrigin = origin;

            if (locomotionMediator == null)
                locomotionMediator = GetComponent<LocomotionMediator>();

            if (locomotionMediator == null)
                locomotionMediator = gameObject.AddComponent<LocomotionMediator>();

            if (Application.isPlaying)
                locomotionMediator.xrOrigin = origin;

            if (teleportationProvider == null)
                teleportationProvider = GetComponent<TeleportationProvider>();

            if (teleportationProvider == null)
                teleportationProvider = gameObject.AddComponent<TeleportationProvider>();

            if (continuousMoveProvider == null)
                continuousMoveProvider = GetComponent<ContinuousMoveProvider>();

            if (continuousMoveProvider == null)
                continuousMoveProvider = gameObject.AddComponent<ContinuousMoveProvider>();

            if (snapTurnProvider == null)
                snapTurnProvider = GetComponent<SnapTurnProvider>();

            if (snapTurnProvider == null)
                snapTurnProvider = gameObject.AddComponent<SnapTurnProvider>();

            if (continuousTurnProvider == null)
                continuousTurnProvider = GetComponent<ContinuousTurnProvider>();

            if (continuousTurnProvider == null)
                continuousTurnProvider = gameObject.AddComponent<ContinuousTurnProvider>();

            // A CharacterController gives the body a collision capsule so gravity/jumping land on the
            // voxel terrain; XRBodyTransformer auto-creates a CharacterControllerBodyManipulator when it
            // sees one. GravityProvider must exist before JumpProvider (JumpProvider disables itself in
            // Awake if it cannot find a GravityProvider), so add them in that order.
            if (characterController == null)
                characterController = GetComponent<CharacterController>();

            if (characterController == null)
                characterController = gameObject.AddComponent<CharacterController>();

            ConfigureCharacterController(characterController);

            // Own the collision capsule: XRI's stock manipulator would resize it to the tracked
            // camera height every move, making the player's size depend on who is wearing the
            // headset and silently undoing crouch.
            if (playerBodyManipulator == null)
            {
                playerBodyManipulator = ScriptableObject.CreateInstance<BlockiversePlayerBodyManipulator>();
                playerBodyManipulator.name = "Blockiverse Player Body Manipulator";
            }

            playerBodyManipulator.Configure(
                BlockiversePlayerBodyManipulator.DefaultStandingCapsuleHeight,
                BlockiversePlayerBodyManipulator.DefaultCrouchCapsuleHeight);
            playerBodyManipulator.UseRealPlayerHeight = comfortSettings != null && comfortSettings.RealPlayerHeightEnabled;
            bodyTransformer.constrainedBodyManipulator = playerBodyManipulator;

            if (gravityProvider == null)
                gravityProvider = GetComponent<GravityProvider>();

            if (gravityProvider == null)
                gravityProvider = gameObject.AddComponent<GravityProvider>();

            if (jumpProvider == null)
                jumpProvider = GetComponent<JumpProvider>();

            if (jumpProvider == null)
                jumpProvider = gameObject.AddComponent<JumpProvider>();

            // After GravityProvider, so the swim provider finds it and can register itself as an
            // IGravityController on enable. GravityProvider only auto-populates that list once,
            // from components already present, so a provider added later is never consulted.
            if (swimProvider == null)
                swimProvider = GetComponent<BlockiverseSwimProvider>();

            if (swimProvider == null)
                swimProvider = gameObject.AddComponent<BlockiverseSwimProvider>();

            if (heightReset == null)
                heightReset = GetComponent<BlockiverseHeightReset>();

            if (heightReset == null)
                heightReset = gameObject.AddComponent<BlockiverseHeightReset>();

            heightReset.Configure(origin, comfortSettings);

            ConfigureXriLocomotionProviders();
        }

        void ConfigureXriLocomotionProviders()
        {
            XROrigin origin = GetComponent<XROrigin>();

            if (bodyTransformer != null)
                bodyTransformer.xrOrigin = origin;

            if (Application.isPlaying && locomotionMediator != null)
                locomotionMediator.xrOrigin = origin;

            if (teleportationProvider != null)
            {
                teleportationProvider.mediator = locomotionMediator;
                teleportationProvider.delayTime = 0.0f;
            }

            if (continuousMoveProvider != null)
            {
                continuousMoveProvider.mediator = locomotionMediator;
                continuousMoveProvider.forwardSource = origin != null && origin.Camera != null
                    ? origin.Camera.transform
                    : transform;
                continuousMoveProvider.enableStrafe = true;
                continuousMoveProvider.enableFly = false;
            }

            if (snapTurnProvider != null)
            {
                snapTurnProvider.mediator = locomotionMediator;
                snapTurnProvider.enableTurnLeftRight = true;
                snapTurnProvider.enableTurnAround = comfortSettings == null || comfortSettings.SnapTurnAroundEnabled;
                snapTurnProvider.delayTime = 0.0f;
            }

            if (continuousTurnProvider != null)
            {
                continuousTurnProvider.mediator = locomotionMediator;
                continuousTurnProvider.turnSpeed = DefaultContinuousTurnSpeed;
            }

            if (gravityProvider != null)
            {
                gravityProvider.mediator = locomotionMediator;
                gravityProvider.enabled = true;
                gravityProvider.useGravity = true;
                gravityProvider.useLocalSpaceGravity = true;
                gravityProvider.sphereCastLayerMask = GetVoxelTerrainLayerMask();
                gravityProvider.sphereCastTriggerInteraction = QueryTriggerInteraction.Ignore;
            }

            if (swimProvider != null)
            {
                // LocomotionProvider.OnEnable disables itself when it has no mediator, and
                // AddComponent runs OnEnable before this line, so the mediator assignment has to be
                // followed by an explicit re-enable exactly as the jump provider gets one below.
                swimProvider.mediator = locomotionMediator;
                swimProvider.Configure(this, null, gravityProvider, GetComponent<BlockiverseGaitCycle>(), null);
                swimProvider.enabled = true;
            }

            if (jumpProvider != null)
            {
                jumpProvider.mediator = locomotionMediator;
                jumpProvider.jumpHeight = DefaultJumpHeightMeters;
                jumpProvider.disableGravityDuringJump = false;
                jumpProvider.unlimitedInAirJumps = false;
                jumpProvider.inAirJumpCount = 0;
            }

            ConfigureXriProviderInputs();
            SubscribeLocomotionFeedback();
            // Providers may be fresh instances; force a settings re-push past the change gate.
            comfortApplied = false;
            ApplyComfortSettingsToProviders();
        }

        void ConfigureXriProviderInputs()
        {
            if (continuousMoveProvider != null)
            {
                BlockiverseControllerRole moveHand = GetMoveHand();
                bool hasLeftMove = TryFindAction(
                    BlockiverseInputActionNames.LeftHandMap,
                    BlockiverseInputActionNames.Move,
                    out InputAction leftMove);
                bool hasRightMove = TryFindAction(
                    BlockiverseInputActionNames.RightHandMap,
                    BlockiverseInputActionNames.Move,
                    out InputAction rightMove);

                continuousMoveProvider.leftHandMoveInput = CreateVector2ActionReader(
                    continuousMoveProvider.leftHandMoveInput,
                    "Left Hand Move",
                    moveHand == BlockiverseControllerRole.Left && hasLeftMove
                        ? leftMove
                        : null);
                continuousMoveProvider.rightHandMoveInput = CreateVector2ActionReader(
                    continuousMoveProvider.rightHandMoveInput,
                    "Right Hand Move",
                    moveHand == BlockiverseControllerRole.Right && hasRightMove
                        ? rightMove
                        : null);
            }

            BlockiverseControllerRole turnHand = GetTurnHand();
            bool hasLeftTurn = TryFindAction(
                BlockiverseInputActionNames.LeftHandMap,
                BlockiverseInputActionNames.Turn,
                out InputAction leftTurn);
            bool hasRightTurn = TryFindAction(
                BlockiverseInputActionNames.RightHandMap,
                BlockiverseInputActionNames.Turn,
                out InputAction rightTurn);

            if (snapTurnProvider != null)
            {
                snapTurnProvider.leftHandTurnInput = CreateVector2ActionReader(
                    snapTurnProvider.leftHandTurnInput,
                    "Left Hand Snap Turn",
                    (turnWithBothHands || turnHand == BlockiverseControllerRole.Left) && hasLeftTurn
                        ? leftTurn
                        : null);
                snapTurnProvider.rightHandTurnInput = CreateVector2ActionReader(
                    snapTurnProvider.rightHandTurnInput,
                    "Right Hand Snap Turn",
                    (turnWithBothHands || turnHand == BlockiverseControllerRole.Right) && hasRightTurn
                        ? rightTurn
                        : null);
            }

            if (continuousTurnProvider != null)
            {
                continuousTurnProvider.leftHandTurnInput = CreateVector2ActionReader(
                    continuousTurnProvider.leftHandTurnInput,
                    "Left Hand Smooth Turn",
                    (turnWithBothHands || turnHand == BlockiverseControllerRole.Left) && hasLeftTurn
                        ? leftTurn
                        : null);
                continuousTurnProvider.rightHandTurnInput = CreateVector2ActionReader(
                    continuousTurnProvider.rightHandTurnInput,
                    "Right Hand Smooth Turn",
                    (turnWithBothHands || turnHand == BlockiverseControllerRole.Right) && hasRightTurn
                        ? rightTurn
                        : null);
            }

            if (jumpProvider != null)
            {
                jumpProvider.jumpInput = CreateButtonActionReader(
                    jumpProvider.jumpInput,
                    "Jump",
                    ResolveJumpActionForCurrentControls());
            }

            BlockiverseXrUiInputConfigurator.ConfigureAll(inputActions, GetToolHand());
        }

        // Re-wire ray readers from the live InputActionAsset every run. These
        // XRInputButtonReaders are serialized on the ray interactors with embedded direct actions whose
        // map-owned bindings are lost on serialization, which is why UI clicks and teleport-select silently
        // fail. Pointing them at the live actions (InputActionReference) restores the bindings at runtime.
        void EnsureRayInteractorInputs()
        {
            leftInteractionRay = null;
            rightInteractionRay = null;
            RemoveStaleControllerRayOrigins();

            foreach (BlockiverseLocomotionRayMediator rayMediator in GetComponentsInChildren<BlockiverseLocomotionRayMediator>(true))
            {
                string mapName = GetControllerMapName(rayMediator.Hand);
                Transform rayOrigin = EnsureControllerRayOrigin(rayMediator.transform, rayMediator.Hand, mapName);
                BlockiverseControllerAnchor anchor = rayMediator.GetComponent<BlockiverseControllerAnchor>();
                XRRayInteractor interactionRay = rayMediator.InteractionRay;

                if (interactionRay != null)
                {
                    CacheInteractionRay(rayMediator.Hand, interactionRay);
                    BlockiverseRayDefaults.ConfigureInteractionRay(
                        interactionRay,
                        rayOrigin,
                        GetVoxelInteractionRaycastLayerMask());
                    ConfigureRayLineVisual(interactionRay);
                    interactionRay.uiPressInput = CreateButtonActionReader(
                        interactionRay.uiPressInput,
                        "UI Press",
                        TryFindAction(mapName, BlockiverseInputActionNames.UiPress, out InputAction uiPress)
                            ? uiPress
                            : null);
                    interactionRay.uiScrollInput = CreateVector2ActionReader(
                        interactionRay.uiScrollInput,
                        "UI Scroll",
                        TryFindAction(mapName, BlockiverseInputActionNames.UiScroll, out InputAction uiScroll)
                            ? uiScroll
                            : null);
                }

                XRRayInteractor teleportRay = rayMediator.TeleportRay;

                if (teleportRay != null)
                {
                    // Includes fluid: the teleport ray stops at the water surface and lands the
                    // player treading, instead of passing through to the seabed. Without water in
                    // this mask, XRRayInteractor breaks at the first hit with no registered
                    // interactable and teleporting anywhere near water fails outright.
                    BlockiverseRayDefaults.ConfigureTeleportRay(
                        teleportRay,
                        rayOrigin,
                        GetVoxelTargetingLayerMask());
                    ConfigureRayLineVisual(teleportRay);
                    teleportRay.selectInput = CreateButtonActionReader(
                        teleportRay.selectInput,
                        "Teleport Select",
                        TryFindAction(mapName, BlockiverseInputActionNames.TeleportSelect, out InputAction teleportSelect)
                            ? teleportSelect
                            : null);
                }

                rayMediator.Configure(this, comfortSettings, interactionRay, teleportRay, rayMediator.Hand, anchor);
            }
        }

        // The controller transform rides the OpenXR grip pose; Meta's system pointer rides the aim
        // pose. The ray origin is a controller child that BlockiverseAimPoseRayOrigin keeps on the
        // aim pose (grip->aim is a rigid per-controller offset), with a fixed fallback offset until
        // the first tracked sample arrives.
        Transform EnsureControllerRayOrigin(Transform controller, BlockiverseControllerRole role, string mapName)
        {
            if (controller == null)
                return null;

            Transform rayOrigin = controller.Find(ControllerRayOriginName);

            if (rayOrigin == null)
            {
                GameObject rayOriginObject = new(ControllerRayOriginName);
                rayOriginObject.transform.SetParent(controller, false);
                rayOrigin = rayOriginObject.transform;
            }

            BlockiverseAimPoseRayOrigin aimOrigin = rayOrigin.GetComponent<BlockiverseAimPoseRayOrigin>();

            if (aimOrigin == null)
                aimOrigin = rayOrigin.gameObject.AddComponent<BlockiverseAimPoseRayOrigin>();

            if (!aimOrigin.UsingAimPose)
            {
                rayOrigin.SetLocalPositionAndRotation(
                    BlockiverseAimPoseRayOrigin.ResolveFallbackLocalPosition(role),
                    BlockiverseAimPoseRayOrigin.ResolveFallbackLocalRotation(role));
            }

            TryFindAction(mapName, BlockiverseInputActionNames.Position, out InputAction gripPosition);
            TryFindAction(mapName, BlockiverseInputActionNames.Rotation, out InputAction gripRotation);
            TryFindAction(mapName, BlockiverseInputActionNames.TrackingState, out InputAction trackingState);
            TryFindAction(mapName, BlockiverseInputActionNames.AimPosition, out InputAction aimPosition);
            TryFindAction(mapName, BlockiverseInputActionNames.AimRotation, out InputAction aimRotation);
            aimOrigin.Configure(role, gripPosition, gripRotation, trackingState, aimPosition, aimRotation);
            return rayOrigin;
        }

        static void ConfigureRayLineVisual(XRRayInteractor ray)
        {
            BlockiverseRayDefaults.ConfigureLineVisual(ray);
        }

        void CacheInteractionRay(BlockiverseControllerRole hand, XRRayInteractor interactionRay)
        {
            if (hand == BlockiverseControllerRole.Left)
                leftInteractionRay = interactionRay;
            else
                rightInteractionRay = interactionRay;
        }

        static string GetControllerMapName(BlockiverseControllerRole role)
        {
            return role == BlockiverseControllerRole.Left
                ? BlockiverseInputActionNames.LeftHandMap
                : BlockiverseInputActionNames.RightHandMap;
        }

        static BlockiverseControllerRole OppositeHand(BlockiverseControllerRole role) =>
            role == BlockiverseControllerRole.Left
                ? BlockiverseControllerRole.Right
                : BlockiverseControllerRole.Left;

        BlockiverseControllerRole GetDominantHand() =>
            comfortSettings != null ? comfortSettings.DominantHand : BlockiverseControllerRole.Right;

        BlockiverseControllerRole GetMoveHand()
        {
            BlockiverseControllerRole dominantHand = GetDominantHand();
            return OppositeHand(dominantHand);
        }

        BlockiverseControllerRole GetTurnHand() => GetDominantHand();

        BlockiverseControllerRole GetToolHand() => GetDominantHand();

        // Solid voxel terrain only — deliberately excludes fluid. This is the mask gravity uses for
        // its ground sphere-cast, and widening it to include fluid is what made the player walk on
        // water: GravityProvider resolves "grounded" with a PhysicsScene.SphereCast, and scene
        // queries ignore Collider.excludeLayers, so a fluid collider in this mask reads as ground.
        static LayerMask GetVoxelTerrainLayerMask()
        {
            if (cachedTerrainLayerMask.HasValue)
                return cachedTerrainLayerMask.Value;

            int terrainLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);
            cachedTerrainLayerMask = terrainLayer >= 0
                ? (LayerMask)(1 << terrainLayer)
                : (LayerMask)BlockiverseProject.InteractionLayerMask;
            return cachedTerrainLayerMask.Value;
        }

        // Terrain plus fluid: what rays are allowed to target. Block place/mine and drink/bucket
        // fill need to hit water, and the teleport ray needs to land on the surface rather than
        // punch through to the seabed.
        static LayerMask GetVoxelTargetingLayerMask()
        {
            if (cachedTargetingLayerMask.HasValue)
                return cachedTargetingLayerMask.Value;

            int fluidLayer = LayerMask.NameToLayer(BlockiverseProject.FluidLayerName);
            int fluidMask = fluidLayer >= 0 ? 1 << fluidLayer : BlockiverseProject.FluidLayerMask;
            cachedTargetingLayerMask = (LayerMask)(GetVoxelTerrainLayerMask().value | fluidMask);
            return cachedTargetingLayerMask.Value;
        }

        static LayerMask GetVrUiRaycastLayerMask()
        {
            return GetVoxelTargetingLayerMask();
        }

        // The INTERACTION ray only. Terrain + fluid + passable vegetation, so a plant can be aimed
        // at and harvested even though it lives on the passable layer and no longer contributes to
        // the chunk collider.
        //
        // Deliberately NOT folded into GetVrUiRaycastLayerMask: that helper also feeds the teleport
        // ray, and a teleport arc must pass THROUGH grass to land on the ground beneath it
        // (vegetation ruleset §4a.4). The two rays want different answers, which is the entire
        // reason this is a second method.
        static LayerMask GetVoxelInteractionRaycastLayerMask()
        {
            int passableLayer = LayerMask.NameToLayer(BlockiverseProject.PassableLayerName);
            int passableMask = passableLayer >= 0 ? 1 << passableLayer : BlockiverseProject.PassableLayerMask;
            return (LayerMask)(GetVoxelTargetingLayerMask().value | passableMask);
        }

        public static void ConfigureCharacterController(CharacterController controller)
        {
            if (controller == null)
                return;

            // The CharacterControllerBodyManipulator rewrites height/center each move from the camera, so
            // these are starting values; radius/slope/step define how the capsule clears voxel edges.
            controller.radius = 0.3f;
            controller.height = 1.6f;
            controller.center = new Vector3(0.0f, 0.8f, 0.0f);
            controller.slopeLimit = 45.0f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.02f;
            controller.minMoveDistance = 0.0f;
        }

        void ApplyComfortSettingsToProviders()
        {
            BlockiverseLocomotionMode mode = comfortSettings != null
                ? comfortSettings.LocomotionMode
                : BlockiverseLocomotionMode.Glide;
            bool smoothTurn = comfortSettings != null && comfortSettings.SmoothTurnEnabled;
            float moveSpeed = comfortSettings != null
                ? comfortSettings.ContinuousMoveSpeed
                : DefaultContinuousMoveSpeed;
            bool sprintActive = SprintActive;
            bool swimming = SwimLocomotionActive;
            bool flying = CreativeFlightLocomotionActive;
            float swimSpeedFactor = BlockiverseSwimMotion.HorizontalSpeedFactor(
                swimProvider != null ? swimProvider.State : SwimState.Dry,
                swimProvider != null ? swimProvider.Family : default,
                comfortSettings != null ? comfortSettings.SwimSpeedFactor : BlockiverseSwimMotion.DefaultSwimSpeedFactor);
            float resolvedMoveSpeed =
                ResolveHorizontalMoveSpeed(moveSpeed, sprintActive, flying) * swimSpeedFactor;
            float continuousTurnSpeed = comfortSettings != null
                ? comfortSettings.ContinuousTurnSpeed
                : DefaultContinuousTurnSpeed;
            float snapTurnDegrees = comfortSettings != null
                ? comfortSettings.SnapTurnDegrees
                : DefaultSnapTurnDegrees;
            bool snapTurnAroundEnabled = comfortSettings == null || comfortSettings.SnapTurnAroundEnabled;
            BlockiverseControllerRole dominantHand = GetDominantHand();
            bool controlHandChanged =
                !comfortApplied ||
                dominantHand != lastDominantHand ||
                turnWithBothHands != lastTurnWithBothHands;

            // Update runs hot; only push to the providers when a comfort value actually changed
            // (ConfigureXriLocomotionProviders resets comfortApplied so reconfigures re-push).
            if (comfortApplied &&
                mode == lastLocomotionMode &&
                smoothTurn == lastSmoothTurn &&
                Mathf.Approximately(moveSpeed, lastMoveSpeed) &&
                Mathf.Approximately(continuousTurnSpeed, lastContinuousTurnSpeed) &&
                Mathf.Approximately(snapTurnDegrees, lastSnapTurnDegrees) &&
                snapTurnAroundEnabled == lastSnapTurnAroundEnabled &&
                sprintActive == lastSprintActive &&
                swimming == lastSwimming &&
                flying == lastFlying &&
                Mathf.Approximately(swimSpeedFactor, lastSwimSpeedFactor) &&
                !controlHandChanged)
            {
                return;
            }

            comfortApplied = true;
            lastLocomotionMode = mode;
            lastSmoothTurn = smoothTurn;
            lastMoveSpeed = moveSpeed;
            lastContinuousTurnSpeed = continuousTurnSpeed;
            lastSnapTurnDegrees = snapTurnDegrees;
            lastSnapTurnAroundEnabled = snapTurnAroundEnabled;
            lastDominantHand = dominantHand;
            lastTurnWithBothHands = turnWithBothHands;
            lastSprintActive = sprintActive;
            lastSwimming = swimming;
            lastFlying = flying;
            lastSwimSpeedFactor = swimSpeedFactor;

            if (controlHandChanged)
            {
                ConfigureXriProviderInputs();
                cachedActionAsset = null;
            }

            bool isGlide = mode == BlockiverseLocomotionMode.Glide;
            bool locomotionAllowed = !locomotionSuppressed && !creativeFlightLocomotionActive;

            if (continuousMoveProvider != null)
            {
                continuousMoveProvider.moveSpeed = resolvedMoveSpeed;
                continuousMoveProvider.enabled = isGlide && locomotionAllowed;
            }

            if (snapTurnProvider != null)
            {
                snapTurnProvider.turnAmount = snapTurnDegrees;
                snapTurnProvider.enableTurnAround = snapTurnAroundEnabled;
            }

            if (continuousTurnProvider != null)
            {
                continuousTurnProvider.turnSpeed = continuousTurnSpeed;
            }

            UpdateTurnProviderEnabledState();

            if (gravityProvider != null)
            {
                gravityProvider.enabled = true;
                gravityProvider.useGravity = locomotionAllowed;
                gravityProvider.useLocalSpaceGravity = true;
                gravityProvider.sphereCastLayerMask = GetVoxelTerrainLayerMask();
                gravityProvider.sphereCastTriggerInteraction = QueryTriggerInteraction.Ignore;
            }

            // In Teleport mode teleport rays are active; in Glide mode they must stay inactive.
            // The teleport ray mediators read LocomotionMode directly, so no rig-level toggle is
            // needed here. Jump is only meaningful in Glide mode (Teleport mode teleports instead);
            // the jump reader itself is wired once in ConfigureXriProviderInputs, never per frame.
            // Jumping underwater is meaningless, so the provider is off while swimming -- but the
            // swim provider reads the jump ACTION directly rather than this component, so swimming
            // up still works in Teleport mode, where the jump provider is disabled anyway.
            if (jumpProvider != null)
                jumpProvider.enabled = isGlide && locomotionAllowed && !swimming;
        }

        void UpdateTurnProviderEnabledState()
        {
            bool smoothTurn = comfortSettings != null && comfortSettings.SmoothTurnEnabled;
            bool suppressTurnForUi = IsActiveTurnRayOverUi();
            bool enableSnapTurn = !locomotionSuppressed && !smoothTurn && !suppressTurnForUi;
            bool enableContinuousTurn = !locomotionSuppressed && smoothTurn && !suppressTurnForUi;

            if (snapTurnProvider != null && snapTurnProvider.enabled != enableSnapTurn)
                snapTurnProvider.enabled = enableSnapTurn;

            if (continuousTurnProvider != null && continuousTurnProvider.enabled != enableContinuousTurn)
                continuousTurnProvider.enabled = enableContinuousTurn;
        }

        bool IsActiveTurnRayOverUi()
        {
            XRRayInteractor interactionRay = GetToolHand() == BlockiverseControllerRole.Left
                ? leftInteractionRay
                : rightInteractionRay;

            return interactionRay != null && interactionRay.IsOverUIGameObject();
        }

        void SubscribeLocomotionFeedback()
        {
            if (!Application.isPlaying)
                return;

            ResolveControllerHaptics();

            if (teleportationProvider != null)
            {
                teleportEndedHandler ??= _ => PlayTeleportCue();
                teleportationProvider.locomotionEnded -= teleportEndedHandler;
                teleportationProvider.locomotionEnded += teleportEndedHandler;
            }

            if (snapTurnProvider != null)
            {
                snapTurnEndedHandler ??= _ => PlaySnapTurnHaptic();
                snapTurnProvider.locomotionEnded -= snapTurnEndedHandler;
                snapTurnProvider.locomotionEnded += snapTurnEndedHandler;
            }
        }

        void UnsubscribeLocomotionFeedback()
        {
            if (teleportationProvider != null && teleportEndedHandler != null)
                teleportationProvider.locomotionEnded -= teleportEndedHandler;
            if (snapTurnProvider != null && snapTurnEndedHandler != null)
                snapTurnProvider.locomotionEnded -= snapTurnEndedHandler;
        }

        void EnableHeadPoseDriver()
        {
            if (headPoseDriver == null)
                return;

            headPoseDriver.enabled = true;
        }

        void DisableTrackedPoseDrivers()
        {
            foreach (TrackedPoseDriver driver in GetComponentsInChildren<TrackedPoseDriver>(true))
                driver.enabled = false;

            if (headPoseDriver != null)
                headPoseDriver.enabled = false;
        }

        void PlayTeleportCue()
        {
            if (audioCuePlayer == null && Application.isPlaying)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            audioCuePlayer?.PlayCue(BlockiverseAudioCue.Footstep);
            leftControllerHaptics?.SendPattern(BlockiverseHapticPattern.TeleportLand);
            rightControllerHaptics?.SendPattern(BlockiverseHapticPattern.TeleportLand);
        }

        void PlaySnapTurnHaptic()
        {
            ResolveControllerHaptics();
            GetHapticsForRole(GetTurnHand())?.SendPattern(BlockiverseHapticPattern.SnapTurn);
        }

        void ResolveControllerHaptics()
        {
            if (leftControllerHaptics != null && rightControllerHaptics != null)
                return;

            foreach (BlockiverseControllerHaptics haptics in GetComponentsInChildren<BlockiverseControllerHaptics>(true))
            {
                if (haptics.Role == BlockiverseControllerRole.Left && leftControllerHaptics == null)
                    leftControllerHaptics = haptics;
                else if (haptics.Role == BlockiverseControllerRole.Right && rightControllerHaptics == null)
                    rightControllerHaptics = haptics;
            }
        }

        BlockiverseControllerHaptics GetHapticsForRole(BlockiverseControllerRole role)
        {
            ResolveControllerHaptics();
            return role == BlockiverseControllerRole.Left ? leftControllerHaptics : rightControllerHaptics;
        }

        static bool HasBinding(InputActionProperty property, string expectedPath)
        {
            InputAction action = property.action;

            if (action == null)
                return false;

            foreach (InputBinding binding in action.bindings)
            {
                if (binding.effectivePath == expectedPath || binding.path == expectedPath)
                    return true;
            }

            return false;
        }

        static XRInputValueReader<Vector2> CreateVector2ActionReader(
            XRInputValueReader<Vector2> currentReader,
            string name,
            InputAction action)
        {
            if (ReaderAlreadyTargetsAction(currentReader, action))
                return currentReader;

            if (action == null)
                return ReaderAlreadyUnused(currentReader) ? currentReader : CreateUnusedVector2Reader(name);

            // Reference the action rather than owning it (InputAction mode): the rig enables/disables
            // the whole InputActionAsset, so a reader must not toggle the action's lifecycle. Snap and
            // continuous turn both read the same Turn action, and disabling the inactive provider must
            // not disable that shared action for the active one.
            return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.InputActionReference)
            {
                inputActionReference = InputActionReference.Create(action)
            };
        }

        static XRInputValueReader<Vector2> CreateUnusedVector2Reader(string name)
        {
            return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.Unused);
        }

        static XRInputButtonReader CreateButtonActionReader(XRInputButtonReader currentReader, string name, InputAction action)
        {
            if (ReaderAlreadyTargetsAction(currentReader, action))
                return currentReader;

            if (action == null)
            {
                return ReaderAlreadyUnused(currentReader)
                    ? currentReader
                    : new XRInputButtonReader(name, inputSourceMode: XRInputButtonReader.InputSourceMode.Unused);
            }

            // Reference the live action instead of embedding it (see CreateVector2ActionReader): a direct
            // InputAction serialized into the prefab loses its map-owned bindings, so the press never reads.
            return new XRInputButtonReader(name, inputSourceMode: XRInputButtonReader.InputSourceMode.InputActionReference)
            {
                inputActionReferencePerformed = InputActionReference.Create(action)
            };
        }

        static bool ReaderAlreadyTargetsAction(XRInputValueReader<Vector2> reader, InputAction action)
        {
            return action != null &&
                   reader != null &&
                   reader.inputSourceMode == XRInputValueReader.InputSourceMode.InputActionReference &&
                   reader.inputActionReference != null &&
                   reader.inputActionReference.action == action;
        }

        static bool ReaderAlreadyTargetsAction(XRInputButtonReader reader, InputAction action)
        {
            return action != null &&
                   reader != null &&
                   reader.inputSourceMode == XRInputButtonReader.InputSourceMode.InputActionReference &&
                   reader.inputActionReferencePerformed != null &&
                   reader.inputActionReferencePerformed.action == action;
        }

        static bool ReaderAlreadyUnused(XRInputValueReader<Vector2> reader)
        {
            return reader != null &&
                   reader.inputSourceMode == XRInputValueReader.InputSourceMode.Unused;
        }

        static bool ReaderAlreadyUnused(XRInputButtonReader reader)
        {
            return reader != null &&
                   reader.inputSourceMode == XRInputButtonReader.InputSourceMode.Unused;
        }
    }
}

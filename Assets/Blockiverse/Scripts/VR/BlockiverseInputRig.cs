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
    public sealed class BlockiverseInputRig : MonoBehaviour
    {
        const float DefaultContinuousMoveSpeed = 1.8f;
        const float SprintMoveMultiplier = 2.2f;
        const float SprintClickToggleMaxSeconds = 0.25f;
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
        static readonly Quaternion ControllerRayOriginLocalRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);

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
        [SerializeField] CharacterController characterController;
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] BlockiverseHeightReset heightReset;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseControllerHaptics leftControllerHaptics;
        [SerializeField] BlockiverseControllerHaptics rightControllerHaptics;
        [SerializeField] BlockiverseFoveatedRenderingController foveatedRenderingController;
        [SerializeField] UnityEvent menuPressed = new();
        [SerializeField] UnityEvent quickMenuPressed = new();
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
        InputAction cachedQuickMenuAction;
        InputAction cachedBreakAction;
        InputAction cachedPlaceAction;
        InputAction cachedBlockEditingToggleAction;
        InputAction cachedSprintAction;
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
        bool locomotionSuppressed;
        bool turnWithBothHands;
        bool creativeFlightLocomotionActive;
        bool sprintToggled;
        bool sprintHeld;
        float sprintPressStartedAt = -1.0f;
        XRRayInteractor leftInteractionRay;
        XRRayInteractor rightInteractionRay;

        static LayerMask? cachedTerrainLayerMask;

        public InputActionAsset InputActions => inputActions;
        public UnityEvent MenuPressed => menuPressed;
        public UnityEvent QuickMenuPressed => quickMenuPressed;
        public UnityEvent BreakPressed => breakPressed;
        public UnityEvent BreakReleased => breakReleased;
        // Live held-state of the break input (hold-to-mine polls this as a release safety net).
        public bool IsBreakHeld => cachedBreakAction != null && cachedBreakAction.IsPressed();
        public UnityEvent PlacePressed => placePressed;
        public UnityEvent BlockEditingTogglePressed => blockEditingTogglePressed;
        public bool SprintActive => sprintToggled || sprintHeld;
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
        public BlockiverseAudioCuePlayer AudioCuePlayer => audioCuePlayer;
        public BlockiverseControllerHaptics LeftControllerHaptics => leftControllerHaptics;
        public BlockiverseControllerHaptics RightControllerHaptics => rightControllerHaptics;
        public BlockiverseFoveatedRenderingController FoveatedRenderingController => foveatedRenderingController;
        public XRRayInteractor LeftInteractionRay => leftInteractionRay;
        public XRRayInteractor RightInteractionRay => rightInteractionRay;
        public BlockiverseControllerRole ActiveMoveHand => GetMoveHand();
        public BlockiverseControllerRole ActiveTurnHand => GetTurnHand();
        public BlockiverseControllerRole ActiveToolHand => GetToolHand();
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

        public static bool ShouldToggleSprint(float pressDurationSeconds) =>
            pressDurationSeconds >= 0.0f && pressDurationSeconds <= SprintClickToggleMaxSeconds;

        public static float ResolveSprintMoveSpeed(float baseMoveSpeed, bool sprintActive) =>
            sprintActive ? baseMoveSpeed * SprintMoveMultiplier : baseMoveSpeed;

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
            UnsubscribeLocomotionFeedback();
            inputActions?.Disable();
            DisableTrackedPoseDrivers();
        }

        void OnDestroy()
        {
            ClearTransientSprintState();
            UnsubscribeLocomotionFeedback();
            inputActions?.Disable();
            DisableTrackedPoseDrivers();
        }

        void Update()
        {
            RefreshCachedActions();
            UpdateSprintInput(Time.unscaledTime);
            ApplyComfortSettingsToProviders();
            UpdateTurnProviderEnabledState();
            UpdateMenu();
            UpdateQuickMenu();
            UpdateCreativeBindings();
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
            TryFindAction(supportMap, BlockiverseInputActionNames.Activate, out cachedQuickMenuAction);
            TryFindAction(dominantMap, BlockiverseInputActionNames.Select, out cachedBreakAction);
            TryFindAction(dominantMap, BlockiverseInputActionNames.Activate, out cachedPlaceAction);
            TryFindAction(dominantMap, BlockiverseInputActionNames.SecondaryButton, out cachedBlockEditingToggleAction);
            TryFindAction(supportMap, BlockiverseInputActionNames.Sprint, out cachedSprintAction);
        }

        void UpdateSprintInput(float now)
        {
            if (!BlockiverseRuntimeState.AllowWorldInput || cachedSprintAction == null)
            {
                ClearTransientSprintState();
                return;
            }

            if (cachedSprintAction.WasPressedThisFrame())
            {
                sprintHeld = true;
                sprintPressStartedAt = now;
            }

            if (cachedSprintAction.IsPressed())
                sprintHeld = true;

            if (cachedSprintAction.WasReleasedThisFrame())
            {
                float pressDuration = sprintPressStartedAt >= 0.0f
                    ? now - sprintPressStartedAt
                    : float.PositiveInfinity;

                ClearTransientSprintState();
                if (ShouldToggleSprint(pressDuration))
                    sprintToggled = !sprintToggled;
            }
        }

        void ClearTransientSprintState()
        {
            sprintHeld = false;
            sprintPressStartedAt = -1.0f;
        }

        void UpdateMenu()
        {
            if (cachedMenuAction != null && cachedMenuAction.WasPressedThisFrame())
                menuPressed?.Invoke();
        }

        void UpdateQuickMenu()
        {
            if (cachedQuickMenuAction != null && cachedQuickMenuAction.WasPressedThisFrame())
                quickMenuPressed?.Invoke();
        }

        void UpdateCreativeBindings()
        {
            if (!BlockiverseRuntimeState.AllowWorldInput)
                return;

            if (cachedBreakAction != null && cachedBreakAction.WasPressedThisFrame())
                breakPressed?.Invoke();

            if (cachedBreakAction != null && cachedBreakAction.WasReleasedThisFrame())
                breakReleased?.Invoke();

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

            if (gravityProvider == null)
                gravityProvider = GetComponent<GravityProvider>();

            if (gravityProvider == null)
                gravityProvider = gameObject.AddComponent<GravityProvider>();

            if (jumpProvider == null)
                jumpProvider = GetComponent<JumpProvider>();

            if (jumpProvider == null)
                jumpProvider = gameObject.AddComponent<JumpProvider>();

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
                Transform rayOrigin = EnsureControllerRayOrigin(rayMediator.transform);
                BlockiverseControllerAnchor anchor = rayMediator.GetComponent<BlockiverseControllerAnchor>();
                XRRayInteractor interactionRay = rayMediator.InteractionRay;

                if (interactionRay != null)
                {
                    CacheInteractionRay(rayMediator.Hand, interactionRay);
                    BlockiverseRayDefaults.ConfigureInteractionRay(
                        interactionRay,
                        rayOrigin,
                        GetVoxelTerrainLayerMask());
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
                    BlockiverseRayDefaults.ConfigureTeleportRay(
                        teleportRay,
                        rayOrigin,
                        GetVoxelTerrainLayerMask());
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

        static Transform EnsureControllerRayOrigin(Transform controller)
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

            rayOrigin.SetLocalPositionAndRotation(Vector3.zero, ControllerRayOriginLocalRotation);
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
            float resolvedMoveSpeed = ResolveSprintMoveSpeed(moveSpeed, sprintActive);
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
            if (jumpProvider != null)
                jumpProvider.enabled = isGlide && locomotionAllowed;
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

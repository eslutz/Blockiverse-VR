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
        const float DefaultSnapTurnDegrees = 45.0f;
        const float DefaultContinuousTurnSpeed = 60.0f;
        const float DefaultJumpHeightMeters = 1.3f;
        const string HeadPositionPath = "<XRHMD>/centerEyePosition";
        const string HeadRotationPath = "<XRHMD>/centerEyeRotation";
        const string HeadTrackingStatePath = "<XRHMD>/trackingState";
        const string LeftControllerPositionPath = "<XRController>{LeftHand}/devicePosition";
        const string LeftControllerRotationPath = "<XRController>{LeftHand}/deviceRotation";
        const string LeftControllerTrackingStatePath = "<XRController>{LeftHand}/trackingState";
        const string LeftControllerPointerPositionPath = "<XRController>{LeftHand}/pointerPosition";
        const string LeftControllerPointerRotationPath = "<XRController>{LeftHand}/pointerRotation";
        const string RightControllerPositionPath = "<XRController>{RightHand}/devicePosition";
        const string RightControllerRotationPath = "<XRController>{RightHand}/deviceRotation";
        const string RightControllerTrackingStatePath = "<XRController>{RightHand}/trackingState";
        const string RightControllerPointerPositionPath = "<XRController>{RightHand}/pointerPosition";
        const string RightControllerPointerRotationPath = "<XRController>{RightHand}/pointerRotation";
        const string LeftAimPoseName = "Left Aim Pose";
        const string RightAimPoseName = "Right Aim Pose";

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
        [SerializeField] UnityEvent menuPressed = new();
        [SerializeField] UnityEvent quickMenuPressed = new();
        [SerializeField] UnityEvent breakPressed = new();
        [SerializeField] UnityEvent breakReleased = new();
        [SerializeField] UnityEvent placePressed = new();
        [SerializeField] UnityEvent undoPressed = new();
        [SerializeField] UnityEvent blockEditingTogglePressed = new();

        Action<LocomotionProvider> teleportEndedHandler;

        // Cached gameplay/hand actions — resolved once per InputActionAsset so the hot Update
        // poll avoids five string-keyed FindActionMap/FindAction lookups every frame.
        InputActionAsset cachedActionAsset;
        InputAction cachedMenuAction;
        InputAction cachedQuickMenuAction;
        InputAction cachedBreakAction;
        InputAction cachedPlaceAction;
        InputAction cachedUndoAction;
        InputAction cachedBlockEditingToggleAction;

        // Last comfort values pushed to the XRI providers. Provider fields — and especially the
        // jump reader, whose InputActionReference is a ScriptableObject instance — must only be
        // rebuilt when a setting actually changes, never per frame.
        bool comfortApplied;
        BlockiverseLocomotionMode lastLocomotionMode;
        bool lastSmoothTurn;
        float lastMoveSpeed;
        float lastSnapTurnDegrees;

        static LayerMask? cachedTerrainLayerMask;

        public InputActionAsset InputActions => inputActions;
        public UnityEvent MenuPressed => menuPressed;
        public UnityEvent QuickMenuPressed => quickMenuPressed;
        public UnityEvent BreakPressed => breakPressed;
        public UnityEvent BreakReleased => breakReleased;
        // Live held-state of the break input (hold-to-mine polls this as a release safety net).
        public bool IsBreakHeld => cachedBreakAction != null && cachedBreakAction.IsPressed();
        public UnityEvent PlacePressed => placePressed;
        public UnityEvent UndoPressed => undoPressed;
        public UnityEvent BlockEditingTogglePressed => blockEditingTogglePressed;
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

        public void Configure(InputActionAsset actions)
        {
            inputActions = actions;
            ConfigureXriProviderInputs();
            BlockiverseXrUiInputConfigurator.ConfigureAll(inputActions);

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
            EnsureControllerAimPoseDrivers();
            EnsureXriLocomotionProviders();
            EnsureRayInteractorInputs();
            BlockiverseXrUiInputConfigurator.ConfigureAll(inputActions);
        }

        public InputAction FindAction(string mapName, string actionName)
        {
            if (inputActions == null)
                throw new InvalidOperationException("Blockiverse input actions are not assigned.");

            InputActionMap map = inputActions.FindActionMap(mapName, throwIfNotFound: true);
            return map.FindAction(actionName, throwIfNotFound: true);
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

        public static void ConfigureControllerAimPoseDriverActions(TrackedPoseDriver driver, BlockiverseControllerRole role)
        {
            if (role == BlockiverseControllerRole.Left)
            {
                ConfigurePoseDriverActions(
                    driver,
                    LeftControllerPointerPositionPath,
                    LeftControllerPointerRotationPath,
                    LeftControllerTrackingStatePath);
            }
            else
            {
                ConfigurePoseDriverActions(
                    driver,
                    RightControllerPointerPositionPath,
                    RightControllerPointerRotationPath,
                    RightControllerTrackingStatePath);
            }
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
            SubscribeTeleportFeedback();
        }

        void OnDisable()
        {
            UnsubscribeTeleportFeedback();
            inputActions?.Disable();
            DisableTrackedPoseDrivers();
        }

        void OnDestroy()
        {
            UnsubscribeTeleportFeedback();
            inputActions?.Disable();
            DisableTrackedPoseDrivers();
        }

        void Update()
        {
            ApplyComfortSettingsToProviders();
            RefreshCachedActions();
            UpdateMenu();
            UpdateQuickMenu();
            UpdateCreativeBindings();
        }

        void RefreshCachedActions()
        {
            if (cachedActionAsset == inputActions)
                return;

            cachedActionAsset = inputActions;
            TryFindAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, out cachedMenuAction);
            TryFindAction(BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.Activate, out cachedQuickMenuAction);
            TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Select, out cachedBreakAction);
            TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Activate, out cachedPlaceAction);
            TryFindAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Undo, out cachedUndoAction);
            TryFindAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.BlockEditingToggle, out cachedBlockEditingToggleAction);
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
            if (cachedBreakAction != null && cachedBreakAction.WasPressedThisFrame())
                breakPressed?.Invoke();

            if (cachedBreakAction != null && cachedBreakAction.WasReleasedThisFrame())
                breakReleased?.Invoke();

            if (cachedPlaceAction != null && cachedPlaceAction.WasPressedThisFrame())
                placePressed?.Invoke();

            if (cachedUndoAction != null && cachedUndoAction.WasPressedThisFrame())
                undoPressed?.Invoke();

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

        void EnsureControllerAimPoseDrivers()
        {
            EnsureControllerAimPoseDriver(BlockiverseControllerRole.Left);
            EnsureControllerAimPoseDriver(BlockiverseControllerRole.Right);
        }

        Transform EnsureControllerAimPoseDriver(BlockiverseControllerRole role)
        {
            Transform parent = ResolveCameraOffsetTransform();
            string aimPoseName = GetAimPoseName(role);
            Transform aimPose = parent.Find(aimPoseName);

            if (aimPose == null)
            {
                var aimPoseObject = new GameObject(aimPoseName);
                aimPoseObject.transform.SetParent(parent, false);
                aimPose = aimPoseObject.transform;
            }

            TrackedPoseDriver driver = aimPose.GetComponent<TrackedPoseDriver>();

            if (driver == null)
                driver = aimPose.gameObject.AddComponent<TrackedPoseDriver>();

            ConfigureControllerAimPoseDriverActions(driver, role);
            driver.enabled = true;
            return aimPose;
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
                snapTurnProvider.enableTurnAround = false;
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
            SubscribeTeleportFeedback();
            // Providers may be fresh instances; force a settings re-push past the change gate.
            comfortApplied = false;
            ApplyComfortSettingsToProviders();
        }

        void ConfigureXriProviderInputs()
        {
            if (continuousMoveProvider != null)
            {
                continuousMoveProvider.leftHandMoveInput = CreateVector2ActionReader(
                    "Left Hand Move",
                    TryFindAction(BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.Move, out InputAction leftMove)
                        ? leftMove
                        : null);
                continuousMoveProvider.rightHandMoveInput = CreateUnusedVector2Reader("Right Hand Move");
            }

            bool hasRightTurn = TryFindAction(
                BlockiverseInputActionNames.RightHandMap,
                BlockiverseInputActionNames.Turn,
                out InputAction rightTurn);

            if (snapTurnProvider != null)
            {
                snapTurnProvider.leftHandTurnInput = CreateUnusedVector2Reader("Left Hand Snap Turn");
                snapTurnProvider.rightHandTurnInput = CreateVector2ActionReader(
                    "Right Hand Snap Turn",
                    hasRightTurn ? rightTurn : null);
            }

            if (continuousTurnProvider != null)
            {
                continuousTurnProvider.leftHandTurnInput = CreateUnusedVector2Reader("Left Hand Smooth Turn");
                continuousTurnProvider.rightHandTurnInput = CreateVector2ActionReader(
                    "Right Hand Smooth Turn",
                    hasRightTurn ? rightTurn : null);
            }

            if (jumpProvider != null)
            {
                jumpProvider.jumpInput = CreateButtonActionReader(
                    "Jump",
                    TryFindAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Jump, out InputAction jumpAction)
                        ? jumpAction
                        : null);
            }
        }

        // Re-wire ray readers from the live InputActionAsset every run. These
        // XRInputButtonReaders are serialized on the ray interactors with embedded direct actions whose
        // map-owned bindings are lost on serialization, which is why UI clicks and teleport-select silently
        // fail. Pointing them at the live actions (InputActionReference) restores the bindings at runtime.
        void EnsureRayInteractorInputs()
        {
            foreach (BlockiverseLocomotionRayMediator rayMediator in GetComponentsInChildren<BlockiverseLocomotionRayMediator>(true))
            {
                string mapName = GetControllerMapName(rayMediator.Hand);
                Transform aimPose = EnsureControllerAimPoseDriver(rayMediator.Hand);
                XRRayInteractor interactionRay = rayMediator.InteractionRay;

                if (interactionRay != null)
                {
                    interactionRay.rayOriginTransform = aimPose;
                    interactionRay.enableUIInteraction = true;
                    interactionRay.blockUIOnInteractableSelection = false;
                    interactionRay.uiPressInput = CreateButtonActionReader(
                        "UI Press",
                        TryFindAction(mapName, BlockiverseInputActionNames.UiPress, out InputAction uiPress)
                            ? uiPress
                            : null);
                    interactionRay.uiScrollInput = CreateVector2ActionReader(
                        "UI Scroll",
                        TryFindAction(mapName, BlockiverseInputActionNames.UiScroll, out InputAction uiScroll)
                            ? uiScroll
                            : null);
                }

                XRRayInteractor teleportRay = rayMediator.TeleportRay;

                if (teleportRay != null)
                {
                    teleportRay.rayOriginTransform = aimPose;
                    teleportRay.selectInput = CreateButtonActionReader(
                        "Teleport Select",
                        TryFindAction(mapName, BlockiverseInputActionNames.TeleportSelect, out InputAction teleportSelect)
                            ? teleportSelect
                            : null);
                }
            }
        }

        static string GetControllerMapName(BlockiverseControllerRole role)
        {
            return role == BlockiverseControllerRole.Left
                ? BlockiverseInputActionNames.LeftHandMap
                : BlockiverseInputActionNames.RightHandMap;
        }

        static string GetAimPoseName(BlockiverseControllerRole role)
        {
            return role == BlockiverseControllerRole.Left ? LeftAimPoseName : RightAimPoseName;
        }

        Transform ResolveCameraOffsetTransform()
        {
            XROrigin origin = GetComponent<XROrigin>();

            if (origin != null && origin.CameraFloorOffsetObject != null)
                return origin.CameraFloorOffsetObject.transform;

            Transform cameraOffset = transform.Find("Camera Offset");
            return cameraOffset != null ? cameraOffset : transform;
        }

        static LayerMask GetVoxelTerrainLayerMask()
        {
            if (cachedTerrainLayerMask.HasValue)
                return cachedTerrainLayerMask.Value;

            int terrainLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);
            cachedTerrainLayerMask = terrainLayer >= 0 ? (LayerMask)(1 << terrainLayer) : Physics.DefaultRaycastLayers;
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
            float snapTurnDegrees = comfortSettings != null
                ? comfortSettings.SnapTurnDegrees
                : DefaultSnapTurnDegrees;

            // Update runs hot; only push to the providers when a comfort value actually changed
            // (ConfigureXriLocomotionProviders resets comfortApplied so reconfigures re-push).
            if (comfortApplied &&
                mode == lastLocomotionMode &&
                smoothTurn == lastSmoothTurn &&
                Mathf.Approximately(moveSpeed, lastMoveSpeed) &&
                Mathf.Approximately(snapTurnDegrees, lastSnapTurnDegrees))
            {
                return;
            }

            comfortApplied = true;
            lastLocomotionMode = mode;
            lastSmoothTurn = smoothTurn;
            lastMoveSpeed = moveSpeed;
            lastSnapTurnDegrees = snapTurnDegrees;

            bool isGlide = mode == BlockiverseLocomotionMode.Glide;

            if (continuousMoveProvider != null)
            {
                continuousMoveProvider.moveSpeed = moveSpeed;
                continuousMoveProvider.enabled = isGlide;
            }

            if (snapTurnProvider != null)
            {
                snapTurnProvider.turnAmount = snapTurnDegrees;
                snapTurnProvider.enabled = !smoothTurn;
            }

            if (continuousTurnProvider != null)
                continuousTurnProvider.enabled = smoothTurn;

            if (gravityProvider != null)
            {
                gravityProvider.enabled = true;
                gravityProvider.useGravity = true;
                gravityProvider.useLocalSpaceGravity = true;
                gravityProvider.sphereCastLayerMask = GetVoxelTerrainLayerMask();
                gravityProvider.sphereCastTriggerInteraction = QueryTriggerInteraction.Ignore;
            }

            // In Teleport mode teleport rays are active; in Glide mode they must stay inactive.
            // The teleport ray mediators read LocomotionMode directly, so no rig-level toggle is
            // needed here. Jump is only meaningful in Glide mode (Teleport mode teleports instead);
            // the jump reader itself is wired once in ConfigureXriProviderInputs, never per frame.
            if (jumpProvider != null)
                jumpProvider.enabled = isGlide;
        }

        void SubscribeTeleportFeedback()
        {
            if (!Application.isPlaying || teleportationProvider == null)
                return;

            teleportEndedHandler ??= _ => PlayTeleportCue();
            teleportationProvider.locomotionEnded -= teleportEndedHandler;
            teleportationProvider.locomotionEnded += teleportEndedHandler;
        }

        void UnsubscribeTeleportFeedback()
        {
            if (teleportationProvider != null && teleportEndedHandler != null)
                teleportationProvider.locomotionEnded -= teleportEndedHandler;
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

        static XRInputValueReader<Vector2> CreateVector2ActionReader(string name, InputAction action)
        {
            if (action == null)
                return CreateUnusedVector2Reader(name);

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

        static XRInputButtonReader CreateButtonActionReader(string name, InputAction action)
        {
            if (action == null)
                return new XRInputButtonReader(name, inputSourceMode: XRInputButtonReader.InputSourceMode.Unused);

            // Reference the live action instead of embedding it (see CreateVector2ActionReader): a direct
            // InputAction serialized into the prefab loses its map-owned bindings, so the press never reads.
            return new XRInputButtonReader(name, inputSourceMode: XRInputButtonReader.InputSourceMode.InputActionReference)
            {
                inputActionReferencePerformed = InputActionReference.Create(action)
            };
        }
    }
}

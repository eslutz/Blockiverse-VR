using System;
using Blockiverse.Gameplay;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Unity.XR.CoreUtils;

namespace Blockiverse.VR
{
    [DefaultExecutionOrder(XRInteractionUpdateOrder.k_LocomotionProviders - 1)]
    public sealed class BlockiverseInputRig : MonoBehaviour
    {
        const float TeleportDistanceMeters = 2.0f;
        const float DefaultContinuousMoveSpeed = 1.8f;
        const float DefaultSnapTurnDegrees = 45.0f;
        const string HeadPositionPath = "<XRHMD>/centerEyePosition";
        const string HeadRotationPath = "<XRHMD>/centerEyeRotation";
        const string HeadTrackingStatePath = "<XRHMD>/trackingState";

        [SerializeField] InputActionAsset inputActions;
        [SerializeField] TrackedPoseDriver headPoseDriver;
        [SerializeField] XRBodyTransformer bodyTransformer;
        [SerializeField] LocomotionMediator locomotionMediator;
        [SerializeField] ContinuousMoveProvider continuousMoveProvider;
        [SerializeField] TeleportationProvider teleportationProvider;
        [SerializeField] SnapTurnProvider snapTurnProvider;
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] BlockiverseHeightReset heightReset;
        [SerializeField] BlockiverseVrUiPointer uiPointer;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] UnityEvent menuPressed = new();
        [SerializeField] UnityEvent quickMenuPressed = new();
        [SerializeField] UnityEvent breakPressed = new();
        [SerializeField] UnityEvent placePressed = new();
        [SerializeField] UnityEvent undoPressed = new();

        public InputActionAsset InputActions => inputActions;
        public UnityEvent MenuPressed => menuPressed;
        public UnityEvent QuickMenuPressed => quickMenuPressed;
        public UnityEvent BreakPressed => breakPressed;
        public UnityEvent PlacePressed => placePressed;
        public UnityEvent UndoPressed => undoPressed;
        public BlockiverseVrUiPointer UiPointer => uiPointer;
        public TrackedPoseDriver HeadPoseDriver => headPoseDriver;
        public XRBodyTransformer BodyTransformer => bodyTransformer;
        public LocomotionMediator LocomotionMediator => locomotionMediator;
        public ContinuousMoveProvider ContinuousMoveProvider => continuousMoveProvider;
        public TeleportationProvider TeleportationProvider => teleportationProvider;
        public SnapTurnProvider SnapTurnProvider => snapTurnProvider;

        public void Configure(InputActionAsset actions)
        {
            inputActions = actions;
            ConfigureXriProviderInputs();

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
            BlockiverseComfortSettings settings = null)
        {
            teleportationProvider = teleport;
            snapTurnProvider = snapTurn;
            heightReset = reset;
            continuousMoveProvider = continuousMove != null ? continuousMove : continuousMoveProvider;
            locomotionMediator = mediator != null ? mediator : locomotionMediator;
            bodyTransformer = transformer != null ? transformer : bodyTransformer;
            comfortSettings = settings != null ? settings : comfortSettings;
            ConfigureXriLocomotionProviders();
        }

        public void ConfigureTeleportFeedback(BlockiverseAudioCuePlayer cuePlayer)
        {
            audioCuePlayer = cuePlayer;
        }

        public void ConfigureUiPointer(BlockiverseVrUiPointer pointer)
        {
            uiPointer = pointer;
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
            EnsureXriLocomotionProviders();
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
            if (driver == null)
                return;

            if (!HasBinding(driver.positionInput, HeadPositionPath))
            {
                driver.positionInput = new InputActionProperty(
                    new InputAction(
                        "Head Position",
                        binding: HeadPositionPath,
                        expectedControlType: "Vector3"));
            }

            if (!HasBinding(driver.rotationInput, HeadRotationPath))
            {
                driver.rotationInput = new InputActionProperty(
                    new InputAction(
                        "Head Rotation",
                        binding: HeadRotationPath,
                        expectedControlType: "Quaternion"));
            }

            if (!HasBinding(driver.trackingStateInput, HeadTrackingStatePath))
            {
                driver.trackingStateInput = new InputActionProperty(
                    new InputAction(
                        "Head Tracking State",
                        binding: HeadTrackingStatePath,
                        expectedControlType: "Integer"));
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

        void OnEnable()
        {
            RepairRuntimeTracking();
            inputActions?.Enable();
        }

        void OnDisable()
        {
            inputActions?.Disable();
            DisableHeadPoseDriver();
        }

        void OnDestroy()
        {
            inputActions?.Disable();
            DisableHeadPoseDriver();
        }

        void Update()
        {
            ApplyComfortSettingsToProviders();
            UpdateTeleport();
            UpdateHeightReset();
            UpdateMenu();
            UpdateQuickMenu();
            UpdateCreativeBindings();
        }

        void UpdateTeleport()
        {
            if (teleportationProvider == null ||
                (comfortSettings != null && !comfortSettings.TeleportEnabled) ||
                !TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.TeleportMode, out InputAction teleportMode) ||
                !TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.TeleportSelect, out InputAction teleportSelect))
            {
                return;
            }

            if (!teleportMode.IsPressed() || !teleportSelect.WasPressedThisFrame())
                return;

            var request = new TeleportRequest
            {
                destinationPosition = GetDefaultTeleportDestination(),
                destinationRotation = transform.rotation,
                matchOrientation = MatchOrientation.None
            };

            if (teleportationProvider.QueueTeleportRequest(request))
                PlayTeleportCue();
        }

        void UpdateHeightReset()
        {
            if (heightReset == null ||
                !TryFindAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.HeightReset, out InputAction heightResetAction) ||
                !heightResetAction.WasPressedThisFrame())
            {
                return;
            }

            heightReset.ResetHeight();
        }

        void UpdateMenu()
        {
            if (!TryFindAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, out InputAction menuAction) ||
                !menuAction.WasPressedThisFrame())
            {
                return;
            }

            menuPressed?.Invoke();
        }

        void UpdateQuickMenu()
        {
            if (!TryFindAction(BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.Activate, out InputAction quickMenuAction) ||
                !quickMenuAction.WasPressedThisFrame())
            {
                return;
            }

            quickMenuPressed?.Invoke();
        }

        void UpdateCreativeBindings()
        {
            if (TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Select, out InputAction breakAction) &&
                breakAction.WasPressedThisFrame())
            {
                if (uiPointer != null && uiPointer.TryClick())
                    return;

                breakPressed?.Invoke();
            }

            if (TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Activate, out InputAction placeAction) &&
                placeAction.WasPressedThisFrame())
            {
                if (uiPointer != null && uiPointer.IsPointerOverUi)
                    return;

                placePressed?.Invoke();
            }

            if (TryFindAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Undo, out InputAction undoAction) &&
                undoAction.WasPressedThisFrame())
            {
                undoPressed?.Invoke();
            }
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

            ConfigureXriProviderInputs();
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

            if (snapTurnProvider != null)
            {
                snapTurnProvider.leftHandTurnInput = CreateUnusedVector2Reader("Left Hand Snap Turn");
                snapTurnProvider.rightHandTurnInput = CreateVector2ActionReader(
                    "Right Hand Snap Turn",
                    TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Turn, out InputAction rightTurn)
                        ? rightTurn
                        : null);
            }
        }

        void ApplyComfortSettingsToProviders()
        {
            if (continuousMoveProvider != null)
            {
                continuousMoveProvider.moveSpeed = comfortSettings != null
                    ? comfortSettings.ContinuousMoveSpeed
                    : DefaultContinuousMoveSpeed;
                continuousMoveProvider.enabled = comfortSettings == null || comfortSettings.ContinuousMoveEnabled;
            }

            if (snapTurnProvider != null)
            {
                snapTurnProvider.turnAmount = comfortSettings != null
                    ? comfortSettings.SnapTurnDegrees
                    : DefaultSnapTurnDegrees;
            }
        }

        void EnableHeadPoseDriver()
        {
            if (headPoseDriver == null)
                return;

            headPoseDriver.enabled = true;
        }

        void DisableHeadPoseDriver()
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

        Vector3 GetDefaultTeleportDestination()
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;

            Vector3 destination = transform.position + forward.normalized * TeleportDistanceMeters;
            destination.y = transform.position.y;
            return destination;
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

            return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.InputAction)
            {
                inputAction = action
            };
        }

        static XRInputValueReader<Vector2> CreateUnusedVector2Reader(string name)
        {
            return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.Unused);
        }
    }
}

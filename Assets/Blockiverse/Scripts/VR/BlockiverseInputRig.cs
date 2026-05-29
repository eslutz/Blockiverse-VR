using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;

namespace Blockiverse.VR
{
    public sealed class BlockiverseInputRig : MonoBehaviour
    {
        const float SnapTurnActivationThreshold = 0.75f;
        const float SnapTurnResetThreshold = 0.25f;
        const float TeleportDistanceMeters = 2.0f;
        const string HeadPositionPath = "<XRHMD>/centerEyePosition";
        const string HeadRotationPath = "<XRHMD>/centerEyeRotation";
        const string HeadTrackingStatePath = "<XRHMD>/trackingState";

        [SerializeField] InputActionAsset inputActions;
        [SerializeField] TrackedPoseDriver headPoseDriver;
        [SerializeField] BlockiverseHeadPoseTracker headPoseTracker;
        [SerializeField] BlockiverseContinuousMoveLocomotion continuousMoveLocomotion;
        [SerializeField] BlockiverseTeleportLocomotion teleportLocomotion;
        [SerializeField] BlockiverseSnapTurnLocomotion snapTurnLocomotion;
        [SerializeField] BlockiverseHeightReset heightReset;
        [SerializeField] BlockiverseVrUiPointer uiPointer;
        [SerializeField] UnityEvent menuPressed = new();
        [SerializeField] UnityEvent quickMenuPressed = new();
        [SerializeField] UnityEvent breakPressed = new();
        [SerializeField] UnityEvent placePressed = new();
        [SerializeField] UnityEvent undoPressed = new();

        bool snapTurnReady = true;

        public InputActionAsset InputActions => inputActions;
        public UnityEvent MenuPressed => menuPressed;
        public UnityEvent QuickMenuPressed => quickMenuPressed;
        public UnityEvent BreakPressed => breakPressed;
        public UnityEvent PlacePressed => placePressed;
        public UnityEvent UndoPressed => undoPressed;
        public BlockiverseVrUiPointer UiPointer => uiPointer;
        public TrackedPoseDriver HeadPoseDriver => headPoseDriver;
        public BlockiverseHeadPoseTracker HeadPoseTracker => headPoseTracker;

        public void Configure(InputActionAsset actions)
        {
            inputActions = actions;

            if (isActiveAndEnabled)
                inputActions?.Enable();
        }

        public void ConfigureLocomotion(
            BlockiverseTeleportLocomotion teleport,
            BlockiverseSnapTurnLocomotion snapTurn,
            BlockiverseHeightReset reset,
            BlockiverseContinuousMoveLocomotion continuousMove = null)
        {
            teleportLocomotion = teleport;
            snapTurnLocomotion = snapTurn;
            heightReset = reset;
            continuousMoveLocomotion = continuousMove != null ? continuousMove : continuousMoveLocomotion;
        }

        public void ConfigureUiPointer(BlockiverseVrUiPointer pointer)
        {
            uiPointer = pointer;
        }

        public void ConfigureHeadPoseDriver(TrackedPoseDriver driver)
        {
            headPoseDriver = driver;
            ConfigureHeadPoseDriverActions(headPoseDriver);
            DisableHeadPoseDriverForLifecycle();
        }

        public void ConfigureHeadPoseTracker(BlockiverseHeadPoseTracker tracker)
        {
            headPoseTracker = tracker;
            headPoseTracker?.RepairActions();
        }

        public void RepairRuntimeTracking()
        {
            EnsureHeadPoseDriver();
            EnsureContinuousMoveLocomotion();
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
            DisableHeadPoseDriverForLifecycle();
        }

        void OnDestroy()
        {
            inputActions?.Disable();
            DisableHeadPoseDriverForLifecycle();
        }

        void Update()
        {
            UpdateContinuousMove();
            UpdateSnapTurn();
            UpdateTeleport();
            UpdateHeightReset();
            UpdateMenu();
            UpdateQuickMenu();
            UpdateCreativeBindings();
        }

        void UpdateContinuousMove()
        {
            if (continuousMoveLocomotion == null)
                return;

            if (!TryFindAction(BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.Move, out InputAction move) &&
                !TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Move, out move))
            {
                return;
            }

            continuousMoveLocomotion.TryMove(move.ReadValue<Vector2>(), Time.deltaTime);
        }

        void UpdateSnapTurn()
        {
            if (snapTurnLocomotion == null ||
                !TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Turn, out InputAction turn))
            {
                return;
            }

            float horizontal = turn.ReadValue<Vector2>().x;
            float magnitude = Mathf.Abs(horizontal);

            if (magnitude <= SnapTurnResetThreshold)
            {
                snapTurnReady = true;
                return;
            }

            if (!snapTurnReady || magnitude < SnapTurnActivationThreshold)
                return;

            snapTurnLocomotion.ApplySnapTurn(horizontal > 0.0f ? 1 : -1);
            snapTurnReady = false;
        }

        void UpdateTeleport()
        {
            if (teleportLocomotion == null ||
                !TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.TeleportMode, out InputAction teleportMode) ||
                !TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.TeleportSelect, out InputAction teleportSelect))
            {
                return;
            }

            if (!teleportMode.IsPressed() || !teleportSelect.WasPressedThisFrame())
                return;

            teleportLocomotion.TryTeleportTo(GetDefaultTeleportDestination());
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
            }

            ConfigureHeadPoseDriverActions(headPoseDriver);
            DisableHeadPoseDriverForLifecycle();
            EnsureHeadPoseTracker(camera);
        }

        void EnsureContinuousMoveLocomotion()
        {
            if (continuousMoveLocomotion == null)
                continuousMoveLocomotion = GetComponent<BlockiverseContinuousMoveLocomotion>();

            if (continuousMoveLocomotion == null)
                continuousMoveLocomotion = gameObject.AddComponent<BlockiverseContinuousMoveLocomotion>();

            continuousMoveLocomotion.Configure(
                GetComponent<XROrigin>(),
                GetComponent<BlockiverseComfortSettings>());
        }

        void DisableHeadPoseDriverForLifecycle()
        {
            if (headPoseDriver == null)
                return;

            headPoseDriver.enabled = false;
        }

        void EnsureHeadPoseTracker(Camera camera)
        {
            if (headPoseTracker == null && camera != null)
                headPoseTracker = camera.GetComponent<BlockiverseHeadPoseTracker>();

            if (headPoseTracker == null)
                headPoseTracker = GetComponentInChildren<BlockiverseHeadPoseTracker>(true);

            if (headPoseTracker == null && camera != null)
                headPoseTracker = camera.gameObject.AddComponent<BlockiverseHeadPoseTracker>();

            headPoseTracker?.RepairActions();
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
    }
}

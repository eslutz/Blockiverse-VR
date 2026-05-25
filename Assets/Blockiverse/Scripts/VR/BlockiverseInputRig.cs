using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Blockiverse.VR
{
    public sealed class BlockiverseInputRig : MonoBehaviour
    {
        const float SnapTurnActivationThreshold = 0.75f;
        const float SnapTurnResetThreshold = 0.25f;
        const float TeleportDistanceMeters = 2.0f;

        [SerializeField] InputActionAsset inputActions;
        [SerializeField] BlockiverseTeleportLocomotion teleportLocomotion;
        [SerializeField] BlockiverseSnapTurnLocomotion snapTurnLocomotion;
        [SerializeField] BlockiverseHeightReset heightReset;
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

        public void Configure(InputActionAsset actions)
        {
            inputActions = actions;

            if (isActiveAndEnabled)
                inputActions?.Enable();
        }

        public void ConfigureLocomotion(
            BlockiverseTeleportLocomotion teleport,
            BlockiverseSnapTurnLocomotion snapTurn,
            BlockiverseHeightReset reset)
        {
            teleportLocomotion = teleport;
            snapTurnLocomotion = snapTurn;
            heightReset = reset;
        }

        public InputAction FindAction(string mapName, string actionName)
        {
            if (inputActions == null)
                throw new InvalidOperationException("Blockiverse input actions are not assigned.");

            InputActionMap map = inputActions.FindActionMap(mapName, throwIfNotFound: true);
            return map.FindAction(actionName, throwIfNotFound: true);
        }

        void OnEnable()
        {
            inputActions?.Enable();
        }

        void OnDisable()
        {
            inputActions?.Disable();
        }

        void Update()
        {
            UpdateSnapTurn();
            UpdateTeleport();
            UpdateHeightReset();
            UpdateMenu();
            UpdateQuickMenu();
            UpdateCreativeBindings();
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
                breakPressed?.Invoke();
            }

            if (TryFindAction(BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Activate, out InputAction placeAction) &&
                placeAction.WasPressedThisFrame())
            {
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

        Vector3 GetDefaultTeleportDestination()
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;

            Vector3 destination = transform.position + forward.normalized * TeleportDistanceMeters;
            destination.y = transform.position.y;
            return destination;
        }
    }
}

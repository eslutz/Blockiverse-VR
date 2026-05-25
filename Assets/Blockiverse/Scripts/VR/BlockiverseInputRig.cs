using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blockiverse.VR
{
    public sealed class BlockiverseInputRig : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;

        public InputActionAsset InputActions => inputActions;

        public void Configure(InputActionAsset actions)
        {
            inputActions = actions;
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
    }
}

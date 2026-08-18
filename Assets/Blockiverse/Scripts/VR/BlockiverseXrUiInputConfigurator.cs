using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Blockiverse.Core;

namespace Blockiverse.VR
{
    public static class BlockiverseXrUiInputConfigurator
    {
        public static void ConfigureAll(
            InputActionAsset inputActions,
            BlockiverseControllerRole activeToolHand = BlockiverseControllerRole.Right)
        {
            foreach (XRUIInputModule inputModule in UnityEngine.Object.FindObjectsByType<XRUIInputModule>(
                         UnityEngine.FindObjectsInactive.Include,
                         UnityEngine.FindObjectsSortMode.None))
            {
                Configure(inputModule, inputActions, activeToolHand);
            }
        }

        public static void Configure(
            XRUIInputModule inputModule,
            InputActionAsset inputActions,
            BlockiverseControllerRole activeToolHand = BlockiverseControllerRole.Right)
        {
            if (inputModule == null || inputActions == null)
                return;

            ConfigureInputModuleFlags(inputModule);

            string mapName = activeToolHand == BlockiverseControllerRole.Left
                ? BlockiverseInputActionNames.LeftHandMap
                : BlockiverseInputActionNames.RightHandMap;
            InputAction uiPress = FindAction(inputActions, mapName, BlockiverseInputActionNames.UiPress);
            InputAction uiScroll = FindAction(inputActions, mapName, BlockiverseInputActionNames.UiScroll);

            inputModule.leftClickAction = GetOrCreateReference(inputModule.leftClickAction, uiPress);
            inputModule.scrollWheelAction = GetOrCreateReference(inputModule.scrollWheelAction, uiScroll);
            // The ray interactor already dispatches pointer click from UI Press. Binding the
            // same action to Submit made one trigger pull fire the hovered (auto-selected)
            // Button twice: OnSubmit on press, OnPointerClick on release — selector arrows
            // advanced two options per click on device. Navigation is likewise left unbound
            // so the thumbstick cannot move uGUI selection under the ray.
            inputModule.navigateAction = null;
            inputModule.submitAction = null;
        }

        public static void Configure(
            XRUIInputModule inputModule,
            InputActionReference uiPressReference,
            InputActionReference uiScrollReference)
        {
            if (inputModule == null)
                return;

            ConfigureInputModuleFlags(inputModule);

            inputModule.leftClickAction = uiPressReference;
            inputModule.scrollWheelAction = uiScrollReference;
            // See the overload above: Submit/Navigate stay unbound so UI Press produces
            // exactly one click per trigger pull.
            inputModule.navigateAction = null;
            inputModule.submitAction = null;
        }

        static void ConfigureInputModuleFlags(XRUIInputModule inputModule)
        {
            inputModule.enableXRInput = true;
            inputModule.enableMouseInput = false;
            inputModule.enableTouchInput = false;
            inputModule.enableGamepadInput = false;
            inputModule.enableJoystickInput = false;
        }

        static InputAction FindAction(InputActionAsset inputActions, string mapName, string actionName)
        {
            return inputActions
                .FindActionMap(mapName, throwIfNotFound: false)
                ?.FindAction(actionName, throwIfNotFound: false);
        }

        static InputActionReference GetOrCreateReference(InputActionReference existingReference, InputAction action)
        {
            if (action == null)
                return null;

            return existingReference != null && existingReference.action == action
                ? existingReference
                : InputActionReference.Create(action);
        }
    }
}

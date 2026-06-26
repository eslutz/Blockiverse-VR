#pragma warning disable 0618
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;

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
            inputModule.navigateAction = GetOrCreateReference(inputModule.navigateAction, uiScroll);
            inputModule.submitAction = GetOrCreateReference(inputModule.submitAction, uiPress);
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
            inputModule.navigateAction = uiScrollReference;
            inputModule.submitAction = uiPressReference;
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

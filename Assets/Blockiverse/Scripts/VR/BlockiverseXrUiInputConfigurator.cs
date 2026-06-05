using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Blockiverse.VR
{
    public static class BlockiverseXrUiInputConfigurator
    {
        public static void ConfigureAll(InputActionAsset inputActions)
        {
            foreach (XRUIInputModule inputModule in UnityEngine.Object.FindObjectsByType<XRUIInputModule>(
                         UnityEngine.FindObjectsInactive.Include,
                         UnityEngine.FindObjectsSortMode.None))
            {
                Configure(inputModule, inputActions);
            }
        }

        public static void Configure(XRUIInputModule inputModule, InputActionAsset inputActions)
        {
            if (inputModule == null || inputActions == null)
                return;

            inputModule.enableXRInput = true;
            inputModule.enableMouseInput = false;
            inputModule.enableTouchInput = false;
            inputModule.enableGamepadInput = false;
            inputModule.enableJoystickInput = false;

            InputAction rightUiPress = FindAction(inputActions, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.UiPress);
            InputAction rightUiScroll = FindAction(inputActions, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.UiScroll);

            inputModule.leftClickAction = GetOrCreateReference(inputModule.leftClickAction, rightUiPress);
            inputModule.scrollWheelAction = GetOrCreateReference(inputModule.scrollWheelAction, rightUiScroll);
            inputModule.navigateAction = GetOrCreateReference(inputModule.navigateAction, rightUiScroll);
            inputModule.submitAction = GetOrCreateReference(inputModule.submitAction, rightUiPress);
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

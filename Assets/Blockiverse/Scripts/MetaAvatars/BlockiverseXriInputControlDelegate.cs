using Oculus.Avatar2;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Supplies trigger/grip/thumbstick state to the avatar hand-pose system straight from
    /// the Input System's XR controller devices, so avatar fingers curl with the real
    /// controls. Device-level reads (rather than the project's InputActionAsset) keep this
    /// independent of gameplay action maps and rebinding.
    /// </summary>
    public sealed class BlockiverseXriInputControlDelegate : OvrAvatarInputControlDelegate
    {
        public override bool GetInputControlState(out OvrAvatarInputControlState inputControlState)
        {
            inputControlState = default;
            inputControlState.type = GetControllerType();

            bool anyController = false;
            anyController |= TryReadController(XRController.leftHand, ref inputControlState.leftControllerState);
            anyController |= TryReadController(XRController.rightHand, ref inputControlState.rightControllerState);
            return anyController;
        }

        protected override CAPI.ovrAvatar2ControllerType GetControllerType()
        {
            // Quest 3/3S are the only target devices; avoid touching OvrAvatarManager from
            // here so the delegate stays valid before the manager singleton starts.
            return CAPI.ovrAvatar2ControllerType.Quest3;
        }

        static bool TryReadController(XRController controller, ref OvrAvatarControllerState state)
        {
            if (controller == null || !controller.added)
                return false;

            state.isActive = true;
            state.isVisible = true;
            state.indexTrigger = ReadAxis(controller, "trigger");
            state.handTrigger = ReadAxis(controller, "grip");

            var thumbstick = controller.TryGetChildControl<Vector2Control>("thumbstick");
            if (thumbstick != null)
            {
                UnityEngine.Vector2 value = thumbstick.ReadValue();
                state.joystickX = value.x;
                state.joystickY = value.y;
            }

            if (ReadButton(controller, "primarybutton"))
                state.buttonMask |= CAPI.ovrAvatar2Button.One;
            if (ReadButton(controller, "secondarybutton"))
                state.buttonMask |= CAPI.ovrAvatar2Button.Two;
            if (ReadButton(controller, "thumbstickclicked"))
                state.buttonMask |= CAPI.ovrAvatar2Button.Joystick;

            if (ReadButton(controller, "primarytouched"))
                state.touchMask |= CAPI.ovrAvatar2Touch.One;
            if (ReadButton(controller, "secondarytouched"))
                state.touchMask |= CAPI.ovrAvatar2Touch.Two;
            if (ReadButton(controller, "triggertouched"))
                state.touchMask |= CAPI.ovrAvatar2Touch.Index;
            if (ReadButton(controller, "thumbsticktouched"))
                state.touchMask |= CAPI.ovrAvatar2Touch.Joystick;

            return true;
        }

        static float ReadAxis(XRController controller, string controlName)
        {
            var control = controller.TryGetChildControl<AxisControl>(controlName);
            return control?.ReadValue() ?? 0.0f;
        }

        static bool ReadButton(XRController controller, string controlName)
        {
            var control = controller.TryGetChildControl<ButtonControl>(controlName);
            return control != null && control.isPressed;
        }
    }
}

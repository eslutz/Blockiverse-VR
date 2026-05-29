using UnityEngine;
using UnityEngine.InputSystem;

namespace Blockiverse.VR
{
    public sealed class BlockiverseHeadPoseTracker : MonoBehaviour
    {
        const string HeadPositionPath = "<XRHMD>/centerEyePosition";
        const string HeadRotationPath = "<XRHMD>/centerEyeRotation";
        const string HeadTrackingStatePath = "<XRHMD>/trackingState";

        InputAction positionAction;
        InputAction rotationAction;
        InputAction trackingStateAction;

        public InputAction PositionAction => positionAction;
        public InputAction RotationAction => rotationAction;
        public InputAction TrackingStateAction => trackingStateAction;

        public void RepairActions()
        {
            positionAction = RepairAction(positionAction, "Head Position", "Vector3", HeadPositionPath);
            rotationAction = RepairAction(rotationAction, "Head Rotation", "Quaternion", HeadRotationPath);
            trackingStateAction = RepairAction(trackingStateAction, "Head Tracking State", "Integer", HeadTrackingStatePath);
        }

        void OnEnable()
        {
            RepairActions();
            positionAction.Enable();
            rotationAction.Enable();
            trackingStateAction.Enable();
        }

        void OnDisable()
        {
            positionAction?.Disable();
            rotationAction?.Disable();
            trackingStateAction?.Disable();
        }

        void OnDestroy()
        {
            positionAction?.Dispose();
            rotationAction?.Dispose();
            trackingStateAction?.Dispose();
        }

        void Update()
        {
            RepairActions();

            bool positionTracked = HasResolvedControl(positionAction);
            bool rotationTracked = HasResolvedControl(rotationAction);

            if (HasResolvedControl(trackingStateAction))
            {
                int trackingState = trackingStateAction.ReadValue<int>();
                positionTracked = (trackingState & 1) != 0;
                rotationTracked = (trackingState & 2) != 0;
            }

            if (positionTracked)
                transform.localPosition = positionAction.ReadValue<Vector3>();

            if (rotationTracked)
                transform.localRotation = rotationAction.ReadValue<Quaternion>();
        }

        static InputAction RepairAction(InputAction action, string actionName, string expectedControlType, string bindingPath)
        {
            if (action != null && HasBinding(action, bindingPath))
                return action;

            bool wasEnabled = action?.enabled == true;
            action?.Dispose();

            action = new InputAction(
                actionName,
                InputActionType.PassThrough,
                bindingPath,
                expectedControlType: expectedControlType);

            if (wasEnabled)
                action.Enable();

            return action;
        }

        static bool HasBinding(InputAction action, string expectedPath)
        {
            if (action == null)
                return false;

            foreach (InputBinding binding in action.bindings)
            {
                if (binding.effectivePath == expectedPath || binding.path == expectedPath)
                    return true;
            }

            return false;
        }

        static bool HasResolvedControl(InputAction action)
        {
            return action != null && action.controls.Count > 0;
        }
    }
}

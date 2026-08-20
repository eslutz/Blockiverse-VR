using Blockiverse.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Blockiverse.VR
{
    /// <summary>
    /// Keeps a controller-local "Ray Origin" coincident with the OpenXR <c>aim</c> pose — the pose
    /// Meta's own system pointer originates from — while the controller transform itself stays on
    /// the <c>grip</c> pose (<c>devicePosition</c>/<c>deviceRotation</c>) that drives hands, models,
    /// and everything else hanging off the controller.
    /// </summary>
    /// <remarks>
    /// Grip and aim are two poses of the same rigid controller, so the offset between them is a
    /// constant rigid transform reported by the runtime for the controller model. Applying that
    /// offset in controller-local space is jitter-free regardless of script update order (the
    /// parent's world pose carries it), and the last valid offset is held through tracking blips.
    /// Before any valid sample — or if the runtime never reports an aim pose — the ray falls back
    /// to a fixed offset so it always stays attached to the controller. A separately tracked
    /// aim transform was tried before (removed in PR #319) and a controller-mounted origin with a
    /// fixed 90° pitch replaced it; that fixed pitch is what this component corrects.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BlockiverseAimPoseRayOrigin : MonoBehaviour
    {
        /// <summary>
        /// Controller-local grip->aim offset applied before the first valid aim sample, or if the
        /// runtime never reports one. Right-hand values measured 2026-08-19 through Meta XR Operator
        /// (<c>openxr_get_controller_pose</c> grip vs aim) with
        /// <c>/interaction_profiles/meta/touch_controller_plus</c> bound: the aim origin sits ~4.6 cm
        /// up the handle and ~2 cm toward the ray from the grip origin, pitched ~60° down from the
        /// grip forward with a ~5° inward yaw. The previous fixed origin pitched a full 90° and was
        /// ~30° too low. The left hand mirrors these across the controller's X axis.
        /// </summary>
        public static readonly Quaternion FallbackLocalRotation = new(0.49954f, -0.03776f, -0.0218f, 0.8652f);
        public static readonly Vector3 FallbackLocalPosition = new(-0.009f, -0.0196f, 0.0461f);

        public static Quaternion ResolveFallbackLocalRotation(BlockiverseControllerRole controllerRole) =>
            controllerRole == BlockiverseControllerRole.Left
                ? new Quaternion(FallbackLocalRotation.x, -FallbackLocalRotation.y, -FallbackLocalRotation.z, FallbackLocalRotation.w)
                : FallbackLocalRotation;

        public static Vector3 ResolveFallbackLocalPosition(BlockiverseControllerRole controllerRole) =>
            controllerRole == BlockiverseControllerRole.Left
                ? new Vector3(-FallbackLocalPosition.x, FallbackLocalPosition.y, FallbackLocalPosition.z)
                : FallbackLocalPosition;

        const InputTrackingState RequiredTracking = InputTrackingState.Position | InputTrackingState.Rotation;

        [SerializeField] BlockiverseControllerRole role = BlockiverseControllerRole.Right;

        InputAction gripPositionAction;
        InputAction gripRotationAction;
        InputAction gripTrackingStateAction;
        InputAction aimPositionAction;
        InputAction aimRotationAction;
        InputAction aimTrackingStateAction;
        // True when the actions above were created here (and must be disposed here) rather than
        // supplied from the rig's generated input-actions asset.
        bool ownsActions;

        bool hasValidOffset;
        Vector3 localPosition = FallbackLocalPosition;
        Quaternion localRotation = FallbackLocalRotation;

        public BlockiverseControllerRole Role => role;

        /// <summary>True once a tracked aim pose has been applied at least once.</summary>
        public bool UsingAimPose => hasValidOffset;

        /// <summary>The controller-local offset currently applied to this ray origin.</summary>
        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => localRotation;

        /// <summary>
        /// Binds this origin to a controller role. When the rig supplies its asset-owned grip and aim
        /// actions they are used as-is (the rig owns their lifetime); otherwise direct actions bound
        /// to the standard OpenXR controller usages are created on enable.
        /// </summary>
        public void Configure(
            BlockiverseControllerRole controllerRole,
            InputAction gripPosition = null,
            InputAction gripRotation = null,
            InputAction trackingState = null,
            InputAction aimPosition = null,
            InputAction aimRotation = null)
        {
            role = controllerRole;

            if (!hasValidOffset)
            {
                localPosition = ResolveFallbackLocalPosition(role);
                localRotation = ResolveFallbackLocalRotation(role);
            }

            bool supplied =
                gripPosition != null && gripRotation != null && trackingState != null &&
                aimPosition != null && aimRotation != null;

            if (ownsActions)
                DisposeActions();

            if (supplied)
            {
                gripPositionAction = gripPosition;
                gripRotationAction = gripRotation;
                gripTrackingStateAction = trackingState;
                aimPositionAction = aimPosition;
                aimRotationAction = aimRotation;
                aimTrackingStateAction = trackingState;
                ownsActions = false;
            }
            else
            {
                ClearActionReferences();
                if (isActiveAndEnabled && Application.isPlaying)
                    CreateActions();
            }
        }

        /// <summary>
        /// Resolves the aim pose expressed in grip-local space from two tracking-space poses.
        /// Returns false when either pose lacks position or rotation tracking.
        /// </summary>
        public static bool TryResolveLocalOffset(
            Vector3 gripPosition,
            Quaternion gripRotation,
            InputTrackingState gripTrackingState,
            Vector3 aimPosition,
            Quaternion aimRotation,
            InputTrackingState aimTrackingState,
            out Vector3 localPosition,
            out Quaternion localRotation)
        {
            localPosition = FallbackLocalPosition;
            localRotation = FallbackLocalRotation;

            if ((gripTrackingState & RequiredTracking) != RequiredTracking ||
                (aimTrackingState & RequiredTracking) != RequiredTracking)
                return false;

            if (!IsFinite(gripRotation) || !IsFinite(aimRotation))
                return false;

            Quaternion inverseGrip = Quaternion.Inverse(gripRotation.normalized);
            localRotation = (inverseGrip * aimRotation.normalized).normalized;
            localPosition = inverseGrip * (aimPosition - gripPosition);
            return true;
        }

        public static string ResolveHandUsage(BlockiverseControllerRole controllerRole) =>
            controllerRole == BlockiverseControllerRole.Left ? "LeftHand" : "RightHand";

        void OnEnable()
        {
            if (!hasValidOffset)
            {
                localPosition = ResolveFallbackLocalPosition(role);
                localRotation = ResolveFallbackLocalRotation(role);
            }

            if (!Application.isPlaying)
                return;

            if (aimPositionAction == null)
                CreateActions();

            Application.onBeforeRender += ApplyOffset;
            ApplyOffset();
        }

        void OnDisable()
        {
            Application.onBeforeRender -= ApplyOffset;

            if (ownsActions)
                DisposeActions();
        }

        void Update()
        {
            ApplyOffset();
        }

        void CreateActions()
        {
            if (aimPositionAction != null)
                return;

            ownsActions = true;
            string hand = ResolveHandUsage(role);
            gripPositionAction = CreateAction("Grip Position", $"<XRController>{{{hand}}}/devicePosition", "Vector3");
            gripRotationAction = CreateAction("Grip Rotation", $"<XRController>{{{hand}}}/deviceRotation", "Quaternion");
            gripTrackingStateAction = CreateAction("Grip Tracking State", $"<XRController>{{{hand}}}/trackingState", "Integer");
            aimPositionAction = CreateAction("Aim Position", $"<XRController>{{{hand}}}/pointerPosition", "Vector3");
            aimRotationAction = CreateAction("Aim Rotation", $"<XRController>{{{hand}}}/pointerRotation", "Quaternion");
            aimTrackingStateAction = CreateAction("Aim Tracking State", $"<XRController>{{{hand}}}/trackingState", "Integer");
        }

        static InputAction CreateAction(string name, string binding, string expectedControlType)
        {
            var action = new InputAction(name, InputActionType.Value, binding, expectedControlType: expectedControlType);
            action.Enable();
            return action;
        }

        void DisposeActions()
        {
            Dispose(ref gripPositionAction);
            Dispose(ref gripRotationAction);
            Dispose(ref gripTrackingStateAction);
            Dispose(ref aimPositionAction);
            Dispose(ref aimRotationAction);
            Dispose(ref aimTrackingStateAction);
            ownsActions = false;
        }

        void ClearActionReferences()
        {
            gripPositionAction = null;
            gripRotationAction = null;
            gripTrackingStateAction = null;
            aimPositionAction = null;
            aimRotationAction = null;
            aimTrackingStateAction = null;
            ownsActions = false;
        }

        static void Dispose(ref InputAction action)
        {
            if (action == null)
                return;

            action.Disable();
            action.Dispose();
            action = null;
        }

        void ApplyOffset()
        {
            if (aimPositionAction != null && aimPositionAction.enabled && TryReadOffset(out Vector3 position, out Quaternion rotation))
            {
                localPosition = position;
                localRotation = rotation;
                hasValidOffset = true;
            }

            transform.SetLocalPositionAndRotation(localPosition, localRotation);
        }

        bool TryReadOffset(out Vector3 position, out Quaternion rotation)
        {
            // The OpenXR controller layouts expose one tracking state for the device; both poses
            // read it, so a dropped controller invalidates aim and grip together.
            var gripState = (InputTrackingState)gripTrackingStateAction.ReadValue<int>();
            var aimState = (InputTrackingState)aimTrackingStateAction.ReadValue<int>();

            return TryResolveLocalOffset(
                gripPositionAction.ReadValue<Vector3>(),
                gripRotationAction.ReadValue<Quaternion>(),
                gripState,
                aimPositionAction.ReadValue<Vector3>(),
                aimRotationAction.ReadValue<Quaternion>(),
                aimState,
                out position,
                out rotation);
        }

        static bool IsFinite(Quaternion q) =>
            !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w)) &&
            (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) > 0.0001f;
    }
}

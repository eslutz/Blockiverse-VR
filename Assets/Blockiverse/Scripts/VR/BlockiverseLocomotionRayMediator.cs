using System.Collections;
using Blockiverse.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Blockiverse.VR
{
    /// <summary>
    /// Controls the teleport arc for one controller hand. In <see cref="BlockiverseLocomotionMode.Teleport"/>
    /// mode, pushing the thumbstick forward activates the arc; releasing teleports. In Glide mode
    /// (or whenever the rig is in Glide locomotion) the teleport ray stays inactive so the
    /// thumbstick forward movement is available for walking. The gameplay interaction ray is
    /// disabled while the teleport arc is showing so the trigger cannot also break blocks.
    /// </summary>
    public sealed class BlockiverseLocomotionRayMediator : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] XRRayInteractor interactionRay;
        [SerializeField] XRRayInteractor teleportRay;
        [SerializeField] BlockiverseControllerAnchor controllerAnchor;
        [SerializeField] BlockiverseControllerRole hand = BlockiverseControllerRole.Right;

        InputAction teleportModeAction;
        Coroutine releaseFrameRoutine;
        bool teleportActive;

        public bool TeleportActive => teleportActive;
        public XRRayInteractor InteractionRay => interactionRay;
        public XRRayInteractor TeleportRay => teleportRay;
        public BlockiverseControllerAnchor ControllerAnchor => controllerAnchor;
        public BlockiverseControllerRole Hand => hand;

        public void Configure(
            BlockiverseInputRig rig,
            BlockiverseComfortSettings settings,
            XRRayInteractor interaction,
            XRRayInteractor teleport,
            BlockiverseControllerRole controllerRole,
            BlockiverseControllerAnchor anchor = null)
        {
            inputRig = rig;
            comfortSettings = settings;
            interactionRay = interaction;
            teleportRay = teleport;
            controllerAnchor = anchor != null ? anchor : controllerAnchor != null ? controllerAnchor : GetComponent<BlockiverseControllerAnchor>();
            hand = controllerRole;
            teleportModeAction = null;
            SetTeleportActive(false);
        }

        void OnEnable()
        {
            CancelReleaseFrameRoutine();
            SetTeleportActiveImmediate(false, enableInteractionRay: true);
        }

        void OnDisable()
        {
            CancelReleaseFrameRoutine();
            SetTeleportActiveImmediate(false, enableInteractionRay: true);
        }

        void Update()
        {
            ResolveControllerAnchor();
            bool shouldAim = IsInTeleportMode() && IsTeleportModeHeld();
            SetTeleportActive(shouldAim);
        }

        void ResolveControllerAnchor()
        {
            if (controllerAnchor == null)
                controllerAnchor = GetComponent<BlockiverseControllerAnchor>();
        }

        bool IsInTeleportMode()
        {
            return inputRig != null &&
                BlockiverseRuntimeState.AllowWorldInput &&
                !inputRig.LocomotionSuppressed &&
                comfortSettings != null &&
                comfortSettings.LocomotionMode == BlockiverseLocomotionMode.Teleport;
        }

        bool IsTeleportModeHeld()
        {
            ResolveTeleportModeAction();
            return teleportModeAction != null && teleportModeAction.IsPressed();
        }

        void ResolveTeleportModeAction()
        {
            if (teleportModeAction != null || inputRig == null || inputRig.InputActions == null)
                return;

            string mapName = hand == BlockiverseControllerRole.Left
                ? BlockiverseInputActionNames.LeftHandMap
                : BlockiverseInputActionNames.RightHandMap;

            InputActionMap map = inputRig.InputActions.FindActionMap(mapName, throwIfNotFound: false);
            teleportModeAction = map?.FindAction(BlockiverseInputActionNames.TeleportMode, throwIfNotFound: false);
        }

        void SetTeleportActive(bool active)
        {
            teleportActive = active;

            if (active)
            {
                CancelReleaseFrameRoutine();
                SetTeleportActiveImmediate(true, enableInteractionRay: false);
                return;
            }

            if (!Application.isPlaying ||
                teleportRay == null ||
                !teleportRay.gameObject.activeSelf)
            {
                CancelReleaseFrameRoutine();
                SetTeleportActiveImmediate(false, enableInteractionRay: true);
                return;
            }

            if (releaseFrameRoutine == null)
                releaseFrameRoutine = StartCoroutine(DisableTeleportAfterReleaseFrame());
        }

        IEnumerator DisableTeleportAfterReleaseFrame()
        {
            yield return null;

            releaseFrameRoutine = null;
            if (!teleportActive)
                SetTeleportActiveImmediate(false, enableInteractionRay: true);
        }

        void SetTeleportActiveImmediate(bool active, bool enableInteractionRay)
        {
            // Toggle whole GameObjects so each ray's interactor and its line visual show/hide
            // together. On release, keep the teleport ray alive for one frame so XRI can deliver
            // SelectExited to the TeleportationArea before regular UI/block interaction resumes.
            bool hasTrackedPose = HasUsableRayPose();
            bool showTeleportRay = active && hasTrackedPose;
            bool showInteractionRay = enableInteractionRay && hasTrackedPose && CanUseInteractionRay();

            if (teleportRay != null && teleportRay.gameObject.activeSelf != showTeleportRay)
                teleportRay.gameObject.SetActive(showTeleportRay);

            if (interactionRay != null && interactionRay.gameObject.activeSelf != showInteractionRay)
                interactionRay.gameObject.SetActive(showInteractionRay);
        }

        bool HasUsableRayPose()
        {
            if (!Application.isPlaying)
                return true;

            ResolveControllerAnchor();
            return controllerAnchor == null || controllerAnchor.IsTracked;
        }

        bool CanUseInteractionRay()
        {
            return IsMenuInputActive() || IsActiveInteractionHand();
        }

        static bool IsMenuInputActive()
        {
            return BlockiverseRuntimeState.MenuInputActive;
        }

        bool IsActiveInteractionHand()
        {
            return inputRig == null || inputRig.ActiveToolHand == hand;
        }

        void CancelReleaseFrameRoutine()
        {
            if (releaseFrameRoutine == null)
                return;

            StopCoroutine(releaseFrameRoutine);
            releaseFrameRoutine = null;
        }
    }
}

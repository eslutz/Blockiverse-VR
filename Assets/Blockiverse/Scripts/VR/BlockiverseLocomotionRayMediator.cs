using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Blockiverse.VR
{
    /// <summary>
    /// Controls the teleport arc for one controller hand. In <see cref="BlockiverseLocomotionMode.Teleport"/>
    /// mode, pushing the thumbstick forward activates the arc; releasing teleports. In Glide mode
    /// (or whenever the rig is in Glide locomotion) the teleport ray stays inactive so the
    /// thumbstick forward movement is available for walking. The interaction ray (UI + block
    /// targeting) is disabled while the teleport arc is showing so the trigger cannot also break
    /// blocks or click menus.
    /// </summary>
    public sealed class BlockiverseLocomotionRayMediator : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] XRRayInteractor interactionRay;
        [SerializeField] XRRayInteractor teleportRay;
        [SerializeField] BlockiverseControllerRole hand = BlockiverseControllerRole.Right;

        InputAction teleportModeAction;
        bool teleportActive;

        public bool TeleportActive => teleportActive;
        public XRRayInteractor InteractionRay => interactionRay;
        public XRRayInteractor TeleportRay => teleportRay;

        public void Configure(
            BlockiverseInputRig rig,
            BlockiverseComfortSettings settings,
            XRRayInteractor interaction,
            XRRayInteractor teleport,
            BlockiverseControllerRole controllerRole)
        {
            inputRig = rig;
            comfortSettings = settings;
            interactionRay = interaction;
            teleportRay = teleport;
            hand = controllerRole;
            teleportModeAction = null;
            SetTeleportActive(false);
        }

        void OnEnable()
        {
            SetTeleportActive(false);
        }

        void OnDisable()
        {
            SetTeleportActive(false);
        }

        void Update()
        {
            bool shouldAim = IsInTeleportMode() && IsTeleportModeHeld();
            SetTeleportActive(shouldAim);
        }

        bool IsInTeleportMode()
        {
            return comfortSettings != null &&
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

            // Toggle whole GameObjects so each ray's interactor and its line visual show/hide
            // together. While the teleport arc is showing, the interaction ray is disabled so the
            // trigger does not also break blocks or click UI (bridge guards on isActiveAndEnabled).
            if (teleportRay != null && teleportRay.gameObject.activeSelf != active)
                teleportRay.gameObject.SetActive(active);

            if (interactionRay != null && interactionRay.gameObject.activeSelf == active)
                interactionRay.gameObject.SetActive(!active);
        }
    }
}

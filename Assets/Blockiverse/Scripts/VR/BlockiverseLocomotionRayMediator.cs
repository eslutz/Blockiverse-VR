using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Blockiverse.VR
{
    /// <summary>
    /// Switches a controller between its interaction ray (UI + block targeting) and a projectile
    /// teleport ray while the Teleport Mode action is held, mirroring the XRI Starter Assets
    /// controller mediator. The teleport ray commits to the native TeleportationProvider by
    /// selecting a TeleportationArea; the interaction ray is disabled while aiming so the trigger
    /// does not also break blocks or click UI.
    /// </summary>
    public sealed class BlockiverseLocomotionRayMediator : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
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
            XRRayInteractor interaction,
            XRRayInteractor teleport,
            BlockiverseControllerRole controllerRole)
        {
            inputRig = rig;
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
            SetTeleportActive(IsTeleportModeHeld());
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
            // together. While teleport is aiming, the interaction ray is fully disabled so the
            // trigger cannot also break blocks or click UI (the bridge guards on isActiveAndEnabled).
            if (teleportRay != null && teleportRay.gameObject.activeSelf != active)
                teleportRay.gameObject.SetActive(active);

            if (interactionRay != null && interactionRay.gameObject.activeSelf == active)
                interactionRay.gameObject.SetActive(!active);
        }
    }
}

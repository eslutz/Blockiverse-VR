using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Blockiverse.VR
{
    /// <summary>
    /// Drives creative block break/place/undo from the native controller ray interactor.
    /// Targeting uses the interactor's current 3D raycast hit; break/place are suppressed while
    /// the ray is over UI (native <see cref="XRRayInteractor.IsOverUIGameObject"/>) or while the
    /// interactor is disabled (e.g. teleport aiming), so menus and locomotion never break blocks.
    /// </summary>
    public sealed class BlockiverseCreativeInputBridge : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] XRRayInteractor interactionRay;
        [SerializeField] CreativeInteractionController interactionController;

        UnityAction breakAction;
        UnityAction placeAction;
        UnityAction undoAction;

        public XRRayInteractor InteractionRay => interactionRay;

        public void Configure(
            BlockiverseInputRig rig,
            XRRayInteractor ray,
            CreativeInteractionController controller)
        {
            Unbind();
            inputRig = rig;
            interactionRay = ray;
            interactionController = controller;
            Bind();
        }

        void OnEnable()
        {
            DiscoverDependencies();
            Bind();
        }

        void OnDisable()
        {
            Unbind();
        }

        void Update()
        {
            if (interactionController == null)
                return;

            if (TryGetTarget(out BlockPosition target, out Vector3 normal))
                interactionController.UpdatePreview(target, normal);
            else
                interactionController.HidePreview();
        }

        void Bind()
        {
            DiscoverDependencies();

            if (inputRig == null)
                return;

            EnsureActions();
            inputRig.BreakPressed.RemoveListener(breakAction);
            inputRig.PlacePressed.RemoveListener(placeAction);
            inputRig.UndoPressed.RemoveListener(undoAction);
            inputRig.BreakPressed.AddListener(breakAction);
            inputRig.PlacePressed.AddListener(placeAction);
            inputRig.UndoPressed.AddListener(undoAction);
        }

        void Unbind()
        {
            if (inputRig == null)
                return;

            EnsureActions();
            inputRig.BreakPressed.RemoveListener(breakAction);
            inputRig.PlacePressed.RemoveListener(placeAction);
            inputRig.UndoPressed.RemoveListener(undoAction);
        }

        void EnsureActions()
        {
            breakAction ??= TryBreakTarget;
            placeAction ??= TryPlaceTarget;
            undoAction ??= TryUndo;
        }

        void DiscoverDependencies()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<BlockiverseInputRig>() ?? FindFirstObjectByType<BlockiverseInputRig>();

            if (interactionRay == null)
                interactionRay = GetComponentInChildren<XRRayInteractor>(true);

            if (interactionController == null && Application.isPlaying)
                interactionController = FindFirstObjectByType<CreativeInteractionController>();
        }

        void TryBreakTarget()
        {
            if (TryGetTarget(out BlockPosition target, out _))
                interactionController.TryBreakBlock(target);
        }

        void TryPlaceTarget()
        {
            if (TryGetTarget(out BlockPosition target, out Vector3 normal))
                interactionController.TryPlaceBlock(target, normal);
        }

        void TryUndo()
        {
            interactionController?.UndoLast();
        }

        bool TryGetTarget(out BlockPosition target, out Vector3 normal)
        {
            target = default;
            normal = Vector3.up;

            if (interactionController == null || !CanInteract())
                return false;

            if (!interactionRay.TryGetCurrent3DRaycastHit(out RaycastHit hit))
                return false;

            VoxelChunkTarget chunkTarget = hit.collider.GetComponentInParent<VoxelChunkTarget>();

            if (chunkTarget == null || !chunkTarget.TryGetHitBlock(hit, out target))
                return false;

            normal = hit.normal;
            return true;
        }

        bool CanInteract()
        {
            return interactionRay != null &&
                interactionRay.isActiveAndEnabled &&
                !interactionRay.IsOverUIGameObject();
        }
    }
}

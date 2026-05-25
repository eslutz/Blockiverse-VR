using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Events;

namespace Blockiverse.VR
{
    public sealed class BlockiverseCreativeInputBridge : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] BlockiverseRayPointer rayPointer;
        [SerializeField] CreativeInteractionController interactionController;

        UnityAction breakAction;
        UnityAction placeAction;
        UnityAction undoAction;

        public void Configure(
            BlockiverseInputRig rig,
            BlockiverseRayPointer pointer,
            CreativeInteractionController controller)
        {
            Unbind();
            inputRig = rig;
            rayPointer = pointer;
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
            if (breakAction == null)
                breakAction = TryBreakTarget;
            if (placeAction == null)
                placeAction = TryPlaceTarget;
            if (undoAction == null)
                undoAction = TryUndo;
        }

        void Update()
        {
            if (TryGetTarget(out BlockPosition target, out Vector3 normal))
                interactionController.UpdatePreview(target, normal);
            else
                interactionController?.HidePreview();
        }

        void DiscoverDependencies()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<BlockiverseInputRig>() ?? FindFirstObjectByType<BlockiverseInputRig>();

            if (rayPointer == null)
                rayPointer = GetComponentInChildren<BlockiverseRayPointer>(true) ?? FindFirstObjectByType<BlockiverseRayPointer>();

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

            if (rayPointer == null || interactionController == null || !rayPointer.TryGetHit(out RaycastHit hit))
                return false;

            VoxelChunkTarget chunkTarget = hit.collider.GetComponentInParent<VoxelChunkTarget>();

            if (chunkTarget == null || !chunkTarget.TryGetHitBlock(hit, out target))
                return false;

            normal = hit.normal;
            return true;
        }
    }
}

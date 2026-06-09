using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

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
        [SerializeField] LineRenderer interactionLineRenderer;
        [SerializeField] XRInteractorLineVisual interactionLineVisual;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] MultiplayerSurvivalSync survivalSync;

        UnityAction breakAction;
        UnityAction placeAction;
        UnityAction undoAction;
        UnityAction blockEditingToggleAction;
        bool capturedLineRendererDefault;
        bool lineRendererDefaultEnabled;
        bool capturedLineVisualDefault;
        bool lineVisualDefaultEnabled;

        public XRRayInteractor InteractionRay => interactionRay;

        public void Configure(
            BlockiverseInputRig rig,
            XRRayInteractor ray,
            CreativeInteractionController controller)
        {
            Unbind();
            inputRig = rig;
            interactionRay = ray;
            interactionLineRenderer = null;
            interactionLineVisual = null;
            capturedLineRendererDefault = false;
            capturedLineVisualDefault = false;
            interactionController = controller;
            DiscoverInteractionRayVisuals();
            ApplyInteractionRayVisualState();
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
            ApplyInteractionRayVisualState();

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
            inputRig.BlockEditingTogglePressed.RemoveListener(blockEditingToggleAction);
            inputRig.BreakPressed.AddListener(breakAction);
            inputRig.PlacePressed.AddListener(placeAction);
            inputRig.UndoPressed.AddListener(undoAction);
            inputRig.BlockEditingTogglePressed.AddListener(blockEditingToggleAction);
        }

        void Unbind()
        {
            if (inputRig == null)
                return;

            EnsureActions();
            inputRig.BreakPressed.RemoveListener(breakAction);
            inputRig.PlacePressed.RemoveListener(placeAction);
            inputRig.UndoPressed.RemoveListener(undoAction);
            inputRig.BlockEditingTogglePressed.RemoveListener(blockEditingToggleAction);
        }

        void EnsureActions()
        {
            breakAction ??= TryBreakTarget;
            placeAction ??= TryPlaceTarget;
            undoAction ??= TryUndo;
            blockEditingToggleAction ??= ToggleBlockEditing;
        }

        void DiscoverDependencies()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<BlockiverseInputRig>() ?? FindFirstObjectByType<BlockiverseInputRig>();

            if (interactionRay == null)
                interactionRay = GetComponentInChildren<XRRayInteractor>(true);

            DiscoverInteractionRayVisuals();

            if (interactionController == null && Application.isPlaying)
                interactionController = FindFirstObjectByType<CreativeInteractionController>();

            if (survivalSync == null && Application.isPlaying)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>();
        }

        // The current interaction mode for this player (resolved from the survival sync).
        public PlayerModeState CurrentMode =>
            survivalSync != null ? survivalSync.CurrentMode : PlayerModeState.Creative;

        // Flips between survival and creative interaction. Invoked by the mode-toggle menu action.
        public void ToggleSurvivalCreativeMode()
        {
            DiscoverDependencies();
            survivalSync?.ToggleMode();
        }

        bool SurvivalInteractionActive => survivalSync != null && survivalSync.CurrentMode == PlayerModeState.Survival;

        void DiscoverInteractionRayVisuals()
        {
            if (interactionRay == null)
                return;

            if (interactionLineRenderer == null)
                interactionLineRenderer = interactionRay.GetComponent<LineRenderer>();

            CaptureLineRendererDefault();

            if (interactionLineVisual == null)
                interactionLineVisual = interactionRay.GetComponent<XRInteractorLineVisual>();

            CaptureLineVisualDefault();
        }

        void CaptureLineRendererDefault()
        {
            if (capturedLineRendererDefault || interactionLineRenderer == null)
                return;

            lineRendererDefaultEnabled = interactionLineRenderer.enabled;
            capturedLineRendererDefault = true;
        }

        void CaptureLineVisualDefault()
        {
            if (capturedLineVisualDefault || interactionLineVisual == null)
                return;

            lineVisualDefaultEnabled = interactionLineVisual.enabled;
            capturedLineVisualDefault = true;
        }

        void TryBreakTarget()
        {
            if (!TryGetTarget(out BlockPosition target, out _))
                return;

            // Survival mode: server-authoritative harvest (resource drops, tool tier/durability,
            // container loot). Creative mode: direct delete.
            if (SurvivalInteractionActive)
                survivalSync.TrySubmitHarvest(target, out _);
            else
                interactionController.TryBreakBlock(target);
        }

        void TryPlaceTarget()
        {
            if (!TryGetTarget(out BlockPosition target, out Vector3 normal))
                return;

            // Survival mode: place the held block into the adjacent cell, consuming one from the
            // inventory (authoritative). Creative mode: free placement of the selected catalog block.
            if (SurvivalInteractionActive)
            {
                BlockPosition placement = CreativeInteractionController.ComputePlacementPosition(target, normal);
                survivalSync.TrySubmitPlace(placement, out _);
            }
            else
            {
                interactionController.TryPlaceBlock(target, normal);
            }
        }

        void TryUndo()
        {
            interactionController?.UndoLast();
        }

        void ToggleBlockEditing()
        {
            if (interactionController == null)
                return;

            interactionController.ToggleBlockEditingEnabled();
            ApplyInteractionRayVisualState();
        }

        bool TryGetTarget(out BlockPosition target, out Vector3 normal)
        {
            target = default;
            normal = Vector3.up;

            if (interactionController == null || !interactionController.BlockEditingEnabled || !CanInteract())
                return false;

            if (!interactionRay.TryGetCurrent3DRaycastHit(out RaycastHit hit))
                return false;

            VoxelChunkTarget chunkTarget = hit.collider.GetComponentInParent<VoxelChunkTarget>();

            if (chunkTarget == null || !chunkTarget.TryGetHitBlock(hit, out target))
                return false;

            normal = hit.normal;
            return true;
        }

        void ApplyInteractionRayVisualState()
        {
            DiscoverInteractionRayVisuals();

            bool shouldShow = interactionController == null || interactionController.BlockEditingEnabled;

            if (interactionLineRenderer != null)
                interactionLineRenderer.enabled = shouldShow && (!capturedLineRendererDefault || lineRendererDefaultEnabled);

            if (interactionLineVisual != null)
                interactionLineVisual.enabled = shouldShow && (!capturedLineVisualDefault || lineVisualDefaultEnabled);

            if (!shouldShow)
                interactionController?.HidePreview();
        }

        bool CanInteract()
        {
            return interactionRay != null &&
                interactionRay.isActiveAndEnabled &&
                !interactionRay.IsOverUIGameObject();
        }
    }
}

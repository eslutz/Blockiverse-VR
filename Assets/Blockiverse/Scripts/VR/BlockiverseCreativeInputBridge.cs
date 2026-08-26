using System;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Blockiverse.VR
{
    /// <summary>
    /// Drives creative block break/place from the native controller ray interactor.
    /// Targeting uses the interactor's current 3D raycast hit; break/place are suppressed while
    /// the ray is over UI (native <see cref="XRRayInteractor.IsOverUIGameObject"/>) or while the
    /// interactor is disabled (e.g. teleport aiming), so menus and locomotion never break blocks.
    /// </summary>
    public sealed class BlockiverseCreativeInputBridge : MonoBehaviour, IBlockiverseCreativeInputBridge
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] XRRayInteractor interactionRay;
        [SerializeField] XRRayInteractor leftInteractionRay;
        [SerializeField] XRRayInteractor rightInteractionRay;
        [SerializeField] LineRenderer interactionLineRenderer;
        [SerializeField] XRInteractorLineVisual interactionLineVisual;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] BlockiverseComfortSettings comfortSettings;

        UnityAction breakAction;
        UnityAction breakReleasedAction;
        UnityAction placeAction;
        UnityAction blockEditingToggleAction;
        bool capturedLineRendererDefault;
        bool lineRendererDefaultEnabled;
        bool capturedLineVisualDefault;
        bool lineVisualDefaultEnabled;
        bool lineVisualDefaultOverrideLineLength;
        float lineVisualDefaultLength;

        // Interaction-ray cache (R3): the left/right rays are resolved once after rig wiring and then
        // reused every frame. The allocating GetComponentsInChildren scans only run until both hands
        // resolve (or after an explicit rig change via InvalidateInteractionRayCache), never per-frame.
        bool interactionRaysResolved;
        int interactionRayDiscoveryCount;

        // Hold-to-mine (§7.3): survival break is a timed hold on a fixed target. ToolHit cues +
        // chip VFX play on a cadence while held; releasing or losing the target cancels; the
        // harvest submits when the block's work time elapses. Creative break stays instant.
        const float MineHitCueIntervalSeconds = 0.4f;

        BlockiverseAudioCuePlayer audioCuePlayer;
        BlockiverseVfxCuePlayer vfxCuePlayer;
        bool mining;
        bool miningStartedByToggle;
        BlockPosition miningTarget;
        float miningElapsedSeconds;
        float miningRequiredSeconds;
        float nextMineCueTime;

        public XRRayInteractor InteractionRay => interactionRay;

        // Diagnostics/test seam: number of interaction-ray discovery passes that have run. After the
        // first successful resolve this stays constant until the cache is invalidated, proving the VR
        // hot path no longer re-scans (and re-allocates) every frame.
        public int InteractionRayDiscoveryCount => interactionRayDiscoveryCount;

        public event Action<BlockPosition, float, float> MiningProgressChanged;
        public event Action MiningProgressCleared;

        public void Configure(
            BlockiverseInputRig rig,
            XRRayInteractor ray,
            CreativeInteractionController controller)
        {
            Unbind();
            inputRig = rig;
            SetInteractionRay(ray);
            leftInteractionRay = null;
            rightInteractionRay = null;
            InvalidateInteractionRayCache();
            interactionLineRenderer = null;
            interactionLineVisual = null;
            capturedLineRendererDefault = false;
            capturedLineVisualDefault = false;
            interactionController = controller;
            DiscoverInteractionRayVisuals();
            ApplyInteractionRayVisualState();
            ApplyPlayerOccupancyPredicate();
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
            RefreshActiveInteractionRay();
            ApplyInteractionRayVisualState();

            if (interactionController == null)
                return;

            // The highlight is now the answer to "what will the trigger do", so it appears only
            // while the grip is selecting place/use. Breaking shows the bare ray, which is less
            // visual noise for the more common action.
            // Resolved UNCONDITIONALLY, before any modifier check. TickMining below needs the
            // target whichever verb the trigger is currently on, and short-circuiting this behind
            // PlaceModifierActive left it unassigned while breaking — which the compiler caught,
            // but which would also have stopped mining from tracking its target.
            bool hasTarget = TryGetTarget(out BlockPosition target, out Vector3 normal);

            if (PlaceModifierActive && hasTarget)
            {
                if (UseTargetsTheBlockItself)
                {
                    // A tool that acts ON the block — stripping a log with a Feller, tilling soil.
                    // Highlighting the empty cell NEXT to it would point at the wrong place
                    // entirely, which is the trap in reusing a placement preview for every "use".
                    interactionController.UpdatePreviewAtTarget(target);
                }
                else
                {
                    interactionController.UpdatePreview(target, normal);
                }
            }
            else
            {
                interactionController.HidePreview();
            }

            TickMining(target);
        }

        // Advances an active mine action: hold mode cancels when the trigger is released; toggle
        // mode keeps mining until the started target is lost, blocked, completed, or toggled off.
        void TickMining(BlockPosition currentTarget)
        {
            if (!mining)
                return;

            if (!BlockiverseRuntimeState.AllowWorldInput)
            {
                CancelMining();
                return;
            }

            bool stillHeld = miningStartedByToggle || inputRig == null || inputRig.IsBreakHeld;
            bool stillAimed = interactionController.CurrentTarget.HasValue && currentTarget == miningTarget;

            if (!stillHeld || !stillAimed || !SurvivalInteractionActive)
            {
                CancelMining();
                return;
            }

            miningElapsedSeconds += Time.deltaTime;
            RaiseMiningProgress();

            if (Time.time >= nextMineCueTime)
            {
                nextMineCueTime = Time.time + MineHitCueIntervalSeconds;
                PlayMineStrikeFeedback();
            }

            if (miningElapsedSeconds >= miningRequiredSeconds)
            {
                mining = false;
                miningStartedByToggle = false;
                MiningProgressCleared?.Invoke();
                survivalSync.TrySubmitHarvest(miningTarget, out _);
            }
        }

        void CancelMining()
        {
            bool wasMining = mining;
            mining = false;
            miningStartedByToggle = false;

            if (wasMining)
                MiningProgressCleared?.Invoke();
        }

        void PlayMineStrikeFeedback()
        {
            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();
            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();

            var worldCenter = new Vector3(miningTarget.X + 0.5f, miningTarget.Y + 0.5f, miningTarget.Z + 0.5f);
            BlockiverseAudioCue cue = interactionController != null &&
                                      interactionController.TryGetBlock(miningTarget, out BlockId targetBlock)
                ? BlockiverseBlockFeedbackCues.ToolHitForBlock(BlockRegistry.Default, targetBlock)
                : BlockiverseAudioCue.ToolHitSoft;
            audioCuePlayer?.PlayCueAt(cue, worldCenter);
            vfxCuePlayer?.PlayCue(BlockiverseVfxCue.BlockChipBurst, worldCenter);
        }

        void Bind()
        {
            DiscoverDependencies();

            if (inputRig == null)
                return;

            EnsureActions();
            inputRig.BreakPressed.RemoveListener(breakAction);
            inputRig.BreakReleased.RemoveListener(breakReleasedAction);
            inputRig.PlacePressed.RemoveListener(placeAction);
            inputRig.BlockEditingTogglePressed.RemoveListener(blockEditingToggleAction);
            inputRig.BreakPressed.AddListener(breakAction);
            inputRig.BreakReleased.AddListener(breakReleasedAction);
            inputRig.PlacePressed.AddListener(placeAction);
            inputRig.BlockEditingTogglePressed.AddListener(blockEditingToggleAction);
        }

        void Unbind()
        {
            if (inputRig == null)
                return;

            EnsureActions();
            inputRig.BreakPressed.RemoveListener(breakAction);
            inputRig.BreakReleased.RemoveListener(breakReleasedAction);
            inputRig.PlacePressed.RemoveListener(placeAction);
            inputRig.BlockEditingTogglePressed.RemoveListener(blockEditingToggleAction);
            CancelMining();
        }

        void EnsureActions()
        {
            breakAction ??= TryBreakTarget;
            breakReleasedAction ??= OnBreakReleased;
            // The grip no longer places on press — it selects what the trigger does. It still
            // cancels an in-flight mine, because pressing it means the player changed their mind
            // about what they were doing to that block.
            placeAction ??= OnPlaceModifierPressed;
            blockEditingToggleAction ??= ToggleBlockEditing;
        }

        void DiscoverDependencies()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<BlockiverseInputRig>() ?? FindFirstObjectByType<BlockiverseInputRig>();

            DiscoverInteractionRays();
            RefreshActiveInteractionRay();

            DiscoverInteractionRayVisuals();

            if (interactionController == null && Application.isPlaying)
                interactionController = FindFirstObjectByType<CreativeInteractionController>();

            if (survivalSync == null && Application.isPlaying)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>();

            if (comfortSettings == null)
                comfortSettings = GetComponentInParent<BlockiverseComfortSettings>() ??
                    FindFirstObjectByType<BlockiverseComfortSettings>(FindObjectsInactive.Include);

            ApplyPlayerOccupancyPredicate();
        }

        void ApplyPlayerOccupancyPredicate()
        {
            if (interactionController != null)
                interactionController.ConfigurePlayerOccupancy(IsLocalPlayerOccupyingBlock);

            if (survivalSync != null)
                survivalSync.ConfigureLocalCrouchStateProvider(() => inputRig != null && inputRig.CrouchActive);
        }

        bool IsLocalPlayerOccupyingBlock(BlockPosition targetPosition)
        {
            Transform head = inputRig != null && inputRig.HeadPoseDriver != null
                ? inputRig.HeadPoseDriver.transform
                : inputRig != null
                    ? inputRig.transform
                    : null;

            if (head == null)
                return false;

            BlockPosition headPosition = CreativeInteractionController.ToBlockPosition(head.position);
            return CreativeInteractionController.IsPlayerOccupyingBlock(
                targetPosition,
                headPosition,
                inputRig != null && inputRig.CrouchActive);
        }

        // The current interaction mode for this player (resolved from the survival sync).
        public PlayerModeState CurrentMode =>
            survivalSync != null ? survivalSync.CurrentMode : PlayerModeState.Creative;
        public bool CanToggleSurvivalCreativeMode =>
            survivalSync != null && survivalSync.CanToggleMode;

        // Flips between survival and creative interaction. Invoked by the mode-toggle menu action.
        public bool ToggleSurvivalCreativeMode()
        {
            DiscoverDependencies();
            return survivalSync != null && survivalSync.ToggleMode();
        }

        bool SurvivalInteractionActive => survivalSync != null && survivalSync.CurrentMode == PlayerModeState.Survival;
        bool UseToggleToMine => comfortSettings != null && comfortSettings.ToggleToMineEnabled;

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

        // Forces the next resolve to re-scan the rig for its interaction rays. Call this whenever the
        // rig hierarchy changes (controllers added/removed, rays re-wired) so the cache stays correct.
        public void InvalidateInteractionRayCache()
        {
            interactionRaysResolved = false;
        }

        void DiscoverInteractionRays()
        {
            // Steady state: rays are cached from the rig if not assigned in the inspector.
            if (interactionRaysResolved)
                return;

            interactionRayDiscoveryCount++;

            if (inputRig != null)
            {
                leftInteractionRay ??= inputRig.LeftInteractionRay;
                rightInteractionRay ??= inputRig.RightInteractionRay;
            }

            if (leftInteractionRay == null || rightInteractionRay == null)
            {
                foreach (BlockiverseLocomotionRayMediator mediator in GetComponentsInChildren<BlockiverseLocomotionRayMediator>(true))
                {
                    if (mediator == null || mediator.InteractionRay == null)
                        continue;

                    if (mediator.Hand == BlockiverseControllerRole.Left)
                        leftInteractionRay ??= mediator.InteractionRay;
                    else
                        rightInteractionRay ??= mediator.InteractionRay;
                }
            }

            // Latch only once both hands resolved; otherwise keep retrying so a rig that finishes
            // wiring after the first frame still picks up its rays. No more allocating scans.
            interactionRaysResolved = leftInteractionRay != null && rightInteractionRay != null;
        }

        void RefreshActiveInteractionRay()
        {
            XRRayInteractor resolved = ResolveActiveInteractionRay();

            if (resolved != null && resolved != interactionRay)
                SetInteractionRay(resolved);
        }

        XRRayInteractor ResolveActiveInteractionRay()
        {
            DiscoverInteractionRays();

            if (inputRig != null)
            {
                XRRayInteractor dominantRay = inputRig.ActiveToolHand == BlockiverseControllerRole.Left
                    ? leftInteractionRay
                    : rightInteractionRay;

                if (dominantRay != null)
                    return dominantRay;
            }

            return interactionRay ?? rightInteractionRay ?? leftInteractionRay;
        }

        void SetInteractionRay(XRRayInteractor ray)
        {
            if (interactionRay == ray)
                return;

            interactionRay = ray;
            interactionLineRenderer = null;
            interactionLineVisual = null;
            capturedLineRendererDefault = false;
            capturedLineVisualDefault = false;
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
            lineVisualDefaultOverrideLineLength = interactionLineVisual.overrideInteractorLineLength;
            lineVisualDefaultLength = interactionLineVisual.lineLength;
            capturedLineVisualDefault = true;
        }

        // True when the grip is selecting "place/use" rather than "break".
        bool PlaceModifierActive => inputRig != null && inputRig.PlaceModifierActive;

        void TryBreakTarget()
        {
            // One trigger, two verbs. The grip picks which, so the player never has to remember
            // which button does what — only whether they are holding the modifier.
            if (PlaceModifierActive)
            {
                TryPlaceTarget();
                return;
            }

            if (!TryGetTarget(out BlockPosition target, out _))
                return;

            // Survival mode: hold-to-mine or toggle-to-mine starts a timed action whose harvest
            // submits when the block's work time elapses. Preview failures such as full inventory
            // or no harvest rule submit immediately so host-side rejection feedback can play.
            if (SurvivalInteractionActive)
            {
                if (mining && miningStartedByToggle && target == miningTarget)
                {
                    CancelMining();
                    return;
                }

                if (survivalSync.TryEvaluateHarvestWorkSeconds(target, out float requiredSeconds) &&
                    requiredSeconds > 0f)
                {
                    StartMining(target, requiredSeconds, UseToggleToMine);
                    return;
                }

                survivalSync.TrySubmitHarvest(target, out _);
            }
            else
            {
                interactionController.TryBreakBlock(target);
            }
        }

        // The grip press. It no longer places — the trigger does that now — but pressing it means
        // the player has changed what they intend to do to the block they were working on, so an
        // in-flight mine is abandoned rather than completing under them after they switched.
        void OnPlaceModifierPressed()
        {
            if (mining)
                CancelMining();
        }

        // Whether "use" acts on the aimed block instead of the cell beside it. Creative placement
        // is always adjacent; a survival tool with no block to place acts on the target.
        bool UseTargetsTheBlockItself
        {
            get
            {
                if (!SurvivalInteractionActive || survivalSync == null)
                    return false;

                // Asked of the sync rather than resolved here: Blockiverse.VR references
                // Survival.Health but not Survival, so ItemRegistry is not visible from this
                // assembly. Widening the asmdef to reach one lookup would be the wrong trade.
                return !survivalSync.EquippedItemPlacesBlock;
            }
        }

        void OnBreakReleased()
        {
            if (!miningStartedByToggle)
                CancelMining();
        }

        void StartMining(BlockPosition target, float requiredSeconds, bool startedByToggle)
        {
            mining = true;
            miningStartedByToggle = startedByToggle;
            miningTarget = target;
            miningElapsedSeconds = 0f;
            miningRequiredSeconds = requiredSeconds;
            nextMineCueTime = Time.time;
            RaiseMiningProgress();
        }

        void RaiseMiningProgress()
        {
            if (miningRequiredSeconds <= 0f)
                return;

            MiningProgressChanged?.Invoke(
                miningTarget,
                miningElapsedSeconds,
                miningRequiredSeconds);
        }

        void TryPlaceTarget()
        {
            if (!TryGetTarget(out BlockPosition target, out Vector3 normal))
                return;

            // Survival mode: use the held item authoritatively — place the held block into the adjacent
            // cell, or (Feller held on a branchwood_log) strip the log into smooth_branchwood. The sync
            // decides based on the held item. Creative mode: free placement of the selected catalog block.
            if (SurvivalInteractionActive)
            {
                BlockPosition placement = CreativeInteractionController.ComputePlacementPosition(target, normal);
                survivalSync.TrySubmitUse(target, placement, out _);
            }
            else
            {
                interactionController.TryPlaceBlock(target, normal);
            }
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

            if (interactionController == null ||
                !BlockiverseRuntimeState.AllowWorldInput ||
                !interactionController.BlockEditingEnabled ||
                !CanInteract())
            {
                return false;
            }

            if (!interactionRay.TryGetCurrent3DRaycastHit(out RaycastHit hit))
                return false;

            // The interaction ray deliberately includes the fluid layer -- drink, bucket fill and
            // teleport-onto-water all need to hit a water surface. For BLOCK EDITING that made a
            // lake behave like a wall: the ray stopped dead on the surface, so the only place you
            // could put a block was on top of the water.
            //
            // Re-cast past it on the solid-only mask rather than narrowing the shared mask, which
            // would break teleport-onto-water (shipped) and the two fluid use verbs.
            if (IsFluidHit(hit) && TryRaycastPastFluid(out RaycastHit solidHit))
                hit = solidHit;

            VoxelChunkTarget chunkTarget = hit.collider.GetComponentInParent<VoxelChunkTarget>();

            if (chunkTarget == null || !chunkTarget.TryGetHitBlock(hit, out target))
                return false;

            normal = hit.normal;
            return true;
        }

        static bool IsFluidHit(RaycastHit hit) =>
            hit.collider != null && hit.collider.gameObject.layer == BlockiverseProject.FluidLayerIndex;

        // Same origin, direction and reach as the interactor's own cast, minus the fluid layer, so
        // the builder targets whatever the water is sitting on.
        bool TryRaycastPastFluid(out RaycastHit hit)
        {
            hit = default;

            Transform origin = interactionRay != null ? interactionRay.rayOriginTransform : null;

            if (origin == null)
                return false;

            return Physics.Raycast(
                new Ray(origin.position, origin.forward),
                out hit,
                interactionRay.maxRaycastDistance,
                BlockiverseProject.VoxelGroundLayerMask,
                QueryTriggerInteraction.Ignore);
        }

        void ApplyInteractionRayVisualState()
        {
            DiscoverInteractionRayVisuals();

            bool worldInputAllowed = BlockiverseRuntimeState.AllowWorldInput;
            bool blockEditingVisible = interactionController == null || interactionController.BlockEditingEnabled;
            // The title world is explorable but not editable. Keep the XR interactor active for
            // UI hit-testing, while drawing its line only once it is actually aimed at the menu.
            bool shouldShow = worldInputAllowed ? blockEditingVisible : IsInteractionRayOverUi();

            if (interactionLineRenderer != null)
                interactionLineRenderer.enabled = shouldShow && (!capturedLineRendererDefault || lineRendererDefaultEnabled);

            if (interactionLineVisual != null)
                interactionLineVisual.enabled = shouldShow && (!capturedLineVisualDefault || lineVisualDefaultEnabled);

            RestoreInteractionRayLengthState();

            if (!worldInputAllowed || !blockEditingVisible)
                interactionController?.HidePreview();
        }

        void RestoreInteractionRayLengthState()
        {
            if (interactionLineVisual == null || !capturedLineVisualDefault)
                return;

            interactionLineVisual.overrideInteractorLineLength = lineVisualDefaultOverrideLineLength;
            interactionLineVisual.lineLength = lineVisualDefaultLength;
        }

        bool IsInteractionRayOverUi()
        {
            return interactionRay != null &&
                interactionRay.isActiveAndEnabled &&
                interactionRay.IsOverUIGameObject();
        }

        bool CanInteract()
        {
            return interactionRay != null &&
                interactionRay.isActiveAndEnabled &&
                !IsInteractionRayOverUi();
        }
    }
}

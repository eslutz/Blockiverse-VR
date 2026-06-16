using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class CreativeInteractionController : MonoBehaviour
    {
        public const float MaxBlockInteractionReachMeters = 6.0f;

        readonly List<SetBlockCommand> undoStack = new();
        // Undone edits eligible for redo (§12.1); cleared whenever a fresh edit lands.
        readonly List<SetBlockCommand> redoStack = new();

        VoxelWorld world;
        BlockRegistry registry;
        CreativeHotbar hotbar;
        PlacementPreview placementPreview;
        VoxelWorldRenderer worldRenderer;
        BlockMutationAuthority mutationAuthority;
        MultiplayerChunkAuthoritySync chunkAuthoritySync;
        Func<BlockPosition, bool> playerOccupancyPredicate;
        bool blockEditingEnabled = true;

        public BlockMutationAuthority MutationAuthority => mutationAuthority;
        public BlockMutationResult LastMutationResult { get; private set; }
        public bool BlockEditingEnabled => blockEditingEnabled;
        public int UndoHistoryCount => undoStack.Count;
        public int RedoHistoryCount => redoStack.Count;

        /// <summary>Raised after a block mutation is locally applied so presentation systems (audio, haptics) can react.</summary>
        public event Action<BlockChange> BlockMutationApplied;
        public event Action<bool> BlockEditingEnabledChanged;

        public void Configure(
            VoxelWorld voxelWorld,
            BlockRegistry blockRegistry,
            CreativeHotbar creativeHotbar,
            PlacementPreview preview,
            Bounds? playerCollisionBounds,
            VoxelWorldRenderer renderer = null,
            BlockMutationAuthority authority = null,
            MultiplayerChunkAuthoritySync authoritySync = null)
        {
            bool worldChanged = world != null && !ReferenceEquals(world, voxelWorld);
            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            hotbar = creativeHotbar;
            placementPreview = preview;
            worldRenderer = renderer;
            mutationAuthority = authority ?? BlockMutationAuthority.CreateHost(world, registry);
            chunkAuthoritySync = authoritySync;

            if (worldChanged)
                ResetEditHistory();
        }

        public void ConfigurePlayerOccupancy(Func<BlockPosition, bool> occupancyPredicate)
        {
            playerOccupancyPredicate = occupancyPredicate;
        }

        public void ResetEditHistory()
        {
            undoStack.Clear();
            redoStack.Clear();
            CurrentTarget = null;
            placementPreview?.Hide();
        }

        public static BlockPosition ComputePlacementPosition(BlockPosition targetPosition, Vector3 faceNormal)
        {
            Vector3 rounded = new(
                Mathf.Round(faceNormal.x),
                Mathf.Round(faceNormal.y),
                Mathf.Round(faceNormal.z));

            if (rounded.sqrMagnitude <= Mathf.Epsilon)
                rounded = Vector3.up;

            return targetPosition + new BlockPosition(
                Mathf.Clamp((int)rounded.x, -1, 1),
                Mathf.Clamp((int)rounded.y, -1, 1),
                Mathf.Clamp((int)rounded.z, -1, 1));
        }

        public static BlockPosition ComputeHitBlockPosition(Vector3 hitPoint, Vector3 faceNormal)
        {
            return ToBlockPosition(hitPoint - faceNormal.normalized * 0.001f);
        }

        // The voxel cell containing a Unity world-space point (BlockPosition lives in the
        // engine-free Voxel assembly, so the Vector3 conversion belongs here in Gameplay).
        public static BlockPosition ToBlockPosition(Vector3 worldPosition) => new(
            Mathf.FloorToInt(worldPosition.x),
            Mathf.FloorToInt(worldPosition.y),
            Mathf.FloorToInt(worldPosition.z));

        public static bool IsBlockWithinInteractionReach(Vector3 origin, BlockPosition target)
        {
            float distanceX = DistanceOutsideAxis(origin.x, target.X, target.X + 1);
            float distanceY = DistanceOutsideAxis(origin.y, target.Y, target.Y + 1);
            float distanceZ = DistanceOutsideAxis(origin.z, target.Z, target.Z + 1);
            float maxDistance = MaxBlockInteractionReachMeters;

            return distanceX * distanceX + distanceY * distanceY + distanceZ * distanceZ <= maxDistance * maxDistance;
        }

        static float DistanceOutsideAxis(float value, int minInclusive, int maxExclusive)
        {
            if (value < minInclusive)
                return minInclusive - value;

            if (value > maxExclusive)
                return value - maxExclusive;

            return 0.0f;
        }

        public bool TryBreakBlock(BlockPosition position)
        {
            EnsureConfigured();

            if (!BlockiverseRuntimeState.AllowWorldInput)
                return RejectWorldInputBlocked(position);

            if (!blockEditingEnabled)
                return RejectBlockEditingDisabled(position);

            return TryMutate(position, BlockRegistry.Air, pushUndo: true);
        }

        public bool TryPlaceBlock(BlockPosition targetPosition, Vector3 faceNormal)
        {
            return TryPlaceAt(ComputePlacementPosition(targetPosition, faceNormal));
        }

        public bool TryPlaceAt(BlockPosition position)
        {
            EnsureConfigured();

            if (!BlockiverseRuntimeState.AllowWorldInput)
                return RejectWorldInputBlocked(position);

            if (!blockEditingEnabled)
                return RejectBlockEditingDisabled(position);

            if (hotbar == null || !CanPlaceBlock(position))
                return false;

            BlockId selectedBlock = hotbar.SelectedBlockId;

            if (selectedBlock == BlockRegistry.Air)
                return false;

            return TryMutate(position, selectedBlock, pushUndo: true);
        }

        public bool CanPlaceBlock(BlockPosition position)
        {
            EnsureConfigured();

            if (!BlockiverseRuntimeState.AllowWorldInput)
                return false;

            if (!blockEditingEnabled)
                return false;

            if (!world.Bounds.Contains(position))
                return false;

            if (world.GetBlock(position) != BlockRegistry.Air)
                return false;

            if (playerOccupancyPredicate != null && playerOccupancyPredicate(position))
                return false;

            return true;
        }

        public static bool IsPlayerOccupyingBlock(BlockPosition targetPosition, BlockPosition playerHeadPosition, bool crouching)
        {
            if (targetPosition.X != playerHeadPosition.X || targetPosition.Z != playerHeadPosition.Z)
                return false;

            int feetY = playerHeadPosition.Y - 1;
            return targetPosition.Y == feetY || (!crouching && targetPosition.Y == playerHeadPosition.Y);
        }

        public bool UndoLast()
        {
            EnsureConfigured();

            if (!BlockiverseRuntimeState.AllowWorldInput)
            {
                BlockPosition position = undoStack.Count > 0 ? undoStack[undoStack.Count - 1].Position : default;
                return RejectWorldInputBlocked(position);
            }

            if (!blockEditingEnabled)
            {
                BlockPosition position = undoStack.Count > 0 ? undoStack[undoStack.Count - 1].Position : default;
                return RejectBlockEditingDisabled(position);
            }

            if (undoStack.Count == 0)
                return false;

            SetBlockCommand command = undoStack[undoStack.Count - 1];

            ChunkAuthorityBoundary boundary = ResolveEffectiveBoundary();

            if (!boundary.CanCommitMutations)
            {
                LastMutationResult = BlockMutationResult.Reject(
                    BlockMutationRejectionReason.ClientCannotCommitAuthoritativeState,
                    ChunkCoordinate.FromBlockPosition(command.Position, world.ChunkSize),
                    "Clients must request host validation instead of undoing authoritative chunk mutations locally.");
                return false;
            }

            BlockChange appliedChange = command.AppliedChange;
            var undoRequest = new BlockMutationRequest(
                boundary.LocalClientId,
                appliedChange.Position,
                appliedChange.PreviousBlock,
                expectedCurrentBlock: appliedChange.NewBlock);

            BlockMutationResult result = SubmitMutation(undoRequest, out SetBlockCommand undoCommand, out bool requestSentToHost);
            LastMutationResult = result;

            if (!result.Accepted)
                return requestSentToHost;

            undoStack.RemoveAt(undoStack.Count - 1);

            // The undone edit becomes redoable until a fresh edit invalidates the branch.
            if (redoStack.Count >= GameModeConstants.CreativeUndoHistoryLimit)
                redoStack.RemoveAt(0);
            redoStack.Add(command);

            RebuildChangedChunks();

            if (undoCommand.HasAppliedChange)
                BlockMutationApplied?.Invoke(undoCommand.AppliedChange);

            return true;
        }

        // Re-applies the most recently undone edit (§12.1 redo).
        public bool RedoLast()
        {
            EnsureConfigured();

            if (!BlockiverseRuntimeState.AllowWorldInput)
            {
                BlockPosition position = redoStack.Count > 0 ? redoStack[redoStack.Count - 1].Position : default;
                return RejectWorldInputBlocked(position);
            }

            if (!blockEditingEnabled)
            {
                BlockPosition position = redoStack.Count > 0 ? redoStack[redoStack.Count - 1].Position : default;
                return RejectBlockEditingDisabled(position);
            }

            if (redoStack.Count == 0)
                return false;

            SetBlockCommand command = redoStack[redoStack.Count - 1];

            ChunkAuthorityBoundary boundary = ResolveEffectiveBoundary();
            if (!boundary.CanCommitMutations)
            {
                LastMutationResult = BlockMutationResult.Reject(
                    BlockMutationRejectionReason.ClientCannotCommitAuthoritativeState,
                    ChunkCoordinate.FromBlockPosition(command.Position, world.ChunkSize),
                    "Clients must request host validation instead of redoing authoritative chunk mutations locally.");
                return false;
            }

            BlockChange appliedChange = command.AppliedChange;
            var redoRequest = new BlockMutationRequest(
                boundary.LocalClientId,
                appliedChange.Position,
                appliedChange.NewBlock,
                expectedCurrentBlock: appliedChange.PreviousBlock);

            BlockMutationResult result = SubmitMutation(redoRequest, out SetBlockCommand redoCommand, out bool requestSentToHost);
            LastMutationResult = result;

            if (!result.Accepted)
                return requestSentToHost;

            redoStack.RemoveAt(redoStack.Count - 1);

            if (undoStack.Count >= GameModeConstants.CreativeUndoHistoryLimit)
                undoStack.RemoveAt(0);
            undoStack.Add(command);

            RebuildChangedChunks();

            if (redoCommand.HasAppliedChange)
                BlockMutationApplied?.Invoke(redoCommand.AppliedChange);

            return true;
        }

        // The block the interaction ray currently points at (null when nothing is targeted).
        // Consumed by the creative tools panel for corner selection / pick-block.
        public BlockPosition? CurrentTarget { get; private set; }

        public bool TryGetBlock(BlockPosition position, out BlockId block)
        {
            if (world != null && world.Bounds.Contains(position))
            {
                block = world.GetBlock(position);
                return true;
            }

            block = BlockRegistry.Air;
            return false;
        }

        public void UpdatePreview(BlockPosition targetPosition, Vector3 faceNormal)
        {
            CurrentTarget = targetPosition;

            if (placementPreview == null)
                return;

            if (!BlockiverseRuntimeState.AllowWorldInput || !blockEditingEnabled)
            {
                placementPreview?.Hide();
                return;
            }

            BlockPosition placement = ComputePlacementPosition(targetPosition, faceNormal);
            placementPreview.ShowAt(placement, CanPlaceBlock(placement));
        }

        public void HidePreview()
        {
            CurrentTarget = null;
            placementPreview?.Hide();
        }

        public bool ToggleBlockEditingEnabled()
        {
            SetBlockEditingEnabled(!blockEditingEnabled);
            return blockEditingEnabled;
        }

        public void SetBlockEditingEnabled(bool enabled)
        {
            if (blockEditingEnabled == enabled)
                return;

            blockEditingEnabled = enabled;

            if (!blockEditingEnabled)
                HidePreview();

            BlockEditingEnabledChanged?.Invoke(blockEditingEnabled);
        }

        void EnsureConfigured()
        {
            if (world == null || registry == null || mutationAuthority == null)
                throw new InvalidOperationException("Creative interaction controller has not been configured.");
        }

        bool TryMutate(BlockPosition position, BlockId newBlock, bool pushUndo)
        {
            if (!BlockiverseRuntimeState.AllowWorldInput)
                return RejectWorldInputBlocked(position);

            if (!blockEditingEnabled)
                return RejectBlockEditingDisabled(position);

            BlockMutationRequest request = CreateMutationRequest(position, newBlock);
            BlockMutationResult result = SubmitMutation(request, out SetBlockCommand command, out bool requestSentToHost);
            LastMutationResult = result;

            if (!result.Accepted)
                return requestSentToHost;

            if (pushUndo)
            {
                if (undoStack.Count >= GameModeConstants.CreativeUndoHistoryLimit)
                    undoStack.RemoveAt(0);
                undoStack.Add(command);
                // A fresh edit invalidates the redo branch (standard undo/redo semantics).
                redoStack.Clear();
            }

            RebuildChangedChunks();

            if (command.HasAppliedChange)
                BlockMutationApplied?.Invoke(command.AppliedChange);

            return true;
        }

        bool RejectBlockEditingDisabled(BlockPosition position)
        {
            LastMutationResult = BlockMutationResult.Reject(
                BlockMutationRejectionReason.BlockEditingDisabled,
                ChunkCoordinate.FromBlockPosition(position, world.ChunkSize),
                "Block editing is disabled.");
            return false;
        }

        bool RejectWorldInputBlocked(BlockPosition position)
        {
            LastMutationResult = BlockMutationResult.Reject(
                BlockMutationRejectionReason.WorldInputBlocked,
                ChunkCoordinate.FromBlockPosition(position, world.ChunkSize),
                "World input is blocked by the active UI screen.");
            return false;
        }

        BlockMutationResult SubmitMutation(
            BlockMutationRequest request,
            out SetBlockCommand command,
            out bool requestSentToHost)
        {
            if (chunkAuthoritySync != null)
                return chunkAuthoritySync.TrySubmitMutation(request, out command, out requestSentToHost);

            requestSentToHost = false;
            return mutationAuthority.TryCommit(request, out command);
        }

        BlockMutationRequest CreateMutationRequest(BlockPosition position, BlockId newBlock)
        {
            ChunkAuthorityBoundary boundary = ResolveEffectiveBoundary();

            if (world.Bounds.Contains(position))
            {
                return new BlockMutationRequest(
                    boundary.LocalClientId,
                    position,
                    newBlock,
                    expectedCurrentBlock: world.GetBlock(position));
            }

            return new BlockMutationRequest(boundary.LocalClientId, position, newBlock);
        }

        ChunkAuthorityBoundary ResolveEffectiveBoundary()
        {
            return chunkAuthoritySync != null ? chunkAuthoritySync.CurrentBoundary : mutationAuthority.Boundary;
        }

        void RebuildChangedChunks()
        {
            worldRenderer?.RebuildDirty();
        }

    }
}

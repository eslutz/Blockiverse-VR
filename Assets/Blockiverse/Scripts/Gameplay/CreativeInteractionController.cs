using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class CreativeInteractionController : MonoBehaviour
    {
        readonly Stack<SetBlockCommand> undoStack = new();

        VoxelWorld world;
        BlockRegistry registry;
        CreativeHotbar hotbar;
        PlacementPreview placementPreview;
        VoxelWorldRenderer worldRenderer;
        BlockMutationAuthority mutationAuthority;
        MultiplayerChunkAuthoritySync chunkAuthoritySync;
        Bounds? playerBounds;

        public BlockMutationAuthority MutationAuthority => mutationAuthority;
        public BlockMutationResult LastMutationResult { get; private set; }

        /// <summary>Raised after a block mutation is locally applied so presentation systems (audio, haptics) can react.</summary>
        public event Action<BlockChange> BlockMutationApplied;

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
            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            hotbar = creativeHotbar;
            placementPreview = preview;
            playerBounds = playerCollisionBounds;
            worldRenderer = renderer;
            mutationAuthority = authority ?? BlockMutationAuthority.CreateHost(world, registry);
            chunkAuthoritySync = authoritySync;
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
            Vector3 pointInsideBlock = hitPoint - faceNormal.normalized * 0.001f;
            return new BlockPosition(
                Mathf.FloorToInt(pointInsideBlock.x),
                Mathf.FloorToInt(pointInsideBlock.y),
                Mathf.FloorToInt(pointInsideBlock.z));
        }

        public bool TryBreakBlock(BlockPosition position)
        {
            EnsureConfigured();

            return TryMutate(position, BlockRegistry.Air, pushUndo: true);
        }

        public bool TryPlaceBlock(BlockPosition targetPosition, Vector3 faceNormal)
        {
            return TryPlaceAt(ComputePlacementPosition(targetPosition, faceNormal));
        }

        public bool TryPlaceAt(BlockPosition position)
        {
            EnsureConfigured();

            if (hotbar == null || !CanPlaceBlock(position))
                return false;

            BlockId selectedBlock = hotbar.SelectedBlockId;

            if (selectedBlock == BlockRegistry.Air)
                return false;

            registry.Get(selectedBlock);
            return TryMutate(position, selectedBlock, pushUndo: true);
        }

        public bool CanPlaceBlock(BlockPosition position)
        {
            EnsureConfigured();

            if (!world.Bounds.Contains(position))
                return false;

            if (world.GetBlock(position) != BlockRegistry.Air)
                return false;

            if (playerBounds.HasValue && GetBlockBounds(position).Intersects(playerBounds.Value))
                return false;

            return true;
        }

        public bool UndoLast()
        {
            EnsureConfigured();

            if (undoStack.Count == 0)
                return false;

            SetBlockCommand command = undoStack.Peek();

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

            undoStack.Pop();
            RebuildChangedChunks();

            if (undoCommand.HasAppliedChange)
                BlockMutationApplied?.Invoke(undoCommand.AppliedChange);

            return true;
        }

        public void UpdatePreview(BlockPosition targetPosition, Vector3 faceNormal)
        {
            if (placementPreview == null)
                return;

            BlockPosition placement = ComputePlacementPosition(targetPosition, faceNormal);
            placementPreview.ShowAt(placement, CanPlaceBlock(placement));
        }

        public void HidePreview()
        {
            placementPreview?.Hide();
        }

        void EnsureConfigured()
        {
            if (world == null || registry == null || mutationAuthority == null)
                throw new InvalidOperationException("Creative interaction controller has not been configured.");
        }

        bool TryMutate(BlockPosition position, BlockId newBlock, bool pushUndo)
        {
            BlockMutationRequest request = CreateMutationRequest(position, newBlock);
            BlockMutationResult result = SubmitMutation(request, out SetBlockCommand command, out bool requestSentToHost);
            LastMutationResult = result;

            if (!result.Accepted)
                return requestSentToHost;

            if (pushUndo)
                undoStack.Push(command);

            RebuildChangedChunks();

            if (command.HasAppliedChange)
                BlockMutationApplied?.Invoke(command.AppliedChange);

            return true;
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

        static Bounds GetBlockBounds(BlockPosition position)
        {
            return new Bounds(
                new Vector3(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f),
                Vector3.one);
        }
    }
}

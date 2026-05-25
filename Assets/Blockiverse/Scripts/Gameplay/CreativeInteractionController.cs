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
        Bounds? playerBounds;

        public void Configure(
            VoxelWorld voxelWorld,
            BlockRegistry blockRegistry,
            CreativeHotbar creativeHotbar,
            PlacementPreview preview,
            Bounds? playerCollisionBounds)
        {
            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            hotbar = creativeHotbar;
            placementPreview = preview;
            playerBounds = playerCollisionBounds;
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

            if (!world.Bounds.Contains(position) || world.GetBlock(position) == BlockRegistry.Air)
                return false;

            var command = new SetBlockCommand(position, BlockRegistry.Air);
            command.Execute(world);
            undoStack.Push(command);
            return true;
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
            var command = new SetBlockCommand(position, selectedBlock);
            command.Execute(world);
            undoStack.Push(command);
            return true;
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
            if (undoStack.Count == 0)
                return false;

            SetBlockCommand command = undoStack.Pop();
            command.Undo(world);
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
            if (world == null || registry == null)
                throw new InvalidOperationException("Creative interaction controller has not been configured.");
        }

        static Bounds GetBlockBounds(BlockPosition position)
        {
            return new Bounds(
                new Vector3(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f),
                Vector3.one);
        }
    }
}

using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Gameplay
{
    public enum WorldEditResult
    {
        Success,
        VolumeLimitExceeded,
        OutOfBounds,
        NoClipboard,
        NothingToUndo,
        NothingToRedo,
    }

    public sealed class WorldEditService
    {
        readonly List<BlockChange[]> undoHistory = new();
        readonly List<BlockChange[]> redoHistory = new();

        BlockId[] clipboard;
        int clipboardWidth;
        int clipboardHeight;
        int clipboardDepth;

        public bool HasClipboard => clipboard != null;
        public int UndoCount => undoHistory.Count;
        public int RedoCount => redoHistory.Count;

        public WorldEditResult Fill(VoxelWorld world, BlockPosition min, BlockPosition max, BlockId block)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (!ContainedInWorld(world, min, max))
                return WorldEditResult.OutOfBounds;

            int volume = ComputeVolume(min, max);

            if (volume > GameModeConstants.CreativeMaxFillVolume)
                return WorldEditResult.VolumeLimitExceeded;

            var changes = new List<BlockChange>(volume);
            ApplyToRegion(world, min, max, block, changes);
            PushUndo(changes);
            return WorldEditResult.Success;
        }

        public WorldEditResult Replace(VoxelWorld world, BlockPosition min, BlockPosition max, BlockId target, BlockId replacement)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (!ContainedInWorld(world, min, max))
                return WorldEditResult.OutOfBounds;

            int volume = ComputeVolume(min, max);

            if (volume > GameModeConstants.CreativeMaxReplaceVolume)
                return WorldEditResult.VolumeLimitExceeded;

            var changes = new List<BlockChange>();

            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
            for (int x = min.X; x <= max.X; x++)
            {
                var pos = new BlockPosition(x, y, z);
                BlockId current = world.GetBlock(pos);

                if (current != target)
                    continue;

                BlockId previous = current;
                world.SetBlock(pos, replacement);
                changes.Add(new BlockChange(pos, previous, replacement));
            }

            if (changes.Count > 0)
                PushUndo(changes);

            return WorldEditResult.Success;
        }

        public WorldEditResult Delete(VoxelWorld world, BlockPosition min, BlockPosition max)
        {
            return Fill(world, min, max, BlockRegistry.Air);
        }

        public WorldEditResult Copy(VoxelWorld world, BlockPosition min, BlockPosition max)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (!ContainedInWorld(world, min, max))
                return WorldEditResult.OutOfBounds;

            int volume = ComputeVolume(min, max);

            if (volume > GameModeConstants.CreativeMaxCopyVolume)
                return WorldEditResult.VolumeLimitExceeded;

            clipboardWidth  = max.X - min.X + 1;
            clipboardHeight = max.Y - min.Y + 1;
            clipboardDepth  = max.Z - min.Z + 1;
            clipboard = new BlockId[volume];

            int idx = 0;
            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
            for (int x = min.X; x <= max.X; x++)
                clipboard[idx++] = world.GetBlock(new BlockPosition(x, y, z));

            return WorldEditResult.Success;
        }

        public WorldEditResult Paste(VoxelWorld world, BlockPosition origin)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (clipboard == null)
                return WorldEditResult.NoClipboard;

            var max = new BlockPosition(
                origin.X + clipboardWidth - 1,
                origin.Y + clipboardHeight - 1,
                origin.Z + clipboardDepth - 1);

            if (!ContainedInWorld(world, origin, max))
                return WorldEditResult.OutOfBounds;

            var changes = new List<BlockChange>(clipboard.Length);

            int idx = 0;
            for (int y = 0; y < clipboardHeight; y++)
            for (int z = 0; z < clipboardDepth; z++)
            for (int x = 0; x < clipboardWidth; x++)
            {
                var pos = new BlockPosition(origin.X + x, origin.Y + y, origin.Z + z);
                BlockId previous = world.GetBlock(pos);
                BlockId newBlock = clipboard[idx++];
                world.SetBlock(pos, newBlock);
                changes.Add(new BlockChange(pos, previous, newBlock));
            }

            PushUndo(changes);
            return WorldEditResult.Success;
        }

        public WorldEditResult Undo(VoxelWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (undoHistory.Count == 0)
                return WorldEditResult.NothingToUndo;

            BlockChange[] changes = undoHistory[undoHistory.Count - 1];
            undoHistory.RemoveAt(undoHistory.Count - 1);

            for (int i = changes.Length - 1; i >= 0; i--)
                world.SetBlock(changes[i].Position, changes[i].PreviousBlock);

            redoHistory.Add(changes);
            return WorldEditResult.Success;
        }

        public WorldEditResult Redo(VoxelWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (redoHistory.Count == 0)
                return WorldEditResult.NothingToRedo;

            BlockChange[] changes = redoHistory[redoHistory.Count - 1];
            redoHistory.RemoveAt(redoHistory.Count - 1);

            for (int i = 0; i < changes.Length; i++)
                world.SetBlock(changes[i].Position, changes[i].NewBlock);

            undoHistory.Add(changes);
            return WorldEditResult.Success;
        }

        void PushUndo(List<BlockChange> changes)
        {
            if (undoHistory.Count >= GameModeConstants.CreativeUndoHistoryLimit)
                undoHistory.RemoveAt(0);

            undoHistory.Add(changes.ToArray());
            redoHistory.Clear();
        }

        static void ApplyToRegion(VoxelWorld world, BlockPosition min, BlockPosition max, BlockId block, List<BlockChange> changes)
        {
            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
            for (int x = min.X; x <= max.X; x++)
            {
                var pos = new BlockPosition(x, y, z);
                BlockId previous = world.GetBlock(pos);
                world.SetBlock(pos, block);
                changes.Add(new BlockChange(pos, previous, block));
            }
        }

        static int ComputeVolume(BlockPosition min, BlockPosition max)
        {
            return (max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
        }

        static bool ContainedInWorld(VoxelWorld world, BlockPosition min, BlockPosition max)
        {
            return world.Bounds.Contains(min) && world.Bounds.Contains(max);
        }
    }
}

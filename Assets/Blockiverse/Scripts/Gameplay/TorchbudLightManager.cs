using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class TorchbudLightManager : MonoBehaviour
    {
        readonly HashSet<BlockPosition> emitterPositions = new();

        VoxelWorld world;
        BlockRegistry blockRegistry;
        BlockiverseAudioCuePlayer audioCuePlayer;
        BlockiverseVfxCuePlayer vfxCuePlayer;
        // Suppressed during the initial full-world rebuild so loading a save does not chorus
        // dozens of ignite cues at once; only live placements crackle.
        bool igniteFeedbackEnabled;

        public int ActiveEmitterCount => emitterPositions.Count;
        public int ActiveLightCount => ActiveEmitterCount;

        public static bool IsLightEmitter(BlockId block, BlockRegistry registry)
        {
            return registry != null && registry.TryGet(block, out BlockDefinition def) && def.EmissiveLight > 0;
        }

        public static Vector3 GetLightPosition(BlockPosition position)
        {
            return new Vector3(position.X + 0.5f, position.Y + 0.86f, position.Z + 0.5f);
        }

        public void Configure(VoxelWorld voxelWorld, BlockRegistry blockRegistry)
        {
            if (world != null)
                world.BlockChanged -= OnBlockChanged;

            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            this.blockRegistry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            world.BlockChanged += OnBlockChanged;
            igniteFeedbackEnabled = false;
            RebuildAllLights();
            igniteFeedbackEnabled = true;
        }

        public bool TryGetLight(BlockPosition position, out Light light)
        {
            light = null;
            return false;
        }

        public bool IsTrackingEmitter(BlockPosition position)
        {
            return emitterPositions.Contains(position);
        }

        void RebuildAllLights()
        {
            ClearLights();

            for (int y = 0; y < world.Bounds.Height; y++)
            {
                for (int z = 0; z < world.Bounds.Depth; z++)
                {
                    for (int x = 0; x < world.Bounds.Width; x++)
                    {
                        var position = new BlockPosition(x, y, z);
                        if (IsLightEmitter(world.GetBlock(position), blockRegistry))
                            AddLight(position);
                    }
                }
            }
        }

        void OnBlockChanged(BlockChange change)
        {
            if (IsLightEmitter(change.PreviousBlock, blockRegistry))
                RemoveLight(change.Position);

            if (IsLightEmitter(change.NewBlock, blockRegistry))
                AddLight(change.Position);
        }

        void AddLight(BlockPosition position)
        {
            if (!emitterPositions.Add(position))
                return;

            PlayIgniteFeedback(position);
        }

        void PlayIgniteFeedback(BlockPosition position)
        {
            if (!igniteFeedbackEnabled || !Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();
            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();

            Vector3 lightPosition = GetLightPosition(position);
            audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.TorchIgnite, lightPosition);
            vfxCuePlayer?.PlayCue(BlockiverseVfxCue.TorchSpark, lightPosition);
        }

        void RemoveLight(BlockPosition position)
        {
            emitterPositions.Remove(position);
        }

        void ClearLights()
        {
            emitterPositions.Clear();
        }

        void OnDestroy()
        {
            if (world != null)
                world.BlockChanged -= OnBlockChanged;

            ClearLights();
        }
    }
}

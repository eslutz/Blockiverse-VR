using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class TorchbudLightManager : MonoBehaviour
    {
        const int MaxRuntimePointLights = 24;
        const float MinimumLightRange = 4.0f;
        const float LightRangePerLevel = 0.42f;
        const float MinimumLightIntensity = 0.8f;
        const float LightIntensityPerLevel = 0.11f;

        readonly HashSet<BlockPosition> emitterPositions = new();
        readonly Dictionary<BlockPosition, Light> lightsByPosition = new();

        VoxelWorld world;
        BlockRegistry blockRegistry;
        BlockiverseAudioCuePlayer audioCuePlayer;
        BlockiverseVfxCuePlayer vfxCuePlayer;
        // Suppressed during the initial full-world rebuild so loading a save does not chorus
        // dozens of ignite cues at once; only live placements crackle.
        bool igniteFeedbackEnabled;

        public int ActiveEmitterCount => emitterPositions.Count;
        public int ActiveLightCount => lightsByPosition.Count;

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
            return lightsByPosition.TryGetValue(position, out light);
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

            FillLightSlots();
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
            if (!emitterPositions.Remove(position))
                return;

            DestroyLight(position);
            FillLightSlots();
        }

        void ClearLights()
        {
            foreach (Light light in lightsByPosition.Values)
                DestroyLight(light);

            lightsByPosition.Clear();
            emitterPositions.Clear();
        }

        void FillLightSlots()
        {
            if (world == null || blockRegistry == null)
                return;

            foreach (BlockPosition position in emitterPositions)
            {
                if (lightsByPosition.Count >= MaxRuntimePointLights)
                    break;

                if (!lightsByPosition.ContainsKey(position))
                    CreateLight(position);
            }
        }

        void CreateLight(BlockPosition position)
        {
            if (!blockRegistry.TryGet(world.GetBlock(position), out BlockDefinition definition) ||
                definition.EmissiveLight <= 0)
                return;

            var lightObject = new GameObject($"Torchbud Light {position}");
            lightObject.transform.SetParent(transform, worldPositionStays: false);
            lightObject.transform.position = GetLightPosition(position);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.range = Mathf.Max(MinimumLightRange, definition.EmissiveLight * LightRangePerLevel);
            light.intensity = MinimumLightIntensity + definition.EmissiveLight * LightIntensityPerLevel;
            light.color = LightColorForBlock(definition.Id);

            lightsByPosition[position] = light;
        }

        void DestroyLight(BlockPosition position)
        {
            if (!lightsByPosition.TryGetValue(position, out Light light))
                return;

            lightsByPosition.Remove(position);
            DestroyLight(light);
        }

        static void DestroyLight(Light light)
        {
            if (light == null)
                return;

            GameObject lightObject = light.gameObject;
            if (Application.isPlaying)
                Destroy(lightObject);
            else
                DestroyImmediate(lightObject);
        }

        static Color LightColorForBlock(BlockId block)
        {
            if (block == BlockRegistry.LumenLamp)
                return new Color(1.0f, 0.92f, 0.64f);
            if (block == BlockRegistry.SparkFlare)
                return new Color(1.0f, 0.72f, 0.28f);
            if (block == BlockRegistry.Campfire || block == BlockRegistry.Emberflow || block == BlockRegistry.EmberflowFlow)
                return new Color(1.0f, 0.45f, 0.18f);
            if (block == BlockRegistry.LumenQuartzCluster)
                return new Color(0.54f, 0.93f, 1.0f);
            if (block == BlockRegistry.StaropalGeode)
                return new Color(0.88f, 0.68f, 1.0f);

            return new Color(1.0f, 0.78f, 0.36f);
        }

        void OnDestroy()
        {
            if (world != null)
                world.BlockChanged -= OnBlockChanged;

            ClearLights();
        }
    }
}

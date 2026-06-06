using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class TorchbudLightManager : MonoBehaviour
    {
        const int MaxActiveLights = 24;
        const float LightRange = 6.0f;
        const float LightIntensity = 2.2f;

        static readonly Color TorchbudLightColor = new(1.0f, 0.62f, 0.28f, 1.0f);

        readonly Dictionary<BlockPosition, Light> lightsByPosition = new();

        VoxelWorld world;

        public int ActiveLightCount => lightsByPosition.Count;

        public static bool IsLightEmitter(BlockId block)
        {
            return block == BlockRegistry.Torchbud;
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
            _ = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            world.BlockChanged += OnBlockChanged;
            RebuildAllLights();
        }

        public bool TryGetLight(BlockPosition position, out Light light)
        {
            return lightsByPosition.TryGetValue(position, out light);
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
                        if (IsLightEmitter(world.GetBlock(position)))
                            AddLight(position);
                    }
                }
            }
        }

        void OnBlockChanged(BlockChange change)
        {
            if (IsLightEmitter(change.PreviousBlock))
                RemoveLight(change.Position);

            if (IsLightEmitter(change.NewBlock))
                AddLight(change.Position);
        }

        void AddLight(BlockPosition position)
        {
            if (lightsByPosition.ContainsKey(position) || lightsByPosition.Count >= MaxActiveLights)
                return;

            var lightObject = new GameObject($"Torchbud Light {position}");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = GetLightPosition(position);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = TorchbudLightColor;
            light.range = LightRange;
            light.intensity = LightIntensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
            lightsByPosition.Add(position, light);
        }

        void RemoveLight(BlockPosition position)
        {
            if (!lightsByPosition.Remove(position, out Light light) || light == null)
                return;

            DestroyUnityObject(light.gameObject);
        }

        void ClearLights()
        {
            foreach (Light light in lightsByPosition.Values)
            {
                if (light != null)
                    DestroyUnityObject(light.gameObject);
            }

            lightsByPosition.Clear();
        }

        void OnDestroy()
        {
            if (world != null)
                world.BlockChanged -= OnBlockChanged;

            ClearLights();
        }

        static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}

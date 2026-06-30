using Blockiverse.Voxel;
using UnityEngine;
using Unity.Profiling;

namespace Blockiverse.Gameplay
{
    public static class VoxelLightSampler
    {
        static readonly ProfilerMarker s_SampleAirLightMarker = new ProfilerMarker("VoxelLightSampler.SampleAirLight");

        public const float SurfaceLight = 1.0f;
        public const float CaveMinimumLight = 0.2f;
        public const float CaveEntranceLight = 0.72f;
        public const int DefaultProbeDistance = 12;
        public const int DefaultEmissiveProbeDistance = 8;

        static readonly BlockPosition[] ProbeDirections =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 0, 1),
            new(0, 0, -1),
            new(0, 1, 0)
        };

        static readonly BlockPosition[] EmissiveProbeDirections =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 0, 1),
            new(0, 0, -1),
            new(0, 1, 0),
            new(0, -1, 0)
        };

        public static float SampleAirLight(
            VoxelWorld world,
            BlockRegistry registry,
            BlockPosition airPosition,
            int maxProbeDistance = DefaultProbeDistance,
            VoxelSkyLightMap skyLight = null)
        {
            using (s_SampleAirLightMarker.Auto())
            {
                if (world == null || registry == null)
                    return SurfaceLight;

                if (!world.Bounds.Contains(airPosition))
                    return SurfaceLight;

                BlockDefinition[] defs = registry.CachedDefinitions;

                if (!IsLightPassable(world, registry, defs, airPosition))
                    return CaveMinimumLight;

                float emissiveLight = SampleEmissiveLight(world, registry, defs, airPosition, maxProbeDistance);

                if (HasSkyAccess(world, registry, defs, airPosition, skyLight))
                    return Mathf.Max(SurfaceLight, emissiveLight);

                int nearestOpening = maxProbeDistance + 1;

                foreach (BlockPosition direction in ProbeDirections)
                {
                    for (int step = 1; step <= maxProbeDistance; step++)
                    {
                        BlockPosition probe = new(
                            airPosition.X + direction.X * step,
                            airPosition.Y + direction.Y * step,
                            airPosition.Z + direction.Z * step);

                        if (!world.Bounds.Contains(probe))
                        {
                            nearestOpening = Mathf.Min(nearestOpening, step);
                            break;
                        }

                        if (!IsLightPassable(world, registry, defs, probe))
                            break;

                        if (HasSkyAccess(world, registry, defs, probe, skyLight))
                        {
                            nearestOpening = Mathf.Min(nearestOpening, step);
                            break;
                        }
                    }
                }

                if (nearestOpening > maxProbeDistance)
                    return Mathf.Max(CaveMinimumLight, emissiveLight);

                float openness = 1.0f - (nearestOpening - 1) / (float)maxProbeDistance;
                return Mathf.Max(Mathf.Lerp(CaveMinimumLight, CaveEntranceLight, openness), emissiveLight);
            }
        }

        public static Color ToVertexColor(float light)
        {
            light = Mathf.Clamp01(light);
            return new Color(light, light, light, 1.0f);
        }

        static bool HasSkyAccess(VoxelWorld world, BlockRegistry registry, BlockDefinition[] defs, BlockPosition airPosition, VoxelSkyLightMap skyLight)
        {
            // The sky-light map answers in O(1); the column walk remains as the fallback for
            // callers without one (isolated tests).
            if (skyLight != null)
                return skyLight.HasSkyAccess(airPosition);

            for (int y = airPosition.Y + 1; y < world.Bounds.Height; y++)
            {
                if (!IsLightPassable(world, registry, defs, new BlockPosition(airPosition.X, y, airPosition.Z)))
                    return false;
            }

            return true;
        }

        static float SampleEmissiveLight(
            VoxelWorld world,
            BlockRegistry registry,
            BlockDefinition[] defs,
            BlockPosition airPosition,
            int maxProbeDistance)
        {
            int probeDistance = Mathf.Min(maxProbeDistance, DefaultEmissiveProbeDistance);
            float strongest = 0.0f;

            foreach (BlockPosition direction in EmissiveProbeDirections)
            {
                for (int step = 1; step <= probeDistance; step++)
                {
                    BlockPosition probe = new(
                        airPosition.X + direction.X * step,
                        airPosition.Y + direction.Y * step,
                        airPosition.Z + direction.Z * step);

                    if (!world.Bounds.Contains(probe))
                        break;

                    BlockDefinition definition = GetDefinition(registry, defs, world.GetBlock(probe));
                    if (definition.EmissiveLight > 0)
                    {
                        float normalized = definition.EmissiveLight / 15.0f;
                        float falloff = Mathf.Lerp(1.0f, 0.25f, (step - 1) / (float)probeDistance);
                        strongest = Mathf.Max(strongest, normalized * falloff);
                        break;
                    }

                    if (!IsLightPassable(definition))
                        break;
                }
            }

            return strongest;
        }

        static bool IsLightPassable(VoxelWorld world, BlockRegistry registry, BlockDefinition[] defs, BlockPosition position)
        {
            BlockDefinition definition = GetDefinition(registry, defs, world.GetBlock(position));
            return IsLightPassable(definition);
        }

        static bool IsLightPassable(BlockDefinition definition) =>
            !definition.IsRenderable || !definition.IsSolid;

        static BlockDefinition GetDefinition(BlockRegistry registry, BlockDefinition[] defs, BlockId id)
        {
            int val = id.Value;
            if (defs != null && val >= 0 && val < defs.Length)
            {
                BlockDefinition def = defs[val];
                if (def != null)
                    return def;
            }
            return registry.Get(id);
        }
    }
}

using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public static class VoxelLightSampler
    {
        public const float SurfaceLight = 1.0f;
        public const float CaveMinimumLight = 0.2f;
        public const float CaveEntranceLight = 0.72f;
        public const int DefaultProbeDistance = 12;

        static readonly BlockPosition[] ProbeDirections =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 0, 1),
            new(0, 0, -1),
            new(0, 1, 0)
        };

        public static float SampleAirLight(
            VoxelWorld world,
            BlockRegistry registry,
            BlockPosition airPosition,
            int maxProbeDistance = DefaultProbeDistance)
        {
            if (world == null || registry == null)
                return SurfaceLight;

            if (!world.Bounds.Contains(airPosition))
                return SurfaceLight;

            if (!IsLightPassable(world, registry, airPosition))
                return CaveMinimumLight;

            if (HasSkyAccess(world, registry, airPosition))
                return SurfaceLight;

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

                    if (!IsLightPassable(world, registry, probe))
                        break;

                    if (HasSkyAccess(world, registry, probe))
                    {
                        nearestOpening = Mathf.Min(nearestOpening, step);
                        break;
                    }
                }
            }

            if (nearestOpening > maxProbeDistance)
                return CaveMinimumLight;

            float openness = 1.0f - (nearestOpening - 1) / (float)maxProbeDistance;
            return Mathf.Lerp(CaveMinimumLight, CaveEntranceLight, openness);
        }

        public static Color ToVertexColor(float light)
        {
            light = Mathf.Clamp01(light);
            return new Color(light, light, light, 1.0f);
        }

        static bool HasSkyAccess(VoxelWorld world, BlockRegistry registry, BlockPosition airPosition)
        {
            for (int y = airPosition.Y + 1; y < world.Bounds.Height; y++)
            {
                if (!IsLightPassable(world, registry, new BlockPosition(airPosition.X, y, airPosition.Z)))
                    return false;
            }

            return true;
        }

        static bool IsLightPassable(VoxelWorld world, BlockRegistry registry, BlockPosition position)
        {
            BlockDefinition definition = registry.Get(world.GetBlock(position));
            return !definition.IsRenderable || !definition.IsSolid;
        }
    }
}

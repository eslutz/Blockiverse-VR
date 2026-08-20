using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;
using Unity.Profiling;

namespace Blockiverse.Gameplay
{
    // Bakes the per-face light data that the Voxel Lit shader reads from vertex colour:
    //   R = sky exposure   (gates sun/moon/ambient — 1 under open sky, 0 fully enclosed)
    //   G = emitter reach  (gates realtime point lights — 1 if any emitter in range has clear
    //                       line of sight to the face, else 0; this is what stops a glowwick
    //                       outside a wall, or above the ground, lighting the far side)
    //   B = self-emission  (the block's own emissive level / 15, so a torch stays visible in
    //                       the dark it creates)
    // SampleAirLight keeps its original max(sky, emissive) contract on the 0–15 gameplay scale;
    // crop growth reads it and tests pin it.
    public static class VoxelLightSampler
    {
        static readonly ProfilerMarker s_SampleAirLightMarker = new ProfilerMarker("VoxelLightSampler.SampleAirLight");
        static readonly ProfilerMarker s_SampleEmitterReachMarker = new ProfilerMarker("VoxelLightSampler.SampleEmitterReach");

        public const float SurfaceLight = 1.0f;

        // Fully enclosed cells receive no sky light at all. A sealed room or a deep tunnel is dark
        // unless something in it emits; the ruleset's visibleLight = max(skyLight, blockLight)
        // sanctions 0 (voxel_world_environment_effects.md §5.4 "new-moon storm night -> 0").
        public const float CaveMinimumLight = 0.0f;
        public const float CaveEntranceLight = 0.72f;
        public const int DefaultProbeDistance = 12;
        public const int DefaultEmissiveProbeDistance = 8;

        // Canonical maximum emissive level (spark_flare). A level-L emitter lights L blocks.
        public const int MaxEmissiveLevel = 15;

        // How far the line-of-sight bake looks for emitters: the brightest emitter's range plus a
        // one-block margin so the bake never cuts off inside the realtime light's own range.
        public const int MaxEmitterReachDistance = MaxEmissiveLevel + 1;

        // Where an emitter's realtime point light sits inside its block. The LOS bake aims at the
        // same point so the two agree on what is and is not occluded.
        public static readonly Vector3 EmitterLightOffset = new(0.5f, 0.86f, 0.5f);

        // Nudge off the face plane into the adjacent air cell before tracing, so the ray's start
        // cell is unambiguous.
        const float FaceRayEpsilon = 0.01f;

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

        // Combined gameplay light on the 0..1 scale: max(sky, emissive), the ruleset's visibleLight.
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
                float sky = SampleSkyExposureCore(world, registry, defs, airPosition, maxProbeDistance, skyLight);
                return Mathf.Max(sky, emissiveLight);
            }
        }

        // Sky exposure only (0..1). This is what gates the sun, moon and ambient in the shader —
        // emissive blocks must NOT raise it, or a torch in a cave would let sunlight in.
        public static float SampleSkyExposure(
            VoxelWorld world,
            BlockRegistry registry,
            BlockPosition airPosition,
            int maxProbeDistance = DefaultProbeDistance,
            VoxelSkyLightMap skyLight = null)
        {
            if (world == null || registry == null)
                return SurfaceLight;

            if (!world.Bounds.Contains(airPosition))
                return SurfaceLight;

            BlockDefinition[] defs = registry.CachedDefinitions;

            if (!IsLightPassable(world, registry, defs, airPosition))
                return CaveMinimumLight;

            return SampleSkyExposureCore(world, registry, defs, airPosition, maxProbeDistance, skyLight);
        }

        static float SampleSkyExposureCore(
            VoxelWorld world,
            BlockRegistry registry,
            BlockDefinition[] defs,
            BlockPosition airPosition,
            int maxProbeDistance,
            VoxelSkyLightMap skyLight)
        {
            if (HasSkyAccess(world, registry, defs, airPosition, skyLight))
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
                return CaveMinimumLight;

            float openness = 1.0f - (nearestOpening - 1) / (float)maxProbeDistance;
            return Mathf.Lerp(CaveMinimumLight, CaveEntranceLight, openness);
        }

        // 1 if any emitter in `emitters` is within its range of the face, in front of it, and has
        // an unobstructed straight line to it; otherwise 0. `airPosition` is the air cell the face
        // looks into and `faceNormal` points from the solid block into that cell.
        public static float SampleEmitterReach(
            VoxelWorld world,
            BlockRegistry registry,
            BlockPosition airPosition,
            BlockPosition faceNormal,
            IReadOnlyList<BlockPosition> emitters)
        {
            using (s_SampleEmitterReachMarker.Auto())
            {
                if (world == null || registry == null || emitters == null || emitters.Count == 0)
                    return 0.0f;

                BlockDefinition[] defs = registry.CachedDefinitions;
                var normal = new Vector3(faceNormal.X, faceNormal.Y, faceNormal.Z);
                // The face plane sits between the solid block and airPosition; its centre is the
                // air cell's centre pulled back half a block against the normal.
                Vector3 facePoint = new Vector3(airPosition.X + 0.5f, airPosition.Y + 0.5f, airPosition.Z + 0.5f)
                                    - normal * (0.5f - FaceRayEpsilon);

                for (int i = 0; i < emitters.Count; i++)
                {
                    BlockPosition emitter = emitters[i];
                    BlockDefinition definition = GetDefinition(registry, defs, world.GetBlock(emitter));
                    if (definition.EmissiveLight <= 0)
                        continue;

                    Vector3 lightPoint = new Vector3(emitter.X, emitter.Y, emitter.Z) + EmitterLightOffset;
                    Vector3 toLight = lightPoint - facePoint;

                    // Behind the face: the realtime light's N·L is zero there anyway.
                    if (Vector3.Dot(toLight, normal) <= 0.0f)
                        continue;

                    float reach = definition.EmissiveLight + 1.0f;
                    if (toLight.sqrMagnitude > reach * reach)
                        continue;

                    if (HasLineOfSight(world, registry, defs, facePoint, lightPoint, emitter))
                        return 1.0f;
                }

                return 0.0f;
            }
        }

        // Amanatides–Woo voxel traversal from `from` to `to`. Every cell entered before the
        // emitter's own cell must be light-passable. The start cell is the face's air neighbour,
        // which is passable by construction.
        static bool HasLineOfSight(
            VoxelWorld world,
            BlockRegistry registry,
            BlockDefinition[] defs,
            Vector3 from,
            Vector3 to,
            BlockPosition endCell)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-4f)
                return true;

            Vector3 dir = delta / length;

            int x = Mathf.FloorToInt(from.x);
            int y = Mathf.FloorToInt(from.y);
            int z = Mathf.FloorToInt(from.z);

            int stepX = dir.x > 0f ? 1 : dir.x < 0f ? -1 : 0;
            int stepY = dir.y > 0f ? 1 : dir.y < 0f ? -1 : 0;
            int stepZ = dir.z > 0f ? 1 : dir.z < 0f ? -1 : 0;

            float tDeltaX = stepX != 0 ? Mathf.Abs(1f / dir.x) : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? Mathf.Abs(1f / dir.y) : float.PositiveInfinity;
            float tDeltaZ = stepZ != 0 ? Mathf.Abs(1f / dir.z) : float.PositiveInfinity;

            float tMaxX = stepX > 0 ? (x + 1 - from.x) / dir.x : stepX < 0 ? (x - from.x) / dir.x : float.PositiveInfinity;
            float tMaxY = stepY > 0 ? (y + 1 - from.y) / dir.y : stepY < 0 ? (y - from.y) / dir.y : float.PositiveInfinity;
            float tMaxZ = stepZ > 0 ? (z + 1 - from.z) / dir.z : stepZ < 0 ? (z - from.z) / dir.z : float.PositiveInfinity;

            // Bounded by the longest possible traversal so a degenerate direction can never spin.
            int maxSteps = 3 * (MaxEmitterReachDistance + 2);
            for (int i = 0; i < maxSteps; i++)
            {
                float t;
                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    x += stepX; t = tMaxX; tMaxX += tDeltaX;
                }
                else if (tMaxY < tMaxZ)
                {
                    y += stepY; t = tMaxY; tMaxY += tDeltaY;
                }
                else
                {
                    z += stepZ; t = tMaxZ; tMaxZ += tDeltaZ;
                }

                if (t >= length)
                    return true;

                if (x == endCell.X && y == endCell.Y && z == endCell.Z)
                    return true;

                var cell = new BlockPosition(x, y, z);
                if (!world.Bounds.Contains(cell))
                    return true;

                if (!IsLightPassable(world, registry, defs, cell))
                    return false;
            }

            return true;
        }

        public static Color ToVertexColor(float skyExposure, float emitterReach, float selfEmission)
        {
            return new Color(
                Mathf.Clamp01(skyExposure),
                Mathf.Clamp01(emitterReach),
                Mathf.Clamp01(selfEmission),
                1.0f);
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
                        float normalized = definition.EmissiveLight / (float)MaxEmissiveLevel;
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

        public static bool IsLightPassable(VoxelWorld world, BlockRegistry registry, BlockDefinition[] defs, BlockPosition position)
        {
            BlockDefinition definition = GetDefinition(registry, defs, world.GetBlock(position));
            return IsLightPassable(definition);
        }

        public static bool IsLightPassable(BlockDefinition definition) =>
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

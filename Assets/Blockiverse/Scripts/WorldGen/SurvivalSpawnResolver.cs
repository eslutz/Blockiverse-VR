using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    // Chooses a stable spawn elevation from the terrain surrounding the centre of a world. The
    // annulus deliberately starts outside the protected pad so the pad meets its real landscape
    // rather than inheriting an arbitrary sea-level height.
    public static class SurvivalSpawnResolver
    {
        const int InnerSampleRadius = 6;
        const int OuterSampleRadius = 8;
        const int SpawnHeadroom = 3;

        public static BlockPosition Resolve(int seed, int width, int height, int depth)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < SpawnHeadroom + 2)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (depth < 1)
                throw new ArgumentOutOfRangeException(nameof(depth));

            int centerX = width / 2;
            int centerZ = depth / 2;
            var samples = new List<int>();
            var terrain = new SurvivalBiomeResolver(seed, height);
            int innerSquared = InnerSampleRadius * InnerSampleRadius;
            int outerSquared = OuterSampleRadius * OuterSampleRadius;

            for (int dx = -OuterSampleRadius; dx <= OuterSampleRadius; dx++)
            {
                for (int dz = -OuterSampleRadius; dz <= OuterSampleRadius; dz++)
                {
                    int distanceSquared = dx * dx + dz * dz;
                    if (distanceSquared < innerSquared || distanceSquared > outerSquared)
                        continue;

                    int x = centerX + dx;
                    int z = centerZ + dz;
                    if (x >= 0 && x < width && z >= 0 && z < depth)
                        samples.Add(terrain.SurfaceHeight(x, z));
                }
            }

            if (samples.Count == 0)
                samples.Add(terrain.SurfaceHeight(centerX, centerZ));

            samples.Sort();
            int floorY = samples[samples.Count / 2];
            floorY = Math.Min(floorY, height - SpawnHeadroom - 1);
            return new BlockPosition(centerX, floorY + 1, centerZ);
        }
    }
}

using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    public readonly struct ResourceVeinTuning
    {
        public ResourceVeinTuning(
            BlockId resourceBlock,
            int salt,
            int minY,
            int maxY,
            int chancePermille,
            int radius,
            int verticalRadius)
        {
            if (minY < 0 || maxY < minY)
                throw new ArgumentOutOfRangeException(nameof(minY), "Resource depth range is invalid.");

            if (chancePermille < 0 || chancePermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(chancePermille), "Resource chance must be between 0 and 1000 permille.");

            if (radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(radius), "Resource vein radius must be positive.");

            if (verticalRadius <= 0)
                throw new ArgumentOutOfRangeException(nameof(verticalRadius), "Resource vein vertical radius must be positive.");

            ResourceBlock = resourceBlock;
            Salt = salt;
            MinY = minY;
            MaxY = maxY;
            ChancePermille = chancePermille;
            Radius = radius;
            VerticalRadius = verticalRadius;
        }

        public BlockId ResourceBlock { get; }
        public int Salt { get; }
        public int MinY { get; }
        public int MaxY { get; }
        public int ChancePermille { get; }
        public int Radius { get; }
        public int VerticalRadius { get; }
    }

    public sealed class SurvivalResourceTuning
    {
        readonly Dictionary<BlockId, ResourceVeinTuning> tuningByBlock = new();
        readonly ResourceVeinTuning[] resourceVeins;

        public SurvivalResourceTuning(params ResourceVeinTuning[] resourceVeins)
        {
            if (resourceVeins == null || resourceVeins.Length == 0)
                throw new ArgumentException("At least one resource vein tuning entry is required.", nameof(resourceVeins));

            this.resourceVeins = new ResourceVeinTuning[resourceVeins.Length];
            for (int i = 0; i < resourceVeins.Length; i++)
            {
                ResourceVeinTuning tuning = resourceVeins[i];
                if (tuningByBlock.ContainsKey(tuning.ResourceBlock))
                    throw new InvalidOperationException($"Duplicate resource vein tuning for block: {tuning.ResourceBlock}");

                this.resourceVeins[i] = tuning;
                tuningByBlock.Add(tuning.ResourceBlock, tuning);
            }
        }

        public IReadOnlyList<ResourceVeinTuning> ResourceVeins => resourceVeins;

        public static SurvivalResourceTuning CreateDefault()
        {
            return new SurvivalResourceTuning(
                new ResourceVeinTuning(BlockRegistry.EmbercoalSeam,      salt: 701, minY: 35, maxY: 135, chancePermille: 270, radius: 2, verticalRadius: 2),
                new ResourceVeinTuning(BlockRegistry.RosycopperBloom,    salt: 809, minY: 45, maxY: 150, chancePermille: 145, radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.RustcoreOre,        salt: 907, minY: 15, maxY: 95,  chancePermille: 80,  radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.SunmetalFleck,      salt: 1013, minY: 10, maxY: 65, chancePermille: 35,  radius: 1, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.LumenQuartzCluster, salt: 1117, minY: 15, maxY: 120, chancePermille: 60, radius: 2, verticalRadius: 1));
        }

        public ResourceVeinTuning Get(BlockId resourceBlock)
        {
            if (!tuningByBlock.TryGetValue(resourceBlock, out ResourceVeinTuning tuning))
                throw new KeyNotFoundException($"No resource tuning is registered for block: {resourceBlock}");

            return tuning;
        }
    }
}

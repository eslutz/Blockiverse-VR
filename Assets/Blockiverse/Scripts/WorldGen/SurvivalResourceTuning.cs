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
            // Depth bands and rarity follow the tool-tier ladder (§7.1): tier-2 materials sit
            // shallow and common, tier-5/6 deep and rare, so every smelting recipe (§9.3/§9.4)
            // has an in-world source.
            return new SurvivalResourceTuning(
                new ResourceVeinTuning(BlockRegistry.EmbercoalSeam,      salt: 701,  minY: 35, maxY: 135, chancePermille: 270, radius: 2, verticalRadius: 2),
                new ResourceVeinTuning(BlockRegistry.RosycopperBloom,    salt: 809,  minY: 45, maxY: 150, chancePermille: 145, radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.PaletinThread,      salt: 1213, minY: 30, maxY: 130, chancePermille: 120, radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.NiterstonePocket,   salt: 1409, minY: 40, maxY: 140, chancePermille: 95,  radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.RustcoreOre,        salt: 907,  minY: 15, maxY: 95,  chancePermille: 80,  radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.SunmetalFleck,      salt: 1303, minY: 20, maxY: 90,  chancePermille: 70,  radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.LumenQuartzCluster, salt: 1117, minY: 15, maxY: 120, chancePermille: 60,  radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.UmbraliteNode,      salt: 1511, minY: 8,  maxY: 60,  chancePermille: 50,  radius: 2, verticalRadius: 1),
                new ResourceVeinTuning(BlockRegistry.StaropalGeode,      salt: 1613, minY: 5,  maxY: 40,  chancePermille: 35,  radius: 1, verticalRadius: 1));
        }

        public static SurvivalResourceTuning CreateResourceRich()
        {
            SurvivalResourceTuning defaults = CreateDefault();
            var richVeins = new ResourceVeinTuning[defaults.resourceVeins.Length];

            for (int i = 0; i < defaults.resourceVeins.Length; i++)
            {
                ResourceVeinTuning tuning = defaults.resourceVeins[i];
                richVeins[i] = new ResourceVeinTuning(
                    tuning.ResourceBlock,
                    tuning.Salt,
                    tuning.MinY,
                    tuning.MaxY,
                    Math.Min(1000, tuning.ChancePermille * 2),
                    tuning.Radius + 1,
                    tuning.VerticalRadius);
            }

            return new SurvivalResourceTuning(richVeins);
        }

        public ResourceVeinTuning Get(BlockId resourceBlock)
        {
            if (!tuningByBlock.TryGetValue(resourceBlock, out ResourceVeinTuning tuning))
                throw new KeyNotFoundException($"No resource tuning is registered for block: {resourceBlock}");

            return tuning;
        }
    }
}

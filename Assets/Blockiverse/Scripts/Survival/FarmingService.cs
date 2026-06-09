using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public enum FarmingResult
    {
        Success,
        OutOfBounds,
        NotTillableBlock,
        NotTendedSoil,
        BlockAboveNotAir,
        UnknownCrop,
    }

    // Environmental inputs to a crop growth roll (§11.2). Supplied per crop position so growth can
    // depend on local light and soil moisture; defaults to favorable conditions where a caller has
    // no light/moisture source wired yet.
    public readonly struct CropGrowthConditions
    {
        public CropGrowthConditions(int lightLevel, bool soilMoist, float biomeModifier = 1f)
        {
            LightLevel = lightLevel;
            SoilMoist = soilMoist;
            BiomeModifier = biomeModifier;
        }

        public int LightLevel { get; }
        public bool SoilMoist { get; }
        public float BiomeModifier { get; }

        public static CropGrowthConditions Favorable => new(15, true, 1f);
    }

    public sealed class FarmingService
    {
        public const int GrowthIntervalTicks = 1200;

        // Full canonical stage chains (§11.2): grain 5 stages, berry 6, reed 4.
        // Mature stages (GrainStalk_S4 / Berrybush_S5 / Reedgrass_S3) have no successor.
        static readonly Dictionary<BlockId, BlockId> NextGrowthStage = new()
        {
            { BlockRegistry.GrainStalk,    BlockRegistry.GrainStalk_S1 },
            { BlockRegistry.GrainStalk_S1, BlockRegistry.GrainStalk_S2 },
            { BlockRegistry.GrainStalk_S2, BlockRegistry.GrainStalk_S3 },
            { BlockRegistry.GrainStalk_S3, BlockRegistry.GrainStalk_S4 },
            { BlockRegistry.Berrybush,     BlockRegistry.Berrybush_S1 },
            { BlockRegistry.Berrybush_S1,  BlockRegistry.Berrybush_S2 },
            { BlockRegistry.Berrybush_S2,  BlockRegistry.Berrybush_S3 },
            { BlockRegistry.Berrybush_S3,  BlockRegistry.Berrybush_S4 },
            { BlockRegistry.Berrybush_S4,  BlockRegistry.Berrybush_S5 },
            { BlockRegistry.Reedgrass,     BlockRegistry.Reedgrass_S1 },
            { BlockRegistry.Reedgrass_S1,  BlockRegistry.Reedgrass_S2 },
            { BlockRegistry.Reedgrass_S2,  BlockRegistry.Reedgrass_S3 },
        };

        static readonly HashSet<BlockId> CropBlocks = new(NextGrowthStage.Keys);

        // Growth is rolled probabilistically once per interval; unfavorable light/moisture cuts the
        // chance to 25% of the crop's base rate (§11.2).
        public const float UnfavorableGrowthMultiplier = 0.25f;

        // Per-crop growth profile (§11.2). Base growth chances are canonical; the minimum light
        // levels are balance constants (the ruleset references crop.minLight without fixed values).
        readonly struct CropGrowthProfile
        {
            public CropGrowthProfile(int minLight, float baseGrowthChance)
            {
                MinLight = minLight;
                BaseGrowthChance = baseGrowthChance;
            }

            public int MinLight { get; }
            public float BaseGrowthChance { get; }
        }

        static readonly CropGrowthProfile GrainProfile = new(minLight: 8, baseGrowthChance: 0.35f);
        static readonly CropGrowthProfile BerryProfile = new(minLight: 7, baseGrowthChance: 0.22f);
        static readonly CropGrowthProfile ReedProfile  = new(minLight: 5, baseGrowthChance: 0.40f);

        static readonly Dictionary<BlockId, CropGrowthProfile> ProfilesByStage = BuildProfiles();

        static Dictionary<BlockId, CropGrowthProfile> BuildProfiles()
        {
            var map = new Dictionary<BlockId, CropGrowthProfile>();
            foreach (BlockId b in new[] { BlockRegistry.GrainStalk, BlockRegistry.GrainStalk_S1, BlockRegistry.GrainStalk_S2, BlockRegistry.GrainStalk_S3 })
                map[b] = GrainProfile;
            foreach (BlockId b in new[] { BlockRegistry.Berrybush, BlockRegistry.Berrybush_S1, BlockRegistry.Berrybush_S2, BlockRegistry.Berrybush_S3, BlockRegistry.Berrybush_S4 })
                map[b] = BerryProfile;
            foreach (BlockId b in new[] { BlockRegistry.Reedgrass, BlockRegistry.Reedgrass_S1, BlockRegistry.Reedgrass_S2 })
                map[b] = ReedProfile;
            return map;
        }

        // Seed item → planted crop block (§11.2). Drygrass is a grain variant sharing the grain
        // crop block until per-seed biome variants are tracked.
        static readonly Dictionary<ItemId, BlockId> CropForSeed = new()
        {
            { ItemId.MeadowSeed,   BlockRegistry.GrainStalk },
            { ItemId.DrygrassSeed, BlockRegistry.GrainStalk },
            { ItemId.BerrySeed,    BlockRegistry.Berrybush },
            { ItemId.ReedCutting,  BlockRegistry.Reedgrass },
        };

        readonly Random defaultRandom = new();
        readonly Dictionary<BlockPosition, int> tickAccumulator = new();

        public void ScanAndTrackCrops(VoxelWorld world)
        {
            // Prune entries for positions no longer holding crop blocks.
            var toRemove = new List<BlockPosition>();
            foreach (BlockPosition pos in tickAccumulator.Keys)
                if (!CropBlocks.Contains(world.GetBlock(pos)))
                    toRemove.Add(pos);
            foreach (BlockPosition pos in toRemove)
                tickAccumulator.Remove(pos);

            // Add newly found crops without resetting ticks for already-tracked ones.
            WorldBounds bounds = world.Bounds;
            for (int y = 0; y < bounds.Height; y++)
            for (int z = 0; z < bounds.Depth; z++)
            for (int x = 0; x < bounds.Width; x++)
            {
                var pos = new BlockPosition(x, y, z);
                if (CropBlocks.Contains(world.GetBlock(pos)) && !tickAccumulator.ContainsKey(pos))
                    tickAccumulator[pos] = 0;
            }
        }

        public static bool IsCropBlock(BlockId blockId) => CropBlocks.Contains(blockId);

        public void TrackCrop(BlockPosition position)
        {
            tickAccumulator[position] = 0;
        }

        public FarmingResult Till(VoxelWorld world, BlockPosition position)
        {
            if (!world.Bounds.Contains(position))
                return FarmingResult.OutOfBounds;

            BlockId block = world.GetBlock(position);
            if (block != BlockRegistry.LooseLoam && block != BlockRegistry.Rootsoil && block != BlockRegistry.RiverSilt)
                return FarmingResult.NotTillableBlock;

            world.SetBlock(position, BlockRegistry.TendedSoil);
            return FarmingResult.Success;
        }

        public FarmingResult PlantCrop(VoxelWorld world, BlockPosition soilPosition, BlockId cropKind)
        {
            if (!world.Bounds.Contains(soilPosition))
                return FarmingResult.OutOfBounds;

            if (world.GetBlock(soilPosition) != BlockRegistry.TendedSoil)
                return FarmingResult.NotTendedSoil;

            if (!CropBlocks.Contains(cropKind))
                return FarmingResult.UnknownCrop;

            var cropPosition = new BlockPosition(soilPosition.X, soilPosition.Y + 1, soilPosition.Z);

            if (!world.Bounds.Contains(cropPosition) || world.GetBlock(cropPosition) != BlockRegistry.Air)
                return FarmingResult.BlockAboveNotAir;

            world.SetBlock(cropPosition, cropKind);
            tickAccumulator[cropPosition] = 0;
            return FarmingResult.Success;
        }

        // Plants the crop a seed item produces (§11.2). Returns UnknownCrop for non-seed items.
        public FarmingResult PlantSeed(VoxelWorld world, BlockPosition soilPosition, ItemId seedItemId)
        {
            return CropForSeed.TryGetValue(seedItemId, out BlockId cropKind)
                ? PlantCrop(world, soilPosition, cropKind)
                : FarmingResult.UnknownCrop;
        }

        public static bool IsSeedItem(ItemId seedItemId) => CropForSeed.ContainsKey(seedItemId);

        // Advances tracked crops. Each elapsed growth interval rolls one probabilistic stage
        // advance: favorable light+moisture uses the crop's base chance (× biome modifier); otherwise
        // 25% of it (§11.2). Conditions default to favorable until a light/moisture source is wired;
        // the random source is injectable for deterministic tests.
        public void TickGrowth(
            VoxelWorld world,
            int ticks,
            Func<BlockPosition, CropGrowthConditions> conditions = null,
            Func<double> random = null)
        {
            if (ticks <= 0)
                return;

            conditions ??= _ => CropGrowthConditions.Favorable;
            random ??= defaultRandom.NextDouble;

            foreach (BlockPosition pos in new List<BlockPosition>(tickAccumulator.Keys))
            {
                BlockId current = world.GetBlock(pos);
                if (!NextGrowthStage.ContainsKey(current))
                {
                    // No longer a growable crop (harvested, replaced, or already mature).
                    tickAccumulator.Remove(pos);
                    continue;
                }

                long accumulated = (long)tickAccumulator[pos] + ticks;
                while (accumulated >= GrowthIntervalTicks && NextGrowthStage.TryGetValue(current, out BlockId next))
                {
                    accumulated -= GrowthIntervalTicks;

                    CropGrowthProfile profile = ProfilesByStage[current];
                    CropGrowthConditions cond = conditions(pos);
                    double chance = cond.LightLevel >= profile.MinLight && cond.SoilMoist
                        ? profile.BaseGrowthChance * cond.BiomeModifier
                        : profile.BaseGrowthChance * UnfavorableGrowthMultiplier;

                    if (random() < chance)
                    {
                        current = next;
                        world.SetBlock(pos, current);
                    }
                }

                if (NextGrowthStage.ContainsKey(current))
                    tickAccumulator[pos] = (int)Math.Min(accumulated, int.MaxValue);
                else
                    tickAccumulator.Remove(pos); // matured — stop tracking
            }
        }
    }
}

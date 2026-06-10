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
        RequiresWater,
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

        // Berrybush regrows two game days after harvest (§3); 1 game day = 24000 ticks.
        public const int TicksPerGameDay = 24000;
        public const int BerrybushRegrowTicks = 2 * TicksPerGameDay;

        static readonly HashSet<BlockId> BerrybushStages = new()
        {
            BlockRegistry.Berrybush, BlockRegistry.Berrybush_S1, BlockRegistry.Berrybush_S2,
            BlockRegistry.Berrybush_S3, BlockRegistry.Berrybush_S4, BlockRegistry.Berrybush_S5,
        };

        public static bool IsBerrybushStage(BlockId blockId) => BerrybushStages.Contains(blockId);

        // Crops currently tracked for growth, and pending berrybush regrow delays (delta ticks).
        readonly HashSet<BlockPosition> trackedCrops = new();
        readonly Dictionary<BlockPosition, int> berrybushRegrowAccumulator = new();

        // Scratch list reused across tick calls so iterating a mutating collection does not
        // allocate a fresh list every world tick.
        readonly List<BlockPosition> tickKeyScratch = new();

        // Deterministic growth state: the world seed all rolls derive from, and the last absolute
        // growth interval processed per crop. Together with the synced WorldTimeClock this makes
        // every roll a pure function of (seed, position, stage, interval index) — identical on the
        // host and on every client, including late joiners.
        int deterministicSeed;
        bool deterministicRollsConfigured;
        readonly Dictionary<BlockPosition, long> lastProcessedInterval = new();

        // Crops cross growth boundaries only once per interval (1200 ticks), so the deterministic
        // sweep is skipped until the interval index advances. Newly tracked crops are queued here
        // so they get their interval anchor on the next tick without rescanning every crop.
        long lastSweptGrowthInterval = -1;
        readonly HashSet<BlockPosition> pendingGrowthAnchors = new();

        // Enables seed-keyed deterministic growth rolls (used with the absolute-tick TickGrowth).
        public void ConfigureDeterministicGrowth(int worldSeed)
        {
            deterministicSeed = worldSeed;
            deterministicRollsConfigured = true;
        }

        public void ScanAndTrackCrops(VoxelWorld world)
        {
            // Prune entries for positions no longer holding crop blocks.
            var toRemove = new List<BlockPosition>();
            foreach (BlockPosition pos in trackedCrops)
                if (!CropBlocks.Contains(world.GetBlock(pos)))
                    toRemove.Add(pos);
            foreach (BlockPosition pos in toRemove)
            {
                trackedCrops.Remove(pos);
                lastProcessedInterval.Remove(pos);
                pendingGrowthAnchors.Remove(pos);
            }

            // Add newly found crops without re-anchoring already-tracked ones.
            WorldBounds bounds = world.Bounds;
            for (int y = 0; y < bounds.Height; y++)
            for (int z = 0; z < bounds.Depth; z++)
            for (int x = 0; x < bounds.Width; x++)
            {
                var pos = new BlockPosition(x, y, z);
                if (CropBlocks.Contains(world.GetBlock(pos)) && trackedCrops.Add(pos))
                    pendingGrowthAnchors.Add(pos);
            }
        }

        public static bool IsCropBlock(BlockId blockId) => CropBlocks.Contains(blockId);

        public void TrackCrop(BlockPosition position)
        {
            trackedCrops.Add(position);
            // Restart the deterministic interval anchor too: a freshly planted crop begins growing
            // from the next interval boundary after it is first ticked.
            lastProcessedInterval.Remove(position);
            pendingGrowthAnchors.Add(position);
        }

        // Horizontal/vertical reach of the tended-soil freshwater check (§11.1).
        public const int TillWaterHorizontalReach = 4;
        public const int TillWaterVerticalReach = 1;

        // Scans the §11.1 reach box around the soil cell for a freshwater source block.
        public static bool HasFreshwaterNearby(VoxelWorld world, BlockPosition position)
        {
            for (int dy = -TillWaterVerticalReach; dy <= TillWaterVerticalReach; dy++)
            {
                for (int dz = -TillWaterHorizontalReach; dz <= TillWaterHorizontalReach; dz++)
                {
                    for (int dx = -TillWaterHorizontalReach; dx <= TillWaterHorizontalReach; dx++)
                    {
                        var cell = new BlockPosition(position.X + dx, position.Y + dy, position.Z + dz);
                        if (world.Bounds.Contains(cell) && world.GetBlock(cell) == BlockRegistry.Freshwater)
                            return true;
                    }
                }
            }

            return false;
        }

        // Converts eligible soil to tended soil (§11.1). When no freshwater is nearby, the action
        // consumes one clean_water_flask from the inventory (the emptied flask returns, §731);
        // with neither, it returns RequiresWater. hasFreshwaterNearby is injectable for tests;
        // it defaults to "water present" — the authoritative gameplay path (ProcessHostTill)
        // passes the real HasFreshwaterNearby scan.
        public FarmingResult Till(
            VoxelWorld world,
            BlockPosition position,
            Inventory inventory = null,
            Func<VoxelWorld, BlockPosition, bool> hasFreshwaterNearby = null)
        {
            if (!world.Bounds.Contains(position))
                return FarmingResult.OutOfBounds;

            BlockId block = world.GetBlock(position);
            if (!IsTillableBlock(block))
                return FarmingResult.NotTillableBlock;

            bool waterNearby = hasFreshwaterNearby?.Invoke(world, position) ?? true;
            if (!waterNearby)
            {
                if (inventory == null || !inventory.Remove(ItemId.CleanWaterFlask, 1))
                    return FarmingResult.RequiresWater;

                // Flasks stack to 1, so the consume above freed a slot and the return cannot fail.
                inventory.TryAddAll(new ItemStack(ItemId.WaterFlask, 1));
            }

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
            trackedCrops.Add(cropPosition);
            lastProcessedInterval.Remove(cropPosition);
            pendingGrowthAnchors.Add(cropPosition);
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

        public static bool TryGetCropForSeed(ItemId seedItemId, out BlockId cropKind) =>
            CropForSeed.TryGetValue(seedItemId, out cropKind);

        // Soil blocks the Tiller can convert to tended soil (§11.1).
        public static bool IsTillableBlock(BlockId block) =>
            block == BlockRegistry.LooseLoam || block == BlockRegistry.Rootsoil || block == BlockRegistry.RiverSilt;

        // Deterministic growth driven by the absolute world tick (§11.2, multiplayer ruleset).
        // Each elapsed growth interval rolls one probabilistic stage advance: favorable
        // light+moisture uses the crop's base chance (× biome modifier); otherwise 25% of it.
        // Conditions default to favorable until a light/moisture source is wired.
        // Each crop processes the growth-interval boundaries it has crossed since it was last
        // ticked; every roll is Hash(worldSeed, position, stage, intervalIndex), so the host and
        // all clients — including late joiners whose stages arrive via the chunk snapshot — advance
        // crops identically without any growth traffic. Requires ConfigureDeterministicGrowth.
        public void TickGrowth(
            VoxelWorld world,
            long worldTick,
            Func<BlockPosition, CropGrowthConditions> conditions = null)
        {
            if (!deterministicRollsConfigured)
                throw new InvalidOperationException(
                    "Deterministic growth requires ConfigureDeterministicGrowth(worldSeed) first.");
            if (worldTick < 0 || trackedCrops.Count == 0)
                return;

            conditions ??= _ => CropGrowthConditions.Favorable;
            long currentInterval = worldTick / GrowthIntervalTicks;

            // Anchor crops tracked since the last tick: growth begins at the next boundary after
            // the crop is first ticked, so the anchor must be set now, not at the next interval.
            if (pendingGrowthAnchors.Count > 0)
            {
                foreach (BlockPosition pos in pendingGrowthAnchors)
                {
                    if (trackedCrops.Contains(pos))
                        lastProcessedInterval[pos] = currentInterval;
                }

                pendingGrowthAnchors.Clear();
            }

            // Rolls only happen at interval boundaries, so the per-crop sweep is pure overhead
            // until the interval index advances.
            if (currentInterval == lastSweptGrowthInterval)
                return;
            lastSweptGrowthInterval = currentInterval;

            foreach (BlockPosition pos in SnapshotKeys(trackedCrops))
            {
                BlockId current = world.GetBlock(pos);
                if (!NextGrowthStage.ContainsKey(current))
                {
                    // No longer a growable crop (harvested, replaced, or already mature).
                    trackedCrops.Remove(pos);
                    lastProcessedInterval.Remove(pos);
                    continue;
                }

                if (!lastProcessedInterval.TryGetValue(pos, out long lastInterval))
                {
                    // First tick for this crop: anchor it so growth begins at the next boundary.
                    lastProcessedInterval[pos] = currentInterval;
                    continue;
                }

                for (long interval = lastInterval + 1; interval <= currentInterval; interval++)
                {
                    if (!NextGrowthStage.TryGetValue(current, out BlockId next))
                        break;

                    CropGrowthProfile profile = ProfilesByStage[current];
                    CropGrowthConditions cond = conditions(pos);
                    double chance = cond.LightLevel >= profile.MinLight && cond.SoilMoist
                        ? profile.BaseGrowthChance * cond.BiomeModifier
                        : profile.BaseGrowthChance * UnfavorableGrowthMultiplier;

                    if (DeterministicRoll(pos, current, interval) < chance)
                    {
                        current = next;
                        world.SetBlock(pos, current);
                    }
                }

                if (NextGrowthStage.ContainsKey(current))
                    lastProcessedInterval[pos] = currentInterval;
                else
                {
                    trackedCrops.Remove(pos); // matured — stop tracking
                    lastProcessedInterval.Remove(pos);
                }
            }
        }

        // Copies the positions into the shared scratch list so the loop body can mutate the
        // source collection. Tick methods are never nested, so one scratch list is safe to share.
        List<BlockPosition> SnapshotKeys(IEnumerable<BlockPosition> source)
        {
            tickKeyScratch.Clear();
            tickKeyScratch.AddRange(source);
            return tickKeyScratch;
        }

        // Deterministic roll over (seed, position, stage, interval) mapped to [0, 1) — the shared
        // world-generation hash, so every peer computes identical growth from the same seed.
        double DeterministicRoll(BlockPosition pos, BlockId stage, long interval)
        {
            return DeterministicHash.UnitRoll(deterministicSeed, pos.X, pos.Y, pos.Z, stage.Value, interval);
        }

        // Call after a block is harvested; queues a berrybush to regrow two game days later (§3).
        // Non-berrybush blocks are ignored.
        public void OnBlockHarvested(BlockId harvestedBlock, BlockPosition position)
        {
            if (IsBerrybushStage(harvestedBlock))
                berrybushRegrowAccumulator[position] = 0;
        }

        public bool HasPendingRegrowth(BlockPosition position) => berrybushRegrowAccumulator.ContainsKey(position);

        // Advances queued berrybush regrowth. When the regrow delay elapses and the spot is clear,
        // a fresh berrybush (stage 0) is planted and tracked for growth; if the spot is occupied the
        // pending regrow is dropped.
        public void TickRegrowth(VoxelWorld world, int ticks)
        {
            if (ticks <= 0 || berrybushRegrowAccumulator.Count == 0)
                return;

            foreach (BlockPosition pos in SnapshotKeys(berrybushRegrowAccumulator.Keys))
            {
                long accumulated = (long)berrybushRegrowAccumulator[pos] + ticks;
                if (accumulated < BerrybushRegrowTicks)
                {
                    berrybushRegrowAccumulator[pos] = (int)Math.Min(accumulated, int.MaxValue);
                    continue;
                }

                berrybushRegrowAccumulator.Remove(pos);

                if (world.Bounds.Contains(pos) && world.GetBlock(pos) == BlockRegistry.Air)
                {
                    world.SetBlock(pos, BlockRegistry.Berrybush);
                    trackedCrops.Add(pos);
                    lastProcessedInterval.Remove(pos);
                    pendingGrowthAnchors.Add(pos);
                }
            }
        }

        // ── Save/load (world persistence) ────────────────────────────────────

        // Snapshot of pending berrybush regrowth (accumulated ticks toward the two-day delay).
        public IReadOnlyList<(BlockPosition position, int accumulatedTicks)> ExportBerrybushRegrowth()
        {
            var result = new List<(BlockPosition, int)>(berrybushRegrowAccumulator.Count);
            foreach (KeyValuePair<BlockPosition, int> entry in berrybushRegrowAccumulator)
                result.Add((entry.Key, entry.Value));
            return result;
        }

        // Replaces the pending berrybush regrowth set with saved progress. TickRegrowth drops any
        // entry whose spot turns out to be occupied, so stale positions self-heal.
        public void RestoreBerrybushRegrowth(IEnumerable<(BlockPosition position, int accumulatedTicks)> entries)
        {
            berrybushRegrowAccumulator.Clear();
            if (entries == null)
                return;

            foreach ((BlockPosition position, int accumulatedTicks) in entries)
                berrybushRegrowAccumulator[position] = Math.Max(0, accumulatedTicks);
        }
    }
}

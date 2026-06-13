using System;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    // Canonical mining math (docs/rulesets/voxel_survival_ruleset.md §6 mining, §7 tools).
    // Mining time is derived from block hardness and the equipped tool's speed; durability
    // cost is derived from the block category and whether the correct tool/tier was used.
    public static class MiningFormula
    {
        public const int TicksPerSecond = SimulationTime.TicksPerSecond;
        public const float HandSpeed = 0.5f;

        // Material mining speed by harvest tier (§7.1). Index 0 = bare hand.
        // 1 Reedwood, 2 Flint, 3 Rosycopper, 4 Bronze, 5 Ironroot, 6 Deepsteel, 7 Starforged.
        static readonly float[] MaterialSpeedByTier = { HandSpeed, 0.9f, 1.5f, 2.2f, 3.0f, 4.1f, 5.6f, 7.5f };

        // Tool-class speed multiplier (§7.2).
        public static float ClassSpeedMultiplier(HarvestToolKind toolClass) => toolClass switch
        {
            HarvestToolKind.Delver => 1.00f,
            HarvestToolKind.Spade  => 1.25f,
            HarvestToolKind.Feller => 1.10f,
            HarvestToolKind.Sickle => 1.60f,
            HarvestToolKind.Mallet => 0.85f,
            HarvestToolKind.Carver => 0.60f,
            HarvestToolKind.Tiller => 0.70f,
            _                      => 1.00f, // Hand
        };

        // Final tool speed = material speed (by tier) × tool-class multiplier (§7.2).
        public static float ToolSpeed(HarvestToolKind toolClass, int toolTier)
        {
            if (toolClass == HarvestToolKind.Hand)
                return HandSpeed;

            int tier = Math.Clamp(toolTier, 1, MaterialSpeedByTier.Length - 1);
            return MaterialSpeedByTier[tier] * ClassSpeedMultiplier(toolClass);
        }

        // §6.1: mining time in ticks. Wrong tool class applies a ×0.25 penalty; insufficient
        // tier applies a ×0.15 penalty. Returns int.MaxValue for unbreakable (infinite hardness).
        public static int MineTicks(
            float hardness,
            HarvestToolKind effectiveTool,
            int harvestTierMin,
            HarvestToolKind usedTool,
            int usedTier)
        {
            if (float.IsPositiveInfinity(hardness))
                return int.MaxValue;

            float speed = ToolSpeed(usedTool, usedTier);
            if (usedTool != effectiveTool) speed *= 0.25f;
            if (usedTier < harvestTierMin) speed *= 0.15f;

            float seconds = hardness / Math.Max(speed, 0.05f);
            double ticks = Math.Ceiling(seconds * TicksPerSecond);
            return ticks >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)ticks);
        }

        // §6.3: durability cost for a single harvest action. Common ore costs 2, deep ore
        // (tier ≥ 5 resource nodes) costs 3, everything else costs 1; wrong tool adds +1 and
        // an insufficient tier adds +2. Bare hands have no durability and never call this.
        public static int DurabilityCost(
            BlockCategory category,
            int harvestTierMin,
            bool correctTool,
            bool sufficientTier)
        {
            int cost = category == BlockCategory.Resource
                ? (harvestTierMin >= 5 ? 3 : 2)
                : 1;

            if (!correctTool) cost += 1;
            if (!sufficientTier) cost += 2;
            return cost;
        }
    }
}

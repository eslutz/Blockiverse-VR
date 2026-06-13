using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    // Fuel and timing model for the timed smelting stations (Clay Kiln, Bellows Forge).
    // Canonical fuel burn values and the forge 2× consumption rule come from
    // voxel_survival_ruleset §8.2; recipe durations come from §9.3/§9.4 (seconds → ticks).
    // This is a pure model; a station controller drives it against real fuel/input/output slots.
    public static class SmeltingModel
    {
        public const int TicksPerSecond = SimulationTime.TicksPerSecond;

        // Burn time per fuel unit, in seconds (§8.2).
        static readonly Dictionary<ItemId, int> FuelBurnSecondsById = new()
        {
            { ItemId.WorkPlank,      3 },
            { ItemId.BranchwoodLog, 10 },
            // The ruleset lists "resin_blob"; the in-code harvested resin resource is resin_knot.
            { ItemId.ResinKnot,     20 },
            { ItemId.Embercoal,     80 },
            { ItemId.EmbercoalBlock, 720 },
        };

        public static bool IsFuel(ItemId itemId) => FuelBurnSecondsById.ContainsKey(itemId);

        // Total ticks one unit of the given fuel burns for (0 if it is not a fuel).
        public static int FuelBurnTicks(ItemId itemId)
        {
            return FuelBurnSecondsById.TryGetValue(itemId, out int seconds)
                ? seconds * TicksPerSecond
                : 0;
        }

        // Burn ticks delivered per fuel unit at a station. The Bellows Forge consumes fuel at
        // 2× speed, so each unit yields half the smelting time it would at the Clay Kiln (§8.2).
        public static int EffectiveBurnTicksPerUnit(ItemId fuel, CraftingStation station)
        {
            int baseTicks = FuelBurnTicks(fuel);
            if (baseTicks <= 0)
                return 0;

            return station == CraftingStation.BellowsForge
                ? Math.Max(1, baseTicks / 2)
                : baseTicks;
        }

        // Number of whole fuel units required to run a recipe of the given duration at a station
        // with the given fuel. Returns 0 for an instant recipe and 0 if the item is not a fuel.
        public static int FuelUnitsRequired(int recipeTimeTicks, ItemId fuel, CraftingStation station)
        {
            if (recipeTimeTicks <= 0)
                return 0;

            int perUnit = EffectiveBurnTicksPerUnit(fuel, station);
            if (perUnit <= 0)
                return 0;

            return (recipeTimeTicks + perUnit - 1) / perUnit;
        }

        // True when the supplied fuel stack can fully power the recipe at the station.
        public static bool HasEnoughFuel(CraftingRecipe recipe, ItemStack fuelStack, CraftingStation station)
        {
            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));

            if (recipe.TimeTicks <= 0)
                return true; // instant recipe needs no fuel

            if (fuelStack.IsEmpty || !IsFuel(fuelStack.ItemId))
                return false;

            return fuelStack.Count >= FuelUnitsRequired(recipe.TimeTicks, fuelStack.ItemId, station);
        }
    }
}

using System;
using System.Collections.Generic;

namespace Blockiverse.Survival
{
    // Runtime model for a timed smelting station — Clay Kiln (1 input slot) or Bellows Forge (3)
    // (voxel_survival_menus §8.4, voxel_survival_ruleset §8/§9.3/§9.4). Holds input/fuel/output
    // slots and a tick-driven craft: a craft begins when the inputs satisfy a station recipe and the
    // fuel can power its duration; the fuel is consumed up front and progress advances over ticks,
    // emitting the output on completion and auto-continuing while inputs and fuel remain.
    public sealed class SmeltingStationModel
    {
        // Largest input slot count across station types (the Bellows Forge's three slots).
        public const int MaxInputSlots = 3;

        // True for the stations whose crafts run over ticks and therefore have a runtime model.
        public static bool IsTimedStation(CraftingStation station) =>
            station == CraftingStation.ClayKiln || station == CraftingStation.BellowsForge;

        // Canonical input slot count per timed station (§8.4: kiln 1, forge 3).
        public static int InputSlotCountFor(CraftingStation station)
        {
            if (!IsTimedStation(station))
                throw new ArgumentException("Only timed stations (kiln/forge) have input slots.", nameof(station));
            return station == CraftingStation.BellowsForge ? MaxInputSlots : 1;
        }

        readonly CraftingStation stationType;
        readonly CraftingRecipeBook recipeBook;
        readonly ItemRegistry itemRegistry;
        readonly ItemStack[] inputs;

        public SmeltingStationModel(
            CraftingStation stationType,
            int inputSlotCount,
            CraftingRecipeBook recipeBook = null,
            ItemRegistry itemRegistry = null)
        {
            if (!IsTimedStation(stationType))
                throw new ArgumentException("SmeltingStationModel only models timed stations (kiln/forge).", nameof(stationType));
            if (inputSlotCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(inputSlotCount));

            this.stationType = stationType;
            this.itemRegistry = itemRegistry ?? ItemRegistry.Default;
            this.recipeBook = recipeBook ?? (ReferenceEquals(this.itemRegistry, ItemRegistry.Default) ? CraftingRecipeBook.Default : CraftingRecipeBook.CreateDefault(this.itemRegistry));
            inputs = new ItemStack[inputSlotCount];
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = ItemStack.Empty;
            Fuel = ItemStack.Empty;
            Output = ItemStack.Empty;
        }

        public CraftingStation StationType => stationType;
        public int InputSlotCount => inputs.Length;
        public ItemStack Fuel { get; private set; }
        public ItemStack Output { get; private set; }
        public CraftingRecipe ActiveRecipe { get; private set; }
        public int ProgressTicks { get; private set; }
        public int RequiredTicks => ActiveRecipe?.TimeTicks ?? 0;
        public bool IsActive => ActiveRecipe != null;

        // Bumped whenever slot contents or the active recipe change (not on plain progress
        // ticking), so display code can skip rebuilding labels when nothing visible changed.
        public int ContentVersion { get; private set; }

        public ItemStack GetInput(int slot) => inputs[slot];

        public void SetInput(int slot, ItemStack stack)
        {
            inputs[slot] = stack;
            ContentVersion++;
        }

        public void SetFuel(ItemStack stack)
        {
            Fuel = stack;
            ContentVersion++;
        }

        // Merges the stack into a matching input slot (respecting max stack size) or the first
        // empty slot. Returns false when no slot can receive it; the stack is untouched then.
        public bool TryDepositInput(ItemStack stack)
        {
            if (stack.IsEmpty)
                return false;

            int max = itemRegistry.Get(stack.ItemId).MaxStackSize;
            if (stack.Count > max)
                return false;

            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].CanStackWith(stack) && inputs[i].Count + stack.Count <= max)
                {
                    inputs[i] = inputs[i].WithCount(inputs[i].Count + stack.Count);
                    ContentVersion++;
                    return true;
                }
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].IsEmpty)
                {
                    inputs[i] = stack;
                    ContentVersion++;
                    return true;
                }
            }

            return false;
        }

        // Merges the stack into the fuel slot. Only accepts canonical fuels (§8.2) and a fuel slot
        // that is empty or already holds the same fuel with room in the stack.
        public bool TryDepositFuel(ItemStack stack)
        {
            if (stack.IsEmpty || !SmeltingModel.IsFuel(stack.ItemId))
                return false;

            if (Fuel.IsEmpty)
            {
                Fuel = stack;
                ContentVersion++;
                return true;
            }

            if (!Fuel.CanStackWith(stack))
                return false;

            int max = itemRegistry.Get(stack.ItemId).MaxStackSize;
            if (Fuel.Count + stack.Count > max)
                return false;

            Fuel = Fuel.WithCount(Fuel.Count + stack.Count);
            ContentVersion++;
            return true;
        }

        // Removes and returns the accumulated output (empty when there is none).
        public ItemStack CollectOutput()
        {
            ItemStack collected = Output;
            Output = ItemStack.Empty;
            ContentVersion++;
            return collected;
        }

        public bool TryWithdrawInput(ItemId itemId, int count, out ItemStack withdrawn)
        {
            withdrawn = ItemStack.Empty;

            if (IsActive || itemId.IsNone || count <= 0)
                return false;

            for (int i = 0; i < inputs.Length; i++)
            {
                ItemStack slot = inputs[i];
                if (slot.IsEmpty || slot.ItemId != itemId || slot.Count < count)
                    continue;

                withdrawn = slot.WithCount(count);
                inputs[i] = Decrement(slot, count);
                ContentVersion++;
                return true;
            }

            return false;
        }

        public bool TryWithdrawFuel(ItemId itemId, int count, out ItemStack withdrawn)
        {
            withdrawn = ItemStack.Empty;

            if (itemId.IsNone || count <= 0 || Fuel.IsEmpty || Fuel.ItemId != itemId || Fuel.Count < count)
                return false;

            withdrawn = Fuel.WithCount(count);
            Fuel = Decrement(Fuel, count);
            ContentVersion++;
            return true;
        }

        public IReadOnlyList<ItemStack> DrainContents()
        {
            var contents = new List<ItemStack>(inputs.Length + 2);

            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].IsEmpty)
                    continue;

                contents.Add(inputs[i]);
                inputs[i] = ItemStack.Empty;
            }

            if (!Fuel.IsEmpty)
            {
                contents.Add(Fuel);
                Fuel = ItemStack.Empty;
            }

            if (!Output.IsEmpty)
            {
                contents.Add(Output);
                Output = ItemStack.Empty;
            }

            bool wasActive = ActiveRecipe != null || ProgressTicks != 0;
            ActiveRecipe = null;
            ProgressTicks = 0;
            if (contents.Count > 0 || wasActive)
                ContentVersion++;
            return contents;
        }

        // Applies a host-authoritative snapshot to this client-side display mirror. Mirrors are
        // never ticked locally (the host owns station ticking).
        public void ApplyHostSnapshot(
            ItemStack[] snapshotInputs,
            ItemStack fuel,
            ItemStack output,
            CraftingRecipe activeRecipe,
            int progressTicks)
        {
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = snapshotInputs != null && i < snapshotInputs.Length ? snapshotInputs[i] : ItemStack.Empty;

            Fuel = fuel;
            Output = output;
            ActiveRecipe = activeRecipe;
            ProgressTicks = activeRecipe != null ? Math.Max(0, progressTicks) : 0;
            ContentVersion++;
        }

        // Advances the station by the given ticks, beginning and chaining crafts as inputs/fuel allow.
        public void Tick(int ticks)
        {
            if (ticks <= 0)
                return;

            int remaining = ticks;
            while (remaining > 0)
            {
                if (!IsActive && !TryBeginCraft())
                    return;

                int step = Math.Min(remaining, RequiredTicks - ProgressTicks);
                ProgressTicks += step;
                remaining -= step;

                if (ProgressTicks >= RequiredTicks)
                    CompleteCraft();
            }
        }

        // Attempts to start a craft: picks the first station recipe the inputs satisfy whose output
        // can be received and whose duration the loaded fuel can power, then consumes the fuel.
        public bool TryBeginCraft()
        {
            if (IsActive)
                return true;

            foreach (CraftingRecipe recipe in recipeBook.All)
            {
                if (recipe.RequiredStation != stationType || recipe.TimeTicks <= 0)
                    continue;
                if (!InputsSatisfy(recipe) || !CanReceiveOutput(recipe.Output))
                    continue;
                if (!SmeltingModel.HasEnoughFuel(recipe, Fuel, stationType))
                    continue;

                int fuelUnits = SmeltingModel.FuelUnitsRequired(recipe.TimeTicks, Fuel.ItemId, stationType);
                Fuel = Decrement(Fuel, fuelUnits);
                ActiveRecipe = recipe;
                ProgressTicks = 0;
                ContentVersion++;
                return true;
            }

            return false;
        }

        void CompleteCraft()
        {
            ConsumeInputs(ActiveRecipe);
            Output = MergeOutput(Output, ActiveRecipe.Output);
            ActiveRecipe = null;
            ProgressTicks = 0;
            ContentVersion++;
        }

        // Indexed iteration over AggregatedIngredients keeps these allocation-free; TryBeginCraft
        // runs InputsSatisfy against every recipe each tick while idle.
        bool InputsSatisfy(CraftingRecipe recipe)
        {
            IReadOnlyList<ItemStack> ingredients = recipe.AggregatedIngredients;
            for (int i = 0; i < ingredients.Count; i++)
            {
                if (CountInInputs(ingredients[i].ItemId) < ingredients[i].Count)
                    return false;
            }

            return true;
        }

        void ConsumeInputs(CraftingRecipe recipe)
        {
            IReadOnlyList<ItemStack> ingredients = recipe.AggregatedIngredients;
            for (int n = 0; n < ingredients.Count; n++)
            {
                ItemStack ingredient = ingredients[n];
                int toRemove = ingredient.Count;
                for (int i = 0; i < inputs.Length && toRemove > 0; i++)
                {
                    if (inputs[i].IsEmpty || inputs[i].ItemId != ingredient.ItemId)
                        continue;

                    int take = Math.Min(toRemove, inputs[i].Count);
                    inputs[i] = Decrement(inputs[i], take);
                    toRemove -= take;
                }
            }
        }

        int CountInInputs(ItemId itemId)
        {
            int total = 0;
            foreach (ItemStack slot in inputs)
                if (!slot.IsEmpty && slot.ItemId == itemId)
                    total += slot.Count;
            return total;
        }

        bool CanReceiveOutput(ItemStack output)
        {
            if (Output.IsEmpty)
                return true;
            if (Output.ItemId != output.ItemId)
                return false;

            int max = itemRegistry.Get(output.ItemId).MaxStackSize;
            return Output.Count + output.Count <= max;
        }

        ItemStack MergeOutput(ItemStack current, ItemStack addition)
        {
            if (current.IsEmpty)
                return addition;
            return current.WithCount(current.Count + addition.Count);
        }

        static ItemStack Decrement(ItemStack stack, int amount)
        {
            int remaining = stack.Count - amount;
            return remaining > 0 ? stack.WithCount(remaining) : ItemStack.Empty;
        }
    }
}

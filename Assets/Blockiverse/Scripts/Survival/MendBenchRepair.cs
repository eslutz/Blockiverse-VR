using System;

namespace Blockiverse.Survival
{
    public enum RepairFailureReason
    {
        None,
        WrongStation,
        NotARepairableTool,
        AlreadyFullDurability,
        MissingRepairMaterial
    }

    public readonly struct RepairResult
    {
        RepairResult(bool succeeded, RepairFailureReason failureReason, int newDurability, ItemId materialUsed)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            NewDurability = newDurability;
            MaterialUsed = materialUsed;
        }

        public bool Succeeded { get; }
        public RepairFailureReason FailureReason { get; }
        public int NewDurability { get; }
        public ItemId MaterialUsed { get; }

        public static RepairResult Success(int newDurability, ItemId materialUsed) =>
            new(true, RepairFailureReason.None, newDurability, materialUsed);

        public static RepairResult Failure(RepairFailureReason reason)
        {
            if (reason == RepairFailureReason.None)
                throw new ArgumentException("Failure results must include a concrete reason.", nameof(reason));

            return new RepairResult(false, reason, 0, ItemId.None);
        }
    }

    // Mend Bench tool repair (voxel_survival_ruleset §10.7). One unit of the tool's matching
    // head material restores 25% of the tool's max durability (rounded), capped at max.
    public static class MendBenchRepair
    {
        // Restored durability per repair: round(maxDurability * 0.25).
        public static int RepairAmount(int maxDurability) =>
            (int)Math.Round(maxDurability * 0.25, MidpointRounding.AwayFromZero);

        // Matching head material for a tool tier (§10.7). Tier 2 uses flinty_shingle, the in-code
        // flint resource (the ruleset names it "flint_shard").
        public static ItemId RepairMaterialForTier(int toolTier)
        {
            return toolTier switch
            {
                1 => ItemId.WorkPlank,
                2 => ItemId.FlintyShingle,
                3 => ItemId.RosycopperBar,
                4 => ItemId.BronzeBar,
                5 => ItemId.IronrootBar,
                6 => ItemId.DeepsteelBar,
                7 => ItemId.StarforgedCore,
                _ => ItemId.None,
            };
        }

        public static RepairResult TryRepair(
            ItemRegistry itemRegistry,
            Inventory inventory,
            int toolSlotIndex,
            CraftingStation availableStation)
        {
            if (itemRegistry == null)
                throw new ArgumentNullException(nameof(itemRegistry));
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            if (availableStation != CraftingStation.MendBench)
                return RepairResult.Failure(RepairFailureReason.WrongStation);

            if (toolSlotIndex < 0 || toolSlotIndex >= inventory.SlotCount)
                return RepairResult.Failure(RepairFailureReason.NotARepairableTool);

            ItemStack tool = inventory.GetSlot(toolSlotIndex);
            if (tool.IsEmpty || !itemRegistry.TryGet(tool.ItemId, out ItemDefinition def) ||
                def.Kind != ItemKind.Tool || def.MaxDurability <= 0)
            {
                return RepairResult.Failure(RepairFailureReason.NotARepairableTool);
            }

            // A tracked tool carries durability in [1, MaxDurability]; durability 0 means the slot
            // is not tracking wear, so there is nothing to repair.
            if (tool.Durability <= 0 || tool.Durability >= def.MaxDurability)
                return RepairResult.Failure(RepairFailureReason.AlreadyFullDurability);

            ItemId material = RepairMaterialForTier(def.ToolTier);
            if (material.IsNone || inventory.CountOf(material) < 1)
                return RepairResult.Failure(RepairFailureReason.MissingRepairMaterial);

            if (!inventory.Remove(material, 1))
                return RepairResult.Failure(RepairFailureReason.MissingRepairMaterial);

            int newDurability = Math.Min(def.MaxDurability, tool.Durability + RepairAmount(def.MaxDurability));
            inventory.SetSlot(toolSlotIndex, tool.WithDurability(newDurability));

            return RepairResult.Success(newDurability, material);
        }
    }
}

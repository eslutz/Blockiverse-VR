using Blockiverse.Survival;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class SmeltingAndRepairEditModeTests
    {
        // ── Smelting fuel/time model (§8.2, §9.3/§9.4) ───────────────────────

        [Test]
        public void FuelBurnTicksFollowCanonicalSecondsTable()
        {
            Assert.That(SmeltingModel.FuelBurnTicks(ItemId.WorkPlank), Is.EqualTo(3 * WorldConstants.TicksPerSecond));
            Assert.That(SmeltingModel.FuelBurnTicks(ItemId.BranchwoodLog), Is.EqualTo(10 * WorldConstants.TicksPerSecond));
            Assert.That(SmeltingModel.FuelBurnTicks(ItemId.Embercoal), Is.EqualTo(80 * WorldConstants.TicksPerSecond));
            Assert.That(SmeltingModel.FuelBurnTicks(ItemId.EmbercoalBlock), Is.EqualTo(720 * WorldConstants.TicksPerSecond));
            Assert.That(SmeltingModel.IsFuel(ItemId.Graystone), Is.False);
            Assert.That(SmeltingModel.FuelBurnTicks(ItemId.Graystone), Is.Zero);
        }

        [Test]
        public void ForgeConsumesFuelTwiceAsFastAsKiln()
        {
            int kilnPerUnit = SmeltingModel.EffectiveBurnTicksPerUnit(ItemId.Embercoal, CraftingStation.ClayKiln);
            int forgePerUnit = SmeltingModel.EffectiveBurnTicksPerUnit(ItemId.Embercoal, CraftingStation.BellowsForge);

            Assert.That(kilnPerUnit, Is.EqualTo(SmeltingModel.FuelBurnTicks(ItemId.Embercoal)));
            Assert.That(forgePerUnit, Is.EqualTo(kilnPerUnit / 2));
        }

        [Test]
        public void FuelUnitsRequiredRoundsUpAndIsHigherAtTheForge()
        {
            // A 12-second kiln recipe powered by branchwood (10s/unit) needs 2 logs.
            int kilnTime = 12 * WorldConstants.TicksPerSecond;
            Assert.That(SmeltingModel.FuelUnitsRequired(kilnTime, ItemId.BranchwoodLog, CraftingStation.ClayKiln), Is.EqualTo(2));
            // The same duration at the forge (logs burn 2× faster → 5s/unit) needs 3 logs.
            Assert.That(SmeltingModel.FuelUnitsRequired(kilnTime, ItemId.BranchwoodLog, CraftingStation.BellowsForge), Is.EqualTo(3));
            // Instant recipes need no fuel.
            Assert.That(SmeltingModel.FuelUnitsRequired(0, ItemId.Embercoal, CraftingStation.ClayKiln), Is.Zero);
            // Non-fuel yields zero.
            Assert.That(SmeltingModel.FuelUnitsRequired(kilnTime, ItemId.Graystone, CraftingStation.ClayKiln), Is.Zero);
        }

        [Test]
        public void HasEnoughFuelChecksRecipeDurationAgainstFuelStack()
        {
            var recipe = new CraftingRecipe(
                new ItemStack(ItemId.RosycopperBar, 1),
                CraftingStation.ClayKiln,
                12 * WorldConstants.TicksPerSecond,
                new[] { new ItemStack(ItemId.RawRosycopper, 2) });

            Assert.That(SmeltingModel.HasEnoughFuel(recipe, new ItemStack(ItemId.BranchwoodLog, 2), CraftingStation.ClayKiln), Is.True);
            Assert.That(SmeltingModel.HasEnoughFuel(recipe, new ItemStack(ItemId.BranchwoodLog, 1), CraftingStation.ClayKiln), Is.False);
            Assert.That(SmeltingModel.HasEnoughFuel(recipe, new ItemStack(ItemId.Graystone, 99), CraftingStation.ClayKiln), Is.False);
            Assert.That(SmeltingModel.HasEnoughFuel(recipe, ItemStack.Empty, CraftingStation.ClayKiln), Is.False);
        }

        [Test]
        public void StationDepositsPreserveStackDurability()
        {
            var station = new SmeltingStationModel(CraftingStation.ClayKiln, inputSlotCount: 1);
            ItemStack wornTool = new ItemStack(ItemId.ReedwoodFeller, 1).WithDurability(7);

            bool deposited = station.TryDepositInput(wornTool);

            Assert.That(deposited, Is.True);
            Assert.That(station.GetInput(0), Is.EqualTo(wornTool));
        }

        // ── Mend Bench repair (§10.7) ────────────────────────────────────────

        [Test]
        public void RepairAmountIsAQuarterOfMaxDurability()
        {
            Assert.That(MendBenchRepair.RepairAmount(160), Is.EqualTo(40));
            Assert.That(MendBenchRepair.RepairAmount(90), Is.EqualTo(23)); // 22.5 rounds away from zero
        }

        [Test]
        public void RepairMaterialMatchesToolTier()
        {
            Assert.That(MendBenchRepair.RepairMaterialForTier(1), Is.EqualTo(ItemId.WorkPlank));
            Assert.That(MendBenchRepair.RepairMaterialForTier(3), Is.EqualTo(ItemId.RosycopperBar));
            Assert.That(MendBenchRepair.RepairMaterialForTier(7), Is.EqualTo(ItemId.StarforgedCore));
        }

        [Test]
        public void RepairRestoresDurabilityAndConsumesMatchingMaterialAtMendBench()
        {
            ItemRegistry registry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(registry, slotCount: 4, hotbarSlotCount: 1);
            // Flint Delver: max 90, damaged to 30; matching material is flinty_shingle (tier 2).
            inventory.SetSlot(0, new ItemStack(ItemId.FlintDelver, 1).WithDurability(30));
            inventory.SetSlot(1, new ItemStack(ItemId.FlintyShingle, 2));

            RepairResult result = MendBenchRepair.TryRepair(registry, inventory, 0, CraftingStation.MendBench);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.MaterialUsed, Is.EqualTo(ItemId.FlintyShingle));
            Assert.That(result.NewDurability, Is.EqualTo(30 + 23)); // +25% of 90
            Assert.That(inventory.GetSlot(0).Durability, Is.EqualTo(53));
            Assert.That(inventory.CountOf(ItemId.FlintyShingle), Is.EqualTo(1));
        }

        [Test]
        public void RepairCapsAtMaxDurability()
        {
            ItemRegistry registry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(registry, slotCount: 4, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.FlintDelver, 1).WithDurability(80));
            inventory.SetSlot(1, new ItemStack(ItemId.FlintyShingle, 1));

            RepairResult result = MendBenchRepair.TryRepair(registry, inventory, 0, CraftingStation.MendBench);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NewDurability, Is.EqualTo(90)); // 80 + 23 capped at 90
        }

        [Test]
        public void RepairFailsWithoutMendBenchOrMaterialOrWear()
        {
            ItemRegistry registry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(registry, slotCount: 4, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.FlintDelver, 1).WithDurability(30));

            // Wrong station.
            Assert.That(MendBenchRepair.TryRepair(registry, inventory, 0, CraftingStation.BuildTable).FailureReason,
                Is.EqualTo(RepairFailureReason.WrongStation));
            // No matching material in inventory.
            Assert.That(MendBenchRepair.TryRepair(registry, inventory, 0, CraftingStation.MendBench).FailureReason,
                Is.EqualTo(RepairFailureReason.MissingRepairMaterial));
            // Undamaged tool.
            inventory.SetSlot(0, registry.CreateItemStack(ItemId.FlintDelver));
            inventory.SetSlot(1, new ItemStack(ItemId.FlintyShingle, 1));
            Assert.That(MendBenchRepair.TryRepair(registry, inventory, 0, CraftingStation.MendBench).FailureReason,
                Is.EqualTo(RepairFailureReason.AlreadyFullDurability));
        }
    }
}

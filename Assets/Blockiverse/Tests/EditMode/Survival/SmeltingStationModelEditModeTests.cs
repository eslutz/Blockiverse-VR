using Blockiverse.Survival;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class SmeltingStationModelEditModeTests
    {
        static SmeltingStationModel CreateKiln(out ItemRegistry registry)
        {
            registry = ItemRegistry.CreateDefault();
            return new SmeltingStationModel(CraftingStation.ClayKiln, inputSlotCount: 1, CraftingRecipeBook.CreateDefault(registry), registry);
        }

        [Test]
        public void KilnCraftsFiredBrickAfterItsDurationConsumingInputAndFuel()
        {
            SmeltingStationModel kiln = CreateKiln(out _);
            kiln.SetInput(0, new ItemStack(ItemId.ClayLump, 2));
            kiln.SetFuel(new ItemStack(ItemId.Embercoal, 1));

            kiln.Tick(8 * SmeltingModel.TicksPerSecond);

            Assert.That(kiln.Output, Is.EqualTo(new ItemStack(ItemId.FiredBrick, 1)));
            Assert.That(kiln.GetInput(0).IsEmpty, Is.True);
            Assert.That(kiln.Fuel.IsEmpty, Is.True);
            Assert.That(kiln.IsActive, Is.False);
        }

        [Test]
        public void KilnDoesNotCompleteBeforeItsDuration()
        {
            SmeltingStationModel kiln = CreateKiln(out _);
            kiln.SetInput(0, new ItemStack(ItemId.ClayLump, 2));
            kiln.SetFuel(new ItemStack(ItemId.Embercoal, 1));

            kiln.Tick(8 * SmeltingModel.TicksPerSecond - 1);

            Assert.That(kiln.IsActive, Is.True);
            Assert.That(kiln.Output.IsEmpty, Is.True);
            Assert.That(kiln.ProgressTicks, Is.EqualTo(8 * SmeltingModel.TicksPerSecond - 1));
        }

        [Test]
        public void KilnAutoContinuesWhileInputsAndFuelRemain()
        {
            SmeltingStationModel kiln = CreateKiln(out _);
            kiln.SetInput(0, new ItemStack(ItemId.ClayLump, 4));
            kiln.SetFuel(new ItemStack(ItemId.Embercoal, 2));

            kiln.Tick(16 * SmeltingModel.TicksPerSecond);

            Assert.That(kiln.Output, Is.EqualTo(new ItemStack(ItemId.FiredBrick, 2)));
            Assert.That(kiln.GetInput(0).IsEmpty, Is.True);
        }

        [Test]
        public void StationWithoutFuelDoesNotStart()
        {
            SmeltingStationModel kiln = CreateKiln(out _);
            kiln.SetInput(0, new ItemStack(ItemId.ClayLump, 2));

            Assert.That(kiln.TryBeginCraft(), Is.False);
            kiln.Tick(1000);
            Assert.That(kiln.IsActive, Is.False);
            Assert.That(kiln.Output.IsEmpty, Is.True);
        }

        [Test]
        public void ForgeSmeltsBronzeBarFromCopperAndTin()
        {
            ItemRegistry registry = ItemRegistry.CreateDefault();
            var forge = new SmeltingStationModel(CraftingStation.BellowsForge, inputSlotCount: 3, CraftingRecipeBook.CreateDefault(registry), registry);
            forge.SetInput(0, new ItemStack(ItemId.RosycopperBar, 3));
            forge.SetInput(1, new ItemStack(ItemId.PaletinBar, 1));
            forge.SetFuel(new ItemStack(ItemId.Embercoal, 1));

            forge.Tick(16 * SmeltingModel.TicksPerSecond);

            Assert.That(forge.Output, Is.EqualTo(new ItemStack(ItemId.BronzeBar, 4)));
            Assert.That(forge.GetInput(0).IsEmpty, Is.True);
            Assert.That(forge.GetInput(1).IsEmpty, Is.True);
        }
    }
}

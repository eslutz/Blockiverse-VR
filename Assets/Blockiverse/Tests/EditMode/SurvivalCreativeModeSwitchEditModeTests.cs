using Blockiverse.Gameplay;
using Blockiverse.Survival;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SurvivalCreativeModeSwitchEditModeTests
    {
        ItemRegistry registry;
        Inventory inventory;
        SurvivalCreativeModeSwitch modeSwitch;

        [SetUp]
        public void SetUp()
        {
            registry = ItemRegistry.CreateDefault();
            inventory = new Inventory(registry);
            modeSwitch = new SurvivalCreativeModeSwitch();
        }

        [Test]
        public void DefaultModeIsSurvival()
        {
            Assert.That(modeSwitch.CurrentMode, Is.EqualTo(PlayerModeState.Survival));
        }

        [Test]
        public void SwitchToCreativeReturnsTrueAndChangesMode()
        {
            bool result = modeSwitch.SwitchToCreative(inventory);

            Assert.That(result, Is.True);
            Assert.That(modeSwitch.CurrentMode, Is.EqualTo(PlayerModeState.Creative));
        }

        [Test]
        public void SwitchToCreativeWhenAlreadyCreativeReturnsFalse()
        {
            modeSwitch.SwitchToCreative(inventory);

            bool result = modeSwitch.SwitchToCreative(inventory);

            Assert.That(result, Is.False);
            Assert.That(modeSwitch.CurrentMode, Is.EqualTo(PlayerModeState.Creative));
        }

        [Test]
        public void SwitchToSurvivalWhenAlreadySurvivalReturnsFalse()
        {
            bool result = modeSwitch.SwitchToSurvival(inventory);

            Assert.That(result, Is.False);
            Assert.That(modeSwitch.CurrentMode, Is.EqualTo(PlayerModeState.Survival));
        }

        [Test]
        public void SurvivalInventorySnapshotPreservedOnSwitchToCreative()
        {
            inventory.Add(new ItemStack(ItemId.ReedwoodDelver, 1));

            modeSwitch.SwitchToCreative(inventory);

            Assert.That(inventory.CountOf(ItemId.ReedwoodDelver), Is.EqualTo(0),
                "Creative inventory should be empty after switch.");
            Assert.That(modeSwitch.HasSurvivalSnapshot, Is.True);
        }

        [Test]
        public void SurvivalInventoryRestoredOnSwitchBack()
        {
            inventory.Add(new ItemStack(ItemId.ReedwoodDelver, 1));

            modeSwitch.SwitchToCreative(inventory);

            inventory.Add(new ItemStack(ItemId.FlintDelver, 1));
            Assert.That(inventory.CountOf(ItemId.FlintDelver), Is.EqualTo(1));

            modeSwitch.SwitchToSurvival(inventory);

            Assert.That(inventory.CountOf(ItemId.ReedwoodDelver), Is.EqualTo(1),
                "Survival item should be restored.");
            Assert.That(inventory.CountOf(ItemId.FlintDelver), Is.EqualTo(0),
                "Creative item should be stripped on switch-back.");
        }

        [Test]
        public void RoundTripSurvivalCreativeSurvivalPreservesItems()
        {
            inventory.Add(new ItemStack(ItemId.ReedwoodSpade, 1));
            inventory.Add(new ItemStack(ItemId.FlintMallet, 1));

            modeSwitch.SwitchToCreative(inventory);
            modeSwitch.SwitchToSurvival(inventory);

            Assert.That(inventory.CountOf(ItemId.ReedwoodSpade), Is.EqualTo(1));
            Assert.That(inventory.CountOf(ItemId.FlintMallet), Is.EqualTo(1));
        }

        [Test]
        public void SwitchToSurvivalWithNoSnapshotClearsInventory()
        {
            modeSwitch.SwitchToCreative(inventory);
            inventory.Add(new ItemStack(ItemId.FlintFeller, 1));

            modeSwitch.SwitchToSurvival(inventory);

            Assert.That(inventory.CountOf(ItemId.FlintFeller), Is.EqualTo(0));
        }

        [Test]
        public void HasSurvivalSnapshotIsFalseAfterSwitchBackToSurvival()
        {
            inventory.Add(new ItemStack(ItemId.ReedwoodSpade, 1));

            modeSwitch.SwitchToCreative(inventory);
            Assert.That(modeSwitch.HasSurvivalSnapshot, Is.True);

            modeSwitch.SwitchToSurvival(inventory);
            Assert.That(modeSwitch.HasSurvivalSnapshot, Is.False);
        }
    }
}

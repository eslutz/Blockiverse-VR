using Blockiverse.Survival;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.SurvivalHealth
{
    public sealed class SurvivalVitalsEditModeTests
    {
        [Test]
        public void NewVitalsStartFull()
        {
            var vitals = new SurvivalVitals();
            Assert.That(vitals.Hunger, Is.EqualTo(100));
            Assert.That(vitals.Thirst, Is.EqualTo(100));
            Assert.That(vitals.Stamina, Is.EqualTo(100));
        }

        [Test]
        public void TickDepletesHungerAndThirstOverTime()
        {
            var vitals = new SurvivalVitals();
            vitals.Tick(SurvivalVitals.HungerTicksPerPoint);
            Assert.That(vitals.Hunger, Is.EqualTo(99));
            // Thirst depletes faster, so one hunger-interval also drops at least one thirst point.
            Assert.That(vitals.Thirst, Is.LessThanOrEqualTo(99));
        }

        [Test]
        public void EatAndDrinkRestoreClampedToMax()
        {
            var vitals = new SurvivalVitals();
            vitals.Restore(hunger: 50, thirst: 50, stamina: 50);
            vitals.Eat(30);
            Assert.That(vitals.Hunger, Is.EqualTo(80));
            vitals.Eat(100);
            Assert.That(vitals.Hunger, Is.EqualTo(100));
            vitals.Drink(60);
            Assert.That(vitals.Thirst, Is.EqualTo(100));
        }

        [Test]
        public void StaminaRegeneratesAndIsSpent()
        {
            var vitals = new SurvivalVitals();
            vitals.Restore(hunger: 100, thirst: 100, stamina: 0);

            Assert.That(vitals.TrySpendStamina(10), Is.False, "Cannot spend stamina that is not available.");

            vitals.Tick(SurvivalVitals.StaminaRegenTicksPerPoint * 10);
            Assert.That(vitals.Stamina, Is.EqualTo(10));

            Assert.That(vitals.TrySpendStamina(5), Is.True);
            Assert.That(vitals.Stamina, Is.EqualTo(5));
            Assert.That(vitals.TrySpendStamina(100), Is.False);
            Assert.That(vitals.Stamina, Is.EqualTo(5));
        }

        [Test]
        public void StarvationAndDehydrationDealPeriodicDamage()
        {
            var starving = new SurvivalVitals();
            starving.Restore(hunger: 0, thirst: 100, stamina: 100);
            Assert.That(starving.Tick(SurvivalVitals.StarvationDamageIntervalTicks),
                Is.EqualTo(SurvivalVitals.StarvationDamagePerInterval));

            var both = new SurvivalVitals();
            both.Restore(hunger: 0, thirst: 0, stamina: 100);
            Assert.That(both.Tick(SurvivalVitals.StarvationDamageIntervalTicks),
                Is.EqualTo(SurvivalVitals.StarvationDamagePerInterval * 2));

            var healthy = new SurvivalVitals();
            Assert.That(healthy.Tick(SurvivalVitals.StarvationDamageIntervalTicks), Is.EqualTo(0));
        }

        [Test]
        public void ResetToFullRestoresAllVitals()
        {
            var vitals = new SurvivalVitals();
            vitals.Restore(hunger: 10, thirst: 5, stamina: 0);
            vitals.ResetToFull();
            Assert.That(vitals.Hunger, Is.EqualTo(100));
            Assert.That(vitals.Thirst, Is.EqualTo(100));
            Assert.That(vitals.Stamina, Is.EqualTo(100));
        }
    }
}

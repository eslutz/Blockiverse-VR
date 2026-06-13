using Blockiverse.Survival;
using Blockiverse.Voxel;
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
        public void DifficultyProfilesChangeVitalDepletionRates()
        {
            var easy = new SurvivalVitals();
            var normal = new SurvivalVitals();
            var hard = new SurvivalVitals();

            easy.Tick(SurvivalVitals.HungerTicksPerPoint, SurvivalDifficultyProfile.Easy);
            normal.Tick(SurvivalVitals.HungerTicksPerPoint, SurvivalDifficultyProfile.Normal);
            hard.Tick(SurvivalVitals.HungerTicksPerPoint, SurvivalDifficultyProfile.Hard);

            Assert.That(easy.Hunger, Is.GreaterThan(normal.Hunger));
            Assert.That(hard.Hunger, Is.LessThan(normal.Hunger));
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
        public void ConsumableEffectsApplyPreparedFoodRestoresVitals()
        {
            var playerVitals = new PlayerVitals();
            var survivalVitals = new SurvivalVitals();

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.BerryMash, playerVitals, survivalVitals), Is.True);
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20 + ConsumableEffects.BerryMashHungerRestore));
            Assert.That(survivalVitals.Thirst, Is.EqualTo(20 + ConsumableEffects.BerryMashThirstRestore));

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.Flatbread, playerVitals, survivalVitals), Is.True);
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20 + ConsumableEffects.FlatbreadHungerRestore));

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.CookedMorsel, playerVitals, survivalVitals), Is.True);
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20 + ConsumableEffects.CookedMorselHungerRestore));

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.TrailRation, playerVitals, survivalVitals), Is.True);
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20 + ConsumableEffects.TrailRationHungerRestore));
            Assert.That(survivalVitals.Stamina, Is.EqualTo(20 + ConsumableEffects.TrailRationStaminaRestore));
        }

        [Test]
        public void ConsumableEffectsApplyCoreTableAndIgnoreNonConsumables()
        {
            var playerVitals = new PlayerVitals(currentHealth: 50);
            var survivalVitals = new SurvivalVitals();

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.FieldBandage, playerVitals, survivalVitals), Is.True);
            Assert.That(playerVitals.CurrentHealth, Is.EqualTo(50 + RecoveryWrap.HealAmount));
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20));
            Assert.That(survivalVitals.Thirst, Is.EqualTo(20));
            Assert.That(survivalVitals.Stamina, Is.EqualTo(20));

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.CleanWaterFlask, playerVitals, survivalVitals), Is.True);
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20));
            Assert.That(survivalVitals.Thirst, Is.EqualTo(20 + ConsumableEffects.CleanWaterThirstRestore));
            Assert.That(survivalVitals.Stamina, Is.EqualTo(20 + ConsumableEffects.CleanWaterStaminaRestore));

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.BerryCluster, playerVitals, survivalVitals), Is.True);
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20 + ConsumableEffects.BerryClusterHungerRestore));
            Assert.That(survivalVitals.Thirst, Is.EqualTo(20 + ConsumableEffects.BerryClusterThirstRestore));
            Assert.That(survivalVitals.Stamina, Is.EqualTo(20));

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.GrainBundle, playerVitals, survivalVitals), Is.True);
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20 + ConsumableEffects.GrainBundleHungerRestore));
            Assert.That(survivalVitals.Thirst, Is.EqualTo(20));
            Assert.That(survivalVitals.Stamina, Is.EqualTo(20));

            survivalVitals.Restore(hunger: 20, thirst: 20, stamina: 20);
            Assert.That(ConsumableEffects.TryApply(ItemId.BranchwoodLog, playerVitals, survivalVitals), Is.False);
            Assert.That(playerVitals.CurrentHealth, Is.EqualTo(50 + RecoveryWrap.HealAmount));
            Assert.That(survivalVitals.Hunger, Is.EqualTo(20));
            Assert.That(survivalVitals.Thirst, Is.EqualTo(20));
            Assert.That(survivalVitals.Stamina, Is.EqualTo(20));
        }

        [Test]
        public void BlockHazardsExposeCanonicalDamageTable()
        {
            AssertBlockHazard(
                BlockRegistry.Thornbrush,
                "thornbrush",
                BlockHazards.ThornbrushDamage,
                HazardDamageKind.Environmental,
                "thornbrush",
                BlockHazards.ThornbrushIntervalSeconds,
                HazardContactCells.Feet | HazardContactCells.Head);
            AssertBlockHazard(
                BlockRegistry.Campfire,
                "campfire",
                BlockHazards.CampfireDamage,
                HazardDamageKind.Heat,
                "campfire",
                BlockHazards.CampfireIntervalSeconds,
                HazardContactCells.Feet | HazardContactCells.GroundBelow);
            AssertBlockHazard(
                BlockRegistry.Emberflow,
                "emberflow",
                BlockHazards.EmberflowDamage,
                HazardDamageKind.Heat,
                "emberflow",
                BlockHazards.EmberflowIntervalSeconds,
                HazardContactCells.Feet | HazardContactCells.Head | HazardContactCells.GroundBelow);
            AssertBlockHazard(
                BlockRegistry.EmberflowFlow,
                "emberflow",
                BlockHazards.EmberflowDamage,
                HazardDamageKind.Heat,
                "emberflow",
                BlockHazards.EmberflowIntervalSeconds,
                HazardContactCells.Feet | HazardContactCells.Head | HazardContactCells.GroundBelow);

            Assert.That(BlockHazards.TryGetHazard(BlockRegistry.MeadowTurf, out _), Is.False);
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
        public void DifficultyProfilesChangeStarvationAndHazardDamage()
        {
            var easy = new SurvivalVitals();
            easy.Restore(hunger: 0, thirst: 100, stamina: 100);
            Assert.That(easy.Tick(SurvivalVitals.StarvationDamageIntervalTicks, SurvivalDifficultyProfile.Easy), Is.EqualTo(0));

            var hard = new SurvivalVitals();
            hard.Restore(hunger: 0, thirst: 100, stamina: 100);
            Assert.That(
                hard.Tick(SurvivalDifficultyProfile.Hard.StarvationDamageIntervalTicks, SurvivalDifficultyProfile.Hard),
                Is.EqualTo(SurvivalDifficultyProfile.Hard.StarvationDamagePerInterval));

            Assert.That(SurvivalDifficultyProfile.Easy.ScaleHazardDamage(2), Is.EqualTo(1));
            Assert.That(SurvivalDifficultyProfile.Normal.ScaleHazardDamage(2), Is.EqualTo(2));
            Assert.That(SurvivalDifficultyProfile.Hard.ScaleHazardDamage(2), Is.EqualTo(3));
            Assert.That(SurvivalDifficultyProfile.Easy.ScaleFallDamage(6), Is.EqualTo(3));
            Assert.That(SurvivalDifficultyProfile.Normal.ScaleFallDamage(6), Is.EqualTo(6));
            Assert.That(SurvivalDifficultyProfile.Hard.ScaleFallDamage(6), Is.EqualTo(9));
        }

        [Test]
        public void EnvironmentExposureRequiresSkyExposedColdPressure()
        {
            var warmNight = new SurvivalEnvironmentExposure(temperatureC: 8.0f, skyExposed: true, isNight: true);
            var shelteredCold = new SurvivalEnvironmentExposure(temperatureC: -8.0f, skyExposed: false, isNight: true, precipitationIntensity: 1.0f, stormIntensity: 1.0f);
            var coldNight = new SurvivalEnvironmentExposure(temperatureC: 4.0f, skyExposed: true, isNight: true);
            var blizzard = new SurvivalEnvironmentExposure(temperatureC: -8.0f, skyExposed: true, isNight: true, precipitationIntensity: 1.0f, stormIntensity: 0.8f);

            Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(warmNight), Is.EqualTo(0));
            Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(shelteredCold), Is.EqualTo(0));
            Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(coldNight), Is.EqualTo(1));
            Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(blizzard), Is.EqualTo(3));

            var vitals = new SurvivalVitals();
            Assert.That(
                vitals.TickEnvironmentExposure(SurvivalVitals.EnvironmentExposureDamageIntervalTicks, blizzard),
                Is.EqualTo(SurvivalVitals.EnvironmentExposureDamagePerInterval * 3));
        }

        [Test]
        public void FallDamageStartsAfterSafeDistanceAndScalesByDifficulty()
        {
            Assert.That(SurvivalVitals.ComputeFallDamage(SurvivalVitals.FallSafeDistanceMeters, SurvivalDifficultyProfile.Normal), Is.EqualTo(0));
            Assert.That(SurvivalVitals.ComputeFallDamage(SurvivalVitals.FallSafeDistanceMeters + 2.0f, SurvivalDifficultyProfile.Normal), Is.EqualTo(12));
            Assert.That(SurvivalVitals.ComputeFallDamage(SurvivalVitals.FallSafeDistanceMeters + 2.0f, SurvivalDifficultyProfile.Easy), Is.EqualTo(6));
            Assert.That(SurvivalVitals.ComputeFallDamage(SurvivalVitals.FallSafeDistanceMeters + 2.0f, SurvivalDifficultyProfile.Hard), Is.EqualTo(18));
            Assert.That(SurvivalVitals.ComputeFallDamage(40.0f, SurvivalDifficultyProfile.Hard), Is.EqualTo(SurvivalVitals.FallMaxDamage));
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

        [Test]
        public void RestoreFromClampsOutOfRangeValuesAndResetsAccumulators()
        {
            var vitals = new SurvivalVitals();
            var blizzard = new SurvivalEnvironmentExposure(
                temperatureC: -8.0f,
                skyExposed: true,
                isNight: true,
                precipitationIntensity: 1.0f,
                stormIntensity: 0.8f);

            vitals.Tick(SurvivalVitals.StaminaRegenTicksPerPoint - 1);
            vitals.Restore(hunger: 0, thirst: 100, stamina: 100);
            Assert.That(vitals.Tick(SurvivalVitals.StarvationDamageIntervalTicks - 1), Is.Zero);
            Assert.That(vitals.TickEnvironmentExposure(
                SurvivalVitals.EnvironmentExposureDamageIntervalTicks - 1,
                blizzard), Is.Zero);

            vitals.RestoreFrom(hunger: 150, thirst: -25, stamina: 50);

            Assert.That(vitals.Hunger, Is.EqualTo(100));
            Assert.That(vitals.Thirst, Is.Zero);
            Assert.That(vitals.Stamina, Is.EqualTo(50));

            int subPointTicksAfterRestore =
                SurvivalVitals.HungerTicksPerPoint - SurvivalVitals.StaminaRegenTicksPerPoint + 1;
            Assert.That(vitals.Tick(subPointTicksAfterRestore), Is.Zero);
            Assert.That(vitals.Hunger, Is.EqualTo(100),
                "A partial hunger accumulator from before RestoreFrom must not deplete immediately after load.");
            Assert.That(vitals.TickEnvironmentExposure(1, blizzard), Is.Zero,
                "Environment exposure accumulation from before RestoreFrom must not deal immediate damage after load.");
        }

        static void AssertBlockHazard(
            BlockId block,
            string expectedId,
            int expectedDamage,
            HazardDamageKind expectedKind,
            string expectedSourceId,
            float expectedIntervalSeconds,
            HazardContactCells expectedContactCells)
        {
            Assert.That(BlockHazards.TryGetHazard(block, out BlockHazard hazard), Is.True);
            Assert.That(hazard.Hazard.Id, Is.EqualTo(expectedId));
            Assert.That(hazard.Hazard.DamagePerTick.Amount, Is.EqualTo(expectedDamage));
            Assert.That(hazard.Hazard.DamagePerTick.Kind, Is.EqualTo(expectedKind));
            Assert.That(hazard.Hazard.DamagePerTick.SourceId, Is.EqualTo(expectedSourceId));
            Assert.That(hazard.Hazard.TickIntervalSeconds, Is.EqualTo(expectedIntervalSeconds));
            Assert.That(hazard.ContactCells, Is.EqualTo(expectedContactCells));
        }
    }
}

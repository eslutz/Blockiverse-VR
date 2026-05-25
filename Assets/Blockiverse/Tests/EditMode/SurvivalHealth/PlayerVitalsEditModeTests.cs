using System;
using System.Collections.Generic;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.SurvivalHealth
{
    public sealed class PlayerVitalsEditModeTests
    {
        [Test]
        public void DefaultVitalsStartAtFullHealth()
        {
            var vitals = new PlayerVitals();

            Assert.That(vitals.MaxHealth, Is.EqualTo(100));
            Assert.That(vitals.CurrentHealth, Is.EqualTo(100));
            Assert.That(vitals.IsDead, Is.False);
        }

        [Test]
        public void ConstructorValidatesHealthBounds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerVitals(maxHealth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerVitals(maxHealth: 100, currentHealth: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerVitals(maxHealth: 100, currentHealth: 101));
        }

        [Test]
        public void DamageClampsAtZeroAndMarksDead()
        {
            var vitals = new PlayerVitals();

            HealthChangeResult result = vitals.ApplyDamage(125);

            Assert.That(result.Kind, Is.EqualTo(HealthChangeKind.Damage));
            Assert.That(result.RequestedAmount, Is.EqualTo(125));
            Assert.That(result.AppliedAmount, Is.EqualTo(100));
            Assert.That(result.PreviousHealth, Is.EqualTo(100));
            Assert.That(result.CurrentHealth, Is.EqualTo(0));
            Assert.That(result.WasDead, Is.False);
            Assert.That(result.IsDead, Is.True);
            Assert.That(result.DidDie, Is.True);
            Assert.That(vitals.CurrentHealth, Is.EqualTo(0));
            Assert.That(vitals.IsDead, Is.True);
        }

        [Test]
        public void DamageAndHealingRaiseHealthChangeEvents()
        {
            var vitals = new PlayerVitals(currentHealth: 75);
            var changes = new List<HealthChangeResult>();
            var deaths = new List<HealthChangeResult>();
            vitals.HealthChanged += changes.Add;
            vitals.Died += deaths.Add;

            vitals.ApplyDamage(10);
            vitals.Heal(5);
            vitals.ApplyDamage(100);

            Assert.That(changes, Has.Count.EqualTo(3));
            Assert.That(changes[0].Kind, Is.EqualTo(HealthChangeKind.Damage));
            Assert.That(changes[0].CurrentHealth, Is.EqualTo(65));
            Assert.That(changes[1].Kind, Is.EqualTo(HealthChangeKind.Healing));
            Assert.That(changes[1].CurrentHealth, Is.EqualTo(70));
            Assert.That(deaths, Has.Count.EqualTo(1));
            Assert.That(deaths[0].DidDie, Is.True);
            Assert.That(deaths[0].CurrentHealth, Is.EqualTo(0));
        }

        [Test]
        public void HealingCapsAtMaxHealth()
        {
            var vitals = new PlayerVitals(currentHealth: 90);

            HealthChangeResult result = vitals.Heal(25);

            Assert.That(result.Kind, Is.EqualTo(HealthChangeKind.Healing));
            Assert.That(result.RequestedAmount, Is.EqualTo(25));
            Assert.That(result.AppliedAmount, Is.EqualTo(10));
            Assert.That(result.PreviousHealth, Is.EqualTo(90));
            Assert.That(result.CurrentHealth, Is.EqualTo(100));
            Assert.That(vitals.CurrentHealth, Is.EqualTo(100));
            Assert.That(vitals.IsDead, Is.False);
        }

        [Test]
        public void HealingDeadPlayersDoesNotRevive()
        {
            var vitals = new PlayerVitals();
            vitals.ApplyDamage(100);

            HealthChangeResult result = vitals.Heal(25);

            Assert.That(result.Kind, Is.EqualTo(HealthChangeKind.Healing));
            Assert.That(result.RequestedAmount, Is.EqualTo(25));
            Assert.That(result.AppliedAmount, Is.EqualTo(0));
            Assert.That(result.PreviousHealth, Is.EqualTo(0));
            Assert.That(result.CurrentHealth, Is.EqualTo(0));
            Assert.That(result.WasDead, Is.True);
            Assert.That(result.IsDead, Is.True);
            Assert.That(result.Changed, Is.False);
            Assert.That(vitals.IsDead, Is.True);
        }

        [Test]
        public void RecoveryWrapHealsExactlyTwentyFiveWhenPossible()
        {
            var vitals = new PlayerVitals(currentHealth: 50);

            HealthChangeResult result = RecoveryWrap.ApplyTo(vitals);

            Assert.That(RecoveryWrap.HealAmount, Is.EqualTo(25));
            Assert.That(result.Kind, Is.EqualTo(HealthChangeKind.Healing));
            Assert.That(result.RequestedAmount, Is.EqualTo(25));
            Assert.That(result.AppliedAmount, Is.EqualTo(25));
            Assert.That(result.PreviousHealth, Is.EqualTo(50));
            Assert.That(result.CurrentHealth, Is.EqualTo(75));
            Assert.That(vitals.CurrentHealth, Is.EqualTo(75));
        }

        [Test]
        public void HazardTickAppliesConfiguredNonCombatDamage()
        {
            var vitals = new PlayerVitals();
            var hazard = new HazardVolumeDefinition(
                "ember-bramble",
                new HazardDamage(amount: 15, kind: HazardDamageKind.Environmental, sourceId: "heat-vent"),
                tickIntervalSeconds: 1f);

            HazardTickResult result = hazard.ApplyTick(vitals);

            Assert.That(result.HazardId, Is.EqualTo("ember-bramble"));
            Assert.That(result.Damage.Amount, Is.EqualTo(15));
            Assert.That(result.Damage.Kind, Is.EqualTo(HazardDamageKind.Environmental));
            Assert.That(result.HealthChange.Kind, Is.EqualTo(HealthChangeKind.Damage));
            Assert.That(result.HealthChange.AppliedAmount, Is.EqualTo(15));
            Assert.That(result.HealthChange.CurrentHealth, Is.EqualTo(85));
            Assert.That(vitals.CurrentHealth, Is.EqualTo(85));
        }

        [Test]
        public void RespawnRestoresHealthAndReportsSafeSpawn()
        {
            var vitals = new PlayerVitals();
            var safeSpawn = new BlockPosition(2, 3, 4);
            vitals.ApplyDamage(100);

            RespawnResult result = vitals.RespawnAt(safeSpawn);

            Assert.That(result.SpawnPosition, Is.EqualTo(safeSpawn));
            Assert.That(result.PreviousHealth, Is.EqualTo(0));
            Assert.That(result.CurrentHealth, Is.EqualTo(100));
            Assert.That(result.WasDead, Is.True);
            Assert.That(result.IsDead, Is.False);
            Assert.That(vitals.CurrentHealth, Is.EqualTo(100));
            Assert.That(vitals.IsDead, Is.False);
        }

        [Test]
        public void NegativeDamageOrHealingAmountsAreRejected()
        {
            var vitals = new PlayerVitals();

            Assert.Throws<ArgumentOutOfRangeException>(() => vitals.ApplyDamage(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => vitals.Heal(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HazardDamage(0, HazardDamageKind.Environmental));
        }
    }
}

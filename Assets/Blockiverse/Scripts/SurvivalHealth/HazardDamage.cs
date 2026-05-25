using System;

namespace Blockiverse.Survival
{
    public enum HazardDamageKind
    {
        Environmental,
        Heat,
        Cold,
        Void,
        Suffocation,
        Toxic
    }

    public readonly struct HazardDamage
    {
        public HazardDamage(int amount, HazardDamageKind kind, string sourceId = null)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Hazard damage must be greater than zero.");

            Amount = amount;
            Kind = kind;
            SourceId = sourceId ?? string.Empty;
        }

        public int Amount { get; }
        public HazardDamageKind Kind { get; }
        public string SourceId { get; }
    }

    public sealed class HazardVolumeDefinition
    {
        public HazardVolumeDefinition(string id, HazardDamage damagePerTick, float tickIntervalSeconds)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Hazard IDs must be non-empty.", nameof(id));

            if (tickIntervalSeconds <= 0f || float.IsNaN(tickIntervalSeconds) || float.IsInfinity(tickIntervalSeconds))
                throw new ArgumentOutOfRangeException(nameof(tickIntervalSeconds), "Hazard tick interval must be a finite positive value.");

            Id = id;
            DamagePerTick = damagePerTick;
            TickIntervalSeconds = tickIntervalSeconds;
        }

        public string Id { get; }
        public HazardDamage DamagePerTick { get; }
        public float TickIntervalSeconds { get; }

        public HazardTickResult ApplyTick(PlayerVitals vitals)
        {
            if (vitals == null)
                throw new ArgumentNullException(nameof(vitals));

            HealthChangeResult healthChange = vitals.ApplyDamage(DamagePerTick.Amount);
            return new HazardTickResult(Id, DamagePerTick, healthChange);
        }
    }

    public readonly struct HazardTickResult
    {
        public HazardTickResult(string hazardId, HazardDamage damage, HealthChangeResult healthChange)
        {
            HazardId = hazardId;
            Damage = damage;
            HealthChange = healthChange;
        }

        public string HazardId { get; }
        public HazardDamage Damage { get; }
        public HealthChangeResult HealthChange { get; }
    }
}

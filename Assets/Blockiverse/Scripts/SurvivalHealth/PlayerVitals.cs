using System;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public sealed class PlayerVitals
    {
        public const int DefaultMaxHealth = 100;

        public PlayerVitals(int maxHealth = DefaultMaxHealth, int? currentHealth = null)
        {
            if (maxHealth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxHealth), "Max health must be greater than zero.");

            int startingHealth = currentHealth ?? maxHealth;
            if (startingHealth < 0 || startingHealth > maxHealth)
                throw new ArgumentOutOfRangeException(nameof(currentHealth), "Current health must be between zero and max health.");

            MaxHealth = maxHealth;
            CurrentHealth = startingHealth;
        }

        public event Action<HealthChangeResult> HealthChanged;
        public event Action<HealthChangeResult> Died;

        public int MaxHealth { get; }
        public int CurrentHealth { get; private set; }
        public bool IsDead => CurrentHealth <= 0;

        public HealthChangeResult ApplyDamage(int amount)
        {
            ValidateNonNegativeAmount(amount, nameof(amount));

            int previousHealth = CurrentHealth;
            bool wasDead = IsDead;
            int appliedAmount = wasDead ? 0 : Math.Min(amount, CurrentHealth);
            CurrentHealth -= appliedAmount;

            var result = new HealthChangeResult(
                HealthChangeKind.Damage,
                amount,
                appliedAmount,
                previousHealth,
                CurrentHealth,
                MaxHealth,
                wasDead);

            PublishHealthChange(result);
            return result;
        }

        public HealthChangeResult Heal(int amount)
        {
            ValidateNonNegativeAmount(amount, nameof(amount));

            int previousHealth = CurrentHealth;
            bool wasDead = IsDead;
            int appliedAmount = wasDead ? 0 : Math.Min(amount, MaxHealth - CurrentHealth);
            CurrentHealth += appliedAmount;

            var result = new HealthChangeResult(
                HealthChangeKind.Healing,
                amount,
                appliedAmount,
                previousHealth,
                CurrentHealth,
                MaxHealth,
                wasDead);

            PublishHealthChange(result);
            return result;
        }

        public RespawnResult RespawnAt(BlockPosition safeSpawnPosition)
        {
            int previousHealth = CurrentHealth;
            bool wasDead = IsDead;
            CurrentHealth = MaxHealth;

            var result = new HealthChangeResult(
                HealthChangeKind.Respawn,
                MaxHealth,
                CurrentHealth - previousHealth,
                previousHealth,
                CurrentHealth,
                MaxHealth,
                wasDead);
            PublishHealthChange(result);

            return new RespawnResult(safeSpawnPosition, previousHealth, CurrentHealth, MaxHealth, wasDead);
        }

        public void ResetToFullHealth()
        {
            RespawnAt(new BlockPosition(0, 0, 0));
        }

        static void ValidateNonNegativeAmount(int amount, string parameterName)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(parameterName, "Health change amounts must be non-negative.");
        }

        void PublishHealthChange(HealthChangeResult result)
        {
            if (!result.Changed)
                return;

            HealthChanged?.Invoke(result);
            if (result.DidDie)
                Died?.Invoke(result);
        }
    }
}

using System;

namespace Blockiverse.Core
{
    // Core read-model seam for the local player's health, letting UI bind to live health without
    // referencing the Blockiverse.Survival.Health assembly. Implemented by PlayerVitals
    // (Blockiverse.Survival.Health); SurvivalVitalsRuntime exposes it via HealthView. Exposes only
    // primitives so no Survival.Health types leak into Core — HealthChanged is parameterless.
    public interface IPlayerVitalsView
    {
        int CurrentHealth { get; }
        int MaxHealth { get; }
        bool IsDead { get; }
        event Action HealthChanged;
    }
}

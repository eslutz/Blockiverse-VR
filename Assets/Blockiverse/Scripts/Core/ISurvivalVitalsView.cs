namespace Blockiverse.Core
{
    // Core read-model seam for the local player's survival vitals (hunger/thirst/stamina), letting
    // UI display them without referencing the Blockiverse.Survival.Health assembly. Implemented by
    // SurvivalVitals (Blockiverse.Survival.Health); SurvivalVitalsRuntime exposes it via
    // SurvivalVitalsView. These vitals tick without events, so the HUD refreshes them on a cadence.
    public interface ISurvivalVitalsView
    {
        int Hunger { get; }
        int Thirst { get; }
        int Stamina { get; }
    }
}

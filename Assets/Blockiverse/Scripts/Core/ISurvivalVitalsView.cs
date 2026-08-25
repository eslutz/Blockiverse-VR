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

        // The ceiling all three share (SurvivalVitals.DefaultMax, 100). Added 2026-08-25 for the
        // metered vitals readout: a meter cannot be filled without knowing what full is, and the
        // sentence this replaced only ever printed the raw numbers so it never had to ask.
        //
        // On the seam rather than read off SurvivalVitals directly, because reaching for the
        // concrete type would drag Blockiverse.Survival.Health into every presentation assembly —
        // which is the whole thing this interface exists to prevent.
        int Max { get; }
    }
}

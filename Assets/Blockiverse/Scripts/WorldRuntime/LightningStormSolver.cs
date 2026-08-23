using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // How violent a given thunderstorm is, and how that violence changes over the storm's life.
    //
    // Lightning used to roll a flat 35% every check, so every storm in the game struck at exactly
    // the same rate from its first second to its last. This replaces that constant with two terms:
    //
    //   character -- a per-storm roll, so one storm is a distant grumble and the next is violent.
    //   progress  -- an arc across the storm's life, so it builds, peaks and tapers.
    //
    // Both inputs already travel in the environment sync snapshot (WeatherSyncState.Ticks plus the
    // world seed and elapsed ticks), so host and clients derive the same number for the same storm
    // with no new networking and no new persisted state.
    //
    // A note on what "a storm" means here: WeatherService re-rolls its transition every
    // MinDurationTicks and can pick Thunderstorm again, which resets TicksInCurrentState. So a long
    // storm is a sequence of 1200-tick segments, each with its own character and its own arc. That
    // is the desired behaviour rather than a limitation -- a ten-minute storm surges and lulls
    // instead of holding one intensity -- but it does mean "storm start" is segment start.
    public static class LightningStormSolver
    {
        // A light storm still strikes; a violent one is roughly twice as busy as the old constant.
        public const int MinPeakChancePercent = 12;
        public const int MaxPeakChancePercent = 70;

        // The arc never reaches zero: a storm that stops striking entirely at its edges reads as
        // the weather being broken, not as the storm tapering.
        public const float MinArcScale = 0.35f;

        // Distinct from every other DeterministicHash consumer so the storm roll cannot correlate
        // with terrain, biome, structure or growth rolls that share a seed.
        public const int StormCharacterSalt = 0x11607;

        // How violent this storm is, in [0, 1). Derived from the storm's start tick so every peer
        // computes the same value for the same storm without exchanging anything.
        public static float ResolveCharacter(int worldSeed, long stormStartTick) =>
            (float)DeterministicHash.UnitRoll(worldSeed, 0, 0, 0, StormCharacterSalt, stormStartTick);

        // Where in the storm's life we are, in [0, 1].
        public static float ResolveProgress(int ticksInState, int stormDurationTicks)
        {
            if (stormDurationTicks <= 0)
                return 0.0f;

            return Mathf.Clamp01(ticksInState / (float)stormDurationTicks);
        }

        // The build-peak-taper arc, in [MinArcScale, 1]. Peaks at the storm's midpoint.
        public static float ResolveArcScale(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            return Mathf.Lerp(MinArcScale, 1.0f, 4.0f * clamped * (1.0f - clamped));
        }

        // The percent chance that a single lightning check produces a strike attempt.
        public static int StrikeChancePercent(float character, float progress)
        {
            float peak = Mathf.Lerp(MinPeakChancePercent, MaxPeakChancePercent, Mathf.Clamp01(character));
            return Mathf.Clamp(Mathf.RoundToInt(peak * ResolveArcScale(progress)), 0, 100);
        }
    }
}

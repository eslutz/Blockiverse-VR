using UnityEngine;

namespace Blockiverse.Gameplay
{
    // The sky flash that accompanies a strike: how bright, for how long, and how much of it a bolt
    // at a given distance earns.
    //
    // Pure static so all of it is testable without a scene. The state (when the flash started, how
    // strong it was) lives inside BlockiverseLightingCycleController, because that component
    // rewrites ambient every LateUpdate and anything outside it would be erased within a frame.
    public static class LightningFlashSolver
    {
        // 6 ticks at 20 ticks/second -- the ruleset's flashDurationTicks, converted once here.
        public const float FlashDurationSeconds = 0.30f;

        // A pure decay starting at full brightness is a single-frame pop at 72 Hz and reads as a
        // dropped frame rather than as lightning. A short ramp in is enough to make it read.
        public const float AttackSeconds = 0.04f;

        // Exponential falloff after the attack. Higher decays faster.
        public const float DecayRate = 9.0f;

        // Inside this distance the flash is at full strength -- a strike this close should wash
        // out the sky.
        public const float FullStrengthDistance = 20.0f;

        // At and beyond this the flash is exactly zero: you see the bolt on the horizon and get no
        // flash at all. Deliberately equal to the outer edge of the strike ring, so the two ends
        // of the distance band map onto the two ends of the flash range.
        public const float NoStrengthDistance = LightningStrikeSelector.MaxRingRadius;

        // How bright the flash is at `elapsed` seconds after the strike, in [0, 1].
        public static float Intensity(float elapsed)
        {
            if (elapsed < 0.0f || elapsed >= FlashDurationSeconds)
                return 0.0f;

            if (elapsed < AttackSeconds)
                return elapsed / AttackSeconds;

            float decayElapsed = elapsed - AttackSeconds;
            float decayWindow = FlashDurationSeconds - AttackSeconds;

            // Subtracting the value at the window's end so the curve reaches exactly zero rather
            // than stepping down from a small residual -- a visible pop at the end otherwise.
            float tail = Mathf.Exp(-DecayRate * decayElapsed);
            float floor = Mathf.Exp(-DecayRate * decayWindow);

            return Mathf.Clamp01((tail - floor) / (1.0f - floor));
        }

        // How much flash a strike at this distance earns, in [0, 1]. Quadratic, because real
        // brightness falls with distance squared and a linear ramp reads as far too bright out at
        // the middle of the ring.
        public static float DistanceStrength(float distance)
        {
            if (distance <= FullStrengthDistance)
                return 1.0f;

            if (distance >= NoStrengthDistance)
                return 0.0f;

            float t = (distance - FullStrengthDistance) / (NoStrengthDistance - FullStrengthDistance);
            float remaining = 1.0f - t;
            return remaining * remaining;
        }

        // The additive ambient term for a flash of `strength` at `elapsed` seconds, applied on top
        // of whatever the lighting cycle already resolved. Clamped per channel so a daytime flash
        // cannot push ambient past white.
        public static Color AmbientBoost(Color baseAmbient, float strength, float elapsed)
        {
            float amount = Mathf.Clamp01(strength) * Intensity(elapsed);

            if (amount <= 0.0f)
                return baseAmbient;

            // Slightly blue-white, matching the bolt's own colour.
            var flash = new Color(0.82f, 0.88f, 1.0f) * amount;

            return new Color(
                Mathf.Clamp01(baseAmbient.r + flash.r),
                Mathf.Clamp01(baseAmbient.g + flash.g),
                Mathf.Clamp01(baseAmbient.b + flash.b),
                baseAmbient.a);
        }
    }
}

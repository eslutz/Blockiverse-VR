using System;
using Blockiverse.Core;

namespace Blockiverse.Survival
{
    // Player survival vitals: hunger, thirst, and stamina (0..max, max 100 per the survival
    // menus HUD). Depletion/regen rates are tunable balance constants (the rulesets fix the
    // 0..100 range and persistence fields but not the rates). Hunger/thirst depletion is driven
    // by world ticks; reaching zero applies periodic starvation/dehydration damage that callers
    // route into PlayerVitals. Stamina regenerates over time and is spent by exertion.
    public sealed class SurvivalVitals : ISurvivalVitalsView
    {
        public const int DefaultMax = 100;

        // Ticks of game time to lose one point (20 ticks/second). Defaults: hunger fully
        // depletes over ~1 game day (24000 ticks), thirst a little faster.
        public const int HungerTicksPerPoint = 240;
        public const int ThirstTicksPerPoint = 180;
        // Stamina recovers one point every second of rest.
        public const int StaminaRegenTicksPerPoint = 20;
        // While starving or dehydrated, deal damage on this cadence. Normal difficulty applies
        // 4 HP/minute per empty vital, enough to make an ignored vital failure urgent.
        public const int StarvationDamageIntervalTicks = 600;
        public const int StarvationDamagePerInterval = 2;
        // Cold weather pressure is tick-driven, not frame-driven, so clients remain aligned with
        // the host's synced weather/time state.
        public const int EnvironmentExposureDamageIntervalTicks = 600;
        public const int EnvironmentExposureDamagePerInterval = 1;
        public const float ColdExposureTemperatureThresholdC = 2.0f;
        public const float NightColdPressureThresholdC = 5.0f;
        public const float FallSafeDistanceMeters = 3.0f;
        public const int FallDamagePerMeter = 6;
        public const int FallMaxDamage = 60;

        int hungerAccumulator;
        int thirstAccumulator;
        int staminaAccumulator;
        int starvationAccumulator;
        int environmentExposureAccumulator;

        public SurvivalVitals(int max = DefaultMax)
        {
            if (max <= 0)
                throw new ArgumentOutOfRangeException(nameof(max), "Max vital value must be greater than zero.");

            Max = max;
            Hunger = max;
            Thirst = max;
            Stamina = max;
        }

        public int Max { get; }
        public int Hunger { get; private set; }
        public int Thirst { get; private set; }
        public int Stamina { get; private set; }

        public bool IsStarving => Hunger <= 0;
        public bool IsDehydrated => Thirst <= 0;

        public int Tick(int ticks) => Tick(ticks, SurvivalDifficultyProfile.Normal);

        // Advances vitals by the given number of ticks. Returns the health damage to apply this
        // step from starvation/dehydration (0 when both are above zero).
        public int Tick(int ticks, SurvivalDifficultyProfile difficulty)
        {
            if (ticks <= 0)
                return 0;

            if (!difficulty.IsValid)
                difficulty = SurvivalDifficultyProfile.Normal;

            Hunger = DepleteWithAccumulator(Hunger, ref hungerAccumulator, ticks, difficulty.HungerTicksPerPoint);
            Thirst = DepleteWithAccumulator(Thirst, ref thirstAccumulator, ticks, difficulty.ThirstTicksPerPoint);
            Stamina = RegenWithAccumulator(Stamina, ref staminaAccumulator, ticks, difficulty.StaminaRegenTicksPerPoint, Max);

            if (!IsStarving && !IsDehydrated)
            {
                starvationAccumulator = 0;
                return 0;
            }

            // Each empty vital contributes a damage source on the starvation cadence.
            int sources = (IsStarving ? 1 : 0) + (IsDehydrated ? 1 : 0);
            starvationAccumulator += ticks;
            int damage = 0;
            while (starvationAccumulator >= difficulty.StarvationDamageIntervalTicks)
            {
                starvationAccumulator -= difficulty.StarvationDamageIntervalTicks;
                damage += difficulty.StarvationDamagePerInterval * sources;
            }
            return damage;
        }

        public int TickEnvironmentExposure(int ticks, SurvivalEnvironmentExposure exposure) =>
            TickEnvironmentExposure(ticks, exposure, SurvivalDifficultyProfile.Normal);

        public int TickEnvironmentExposure(int ticks, SurvivalEnvironmentExposure exposure, SurvivalDifficultyProfile difficulty)
        {
            if (ticks <= 0)
                return 0;

            if (!difficulty.IsValid)
                difficulty = SurvivalDifficultyProfile.Normal;

            int pressureSources = ComputeEnvironmentPressureSources(exposure);
            if (pressureSources <= 0)
            {
                environmentExposureAccumulator = 0;
                return 0;
            }

            environmentExposureAccumulator += ticks;
            int damage = 0;
            while (environmentExposureAccumulator >= difficulty.EnvironmentExposureDamageIntervalTicks)
            {
                environmentExposureAccumulator -= difficulty.EnvironmentExposureDamageIntervalTicks;
                damage += difficulty.EnvironmentExposureDamagePerInterval * pressureSources;
            }
            return damage;
        }

        public void ResetEnvironmentExposure() => environmentExposureAccumulator = 0;

        public static int ComputeEnvironmentPressureSources(SurvivalEnvironmentExposure exposure)
        {
            if (!exposure.SkyExposed)
                return 0;

            bool coldEnough = exposure.TemperatureC <= ColdExposureTemperatureThresholdC;
            bool nightCold = exposure.IsNight && exposure.TemperatureC <= NightColdPressureThresholdC;
            if (!coldEnough && !nightCold)
                return 0;

            int sources = 1;
            if (exposure.PrecipitationIntensity >= 0.6f)
                sources++;
            if (exposure.StormIntensity >= 0.6f)
                sources++;
            return sources;
        }

        public static int ComputeFallDamage(float fallMeters, SurvivalDifficultyProfile difficulty)
        {
            if (fallMeters <= FallSafeDistanceMeters)
                return 0;

            if (!difficulty.IsValid)
                difficulty = SurvivalDifficultyProfile.Normal;

            int baseDamage = (int)Math.Ceiling((fallMeters - FallSafeDistanceMeters) * FallDamagePerMeter);
            return Math.Min(FallMaxDamage, difficulty.ScaleFallDamage(baseDamage));
        }

        public void Eat(int amount) => Hunger = Add(Hunger, amount, Max);
        public void Drink(int amount) => Thirst = Add(Thirst, amount, Max);
        public void RecoverStamina(int amount) => Stamina = Add(Stamina, amount, Max);

        // Restores saved vitals (world load). Values clamp into [0, Max]; the sub-point
        // accumulators restart, which at worst shifts the next point by under a second.
        public void RestoreFrom(int hunger, int thirst, int stamina)
        {
            Hunger = Math.Clamp(hunger, 0, Max);
            Thirst = Math.Clamp(thirst, 0, Max);
            Stamina = Math.Clamp(stamina, 0, Max);
            hungerAccumulator = 0;
            thirstAccumulator = 0;
            staminaAccumulator = 0;
            starvationAccumulator = 0;
            environmentExposureAccumulator = 0;
        }

        // Spends stamina for an exertion (sprint/jump). Returns false without spending when there
        // is not enough stamina.
        public bool TrySpendStamina(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Stamina cost must be non-negative.");
            if (amount > Stamina)
                return false;

            Stamina -= amount;
            return true;
        }

        public void ResetToFull()
        {
            Hunger = Max;
            Thirst = Max;
            Stamina = Max;
            hungerAccumulator = thirstAccumulator = staminaAccumulator = starvationAccumulator = environmentExposureAccumulator = 0;
        }

        // Restores persisted values (clamped) without resetting accumulators to defaults.
        public void Restore(int hunger, int thirst, int stamina)
        {
            Hunger = Clamp(hunger, Max);
            Thirst = Clamp(thirst, Max);
            Stamina = Clamp(stamina, Max);
        }

        static int DepleteWithAccumulator(int value, ref int accumulator, int ticks, int ticksPerPoint)
        {
            if (value <= 0)
                return 0;

            accumulator += ticks;
            while (accumulator >= ticksPerPoint && value > 0)
            {
                accumulator -= ticksPerPoint;
                value--;
            }
            return value;
        }

        static int RegenWithAccumulator(int value, ref int accumulator, int ticks, int ticksPerPoint, int max)
        {
            if (value >= max)
            {
                accumulator = 0;
                return max;
            }

            accumulator += ticks;
            while (accumulator >= ticksPerPoint && value < max)
            {
                accumulator -= ticksPerPoint;
                value++;
            }
            return value;
        }

        static int Add(int value, int amount, int max)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Restore amount must be non-negative.");
            return Math.Min(max, value + amount);
        }

        static int Clamp(int value, int max) => Math.Max(0, Math.Min(max, value));
    }

    public readonly struct SurvivalEnvironmentExposure
    {
        public SurvivalEnvironmentExposure(
            float temperatureC,
            bool skyExposed,
            bool isNight,
            float precipitationIntensity = 0.0f,
            float stormIntensity = 0.0f)
        {
            TemperatureC = temperatureC;
            SkyExposed = skyExposed;
            IsNight = isNight;
            PrecipitationIntensity = Clamp01(precipitationIntensity);
            StormIntensity = Clamp01(stormIntensity);
        }

        public float TemperatureC { get; }
        public bool SkyExposed { get; }
        public bool IsNight { get; }
        public float PrecipitationIntensity { get; }
        public float StormIntensity { get; }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0.0f)
                return 0.0f;
            if (value >= 1.0f)
                return 1.0f;
            return value;
        }
    }
}

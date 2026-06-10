using System;

namespace Blockiverse.Survival
{
    // Player survival vitals: hunger, thirst, and stamina (0..max, max 100 per the survival
    // menus HUD). Depletion/regen rates are tunable balance constants (the rulesets fix the
    // 0..100 range and persistence fields but not the rates). Hunger/thirst depletion is driven
    // by world ticks; reaching zero applies periodic starvation/dehydration damage that callers
    // route into PlayerVitals. Stamina regenerates over time and is spent by exertion.
    public sealed class SurvivalVitals
    {
        public const int DefaultMax = 100;

        // Ticks of game time to lose one point (20 ticks/second). Defaults: hunger fully
        // depletes over ~1 game day (24000 ticks), thirst a little faster.
        public const int HungerTicksPerPoint = 240;
        public const int ThirstTicksPerPoint = 180;
        // Stamina recovers one point every second of rest.
        public const int StaminaRegenTicksPerPoint = 20;
        // While starving or dehydrated, deal damage on this cadence.
        public const int StarvationDamageIntervalTicks = 1200;
        public const int StarvationDamagePerInterval = 1;

        int hungerAccumulator;
        int thirstAccumulator;
        int staminaAccumulator;
        int starvationAccumulator;

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

        // Advances vitals by the given number of ticks. Returns the health damage to apply this
        // step from starvation/dehydration (0 when both are above zero).
        public int Tick(int ticks)
        {
            if (ticks <= 0)
                return 0;

            Hunger = DepleteWithAccumulator(Hunger, ref hungerAccumulator, ticks, HungerTicksPerPoint);
            Thirst = DepleteWithAccumulator(Thirst, ref thirstAccumulator, ticks, ThirstTicksPerPoint);
            Stamina = RegenWithAccumulator(Stamina, ref staminaAccumulator, ticks, StaminaRegenTicksPerPoint, Max);

            if (!IsStarving && !IsDehydrated)
            {
                starvationAccumulator = 0;
                return 0;
            }

            // Each empty vital contributes a damage source on the starvation cadence.
            int sources = (IsStarving ? 1 : 0) + (IsDehydrated ? 1 : 0);
            starvationAccumulator += ticks;
            int damage = 0;
            while (starvationAccumulator >= StarvationDamageIntervalTicks)
            {
                starvationAccumulator -= StarvationDamageIntervalTicks;
                damage += StarvationDamagePerInterval * sources;
            }
            return damage;
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
            hungerAccumulator = thirstAccumulator = staminaAccumulator = starvationAccumulator = 0;
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
}

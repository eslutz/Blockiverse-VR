using System;

namespace Blockiverse.Survival
{
    public readonly struct SurvivalDifficultyProfile : IEquatable<SurvivalDifficultyProfile>
    {
        public const string EasyId = "easy";
        public const string NormalId = "normal";
        public const string HardId = "hard";

        public SurvivalDifficultyProfile(
            string id,
            int hungerTicksPerPoint,
            int thirstTicksPerPoint,
            int staminaRegenTicksPerPoint,
            int starvationDamageIntervalTicks,
            int starvationDamagePerInterval,
            int hazardDamagePercent,
            int environmentExposureDamageIntervalTicks,
            int environmentExposureDamagePerInterval,
            int fallDamagePercent)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Difficulty ids must be non-empty.", nameof(id));
            if (hungerTicksPerPoint <= 0)
                throw new ArgumentOutOfRangeException(nameof(hungerTicksPerPoint));
            if (thirstTicksPerPoint <= 0)
                throw new ArgumentOutOfRangeException(nameof(thirstTicksPerPoint));
            if (staminaRegenTicksPerPoint <= 0)
                throw new ArgumentOutOfRangeException(nameof(staminaRegenTicksPerPoint));
            if (starvationDamageIntervalTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(starvationDamageIntervalTicks));
            if (starvationDamagePerInterval <= 0)
                throw new ArgumentOutOfRangeException(nameof(starvationDamagePerInterval));
            if (hazardDamagePercent <= 0)
                throw new ArgumentOutOfRangeException(nameof(hazardDamagePercent));
            if (environmentExposureDamageIntervalTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(environmentExposureDamageIntervalTicks));
            if (environmentExposureDamagePerInterval <= 0)
                throw new ArgumentOutOfRangeException(nameof(environmentExposureDamagePerInterval));
            if (fallDamagePercent <= 0)
                throw new ArgumentOutOfRangeException(nameof(fallDamagePercent));

            Id = id;
            HungerTicksPerPoint = hungerTicksPerPoint;
            ThirstTicksPerPoint = thirstTicksPerPoint;
            StaminaRegenTicksPerPoint = staminaRegenTicksPerPoint;
            StarvationDamageIntervalTicks = starvationDamageIntervalTicks;
            StarvationDamagePerInterval = starvationDamagePerInterval;
            HazardDamagePercent = hazardDamagePercent;
            EnvironmentExposureDamageIntervalTicks = environmentExposureDamageIntervalTicks;
            EnvironmentExposureDamagePerInterval = environmentExposureDamagePerInterval;
            FallDamagePercent = fallDamagePercent;
        }

        public string Id { get; }
        public int HungerTicksPerPoint { get; }
        public int ThirstTicksPerPoint { get; }
        public int StaminaRegenTicksPerPoint { get; }
        public int StarvationDamageIntervalTicks { get; }
        public int StarvationDamagePerInterval { get; }
        public int HazardDamagePercent { get; }
        public int EnvironmentExposureDamageIntervalTicks { get; }
        public int EnvironmentExposureDamagePerInterval { get; }
        public int FallDamagePercent { get; }
        public bool IsValid => HungerTicksPerPoint > 0 &&
            ThirstTicksPerPoint > 0 &&
            StaminaRegenTicksPerPoint > 0 &&
            StarvationDamageIntervalTicks > 0 &&
            StarvationDamagePerInterval > 0 &&
            HazardDamagePercent > 0 &&
            EnvironmentExposureDamageIntervalTicks > 0 &&
            EnvironmentExposureDamagePerInterval > 0 &&
            FallDamagePercent > 0;

        public static SurvivalDifficultyProfile Easy => new(
            EasyId,
            hungerTicksPerPoint: SurvivalVitals.HungerTicksPerPoint * 2,
            thirstTicksPerPoint: SurvivalVitals.ThirstTicksPerPoint * 2,
            staminaRegenTicksPerPoint: SurvivalVitals.StaminaRegenTicksPerPoint,
            starvationDamageIntervalTicks: SurvivalVitals.StarvationDamageIntervalTicks * 2,
            starvationDamagePerInterval: SurvivalVitals.StarvationDamagePerInterval,
            hazardDamagePercent: 50,
            environmentExposureDamageIntervalTicks: SurvivalVitals.EnvironmentExposureDamageIntervalTicks * 2,
            environmentExposureDamagePerInterval: SurvivalVitals.EnvironmentExposureDamagePerInterval,
            fallDamagePercent: 50);

        public static SurvivalDifficultyProfile Normal => new(
            NormalId,
            hungerTicksPerPoint: SurvivalVitals.HungerTicksPerPoint,
            thirstTicksPerPoint: SurvivalVitals.ThirstTicksPerPoint,
            staminaRegenTicksPerPoint: SurvivalVitals.StaminaRegenTicksPerPoint,
            starvationDamageIntervalTicks: SurvivalVitals.StarvationDamageIntervalTicks,
            starvationDamagePerInterval: SurvivalVitals.StarvationDamagePerInterval,
            hazardDamagePercent: 100,
            environmentExposureDamageIntervalTicks: SurvivalVitals.EnvironmentExposureDamageIntervalTicks,
            environmentExposureDamagePerInterval: SurvivalVitals.EnvironmentExposureDamagePerInterval,
            fallDamagePercent: 100);

        public static SurvivalDifficultyProfile Hard => new(
            HardId,
            hungerTicksPerPoint: Math.Max(1, SurvivalVitals.HungerTicksPerPoint / 2),
            thirstTicksPerPoint: Math.Max(1, SurvivalVitals.ThirstTicksPerPoint / 2),
            staminaRegenTicksPerPoint: SurvivalVitals.StaminaRegenTicksPerPoint,
            starvationDamageIntervalTicks: Math.Max(1, SurvivalVitals.StarvationDamageIntervalTicks / 2),
            starvationDamagePerInterval: SurvivalVitals.StarvationDamagePerInterval * 2,
            hazardDamagePercent: 150,
            environmentExposureDamageIntervalTicks: Math.Max(1, SurvivalVitals.EnvironmentExposureDamageIntervalTicks / 2),
            environmentExposureDamagePerInterval: SurvivalVitals.EnvironmentExposureDamagePerInterval * 2,
            fallDamagePercent: 150);

        public static SurvivalDifficultyProfile FromId(string id)
        {
            if (string.Equals(id, EasyId, StringComparison.OrdinalIgnoreCase))
                return Easy;
            if (string.Equals(id, HardId, StringComparison.OrdinalIgnoreCase))
                return Hard;
            return Normal;
        }

        public int ScaleHazardDamage(int baseDamage)
        {
            if (baseDamage <= 0)
                throw new ArgumentOutOfRangeException(nameof(baseDamage));
            return Math.Max(1, (baseDamage * HazardDamagePercent + 99) / 100);
        }

        public int ScaleFallDamage(int baseDamage)
        {
            if (baseDamage <= 0)
                throw new ArgumentOutOfRangeException(nameof(baseDamage));
            return Math.Max(1, (baseDamage * FallDamagePercent + 99) / 100);
        }

        public bool Equals(SurvivalDifficultyProfile other) =>
            string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) =>
            obj is SurvivalDifficultyProfile other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(Id ?? string.Empty);
    }
}

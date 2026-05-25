using System;

namespace Blockiverse.Survival
{
    public static class RecoveryWrap
    {
        public const int HealAmount = 25;

        public static HealthChangeResult ApplyTo(PlayerVitals vitals)
        {
            if (vitals == null)
                throw new ArgumentNullException(nameof(vitals));

            return vitals.Heal(HealAmount);
        }
    }
}

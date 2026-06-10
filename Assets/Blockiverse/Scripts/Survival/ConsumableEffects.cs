namespace Blockiverse.Survival
{
    // Maps consumable items to their vitals effects (voxel_survival_ruleset §13: field_bandage
    // heals, clean_water_flask restores thirst and stamina). Lives in Survival next to the item
    // data so the effect table isn't buried in a runtime MonoBehaviour; amounts are tunable
    // balance constants (canon fixes the items, not the numbers).
    public static class ConsumableEffects
    {
        public const int CleanWaterThirstRestore = 40;
        public const int CleanWaterStaminaRestore = 20;

        // Applies the consumable's effect; returns false when the item has no known effect.
        public static bool TryApply(ItemId itemId, PlayerVitals vitals, SurvivalVitals survivalVitals)
        {
            if (itemId == ItemId.FieldBandage)
            {
                RecoveryWrap.ApplyTo(vitals);
                return true;
            }

            if (itemId == ItemId.CleanWaterFlask)
            {
                survivalVitals.Drink(CleanWaterThirstRestore);
                survivalVitals.RecoverStamina(CleanWaterStaminaRestore);
                return true;
            }

            return false;
        }
    }
}

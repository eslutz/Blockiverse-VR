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
        // Crop foods (§13): berries are a light snack with some juice; grain is the staple meal.
        public const int BerryClusterHungerRestore = 12;
        public const int BerryClusterThirstRestore = 4;
        public const int GrainBundleHungerRestore = 25;
        public const int BerryMashHungerRestore = 20;
        public const int BerryMashThirstRestore = 8;
        public const int FlatbreadHungerRestore = 40;
        public const int CookedMorselHungerRestore = 35;
        public const int TrailRationHungerRestore = 55;
        public const int TrailRationStaminaRestore = 10;

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

            if (itemId == ItemId.BerryCluster)
            {
                survivalVitals.Eat(BerryClusterHungerRestore);
                survivalVitals.Drink(BerryClusterThirstRestore);
                return true;
            }

            if (itemId == ItemId.GrainBundle)
            {
                survivalVitals.Eat(GrainBundleHungerRestore);
                return true;
            }

            if (itemId == ItemId.BerryMash)
            {
                survivalVitals.Eat(BerryMashHungerRestore);
                survivalVitals.Drink(BerryMashThirstRestore);
                return true;
            }

            if (itemId == ItemId.Flatbread)
            {
                survivalVitals.Eat(FlatbreadHungerRestore);
                return true;
            }

            if (itemId == ItemId.CookedMorsel)
            {
                survivalVitals.Eat(CookedMorselHungerRestore);
                return true;
            }

            if (itemId == ItemId.TrailRation)
            {
                survivalVitals.Eat(TrailRationHungerRestore);
                survivalVitals.RecoverStamina(TrailRationStaminaRestore);
                return true;
            }

            return false;
        }
    }
}

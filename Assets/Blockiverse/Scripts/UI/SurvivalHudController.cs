using Blockiverse.Survival;
using UnityEngine;

namespace Blockiverse.UI
{
    public sealed class SurvivalHudController : MonoBehaviour
    {
        [SerializeField] SurvivalInventoryPanel inventoryPanel;
        [SerializeField] SurvivalCraftingPanel craftingPanel;
        [SerializeField] SurvivalHealthPanel healthPanel;
        [SerializeField] int selectedHotbarSlotIndex;

        public Inventory Inventory { get; private set; }
        public CraftingRecipeBook RecipeBook { get; private set; }
        public PlayerVitals Vitals { get; private set; }

        public void Configure(
            SurvivalInventoryPanel targetInventoryPanel,
            SurvivalCraftingPanel targetCraftingPanel,
            SurvivalHealthPanel targetHealthPanel,
            int targetSelectedHotbarSlotIndex = 0)
        {
            inventoryPanel = targetInventoryPanel;
            craftingPanel = targetCraftingPanel;
            healthPanel = targetHealthPanel;
            selectedHotbarSlotIndex = targetSelectedHotbarSlotIndex;
        }

        void Awake()
        {
            BindValidationState();
        }

        void BindValidationState()
        {
            inventoryPanel ??= GetComponentInChildren<SurvivalInventoryPanel>(includeInactive: true);
            craftingPanel ??= GetComponentInChildren<SurvivalCraftingPanel>(includeInactive: true);
            healthPanel ??= GetComponentInChildren<SurvivalHealthPanel>(includeInactive: true);

            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            Inventory = new Inventory(itemRegistry);
            RecipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Vitals = new PlayerVitals();

            inventoryPanel?.Bind(Inventory, itemRegistry, selectedHotbarSlotIndex);
            craftingPanel?.Bind(RecipeBook, Inventory, itemRegistry, CraftingStation.None);
            healthPanel?.Bind(Vitals);
        }
    }
}

using Blockiverse.Gameplay;
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

            // Bind to the authoritative survival inventory when the runtime survival sync is present so
            // the HUD, harvesting, crafting, and container loot all share one inventory. Falls back to a
            // standalone inventory for isolated validation/tests.
            var survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            Inventory = survivalSync != null ? survivalSync.LocalInventory : new Inventory(itemRegistry);
            RecipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Vitals = new PlayerVitals();

            // Register this inventory as the container-loot destination so breaking a crate fills it.
            var worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            worldManager?.SetActivePlayerInventory(Inventory);

            inventoryPanel?.Bind(Inventory, itemRegistry, selectedHotbarSlotIndex);
            craftingPanel?.Bind(RecipeBook, Inventory, itemRegistry, CraftingStation.None);
            healthPanel?.Bind(Vitals);

            if (craftingPanel != null)
            {
                craftingPanel.CraftingChanged -= RefreshPanels;
                craftingPanel.CraftingChanged += RefreshPanels;
            }
        }

        void RefreshPanels()
        {
            inventoryPanel?.Refresh();
            craftingPanel?.Refresh();
            healthPanel?.Refresh();
        }
    }
}

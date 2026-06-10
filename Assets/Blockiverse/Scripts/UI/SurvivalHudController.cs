using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.UI
{
    public sealed class SurvivalHudController : MonoBehaviour
    {
        [SerializeField] SurvivalInventoryPanel inventoryPanel;
        [SerializeField] SurvivalCraftingPanel craftingPanel;
        [SerializeField] SurvivalHealthPanel healthPanel;
        [SerializeField] SurvivalCratePanel cratePanel;
        [SerializeField] int selectedHotbarSlotIndex;

        // Station proximity scan cadence: cheap cube scan around the player to unlock
        // station-gated recipes (voxel_survival_ruleset §8) without per-frame world reads.
        const float StationScanIntervalSeconds = 0.5f;

        // Vitals display refresh cadence: SurvivalVitals has no change events, so the health
        // panel is refreshed periodically while the vitals runtime is active.
        const float VitalsRefreshIntervalSeconds = 0.5f;

        public Inventory Inventory { get; private set; }
        public CraftingRecipeBook RecipeBook { get; private set; }
        public PlayerVitals Vitals { get; private set; }

        CreativeWorldManager worldManager;
        SurvivalVitalsRuntime vitalsRuntime;
        MultiplayerSurvivalSync survivalSync;
        ItemRegistry itemRegistry;
        float nextStationScanTime;
        float nextVitalsRefreshTime;
        CraftingStationSet lastScannedStations;

        public void Configure(
            SurvivalInventoryPanel targetInventoryPanel,
            SurvivalCraftingPanel targetCraftingPanel,
            SurvivalHealthPanel targetHealthPanel,
            SurvivalCratePanel targetCratePanel = null,
            int targetSelectedHotbarSlotIndex = 0)
        {
            inventoryPanel = targetInventoryPanel;
            craftingPanel = targetCraftingPanel;
            healthPanel = targetHealthPanel;
            cratePanel = targetCratePanel;
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
            cratePanel ??= GetComponentInChildren<SurvivalCratePanel>(includeInactive: true);

            itemRegistry = ItemRegistry.CreateDefault();

            // Bind to the authoritative survival inventory when the runtime survival sync is present so
            // the HUD, harvesting, crafting, and container loot all share one inventory. Falls back to a
            // standalone inventory for isolated validation/tests.
            survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            Inventory = survivalSync != null ? survivalSync.LocalInventory : new Inventory(itemRegistry);
            RecipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);

            // Bind to the runtime-owned vitals (ticked by SurvivalVitalsRuntime) when present so the
            // HUD shows live health/hunger/thirst/stamina. Falls back to a standalone instance for
            // isolated validation/tests.
            vitalsRuntime = FindFirstObjectByType<SurvivalVitalsRuntime>(FindObjectsInactive.Include);
            Vitals = vitalsRuntime != null ? vitalsRuntime.Vitals : new PlayerVitals();

            // Register this inventory as the container-loot destination so breaking a crate fills it.
            worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            worldManager?.SetActivePlayerInventory(Inventory);

            // Mirror the selected hotbar slot into the survival sync so VR break/place use the held item.
            if (survivalSync != null && inventoryPanel != null)
            {
                survivalSync.SelectedHotbarSlotIndex = selectedHotbarSlotIndex;
                inventoryPanel.SelectionChanged -= survivalSync.SetSelectedHotbarSlot;
                inventoryPanel.SelectionChanged += survivalSync.SetSelectedHotbarSlot;
            }

            inventoryPanel?.Bind(Inventory, itemRegistry, selectedHotbarSlotIndex);
            // Route crafting through the authoritative sync (when present) so client crafts are
            // host-validated, not applied to the local mirror.
            craftingPanel?.ConfigureSurvivalSync(survivalSync);
            craftingPanel?.Bind(RecipeBook, Inventory, itemRegistry, CraftingStation.None);
            healthPanel?.Bind(Vitals);
            if (vitalsRuntime != null)
                healthPanel?.BindSurvivalVitals(vitalsRuntime.SurvivalVitals);
            cratePanel?.Bind(survivalSync, itemRegistry);

            if (craftingPanel != null)
            {
                craftingPanel.CraftingChanged -= RefreshPanels;
                craftingPanel.CraftingChanged += RefreshPanels;
            }

            if (cratePanel != null)
            {
                cratePanel.CrateChanged -= RefreshPanels;
                cratePanel.CrateChanged += RefreshPanels;
            }

            // Repaint (and re-bind, if the instance was replaced) when the authoritative local
            // inventory or shared crate changes — host snapshots on clients, host-side command
            // results, and mode switches all arrive through these signals.
            if (survivalSync != null)
            {
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                survivalSync.LocalInventoryChanged += OnLocalInventoryChanged;
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;
                survivalSync.SharedCrateChanged += OnSharedCrateChanged;
            }
        }

        void OnDestroy()
        {
            if (survivalSync != null)
            {
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;
            }
        }

        void OnLocalInventoryChanged()
        {
            if (survivalSync != null && !ReferenceEquals(Inventory, survivalSync.LocalInventory))
            {
                // The sync replaced its inventory instance (explicit Configure): rebind every
                // consumer that captured the old reference.
                Inventory = survivalSync.LocalInventory;
                worldManager?.SetActivePlayerInventory(Inventory);
                inventoryPanel?.Bind(Inventory, itemRegistry, inventoryPanel.SelectedHotbarSlotIndex);
                craftingPanel?.Bind(RecipeBook, Inventory, itemRegistry, CraftingStation.None);
            }

            inventoryPanel?.Refresh();
            craftingPanel?.Refresh();
        }

        void OnSharedCrateChanged()
        {
            cratePanel?.Refresh();
        }

        void Update()
        {
            ScanNearbyStations();
            RefreshVitalsDisplay();
        }

        // Keeps the hunger/thirst/stamina readout current (those vitals tick without events).
        void RefreshVitalsDisplay()
        {
            if (vitalsRuntime == null || healthPanel == null || Time.time < nextVitalsRefreshTime)
                return;

            nextVitalsRefreshTime = Time.time + VitalsRefreshIntervalSeconds;
            healthPanel.Refresh();
        }

        // Periodically scans the blocks around the player and feeds the stations in reach to the
        // crafting panel, so station-gated recipes (kiln, forge, mend bench, …) become craftable
        // when the player stands at the placed station.
        void ScanNearbyStations()
        {
            if (craftingPanel == null || worldManager == null || worldManager.World == null)
                return;

            if (Time.time < nextStationScanTime)
                return;

            nextStationScanTime = Time.time + StationScanIntervalSeconds;

            Transform origin = Camera.main != null ? Camera.main.transform : transform;
            BlockPosition center = CreativeInteractionController.ToBlockPosition(origin.position);

            CraftingStationSet stations = StationProximity.ScanNearby(worldManager.World, center);
            if (stations.Equals(lastScannedStations))
                return;

            lastScannedStations = stations;
            craftingPanel.SetAvailableStations(stations);
        }

        void RefreshPanels()
        {
            inventoryPanel?.Refresh();
            craftingPanel?.Refresh();
            healthPanel?.Refresh();
            cratePanel?.Refresh();
        }
    }
}

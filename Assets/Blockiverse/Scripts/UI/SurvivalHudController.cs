using System;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.VR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class SurvivalHudController : MonoBehaviour
    {
        [SerializeField] SurvivalInventoryPanel inventoryPanel;
        [SerializeField] SurvivalCraftingPanel craftingPanel;
        [SerializeField] SurvivalHealthPanel healthPanel;
        [SerializeField] SurvivalCratePanel cratePanel;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Slider miningProgressSlider;
        [SerializeField] int selectedHotbarSlotIndex;
        [SerializeField] float statusMessageSeconds = 2.5f;

        // Station proximity scan cadence: cheap cube scan around the player to unlock
        // station-gated recipes (voxel_survival_ruleset §8) without per-frame world reads.
        const float StationScanIntervalSeconds = 0.5f;

        // Vitals display refresh cadence: SurvivalVitals has no change events, so the health
        // panel is refreshed periodically while the vitals runtime is active.
        const float VitalsRefreshIntervalSeconds = 0.5f;

        public Inventory Inventory { get; private set; }
        public CraftingRecipeBook RecipeBook { get; private set; }
        public PlayerVitals Vitals { get; private set; }
        public SurvivalVitalsRuntime VitalsRuntime => vitalsRuntime;
        public SurvivalInventoryPanel InventoryPanel => inventoryPanel;
        public SurvivalCraftingPanel CraftingPanel => craftingPanel;
        public SurvivalCratePanel CratePanel => cratePanel;
        public string CurrentStatusText => statusLabel != null ? statusLabel.text : string.Empty;

        CreativeWorldManager worldManager;
        SurvivalVitalsRuntime vitalsRuntime;
        MultiplayerSurvivalSync survivalSync;
        BlockiverseCreativeInputBridge inputBridge;
        ItemRegistry itemRegistry;
        float nextStationScanTime;
        float nextVitalsRefreshTime;
        float statusVisibleUntil;
        bool showingMiningProgress;
        CraftingStationSet lastScannedStations;
        // The exact SelectionChanged subscription, stored so a later bind (which may discover a
        // different sync instance) detaches the previous handler instead of leaking it.
        SurvivalInventoryPanel selectionChangedSource;
        Action<int> selectionChangedHandler;

        public void Configure(
            SurvivalInventoryPanel targetInventoryPanel,
            SurvivalCraftingPanel targetCraftingPanel,
            SurvivalHealthPanel targetHealthPanel,
            SurvivalCratePanel targetCratePanel = null,
            int targetSelectedHotbarSlotIndex = 0,
            TMP_Text targetStatusLabel = null,
            Slider targetMiningProgressSlider = null)
        {
            inventoryPanel = targetInventoryPanel;
            craftingPanel = targetCraftingPanel;
            healthPanel = targetHealthPanel;
            cratePanel = targetCratePanel;
            selectedHotbarSlotIndex = targetSelectedHotbarSlotIndex;
            statusLabel = targetStatusLabel;
            miningProgressSlider = targetMiningProgressSlider;
        }

        void Awake()
        {
            BindValidationState();
        }

        void BindValidationState()
        {
            if (inventoryPanel == null)
                inventoryPanel = GetComponentInChildren<SurvivalInventoryPanel>(includeInactive: true);
            if (craftingPanel == null)
                craftingPanel = GetComponentInChildren<SurvivalCraftingPanel>(includeInactive: true);
            if (healthPanel == null)
                healthPanel = GetComponentInChildren<SurvivalHealthPanel>(includeInactive: true);
            if (cratePanel == null)
                cratePanel = GetComponentInChildren<SurvivalCratePanel>(includeInactive: true);
            Transform panel = transform.Find("Panel");
            if (panel != null)
            {
                if (statusLabel == null)
                {
                    Transform statusTransform = panel.Find("Status");
                    if (statusTransform != null)
                        statusLabel = statusTransform.GetComponent<TMP_Text>();
                }

                if (miningProgressSlider == null)
                {
                    Transform progressTransform = panel.Find("Mining Progress");
                    if (progressTransform != null)
                        miningProgressSlider = progressTransform.GetComponent<Slider>();
                }
            }

            itemRegistry = ItemRegistry.Default;
            UnsubscribeTransientFeedback();

            // Bind to the authoritative survival inventory when the runtime survival sync is present so
            // the HUD, harvesting, crafting, and container loot all share one inventory. Falls back to a
            // standalone inventory for isolated validation/tests.
            survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            Inventory = survivalSync != null ? survivalSync.LocalInventory : new Inventory(itemRegistry);
            RecipeBook = CraftingRecipeBook.Default;

            // Bind to the runtime-owned vitals (ticked by SurvivalVitalsRuntime) when present so the
            // HUD shows live health/hunger/thirst/stamina. Falls back to a standalone instance for
            // isolated validation/tests.
            vitalsRuntime = FindFirstObjectByType<SurvivalVitalsRuntime>(FindObjectsInactive.Include);
            Vitals = vitalsRuntime != null ? vitalsRuntime.Vitals : new PlayerVitals();
            inputBridge = FindFirstObjectByType<BlockiverseCreativeInputBridge>(FindObjectsInactive.Include);

            // Register this inventory as the container-loot destination so breaking a crate fills it.
            worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            if (worldManager != null)
                worldManager.SetActivePlayerInventory(Inventory);

            // Mirror the selected hotbar slot into the survival sync so VR break/place use the held item.
            UnsubscribeSelectionChanged();
            if (survivalSync != null && inventoryPanel != null)
            {
                survivalSync.SelectedHotbarSlotIndex = selectedHotbarSlotIndex;
                selectionChangedSource = inventoryPanel;
                selectionChangedHandler = survivalSync.SetSelectedHotbarSlot;
                selectionChangedSource.SelectionChanged += selectionChangedHandler;
            }

            if (inventoryPanel != null)
                inventoryPanel.Bind(Inventory, itemRegistry, selectedHotbarSlotIndex);
            // Route crafting through the authoritative sync (when present) so client crafts are
            // host-validated, not applied to the local mirror.
            if (craftingPanel != null)
            {
                craftingPanel.ConfigureSurvivalSync(survivalSync);
                craftingPanel.Bind(RecipeBook, Inventory, itemRegistry, CraftingStation.None);
            }

            if (healthPanel != null)
            {
                healthPanel.Bind(Vitals);
                if (vitalsRuntime != null)
                    healthPanel.BindSurvivalVitals(vitalsRuntime.SurvivalVitals);
            }

            if (cratePanel != null)
                cratePanel.Bind(survivalSync, itemRegistry);

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
                survivalSync.CommandFeedback -= OnCommandFeedback;
                survivalSync.CommandFeedback += OnCommandFeedback;
            }

            if (inputBridge != null)
            {
                inputBridge.MiningProgressChanged -= OnMiningProgressChanged;
                inputBridge.MiningProgressChanged += OnMiningProgressChanged;
                inputBridge.MiningProgressCleared -= OnMiningProgressCleared;
                inputBridge.MiningProgressCleared += OnMiningProgressCleared;
            }

            SetMiningProgressVisible(false);
            if (statusLabel != null && string.IsNullOrEmpty(statusLabel.text))
                statusLabel.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            UnsubscribeSelectionChanged();
            UnsubscribeTransientFeedback();

            if (craftingPanel != null)
                craftingPanel.CraftingChanged -= RefreshPanels;

            if (cratePanel != null)
                cratePanel.CrateChanged -= RefreshPanels;

            if (survivalSync != null)
            {
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;
            }
        }

        // Removes the stored SelectionChanged subscription. `-= survivalSync.SetSelectedHotbarSlot`
        // against a freshly discovered sync would not remove a previous sync's handler.
        void UnsubscribeSelectionChanged()
        {
            if (selectionChangedSource != null && selectionChangedHandler != null)
                selectionChangedSource.SelectionChanged -= selectionChangedHandler;

            selectionChangedSource = null;
            selectionChangedHandler = null;
        }

        void OnLocalInventoryChanged()
        {
            if (survivalSync != null && !ReferenceEquals(Inventory, survivalSync.LocalInventory))
            {
                // The sync replaced its inventory instance (explicit Configure): rebind every
                // consumer that captured the old reference.
                Inventory = survivalSync.LocalInventory;
                if (worldManager != null)
                    worldManager.SetActivePlayerInventory(Inventory);
                if (inventoryPanel != null)
                    inventoryPanel.Bind(Inventory, itemRegistry, inventoryPanel.SelectedHotbarSlotIndex);
                if (craftingPanel != null)
                    craftingPanel.Bind(RecipeBook, Inventory, itemRegistry, CraftingStation.None);
                // Bind resets the panel's station set to None, and ScanNearbyStations skips its
                // push while the scan result still equals lastScannedStations — re-apply the
                // cached set so station-gated recipes stay unlocked across the rebind.
                if (craftingPanel != null)
                    craftingPanel.SetAvailableStations(lastScannedStations);
            }

            if (inventoryPanel != null)
                inventoryPanel.Refresh();
            if (craftingPanel != null)
                craftingPanel.Refresh();
        }

        void OnSharedCrateChanged()
        {
            if (cratePanel != null)
                cratePanel.Refresh();
        }

        void Update()
        {
            ScanNearbyStations();
            RefreshVitalsDisplay();
            ClearExpiredStatus();
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
            if (inventoryPanel != null)
                inventoryPanel.Refresh();
            if (craftingPanel != null)
                craftingPanel.Refresh();
            if (healthPanel != null)
                healthPanel.Refresh();
            if (cratePanel != null)
                cratePanel.Refresh();
        }

        void OnMiningProgressChanged(BlockPosition position, float elapsedSeconds, float requiredSeconds)
        {
            float progress = requiredSeconds > 0f
                ? Mathf.Clamp01(elapsedSeconds / requiredSeconds)
                : 1.0f;
            int percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);

            showingMiningProgress = true;
            statusVisibleUntil = 0f;
            SetStatusText(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.SurvivalHudMiningProgress,
                percent));

            if (miningProgressSlider != null)
            {
                miningProgressSlider.minValue = 0f;
                miningProgressSlider.maxValue = 1f;
                miningProgressSlider.value = progress;
                SetMiningProgressVisible(true);
            }
        }

        void OnMiningProgressCleared()
        {
            showingMiningProgress = false;
            SetMiningProgressVisible(false);

            if (statusVisibleUntil <= 0f)
                SetStatusText(string.Empty);
        }

        void OnCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            if (result.CommandKind != SurvivalCommandKind.HarvestResource ||
                result.Accepted ||
                result.PendingHostValidation ||
                result.IsDuplicate)
            {
                return;
            }

            string message = result.HarvestFailureReason switch
            {
                BlockHarvestFailureReason.InventoryFull => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.SurvivalHudInventoryFull),
                BlockHarvestFailureReason.InsufficientTool => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.SurvivalHudToolTooWeak),
                _ when result.FailureReason == SurvivalCommandFailureReason.InventoryFull =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.SurvivalHudInventoryFull),
                _ => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.SurvivalHudHarvestRejected)
            };

            ShowTimedStatus(message);
        }

        void ShowTimedStatus(string message)
        {
            showingMiningProgress = false;
            SetMiningProgressVisible(false);
            SetStatusText(message);
            statusVisibleUntil = Time.unscaledTime + Mathf.Max(0.1f, statusMessageSeconds);
        }

        void ClearExpiredStatus()
        {
            if (showingMiningProgress || statusVisibleUntil <= 0f || Time.unscaledTime < statusVisibleUntil)
                return;

            statusVisibleUntil = 0f;
            SetStatusText(string.Empty);
        }

        void SetStatusText(string message)
        {
            if (statusLabel == null)
                return;

            statusLabel.text = message ?? string.Empty;
            statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(statusLabel.text));
        }

        void SetMiningProgressVisible(bool visible)
        {
            if (miningProgressSlider != null)
                miningProgressSlider.gameObject.SetActive(visible);
        }

        void UnsubscribeTransientFeedback()
        {
            if (survivalSync != null)
                survivalSync.CommandFeedback -= OnCommandFeedback;

            if (inputBridge != null)
            {
                inputBridge.MiningProgressChanged -= OnMiningProgressChanged;
                inputBridge.MiningProgressCleared -= OnMiningProgressCleared;
            }
        }
    }
}

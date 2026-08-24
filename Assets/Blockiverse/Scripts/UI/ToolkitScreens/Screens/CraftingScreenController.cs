using System;
using System.Collections.Generic;
using System.Text;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of SurvivalCraftingPanel (matrix row 16). The behaviour contract is the
    // uGUI panel's, verbatim:
    //  - GetSortedRecipes keeps registration order (early-game recipes stay on page one) and
    //    excludes timed kiln/forge recipes, which run on the fueled station model.
    //  - EffectiveStationFor claims the recipe's own station only when it is in reach and
    //    otherwise None — never a substitute station (matrix §4 item 3).
    //  - Host-authoritative submits treat accepted-or-pending as UI success (success cue,
    //    pending status) but raise CraftingChanged only on Accepted (item 2).
    //  - Row text rides a render-diff cache keyed on recipe identity, station set and an
    //    inventory fingerprint; the cache is invalidated on SelectedLocaleChanged because it
    //    keys on data values, not locale.
    // Station availability is fed by the same 0.5 s proximity scan SurvivalHudController runs
    // (same StationProximity.ScanNearby call, same origin derivation), rather than by reading
    // the hidden uGUI panel's state — the uGUI panel exposes no station-set getter, and
    // duplicating the scan's inputs is the only coupling that cannot drift.
    [UiToolkitScreen(MenuActions.CraftingScreen, "Assets/Blockiverse/UI/Documents/CraftingScreen.uxml",
        1000, 904, UiToolkitPlacementProfile.Menu)]
    public sealed class CraftingScreenController : UiToolkitScreenController
    {
        public const int RecipeRowCount = 5;

        // Requested table entry (english "Repair Held Tool"); UiText.Get falls back to the key
        // string until the entry lands centrally.
        const string RepairLabelKey = "ui.generated.crafting.repair";

        const string ReadyKey = "ui.status.crafting.ready";
        const string RecipeUnavailableKey = "ui.status.crafting.recipe_unavailable";
        const string RecipeKey = "ui.status.crafting.recipe";
        const string NeedsStationKey = "ui.status.crafting.needs_station";
        const string CraftedKey = "ui.status.crafting.crafted";
        const string CannotCraftKey = "ui.status.crafting.cannot_craft";
        const string PendingKey = "ui.status.crafting.pending";
        const string ToolRepairedKey = "ui.status.crafting.tool_repaired";
        const string RepairingKey = "ui.status.crafting.repairing";
        const string CannotRepairKey = "ui.status.crafting.cannot_repair";
        const string PageKey = "ui.common.page";
        const string ListSeparatorKey = "ui.common.list_separator";
        const string StackKey = "ui.common.stack";

        // The ui.value.* namespaces resolve almost entirely through humanized fallbacks by
        // design (same shape as BlockiverseLocalization.DisplayName).
        const string CraftingStationValueKeyPrefix = "ui.value.crafting_station.";
        const string CraftingFailureValueKeyPrefix = "ui.value.crafting_failure.";
        const string SurvivalCommandFailureValueKeyPrefix = "ui.value.survival_command_failure.";
        const string RepairFailureValueKeyPrefix = "ui.value.repair_failure.";

        const string StatusConfirmedClassName = "hs-status--confirmed";
        const string StatusRefusedClassName = "hs-status--refused";

        // Station proximity scan cadence, mirrored from SurvivalHudController (§8: cheap cube
        // scan to unlock station-gated recipes without per-frame world reads).
        const float StationScanIntervalSeconds = 0.5f;

        enum RecipeAvailability
        {
            Available,
            MissingIngredients,
            WrongStation
        }

        enum StatusTone
        {
            Neutral,
            Confirmed,
            Refused
        }

        struct RecipeRowRenderState
        {
            public bool IsValid;
            public bool HasRecipe;
            public int RecipeIndex;
            public CraftingRecipe Recipe;
            public CraftingStationSet AvailableStations;
            public int InventoryFingerprint;
            public string Text;
        }

        readonly Button[] recipeButtons = new Button[RecipeRowCount];
        readonly VisualElement[] recipeIcons = new VisualElement[RecipeRowCount];
        readonly Label[] recipeLabels = new Label[RecipeRowCount];
        Button repairButton;
        Button previousPageButton;
        Button nextPageButton;
        Label pageLabel;
        Label statusLabel;
        Button closeButton;

        EventCallback<ClickEvent>[] recipeClickCallbacks;
        EventCallback<ClickEvent> repairClickCallback;
        EventCallback<ClickEvent> previousPageClickCallback;
        EventCallback<ClickEvent> nextPageClickCallback;
        EventCallback<ClickEvent> closeClickCallback;

        CraftingRecipeBook recipeBook;
        Inventory inventory;
        ItemRegistry itemRegistry;
        CraftingStationSet availableStations;
        MultiplayerSurvivalSync survivalSync;
        CreativeWorldManager worldManager;
        BlockiverseItemIconLibrary iconLibrary;

        // GetSortedRecipes cache: CraftingRecipeBook is append-only (Register), so the bound
        // book instance plus its recipe count identifies the content that produced the list.
        readonly List<CraftingRecipe> sortedRecipesCache = new();
        CraftingRecipeBook sortedRecipesSource;
        int sortedRecipesSourceCount = -1;
        int recipePage;
        readonly RecipeRowRenderState[] recipeRowRenderCache = new RecipeRowRenderState[RecipeRowCount];

        float nextStationScanTime;
        CraftingStationSet lastScannedStations;

        string statusText = string.Empty;
        StatusTone statusTone = StatusTone.Neutral;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;
        BlockiverseVfxCuePlayer vfxCuePlayer;

        public event Action CraftingChanged;
        public int RecipePage => recipePage;

        public override string ScreenId => MenuActions.CraftingScreen;

        // Routes crafting through the host-authoritative survival sync when present, so a
        // remote client cannot craft against its local inventory mirror without host
        // validation. Falls back to local CraftingService for isolated use (tests, no
        // networking) — same contract as the uGUI panel.
        public void ConfigureSurvivalSync(MultiplayerSurvivalSync sync) => survivalSync = sync;

        public void Bind(
            CraftingRecipeBook targetRecipeBook,
            Inventory targetInventory,
            ItemRegistry registry = null,
            CraftingStation station = CraftingStation.None)
        {
            recipeBook = targetRecipeBook ?? throw new ArgumentNullException(nameof(targetRecipeBook));
            inventory = targetInventory ?? throw new ArgumentNullException(nameof(targetInventory));
            itemRegistry = registry ?? ItemRegistry.Default;
            availableStations = CraftingStationSet.Of(station);
            InvalidateRecipeRowCache();
            SetStatus(UiText.Get(ReadyKey), StatusTone.Neutral);
            Refresh();
        }

        // Updates the set of stations currently in reach so station-gated recipes become
        // craftable when the player stands at the station (§8).
        public void SetAvailableStations(CraftingStationSet stations)
        {
            availableStations = stations;
            InvalidateRecipeRowCache();
            Refresh();
        }

        protected override void OnAwake()
        {
            if (!Application.isPlaying)
                return;

            // Runtime self-bind, mirroring what SurvivalHudController.BindValidationState does
            // for the uGUI crafting panel: the authoritative survival inventory when the sync
            // is present, a standalone inventory otherwise.
            itemRegistry = ItemRegistry.Default;
            survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            iconLibrary = FindFirstObjectByType<BlockiverseItemIconLibrary>(FindObjectsInactive.Include);

            Inventory boundInventory = survivalSync != null ? survivalSync.LocalInventory : new Inventory(itemRegistry);
            Bind(CraftingRecipeBook.Default, boundInventory, itemRegistry, CraftingStation.None);

            if (survivalSync != null)
            {
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                survivalSync.LocalInventoryChanged += OnLocalInventoryChanged;
            }
        }

        void OnDestroy()
        {
            if (survivalSync != null)
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            for (int i = 0; i < RecipeRowCount; i++)
            {
                int rowNumber = i + 1;
                recipeButtons[i] = Require<Button>(root, "bv-recipe-" + rowNumber, ref allFound);
                recipeIcons[i] = Require<VisualElement>(root, "bv-recipe-icon-" + rowNumber, ref allFound);
                recipeLabels[i] = Require<Label>(root, "bv-recipe-label-" + rowNumber, ref allFound);
            }

            repairButton = Require<Button>(root, "bv-repair", ref allFound);
            previousPageButton = Require<Button>(root, "bv-page-previous", ref allFound);
            nextPageButton = Require<Button>(root, "bv-page-next", ref allFound);
            pageLabel = Require<Label>(root, "bv-page-label", ref allFound);
            statusLabel = Require<Label>(root, "bv-crafting-status", ref allFound);
            closeButton = Require<Button>(root, "bv-crafting-close", ref allFound);

            // Runtime label (no table entry yet; the requested key resolves once it lands).
            if (repairButton != null)
                repairButton.text = UiText.Get(RepairLabelKey);

            // Brand-new elements know nothing: re-render the pending state into them.
            InvalidateRecipeRowCache();
            ApplyStatusToLabel();
            Refresh();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            recipeClickCallbacks = new EventCallback<ClickEvent>[RecipeRowCount];
            for (int i = 0; i < RecipeRowCount; i++)
            {
                int visibleIndex = i;
                recipeClickCallbacks[i] = _ => TryCraftVisibleIndex(visibleIndex);
                recipeButtons[i]?.RegisterCallback(recipeClickCallbacks[i]);
            }

            repairClickCallback = _ => TryRepairHeldTool();
            // These three had no cue of their own and were audible only through the host's
            // route-change sound, which Eric's 2026-08-23 ruling removed — deleting it silenced
            // them by accident. Craft/repair rows keep outcome cues instead of a click.
            previousPageClickCallback = _ => { PlayFeedback(BlockiverseAudioCue.UiSelect); ShowPreviousRecipePage(); };
            nextPageClickCallback = _ => { PlayFeedback(BlockiverseAudioCue.UiSelect); ShowNextRecipePage(); };
            closeClickCallback = _ => { PlayFeedback(BlockiverseAudioCue.UiSelect); CloseScreen(); };
            repairButton?.RegisterCallback(repairClickCallback);
            previousPageButton?.RegisterCallback(previousPageClickCallback);
            nextPageButton?.RegisterCallback(nextPageClickCallback);
            closeButton?.RegisterCallback(closeClickCallback);

            // The recipe-row render cache keys on recipe/station/inventory state, not locale,
            // so a runtime language switch must invalidate it explicitly (matrix §4).
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            for (int i = 0; i < RecipeRowCount; i++)
            {
                if (recipeClickCallbacks != null && recipeClickCallbacks[i] != null)
                    recipeButtons[i]?.UnregisterCallback(recipeClickCallbacks[i]);
            }

            recipeClickCallbacks = null;

            if (repairClickCallback != null)
                repairButton?.UnregisterCallback(repairClickCallback);
            if (previousPageClickCallback != null)
                previousPageButton?.UnregisterCallback(previousPageClickCallback);
            if (nextPageClickCallback != null)
                nextPageButton?.UnregisterCallback(nextPageClickCallback);
            if (closeClickCallback != null)
                closeButton?.UnregisterCallback(closeClickCallback);
            repairClickCallback = null;
            previousPageClickCallback = null;
            nextPageClickCallback = null;
            closeClickCallback = null;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            for (int i = 0; i < RecipeRowCount; i++)
            {
                recipeButtons[i] = null;
                recipeIcons[i] = null;
                recipeLabels[i] = null;
            }

            repairButton = null;
            previousPageButton = null;
            nextPageButton = null;
            pageLabel = null;
            statusLabel = null;
            closeButton = null;
        }

        void Update()
        {
            ScanNearbyStations();
        }

        // Same scan as SurvivalHudController.ScanNearbyStations: cube scan around the camera
        // every 0.5 s, pushed down only when the set actually changed.
        void ScanNearbyStations()
        {
            if (worldManager == null || worldManager.World == null)
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
            SetAvailableStations(stations);
        }

        void OnLocalInventoryChanged()
        {
            if (survivalSync != null && !ReferenceEquals(inventory, survivalSync.LocalInventory))
            {
                // The sync replaced its inventory instance: rebind, then re-apply the cached
                // station set — Bind resets stations to None and the proximity scan skips its
                // push while the scan result still equals lastScannedStations.
                Bind(recipeBook ?? CraftingRecipeBook.Default, survivalSync.LocalInventory, itemRegistry, CraftingStation.None);
                SetAvailableStations(lastScannedStations);
            }

            Refresh();
        }

        void OnSelectedLocaleChanged(Locale locale)
        {
            if (repairButton != null)
                repairButton.text = UiText.Get(RepairLabelKey);

            InvalidateRecipeRowCache();
            Refresh();
        }

        void CloseScreen()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller != null)
                controller.CloseCraftingScreen();
        }

        // The station actually claimed for a craft: the recipe's own requirement when it is in
        // reach, otherwise None (which the validation gate rejects with MissingStation).
        CraftingStation EffectiveStationFor(CraftingRecipe recipe) =>
            availableStations.Contains(recipe.RequiredStation) ? recipe.RequiredStation : CraftingStation.None;

        public CraftingResult TryCraftAtIndex(int index)
        {
            EnsureBound();

            List<CraftingRecipe> recipes = GetSortedRecipes();

            if (index < 0 || index >= recipes.Count)
            {
                SetStatus(UiText.Get(RecipeUnavailableKey), StatusTone.Refused);
                PlayFeedback(BlockiverseAudioCue.CraftFail);
                return CraftingResult.Failure(CraftingFailureReason.MissingIngredient);
            }

            return TryCraft(recipes[index]);
        }

        public CraftingResult TryCraftVisibleIndex(int visibleIndex)
        {
            return TryCraftAtIndex(recipePage * PageSize + visibleIndex);
        }

        public CraftingResult TryCraftByOutput(ItemId outputItemId)
        {
            EnsureBound();

            if (!recipeBook.TryGetByOutput(outputItemId, out CraftingRecipe recipe))
            {
                SetStatus(UiText.Get(RecipeUnavailableKey), StatusTone.Refused);
                PlayFeedback(BlockiverseAudioCue.CraftFail);
                return CraftingResult.Failure(CraftingFailureReason.MissingIngredient, outputItemId);
            }

            return TryCraft(recipe);
        }

        CraftingResult TryCraft(CraftingRecipe recipe)
        {
            if (survivalSync != null)
                return TryCraftAuthoritative(recipe);

            CraftingResult result = CraftingService.TryCraft(inventory, recipe, EffectiveStationFor(recipe));
            SetStatus(
                result.Succeeded
                    ? UiText.Format(CraftedKey, FormatStack(recipe.Output))
                    : UiText.Format(
                        CannotCraftKey,
                        itemRegistry.Get(recipe.Output.ItemId).Name,
                        EnumDisplayName(CraftingFailureValueKeyPrefix, result.FailureReason.ToString())),
                result.Succeeded ? StatusTone.Confirmed : StatusTone.Refused);
            Refresh();
            PlayFeedback(result.Succeeded ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

            if (result.Succeeded)
                CraftingChanged?.Invoke();

            return result;
        }

        // Host-authoritative craft: the host validates and mutates the inventory, then
        // broadcasts the result/snapshot. On the host/offline peer this resolves immediately;
        // on a remote client it is pending until the host responds (the inventory mirror
        // updates from the snapshot).
        CraftingResult TryCraftAuthoritative(CraftingRecipe recipe)
        {
            SurvivalCommandResult command = survivalSync.TrySubmitCraft(recipe.Output.ItemId, EffectiveStationFor(recipe), out bool sentToHost);
            bool acceptedOrPending = command.Accepted || command.PendingHostValidation || sentToHost;

            if (command.Accepted)
                SetStatus(UiText.Format(CraftedKey, FormatStack(recipe.Output)), StatusTone.Confirmed);
            else if (sentToHost)
                SetStatus(UiText.Format(PendingKey, itemRegistry.Get(recipe.Output.ItemId).Name), StatusTone.Neutral);
            else
                SetStatus(
                    UiText.Format(
                        CannotCraftKey,
                        itemRegistry.Get(recipe.Output.ItemId).Name,
                        EnumDisplayName(CraftingFailureValueKeyPrefix, command.CraftingFailureReason.ToString())),
                    StatusTone.Refused);
            Refresh();
            PlayFeedback(acceptedOrPending ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

            if (command.Accepted)
                CraftingChanged?.Invoke();

            return acceptedOrPending
                ? CraftingResult.Success()
                : CraftingResult.Failure(command.CraftingFailureReason, recipe.Output.ItemId);
        }

        // Mend Bench repair of the held tool (§10.7). Routed through the host-authoritative
        // sync when present (the host re-validates bench proximity); falls back to local
        // MendBenchRepair for isolated use. toolSlotIndex -1 repairs the selected hotbar slot.
        public bool TryRepairHeldTool(int toolSlotIndex = -1)
        {
            EnsureBound();

            if (survivalSync != null)
            {
                SurvivalCommandResult command = survivalSync.TrySubmitRepair(out bool sentToHost, toolSlotIndex);
                bool acceptedOrPending = command.Accepted || command.PendingHostValidation || sentToHost;

                if (command.Accepted)
                    SetStatus(UiText.Get(ToolRepairedKey), StatusTone.Confirmed);
                else if (sentToHost)
                    SetStatus(UiText.Get(RepairingKey), StatusTone.Neutral);
                else
                    SetStatus(
                        UiText.Format(
                            CannotRepairKey,
                            EnumDisplayName(SurvivalCommandFailureValueKeyPrefix, command.FailureReason.ToString())),
                        StatusTone.Refused);
                Refresh();
                PlayFeedback(acceptedOrPending ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

                if (command.Accepted)
                    CraftingChanged?.Invoke();

                return acceptedOrPending;
            }

            CraftingStation station = availableStations.Contains(CraftingStation.MendBench)
                ? CraftingStation.MendBench
                : CraftingStation.None;
            RepairResult result = MendBenchRepair.TryRepair(itemRegistry, inventory, Math.Max(0, toolSlotIndex), station);

            SetStatus(
                result.Succeeded
                    ? UiText.Get(ToolRepairedKey)
                    : UiText.Format(
                        CannotRepairKey,
                        EnumDisplayName(RepairFailureValueKeyPrefix, result.FailureReason.ToString())),
                result.Succeeded ? StatusTone.Confirmed : StatusTone.Refused);
            Refresh();
            PlayFeedback(result.Succeeded ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

            if (result.Succeeded)
                CraftingChanged?.Invoke();

            return result.Succeeded;
        }

        public void Refresh()
        {
            List<CraftingRecipe> recipes = GetSortedRecipes();
            ClampRecipePage(recipes.Count);
            int offset = recipePage * PageSize;
            int inventoryFingerprint = ComputeInventoryFingerprint();
            for (int i = 0; i < RecipeRowCount; i++)
            {
                if (recipeLabels[i] == null)
                    continue;

                int recipeIndex = offset + i;
                bool hasRecipe = recipeIndex < recipes.Count;
                CraftingRecipe recipe = hasRecipe ? recipes[recipeIndex] : null;
                SetTextIfChanged(recipeLabels[i], GetRecipeRowText(i, recipeIndex, recipe, hasRecipe, inventoryFingerprint));
                SetRecipeIcon(i, recipe);
                recipeButtons[i]?.SetEnabled(hasRecipe);
            }

            RefreshPagingControls(recipes.Count);
        }

        public void ShowNextRecipePage()
        {
            int pageCount = RecipePageCount(GetSortedRecipes().Count);
            if (recipePage < pageCount - 1)
            {
                recipePage++;
                Refresh();
            }
        }

        public void ShowPreviousRecipePage()
        {
            if (recipePage > 0)
            {
                recipePage--;
                Refresh();
            }
        }

        List<CraftingRecipe> GetSortedRecipes()
        {
            int recipeCount = recipeBook != null ? recipeBook.All.Count : 0;
            if (ReferenceEquals(sortedRecipesSource, recipeBook) && sortedRecipesSourceCount == recipeCount)
                return sortedRecipesCache;

            sortedRecipesCache.Clear();
            sortedRecipesSource = recipeBook;
            sortedRecipesSourceCount = recipeCount;

            if (recipeBook == null)
                return sortedRecipesCache;

            // Registration order (basics → stations → smelting → tools → utility) keeps
            // early-game recipes at the top of the limited recipe slots, which alphabetical
            // order would bury. Timed (kiln/forge) recipes are excluded: they run on the
            // fueled station model via the station screen, and CraftingService rejects them
            // as instant crafts anyway.
            foreach (CraftingRecipe recipe in recipeBook.All)
            {
                if (recipe.TimeTicks <= 0)
                    sortedRecipesCache.Add(recipe);
            }

            return sortedRecipesCache;
        }

        int PageSize => RecipeRowCount;

        int RecipePageCount(int recipeCount)
        {
            int pageSize = PageSize;
            return Math.Max(1, (recipeCount + pageSize - 1) / pageSize);
        }

        void ClampRecipePage(int recipeCount)
        {
            recipePage = Mathf.Clamp(recipePage, 0, RecipePageCount(recipeCount) - 1);
        }

        void RefreshPagingControls(int recipeCount)
        {
            int pageCount = RecipePageCount(recipeCount);
            if (pageLabel != null)
                SetTextIfChanged(pageLabel, UiText.Format(PageKey, recipePage + 1, pageCount));

            previousPageButton?.SetEnabled(recipePage > 0);
            nextPageButton?.SetEnabled(recipePage < pageCount - 1);
        }

        // Output-item icon next to the recipe row (hidden, not collapsed, when no icon exists
        // for the output, so rows keep one silhouette).
        void SetRecipeIcon(int index, CraftingRecipe recipe)
        {
            VisualElement iconElement = recipeIcons[index];
            if (iconElement == null)
                return;

            Sprite icon = null;
            if (recipe != null && iconLibrary != null)
                iconLibrary.TryGetIcon(recipe.Output.ItemId, out icon);

            iconElement.style.backgroundImage = icon != null
                ? new StyleBackground(icon)
                : new StyleBackground(StyleKeyword.None);
            iconElement.style.visibility = icon != null ? Visibility.Visible : Visibility.Hidden;
        }

        string FormatRecipe(CraftingRecipe recipe)
        {
            string marker = AvailabilityMarker(AvailabilityFor(recipe));
            string text = UiText.Format(RecipeKey, FormatStack(recipe.Output), FormatIngredients(recipe));
            if (!availableStations.Contains(recipe.RequiredStation))
                text = UiText.Format(
                    NeedsStationKey,
                    FormatStack(recipe.Output),
                    FormatIngredients(recipe),
                    EnumDisplayName(CraftingStationValueKeyPrefix, recipe.RequiredStation.ToString()));
            return marker + " " + text;
        }

        RecipeAvailability AvailabilityFor(CraftingRecipe recipe)
        {
            if (!availableStations.Contains(recipe.RequiredStation))
                return RecipeAvailability.WrongStation;

            foreach (ItemStack ingredient in recipe.Ingredients)
                if (inventory == null || inventory.CountOf(ingredient.ItemId) < ingredient.Count)
                    return RecipeAvailability.MissingIngredients;

            return RecipeAvailability.Available;
        }

        static string AvailabilityMarker(RecipeAvailability availability) =>
            availability switch
            {
                RecipeAvailability.Available => "✓",
                RecipeAvailability.MissingIngredients => "✗",
                RecipeAvailability.WrongStation => "!",
                _ => "!"
            };

        string GetRecipeRowText(
            int rowIndex,
            int recipeIndex,
            CraftingRecipe recipe,
            bool hasRecipe,
            int inventoryFingerprint)
        {
            RecipeRowRenderState previous = recipeRowRenderCache[rowIndex];
            if (previous.IsValid &&
                previous.HasRecipe == hasRecipe &&
                previous.RecipeIndex == recipeIndex &&
                ReferenceEquals(previous.Recipe, recipe) &&
                previous.AvailableStations.Equals(availableStations) &&
                previous.InventoryFingerprint == inventoryFingerprint)
            {
                return previous.Text;
            }

            string text = hasRecipe ? FormatRecipe(recipe) : string.Empty;
            recipeRowRenderCache[rowIndex] = new RecipeRowRenderState
            {
                IsValid = true,
                HasRecipe = hasRecipe,
                RecipeIndex = recipeIndex,
                Recipe = recipe,
                AvailableStations = availableStations,
                InventoryFingerprint = inventoryFingerprint,
                Text = text,
            };
            return text;
        }

        int ComputeInventoryFingerprint()
        {
            if (inventory == null)
                return 0;

            unchecked
            {
                int hash = 17;
                for (int slot = 0; slot < inventory.SlotCount; slot++)
                {
                    ItemStack stack = inventory.GetSlot(slot);
                    hash = (hash * 31) ^ stack.ItemId.GetHashCode();
                    hash = (hash * 31) ^ stack.Count;
                    hash = (hash * 31) ^ stack.Durability;
                }
                return hash;
            }
        }

        void InvalidateRecipeRowCache()
        {
            for (int i = 0; i < recipeRowRenderCache.Length; i++)
                recipeRowRenderCache[i].IsValid = false;
        }

        string FormatIngredients(CraftingRecipe recipe)
        {
            var parts = new string[recipe.Ingredients.Count];
            for (int i = 0; i < recipe.Ingredients.Count; i++)
                parts[i] = FormatStack(recipe.Ingredients[i]);

            return string.Join(UiText.Get(ListSeparatorKey), parts);
        }

        string FormatStack(ItemStack stack)
        {
            ItemDefinition definition = itemRegistry.Get(stack.ItemId);
            return UiText.Format(StackKey, definition.Name, stack.Count);
        }

        static void SetTextIfChanged(Label label, string text)
        {
            if (label != null && !string.Equals(label.text, text, StringComparison.Ordinal))
                label.text = text;
        }

        void SetStatus(string message, StatusTone tone)
        {
            statusText = message ?? string.Empty;
            statusTone = tone;
            ApplyStatusToLabel();
        }

        void ApplyStatusToLabel()
        {
            if (statusLabel == null)
                return;

            statusLabel.text = statusText;
            statusLabel.EnableInClassList(StatusConfirmedClassName, statusTone == StatusTone.Confirmed);
            statusLabel.EnableInClassList(StatusRefusedClassName, statusTone == StatusTone.Refused);
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
            DiscoverVfxFeedback();

            // Visual punctuation at the panel itself: sparks on success, a dull puff on failure.
            if (cue == BlockiverseAudioCue.CraftSuccess)
                vfxCuePlayer?.PlayCue(BlockiverseVfxCue.CraftSuccessSpark, transform.position);
            else if (cue == BlockiverseAudioCue.CraftFail)
                vfxCuePlayer?.PlayCue(BlockiverseVfxCue.CraftFailPuff, transform.position);
        }

        void DiscoverVfxFeedback()
        {
            if (!Application.isPlaying)
                return;

            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();
        }

        void EnsureBound()
        {
            if (recipeBook == null || inventory == null)
                throw new InvalidOperationException("Crafting screen has not been bound.");
        }

        // ui.value.* resolution with the same humanize fallback BlockiverseLocalization uses:
        // most of these namespaces have no table entries by design, and a raw key string or a
        // raw enum name on a player-facing panel would both be regressions.
        static string EnumDisplayName(string keyPrefix, string enumValueName)
        {
            string key = keyPrefix + ToSnakeCase(enumValueName);
            string resolved = UiText.Get(key);
            return string.Equals(resolved, key, StringComparison.Ordinal)
                ? HumanizeEnumName(enumValueName)
                : resolved;
        }

        static string ToSnakeCase(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            char previous = '\0';

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (builder.Length > 0 &&
                    char.IsUpper(character) &&
                    (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower)))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
                previous = character;
            }

            return builder.ToString();
        }

        static string HumanizeEnumName(string value)
        {
            var words = new List<string>();
            var builder = new StringBuilder(value.Length);
            char previous = '\0';

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (builder.Length > 0 &&
                    char.IsUpper(character) &&
                    (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower)))
                {
                    words.Add(builder.ToString());
                    builder.Clear();
                }

                builder.Append(character);
                previous = character;
            }

            if (builder.Length > 0)
                words.Add(builder.ToString());

            for (int i = 0; i < words.Count; i++)
            {
                string lower = words[i].ToLowerInvariant();
                bool lowerMinorWord = i > 0 && (lower == "a" || lower == "an" || lower == "the" || lower == "of" || lower == "to");
                words[i] = lowerMinorWord ? lower : char.ToUpperInvariant(lower[0]) + lower.Substring(1);
            }

            return string.Join(" ", words);
        }
    }
}

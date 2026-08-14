using System;
using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.VR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class SurvivalCraftingPanel : MonoBehaviour
    {
        [SerializeField] Button[] recipeButtons;
        [SerializeField] Button repairButton;
        [SerializeField] Button previousRecipePageButton;
        [SerializeField] Button nextRecipePageButton;
        [SerializeField] TMP_Text[] recipeLabels;
        [SerializeField] TMP_Text recipePageLabel;
        [SerializeField] Image[] recipeIcons;
        [SerializeField] BlockiverseItemIconLibrary iconLibrary;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;
        [SerializeField] MultiplayerSurvivalSync survivalSync;

        CraftingRecipeBook recipeBook;
        Inventory inventory;
        ItemRegistry itemRegistry;
        CraftingStationSet availableStations;

        // GetSortedRecipes cache: CraftingRecipeBook is append-only (Register), so the bound book
        // instance plus its recipe count identifies the content that produced the list.
        readonly List<CraftingRecipe> sortedRecipesCache = new();
        CraftingRecipeBook sortedRecipesSource;
        int sortedRecipesSourceCount = -1;
        int recipePage;
        RecipeRowRenderState[] recipeRowRenderCache = Array.Empty<RecipeRowRenderState>();

        enum RecipeAvailability
        {
            Available,
            MissingIngredients,
            WrongStation
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

        public event Action CraftingChanged;
        public int RecipePage => recipePage;

        // Routes crafting through the host-authoritative survival sync when present, so a remote client
        // cannot craft against its local inventory mirror without host validation. Falls back to local
        // CraftingService for isolated/single-component use (tests, no networking).
        public void ConfigureSurvivalSync(MultiplayerSurvivalSync sync) => survivalSync = sync;

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            BlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void Configure(TMP_Text[] targetRecipeLabels, TMP_Text targetStatusLabel)
        {
            Configure(null, targetRecipeLabels, targetStatusLabel);
        }

        public void Configure(
            Button[] targetRecipeButtons,
            TMP_Text[] targetRecipeLabels,
            TMP_Text targetStatusLabel,
            Image[] targetRecipeIcons = null,
            BlockiverseItemIconLibrary targetIconLibrary = null)
        {
            recipeLabels = targetRecipeLabels ?? Array.Empty<TMP_Text>();
            recipeButtons = targetRecipeButtons ?? Array.Empty<Button>();
            recipeIcons = targetRecipeIcons ?? Array.Empty<Image>();
            iconLibrary = targetIconLibrary;
            statusLabel = targetStatusLabel;
            InvalidateRecipeRowCache();
            WireRecipeButtons();
            Refresh();
        }

        public void ConfigureRepairButton(Button targetRepairButton)
        {
            repairButton = targetRepairButton;
            WireRepairButton();
        }

        public void ConfigurePaging(Button previousButton, Button nextButton, TMP_Text targetPageLabel)
        {
            previousRecipePageButton = previousButton;
            nextRecipePageButton = nextButton;
            recipePageLabel = targetPageLabel;
            WirePagingButtons();
            Refresh();
        }

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
            SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingReady));
            Refresh();
        }

        // Updates the set of stations currently in reach (fed by the HUD's proximity scan) so
        // station-gated recipes become craftable when the player stands at the station (§8).
        public void SetAvailableStations(CraftingStationSet stations)
        {
            availableStations = stations;
            InvalidateRecipeRowCache();
            Refresh();
        }

        // The station actually claimed for a craft: the recipe's own requirement when it is in
        // reach, otherwise None (which CraftingService rejects with MissingStation).
        CraftingStation EffectiveStationFor(CraftingRecipe recipe) =>
            availableStations.Contains(recipe.RequiredStation) ? recipe.RequiredStation : CraftingStation.None;

        public CraftingResult TryCraftAtIndex(int index)
        {
            EnsureBound();

            List<CraftingRecipe> recipes = GetSortedRecipes();

            if (index < 0 || index >= recipes.Count)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingRecipeUnavailable));
                PlayFeedback(BlockiverseAudioCue.CraftFail);
                return CraftingResult.Failure(CraftingFailureReason.MissingIngredient);
            }

            return TryCraft(recipes[index]);
        }

        public CraftingResult TryCraftVisibleIndex(int visibleIndex)
        {
            int pageSize = PageSize;
            return TryCraftAtIndex(recipePage * pageSize + visibleIndex);
        }

        public CraftingResult TryCraftByOutput(ItemId outputItemId)
        {
            EnsureBound();

            if (!recipeBook.TryGetByOutput(outputItemId, out CraftingRecipe recipe))
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingRecipeUnavailable));
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
            SetStatus(result.Succeeded
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CraftingCrafted, FormatStack(recipe.Output))
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.CraftingCannotCraft,
                    itemRegistry.Get(recipe.Output.ItemId).Name,
                    BlockiverseLocalization.DisplayName(result.FailureReason)));
            Refresh();
            PlayFeedback(result.Succeeded ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

            if (result.Succeeded)
                CraftingChanged?.Invoke();

            return result;
        }

        // Host-authoritative craft: the host validates and mutates the inventory, then broadcasts the
        // result/snapshot. On the host/offline peer this resolves immediately; on a remote client it is
        // pending until the host responds (the inventory mirror updates from the snapshot).
        CraftingResult TryCraftAuthoritative(CraftingRecipe recipe)
        {
            SurvivalCommandResult command = survivalSync.TrySubmitCraft(recipe.Output.ItemId, EffectiveStationFor(recipe), out bool sentToHost);
            bool acceptedOrPending = command.Accepted || command.PendingHostValidation || sentToHost;

            SetStatus(command.Accepted
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CraftingCrafted, FormatStack(recipe.Output))
                : sentToHost
                    ? BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.CraftingPending,
                        itemRegistry.Get(recipe.Output.ItemId).Name)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.CraftingCannotCraft,
                        itemRegistry.Get(recipe.Output.ItemId).Name,
                        BlockiverseLocalization.DisplayName(command.CraftingFailureReason)));
            Refresh();
            PlayFeedback(acceptedOrPending ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

            if (command.Accepted)
                CraftingChanged?.Invoke();

            return acceptedOrPending
                ? CraftingResult.Success()
                : CraftingResult.Failure(command.CraftingFailureReason, recipe.Output.ItemId);
        }

        // Mend Bench repair of the held tool (§10.7): one matching head material restores 25% max
        // durability. Routed through the host-authoritative sync when present (the host re-validates
        // bench proximity); falls back to local MendBenchRepair for isolated use (tests, no
        // networking). toolSlotIndex -1 repairs the selected hotbar slot.
        public bool TryRepairHeldTool(int toolSlotIndex = -1)
        {
            EnsureBound();

            if (survivalSync != null)
            {
                SurvivalCommandResult command = survivalSync.TrySubmitRepair(out bool sentToHost, toolSlotIndex);
                bool acceptedOrPending = command.Accepted || command.PendingHostValidation || sentToHost;

                SetStatus(command.Accepted
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingToolRepaired)
                    : sentToHost
                        ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingRepairing)
                        : BlockiverseLocalization.Format(
                            BlockiverseLocalization.Keys.CraftingCannotRepair,
                            BlockiverseLocalization.DisplayName(command.FailureReason)));
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

            SetStatus(result.Succeeded
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingToolRepaired)
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.CraftingCannotRepair,
                    BlockiverseLocalization.DisplayName(result.FailureReason)));
            Refresh();
            PlayFeedback(result.Succeeded ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

            if (result.Succeeded)
                CraftingChanged?.Invoke();

            return result.Succeeded;
        }

        public void Refresh()
        {
            if (recipeLabels == null)
                return;

            List<CraftingRecipe> recipes = GetSortedRecipes();
            ClampRecipePage(recipes.Count);
            int offset = recipePage * PageSize;
            int inventoryFingerprint = ComputeInventoryFingerprint();
            EnsureRecipeRowCache(recipeLabels.Length);
            for (int i = 0; i < recipeLabels.Length; i++)
            {
                if (recipeLabels[i] == null)
                    continue;

                int recipeIndex = offset + i;
                bool hasRecipe = recipeIndex < recipes.Count;
                CraftingRecipe recipe = hasRecipe ? recipes[recipeIndex] : null;
                SetTextIfChanged(recipeLabels[i], GetRecipeRowText(i, recipeIndex, recipe, hasRecipe, inventoryFingerprint));
                SetRecipeIcon(i, recipe);
                if (recipeButtons != null && i < recipeButtons.Length && recipeButtons[i] != null)
                    recipeButtons[i].interactable = hasRecipe;
            }

            RefreshPagingControls(recipes.Count);
        }

        // Output-item icon next to the recipe row (blank when no icon exists for the output).
        void SetRecipeIcon(int index, CraftingRecipe recipe)
        {
            if (recipeIcons == null || index >= recipeIcons.Length || recipeIcons[index] == null)
                return;

            Sprite icon = null;
            if (recipe != null && iconLibrary != null)
                iconLibrary.TryGetIcon(recipe.Output.ItemId, out icon);

            recipeIcons[index].sprite = icon;
            recipeIcons[index].enabled = icon != null;
        }

        void Awake()
        {
            WireRecipeButtons();
            WireRepairButton();
            WirePagingButtons();
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

        void WireRepairButton()
        {
            if (repairButton == null)
                return;

            repairButton.onClick.RemoveAllListeners();
            repairButton.onClick.AddListener(() => TryRepairHeldTool());
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

            // Registration order (basics → stations → smelting → tools → utility) keeps early-game
            // recipes at the top of the limited recipe slots, which alphabetical order would bury.
            // Timed (kiln/forge) recipes are excluded: they run on the fueled station model via the
            // station panel, and CraftingService rejects them as instant crafts anyway.
            foreach (CraftingRecipe recipe in recipeBook.All)
            {
                if (recipe.TimeTicks <= 0)
                    sortedRecipesCache.Add(recipe);
            }

            return sortedRecipesCache;
        }

        void WireRecipeButtons()
        {
            if (recipeButtons == null)
                return;

            for (int index = 0; index < recipeButtons.Length; index++)
            {
                Button button = recipeButtons[index];

                if (button == null)
                    continue;

                int recipeIndex = index;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => TryCraftVisibleIndex(recipeIndex));
            }
        }

        void WirePagingButtons()
        {
            if (previousRecipePageButton != null)
            {
                previousRecipePageButton.onClick.RemoveAllListeners();
                previousRecipePageButton.onClick.AddListener(ShowPreviousRecipePage);
            }

            if (nextRecipePageButton != null)
            {
                nextRecipePageButton.onClick.RemoveAllListeners();
                nextRecipePageButton.onClick.AddListener(ShowNextRecipePage);
            }
        }

        int PageSize => recipeLabels != null && recipeLabels.Length > 0 ? recipeLabels.Length : 1;

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
            if (recipePageLabel != null)
                SetTextIfChanged(
                    recipePageLabel,
                    BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.CommonPage,
                        recipePage + 1,
                        pageCount));

            if (previousRecipePageButton != null)
                previousRecipePageButton.interactable = recipePage > 0;

            if (nextRecipePageButton != null)
                nextRecipePageButton.interactable = recipePage < pageCount - 1;
        }

        string FormatRecipe(CraftingRecipe recipe)
        {
            string marker = AvailabilityMarker(AvailabilityFor(recipe));
            string text = BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.CraftingRecipe,
                FormatStack(recipe.Output),
                FormatIngredients(recipe));
            if (!availableStations.Contains(recipe.RequiredStation))
                text = BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.CraftingNeedsStation,
                    FormatStack(recipe.Output),
                    FormatIngredients(recipe),
                    BlockiverseLocalization.DisplayName(recipe.RequiredStation));
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

        static void SetTextIfChanged(TMP_Text label, string text)
        {
            if (label != null && !string.Equals(label.text, text, StringComparison.Ordinal))
                label.text = text;
        }

        void EnsureRecipeRowCache(int length)
        {
            if (recipeRowRenderCache.Length == length)
                return;

            recipeRowRenderCache = new RecipeRowRenderState[length];
            InvalidateRecipeRowCache();
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

            return string.Join(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonListSeparator),
                parts);
        }

        string FormatStack(ItemStack stack)
        {
            ItemDefinition definition = itemRegistry.Get(stack.ItemId);
            return BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CommonStack, definition.Name, stack.Count);
        }

        void SetStatus(string status)
        {
            if (statusLabel != null)
                statusLabel.text = status;
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

        BlockiverseVfxCuePlayer vfxCuePlayer;

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
                throw new InvalidOperationException("Survival crafting panel has not been bound.");
        }
    }
}

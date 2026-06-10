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
        [SerializeField] TMP_Text[] recipeLabels;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;
        [SerializeField] MultiplayerSurvivalSync survivalSync;

        CraftingRecipeBook recipeBook;
        Inventory inventory;
        ItemRegistry itemRegistry;
        CraftingStationSet availableStations;

        public event Action CraftingChanged;

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

        public void Configure(Button[] targetRecipeButtons, TMP_Text[] targetRecipeLabels, TMP_Text targetStatusLabel)
        {
            recipeLabels = targetRecipeLabels ?? Array.Empty<TMP_Text>();
            recipeButtons = targetRecipeButtons ?? Array.Empty<Button>();
            statusLabel = targetStatusLabel;
            WireRecipeButtons();
            Refresh();
        }

        public void ConfigureRepairButton(Button targetRepairButton)
        {
            repairButton = targetRepairButton;
            WireRepairButton();
        }

        public void Bind(
            CraftingRecipeBook targetRecipeBook,
            Inventory targetInventory,
            ItemRegistry registry = null,
            CraftingStation station = CraftingStation.None)
        {
            recipeBook = targetRecipeBook ?? throw new ArgumentNullException(nameof(targetRecipeBook));
            inventory = targetInventory ?? throw new ArgumentNullException(nameof(targetInventory));
            itemRegistry = registry ?? ItemRegistry.CreateDefault();
            availableStations = CraftingStationSet.Of(station);
            SetStatus("Ready");
            Refresh();
        }

        // Updates the set of stations currently in reach (fed by the HUD's proximity scan) so
        // station-gated recipes become craftable when the player stands at the station (§8).
        public void SetAvailableStations(CraftingStationSet stations)
        {
            availableStations = stations;
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
                SetStatus("Recipe unavailable");
                PlayFeedback(BlockiverseAudioCue.CraftFail);
                return CraftingResult.Failure(CraftingFailureReason.MissingIngredient);
            }

            return TryCraft(recipes[index]);
        }

        public CraftingResult TryCraftByOutput(ItemId outputItemId)
        {
            EnsureBound();

            if (!recipeBook.TryGetByOutput(outputItemId, out CraftingRecipe recipe))
            {
                SetStatus("Recipe unavailable");
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
                ? $"Crafted {FormatStack(recipe.Output)}"
                : $"Cannot craft {itemRegistry.Get(recipe.Output.ItemId).Name}: {result.FailureReason}");
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
                ? $"Crafted {FormatStack(recipe.Output)}"
                : sentToHost
                    ? $"Crafting {itemRegistry.Get(recipe.Output.ItemId).Name}…"
                    : $"Cannot craft {itemRegistry.Get(recipe.Output.ItemId).Name}: {command.CraftingFailureReason}");
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
                    ? "Tool repaired"
                    : sentToHost
                        ? "Repairing…"
                        : $"Cannot repair: {command.FailureReason}");
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

            SetStatus(result.Succeeded ? "Tool repaired" : $"Cannot repair: {result.FailureReason}");
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
            for (int i = 0; i < recipeLabels.Length; i++)
            {
                if (recipeLabels[i] == null)
                    continue;

                recipeLabels[i].text = i < recipes.Count ? FormatRecipe(recipes[i]) : string.Empty;
            }
        }

        void Awake()
        {
            WireRecipeButtons();
            WireRepairButton();
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
            var recipes = new List<CraftingRecipe>();
            if (recipeBook == null)
                return recipes;

            // Registration order (basics → stations → smelting → tools → utility) keeps early-game
            // recipes at the top of the limited recipe slots, which alphabetical order would bury.
            // Timed (kiln/forge) recipes are excluded: they run on the fueled station model via the
            // station panel, and CraftingService rejects them as instant crafts anyway.
            foreach (CraftingRecipe recipe in recipeBook.All)
            {
                if (recipe.TimeTicks <= 0)
                    recipes.Add(recipe);
            }

            return recipes;
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
                button.onClick.AddListener(() => TryCraftAtIndex(recipeIndex));
            }
        }

        string FormatRecipe(CraftingRecipe recipe)
        {
            string text = $"{FormatStack(recipe.Output)} - {FormatIngredients(recipe)}";
            if (!availableStations.Contains(recipe.RequiredStation))
                text += $" [needs {CraftingStationNames.DisplayName(recipe.RequiredStation)}]";
            return text;
        }

        string FormatIngredients(CraftingRecipe recipe)
        {
            var parts = new string[recipe.Ingredients.Count];
            for (int i = 0; i < recipe.Ingredients.Count; i++)
                parts[i] = FormatStack(recipe.Ingredients[i]);

            return string.Join(", ", parts);
        }

        string FormatStack(ItemStack stack)
        {
            ItemDefinition definition = itemRegistry.Get(stack.ItemId);
            return $"{definition.Name} x{stack.Count}";
        }

        void SetStatus(string status)
        {
            if (statusLabel != null)
                statusLabel.text = status;
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            DiscoverFeedback();
            audioCuePlayer?.PlayCue(cue);
            interactionHaptics?.PlayUiTick();

            // Visual punctuation at the panel itself: sparks on success, a dull puff on failure.
            if (cue == BlockiverseAudioCue.CraftSuccess)
                vfxCuePlayer?.PlayCue(BlockiverseVfxCue.CraftSuccessSpark, transform.position);
            else if (cue == BlockiverseAudioCue.CraftFail)
                vfxCuePlayer?.PlayCue(BlockiverseVfxCue.CraftFailPuff, transform.position);
        }

        BlockiverseVfxCuePlayer vfxCuePlayer;

        void DiscoverFeedback()
        {
            if (!Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (interactionHaptics == null)
                interactionHaptics = FindFirstObjectByType<BlockiverseInteractionHaptics>();

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

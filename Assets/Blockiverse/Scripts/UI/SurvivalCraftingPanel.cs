using System;
using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.VR;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class SurvivalCraftingPanel : MonoBehaviour
    {
        [SerializeField] Button[] recipeButtons;
        [SerializeField] Text[] recipeLabels;
        [SerializeField] Text statusLabel;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        CraftingRecipeBook recipeBook;
        Inventory inventory;
        ItemRegistry itemRegistry;
        CraftingStation availableStation;

        public event Action CraftingChanged;

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            BlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void Configure(Text[] targetRecipeLabels, Text targetStatusLabel)
        {
            Configure(null, targetRecipeLabels, targetStatusLabel);
        }

        public void Configure(Button[] targetRecipeButtons, Text[] targetRecipeLabels, Text targetStatusLabel)
        {
            recipeLabels = targetRecipeLabels ?? Array.Empty<Text>();
            recipeButtons = targetRecipeButtons ?? Array.Empty<Button>();
            statusLabel = targetStatusLabel;
            WireRecipeButtons();
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
            itemRegistry = registry ?? ItemRegistry.CreateDefault();
            availableStation = station;
            SetStatus("Ready");
            Refresh();
        }

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
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, availableStation);
            SetStatus(result.Succeeded
                ? $"Crafted {FormatStack(recipe.Output)}"
                : $"Cannot craft {itemRegistry.Get(recipe.Output.ItemId).Name}: {result.FailureReason}");
            Refresh();
            PlayFeedback(result.Succeeded ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

            if (result.Succeeded)
                CraftingChanged?.Invoke();

            return result;
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
        }

        List<CraftingRecipe> GetSortedRecipes()
        {
            var recipes = new List<CraftingRecipe>();
            if (recipeBook == null)
                return recipes;

            recipes.AddRange(recipeBook.All);
            recipes.Sort((left, right) => ((int)left.Output.ItemId).CompareTo((int)right.Output.ItemId));
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
            return $"{FormatStack(recipe.Output)} - {FormatIngredients(recipe)}";
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
        }

        void DiscoverFeedback()
        {
            if (!Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (interactionHaptics == null)
                interactionHaptics = FindFirstObjectByType<BlockiverseInteractionHaptics>();
        }

        void EnsureBound()
        {
            if (recipeBook == null || inventory == null)
                throw new InvalidOperationException("Survival crafting panel has not been bound.");
        }
    }
}

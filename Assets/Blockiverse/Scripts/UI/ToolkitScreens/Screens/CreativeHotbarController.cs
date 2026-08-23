using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of the quick block menu (matrix row 24; uGUI: CreativeHotbar on the
    // "Block Menu" canvas). Not a routed screen: implementing IUiToolkitQuickBlockMenu makes
    // UiToolkitMenuHost exclude it from routed visibility and toggle it from the quick-menu
    // hardware button, usable only while the gameplay HUD is the routed screen.
    //
    // Selection reaches the SAME consumer as uGUI: CreativeInteractionController places
    // whatever the scene CreativeHotbar component reports as SelectedBlockId, and that
    // component stays in the tree as the dev fallback — so every selection here is mirrored
    // into it via SelectBlock. The mirror plays the uGUI UiSelect AUDIO cue itself, so this
    // controller adds only the haptic tick when the mirror handled it, and plays the full
    // UiSelect feedback only when no scene hotbar exists; either way exactly one audio cue
    // per selection, plus the haptic the uGUI hotbar never had.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/CreativeHotbar.uxml",
        590, 500, UiToolkitPlacementProfile.Hud, HudLocalY = 0.9f)]
    public sealed class CreativeHotbarController : UiToolkitScreenController, IUiToolkitQuickBlockMenu
    {
        static class Keys
        {
            // Requested new entry (uGUI hard-codes the literal "No block"); until it lands in
            // the table UiText.Get falls back to the key string.
            public const string NoneSelected = "ui.status.blocks.none_selected";
        }

        readonly List<BlockId> blockIds = new();
        readonly List<(Button button, EventCallback<ClickEvent> callback)> renderedSlots = new();

        BlockRegistry registry;
        int selectedIndex;
        CreativeHotbar sceneHotbar;

        Label selectedLabel;
        ScrollView slotsView;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        public override string ScreenId => MenuActions.GameplayHudScreen;

        public BlockId SelectedBlockId => blockIds.Count == 0 ? BlockRegistry.Air : blockIds[selectedIndex];
        public IReadOnlyList<BlockId> BlockIds => blockIds;

        public bool IsQuickMenuVisible => IsVisible;

        public void SetQuickMenuVisible(bool visible)
        {
            // The host pushes false on every router change; only a real transition may play
            // the open/close cues (the uGUI hotbar's initial Hide was likewise silent).
            if (visible == IsVisible)
                return;

            SetVisible(visible, visible);
            BlockiverseUiFeedback.Play(
                ref audioCuePlayer,
                ref interactionHaptics,
                visible ? BlockiverseAudioCue.InventoryOpen : BlockiverseAudioCue.InventoryClose,
                playAudio: true,
                playHaptic: false);
        }

        // Mirrors CreativeHotbar.Configure: non-air, renderable blocks only, selection reset
        // to the first entry.
        public void Configure(BlockRegistry blockRegistry, IEnumerable<BlockId> selectableBlocks)
        {
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            blockIds.Clear();

            if (selectableBlocks != null)
            {
                foreach (BlockId blockId in selectableBlocks)
                {
                    BlockDefinition definition = registry.Get(blockId);

                    if (definition.Id != BlockRegistry.Air && definition.IsRenderable)
                        blockIds.Add(definition.Id);
                }
            }

            selectedIndex = 0;
            RebuildSlotButtons();
            RefreshSelection();
        }

        public void SelectIndex(int index)
        {
            if (blockIds.Count == 0)
                return;

            selectedIndex = Mathf.Clamp(index, 0, blockIds.Count - 1);
            RefreshSelection();

            bool mirrored = MirrorSelectionIntoSceneHotbar();
            BlockiverseUiFeedback.Play(
                ref audioCuePlayer,
                ref interactionHaptics,
                BlockiverseAudioCue.UiSelect,
                playAudio: !mirrored,
                playHaptic: true);
        }

        public void SelectNext()
        {
            if (blockIds.Count == 0)
                return;

            SelectIndex((selectedIndex + 1) % blockIds.Count);
        }

        // Selects a specific block (catalog browser / pick-block parity). False when the
        // block is not in the selectable list.
        public bool SelectBlock(BlockId blockId)
        {
            for (int i = 0; i < blockIds.Count; i++)
            {
                if (blockIds[i] == blockId)
                {
                    SelectIndex(i);
                    return true;
                }
            }

            return false;
        }

        protected override void OnAwake()
        {
            if (registry == null)
                ConfigureDefault();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            selectedLabel = Require<Label>(root, "bv-hotbar-selected", ref allFound);
            slotsView = Require<ScrollView>(root, "bv-hotbar-slots", ref allFound);

            RefreshSelection();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            RebuildSlotButtons();
        }

        protected override void OnUnregisterCallbacks()
        {
            UnregisterSlotCallbacks();
        }

        protected override void OnDetach()
        {
            selectedLabel = null;
            slotsView = null;
        }

        void ConfigureDefault()
        {
            CreativeCatalog catalog = CreativeCatalog.CreateDefault();
            var catalogBlocks = new List<BlockId>(catalog.All.Count);
            foreach (CreativeCatalogEntry entry in catalog.All)
                catalogBlocks.Add(entry.BlockId);

            Configure(BlockRegistry.Default, catalogBlocks);
        }

        bool MirrorSelectionIntoSceneHotbar()
        {
            if (!Application.isPlaying)
                return false;

            if (sceneHotbar == null)
                sceneHotbar = BlockiverseSceneLookup.Find<CreativeHotbar>(FindObjectsInactive.Include);

            return sceneHotbar != null && sceneHotbar.SelectBlock(SelectedBlockId);
        }

        void RefreshSelection()
        {
            if (selectedLabel != null)
            {
                selectedLabel.text = blockIds.Count == 0
                    ? UiText.Get(Keys.NoneSelected)
                    : registry.Get(SelectedBlockId).Name;
            }

            for (int i = 0; i < renderedSlots.Count; i++)
            {
                if (i == selectedIndex && blockIds.Count > 0)
                    renderedSlots[i].button.AddToClassList("hs-button--selected");
                else
                    renderedSlots[i].button.RemoveFromClassList("hs-button--selected");
            }
        }

        void UnregisterSlotCallbacks()
        {
            foreach ((Button button, EventCallback<ClickEvent> callback) in renderedSlots)
                button.UnregisterCallback(callback);

            renderedSlots.Clear();
        }

        // One button per selectable block. Old callbacks are unregistered before the view is
        // cleared so the registration balance cannot drift across rebuilds.
        void RebuildSlotButtons()
        {
            if (slotsView == null)
                return;

            UnregisterSlotCallbacks();
            slotsView.Clear();

            for (int i = 0; i < blockIds.Count; i++)
            {
                var button = new Button
                {
                    name = $"bv-hotbar-slot-{i}",
                    text = registry.Get(blockIds[i]).Name,
                };
                button.AddToClassList("hs-button");

                int slotIndex = i;
                EventCallback<ClickEvent> callback = _ => SelectIndex(slotIndex);
                button.RegisterCallback(callback);
                renderedSlots.Add((button, callback));
                slotsView.Add(button);
            }

            RefreshSelection();
        }
    }
}

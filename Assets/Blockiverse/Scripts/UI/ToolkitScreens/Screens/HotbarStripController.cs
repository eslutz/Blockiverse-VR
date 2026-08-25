using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // The persistent hotbar strip (FPV HUD report; ADR 0010 amendment 2026-08-25).
    //
    // ── What this adds that the migration did not ────────────────────────────
    //
    // #344 ported the uGUI HUD faithfully, and the uGUI HUD had no hotbar: changing the held item
    // meant opening the inventory screen, and changing the held BLOCK meant opening the block
    // menu. The report's central interaction argument is that the most frequent action in a voxel
    // game should not cost a screen open, so this strip shows all ten slots all the time and the
    // support hand's face buttons cycle them.
    //
    // ── Read-only by construction ────────────────────────────────────────────
    //
    // NonInteractive, so the generator produces no collider. A permanently live trigger volume
    // across 0.94 m of the player's lower view would compete with break and place for every ray —
    // the single worst thing a persistent HUD panel can do, and the report says so more strongly
    // than it asks for ray selection. Arbitrary-slot picking still exists on the inventory screen.
    //
    // ── Authority ────────────────────────────────────────────────────────────
    //
    // Selection goes through MultiplayerSurvivalSync.SetSelectedHotbarSlot and is then read BACK
    // from the sync, never assumed: the sync clamps to its own hotbar size, and rendering what was
    // asked for rather than what was accepted is how a HUD ends up showing a slot the domain does
    // not consider held.
    //
    // ── Render discipline ────────────────────────────────────────────────────
    //
    // Per-slot last-value gates, matching InventoryScreenController's SlotRenderState. Text
    // assignment allocates in retained mode and this panel is visible for the entire session, so
    // an ungated refresh is a permanent per-frame cost rather than a one-off.
    // ── Placement: 760x92 at Y −0.340 ────────────────────────────────────────
    //
    // Narrower and lower than it was. At 1000 px it spanned ±24 deg of the player's view, and with
    // the action bar stacked directly beneath it the two read — in live validation — as "a
    // continuous block, not two separable things". That block was the substance of the crowding
    // Eric raised. 760 px is ±19 deg, and the action bar has left the HUD entirely for the wrist
    // menu, so the strip no longer has to fit a gap between two other panels.
    //
    // 64 px slots rather than 84: ten slots plus margins is what sets the width, so the slot size
    // IS the width decision. 64 px is 64 mm at the project's scale — still a comfortable glance
    // target at 1.10 m.
    //
    // Dropped to Y −0.340 (15-19 deg below eye level) now that nothing sits under it. It is the one
    // persistent panel the report explicitly wants, so it stays centred and low rather than moving
    // to a side.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/HotbarStrip.uxml",
        760, 92, UiToolkitPlacementProfile.Hud, HudLocalY = -0.340f, NonInteractive = true)]
    public sealed class HotbarStripController : UiToolkitScreenController
    {
        // Inventory.DefaultHotbarSlotCount. The save format, the wire protocol and
        // SelectedHotbarSlotIndex all index this range; it is not a layout choice.
        public const int SlotCount = 10;

        // Collapses the strip when there is no survival inventory — i.e. in Creative, which keeps
        // its own block-label cycler and must not also carry ten empty survival slots.
        //
        // This is also what keeps the strip clear of the creative quick block menu: that panel is
        // 590 x 500 at Y -0.50, so its top edge reaches -0.25 and overlaps this strip's -0.320..
        // -0.210 band by 590 x 70 mm. HudPanelOverlapEditModeTests excludes the quick block menu as
        // "opened on demand", which is true but would not have saved a strip that was visible in
        // the same mode.
        //
        // Applied to bv-hotbar-strip, NOT bv-screen-root: the base class writes an inline
        // style.display onto the root, and inline outranks USS.
        const string StripHiddenClass = "hb-strip--hidden";

        const string OccupiedClass = "hb-slot--occupied";
        const string SelectedClass = "hb-slot--selected";
        const string CountHiddenClass = "hb-slot__count--hidden";

        VisualElement strip;

        readonly VisualElement[] slots = new VisualElement[SlotCount];
        readonly VisualElement[] slotIcons = new VisualElement[SlotCount];
        readonly Label[] slotCounts = new Label[SlotCount];

        // Render-diff, one entry per slot.
        readonly ItemStack[] lastStacks = new ItemStack[SlotCount];
        readonly bool[] lastValid = new bool[SlotCount];

        MultiplayerSurvivalSync survivalSync;
        BlockiverseItemIconLibrary iconLibrary;
        ItemRegistry itemRegistry;
        Inventory inventory;

        int lastSelectedRendered = int.MinValue;

        public override string ScreenId => MenuActions.GameplayHudScreen;

        // The sync is the authority whenever there IS one — it clamps to its own hotbar size, and
        // rendering what was asked for rather than what was accepted is how a HUD ends up showing a
        // slot the domain does not consider held.
        //
        // The local fallback covers the window before a sync exists (the strip attaches with the
        // HUD, which can happen first). Without it, cycling in that window was silently inert: the
        // player pressed a face button and nothing moved, with nothing logged.
        public int SelectedSlotIndex =>
            survivalSync != null ? survivalSync.SelectedHotbarSlotIndex : localSelectedIndex;

        int localSelectedIndex;

        public void ConfigureIconLibrary(BlockiverseItemIconLibrary targetIconLibrary)
        {
            iconLibrary = targetIconLibrary;
            InvalidateRenderCache();
            Refresh();
        }

        // Test seam: bind an inventory directly rather than discovering a sync from the scene.
        public void BindForTest(Inventory targetInventory, ItemRegistry registry = null)
        {
            inventory = targetInventory;
            itemRegistry = registry ?? ItemRegistry.Default;
            InvalidateRenderCache();
            Refresh();
        }

        // ── Selection ────────────────────────────────────────────────────────

        public void SelectNext() => Cycle(1);

        public void SelectPrevious() => Cycle(-1);

        void Cycle(int delta)
        {
            int count = inventory != null ? inventory.HotbarSlotCount : 0;

            if (count <= 0)
                return;

            // Double modulo: C# % keeps the sign of the dividend, so decrementing past zero would
            // otherwise land on a negative index.
            int next = ((SelectedSlotIndex + delta) % count + count) % count;
            SelectSlot(next);
        }

        public void SelectSlot(int slotIndex)
        {
            if (inventory == null || inventory.HotbarSlotCount <= 0)
                return;

            int clamped = Mathf.Clamp(slotIndex, 0, inventory.HotbarSlotCount - 1);

            localSelectedIndex = clamped;

            if (survivalSync != null)
                survivalSync.SetSelectedHotbarSlot(clamped);

            Refresh();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        protected override void OnAwake() => BindFromScene();

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            strip = Require<VisualElement>(root, "bv-hotbar-strip", ref allFound);

            for (int i = 0; i < SlotCount; i++)
            {
                string prefix = $"bv-hotbar-slot-{i}";
                slots[i] = Require<VisualElement>(root, prefix, ref allFound);
                slotIcons[i] = Require<VisualElement>(root, prefix + "-icon", ref allFound);
                slotCounts[i] = Require<Label>(root, prefix + "-count", ref allFound);
            }

            // Brand-new element instances: repaint from the bound inventory, not the cache.
            InvalidateRenderCache();
            Refresh();
            return allFound;
        }

        // Nothing here is clickable — the panel is NonInteractive and every element is
        // picking-mode Ignore, so there is no element callback to balance. Selection arrives from
        // Input Actions through SelectNext/SelectPrevious, and the inventory subscription is keyed
        // on the sync instance in BindFromScene rather than on attach, because it must survive the
        // tree being rebuilt.
        protected override void OnRegisterCallbacks()
        {
        }

        protected override void OnUnregisterCallbacks()
        {
        }

        protected override void OnDetach()
        {
            strip = null;

            for (int i = 0; i < SlotCount; i++)
            {
                slots[i] = null;
                slotIcons[i] = null;
                slotCounts[i] = null;
            }
        }

        protected override void OnShown()
        {
            BindFromScene();
            Refresh();
        }

        void OnDestroy()
        {
            if (survivalSync != null)
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
        }

        // Re-run on every show so a replaced sync instance rebinds, mirroring the other screens'
        // BindFromScene. The subscription is stored against the exact instance it was made on, so a
        // later bind detaches the previous one instead of leaking it.
        void BindFromScene()
        {
            itemRegistry ??= ItemRegistry.Default;

            if (iconLibrary == null)
                iconLibrary = BlockiverseSceneLookup.Find<BlockiverseItemIconLibrary>(FindObjectsInactive.Include);

            MultiplayerSurvivalSync sync =
                BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (!ReferenceEquals(sync, survivalSync))
            {
                if (survivalSync != null)
                    survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;

                survivalSync = sync;

                if (survivalSync != null)
                    survivalSync.LocalInventoryChanged += OnLocalInventoryChanged;
            }

            if (survivalSync != null)
                inventory = survivalSync.LocalInventory;

            InvalidateRenderCache();
        }

        void OnLocalInventoryChanged()
        {
            // The sync can REPLACE its inventory instance (an explicit Configure, or a host
            // snapshot adopting a restored save). Rebinding rather than trusting the captured
            // reference is what stops the strip rendering an inventory nothing writes to any more.
            if (survivalSync != null && !ReferenceEquals(inventory, survivalSync.LocalInventory))
            {
                inventory = survivalSync.LocalInventory;
                InvalidateRenderCache();
            }

            Refresh();
        }

        // ── Render ───────────────────────────────────────────────────────────

        public void Refresh()
        {
            if (slots[0] == null)
                return;

            // No survival inventory means Creative: collapse rather than draw ten empty recesses.
            bool hasHotbar = inventory != null && inventory.HotbarSlotCount > 0;
            strip?.EnableInClassList(StripHiddenClass, !hasHotbar);

            if (!hasHotbar)
                return;

            for (int i = 0; i < SlotCount; i++)
            {
                ItemStack stack = inventory != null && i < inventory.SlotCount
                    ? inventory.GetSlot(i)
                    : ItemStack.Empty;

                if (lastValid[i] && lastStacks[i].Equals(stack))
                    continue;

                lastStacks[i] = stack;
                lastValid[i] = true;
                ApplySlot(i, stack);
            }

            RefreshSelection();
        }

        void RefreshSelection()
        {
            int selected = SelectedSlotIndex;

            if (selected == lastSelectedRendered)
                return;

            if (lastSelectedRendered >= 0 && lastSelectedRendered < SlotCount)
                slots[lastSelectedRendered]?.RemoveFromClassList(SelectedClass);

            if (selected >= 0 && selected < SlotCount)
                slots[selected]?.AddToClassList(SelectedClass);

            lastSelectedRendered = selected;
        }

        void ApplySlot(int index, ItemStack stack)
        {
            bool empty = stack.IsEmpty;

            slots[index]?.EnableInClassList(OccupiedClass, !empty);

            VisualElement icon = slotIcons[index];

            if (icon != null)
            {
                Sprite sprite = null;

                if (!empty && iconLibrary != null)
                    iconLibrary.TryGetIcon(stack.ItemId, out sprite);

                // StyleKeyword.None clears the image. A default StyleBackground leaves the previous
                // sprite in place, so an emptied slot would keep drawing what used to be in it.
                icon.style.backgroundImage = sprite != null
                    ? new StyleBackground(sprite)
                    : new StyleBackground(StyleKeyword.None);
            }

            Label count = slotCounts[index];

            if (count == null)
                return;

            // A count of 1 is not drawn: ten slots each announcing "1" is noise, and the icon
            // already says the slot is occupied. The number only matters once it is a quantity.
            bool showCount = !empty && stack.Count > 1;

            if (showCount)
                count.text = UiText.Format(HotbarKeys.SlotCount, stack.Count);

            count.EnableInClassList(CountHiddenClass, !showCount);
        }

        void InvalidateRenderCache()
        {
            for (int i = 0; i < lastValid.Length; i++)
                lastValid[i] = false;

            lastSelectedRendered = int.MinValue;
        }

        static class HotbarKeys
        {
            // Reuses the inventory's existing stack-count entry rather than adding a parallel one:
            // it is the same quantity in the same visual role, and two keys would be two things to
            // keep in sync for no gain.
            public const string SlotCount = "ui.common.stack_count";
        }
    }
}

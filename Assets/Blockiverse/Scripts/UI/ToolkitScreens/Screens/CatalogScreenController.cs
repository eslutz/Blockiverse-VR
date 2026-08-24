using System;
using System.Collections.Generic;
using System.Text;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of BlockiverseCatalogBrowserPanel (matrix row 17): cycle through catalog
    // categories, page through a 3x4 grid of the category's blocks, or search the whole catalog
    // by display name. Picking an entry selects that block in the scene's CreativeHotbar — the
    // same consumer the uGUI panel feeds, and the source CreativeInteractionController reads for
    // placement. Cue split: category/page/close play the click here, but ENTRY PICKS DO NOT — the
    // hotbar plays UiSelect inside SelectBlock, and a cue here too double-plays the same clip in
    // the same frame (~+6 dB louder than every other click). CreativeHotbarController solved the
    // identical collision with playAudio:!mirrored; here the hotbar's cue is simply the only one.
    [UiToolkitScreen(MenuActions.CatalogScreen, "Assets/Blockiverse/UI/Documents/CatalogScreen.uxml",
        1000, 760, UiToolkitPlacementProfile.Menu)]
    public sealed class CatalogScreenController : UiToolkitScreenController
    {
        public const int EntryCount = 12;

        static class Keys
        {
            public const string CatalogSearch = "ui.status.catalog.search";
            public const string CommonPage = "ui.common.page";
            public const string BlocksSearchPlaceholder = "ui.generated.blocks.search_placeholder";
            public const string CategoryValueKeyPrefix = "ui.value.creative_catalog_category.";
        }

        static readonly CreativeCatalogCategory[] Categories =
            (CreativeCatalogCategory[])Enum.GetValues(typeof(CreativeCatalogCategory));

        readonly List<BlockId> visibleBlocks = new();
        readonly List<BlockId> pageBlocks = new();

        CreativeHotbar hotbar;
        CreativeCatalog catalog;
        BlockRegistry registry;
        int categoryIndex;
        int pageIndex;
        // Mirrors the search field so a tree rebuild mid-session does not silently drop the
        // active filter; the field itself stays the source of truth at Refresh time.
        string searchText = string.Empty;

        Button categoryButton;
        Label categoryValueLabel;
        TextField searchField;
        Button previousPageButton;
        Label pageLabel;
        Button nextPageButton;
        Button closeButton;
        readonly Button[] entryButtons = new Button[EntryCount];

        EventCallback<ClickEvent> categoryClickCallback;
        EventCallback<ClickEvent> previousPageClickCallback;
        EventCallback<ClickEvent> nextPageClickCallback;
        EventCallback<ClickEvent> closeClickCallback;
        EventCallback<ClickEvent>[] entryClickCallbacks;
        EventCallback<ChangeEvent<string>> searchChangedCallback;

        public override string ScreenId => MenuActions.CatalogScreen;

        // Test seam mirroring the uGUI panel's Configure: injects the selection consumer and
        // optionally the catalog/registry pair, then repaints.
        public void ConfigureCatalog(
            CreativeHotbar creativeHotbar,
            BlockRegistry blockRegistry = null,
            CreativeCatalog creativeCatalog = null)
        {
            hotbar = creativeHotbar;
            if (blockRegistry != null)
                registry = blockRegistry;
            if (creativeCatalog != null)
                catalog = creativeCatalog;
            Refresh();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            categoryButton = Require<Button>(root, "bv-category", ref allFound);
            categoryValueLabel = Require<Label>(root, "bv-category-value", ref allFound);
            searchField = Require<TextField>(root, "bv-search", ref allFound);
            previousPageButton = Require<Button>(root, "bv-page-previous", ref allFound);
            pageLabel = Require<Label>(root, "bv-page-label", ref allFound);
            nextPageButton = Require<Button>(root, "bv-page-next", ref allFound);
            closeButton = Require<Button>(root, "bv-close", ref allFound);

            for (int i = 0; i < EntryCount; i++)
                entryButtons[i] = Require<Button>(root, $"bv-entry-{i + 1}", ref allFound);

            if (searchField != null)
            {
                searchField.textEdition.placeholder = UiText.Get(Keys.BlocksSearchPlaceholder);
                searchField.SetValueWithoutNotify(searchText);
            }

            if (hotbar == null)
                hotbar = BlockiverseSceneLookup.Find<CreativeHotbar>(FindObjectsInactive.Include);

            Refresh();
            return allFound;
        }

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        // Cue rides the click, never the Submit*/Cycle* seams the tests drive directly.
        void PlayCue() =>
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);

        protected override void OnRegisterCallbacks()
        {
            categoryClickCallback = _ => { PlayCue(); CycleCategory(); };
            previousPageClickCallback = _ => { PlayCue(); PreviousPage(); };
            nextPageClickCallback = _ => { PlayCue(); NextPage(); };
            closeClickCallback = _ => { PlayCue(); SubmitClose(); };
            categoryButton?.RegisterCallback(categoryClickCallback);
            previousPageButton?.RegisterCallback(previousPageClickCallback);
            nextPageButton?.RegisterCallback(nextPageClickCallback);
            closeButton?.RegisterCallback(closeClickCallback);

            entryClickCallbacks = new EventCallback<ClickEvent>[EntryCount];
            for (int i = 0; i < EntryCount; i++)
            {
                int index = i;
                // No PlayCue: SelectEntry -> hotbar.SelectBlock -> hotbar plays UiSelect.
                entryClickCallbacks[i] = _ => SelectEntry(index);
                entryButtons[i]?.RegisterCallback(entryClickCallbacks[i]);
            }

            searchChangedCallback = _ => ApplySearchFilter();
            searchField?.RegisterValueChangedCallback(searchChangedCallback);

            // The category/page labels are cached dynamic text (matrix §4: locale change).
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (categoryClickCallback != null)
                categoryButton?.UnregisterCallback(categoryClickCallback);
            if (previousPageClickCallback != null)
                previousPageButton?.UnregisterCallback(previousPageClickCallback);
            if (nextPageClickCallback != null)
                nextPageButton?.UnregisterCallback(nextPageClickCallback);
            if (closeClickCallback != null)
                closeButton?.UnregisterCallback(closeClickCallback);
            categoryClickCallback = null;
            previousPageClickCallback = null;
            nextPageClickCallback = null;
            closeClickCallback = null;

            for (int i = 0; i < EntryCount; i++)
            {
                if (entryClickCallbacks != null && entryClickCallbacks[i] != null)
                    entryButtons[i]?.UnregisterCallback(entryClickCallbacks[i]);
            }

            entryClickCallbacks = null;

            if (searchChangedCallback != null)
                searchField?.UnregisterValueChangedCallback(searchChangedCallback);
            searchChangedCallback = null;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            categoryButton = null;
            categoryValueLabel = null;
            searchField = null;
            previousPageButton = null;
            pageLabel = null;
            nextPageButton = null;
            closeButton = null;

            for (int i = 0; i < EntryCount; i++)
                entryButtons[i] = null;
        }

        // ── Public handler seams (also the click targets) ─────────────────────

        public void CycleCategory()
        {
            categoryIndex = (categoryIndex + 1) % Categories.Length;
            pageIndex = 0;
            searchText = string.Empty;
            searchField?.SetValueWithoutNotify(string.Empty);
            Refresh();
        }

        public void NextPage()
        {
            pageIndex++;
            Refresh();
        }

        public void PreviousPage()
        {
            pageIndex = Mathf.Max(0, pageIndex - 1);
            Refresh();
        }

        public void ApplySearchFilter()
        {
            searchText = searchField != null ? searchField.value : string.Empty;
            pageIndex = 0;
            Refresh();
        }

        public void SelectEntry(int entryIndex)
        {
            if (entryIndex < 0 || entryIndex >= pageBlocks.Count)
                return;

            if (hotbar == null)
                hotbar = BlockiverseSceneLookup.Find<CreativeHotbar>(FindObjectsInactive.Include);

            if (hotbar == null)
                return;

            hotbar.SelectBlock(pageBlocks[entryIndex]);
        }

        public void SubmitClose()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller != null)
                controller.CloseCatalogScreen();
        }

        // ── Grid refresh ──────────────────────────────────────────────────────

        public void Refresh()
        {
            EnsureCatalog();
            BuildVisibleBlocks();

            int pageCount = Mathf.Max(1, (visibleBlocks.Count + EntryCount - 1) / EntryCount);
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);

            pageBlocks.Clear();
            int start = pageIndex * EntryCount;
            for (int i = start; i < visibleBlocks.Count && pageBlocks.Count < EntryCount; i++)
                pageBlocks.Add(visibleBlocks[i]);

            for (int i = 0; i < EntryCount; i++)
            {
                Button entry = entryButtons[i];
                if (entry == null)
                    continue;

                bool hasEntry = i < pageBlocks.Count;
                // Hidden, never collapsed: the grid keeps its geometry on short pages so entry
                // positions never reflow (the uGUI grid was absolutely positioned).
                entry.style.visibility = hasEntry ? Visibility.Visible : Visibility.Hidden;
                entry.text = hasEntry ? registry.Get(pageBlocks[i]).Name : string.Empty;
            }

            bool searching = !string.IsNullOrWhiteSpace(CurrentSearchText());
            if (categoryValueLabel != null)
                categoryValueLabel.text = searching
                    ? UiText.Get(Keys.CatalogSearch)
                    : CategoryDisplayName(Categories[categoryIndex]);
            if (pageLabel != null)
                pageLabel.text = UiText.Format(Keys.CommonPage, pageIndex + 1, pageCount);
        }

        void BuildVisibleBlocks()
        {
            visibleBlocks.Clear();

            string search = CurrentSearchText();
            if (!string.IsNullOrWhiteSpace(search))
            {
                // Search spans the whole catalog regardless of the active category.
                foreach (CreativeCatalogEntry entry in catalog.All)
                {
                    if (registry.Get(entry.BlockId).Name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        visibleBlocks.Add(entry.BlockId);
                }

                return;
            }

            foreach (CreativeCatalogEntry entry in catalog.InCategory(Categories[categoryIndex]))
                visibleBlocks.Add(entry.BlockId);
        }

        string CurrentSearchText() => searchField != null ? searchField.value : searchText;

        void EnsureCatalog()
        {
            if (registry == null)
                registry = BlockRegistry.Default;
            if (catalog == null)
                catalog = CreativeCatalog.CreateDefault(registry);
        }

        void OnSelectedLocaleChanged(Locale locale) => Refresh();

        // Category display names resolve table-first from the same
        // ui.value.creative_catalog_category.* keys the uGUI shim's DisplayName uses, with the
        // identical humanized fallback while those entries are still pending centrally. Screen
        // controllers must not call BlockiverseLocalization, so the two tiny transforms are
        // reproduced here; they match NormalizeKey/HumanizeIdentifier byte-for-byte for this
        // closed enum set (single-case names, no digits, no consecutive capitals).
        static string CategoryDisplayName(CreativeCatalogCategory category)
        {
            string enumName = category.ToString();
            string key = Keys.CategoryValueKeyPrefix + ToSnakeCase(enumName);
            string resolved = UiText.Get(key);
            return string.Equals(resolved, key, StringComparison.Ordinal) ? SplitWords(enumName) : resolved;
        }

        static string ToSnakeCase(string enumName)
        {
            var builder = new StringBuilder(enumName.Length + 4);
            for (int i = 0; i < enumName.Length; i++)
            {
                char character = enumName[i];
                if (char.IsUpper(character) && i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        static string SplitWords(string enumName)
        {
            var builder = new StringBuilder(enumName.Length + 4);
            for (int i = 0; i < enumName.Length; i++)
            {
                char character = enumName[i];
                if (char.IsUpper(character) && i > 0)
                    builder.Append(' ');
                builder.Append(character);
            }

            return builder.ToString();
        }
    }
}

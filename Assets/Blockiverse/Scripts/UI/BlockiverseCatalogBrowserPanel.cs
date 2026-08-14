using System;
using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // Creative catalog browser on the quick blocks menu: cycle through catalog categories, page
    // through a button grid of the category's blocks, or search the whole catalog by name (the
    // search field raises the system keyboard, like the LAN address field). Picking an entry
    // selects that block in the creative hotbar.
    [DisallowMultipleComponent]
    public sealed class BlockiverseCatalogBrowserPanel : MonoBehaviour
    {
        [SerializeField] CreativeHotbar hotbar;
        [SerializeField] TMP_Text categoryLabel;
        [SerializeField] TMP_Text pageLabel;
        [SerializeField] TMP_InputField searchField;
        [SerializeField] Button[] entryButtons;
        [SerializeField] TMP_Text[] entryLabels;

        readonly List<BlockId> visibleBlocks = new();
        readonly List<BlockId> pageBlocks = new();

        CreativeCatalog catalog;
        BlockRegistry registry;
        int categoryIndex;
        int pageIndex;
        bool wired;

        static readonly CreativeCatalogCategory[] Categories =
            (CreativeCatalogCategory[])Enum.GetValues(typeof(CreativeCatalogCategory));

        public void Configure(
            CreativeHotbar creativeHotbar,
            TMP_Text category,
            TMP_Text page,
            TMP_InputField search,
            Button[] buttons,
            TMP_Text[] labels)
        {
            UnwireControls();
            hotbar = creativeHotbar;
            categoryLabel = category;
            pageLabel = page;
            searchField = search;
            entryButtons = buttons ?? Array.Empty<Button>();
            entryLabels = labels ?? Array.Empty<TMP_Text>();
            WireControls();
            Refresh();
        }

        void OnEnable()
        {
            EnsureCatalog();
            WireControls();
            Refresh();
        }

        void OnDisable()
        {
            UnwireControls();
        }

        void EnsureCatalog()
        {
            if (catalog != null)
                return;

            registry = BlockRegistry.Default;
            catalog = CreativeCatalog.CreateDefault(registry);
        }

        void WireControls()
        {
            if (wired || entryButtons == null)
                return;

            for (int i = 0; i < entryButtons.Length; i++)
            {
                int index = i;
                entryButtons[i]?.onClick.AddListener(() => OnEntryClicked(index));
            }

            searchField?.onValueChanged.AddListener(OnSearchChanged);
            wired = true;
        }

        void UnwireControls()
        {
            if (!wired)
                return;

            // Listeners added with closures cannot be removed individually; clearing the
            // runtime (non-persistent) listeners is equivalent here.
            if (entryButtons != null)
            {
                foreach (Button button in entryButtons)
                    button?.onClick.RemoveAllListeners();
            }

            searchField?.onValueChanged.RemoveListener(OnSearchChanged);
            wired = false;
        }

        // ── Controls (wired by the bootstrapper as persistent listeners) ──────

        public void CycleCategory()
        {
            categoryIndex = (categoryIndex + 1) % Categories.Length;
            pageIndex = 0;
            searchField?.SetTextWithoutNotify(string.Empty);
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

        void OnSearchChanged(string _)
        {
            pageIndex = 0;
            Refresh();
        }

        void OnEntryClicked(int index)
        {
            if (index >= pageBlocks.Count || hotbar == null)
                return;

            hotbar.SelectBlock(pageBlocks[index]);
        }

        // ── Grid refresh ──────────────────────────────────────────────────────

        public void Refresh()
        {
            EnsureCatalog();
            BuildVisibleBlocks();

            int perPage = entryButtons?.Length ?? 0;
            int pageCount = perPage > 0 ? Mathf.Max(1, (visibleBlocks.Count + perPage - 1) / perPage) : 1;
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);

            pageBlocks.Clear();
            int start = pageIndex * perPage;
            for (int i = start; i < visibleBlocks.Count && pageBlocks.Count < perPage; i++)
                pageBlocks.Add(visibleBlocks[i]);

            for (int i = 0; i < perPage; i++)
            {
                bool hasEntry = i < pageBlocks.Count;
                if (entryButtons[i] != null)
                    entryButtons[i].gameObject.SetActive(hasEntry);
                if (entryLabels != null && i < entryLabels.Length && entryLabels[i] != null)
                    entryLabels[i].text = hasEntry ? registry.Get(pageBlocks[i]).Name : string.Empty;
            }

            bool searching = !string.IsNullOrWhiteSpace(searchField != null ? searchField.text : null);
            if (categoryLabel != null)
                categoryLabel.text = searching
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CatalogSearch)
                    : BlockiverseLocalization.DisplayName(Categories[categoryIndex]);
            if (pageLabel != null)
                pageLabel.text = BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.CommonPage,
                    pageIndex + 1,
                    pageCount);
        }

        void BuildVisibleBlocks()
        {
            visibleBlocks.Clear();

            string search = searchField != null ? searchField.text : null;
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
    }
}

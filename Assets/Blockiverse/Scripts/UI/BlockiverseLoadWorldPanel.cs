using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // View layer for the Load World screen (voxel_survival_menus §6.4). Wraps SaveListModel;
    // pages save rows in fixed-size groups. ActionRequested fires LoadWorldLoad or LoadWorldCancel.
    public sealed class BlockiverseLoadWorldPanel : MonoBehaviour
    {
        const int MaxEntries = 6;

        [SerializeField] Button[] entryButtons;
        [SerializeField] TMP_Text[] entryLabels;
        [SerializeField] Button loadButton;
        [SerializeField] Button detailsButton;
        [SerializeField] Button cancelButton;
        [SerializeField] Button previousPageButton;
        [SerializeField] Button nextPageButton;
        [SerializeField] TMP_Text pageLabel;
        [SerializeField] TMP_Text selectionLabel;

        readonly SaveListModel model = new();
        int pageIndex;
        bool controlsWired;

        public WorldSaveSummary? SelectedSave => model.SelectedSave;
        public int PageIndex => pageIndex;
        public int PageCount => PageCountFor(model.VisibleSaves.Count);
        public event Action<string> ActionRequested;

        public void Configure(
            Button[] entryButtons,
            TMP_Text[] entryLabels,
            Button loadButton,
            Button cancelButton,
            TMP_Text selectionLabel,
            Button detailsButton = null,
            Button previousPageButton = null,
            Button nextPageButton = null,
            TMP_Text pageLabel = null)
        {
            this.entryButtons = entryButtons ?? Array.Empty<Button>();
            this.entryLabels = entryLabels ?? Array.Empty<TMP_Text>();
            this.loadButton = loadButton;
            this.detailsButton = detailsButton;
            this.cancelButton = cancelButton;
            this.previousPageButton = previousPageButton;
            this.nextPageButton = nextPageButton;
            this.pageLabel = pageLabel;
            this.selectionLabel = selectionLabel;
            controlsWired = false;
            WireControls();
            RefreshList();
        }

        public void ResolveRuntimeReferences()
        {
            Transform root = transform.Find("Panel") ?? transform;
            bool changed = false;

            if (NeedsRefresh(entryButtons, MaxEntries))
            {
                entryButtons = FindEntryButtons(root);
                changed |= entryButtons.Length > 0;
            }

            if (NeedsRefresh(entryLabels, MaxEntries))
            {
                entryLabels = FindEntryLabels(root);
                changed |= entryLabels.Length > 0;
            }

            changed |= AssignIfMissing(ref loadButton, FindChildComponent<Button>(root, "Load Button"));
            changed |= AssignIfMissing(ref detailsButton, FindChildComponent<Button>(root, "Details Button"));
            changed |= AssignIfMissing(ref cancelButton, FindChildComponent<Button>(root, "Cancel Button"));
            changed |= AssignIfMissing(ref previousPageButton, FindChildComponent<Button>(root, "Previous Page Button"));
            changed |= AssignIfMissing(ref nextPageButton, FindChildComponent<Button>(root, "Next Page Button"));
            changed |= AssignIfMissing(ref pageLabel, FindChildComponent<TMP_Text>(root, "Page"));
            changed |= AssignIfMissing(ref selectionLabel, FindChildComponent<TMP_Text>(root, "Selection"));

            if (changed)
                controlsWired = false;

            WireControls();
            RefreshList();
        }

        public void SetSaves(IEnumerable<WorldSaveSummary> saves)
        {
            ResolveRuntimeReferences();
            model.SetSaves(saves);
            pageIndex = 0;
            RefreshList();
        }

        public void SetStatus(string message)
        {
            ResolveRuntimeReferences();
            if (selectionLabel != null)
                selectionLabel.text = message ?? string.Empty;
        }

        void Awake()
        {
            ResolveRuntimeReferences();
        }

        void WireControls()
        {
            if (controlsWired)
                return;

            entryButtons ??= Array.Empty<Button>();

            for (int i = 0; i < entryButtons.Length; i++)
            {
                int idx = i;
                entryButtons[idx]?.onClick.AddListener(() => OnEntryClicked(idx));
            }

            loadButton?.onClick.AddListener(() => ActionRequested?.Invoke(MenuActions.LoadWorldLoad));
            detailsButton?.onClick.AddListener(() => ActionRequested?.Invoke(MenuActions.LoadWorldDetails));
            cancelButton?.onClick.AddListener(() => ActionRequested?.Invoke(MenuActions.LoadWorldCancel));
            previousPageButton?.onClick.AddListener(() => ChangePage(-1));
            nextPageButton?.onClick.AddListener(() => ChangePage(1));
            controlsWired = true;
        }

        void OnEntryClicked(int idx)
        {
            IReadOnlyList<WorldSaveSummary> visible = model.VisibleSaves;
            int saveIndex = pageIndex * MaxEntries + idx;
            if (saveIndex < visible.Count)
            {
                model.Select(visible[saveIndex].Name);
                RefreshSelection();
            }
        }

        void ChangePage(int delta)
        {
            IReadOnlyList<WorldSaveSummary> visible = model.VisibleSaves;
            int pageCount = PageCountFor(visible.Count);
            int nextPage = Mathf.Clamp(pageIndex + delta, 0, pageCount - 1);
            if (nextPage == pageIndex)
                return;

            pageIndex = nextPage;
            SelectFirstOnCurrentPage(visible);
            RefreshList();
        }

        void RefreshList()
        {
            if (entryButtons == null)
                entryButtons = Array.Empty<Button>();

            IReadOnlyList<WorldSaveSummary> visible = model.VisibleSaves;
            int pageCount = PageCountFor(visible.Count);
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            int firstVisibleIndex = pageIndex * MaxEntries;

            for (int i = 0; i < entryButtons.Length; i++)
            {
                int saveIndex = firstVisibleIndex + i;
                bool hasEntry = saveIndex < visible.Count;

                if (entryButtons[i] != null)
                    entryButtons[i].gameObject.SetActive(hasEntry);

                if (entryLabels != null && i < entryLabels.Length && entryLabels[i] != null)
                    entryLabels[i].text = hasEntry
                        ? BlockiverseLocalization.Format(
                            BlockiverseLocalization.Keys.LoadWorldEntry,
                            visible[saveIndex].Name,
                            visible[saveIndex].DayCount)
                        : string.Empty;
            }

            RefreshPaging(visible.Count);
            RefreshSelection();
        }

        void SelectFirstOnCurrentPage(IReadOnlyList<WorldSaveSummary> visible)
        {
            int firstVisibleIndex = pageIndex * MaxEntries;
            if (firstVisibleIndex < visible.Count)
                model.Select(visible[firstVisibleIndex].Name);
        }

        void RefreshPaging(int visibleCount)
        {
            int pageCount = PageCountFor(visibleCount);
            bool hasMultiplePages = visibleCount > MaxEntries;

            if (previousPageButton != null)
            {
                previousPageButton.gameObject.SetActive(hasMultiplePages);
                previousPageButton.interactable = pageIndex > 0;
            }

            if (nextPageButton != null)
            {
                nextPageButton.gameObject.SetActive(hasMultiplePages);
                nextPageButton.interactable = pageIndex < pageCount - 1;
            }

            if (pageLabel != null)
            {
                pageLabel.gameObject.SetActive(hasMultiplePages);
                pageLabel.text = BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LoadWorldPage,
                    pageIndex + 1,
                    pageCount);
            }
        }

        void RefreshSelection()
        {
            bool hasSave = model.SelectedSave.HasValue;
            if (selectionLabel != null)
                selectionLabel.text = hasSave
                    ? model.SelectedSave.Value.Name
                    : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LoadWorldNoSaveSelected);
            if (loadButton != null)
                loadButton.interactable = hasSave;
            if (detailsButton != null)
                detailsButton.interactable = hasSave;
        }

        static int PageCountFor(int visibleCount) => Mathf.Max(1, Mathf.CeilToInt(visibleCount / (float)MaxEntries));

        static Button[] FindEntryButtons(Transform root)
        {
            var buttons = new Button[MaxEntries];
            for (int i = 0; i < MaxEntries; i++)
                buttons[i] = FindChildComponent<Button>(root, $"Save {i + 1}");
            return buttons;
        }

        static TMP_Text[] FindEntryLabels(Transform root)
        {
            var labels = new TMP_Text[MaxEntries];
            for (int i = 0; i < MaxEntries; i++)
                labels[i] = FindChildComponent<TMP_Text>(root, $"Save {i + 1}");
            return labels;
        }

        static T FindChildComponent<T>(Transform root, string path) where T : Component
        {
            Transform child = root != null ? root.Find(path) : null;
            return child != null ? child.GetComponent<T>() : null;
        }

        static bool AssignIfMissing<T>(ref T target, T value) where T : Component
        {
            if (target != null || value == null)
                return false;

            target = value;
            return true;
        }

        static bool NeedsRefresh<T>(T[] values, int expectedLength) where T : class
        {
            if (values == null || values.Length != expectedLength || values.Length == 0)
                return true;

            for (int i = 0; i < values.Length; i++)
                if (values[i] == null)
                    return true;
            return false;
        }
    }
}

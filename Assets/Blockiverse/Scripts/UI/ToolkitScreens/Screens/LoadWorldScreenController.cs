using System;
using System.Collections.Generic;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of BlockiverseLoadWorldPanel (voxel_survival_menus §6.4): a fixed
    // six-row page over SaveListModel. The behaviour is copied from the uGUI panel
    // deliberately, not redesigned — same paging clamps, same first-save auto-selection on a
    // page change, same row/page/selection strings through the same localization keys — so
    // the two backends stay interchangeable during the coexistence window. Like the uGUI
    // panel, entry clicks select without loading, and Load/Details are inert without a
    // selection.
    [UiToolkitScreen(
        MenuActions.LoadWorldScreen,
        "Assets/Blockiverse/UI/Documents/LoadWorldScreen.uxml",
        900,
        894,
        UiToolkitPlacementProfile.Menu)]
    public sealed class LoadWorldScreenController : UiToolkitScreenController, IUiToolkitSaveListScreen, IUiToolkitStatusScreen
    {
        public const int MaxEntries = 6;

        public const string SaveEntryNamePrefix = "bv-save-";
        public const string PreviousPageButtonName = "bv-previous-page";
        public const string NextPageButtonName = "bv-next-page";
        public const string PageLabelName = "bv-page-label";
        public const string LoadButtonName = "bv-load";
        public const string DetailsButtonName = "bv-details";
        public const string CancelButtonName = "bv-cancel";
        public const string StatusLabelName = "bv-status";

        public const string SelectedRowClassName = "hs-button--selected";

        readonly SaveListModel model = new();
        readonly Button[] entryButtons = new Button[MaxEntries];
        Button previousPageButton;
        Button nextPageButton;
        Label pageLabel;
        Button loadButton;
        Button detailsButton;
        Button cancelButton;
        Label statusLabel;
        int pageIndex;

        public override string ScreenId => MenuActions.LoadWorldScreen;

        public WorldSaveSummary? SelectedSave => model.SelectedSave;
        public int PageIndex => pageIndex;
        public int PageCount => PageCountFor(model.VisibleSaves.Count);

        public void SetSaves(IEnumerable<WorldSaveSummary> saves)
        {
            model.SetSaves(saves);
            pageIndex = 0;
            RefreshList();
        }

        public void SetStatus(string message)
        {
            if (statusLabel != null)
                statusLabel.text = message ?? string.Empty;
        }

        // Entry click: selects without loading, exactly like the uGUI save rows. Public seam —
        // EditMode tests cannot raise ClickEvent without a live panel.
        public void SelectEntry(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MaxEntries)
                return;

            IReadOnlyList<WorldSaveSummary> visible = model.VisibleSaves;
            int saveIndex = pageIndex * MaxEntries + slotIndex;

            if (saveIndex >= visible.Count)
                return;

            model.Select(visible[saveIndex].Name);
            RefreshSelection();
        }

        public void ChangePage(int delta)
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

        // Guarded like the uGUI buttons' interactable gate: Load/Details without a selection
        // must not dispatch, however the request arrives.
        public void RequestLoad()
        {
            if (!model.SelectedSave.HasValue)
                return;

            DispatchAction(MenuActions.LoadWorldLoad);
        }

        public void RequestDetails()
        {
            if (!model.SelectedSave.HasValue)
                return;

            DispatchAction(MenuActions.LoadWorldDetails);
        }

        public void RequestCancel() => DispatchAction(MenuActions.LoadWorldCancel);

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            for (int i = 0; i < MaxEntries; i++)
                entryButtons[i] = Require<Button>(root, SaveEntryNamePrefix + (i + 1), ref allFound);

            previousPageButton = Require<Button>(root, PreviousPageButtonName, ref allFound);
            nextPageButton = Require<Button>(root, NextPageButtonName, ref allFound);
            pageLabel = Require<Label>(root, PageLabelName, ref allFound);
            loadButton = Require<Button>(root, LoadButtonName, ref allFound);
            detailsButton = Require<Button>(root, DetailsButtonName, ref allFound);
            cancelButton = Require<Button>(root, CancelButtonName, ref allFound);
            statusLabel = Require<Label>(root, StatusLabelName, ref allFound);

            // The tree is brand new on every attach; render the retained model state into it.
            RefreshList();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            for (int i = 0; i < MaxEntries; i++)
                entryButtons[i]?.RegisterCallback<ClickEvent, int>(OnEntryClicked, i);

            previousPageButton?.RegisterCallback<ClickEvent>(OnPreviousPageClicked);
            nextPageButton?.RegisterCallback<ClickEvent>(OnNextPageClicked);
            loadButton?.RegisterCallback<ClickEvent>(OnLoadClicked);
            detailsButton?.RegisterCallback<ClickEvent>(OnDetailsClicked);
            cancelButton?.RegisterCallback<ClickEvent>(OnCancelClicked);

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            for (int i = 0; i < MaxEntries; i++)
                entryButtons[i]?.UnregisterCallback<ClickEvent, int>(OnEntryClicked);

            previousPageButton?.UnregisterCallback<ClickEvent>(OnPreviousPageClicked);
            nextPageButton?.UnregisterCallback<ClickEvent>(OnNextPageClicked);
            loadButton?.UnregisterCallback<ClickEvent>(OnLoadClicked);
            detailsButton?.UnregisterCallback<ClickEvent>(OnDetailsClicked);
            cancelButton?.UnregisterCallback<ClickEvent>(OnCancelClicked);

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            for (int i = 0; i < MaxEntries; i++)
                entryButtons[i] = null;

            previousPageButton = null;
            nextPageButton = null;
            pageLabel = null;
            loadButton = null;
            detailsButton = null;
            cancelButton = null;
            statusLabel = null;
        }

        void OnEntryClicked(ClickEvent evt, int slotIndex) => SelectEntry(slotIndex);

        void OnPreviousPageClicked(ClickEvent evt) => ChangePage(-1);

        void OnNextPageClicked(ClickEvent evt) => ChangePage(1);

        void OnLoadClicked(ClickEvent evt) => RequestLoad();

        void OnDetailsClicked(ClickEvent evt) => RequestDetails();

        void OnCancelClicked(ClickEvent evt) => RequestCancel();

        // Rows, page counter and selection status are dynamic strings cached in element text;
        // static bindings update natively on locale change but these must be re-rendered.
        void OnSelectedLocaleChanged(Locale locale) => RefreshList();

        void RefreshList()
        {
            IReadOnlyList<WorldSaveSummary> visible = model.VisibleSaves;
            int pageCount = PageCountFor(visible.Count);
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            int firstVisibleIndex = pageIndex * MaxEntries;

            for (int i = 0; i < MaxEntries; i++)
            {
                Button entry = entryButtons[i];

                if (entry == null)
                    continue;

                int saveIndex = firstVisibleIndex + i;
                bool hasEntry = saveIndex < visible.Count;

                entry.style.display = hasEntry ? DisplayStyle.Flex : DisplayStyle.None;
                entry.text = hasEntry
                    ? UiText.Format(
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
            DisplayStyle pagingDisplay = hasMultiplePages ? DisplayStyle.Flex : DisplayStyle.None;

            if (previousPageButton != null)
            {
                previousPageButton.style.display = pagingDisplay;
                previousPageButton.SetEnabled(pageIndex > 0);
            }

            if (nextPageButton != null)
            {
                nextPageButton.style.display = pagingDisplay;
                nextPageButton.SetEnabled(pageIndex < pageCount - 1);
            }

            if (pageLabel != null)
            {
                pageLabel.style.display = pagingDisplay;
                pageLabel.text = UiText.Format(
                    BlockiverseLocalization.Keys.LoadWorldPage,
                    pageIndex + 1,
                    pageCount);
            }
        }

        void RefreshSelection()
        {
            bool hasSave = model.SelectedSave.HasValue;
            string selectedName = hasSave ? model.SelectedSave.Value.Name : null;

            if (statusLabel != null)
                statusLabel.text = hasSave
                    ? selectedName
                    : UiText.Get(BlockiverseLocalization.Keys.LoadWorldNoSaveSelected);

            loadButton?.SetEnabled(hasSave);
            detailsButton?.SetEnabled(hasSave);

            IReadOnlyList<WorldSaveSummary> visible = model.VisibleSaves;
            int firstVisibleIndex = pageIndex * MaxEntries;

            for (int i = 0; i < MaxEntries; i++)
            {
                Button entry = entryButtons[i];

                if (entry == null)
                    continue;

                int saveIndex = firstVisibleIndex + i;
                bool isSelected = hasSave && saveIndex < visible.Count &&
                    string.Equals(visible[saveIndex].Name, selectedName, StringComparison.OrdinalIgnoreCase);
                entry.EnableInClassList(SelectedRowClassName, isSelected);
            }
        }

        static int PageCountFor(int visibleCount) =>
            Mathf.Max(1, Mathf.CeilToInt(visibleCount / (float)MaxEntries));
    }
}

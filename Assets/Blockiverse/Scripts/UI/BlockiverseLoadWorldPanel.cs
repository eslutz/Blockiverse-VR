using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // View layer for the Load World screen (voxel_survival_menus §6.4). Wraps SaveListModel;
    // up to MaxEntries save rows are shown. ActionRequested fires LoadWorldLoad or LoadWorldCancel.
    public sealed class BlockiverseLoadWorldPanel : MonoBehaviour
    {
        const int MaxEntries = 6;

        Button[] entryButtons;
        TMP_Text[] entryLabels;
        Button loadButton;
        Button cancelButton;
        TMP_Text selectionLabel;

        readonly SaveListModel model = new();

        public WorldSaveSummary? SelectedSave => model.SelectedSave;
        public event Action<string> ActionRequested;

        public void Configure(
            Button[] entryButtons,
            TMP_Text[] entryLabels,
            Button loadButton,
            Button cancelButton,
            TMP_Text selectionLabel)
        {
            this.entryButtons = entryButtons ?? Array.Empty<Button>();
            this.entryLabels = entryLabels ?? Array.Empty<TMP_Text>();
            this.loadButton = loadButton;
            this.cancelButton = cancelButton;
            this.selectionLabel = selectionLabel;
            WireControls();
            RefreshList();
        }

        public void SetSaves(IEnumerable<WorldSaveSummary> saves)
        {
            model.SetSaves(saves);
            RefreshList();
        }

        void WireControls()
        {
            for (int i = 0; i < entryButtons.Length; i++)
            {
                int idx = i;
                entryButtons[idx]?.onClick.AddListener(() => OnEntryClicked(idx));
            }

            loadButton?.onClick.AddListener(() => ActionRequested?.Invoke(MenuActions.LoadWorldLoad));
            cancelButton?.onClick.AddListener(() => ActionRequested?.Invoke(MenuActions.LoadWorldCancel));
        }

        void OnEntryClicked(int idx)
        {
            IReadOnlyList<WorldSaveSummary> visible = model.VisibleSaves;
            if (idx < visible.Count)
            {
                model.Select(visible[idx].Name);
                RefreshSelection();
            }
        }

        void RefreshList()
        {
            IReadOnlyList<WorldSaveSummary> visible = model.VisibleSaves;

            for (int i = 0; i < entryButtons.Length; i++)
            {
                bool hasEntry = i < visible.Count;

                if (entryButtons[i] != null)
                    entryButtons[i].gameObject.SetActive(hasEntry);

                if (entryLabels != null && i < entryLabels.Length && entryLabels[i] != null)
                    entryLabels[i].text = hasEntry ? $"{visible[i].Name}  ·  Day {visible[i].DayCount}" : string.Empty;
            }

            RefreshSelection();
        }

        void RefreshSelection()
        {
            bool hasSave = model.SelectedSave.HasValue;
            if (selectionLabel != null)
                selectionLabel.text = hasSave ? model.SelectedSave.Value.Name : "No save selected";
            if (loadButton != null)
                loadButton.interactable = hasSave;
        }
    }
}

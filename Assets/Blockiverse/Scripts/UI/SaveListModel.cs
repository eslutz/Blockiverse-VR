using System;
using System.Collections.Generic;
using System.Linq;

namespace Blockiverse.UI
{
    // A summary row for the Load World list (voxel_survival_menus §6.4). A DTO populated from save
    // manifests; the list model only sorts, filters, and selects.
    public readonly struct WorldSaveSummary
    {
        public WorldSaveSummary(string name, string seed, string gameMode, string difficulty, int dayCount, DateTime lastPlayedUtc, DateTime createdUtc)
        {
            Name = name ?? string.Empty;
            Seed = seed ?? string.Empty;
            GameMode = gameMode ?? string.Empty;
            Difficulty = difficulty ?? string.Empty;
            DayCount = dayCount;
            LastPlayedUtc = lastPlayedUtc;
            CreatedUtc = createdUtc;
        }

        public string Name { get; }
        public string Seed { get; }
        public string GameMode { get; }
        public string Difficulty { get; }
        public int DayCount { get; }
        public DateTime LastPlayedUtc { get; }
        public DateTime CreatedUtc { get; }
    }

    public enum SaveSortMode
    {
        LastPlayed,
        Name,
        Day,
        Mode,
        Created,
    }

    // Sort/filter/select model behind the Load World menu (§6.4). VisibleSaves applies the search
    // filter then the sort; selection is kept stable across re-sorts when the selected save remains
    // visible.
    public sealed class SaveListModel
    {
        readonly List<WorldSaveSummary> saves = new();

        public SaveListModel(IEnumerable<WorldSaveSummary> initialSaves = null)
        {
            if (initialSaves != null)
                saves.AddRange(initialSaves);
            SelectFirst();
        }

        public SaveSortMode SortMode { get; private set; } = SaveSortMode.LastPlayed;
        public string SearchText { get; private set; } = string.Empty;
        public WorldSaveSummary? SelectedSave { get; private set; }
        public bool HasSaves => saves.Count > 0;

        public IReadOnlyList<WorldSaveSummary> VisibleSaves => BuildVisible();

        public void SetSaves(IEnumerable<WorldSaveSummary> updatedSaves)
        {
            saves.Clear();
            if (updatedSaves != null)
                saves.AddRange(updatedSaves);
            SelectFirst();
        }

        public void SetSort(SaveSortMode mode)
        {
            SortMode = mode;
            KeepSelectionVisibleOrReset();
        }

        public void SetSearch(string searchText)
        {
            SearchText = searchText?.Trim() ?? string.Empty;
            KeepSelectionVisibleOrReset();
        }

        public bool Select(string name)
        {
            foreach (WorldSaveSummary save in BuildVisible())
            {
                if (string.Equals(save.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedSave = save;
                    return true;
                }
            }

            return false;
        }

        public void SelectFirst()
        {
            IReadOnlyList<WorldSaveSummary> visible = BuildVisible();
            SelectedSave = visible.Count > 0 ? visible[0] : (WorldSaveSummary?)null;
        }

        void KeepSelectionVisibleOrReset()
        {
            if (SelectedSave.HasValue && BuildVisible().Any(s => string.Equals(s.Name, SelectedSave.Value.Name, StringComparison.OrdinalIgnoreCase)))
                return;

            SelectFirst();
        }

        List<WorldSaveSummary> BuildVisible()
        {
            IEnumerable<WorldSaveSummary> filtered = saves;

            if (!string.IsNullOrEmpty(SearchText))
            {
                filtered = filtered.Where(s =>
                    Contains(s.Name, SearchText) ||
                    Contains(s.Seed, SearchText) ||
                    Contains(s.GameMode, SearchText));
            }

            return SortMode switch
            {
                SaveSortMode.Name => filtered.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                SaveSortMode.Day => filtered.OrderByDescending(s => s.DayCount).ToList(),
                SaveSortMode.Mode => filtered.OrderBy(s => s.GameMode, StringComparer.OrdinalIgnoreCase).ToList(),
                SaveSortMode.Created => filtered.OrderByDescending(s => s.CreatedUtc).ToList(),
                _ => filtered.OrderByDescending(s => s.LastPlayedUtc).ToList(),
            };
        }

        static bool Contains(string value, string term) =>
            value != null && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

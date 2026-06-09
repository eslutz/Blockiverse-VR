using System;
using System.Linq;
using Blockiverse.UI;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SaveListModelEditModeTests
    {
        static WorldSaveSummary[] Sample()
        {
            var baseTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            return new[]
            {
                new WorldSaveSummary("Meadow Home", "918273645", "survival", "normal", 18, baseTime.AddDays(5), baseTime),
                new WorldSaveSummary("Salt Flats Test", "555", "survival", "easy", 4, baseTime.AddDays(2), baseTime.AddDays(1)),
                new WorldSaveSummary("Deep Cave Run", "deepcave", "creative", "hard", 33, baseTime.AddDays(9), baseTime.AddDays(3)),
            };
        }

        [Test]
        public void DefaultSortIsLastPlayedAndSelectsFirst()
        {
            var model = new SaveListModel(Sample());

            Assert.That(model.SortMode, Is.EqualTo(SaveSortMode.LastPlayed));
            Assert.That(model.VisibleSaves.Select(s => s.Name).ToArray(),
                Is.EqualTo(new[] { "Deep Cave Run", "Meadow Home", "Salt Flats Test" }));
            Assert.That(model.SelectedSave.Value.Name, Is.EqualTo("Deep Cave Run"));
            Assert.That(model.HasSaves, Is.True);
        }

        [Test]
        public void SortByNameAndDayReorderTheList()
        {
            var model = new SaveListModel(Sample());

            model.SetSort(SaveSortMode.Name);
            Assert.That(model.VisibleSaves.Select(s => s.Name).ToArray(),
                Is.EqualTo(new[] { "Deep Cave Run", "Meadow Home", "Salt Flats Test" }));

            model.SetSort(SaveSortMode.Day);
            Assert.That(model.VisibleSaves.First().Name, Is.EqualTo("Deep Cave Run")); // day 33
            Assert.That(model.VisibleSaves.Last().Name, Is.EqualTo("Salt Flats Test")); // day 4
        }

        [Test]
        public void SearchFiltersByNameSeedAndMode()
        {
            var model = new SaveListModel(Sample());

            model.SetSearch("meadow");
            Assert.That(model.VisibleSaves.Select(s => s.Name).ToArray(), Is.EqualTo(new[] { "Meadow Home" }));

            model.SetSearch("creative");
            Assert.That(model.VisibleSaves.Select(s => s.Name).ToArray(), Is.EqualTo(new[] { "Deep Cave Run" }));

            model.SetSearch("555");
            Assert.That(model.VisibleSaves.Select(s => s.Name).ToArray(), Is.EqualTo(new[] { "Salt Flats Test" }));

            model.SetSearch("");
            Assert.That(model.VisibleSaves.Count, Is.EqualTo(3));
        }

        [Test]
        public void SelectionPersistsAcrossSortAndResetsWhenFilteredOut()
        {
            var model = new SaveListModel(Sample());
            Assert.That(model.Select("Salt Flats Test"), Is.True);

            model.SetSort(SaveSortMode.Name);
            Assert.That(model.SelectedSave.Value.Name, Is.EqualTo("Salt Flats Test"), "Selection should survive a re-sort.");

            model.SetSearch("deep"); // filters out the selected save
            Assert.That(model.SelectedSave.Value.Name, Is.EqualTo("Deep Cave Run"), "Selection resets to first visible when filtered out.");
        }

        [Test]
        public void EmptyListHasNoSelection()
        {
            var model = new SaveListModel();

            Assert.That(model.HasSaves, Is.False);
            Assert.That(model.SelectedSave.HasValue, Is.False);
            Assert.That(model.Select("anything"), Is.False);
        }
    }
}

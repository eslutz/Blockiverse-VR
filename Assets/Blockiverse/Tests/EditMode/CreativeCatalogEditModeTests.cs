using System;
using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class CreativeCatalogEditModeTests
    {
        [Test]
        public void AllThirteenCategoriesHaveAtLeastOneEntry()
        {
            CreativeCatalog catalog = CreativeCatalog.CreateDefault();

            foreach (CreativeCatalogCategory category in Enum.GetValues(typeof(CreativeCatalogCategory)))
            {
                Assert.That(
                    catalog.InCategory(category).Any(),
                    Is.True,
                    $"Category {category} has no entries in the default catalog.");
            }
        }

        [Test]
        public void CatalogEnumHasExactlyThirteenValues()
        {
            int count = Enum.GetValues(typeof(CreativeCatalogCategory)).Length;
            Assert.That(count, Is.EqualTo(13));
        }

        [Test]
        public void AllCanonicalBlockIdsResolveInDefaultRegistry()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            CreativeCatalog catalog = CreativeCatalog.CreateDefault(registry);

            foreach (CreativeCatalogEntry entry in catalog.All)
            {
                Assert.DoesNotThrow(
                    () => registry.Get(entry.BlockId),
                    $"Block ID {entry.BlockId} in catalog category {entry.Category} does not resolve in the default registry.");
            }
        }

        [Test]
        public void NoDuplicateBlockIdsInCatalog()
        {
            CreativeCatalog catalog = CreativeCatalog.CreateDefault();

            BlockId[] ids = catalog.All.Select(e => e.BlockId).ToArray();
            int uniqueCount = ids.Distinct().Count();

            Assert.That(uniqueCount, Is.EqualTo(ids.Length),
                "Catalog contains duplicate block ID entries.");
        }

        [Test]
        public void CatalogDoesNotContainAirBlock()
        {
            CreativeCatalog catalog = CreativeCatalog.CreateDefault();

            Assert.That(
                catalog.All.Any(e => e.BlockId == BlockRegistry.Air),
                Is.False,
                "Catalog must not include the Air block.");
        }

        [Test]
        public void InCategoryReturnsOnlyEntriesForThatCategory()
        {
            CreativeCatalog catalog = CreativeCatalog.CreateDefault();

            foreach (CreativeCatalogCategory category in Enum.GetValues(typeof(CreativeCatalogCategory)))
            {
                foreach (CreativeCatalogEntry entry in catalog.InCategory(category))
                    Assert.That(entry.Category, Is.EqualTo(category));
            }
        }
    }
}

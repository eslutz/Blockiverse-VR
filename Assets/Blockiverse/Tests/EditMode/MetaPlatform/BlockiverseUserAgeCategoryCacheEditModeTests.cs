using Blockiverse.MetaPlatform;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.MetaPlatform
{
    public sealed class BlockiverseUserAgeCategoryCacheEditModeTests
    {
        [SetUp]
        public void SetUp()
        {
            BlockiverseUserAgeCategoryCache.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            BlockiverseUserAgeCategoryCache.ClearForTests();
        }

        [Test]
        public void EmptyCacheReturnsFalse()
        {
            Assert.That(BlockiverseUserAgeCategoryCache.TryLoad(out _), Is.False);
        }

        [Test]
        public void KnownCategoryRoundTripsAsCached()
        {
            var live = new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Teen,
                BlockiverseUserAgeCategorySource.LiveApi,
                1234,
                "live");

            BlockiverseUserAgeCategoryCache.Save(live);

            Assert.That(BlockiverseUserAgeCategoryCache.TryLoad(out var cached), Is.True);
            Assert.That(cached.Category, Is.EqualTo(BlockiverseUserAgeCategory.Teen));
            Assert.That(cached.Source, Is.EqualTo(BlockiverseUserAgeCategorySource.Cached));
            Assert.That(cached.UnixSeconds, Is.EqualTo(1234));
        }

        [Test]
        public void UnknownCategoryIsNotCached()
        {
            BlockiverseUserAgeCategoryCache.Save(BlockiverseUserAgeCategoryState.Unknown(
                BlockiverseUserAgeCategorySource.LiveApi,
                "unknown"));

            Assert.That(BlockiverseUserAgeCategoryCache.TryLoad(out _), Is.False);
        }
    }
}

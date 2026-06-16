using Blockiverse.MetaPlatform;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.MetaPlatform
{
    public sealed class BlockiverseUserAgeCategoryPolicyEditModeTests
    {
        [Test]
        public void ChildAvoidsMetaProfileLookup()
        {
            Assert.That(
                BlockiversePlatformFeaturePolicy.ShouldAvoidMetaProfileLookup(BlockiverseUserAgeCategory.Child),
                Is.True);
        }

        [Test]
        public void TeenAdultAndUnknownDoNotBlockBaseGame()
        {
            Assert.That(
                BlockiversePlatformFeaturePolicy.ShouldKeepBaseGamePlayable(BlockiverseUserAgeCategory.Unknown),
                Is.True);
            Assert.That(
                BlockiversePlatformFeaturePolicy.ShouldKeepBaseGamePlayable(BlockiverseUserAgeCategory.Teen),
                Is.True);
            Assert.That(
                BlockiversePlatformFeaturePolicy.ShouldKeepBaseGamePlayable(BlockiverseUserAgeCategory.Adult),
                Is.True);
        }

        [Test]
        public void ChildSocialFeaturesAreDisabled()
        {
            Assert.That(
                BlockiversePlatformFeaturePolicy.CanUseMetaSocialFeature(BlockiverseUserAgeCategory.Child),
                Is.False);
            Assert.That(
                BlockiversePlatformFeaturePolicy.CanUseMetaSocialFeature(BlockiverseUserAgeCategory.Teen),
                Is.True);
            Assert.That(
                BlockiversePlatformFeaturePolicy.CanUseMetaSocialFeature(BlockiverseUserAgeCategory.Adult),
                Is.True);
        }
    }
}

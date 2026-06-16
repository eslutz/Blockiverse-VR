using Blockiverse.MetaAvatars;
using Blockiverse.MetaPlatform;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.MetaAvatars.EditMode
{
    public sealed class MetaHorizonAvatarProviderAgeGateEditModeTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            BlockiverseUserAgeCategoryService.ResetForTests();

            if (root != null)
                Object.DestroyImmediate(root);
        }

        [Test]
        public void LoggedInUserAvatarLookupIsDisabledForChildAccounts()
        {
            MetaHorizonAvatarProvider provider = CreateProvider();
            BlockiverseUserAgeCategoryService.SetCurrentForTests(new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Child,
                BlockiverseUserAgeCategorySource.LiveApi,
                1,
                "child"));

            Assert.That(provider.CanRequestLoggedInUserAvatarForCurrentAgeCategory(out string reason), Is.False);
            Assert.That(reason, Does.Contain("child"));
        }

        [Test]
        public void LoggedInUserAvatarLookupRemainsAvailableForTeenAndAdultAccounts()
        {
            MetaHorizonAvatarProvider provider = CreateProvider();

            BlockiverseUserAgeCategoryService.SetCurrentForTests(new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Teen,
                BlockiverseUserAgeCategorySource.LiveApi,
                1,
                "teen"));
            Assert.That(provider.CanRequestLoggedInUserAvatarForCurrentAgeCategory(out string teenReason), Is.True);
            Assert.That(teenReason, Is.Empty);

            BlockiverseUserAgeCategoryService.SetCurrentForTests(new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Adult,
                BlockiverseUserAgeCategorySource.LiveApi,
                1,
                "adult"));
            Assert.That(provider.CanRequestLoggedInUserAvatarForCurrentAgeCategory(out string adultReason), Is.True);
            Assert.That(adultReason, Is.Empty);
        }

        MetaHorizonAvatarProvider CreateProvider()
        {
            root = new GameObject("Meta Horizon Avatar Provider Age Gate Test");
            return root.AddComponent<MetaHorizonAvatarProvider>();
        }
    }
}

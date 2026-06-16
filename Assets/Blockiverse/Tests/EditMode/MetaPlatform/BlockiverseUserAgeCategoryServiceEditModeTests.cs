using System;
using Blockiverse.MetaPlatform;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode.MetaPlatform
{
    public sealed class BlockiverseUserAgeCategoryServiceEditModeTests
    {
        GameObject serviceObject;

        [SetUp]
        public void SetUp()
        {
            BlockiverseUserAgeCategoryCache.ClearForTests();
            BlockiverseUserAgeCategoryService.ResetForTests();
            serviceObject = new GameObject("Age Category Service Test");
            serviceObject.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            if (serviceObject != null)
                UnityEngine.Object.DestroyImmediate(serviceObject);

            BlockiverseUserAgeCategoryCache.ClearForTests();
            BlockiverseUserAgeCategoryService.ResetForTests();
        }

        [Test]
        public void RefreshRequestsOnlyOncePerSession()
        {
            var fakeClient = new FakeClient(new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Adult,
                BlockiverseUserAgeCategorySource.LiveApi,
                1,
                "ok"));
            BlockiverseUserAgeCategoryService service = serviceObject.AddComponent<BlockiverseUserAgeCategoryService>();
            service.ConfigureForTests(fakeClient);
            serviceObject.SetActive(true);

            service.RefreshOncePerSession();
            service.RefreshOncePerSession();

            Assert.That(fakeClient.CallCount, Is.EqualTo(1));
            Assert.That(BlockiverseUserAgeCategoryService.Current.Category,
                Is.EqualTo(BlockiverseUserAgeCategory.Adult));
        }

        [Test]
        public void ErrorUsesCachedKnownCategory()
        {
            BlockiverseUserAgeCategoryCache.Save(new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Teen,
                BlockiverseUserAgeCategorySource.LiveApi,
                1,
                "ok"));

            BlockiverseUserAgeCategoryService service = serviceObject.AddComponent<BlockiverseUserAgeCategoryService>();
            service.ConfigureForTests(new FakeClient(BlockiverseUserAgeCategoryState.Unknown(
                BlockiverseUserAgeCategorySource.Error,
                "failed")));
            serviceObject.SetActive(true);

            service.RefreshOncePerSession();

            Assert.That(BlockiverseUserAgeCategoryService.Current.Category,
                Is.EqualTo(BlockiverseUserAgeCategory.Teen));
            Assert.That(BlockiverseUserAgeCategoryService.Current.Source,
                Is.EqualTo(BlockiverseUserAgeCategorySource.Cached));
        }

        sealed class FakeClient : IUserAgeCategoryClient
        {
            readonly BlockiverseUserAgeCategoryState result;

            public FakeClient(BlockiverseUserAgeCategoryState result)
            {
                this.result = result;
            }

            public int CallCount { get; private set; }

            public void Get(Action<BlockiverseUserAgeCategoryState> completed)
            {
                CallCount++;
                completed(result);
            }
        }
    }
}

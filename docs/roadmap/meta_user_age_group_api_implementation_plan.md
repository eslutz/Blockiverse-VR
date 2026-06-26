# Meta User Age Group API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Mixed Ages release path compliant with Meta's User Age Group API requirements while keeping the base game playable when the API is offline, fails, or returns `UNKNOWN`.

**Architecture:** Add one Meta platform policy layer that retrieves `UserAgeCategory.Get()` once per Quest session, caches the last known category as a fallback, and exposes typed feature gates to gameplay, UI, and Meta Avatars code. Child accounts keep access to the base offline/LAN game experience but avoid child-restricted Meta platform calls such as profile/avatar lookup, future invites, friends, matchmaking, or other social SDK features until the current Meta policy review explicitly permits them.

**Tech Stack:** Unity `6000.3.18f1`, C#, Meta XR Core SDK `81.0.1`, Meta XR Platform SDK `81.0.1`, Oculus Platform `UserAgeCategory.Get()`, Unity Test Framework, `scripts/unity/run-tests.sh`, Horizon Debug Bridge `hzdb`.

---

## Implementation Status

Implemented in-repo on 2026-06-13 for the Mixed Ages path:

- Runtime age-category service, SDK adapter, cache, and policy assembly under `Assets/Blockiverse/Scripts/MetaPlatform/`.
- Boot scene generation through `BlockiverseProjectBootstrapper` with one `BlockiverseUserAgeCategoryService`.
- Meta avatar/profile lookup gate for child accounts plus fallback identity/avatar messaging in the LAN session menu.
- Focused EditMode coverage for policy/cache/service behavior, avatar gating, LAN notice behavior, and Boot-scene service wiring.
- Android development APK build succeeds with the `UNITY_ANDROID` Meta Platform adapter compiled into `Blockiverse.MetaPlatform.dll`.
- Store/privacy/data-use/VRC/ruleset/roadmap documentation aligned with the implemented behavior.

Remaining external release work: confirm the Meta Developer Dashboard self-certification, complete or update Data Use Checkup if prompted, validate CH/TN/AD/UNKNOWN behavior on Quest/release-channel accounts, install/launch on device once `hzdb` is available, and clear the current unrelated full-gate EditMode failures before PR merge or store submission.

---

## Compliance Finding

Checked on 2026-06-13 against Meta's current docs:

- Meta says age-group self-certification is mandatory for Quest release channels.
- Meta says `UserAgeCategory.Get()` is required only for apps self-certified as Mixed Ages.
- Meta says Mixed Ages apps should call the API at least once per user session when online, should not interrupt the user experience, should not block the app when offline or when the API fails, and may cache the last known age category as a fallback.
- Meta's Unity API requires Meta XR Platform SDK build version `56.0` or later. This repo currently has `com.meta.xr.sdk.platform` `81.0.1`, so the installed SDK is new enough.

Pre-implementation repo state:

- The Developer Dashboard screenshots show Mixed Ages selected, which makes the API required unless Eric re-certifies the app as Teens and Adults.
- `Packages/manifest.json` includes `com.meta.xr.sdk.platform` `81.0.1`.
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs` already called `Oculus.Platform.Core.Initialize()`, `Users.GetAccessToken()`, and `Users.GetLoggedInUser()` to load Meta Horizon avatars.
- No repo code called `Oculus.Platform.UserAgeCategory.Get()`.
- Store docs said the app was not directed at children, which conflicted with a Mixed Ages dashboard selection and had to be resolved before submission.

Conclusion: this plan was required for the Mixed Ages path and has now been implemented in the repo. The external dashboard, Data Use Checkup, and Quest account validation steps still have to be completed before release-channel or store submission.

## Source References

- Meta Unity Get Age Category API: <https://developers.meta.com/horizon/documentation/unity/ps-get-age-category-api/>
- Meta age group self-certification and youth requirements: <https://developers.meta.com/horizon/resources/age-groups/>
- Installed SDK surface:
  - `Library/PackageCache/com.meta.xr.sdk.platform@dfb2e10f4ace/Scripts/Platform.cs`
  - `Library/PackageCache/com.meta.xr.sdk.platform@dfb2e10f4ace/Scripts/AccountAgeCategory.cs`
  - `Library/PackageCache/com.meta.xr.sdk.platform@dfb2e10f4ace/Scripts/Models/UserAccountAgeCategory.cs`

## Product Decision Gate

Before coding, Eric must choose one path:

1. **Mixed Ages path:** keep the dashboard selection from the screenshots and implement this plan.
2. **Teens and Adults path:** change the Dashboard self-certification to Teens and Adults, update store/privacy docs, and skip the runtime API work unless the certification changes again.

The rest of this plan assumes path 1, Mixed Ages.

## File Structure

Create runtime policy files:

- `Assets/Blockiverse/Scripts/MetaPlatform/Blockiverse.MetaPlatform.asmdef`: new assembly for Meta platform compliance and user-age policy. References `Blockiverse.Core` and `Oculus.Platform`.
- `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategory.cs`: repo-owned enum for `Unknown`, `Child`, `Teen`, and `Adult`.
- `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategorySource.cs`: source enum for `None`, `LiveApi`, `Cached`, `Offline`, `Error`, and `UnsupportedRuntime`.
- `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategoryState.cs`: immutable state struct with category, source, timestamp, and message.
- `Assets/Blockiverse/Scripts/MetaPlatform/IUserAgeCategoryClient.cs`: test seam for the Meta Platform SDK call.
- `Assets/Blockiverse/Scripts/MetaPlatform/OculusUserAgeCategoryClient.cs`: production adapter for `Core.Initialize()` and `UserAgeCategory.Get()`.
- `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategoryCache.cs`: local `PlayerPrefs` fallback cache for the last known category.
- `Assets/Blockiverse/Scripts/MetaPlatform/BlockiversePlatformFeaturePolicy.cs`: child/teen/adult/unknown feature policy for Meta platform dependent features.
- `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategoryService.cs`: Boot-scene `MonoBehaviour` that queries once per session, runs Platform callbacks, and exposes the current state.

Modify runtime integration:

- `Assets/Blockiverse/Scripts/MetaAvatars/Blockiverse.MetaAvatars.asmdef`: add `Blockiverse.MetaPlatform`.
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs`: consult `BlockiversePlatformFeaturePolicy` before Meta profile/avatar access token and logged-in user calls.
- `Assets/Blockiverse/Scripts/UI/Blockiverse.UI.asmdef`: add `Blockiverse.MetaPlatform` only if UI text needs a current policy notice.
- `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`: keep LAN multiplayer nonblocking, but show a concise notice if a child account is using fallback identity and no Meta social features are available.
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs`: add the age service to the generated Boot scene.

Create tests:

- `Assets/Blockiverse/Tests/EditMode/MetaPlatform/Blockiverse.Tests.MetaPlatform.EditMode.asmdef`
- `Assets/Blockiverse/Tests/EditMode/MetaPlatform/BlockiverseUserAgeCategoryPolicyEditModeTests.cs`
- `Assets/Blockiverse/Tests/EditMode/MetaPlatform/BlockiverseUserAgeCategoryCacheEditModeTests.cs`
- `Assets/Blockiverse/Tests/EditMode/MetaPlatform/BlockiverseUserAgeCategoryServiceEditModeTests.cs`
- Extend `Assets/Blockiverse/Tests/EditMode/MetaAvatars/BlockiverseMetaAvatarPresenterEditModeTests.cs` or add `MetaHorizonAvatarProviderAgeGateEditModeTests.cs` if direct provider tests are practical without native avatar creation.
- Extend `Assets/Blockiverse/Tests/EditMode/BlockiverseBootstrapEditModeTests.cs` to verify the generated Boot scene contains exactly one `BlockiverseUserAgeCategoryService`.

Update docs:

- `docs/roadmap/blockiverse_vr_execution_plan.md`
- `docs/rulesets/voxel_multiplayer_networking_ruleset.md`
- `docs/store-submission/checklist.md`
- `docs/store-submission/data-and-safety.md`
- `docs/store-submission/privacy-policy.md`
- `docs/store-submission/vrc-checklist.md`
- `CHANGELOG.md`

## Task 1: Add The Meta Platform Policy Assembly

**Files:**

- Create: `Assets/Blockiverse/Scripts/MetaPlatform/Blockiverse.MetaPlatform.asmdef`
- Create: `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategory.cs`
- Create: `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategorySource.cs`
- Create: `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategoryState.cs`
- Create: `Assets/Blockiverse/Scripts/MetaPlatform/BlockiversePlatformFeaturePolicy.cs`
- Test: `Assets/Blockiverse/Tests/EditMode/MetaPlatform/BlockiverseUserAgeCategoryPolicyEditModeTests.cs`

- [x] **Step 1: Create the assembly**

Create `Assets/Blockiverse/Scripts/MetaPlatform/Blockiverse.MetaPlatform.asmdef`:

```json
{
  "name": "Blockiverse.MetaPlatform",
  "rootNamespace": "Blockiverse.MetaPlatform",
  "references": [
    "Blockiverse.Core",
    "Oculus.Platform"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [x] **Step 2: Add the repo-owned age enums and state**

Create `BlockiverseUserAgeCategory.cs`:

```csharp
namespace Blockiverse.MetaPlatform
{
    public enum BlockiverseUserAgeCategory
    {
        Unknown = 0,
        Child = 1,
        Teen = 2,
        Adult = 3,
    }
}
```

Create `BlockiverseUserAgeCategorySource.cs`:

```csharp
namespace Blockiverse.MetaPlatform
{
    public enum BlockiverseUserAgeCategorySource
    {
        None = 0,
        LiveApi = 1,
        Cached = 2,
        Offline = 3,
        Error = 4,
        UnsupportedRuntime = 5,
    }
}
```

Create `BlockiverseUserAgeCategoryState.cs`:

```csharp
using System;

namespace Blockiverse.MetaPlatform
{
    public readonly struct BlockiverseUserAgeCategoryState : IEquatable<BlockiverseUserAgeCategoryState>
    {
        public BlockiverseUserAgeCategoryState(
            BlockiverseUserAgeCategory category,
            BlockiverseUserAgeCategorySource source,
            long unixSeconds,
            string message)
        {
            Category = category;
            Source = source;
            UnixSeconds = unixSeconds;
            Message = message ?? string.Empty;
        }

        public BlockiverseUserAgeCategory Category { get; }
        public BlockiverseUserAgeCategorySource Source { get; }
        public long UnixSeconds { get; }
        public string Message { get; }

        public bool HasKnownCategory =>
            Category == BlockiverseUserAgeCategory.Child ||
            Category == BlockiverseUserAgeCategory.Teen ||
            Category == BlockiverseUserAgeCategory.Adult;

        public static BlockiverseUserAgeCategoryState Unknown(
            BlockiverseUserAgeCategorySource source,
            string message)
        {
            return new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Unknown,
                source,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                message);
        }

        public bool Equals(BlockiverseUserAgeCategoryState other) =>
            Category == other.Category &&
            Source == other.Source &&
            UnixSeconds == other.UnixSeconds &&
            Message == other.Message;

        public override bool Equals(object obj) =>
            obj is BlockiverseUserAgeCategoryState other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Category, Source, UnixSeconds, Message);
    }
}
```

- [x] **Step 3: Add the feature policy**

Create `BlockiversePlatformFeaturePolicy.cs`:

```csharp
namespace Blockiverse.MetaPlatform
{
    public static class BlockiversePlatformFeaturePolicy
    {
        public static bool ShouldAvoidMetaProfileLookup(BlockiverseUserAgeCategory category) =>
            category == BlockiverseUserAgeCategory.Child;

        public static bool CanUseMetaSocialFeature(BlockiverseUserAgeCategory category) =>
            category != BlockiverseUserAgeCategory.Child;

        public static bool ShouldKeepBaseGamePlayable(BlockiverseUserAgeCategory category) =>
            true;

        public static string AvatarFallbackReason(BlockiverseUserAgeCategoryState state)
        {
            if (state.Category == BlockiverseUserAgeCategory.Child)
                return "Meta profile avatar is unavailable for child accounts; fallback avatar remains active.";

            if (state.Category == BlockiverseUserAgeCategory.Unknown)
                return "Meta age category is unavailable; fallback avatar remains active until platform services are ready.";

            return string.Empty;
        }
    }
}
```

- [x] **Step 4: Add policy tests**

Create `Assets/Blockiverse/Tests/EditMode/MetaPlatform/Blockiverse.Tests.MetaPlatform.EditMode.asmdef`:

```json
{
  "name": "Blockiverse.Tests.MetaPlatform.EditMode",
  "rootNamespace": "Blockiverse.Tests.EditMode.MetaPlatform",
  "references": [
    "Blockiverse.MetaPlatform",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [
    "nunit.framework.dll"
  ],
  "autoReferenced": false,
  "defineConstraints": [
    "UNITY_INCLUDE_TESTS"
  ],
  "versionDefines": [],
  "noEngineReferences": false
}
```

Create `BlockiverseUserAgeCategoryPolicyEditModeTests.cs`:

```csharp
using Blockiverse.MetaPlatform;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.MetaPlatform
{
    public sealed class BlockiverseUserAgeCategoryPolicyEditModeTests
    {
        [Test]
        public void ChildAvoidsMetaProfileLookup()
        {
            Assert.That(BlockiversePlatformFeaturePolicy.ShouldAvoidMetaProfileLookup(
                BlockiverseUserAgeCategory.Child), Is.True);
        }

        [Test]
        public void TeenAdultAndUnknownDoNotBlockBaseGame()
        {
            Assert.That(BlockiversePlatformFeaturePolicy.ShouldKeepBaseGamePlayable(
                BlockiverseUserAgeCategory.Unknown), Is.True);
            Assert.That(BlockiversePlatformFeaturePolicy.ShouldKeepBaseGamePlayable(
                BlockiverseUserAgeCategory.Teen), Is.True);
            Assert.That(BlockiversePlatformFeaturePolicy.ShouldKeepBaseGamePlayable(
                BlockiverseUserAgeCategory.Adult), Is.True);
        }

        [Test]
        public void ChildSocialFeaturesAreDisabled()
        {
            Assert.That(BlockiversePlatformFeaturePolicy.CanUseMetaSocialFeature(
                BlockiverseUserAgeCategory.Child), Is.False);
            Assert.That(BlockiversePlatformFeaturePolicy.CanUseMetaSocialFeature(
                BlockiverseUserAgeCategory.Teen), Is.True);
            Assert.That(BlockiversePlatformFeaturePolicy.CanUseMetaSocialFeature(
                BlockiverseUserAgeCategory.Adult), Is.True);
        }
    }
}
```

- [x] **Step 5: Run the new policy tests**

Run:

```bash
scripts/unity/run-tests.sh --platform EditMode \
  --filter Blockiverse.Tests.EditMode.MetaPlatform.BlockiverseUserAgeCategoryPolicyEditModeTests \
  --results-name MetaPlatformPolicy
```

Expected: the new tests pass and write `TestResults/Unity/MetaPlatformPolicy.xml`.

## Task 2: Add The User Age Category API Client And Cache

**Files:**

- Create: `Assets/Blockiverse/Scripts/MetaPlatform/IUserAgeCategoryClient.cs`
- Create: `Assets/Blockiverse/Scripts/MetaPlatform/OculusUserAgeCategoryClient.cs`
- Create: `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategoryCache.cs`
- Test: `Assets/Blockiverse/Tests/EditMode/MetaPlatform/BlockiverseUserAgeCategoryCacheEditModeTests.cs`

- [x] **Step 1: Add the client interface**

Create `IUserAgeCategoryClient.cs`:

```csharp
using System;

namespace Blockiverse.MetaPlatform
{
    public interface IUserAgeCategoryClient
    {
        void Get(Action<BlockiverseUserAgeCategoryState> completed);
    }
}
```

- [x] **Step 2: Add the production Oculus client**

Create `OculusUserAgeCategoryClient.cs`:

```csharp
using System;
using Oculus.Platform;
using Oculus.Platform.Models;
using UnityEngine;

namespace Blockiverse.MetaPlatform
{
    public sealed class OculusUserAgeCategoryClient : IUserAgeCategoryClient
    {
        public void Get(Action<BlockiverseUserAgeCategoryState> completed)
        {
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                Core.Initialize();
                Request<UserAccountAgeCategory> request = UserAgeCategory.Get();
                if (request == null)
                {
                    completed(BlockiverseUserAgeCategoryState.Unknown(
                        BlockiverseUserAgeCategorySource.Error,
                        "Meta Platform returned no age category request."));
                    return;
                }

                request.OnComplete(message => HandleCompleted(message, completed));
            }
            catch (Exception exception)
            {
                completed(BlockiverseUserAgeCategoryState.Unknown(
                    BlockiverseUserAgeCategorySource.Error,
                    exception.Message));
            }
#else
            completed(BlockiverseUserAgeCategoryState.Unknown(
                BlockiverseUserAgeCategorySource.UnsupportedRuntime,
                "Meta user age category is only available in Quest Android runtime."));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static void HandleCompleted(
            Message<UserAccountAgeCategory> message,
            Action<BlockiverseUserAgeCategoryState> completed)
        {
            if (message.IsError)
            {
                Error error = message.GetError();
                completed(BlockiverseUserAgeCategoryState.Unknown(
                    BlockiverseUserAgeCategorySource.Error,
                    error?.Message ?? "Meta user age category request failed."));
                return;
            }

            completed(new BlockiverseUserAgeCategoryState(
                Convert(message.Data.AgeCategory),
                BlockiverseUserAgeCategorySource.LiveApi,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                "Meta user age category resolved."));
        }
#endif

        static BlockiverseUserAgeCategory Convert(AccountAgeCategory category)
        {
            return category switch
            {
                AccountAgeCategory.Ch => BlockiverseUserAgeCategory.Child,
                AccountAgeCategory.Tn => BlockiverseUserAgeCategory.Teen,
                AccountAgeCategory.Ad => BlockiverseUserAgeCategory.Adult,
                _ => BlockiverseUserAgeCategory.Unknown,
            };
        }
    }
}
```

- [x] **Step 3: Add local fallback caching**

Create `BlockiverseUserAgeCategoryCache.cs`:

```csharp
using System;
using UnityEngine;

namespace Blockiverse.MetaPlatform
{
    public static class BlockiverseUserAgeCategoryCache
    {
        const string CategoryKey = "Blockiverse.MetaPlatform.UserAgeCategory";
        const string TimestampKey = "Blockiverse.MetaPlatform.UserAgeCategoryTimestamp";

        public static bool TryLoad(out BlockiverseUserAgeCategoryState state)
        {
            if (!PlayerPrefs.HasKey(CategoryKey))
            {
                state = default;
                return false;
            }

            string raw = PlayerPrefs.GetString(CategoryKey, string.Empty);
            if (!Enum.TryParse(raw, out BlockiverseUserAgeCategory category) ||
                category == BlockiverseUserAgeCategory.Unknown)
            {
                state = default;
                return false;
            }

            state = new BlockiverseUserAgeCategoryState(
                category,
                BlockiverseUserAgeCategorySource.Cached,
                Convert.ToInt64(PlayerPrefs.GetString(TimestampKey, "0")),
                "Using cached Meta user age category because live category is unavailable.");
            return true;
        }

        public static void Save(BlockiverseUserAgeCategoryState state)
        {
            if (!state.HasKnownCategory)
                return;

            PlayerPrefs.SetString(CategoryKey, state.Category.ToString());
            PlayerPrefs.SetString(TimestampKey, state.UnixSeconds.ToString());
            PlayerPrefs.Save();
        }

        public static void ClearForTests()
        {
            PlayerPrefs.DeleteKey(CategoryKey);
            PlayerPrefs.DeleteKey(TimestampKey);
        }
    }
}
```

- [x] **Step 4: Add cache tests**

Create `BlockiverseUserAgeCategoryCacheEditModeTests.cs`:

```csharp
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
```

- [x] **Step 5: Run cache tests**

Run:

```bash
scripts/unity/run-tests.sh --platform EditMode \
  --filter Blockiverse.Tests.EditMode.MetaPlatform.BlockiverseUserAgeCategoryCacheEditModeTests \
  --results-name MetaPlatformCache
```

Expected: the new cache tests pass and write `TestResults/Unity/MetaPlatformCache.xml`.

## Task 3: Add The Session Service

**Files:**

- Create: `Assets/Blockiverse/Scripts/MetaPlatform/BlockiverseUserAgeCategoryService.cs`
- Test: `Assets/Blockiverse/Tests/EditMode/MetaPlatform/BlockiverseUserAgeCategoryServiceEditModeTests.cs`

- [x] **Step 1: Add the service**

Create `BlockiverseUserAgeCategoryService.cs`:

```csharp
using System;
using Oculus.Platform;
using UnityEngine;

namespace Blockiverse.MetaPlatform
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseUserAgeCategoryService : MonoBehaviour
    {
        static BlockiverseUserAgeCategoryState current =
            BlockiverseUserAgeCategoryState.Unknown(BlockiverseUserAgeCategorySource.None, "Not requested.");

        IUserAgeCategoryClient client;
        bool requestedThisSession;

        public static BlockiverseUserAgeCategoryState Current => current;
        public static event Action<BlockiverseUserAgeCategoryState> Changed;

        public bool RequestedThisSession => requestedThisSession;

        public void ConfigureForTests(IUserAgeCategoryClient testClient)
        {
            client = testClient;
        }

        public void RefreshOncePerSession()
        {
            if (requestedThisSession)
                return;

            requestedThisSession = true;

            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                ApplyOfflineFallback();
                return;
            }

            client ??= new OculusUserAgeCategoryClient();
            client.Get(ApplyResult);
        }

        void Awake()
        {
            RefreshOncePerSession();
        }

        void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Core.IsInitialized())
                Request.RunCallbacks();
#endif
        }

        void ApplyOfflineFallback()
        {
            if (BlockiverseUserAgeCategoryCache.TryLoad(out var cached))
                ApplyResult(cached);
            else
                ApplyResult(BlockiverseUserAgeCategoryState.Unknown(
                    BlockiverseUserAgeCategorySource.Offline,
                    "Meta user age category is unavailable while offline."));
        }

        void ApplyResult(BlockiverseUserAgeCategoryState state)
        {
            if (!state.HasKnownCategory &&
                BlockiverseUserAgeCategoryCache.TryLoad(out var cached))
            {
                current = cached;
            }
            else
            {
                current = state;
                BlockiverseUserAgeCategoryCache.Save(state);
            }

            Changed?.Invoke(current);
        }

        public static void ResetForTests()
        {
            current = BlockiverseUserAgeCategoryState.Unknown(
                BlockiverseUserAgeCategorySource.None,
                "Not requested.");
            Changed = null;
        }
    }
}
```

- [x] **Step 2: Add service tests**

Create `BlockiverseUserAgeCategoryServiceEditModeTests.cs`:

```csharp
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
            var service = serviceObject.AddComponent<BlockiverseUserAgeCategoryService>();
            service.ConfigureForTests(fakeClient);

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

            var service = serviceObject.AddComponent<BlockiverseUserAgeCategoryService>();
            service.ConfigureForTests(new FakeClient(BlockiverseUserAgeCategoryState.Unknown(
                BlockiverseUserAgeCategorySource.Error,
                "failed")));

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
```

- [x] **Step 3: Run service tests**

Run:

```bash
scripts/unity/run-tests.sh --platform EditMode \
  --filter Blockiverse.Tests.EditMode.MetaPlatform.BlockiverseUserAgeCategoryServiceEditModeTests \
  --results-name MetaPlatformService
```

Expected: the new service tests pass and write `TestResults/Unity/MetaPlatformService.xml`.

## Task 4: Gate Meta Avatar Profile Lookup

**Files:**

- Modify: `Assets/Blockiverse/Scripts/MetaAvatars/Blockiverse.MetaAvatars.asmdef`
- Modify: `Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs`
- Test: `Assets/Blockiverse/Tests/EditMode/MetaAvatars/MetaHorizonAvatarProviderAgeGateEditModeTests.cs` or extend the closest existing MetaAvatars EditMode test.

- [x] **Step 1: Reference the new assembly**

Add `Blockiverse.MetaPlatform` to `Assets/Blockiverse/Scripts/MetaAvatars/Blockiverse.MetaAvatars.asmdef`:

```json
"references": [
  "Blockiverse.Networking",
  "Blockiverse.MetaPlatform",
  "Meta.XR.MultiplayerBlocks.Shared",
  "Oculus.AvatarSDK2",
  "Oculus.Platform",
  "Oculus.VR",
  "Unity.Collections",
  "Unity.Netcode.Runtime"
]
```

- [x] **Step 2: Add the policy check to the provider**

In `MetaHorizonAvatarProvider.cs`, add:

```csharp
using Blockiverse.MetaPlatform;
```

At the start of `TryRequestLoggedInUserAvatar()` add:

```csharp
BlockiverseUserAgeCategoryState ageState = BlockiverseUserAgeCategoryService.Current;
if (BlockiversePlatformFeaturePolicy.ShouldAvoidMetaProfileLookup(ageState.Category))
{
    fallbackReason = BlockiversePlatformFeaturePolicy.AvatarFallbackReason(ageState);
    return false;
}
```

Expected behavior:

- Child: no access-token request, no logged-in-user request, fallback proxy remains active.
- Teen/adult: existing avatar lookup behavior remains unchanged.
- Unknown: do not block app usage; keep existing behavior or fallback if Platform APIs fail. If Eric wants stricter privacy, change `ShouldAvoidMetaProfileLookup` to include `Unknown` after confirming it still satisfies Meta's "do not block" requirement.

- [x] **Step 3: Add avatar gate coverage**

If direct `MetaHorizonAvatarProvider` testing is practical without native avatar creation, add this test:

```csharp
using Blockiverse.MetaAvatars;
using Blockiverse.MetaPlatform;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode.MetaAvatars
{
    public sealed class MetaHorizonAvatarProviderAgeGateEditModeTests
    {
        GameObject providerObject;

        [SetUp]
        public void SetUp()
        {
            BlockiverseUserAgeCategoryService.ResetForTests();
            providerObject = new GameObject("Provider");
        }

        [TearDown]
        public void TearDown()
        {
            if (providerObject != null)
                Object.DestroyImmediate(providerObject);
            BlockiverseUserAgeCategoryService.ResetForTests();
        }

        [Test]
        public void ChildPolicyKeepsFallbackProxyReason()
        {
            var state = new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Child,
                BlockiverseUserAgeCategorySource.LiveApi,
                1,
                "child");
            typeof(BlockiverseUserAgeCategoryService)
                .GetField("current", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .SetValue(null, state);

            var provider = providerObject.AddComponent<MetaHorizonAvatarProvider>();
            provider.TickProvider();

            Assert.That(provider.FallbackReason, Does.Contain("fallback avatar"));
        }
    }
}
```

If reflection against the service is too brittle, add an internal test-only setter behind `#if UNITY_INCLUDE_TESTS` in `BlockiverseUserAgeCategoryService`:

```csharp
#if UNITY_INCLUDE_TESTS
public static void SetCurrentForTests(BlockiverseUserAgeCategoryState state)
{
    current = state;
    Changed?.Invoke(current);
}
#endif
```

- [x] **Step 4: Run Meta avatar focused tests**

Run:

```bash
scripts/unity/run-tests.sh --platform EditMode \
  --filter Blockiverse.Tests.EditMode.MetaAvatars \
  --results-name MetaAvatarAgeGate
```

Expected: Meta avatar tests pass and write `TestResults/Unity/MetaAvatarAgeGate.xml`.

## Task 5: Wire The Service Into The Generated Boot Scene

**Files:**

- Modify: `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs`
- Modify: `Assets/Blockiverse/Scenes/Boot.unity` after running the bootstrapper
- Test: `Assets/Blockiverse/Tests/EditMode/BlockiverseBootstrapEditModeTests.cs`

- [x] **Step 1: Update the bootstrapper**

Add a method in `BlockiverseProjectBootstrapper.Scenes.cs`:

```csharp
using Blockiverse.MetaPlatform;
```

```csharp
static void EnsureMetaPlatformCompliance(Scene scene)
{
    const string ObjectName = "Meta Platform Compliance";
    GameObject serviceObject = FindRootGameObject(scene, ObjectName);
    if (serviceObject == null)
    {
        serviceObject = new GameObject(ObjectName);
        SceneManager.MoveGameObjectToScene(serviceObject, scene);
    }

    EnsureComponent<BlockiverseUserAgeCategoryService>(serviceObject);
}
```

Call `EnsureMetaPlatformCompliance(scene);` in the Boot scene generation path after the world/session root objects are available.

- [x] **Step 2: Update editor assembly references**

Add `Blockiverse.MetaPlatform` to `Assets/Blockiverse/Scripts/Editor/Blockiverse.Editor.asmdef`.

- [x] **Step 3: Add bootstrap coverage**

Extend `BlockiverseBootstrapEditModeTests.cs` with:

```csharp
[Test]
public void BootSceneContainsOneMetaUserAgeCategoryService()
{
    Scene bootScene = EditorSceneManager.OpenScene(BlockiverseProject.BootScenePath);
    var services = Object.FindObjectsByType<BlockiverseUserAgeCategoryService>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None);

    Assert.That(services, Has.Length.EqualTo(1));
}
```

Add imports:

```csharp
using Blockiverse.MetaPlatform;
using UnityEditor.SceneManagement;
```

- [x] **Step 4: Regenerate the scene**

Run the existing bootstrapper entry point from Unity batchmode or the project menu. If there is an existing committed script entry point for bootstrap-only runs, use it. Otherwise run:

```bash
scripts/unity/run-tests.sh --platform EditMode \
  --filter Blockiverse.Tests.EditMode.BlockiverseBootstrapEditModeTests \
  --results-name BootstrapMetaPlatform
```

Expected: the bootstrap tests pass and `Boot.unity` has exactly one `Meta Platform Compliance` root with `BlockiverseUserAgeCategoryService`.

## Task 6: Update Store And Ruleset Documentation

**Files:**

- Modify: `docs/roadmap/blockiverse_vr_execution_plan.md`
- Modify: `docs/rulesets/voxel_multiplayer_networking_ruleset.md`
- Modify: `docs/store-submission/checklist.md`
- Modify: `docs/store-submission/data-and-safety.md`
- Modify: `docs/store-submission/privacy-policy.md`
- Modify: `docs/store-submission/vrc-checklist.md`
- Modify: `CHANGELOG.md`

- [x] **Step 1: Roadmap release-channel scope**

In Phase 17 scope, add:

```text
Age group self-certification
User Age Group API integration for Mixed Ages certification
```

In Phase 17 tests, add:

```text
User Age Group API returns CH/TN/AD/UNKNOWN test cases and fallback behavior is documented.
```

- [x] **Step 2: Multiplayer ruleset age policy**

In `docs/rulesets/voxel_multiplayer_networking_ruleset.md` under voice/social features, add:

```md
| User age category | Mixed Ages builds call Meta's User Age Group API once per online session. `UNKNOWN`, offline, and failed calls must not block the base game. Child accounts use fallback identity/avatar behavior for Meta platform dependent social/profile features unless a current Meta policy review explicitly permits the feature. |
```

- [x] **Step 3: Store checklist**

In `docs/store-submission/checklist.md`, add:

```md
- Age group self-certification - Developer Dashboard **(external)**; if Mixed Ages, runtime User Age Group API validation must pass.
- User Age Group API evidence - `meta_user_age_group_api_implementation_plan.md`, `Assets/Blockiverse/Scripts/MetaPlatform/`, focused Unity test results, and final release validation notes.
```

- [x] **Step 4: Data and safety declarations**

Add a data row:

```md
| User age category | Via Meta API; cached locally when known | Handled by Meta, local cache on device | Age-appropriate platform feature policy for Mixed Ages certification | Values are CH/TN/AD/UNKNOWN; no birthdate is collected by Blockiverse VR |
```

Update child safety:

```md
- Mixed Ages builds use Meta's User Age Group API to determine the current user's Meta account age category when online.
- `UNKNOWN`, offline, or failed API responses do not block the base game; the app uses cached category data when available and otherwise keeps child-sensitive Meta social/profile features unavailable or on fallback behavior.
- Users below Meta's minimum platform age are not permitted.
```

- [x] **Step 5: Privacy policy**

Replace the current children's privacy section with:

```md
## Children's privacy

If Blockiverse VR is self-certified as Mixed Ages, the game requests the current user's age category from Meta so it can apply age-appropriate platform feature behavior. Blockiverse VR does not receive or store a birthdate. When available, the game may cache the last known category on the device to keep the app usable when offline or when the API fails. Users below Meta's minimum platform age are not permitted.
```

- [x] **Step 6: VRC checklist**

Add under Content & legal:

```md
- [ ] Age group self-certification is current in the Meta Developer Dashboard.
- [ ] Mixed Ages builds call `UserAgeCategory.Get()` at least once per online session.
- [ ] CH/TN/AD/UNKNOWN handling is validated on Quest or documented with Meta dashboard/API evidence.
```

- [x] **Step 7: Changelog**

Add an Unreleased entry:

```md
- Implemented Mixed Ages User Age Group API compliance: session-level Meta age category retrieval, local fallback cache, child-safe Meta profile/social feature gates, generated Boot scene wiring, validation coverage, and store/privacy document updates.
```

## Task 7: Quest And Release-Channel Validation

**Files:**

- No committed large artifacts. Store logs/screenshots outside the repo or attach to the PR/issue.
- Update PR body or release validation notes with the command output.

- [x] **Step 1: Run targeted EditMode tests**

Run:

```bash
scripts/unity/run-tests.sh --platform EditMode \
  --filter Blockiverse.Tests.EditMode.MetaPlatform \
  --results-name MetaPlatformAll
```

Expected: all MetaPlatform EditMode tests pass.

Status 2026-06-13: Passed. `TestResults/Unity/MetaPlatformAll.xml` reports 8 tests passed, 0 failed.

- [ ] **Step 2: Run full Unity gate**

Run:

```bash
scripts/unity/run-tests.sh
```

Expected: full EditMode and PlayMode gates pass and write `TestResults/Unity/EditMode.xml` and `TestResults/Unity/PlayMode.xml`.

Status 2026-06-13: Attempted. `TestResults/Unity/EditMode.xml` reports 684 tests, 626 passed, 58 failed, before PlayMode ran. The current failures are outside the age-group implementation surface, primarily the dirty authored block atlas/source-material state, menu wiring `ShouldRunBehaviour()` log assertions, and existing survival/resource-loop expectations.

- [x] **Step 3: Build a development APK**

Run:

```bash
scripts/unity/build-development-apk.sh
```

Expected: Unity builds a development APK without compile errors.

Status 2026-06-13: Passed. `scripts/unity/build-development-apk.sh` completed with `Build Finished, Result: Success` and wrote `Builds/Android/BlockiverseVR-development.apk`.

- [ ] **Step 4: Verify a Quest device is available**

Run:

```bash
hzdb --version
hzdb device list
```

Expected: `hzdb` prints a version and lists the target Quest 3 or Quest 3S.

Status 2026-06-13: Blocked locally. `hzdb --version` and `hzdb device list` both returned `zsh:1: command not found: hzdb`.

- [ ] **Step 5: Install and launch**

Use the exact APK path emitted by the build script:

```bash
hzdb install <APK_PATH>
hzdb shell monkey -p <PACKAGE_NAME> 1
```

Expected: app launches to the title flow without hanging on the age-category call.

- [ ] **Step 6: Capture runtime evidence**

Run a log capture while launching:

```bash
hzdb logcat --clear
hzdb logcat | rg "Blockiverse|UserAgeCategory|Meta Platform|MetaHorizonAvatarProvider"
```

Expected:

- Online account: a log line shows the age category request completed, with CH/TN/AD/UNKNOWN but no birthdate or sensitive data.
- Offline/API error: the app remains usable and logs cached/unknown fallback state.
- Child-category test account, when available: no Meta profile/avatar access token lookup occurs; fallback avatar remains active.

If a CH test account is not available, document that limitation in the PR and add a follow-up issue for production release-channel testing.

- [ ] **Step 7: Developer Dashboard and Data Use Checkup**

In the Meta Developer Dashboard:

- Confirm the intended self-certification remains Mixed Ages.
- Complete or update Data Use Checkup for the User Age Group API if prompted.
- Record the dashboard state in the PR validation notes. Do not commit dashboard screenshots unless Eric explicitly wants them in the repo.

## Completion Criteria

- Mixed Ages path calls `UserAgeCategory.Get()` at least once per online user session.
- Offline/API failure/`UNKNOWN` does not block app launch or base gameplay.
- Last known CH/TN/AD category is cached locally and used only as a fallback.
- Child accounts do not invoke child-restricted Meta profile/social feature calls from Blockiverse code; fallback identity/avatar behavior remains available.
- Teens and adults retain the current Meta avatar/profile path.
- Store/privacy/data-use docs match the implemented behavior.
- Focused MetaPlatform tests pass.
- Full local Unity gate passes before PR review or merge.
- Quest install/launch evidence is recorded, with any missing CH test-account coverage tracked as a release blocker or follow-up.

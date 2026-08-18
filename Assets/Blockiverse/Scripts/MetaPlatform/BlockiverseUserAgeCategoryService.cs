using System;
using Blockiverse.Core;
using Oculus.Platform;
using UnityEngine;
using OculusCore = Oculus.Platform.Core;
using OculusRequest = Oculus.Platform.Request;

namespace Blockiverse.MetaPlatform
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseUserAgeCategoryService : MonoBehaviour
    {
        static BlockiverseUserAgeCategoryState current =
            BlockiverseUserAgeCategoryState.Unknown(BlockiverseUserAgeCategorySource.None, "Not requested.");

        // Bridges the Meta age policy into the dependency-free Blockiverse.Core seam so UI can
        // consult CanUseMetaSocialFeature without referencing the MetaPlatform assembly. Wired from
        // the static constructor so it is registered the first time this type is touched: scene
        // load in play mode, or SetCurrentForTests/ResetForTests in edit-mode tests.
        static BlockiverseUserAgeCategoryService()
        {
            BlockiverseMetaSocialPolicy.CanUseMetaSocialFeatureCallback = () =>
                BlockiversePlatformFeaturePolicy.CanUseMetaSocialFeature(current.Category);
        }

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

            if (client == null && UnityEngine.Application.internetReachability == NetworkReachability.NotReachable)
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
            if (OculusCore.IsInitialized())
                OculusRequest.RunCallbacks();
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

            Debug.Log(
                $"[BlockiverseUserAgeCategoryService] Age category resolved as {current.Category} from {current.Source}.",
                this);
            Changed?.Invoke(current);
        }

        public static void ResetForTests()
        {
            current = BlockiverseUserAgeCategoryState.Unknown(
                BlockiverseUserAgeCategorySource.None,
                "Not requested.");
            Changed = null;
        }

#if UNITY_INCLUDE_TESTS
        public static void SetCurrentForTests(BlockiverseUserAgeCategoryState state)
        {
            current = state;
            Changed?.Invoke(current);
        }
#endif
    }
}

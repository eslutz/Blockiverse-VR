using System;
using Blockiverse.MetaPlatform;
using Oculus.Avatar2;
using Oculus.Platform;
using Oculus.Platform.Models;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Loads and presents the Meta Horizon avatar on Quest.
    ///
    /// Local first-person: resolves the signed-in profile through the Meta Platform SDK
    /// (access token -> logged-in user -> CDN avatar) with retry/backoff — a transient
    /// failure at boot no longer disables the avatar for the whole session. While the
    /// profile chain is unresolved the fallback proxy stays up; after the first settled
    /// attempt the SDK's default avatar is presented (and upgraded in place when the real
    /// profile avatar arrives). Child accounts never trigger a profile lookup and keep the
    /// proxy, per policy.
    ///
    /// Remote third-person: posed by the owner's recorded stream; when the owner's Meta
    /// user id arrives over the network, the peer's real avatar is loaded so players see
    /// each other's actual likeness, not a default.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MetaHorizonAvatarProvider : MonoBehaviour, IBlockiverseMetaAvatarProvider
    {
        /// <summary>Root object the bootstrapper authors into the Boot scene: an inactive,
        /// fully-configured Avatar SDK manager (shader configs, GPU skinning shaders, LOD
        /// manager). It stays inactive in the editor so desktop tests never load avatar
        /// native libraries; the provider activates it on Quest.</summary>
        public const string SdkManagerObjectName = "Meta Avatar SDK Manager";

        const string AvatarEntityName = "Meta Horizon Avatar Entity";
#if UNITY_ANDROID && !UNITY_EDITOR
        const int AvatarManagerMaxConcurrentAvatarsLoading = 4;
        const int AvatarManagerMaxConcurrentResourcesLoading = 2;
        static readonly float[] RetryDelaysSeconds = { 2.0f, 4.0f, 8.0f, 16.0f, 30.0f, 60.0f };
#endif

        [SerializeField] BlockiverseMetaAvatarEntity avatarEntity;
        [SerializeField] OvrAvatarEntity.StreamLOD streamLod = OvrAvatarEntity.StreamLOD.Medium;
        [SerializeField] bool preferLoggedInUserAvatar = true;
        [SerializeField] bool loadFallbackPreset;
        [SerializeField] string fallbackPresetPath = "0";
        [SerializeField] string fallbackReason = "Meta Horizon avatar has not loaded yet.";

        byte[] streamBuffer = Array.Empty<byte>();
        byte[] recordedStreamData = Array.Empty<byte>();
        MetaAvatarPresentationMode mode = MetaAvatarPresentationMode.RemoteThirdPerson;
        bool hasAppliedRemoteStream;
        bool avatarPresentationUnlocked;
        ulong localUserId;
        ulong remoteUserId;

#if UNITY_ANDROID && !UNITY_EDITOR
        enum LocalLoadPhase
        {
            NotStarted,
            RequestingAccessToken,
            RequestingLoggedInUser,
            LoadingUserAvatar,
            Loaded,
            FailedWaitingForRetry,
            SuppressedByPolicy,
            PresetRequested
        }

        LocalLoadPhase localLoadPhase = LocalLoadPhase.NotStarted;
        int retryAttempt;
        float nextRetryTime;
        float phaseStartedTime;
        bool presetStopgapRequested;
        // Platform callbacks cannot be cancelled; a timed-out attempt's late callback must
        // not re-enter the machine (it could clobber a healthy successor chain, or restart
        // a settled one and wedge on the SDK's first-time-only success event). Every issued
        // request captures the generation; stale generations are dropped.
        int platformAttemptGeneration;
        float ageCategoryWaitDeadline;
        int remoteRetryAttempt;
        float nextRemoteLoadRetryTime;
        float remoteLoadStartedTime;

        const float PlatformCallbackTimeoutSeconds = 30.0f;
        const float UserAvatarLoadTimeoutSeconds = 90.0f;
        const float AgeCategoryWaitTimeoutSeconds = 20.0f;
#endif

        public bool PreferLoggedInUserAvatar => preferLoggedInUserAvatar;
        public bool LoadFallbackPresetEnabled => loadFallbackPreset;
        public string FallbackPresetPath => fallbackPresetPath;
        public ulong RemoteUserId => remoteUserId;

        public bool IsAvatarReady
        {
            get
            {
                if (avatarEntity == null)
                    return false;

                if (!avatarPresentationUnlocked)
                    RefreshPresentationUnlock();

                return avatarPresentationUnlocked;
            }
        }

        public string FallbackReason => IsAvatarReady ? string.Empty : fallbackReason;

        public void Configure(MetaAvatarTrackingSources sources, MetaAvatarPresentationMode presentationMode, bool hideFirstPersonHead)
        {
            mode = presentationMode;
            EnsureAvatarEntity(presentationMode, hideFirstPersonHead);

            if (avatarEntity == null)
                return;

            // For a pre-existing entity (e.g. from a previous Configure call), update its
            // presentation and tracking. For newly-created entities these calls already ran
            // inside EnsureAvatarEntity(), so they are idempotent here.
            OvrAvatarInputManagerBehavior inputManager = ResolveInputManager();
            avatarEntity.ConfigurePresentation(presentationMode, hideFirstPersonHead);
            avatarEntity.EnsureInputManager(inputManager);
#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureAvatarManager();
            avatarEntity.CreateConfiguredEntity(inputManager);
#endif
            _ = sources;
        }

        public void TickProvider()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Request.RunCallbacks();

            if (avatarEntity != null && !avatarEntity.IsCreated)
            {
                EnsureAvatarManager();
                avatarEntity.CreateConfiguredEntity(ResolveInputManager());
            }

            if (mode == MetaAvatarPresentationMode.LocalFirstPerson)
                TickLocalAvatarLoad();
            else
                TickRemoteAvatarLoad();
#endif

            RefreshPresentationUnlock();
        }

        public bool TryRecordStream(out byte[] streamData)
        {
            streamData = Array.Empty<byte>();

            if (avatarEntity == null || !IsAvatarReady)
                return false;

            uint byteCount = avatarEntity.RecordStreamData_AutoBuffer(streamLod, ref streamBuffer);
            if (byteCount == 0)
                return false;

            int streamLength = checked((int)byteCount);
            if (recordedStreamData.Length != streamLength)
                recordedStreamData = new byte[streamLength];

            Array.Copy(streamBuffer, recordedStreamData, streamLength);
            streamData = recordedStreamData;
            return true;
        }

        public void ApplyStreamData(byte[] streamData)
        {
            EnsureAvatarEntity();

            if (avatarEntity == null || streamData == null || streamData.Length == 0)
                return;

            avatarEntity.SetIsLocal(false);
            hasAppliedRemoteStream = avatarEntity.ApplyStreamData(streamData);
            if (!hasAppliedRemoteStream)
                fallbackReason = "Remote Meta Horizon avatar stream is waiting for a ready entity.";

            RefreshPresentationUnlock();
        }

        public bool TryGetLocalUserId(out ulong userId)
        {
            userId = localUserId;
            return userId != 0;
        }

        public void ConfigureRemoteUserAvatar(ulong userId)
        {
            if (remoteUserId == userId)
                return;

            remoteUserId = userId;
#if UNITY_ANDROID && !UNITY_EDITOR
            // A new peer identity deserves a fresh retry budget.
            remoteRetryAttempt = 0;
            nextRemoteLoadRetryTime = 0.0f;
#endif
        }

        /// <summary>
        /// Presentation-owned visibility for the avatar entity. Never toggles the entity's
        /// GameObject: the SDK only advances loading while the behaviour is active, so
        /// deactivating a not-yet-ready entity deadlocks it into never becoming ready.
        /// </summary>
        public void SetEntityVisible(bool visible)
        {
            avatarEntity?.SetPresentationVisible(visible);
        }

        void RefreshPresentationUnlock()
        {
            if (avatarPresentationUnlocked)
                return;

            if (avatarEntity == null || !avatarEntity.IsRenderableReady)
                return;

            if (mode == MetaAvatarPresentationMode.RemoteThirdPerson)
            {
                // A remote avatar is presentable once the first stream frame has posed it;
                // the peer's real likeness upgrades the model in place later.
                if (hasAppliedRemoteStream)
                    avatarPresentationUnlocked = true;
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            // Hold the proxy until the profile chain settles once, so the player does not
            // see the generic default avatar flash before their own arrives. After that
            // first settlement, presentation is sticky: retries and late model swaps must
            // never re-show the proxy.
            bool chainSettled = localLoadPhase is LocalLoadPhase.Loaded
                or LocalLoadPhase.FailedWaitingForRetry
                or LocalLoadPhase.PresetRequested;
            if (chainSettled)
                avatarPresentationUnlocked = true;
#endif
        }

        void EnsureAvatarEntity(
            MetaAvatarPresentationMode presentationMode = MetaAvatarPresentationMode.RemoteThirdPerson,
            bool hideFirstPersonHead = false)
        {
            if (avatarEntity != null)
                return;

            Transform existing = transform.Find(AvatarEntityName);
            if (existing != null)
                avatarEntity = existing.GetComponent<BlockiverseMetaAvatarEntity>();

            if (avatarEntity != null)
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Create inactive so OvrAvatarEntity.Awake() cannot create the SDK entity before
            // Blockiverse has staged creation flags and the avatar input manager. The entity
            // stays at an identity local pose under this provider's transform — the rig root
            // locally, the network player object remotely — which is the tracking-space
            // origin the SDK expects as the avatar root.
            var entityObject = new GameObject(AvatarEntityName);
            entityObject.SetActive(false);
            entityObject.transform.SetParent(transform, false);
            avatarEntity = entityObject.AddComponent<BlockiverseMetaAvatarEntity>();

            OvrAvatarInputManagerBehavior inputManager = ResolveInputManager();
            avatarEntity.ConfigurePresentation(presentationMode, hideFirstPersonHead);
            avatarEntity.EnsureInputManager(inputManager);

            entityObject.SetActive(true);
            EnsureAvatarManager();
            avatarEntity.CreateConfiguredEntity(inputManager);
#else
            _ = presentationMode;
            _ = hideFirstPersonHead;
            fallbackReason = "Meta Horizon avatar entity is only created in Quest runtime.";
#endif
        }

        OvrAvatarInputManagerBehavior ResolveInputManager()
        {
            return GetComponent<OvrAvatarInputManagerBehavior>()
                ?? GetComponentInParent<OvrAvatarInputManagerBehavior>(true)
                ?? GetComponentInChildren<OvrAvatarInputManagerBehavior>(true);
        }

        public bool CanRequestLoggedInUserAvatarForCurrentAgeCategory(out string ageGateFallbackReason)
        {
            BlockiverseUserAgeCategoryState ageState = BlockiverseUserAgeCategoryService.Current;
            if (!BlockiversePlatformFeaturePolicy.ShouldAvoidMetaProfileLookup(ageState.Category))
            {
                ageGateFallbackReason = string.Empty;
                return true;
            }

            ageGateFallbackReason = BlockiversePlatformFeaturePolicy.AvatarFallbackReason(ageState);
            return false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool EnsureAvatarManager()
        {
            if (!OvrAvatarManager.hasInstance)
            {
                // Prefer the bootstrapper-authored, asset-configured manager: activating it
                // runs OvrAvatarManager.Initialize with real shader configurations, which a
                // bare runtime-instantiated manager lacks (its Shader.Find("Standard")
                // fallback is stripped from URP Android builds and every avatar load aborts).
                //
                // The search must include inactive objects AND must not walk scenes: during
                // the very first Awake, Scene.isLoaded is still false for the loading Boot
                // scene, and after activation the singleton reparents to DontDestroyOnLoad —
                // a scene-root walk misses the manager in both states and would fall back to
                // the bare (device-fatal) manager exactly once per session, permanently.
                OvrAvatarManager configured =
                    FindAnyObjectByType<OvrAvatarManager>(FindObjectsInactive.Include);
                if (configured != null)
                {
                    if (!configured.gameObject.activeSelf)
                        configured.gameObject.SetActive(true);
                }
                else
                {
                    Debug.LogWarning(
                        $"[MetaHorizonAvatarProvider] '{SdkManagerObjectName}' not found; " +
                        "falling back to an unconfigured runtime OvrAvatarManager. Avatars will not " +
                        "render without shader configuration — rerun the project bootstrapper.");
                    OvrAvatarManager.Instantiate();
                }
            }

            if (!OvrAvatarManager.hasInstance)
                return false;

            OvrAvatarManager manager = OvrAvatarManager.Instance;
            manager.MaxConcurrentAvatarsLoading = AvatarManagerMaxConcurrentAvatarsLoading;
            manager.MaxConcurrentResourcesLoading = AvatarManagerMaxConcurrentResourcesLoading;
            return OvrAvatarManager.initialized;
        }

        void TickLocalAvatarLoad()
        {
            if (avatarEntity == null || !avatarEntity.IsCreated)
            {
                fallbackReason = "Meta Horizon avatar entity is waiting for Avatar SDK initialization.";
                return;
            }

            switch (localLoadPhase)
            {
                case LocalLoadPhase.NotStarted:
                    StartLocalAvatarLoad();
                    break;

                case LocalLoadPhase.RequestingAccessToken:
                case LocalLoadPhase.RequestingLoggedInUser:
                    // A platform callback that never arrives must not hang the chain forever.
                    if (Time.unscaledTime - phaseStartedTime > PlatformCallbackTimeoutSeconds)
                        ScheduleRetry("Meta Platform request timed out.");
                    break;

                case LocalLoadPhase.LoadingUserAvatar:
                    // State poll first: the SDK's success event fires only on the FIRST
                    // transition into UserAvatar, so a re-load after an earlier success (or
                    // a race with a preset) completes eventlessly.
                    if (avatarEntity.HasUserAvatarModel)
                    {
                        localLoadPhase = LocalLoadPhase.Loaded;
                        fallbackReason = string.Empty;
                    }
                    else if (!avatarEntity.UserAvatarLoadInFlight)
                    {
                        if (avatarEntity.UserAvatarLoadFailed)
                        {
                            ScheduleRetry("Meta Horizon avatar download failed.");
                        }
                        else
                        {
                            localLoadPhase = LocalLoadPhase.Loaded;
                            fallbackReason = string.Empty;
                        }
                    }
                    else if (Time.unscaledTime - phaseStartedTime > UserAvatarLoadTimeoutSeconds)
                    {
                        // The SDK never reports cancelled load requests; without this the
                        // machine waits on a dead flag for the whole session.
                        avatarEntity.AbandonUserAvatarLoadTracking();
                        ScheduleRetry("Meta Horizon avatar download timed out.");
                    }
                    break;

                case LocalLoadPhase.FailedWaitingForRetry:
                    if (Time.unscaledTime >= nextRetryTime)
                        StartLocalAvatarLoad();
                    break;
            }
        }

        void StartLocalAvatarLoad()
        {
            if (!preferLoggedInUserAvatar)
            {
                // Profile avatar disabled: settle immediately so the preset (if enabled)
                // or the SDK default avatar presents instead of the proxy.
                TryStartPresetStopgap();
                localLoadPhase = LocalLoadPhase.PresetRequested;
                fallbackReason = "Meta Horizon profile avatar is disabled.";
                return;
            }

            // The age category resolves asynchronously and starts as Unknown, which passes
            // the child gate — starting immediately would race a child account's category
            // resolution and issue the profile lookup the policy forbids. Hold (bounded, so
            // an offline adult is not bricked) until the service reports any resolved
            // source; the offline/cache fallbacks all count as resolved.
            BlockiverseUserAgeCategoryState ageState = BlockiverseUserAgeCategoryService.Current;
            if (ageState.Source == BlockiverseUserAgeCategorySource.None)
            {
                if (ageCategoryWaitDeadline <= 0.0f)
                    ageCategoryWaitDeadline = Time.unscaledTime + AgeCategoryWaitTimeoutSeconds;

                if (Time.unscaledTime < ageCategoryWaitDeadline)
                {
                    fallbackReason = "Meta Horizon avatar is waiting for the account age category.";
                    return; // Stays NotStarted; retried next tick.
                }
            }

            if (!CanRequestLoggedInUserAvatarForCurrentAgeCategory(out string ageGateFallbackReason))
            {
                // Policy, not an error: child accounts keep the fallback proxy and no
                // profile lookup is ever issued. No retry — the category cannot relax
                // mid-session.
                fallbackReason = ageGateFallbackReason;
                localLoadPhase = LocalLoadPhase.SuppressedByPolicy;
                return;
            }

            try
            {
                // Core.Initialize has no already-initialized guard and leaks a persistent
                // CallbackRunner object per call; with retries in play it must be guarded.
                if (!Core.IsInitialized())
                    Core.Initialize();

                localLoadPhase = LocalLoadPhase.RequestingAccessToken;
                phaseStartedTime = Time.unscaledTime;
                fallbackReason = "Meta Horizon avatar is waiting for the signed-in Quest profile.";
                int generation = ++platformAttemptGeneration;
                Users.GetAccessToken().OnComplete(message => OnAccessTokenResolved(message, generation));
            }
            catch (Exception exception)
            {
                ScheduleRetry($"Meta Platform initialization failed: {exception.Message}");
            }
        }

        void ScheduleRetry(string reason)
        {
            fallbackReason = reason;
            localLoadPhase = LocalLoadPhase.FailedWaitingForRetry;
            // Invalidate any outstanding platform callback from the abandoned attempt.
            platformAttemptGeneration++;
            float delay = RetryDelaysSeconds[Mathf.Min(retryAttempt, RetryDelaysSeconds.Length - 1)];
            retryAttempt++;
            nextRetryTime = Time.unscaledTime + delay;
            Debug.LogWarning($"[MetaHorizonAvatarProvider] {reason} Retrying in {delay:0}s (attempt {retryAttempt}).", this);
        }

        void TryStartPresetStopgap()
        {
            // Deliberately only used when the profile avatar is disabled outright: a preset
            // load races the profile chain otherwise — presets transition the entity to the
            // same UserAvatar state and share its load-event channel, corrupting the
            // profile-load tracking.
            if (!loadFallbackPreset || presetStopgapRequested || avatarEntity == null)
                return;

            presetStopgapRequested = avatarEntity.TryLoadPresetAvatar(fallbackPresetPath);
        }

        void OnAccessTokenResolved(Message<string> message, int generation)
        {
            if (generation != platformAttemptGeneration)
                return; // A newer attempt owns the machine; this callback timed out earlier.

            if (message.IsError)
            {
                Error error = message.GetError();
                ScheduleRetry($"Meta Platform access token lookup failed: {error.Message}");
                return;
            }

            OvrAvatarEntitlement.SetAccessToken(message.Data);
            localLoadPhase = LocalLoadPhase.RequestingLoggedInUser;
            phaseStartedTime = Time.unscaledTime;
            Users.GetLoggedInUser().OnComplete(userMessage => OnLoggedInUserResolved(userMessage, generation));
        }

        void OnLoggedInUserResolved(Message<User> message, int generation)
        {
            if (generation != platformAttemptGeneration)
                return;

            if (message.IsError)
            {
                Error error = message.GetError();
                ScheduleRetry($"Meta Platform user lookup failed: {error.Message}");
                return;
            }

            if (message.Data.ID == 0)
            {
                // Classic Data Use Checkup / entitlement symptom: auth succeeded but the
                // platform refused the identity. Retrying rarely fixes it within a session,
                // but the logged reason tells the developer exactly where to look.
                ScheduleRetry("Meta Platform returned user id 0 (check Data Use Checkup and account entitlement).");
                return;
            }

            localUserId = message.Data.ID;

            if (avatarEntity != null && avatarEntity.TryLoadUserAvatar(localUserId))
            {
                localLoadPhase = LocalLoadPhase.LoadingUserAvatar;
                phaseStartedTime = Time.unscaledTime;
                fallbackReason = "Meta Horizon avatar is loading from the signed-in Quest profile.";
                Debug.Log($"[MetaHorizonAvatarProvider] Loading signed-in user avatar {localUserId}.", this);
            }
            else
            {
                ScheduleRetry("Meta Horizon avatar user load could not start.");
            }
        }

        void TickRemoteAvatarLoad()
        {
            if (remoteUserId == 0 ||
                avatarEntity == null ||
                !avatarEntity.IsCreated ||
                !OvrAvatarEntitlement.AccessTokenIsValid())
            {
                return;
            }

            if (avatarEntity.UserAvatarLoadInFlight)
            {
                if (avatarEntity.RequestedUserId == remoteUserId &&
                    Time.unscaledTime - remoteLoadStartedTime > UserAvatarLoadTimeoutSeconds)
                {
                    avatarEntity.AbandonUserAvatarLoadTracking();
                }
                return;
            }

            if (avatarEntity.RequestedUserId == remoteUserId &&
                (avatarEntity.HasUserAvatarModel || !avatarEntity.UserAvatarLoadFailed))
            {
                return;
            }

            if (Time.unscaledTime < nextRemoteLoadRetryTime)
                return;

            // Same capped backoff as the local chain: a permanently failing peer id must
            // not turn into an every-few-seconds CDN request for the whole session.
            float delay = RetryDelaysSeconds[Mathf.Min(remoteRetryAttempt, RetryDelaysSeconds.Length - 1)];
            remoteRetryAttempt++;
            nextRemoteLoadRetryTime = Time.unscaledTime + delay;
            if (avatarEntity.TryLoadUserAvatar(remoteUserId))
            {
                remoteLoadStartedTime = Time.unscaledTime;
                Debug.Log($"[MetaHorizonAvatarProvider] Loading remote player avatar {remoteUserId}.", this);
            }
        }
#endif
    }
}

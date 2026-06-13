using System;
using Oculus.Avatar2;
using Oculus.Platform;
using Oculus.Platform.Models;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    [DisallowMultipleComponent]
    public sealed class MetaHorizonAvatarProvider : MonoBehaviour, IBlockiverseMetaAvatarProvider
    {
        const string AvatarEntityName = "Meta Horizon Avatar Entity";
#if UNITY_ANDROID && !UNITY_EDITOR
        const int AvatarManagerMaxConcurrentAvatarsLoading = 4;
        const int AvatarManagerMaxConcurrentResourcesLoading = 2;
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
        bool attemptedLocalLoad;
#if UNITY_ANDROID && !UNITY_EDITOR
        bool waitingForAccessToken;
        bool waitingForLoggedInUser;
#endif
        bool hasAppliedRemoteStream;

        public bool IsAvatarReady
        {
            get
            {
                if (avatarEntity == null)
                    return false;

                if (mode == MetaAvatarPresentationMode.RemoteThirdPerson)
                    return hasAppliedRemoteStream;

                return avatarEntity.IsRenderableReady;
            }
        }

        public string FallbackReason => IsAvatarReady ? string.Empty : fallbackReason;

        public void Configure(MetaAvatarTrackingSources sources, MetaAvatarPresentationMode presentationMode, bool hideFirstPersonHead)
        {
            mode = presentationMode;
            EnsureAvatarEntity(presentationMode, hideFirstPersonHead, sources);

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
            avatarEntity.SetTrackingSources(sources);

            if (presentationMode == MetaAvatarPresentationMode.LocalFirstPerson && !attemptedLocalLoad)
                attemptedLocalLoad = TryStartLocalAvatarLoad();
        }

        public void TickProvider()
        {
            avatarEntity?.SetTrackingSourcesFromTransforms();

#if UNITY_ANDROID && !UNITY_EDITOR
            Request.RunCallbacks();

            if (avatarEntity != null && !avatarEntity.IsCreated)
            {
                EnsureAvatarManager();
                avatarEntity.CreateConfiguredEntity(ResolveInputManager());
            }
#endif

            if (mode == MetaAvatarPresentationMode.LocalFirstPerson && !attemptedLocalLoad)
                attemptedLocalLoad = TryStartLocalAvatarLoad();
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
            fallbackReason = hasAppliedRemoteStream
                ? string.Empty
                : "Remote Meta Horizon avatar stream is waiting for a ready entity.";
        }

        void EnsureAvatarEntity(
            MetaAvatarPresentationMode presentationMode = MetaAvatarPresentationMode.RemoteThirdPerson,
            bool hideFirstPersonHead = false,
            MetaAvatarTrackingSources sources = default)
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
            // Blockiverse has staged creation flags and the avatar input manager.
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

            if (sources.Head != null)
                avatarEntity.SetTrackingSources(sources);
#else
            fallbackReason = "Meta Horizon avatar entity is only created in Quest runtime.";
#endif
        }

        OvrAvatarInputManagerBehavior ResolveInputManager()
        {
            return GetComponent<OvrAvatarInputManagerBehavior>()
                ?? GetComponentInParent<OvrAvatarInputManagerBehavior>(true)
                ?? GetComponentInChildren<OvrAvatarInputManagerBehavior>(true);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool EnsureAvatarManager()
        {
            if (!OvrAvatarManager.hasInstance)
                OvrAvatarManager.Instantiate();

            if (!OvrAvatarManager.hasInstance)
                return false;

            OvrAvatarManager manager = OvrAvatarManager.Instance;
            manager.MaxConcurrentAvatarsLoading = AvatarManagerMaxConcurrentAvatarsLoading;
            manager.MaxConcurrentResourcesLoading = AvatarManagerMaxConcurrentResourcesLoading;
            return OvrAvatarManager.initialized;
        }
#endif

        bool TryStartLocalAvatarLoad()
        {
            if (avatarEntity == null || !avatarEntity.IsCreated)
            {
                fallbackReason = "Meta Horizon avatar entity is waiting for Avatar SDK initialization.";
                return false;
            }

            if (preferLoggedInUserAvatar && TryRequestLoggedInUserAvatar())
            {
                fallbackReason = "Meta Horizon avatar is waiting for the signed-in Quest profile.";
                return true;
            }

            if (loadFallbackPreset && avatarEntity.TryLoadPresetAvatar(fallbackPresetPath))
            {
                fallbackReason = "Meta Horizon fallback avatar preset is loading.";
                return true;
            }

            fallbackReason = "Meta Horizon avatar could not start loading; fallback proxy remains active.";
            return true;
        }

        bool TryRequestLoggedInUserAvatar()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (waitingForAccessToken || waitingForLoggedInUser)
                return true;

            try
            {
                Core.Initialize();
                waitingForAccessToken = true;
                Users.GetAccessToken().OnComplete(OnAccessTokenResolved);
                return true;
            }
            catch (Exception exception)
            {
                fallbackReason = $"Meta Platform user lookup failed: {exception.Message}";
                waitingForAccessToken = false;
                waitingForLoggedInUser = false;
                return false;
            }
#else
            fallbackReason = "Meta Horizon logged-in user avatar requires Quest runtime.";
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void OnAccessTokenResolved(Message<string> message)
        {
            waitingForAccessToken = false;

            if (message.IsError)
            {
                Error error = message.GetError();
                fallbackReason = $"Meta Platform access token lookup failed: {error.Message}";
                Debug.LogWarning($"[MetaHorizonAvatarProvider] {fallbackReason}", this);
                return;
            }

            OvrAvatarEntitlement.SetAccessToken(message.Data);
            waitingForLoggedInUser = true;
            Users.GetLoggedInUser().OnComplete(OnLoggedInUserResolved);
        }

        void OnLoggedInUserResolved(Message<User> message)
        {
            waitingForLoggedInUser = false;

            if (message.IsError)
            {
                Error error = message.GetError();
                fallbackReason = $"Meta Platform user lookup failed: {error.Message}";
                Debug.LogWarning($"[MetaHorizonAvatarProvider] {fallbackReason}", this);
                return;
            }

            if (avatarEntity != null && avatarEntity.TryLoadUserAvatar(message.Data.ID))
            {
                fallbackReason = "Meta Horizon avatar is loading from the signed-in Quest profile.";
                Debug.Log($"[MetaHorizonAvatarProvider] Loading signed-in user avatar {message.Data.ID}.", this);
            }
            else
            {
                fallbackReason = "Meta Horizon avatar user load could not start; fallback proxy remains active.";
                Debug.LogWarning($"[MetaHorizonAvatarProvider] {fallbackReason}", this);
            }
        }
#endif
    }
}

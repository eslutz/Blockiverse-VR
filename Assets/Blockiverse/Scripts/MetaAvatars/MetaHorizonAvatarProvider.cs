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

        [SerializeField] BlockiverseMetaAvatarEntity avatarEntity;
        [SerializeField] OvrAvatarEntity.StreamLOD streamLod = OvrAvatarEntity.StreamLOD.Medium;
        [SerializeField] bool preferLoggedInUserAvatar = true;
        [SerializeField] bool loadFallbackPreset;
        [SerializeField] string fallbackPresetPath = "0";
        [SerializeField] string fallbackReason = "Meta Horizon avatar has not loaded yet.";

        byte[] streamBuffer = Array.Empty<byte>();
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

                return avatarEntity.IsCreated && !avatarEntity.IsPendingAvatar;
            }
        }

        public string FallbackReason => IsAvatarReady ? string.Empty : fallbackReason;

        public void Configure(MetaAvatarTrackingSources sources, MetaAvatarPresentationMode presentationMode, bool hideFirstPersonHead)
        {
            mode = presentationMode;
            EnsureAvatarEntity();

            if (avatarEntity == null)
                return;

            avatarEntity.ConfigurePresentation(presentationMode, hideFirstPersonHead);
            avatarEntity.SetTrackingSources(sources);
            avatarEntity.SetIsLocal(presentationMode == MetaAvatarPresentationMode.LocalFirstPerson);

            if (presentationMode == MetaAvatarPresentationMode.LocalFirstPerson && !attemptedLocalLoad)
                attemptedLocalLoad = TryStartLocalAvatarLoad();
        }

        public void TickProvider()
        {
            avatarEntity?.SetTrackingSourcesFromTransforms();

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

            streamData = new byte[byteCount];
            Array.Copy(streamBuffer, streamData, byteCount);
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

        void EnsureAvatarEntity()
        {
            if (avatarEntity != null)
                return;

            Transform existing = transform.Find(AvatarEntityName);
            if (existing != null)
                avatarEntity = existing.GetComponent<BlockiverseMetaAvatarEntity>();

            if (avatarEntity != null)
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            var entityObject = new GameObject(AvatarEntityName);
            entityObject.transform.SetParent(transform, false);
            avatarEntity = entityObject.AddComponent<BlockiverseMetaAvatarEntity>();
#else
            fallbackReason = "Meta Horizon avatar entity is only created in Quest runtime.";
#endif
        }

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
                return;
            }

            if (avatarEntity != null && avatarEntity.TryLoadUserAvatar(message.Data.ID))
                fallbackReason = "Meta Horizon avatar is loading from the signed-in Quest profile.";
            else
                fallbackReason = "Meta Horizon avatar user load could not start; fallback proxy remains active.";
        }
#endif
    }
}

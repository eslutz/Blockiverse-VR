using System;
using Blockiverse.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class MetaAvatarStreamRelay : NetworkBehaviour
    {
        [SerializeField] BlockiverseMetaAvatarPresenter remotePresenter;
        [SerializeField] float streamSendRateHz = 15.0f;

        BlockiverseMetaAvatarPresenter localFirstPersonPresenter;
        BlockiverseNetworkAvatarRig ownerNetworkFallbackRig;
        double nextSendTime;
        double nextPresenterSearchTime;

        void Awake()
        {
            remotePresenter ??= GetComponent<BlockiverseMetaAvatarPresenter>();
            ownerNetworkFallbackRig = GetComponent<BlockiverseNetworkAvatarRig>();
        }

        void LateUpdate()
        {
            if (!IsSpawned)
                return;

            if (!IsOwner)
                return;

            // The local presenter may not exist (avatar disabled): throttle the scene walk
            // instead of running FindObjectsByType every frame until one appears.
            if (localFirstPersonPresenter == null && Time.unscaledTimeAsDouble >= nextPresenterSearchTime)
            {
                nextPresenterSearchTime = Time.unscaledTimeAsDouble + 1.0;
                localFirstPersonPresenter = FindLocalFirstPersonPresenter();
            }

            HideOwnerNetworkFallbackWhenLocalAvatarIsReady();

            if (localFirstPersonPresenter == null || NetworkManager == null)
                return;

            double now = Time.unscaledTimeAsDouble;
            double minInterval = streamSendRateHz <= 0.0f ? 0.0f : 1.0f / streamSendRateHz;
            if (minInterval > 0.0f && now < nextSendTime)
                return;

            if (!localFirstPersonPresenter.TryRecordLocalStream(out byte[] streamData) || streamData.Length == 0)
                return;

            nextSendTime = now + minInterval;
            SubmitAvatarStreamServerRpc(new MetaAvatarStreamMessage(OwnerClientId, now, streamData));
        }

        public void ApplyRemoteStreamForTests(MetaAvatarStreamMessage message)
        {
            remotePresenter ??= GetComponent<BlockiverseMetaAvatarPresenter>();
            remotePresenter?.ApplyRemoteStream(message.Payload ?? Array.Empty<byte>());
        }

        [ServerRpc]
        void SubmitAvatarStreamServerRpc(MetaAvatarStreamMessage message)
        {
            // Re-stamp the sender id server-side: a modified client could spoof any identity.
            message.SenderClientId = OwnerClientId;
            ReceiveAvatarStreamClientRpc(message);
        }

        [ClientRpc]
        void ReceiveAvatarStreamClientRpc(MetaAvatarStreamMessage message)
        {
            if (IsOwner || message.SenderClientId == NetworkManager.LocalClientId)
                return;

            remotePresenter ??= GetComponent<BlockiverseMetaAvatarPresenter>();
            remotePresenter?.ApplyRemoteStream(message.Payload ?? Array.Empty<byte>());
        }

        void HideOwnerNetworkFallbackWhenLocalAvatarIsReady()
        {
            if (ownerNetworkFallbackRig == null || localFirstPersonPresenter == null)
                return;

            ownerNetworkFallbackRig.ConfigureFallbackProxy(true);
            ownerNetworkFallbackRig.SetMetaAvatarAvailable(localFirstPersonPresenter.AvatarReady);
        }

        static BlockiverseMetaAvatarPresenter FindLocalFirstPersonPresenter()
        {
            foreach (BlockiverseMetaAvatarPresenter presenter in FindObjectsByType<BlockiverseMetaAvatarPresenter>(FindObjectsSortMode.None))
            {
                if (presenter.PresentationMode == MetaAvatarPresentationMode.LocalFirstPerson)
                    return presenter;
            }

            return null;
        }
    }
}

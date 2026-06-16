using System;
using System.Collections.Generic;
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
        readonly List<ulong> remoteStreamTargetClientIds = new();
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

            if (!localFirstPersonPresenter.TryRecordLocalStream(out byte[] streamData) ||
                streamData.Length == 0 ||
                streamData.Length > MetaAvatarStreamMessage.MaxPayloadBytes)
            {
                return;
            }

            nextSendTime = now + minInterval;
            SubmitAvatarStreamServerRpc(new MetaAvatarStreamMessage(OwnerClientId, now, streamData));
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        void SubmitAvatarStreamServerRpc(MetaAvatarStreamMessage message)
        {
            if (!message.HasValidPayload)
                return;

            // Re-stamp the sender id server-side: a modified client could spoof any identity.
            message.SenderClientId = OwnerClientId;
            ClientRpcParams recipients = BuildRemoteStreamRecipients();
            if (remoteStreamTargetClientIds.Count > 0)
                ReceiveAvatarStreamClientRpc(message, recipients);
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        void ReceiveAvatarStreamClientRpc(MetaAvatarStreamMessage message, ClientRpcParams clientRpcParams = default)
        {
            if (!message.HasValidPayload)
                return;

            if (IsOwner || (NetworkManager != null && message.SenderClientId == NetworkManager.LocalClientId))
                return;

            remotePresenter ??= GetComponent<BlockiverseMetaAvatarPresenter>();
            remotePresenter?.ApplyRemoteStream(message.Payload);
        }

        ClientRpcParams BuildRemoteStreamRecipients()
        {
            remoteStreamTargetClientIds.Clear();

            if (NetworkManager != null)
            {
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                {
                    if (clientId != OwnerClientId)
                        remoteStreamTargetClientIds.Add(clientId);
                }
            }

            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = remoteStreamTargetClientIds,
                },
            };
        }

        void HideOwnerNetworkFallbackWhenLocalAvatarIsReady()
        {
            if (ownerNetworkFallbackRig == null || localFirstPersonPresenter == null)
                return;

            ownerNetworkFallbackRig.ConfigureFallbackProxy(true);
            ownerNetworkFallbackRig.ConfigureFirstPersonFallbackVisuals(false);
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

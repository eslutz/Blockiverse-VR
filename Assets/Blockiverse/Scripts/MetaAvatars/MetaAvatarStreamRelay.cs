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
        readonly List<MetaAvatarStreamMessage> _sendBuffer = new();
        readonly MetaAvatarStreamReassembler _reassembler = new();
        double nextSendTime;
        double nextPresenterSearchTime;
        uint localFrameSequence;
        double nextOversizeWarningTime;
        double LastRemoteStreamTime;

        const double OversizeWarningIntervalSeconds = 5.0;

        void Awake()
        {
            remotePresenter ??= GetComponent<BlockiverseMetaAvatarPresenter>();
            ownerNetworkFallbackRig = GetComponent<BlockiverseNetworkAvatarRig>();
        }

        public override void OnNetworkDespawn()
        {
            _reassembler.Clear();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            _reassembler.Clear();
            base.OnDestroy();
        }

        void LateUpdate()
        {
            if (!IsSpawned)
                return;

            if (!IsOwner)
            {
                if (LastRemoteStreamTime > 0.0)
                {
                    double now = Time.unscaledTimeAsDouble;
                    bool streamStale = (now - LastRemoteStreamTime) > 3.0;
                    if (ownerNetworkFallbackRig != null)
                        ownerNetworkFallbackRig.SetStreamStale(streamStale);
                }
                return;
            }

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

            double nowLocal = Time.unscaledTimeAsDouble;
            double minInterval = streamSendRateHz <= 0.0f ? 0.0f : 1.0f / streamSendRateHz;
            if (minInterval > 0.0f && nowLocal < nextSendTime)
                return;

            if (!localFirstPersonPresenter.TryRecordLocalStream(out byte[] streamData) ||
                streamData == null ||
                streamData.Length == 0)
            {
                // Empty captures are normal (avatar not rendering yet): nothing to send.
                return;
            }

            if (streamData.Length > MetaAvatarStreamMessage.MaxStreamBytes)
            {
                if (nowLocal >= nextOversizeWarningTime)
                {
                    nextOversizeWarningTime = nowLocal + OversizeWarningIntervalSeconds;
                    Debug.LogWarning($"[MetaAvatarStreamRelay] Dropping avatar stream of {streamData.Length} bytes (exceeds MaxStreamBytes={MetaAvatarStreamMessage.MaxStreamBytes}).");
                }

                return;
            }

            unchecked
            {
                localFrameSequence++;
            }

            int fragmentCount = MetaAvatarStreamReassembler.Fragment(
                OwnerClientId, nowLocal, localFrameSequence, streamData, _sendBuffer);
            if (fragmentCount == 0)
                return;

            nextSendTime = nowLocal + minInterval;
            for (int i = 0; i < _sendBuffer.Count; i++)
                SubmitAvatarStreamServerRpc(_sendBuffer[i]);
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        void SubmitAvatarStreamServerRpc(MetaAvatarStreamMessage message)
        {
            if (!message.HasValidPayload)
                return;

            // Re-stamp the sender id server-side: a modified client could spoof any identity.
            // Reconstruct so the fragment routing fields are preserved unchanged.
            var stamped = new MetaAvatarStreamMessage(
                OwnerClientId,
                message.SentTime,
                message.FrameSequence,
                message.FragmentIndex,
                message.FragmentCount,
                message.Payload);

            ClientRpcParams recipients = BuildRemoteStreamRecipients();
            if (remoteStreamTargetClientIds.Count > 0)
                ReceiveAvatarStreamClientRpc(stamped, recipients);
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        void ReceiveAvatarStreamClientRpc(MetaAvatarStreamMessage message, ClientRpcParams clientRpcParams = default)
        {
            if (!message.HasValidPayload)
                return;

            if (IsOwner || (NetworkManager != null && message.SenderClientId == NetworkManager.LocalClientId))
                return;

            if (!_reassembler.TryReassemble(message, out byte[] complete, out double sentTime))
                return;

            // Stored for Step 12 (staleness/hiding)
            LastRemoteStreamTime = sentTime;
            if (ownerNetworkFallbackRig != null)
            {
                ownerNetworkFallbackRig.SetStreamStale(false);
                ownerNetworkFallbackRig.SetMetaAvatarAvailable(true);
            }

            remotePresenter ??= GetComponent<BlockiverseMetaAvatarPresenter>();
            remotePresenter?.ApplyRemoteStream(complete);
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

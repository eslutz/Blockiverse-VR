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

        // Owner-published Meta user id: remote peers load the owner's real profile avatar
        // from it instead of posing a generic default. Zero means "not resolved" — a child
        // account (or a failed platform chain) never publishes one, by policy.
        readonly NetworkVariable<ulong> ownerMetaUserId = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        BlockiverseMetaAvatarPresenter localFirstPersonPresenter;
        BlockiverseNetworkAvatarRig ownerNetworkFallbackRig;
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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsOwner)
            {
                ownerMetaUserId.OnValueChanged += OnOwnerMetaUserIdChanged;
                ApplyOwnerMetaUserId(ownerMetaUserId.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            ownerMetaUserId.OnValueChanged -= OnOwnerMetaUserIdChanged;
            _reassembler.Clear();
            base.OnNetworkDespawn();
        }

        void OnOwnerMetaUserIdChanged(ulong previousValue, ulong newValue)
        {
            ApplyOwnerMetaUserId(newValue);
        }

        void ApplyOwnerMetaUserId(ulong userId)
        {
            if (userId == 0)
                return;

            remotePresenter ??= GetComponent<BlockiverseMetaAvatarPresenter>();
            remotePresenter?.ConfigureRemoteUserAvatar(userId);
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

            if (ownerMetaUserId.Value == 0 &&
                localFirstPersonPresenter.TryGetLocalMetaUserId(out ulong localMetaUserId))
            {
                ownerMetaUserId.Value = localMetaUserId;
            }

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
                SubmitAvatarStreamRpc(_sendBuffer[i]);
        }

        // InvokePermission.Owner preserves the old [ServerRpc] default (which required
        // ownership): only the player this relay belongs to may push its avatar stream.
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void SubmitAvatarStreamRpc(MetaAvatarStreamMessage message)
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

            ReceiveAvatarStreamRpc(stamped);
        }

        // SendTo.NotOwner reproduces the old hand-built recipient list (every connected client
        // except the owner, host included) without the per-fragment list rebuild.
        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        void ReceiveAvatarStreamRpc(MetaAvatarStreamMessage message)
        {
            if (!message.HasValidPayload)
                return;

            if (IsOwner || (NetworkManager != null && message.SenderClientId == NetworkManager.LocalClientId))
                return;

            if (!_reassembler.TryReassemble(message, out byte[] complete, out _))
                return;

            // Stored for Step 12 (staleness/hiding). Stamp receiver-local time: the
            // message's SentTime is the sender's process clock, which is unrelated to
            // this peer's clock, so comparing it against our unscaled time would mark
            // healthy streams stale (or mask stopped ones) by the uptime difference.
            LastRemoteStreamTime = Time.unscaledTimeAsDouble;
            if (ownerNetworkFallbackRig != null)
                ownerNetworkFallbackRig.SetStreamStale(false);

            // Availability is NOT forced true here: ApplyRemoteStream -> RefreshAvatarState
            // asks the provider, which also requires the entity to be renderable. Forcing it
            // used to hide the proxy while the entity had no drawable model yet, leaving the
            // remote player invisible.
            remotePresenter ??= GetComponent<BlockiverseMetaAvatarPresenter>();
            remotePresenter?.ApplyRemoteStream(complete);
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

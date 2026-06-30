using System;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// A single fragment of an avatar stream frame. Large frames are split across multiple
    /// fragments because the relay uses <see cref="RpcDelivery.Unreliable"/>, which NGO does
    /// not fragment — each message must fit under the transport MTU.
    /// </summary>
    public struct MetaAvatarStreamMessage : INetworkSerializable
    {
        /// <summary>Maximum payload bytes for a single fragment. Must stay well under the unreliable-transport MTU.</summary>
        public const int MaxFragmentBytes = 1024;

        /// <summary>Maximum total bytes of a reassembled avatar stream frame.</summary>
        public const int MaxStreamBytes = 128 * 1024;

        public ulong SenderClientId;
        public double SentTime;

        /// <summary>The logical stream frame this fragment belongs to.</summary>
        public uint FrameSequence;

        /// <summary>This fragment's index within its frame, 0..FragmentCount-1.</summary>
        public ushort FragmentIndex;

        /// <summary>Total number of fragments composing the frame.</summary>
        public ushort FragmentCount;

        public byte[] Payload;

        public MetaAvatarStreamMessage(
            ulong senderClientId,
            double sentTime,
            uint frameSequence,
            ushort fragmentIndex,
            ushort fragmentCount,
            byte[] payload)
        {
            SenderClientId = senderClientId;
            SentTime = sentTime;
            FrameSequence = frameSequence;
            FragmentIndex = fragmentIndex;
            FragmentCount = fragmentCount;
            Payload = payload ?? Array.Empty<byte>();
        }

        public bool HasValidPayload =>
            Payload != null &&
            Payload.Length > 0 &&
            Payload.Length <= MaxFragmentBytes &&
            FragmentCount >= 1 &&
            FragmentIndex < FragmentCount;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SenderClientId);
            serializer.SerializeValue(ref SentTime);
            serializer.SerializeValue(ref FrameSequence);
            serializer.SerializeValue(ref FragmentIndex);
            serializer.SerializeValue(ref FragmentCount);

            int length = serializer.IsReader ? 0 : Mathf.Clamp(Payload?.Length ?? 0, 0, MaxFragmentBytes);
            serializer.SerializeValue(ref length);
            length = Mathf.Clamp(length, 0, MaxFragmentBytes);

            // Bulk-copy the payload instead of per-byte SerializeValue calls: fragments run
            // at 15 Hz, so the byte loop costs thousands of calls per message.
            if (serializer.IsReader)
            {
                if (length == 0)
                {
                    Payload = Array.Empty<byte>();
                }
                else
                {
                    if (Payload == null || Payload.Length != length)
                        Payload = new byte[length];

                    serializer.GetFastBufferReader().ReadBytesSafe(ref Payload, length);
                }
            }
            else if (length > 0)
            {
                serializer.GetFastBufferWriter().WriteBytesSafe(Payload, length);
            }
        }
    }
}

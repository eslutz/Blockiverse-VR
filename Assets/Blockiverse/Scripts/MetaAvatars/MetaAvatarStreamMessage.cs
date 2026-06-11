using System;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    public struct MetaAvatarStreamMessage : INetworkSerializable
    {
        public const int MaxPayloadBytes = 64 * 1024;

        public ulong SenderClientId;
        public double SentTime;
        public byte[] Payload;

        public MetaAvatarStreamMessage(ulong senderClientId, double sentTime, byte[] payload)
        {
            SenderClientId = senderClientId;
            SentTime = sentTime;
            Payload = payload ?? Array.Empty<byte>();
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SenderClientId);
            serializer.SerializeValue(ref SentTime);

            int length = serializer.IsReader ? 0 : Mathf.Clamp(Payload?.Length ?? 0, 0, MaxPayloadBytes);
            serializer.SerializeValue(ref length);
            length = Mathf.Clamp(length, 0, MaxPayloadBytes);

            // Bulk-copy the payload instead of per-byte SerializeValue calls: streams run
            // up to 64 KiB at 15 Hz, so the byte loop costs tens of thousands of calls per message.
            if (serializer.IsReader)
            {
                Payload = length == 0 ? Array.Empty<byte>() : new byte[length];
                if (length > 0)
                    serializer.GetFastBufferReader().ReadBytesSafe(ref Payload, length);
            }
            else if (length > 0)
            {
                serializer.GetFastBufferWriter().WriteBytesSafe(Payload, length);
            }
        }
    }
}

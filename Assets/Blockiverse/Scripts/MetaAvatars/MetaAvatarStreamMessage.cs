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

            if (serializer.IsReader)
                Payload = new byte[length];

            for (int i = 0; i < length; i++)
                serializer.SerializeValue(ref Payload[i]);
        }
    }
}

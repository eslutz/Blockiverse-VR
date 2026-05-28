using System;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    public enum MetaAvatarPresentationMode
    {
        LocalFirstPerson,
        RemoteThirdPerson
    }

    [Serializable]
    public readonly struct MetaAvatarTrackingSources
    {
        public MetaAvatarTrackingSources(Transform head, Transform leftHand, Transform rightHand)
        {
            Head = head;
            LeftHand = leftHand;
            RightHand = rightHand;
        }

        public static MetaAvatarTrackingSources Empty => new(null, null, null);

        public Transform Head { get; }
        public Transform LeftHand { get; }
        public Transform RightHand { get; }
    }

    public interface IBlockiverseMetaAvatarProvider
    {
        bool IsAvatarReady { get; }
        string FallbackReason { get; }
        void Configure(MetaAvatarTrackingSources sources, MetaAvatarPresentationMode mode, bool hideFirstPersonHead);
        void TickProvider();
        bool TryRecordStream(out byte[] streamData);
        void ApplyStreamData(byte[] streamData);
    }
}

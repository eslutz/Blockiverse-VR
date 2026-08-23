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

        // Default implementations keep test fakes compiling: only providers that talk to the
        // Meta platform (and the editor mock, for tests) have a real user identity to offer.
        bool TryGetLocalUserId(out ulong userId)
        {
            userId = 0;
            return false;
        }

        void ConfigureRemoteUserAvatar(ulong userId)
        {
        }

        /// <summary>Show or hide the presented avatar. Implementations must never toggle
        /// the entity GameObject's active state: the Avatar SDK only advances loading while
        /// the entity behaviour is active, so deactivating a hidden-but-loading avatar
        /// deadlocks it (it can never become ready enough to be shown again).</summary>
        void SetEntityVisible(bool visible)
        {
        }
    }
}

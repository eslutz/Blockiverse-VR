#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Editor/test-only mock implementation of <see cref="IBlockiverseMetaAvatarProvider"/>.
    /// Lets the presenter, relay, and fallback-switching logic run in Editor PlayMode (and CI)
    /// without the Meta Avatar SDK, which only initializes on Quest hardware behind the
    /// <c>#if UNITY_ANDROID &amp;&amp; !UNITY_EDITOR</c> guards in <see cref="MetaHorizonAvatarProvider"/>.
    /// Compiled out of player builds that do not include tests, so it can never reach device.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseEditorMockMetaAvatarProvider : MonoBehaviour, IBlockiverseMetaAvatarProvider
    {
        const string NotReadyReason = "Mock Meta avatar provider is not ready.";

        /// <summary>Drives <see cref="IBlockiverseMetaAvatarProvider.IsAvatarReady"/>; set by tests.</summary>
        public bool IsAvatarReady { get; set; }

        public string FallbackReason { get; set; } = NotReadyReason;

        /// <summary>Bytes returned from <see cref="TryRecordStream"/> when the avatar is ready.</summary>
        public byte[] RecordedStream { get; set; } = Array.Empty<byte>();

        /// <summary>Last stream handed to <see cref="ApplyStreamData"/>.</summary>
        public byte[] LastAppliedStream { get; private set; } = Array.Empty<byte>();

        public MetaAvatarPresentationMode Mode { get; private set; }
        public MetaAvatarTrackingSources Sources { get; private set; }
        public bool HideFirstPersonHead { get; private set; }
        public int TickCount { get; private set; }

        /// <summary>Convenience: fill <see cref="RecordedStream"/> with a deterministic pattern of the given size.</summary>
        public void SetRecordedStreamSize(int byteCount)
        {
            byte[] data = new byte[Mathf.Max(0, byteCount)];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i * 31 + 7);

            RecordedStream = data;
        }

        public void Configure(MetaAvatarTrackingSources sources, MetaAvatarPresentationMode mode, bool hideFirstPersonHead)
        {
            Sources = sources;
            Mode = mode;
            HideFirstPersonHead = hideFirstPersonHead;
        }

        public void TickProvider()
        {
            TickCount++;
        }

        public bool TryRecordStream(out byte[] streamData)
        {
            streamData = RecordedStream ?? Array.Empty<byte>();
            return IsAvatarReady && streamData.Length > 0;
        }

        public void ApplyStreamData(byte[] streamData)
        {
            LastAppliedStream = streamData ?? Array.Empty<byte>();
            IsAvatarReady = LastAppliedStream.Length > 0;
            FallbackReason = IsAvatarReady ? string.Empty : NotReadyReason;
        }
    }
}
#endif
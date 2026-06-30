using System;
using System.Collections.Generic;

namespace Blockiverse.MetaAvatars
{
    /// <summary>
    /// Splits avatar stream frames into <see cref="MetaAvatarStreamMessage"/> fragments and
    /// reassembles them on the receiving side. Pure data logic with no Unity networking
    /// dependency beyond the message struct, so it is fully unit-testable in EditMode.
    /// </summary>
    public sealed class MetaAvatarStreamReassembler
    {
        sealed class PartialFrame
        {
            public uint FrameSequence;
            public ushort FragmentCount;
            public int ReceivedCount;
            public int TotalLength;
            public double SentTime;
            public byte[][] Fragments = Array.Empty<byte[]>();
            public bool[] Received = Array.Empty<bool>();

            public void Reset(in MetaAvatarStreamMessage fragment)
            {
                FrameSequence = fragment.FrameSequence;
                FragmentCount = fragment.FragmentCount;
                ReceivedCount = 0;
                TotalLength = 0;
                SentTime = fragment.SentTime;

                if (Fragments.Length < FragmentCount)
                {
                    Fragments = new byte[FragmentCount][];
                    Received = new bool[FragmentCount];
                }
                else
                {
                    for (int i = 0; i < FragmentCount; i++)
                    {
                        Fragments[i] = null;
                        Received[i] = false;
                    }
                }
            }
        }

        readonly Dictionary<ulong, PartialFrame> _partialBySender = new();

        /// <summary>
        /// Splits <paramref name="data"/> into fragments appended to <paramref name="output"/> (which is cleared first).
        /// Returns the fragment count, or 0 when the data is null/empty or exceeds <see cref="MetaAvatarStreamMessage.MaxStreamBytes"/>.
        /// </summary>
        public static int Fragment(
            ulong senderClientId,
            double sentTime,
            uint frameSequence,
            byte[] data,
            List<MetaAvatarStreamMessage> output)
        {
            output.Clear();

            if (data == null || data.Length == 0)
                return 0;

            if (data.Length > MetaAvatarStreamMessage.MaxStreamBytes)
                return 0;

            int fragmentSize = MetaAvatarStreamMessage.MaxFragmentBytes;
            int count = (data.Length + fragmentSize - 1) / fragmentSize;

            for (int i = 0; i < count; i++)
            {
                int offset = i * fragmentSize;
                int length = Math.Min(fragmentSize, data.Length - offset);
                byte[] payload = new byte[length];
                Array.Copy(data, offset, payload, 0, length);

                output.Add(new MetaAvatarStreamMessage(
                    senderClientId,
                    sentTime,
                    frameSequence,
                    (ushort)i,
                    (ushort)count,
                    payload));
            }

            return count;
        }

        /// <summary>
        /// Feeds a received fragment. Returns true and yields the reassembled stream once every
        /// fragment of the current frame has arrived. Newest frame wins: a fragment from a newer
        /// <see cref="MetaAvatarStreamMessage.FrameSequence"/> discards an incomplete older frame,
        /// while fragments from an older frame are ignored.
        /// </summary>
        public bool TryReassemble(in MetaAvatarStreamMessage fragment, out byte[] complete, out double sentTime)
        {
            complete = null;
            sentTime = 0.0;

            if (!fragment.HasValidPayload)
                return false;

            if (!_partialBySender.TryGetValue(fragment.SenderClientId, out PartialFrame partial))
            {
                partial = new PartialFrame();
                partial.Reset(fragment);
                _partialBySender[fragment.SenderClientId] = partial;
            }
            else if (fragment.FrameSequence > partial.FrameSequence)
            {
                // Newer frame: abandon the incomplete previous one (tolerates lost fragments).
                partial.Reset(fragment);
            }
            else if (fragment.FrameSequence < partial.FrameSequence)
            {
                // Stale fragment from a frame we've already moved past.
                return false;
            }

            if (fragment.FragmentCount != partial.FragmentCount)
                return false;

            if (fragment.FragmentIndex >= partial.FragmentCount)
                return false;

            if (partial.Received[fragment.FragmentIndex])
                return false;

            if (partial.TotalLength + fragment.Payload.Length > MetaAvatarStreamMessage.MaxStreamBytes)
                return false;

            partial.Received[fragment.FragmentIndex] = true;
            partial.Fragments[fragment.FragmentIndex] = fragment.Payload;
            partial.TotalLength += fragment.Payload.Length;
            partial.ReceivedCount++;

            if (partial.ReceivedCount < partial.FragmentCount)
                return false;

            byte[] result = new byte[partial.TotalLength];
            int writeOffset = 0;
            for (int i = 0; i < partial.FragmentCount; i++)
            {
                byte[] piece = partial.Fragments[i];
                Array.Copy(piece, 0, result, writeOffset, piece.Length);
                writeOffset += piece.Length;
            }

            complete = result;
            sentTime = partial.SentTime;

            // Drop the completed partial so the next frame starts clean.
            _partialBySender.Remove(fragment.SenderClientId);
            return true;
        }

        /// <summary>Discards any in-progress reassembly for a single sender.</summary>
        public void Forget(ulong senderClientId)
        {
            _partialBySender.Remove(senderClientId);
        }

        /// <summary>Discards all in-progress reassembly state.</summary>
        public void Clear()
        {
            _partialBySender.Clear();
        }
    }
}

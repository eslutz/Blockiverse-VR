using System.Collections.Generic;
using Blockiverse.MetaAvatars;
using NUnit.Framework;

namespace Blockiverse.Tests.MetaAvatars.EditMode
{
    public sealed class MetaAvatarStreamReassemblerEditModeTests
    {
        const ulong Sender = 42UL;

        static byte[] MakePattern(int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)(i * 31 + 7);
            return data;
        }

        static byte[] FragmentAndReassemble(MetaAvatarStreamReassembler reassembler, ulong sender, uint frameSequence, byte[] data, out int fragmentCount)
        {
            var fragments = new List<MetaAvatarStreamMessage>();
            fragmentCount = MetaAvatarStreamReassembler.Fragment(sender, 1.0, frameSequence, data, fragments);

            byte[] completed = null;
            foreach (MetaAvatarStreamMessage fragment in fragments)
            {
                if (reassembler.TryReassemble(fragment, out byte[] complete, out double _))
                    completed = complete;
            }

            return completed;
        }

        [Test]
        public void RoundTripsSmallSingleFragmentStream()
        {
            var reassembler = new MetaAvatarStreamReassembler();
            byte[] data = MakePattern(64);

            byte[] completed = FragmentAndReassemble(reassembler, Sender, 1, data, out int fragmentCount);

            Assert.That(fragmentCount, Is.EqualTo(1));
            Assert.That(completed, Is.Not.Null);
            CollectionAssert.AreEqual(data, completed);
        }

        [Test]
        public void RoundTripsLargeMultiFragmentStream()
        {
            var reassembler = new MetaAvatarStreamReassembler();
            byte[] data = MakePattern(20000);

            byte[] completed = FragmentAndReassemble(reassembler, Sender, 1, data, out int fragmentCount);

            Assert.That(fragmentCount, Is.GreaterThan(1));
            Assert.That(completed, Is.Not.Null);
            CollectionAssert.AreEqual(data, completed);
        }

        [Test]
        public void HandlesStreamAtCapAndRejectsOversize()
        {
            var reassembler = new MetaAvatarStreamReassembler();
            byte[] atCap = MakePattern(MetaAvatarStreamMessage.MaxStreamBytes);

            byte[] completed = FragmentAndReassemble(reassembler, Sender, 1, atCap, out int fragmentCount);

            Assert.That(fragmentCount, Is.GreaterThan(1));
            Assert.That(completed, Is.Not.Null);
            CollectionAssert.AreEqual(atCap, completed);

            byte[] oversize = MakePattern(MetaAvatarStreamMessage.MaxStreamBytes + 1);
            var output = new List<MetaAvatarStreamMessage>();
            int oversizeCount = MetaAvatarStreamReassembler.Fragment(Sender, 1.0, 2, oversize, output);

            Assert.That(oversizeCount, Is.EqualTo(0));
            Assert.That(output, Is.Empty);
        }

        [Test]
        public void DroppedFragmentNeverCompletesButNewerFrameStillReassembles()
        {
            var reassembler = new MetaAvatarStreamReassembler();
            byte[] firstData = MakePattern(20000);

            var firstFragments = new List<MetaAvatarStreamMessage>();
            int firstCount = MetaAvatarStreamReassembler.Fragment(Sender, 1.0, 1, firstData, firstFragments);
            Assert.That(firstCount, Is.GreaterThan(1));

            // Feed all fragments except one — frame 1 must never complete.
            for (int i = 0; i < firstFragments.Count; i++)
            {
                if (i == 1)
                    continue;

                Assert.That(reassembler.TryReassemble(firstFragments[i], out byte[] _, out double _), Is.False);
            }

            // A newer frame arrives complete and reassembles despite the abandoned frame 1.
            byte[] secondData = MakePattern(15000);
            byte[] secondCompleted = FragmentAndReassemble(reassembler, Sender, 2, secondData, out int secondCount);

            Assert.That(secondCount, Is.GreaterThan(1));
            Assert.That(secondCompleted, Is.Not.Null);
            CollectionAssert.AreEqual(secondData, secondCompleted);
        }

        [Test]
        public void FragmentOfNullOrEmptyReturnsZero()
        {
            var output = new List<MetaAvatarStreamMessage>();

            Assert.That(MetaAvatarStreamReassembler.Fragment(Sender, 1.0, 1, null, output), Is.EqualTo(0));
            Assert.That(output, Is.Empty);

            Assert.That(MetaAvatarStreamReassembler.Fragment(Sender, 1.0, 1, new byte[0], output), Is.EqualTo(0));
            Assert.That(output, Is.Empty);
        }
    }
}

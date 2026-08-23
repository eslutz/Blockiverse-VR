using Blockiverse.Networking;
using NUnit.Framework;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class BlockiverseServerAuthProtocolEditModeTests
    {
        [Test]
        public void CorrectSecretVerifies()
        {
            byte[] nonce = BlockiverseServerAuthProtocol.CreateNonce();
            byte[] response = BlockiverseServerAuthProtocol.ComputeResponse("room-key", nonce, 7);

            Assert.That(BlockiverseServerAuthProtocol.VerifyResponse("room-key", nonce, 7, response), Is.True);
        }

        [Test]
        public void WrongSecretIsRejected()
        {
            byte[] nonce = BlockiverseServerAuthProtocol.CreateNonce();
            byte[] response = BlockiverseServerAuthProtocol.ComputeResponse("wrong", nonce, 7);

            Assert.That(BlockiverseServerAuthProtocol.VerifyResponse("room-key", nonce, 7, response), Is.False);
        }

        [Test]
        public void ResponseIsBoundToTheNonce()
        {
            // The property that kills replay: a response captured from one connection is worthless
            // on another, because the server picked a fresh nonce.
            byte[] firstNonce = BlockiverseServerAuthProtocol.CreateNonce();
            byte[] secondNonce = BlockiverseServerAuthProtocol.CreateNonce();
            byte[] replayed = BlockiverseServerAuthProtocol.ComputeResponse("room-key", firstNonce, 7);

            Assert.That(BlockiverseServerAuthProtocol.VerifyResponse("room-key", secondNonce, 7, replayed), Is.False);
        }

        [Test]
        public void ResponseIsBoundToTheClientId()
        {
            // Two clients handed the same nonce (a server bug this guards against becoming
            // exploitable) still cannot present each other's answers.
            byte[] nonce = BlockiverseServerAuthProtocol.CreateNonce();
            byte[] othersResponse = BlockiverseServerAuthProtocol.ComputeResponse("room-key", nonce, 8);

            Assert.That(BlockiverseServerAuthProtocol.VerifyResponse("room-key", nonce, 7, othersResponse), Is.False);
        }

        [Test]
        public void NoncesAreUnique()
        {
            Assert.That(BlockiverseServerAuthProtocol.CreateNonce(), Is.Not.EqualTo(BlockiverseServerAuthProtocol.CreateNonce()));
        }

        [Test]
        public void MissingInputsNeverVerify()
        {
            byte[] nonce = BlockiverseServerAuthProtocol.CreateNonce();

            Assert.That(BlockiverseServerAuthProtocol.ComputeResponse(null, nonce, 1), Is.Null);
            Assert.That(BlockiverseServerAuthProtocol.ComputeResponse(string.Empty, nonce, 1), Is.Null);
            Assert.That(BlockiverseServerAuthProtocol.ComputeResponse("secret", null, 1), Is.Null);
            Assert.That(BlockiverseServerAuthProtocol.ComputeResponse("secret", new byte[4], 1), Is.Null);
            Assert.That(BlockiverseServerAuthProtocol.VerifyResponse("secret", nonce, 1, null), Is.False);
            Assert.That(BlockiverseServerAuthProtocol.VerifyResponse(null, nonce, 1, new byte[32]), Is.False);
        }
    }
}

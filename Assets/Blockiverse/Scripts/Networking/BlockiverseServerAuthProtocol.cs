using System;
using System.Security.Cryptography;
using System.Text;

namespace Blockiverse.Networking
{
    /// <summary>
    /// The pure cryptography of the post-connect join-secret challenge. Engine-free and stateless
    /// so every property is EditMode-testable without a session.
    ///
    /// Why challenge-response rather than a static HMAC over the approval payload: the payload
    /// body is fully predictable, so a static signature is replayable and permits an offline
    /// dictionary attack from one captured join. A server-chosen random nonce kills both — the
    /// response is worthless for any other connection, and an observer cannot grind the secret
    /// offline without also observing the nonce AND the response for every guess they make online.
    ///
    /// Why post-connect rather than in the approval payload: Netcode's connection approval is a
    /// single client-to-server message with no round trip, so there is nowhere to put a server
    /// nonce. The challenge therefore rides named messages immediately after connect, and the
    /// server holds back world state and disconnects on timeout or mismatch.
    /// </summary>
    public static class BlockiverseServerAuthProtocol
    {
        public const int NonceBytes = 32;
        public const int ResponseBytes = 32;

        // Domain separation: the same secret must never produce a value valid in another context.
        static readonly byte[] Context = Encoding.ASCII.GetBytes("blockiverse-join-v1");

        public static byte[] CreateNonce()
        {
            var nonce = new byte[NonceBytes];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(nonce);
            return nonce;
        }

        /// <summary>HMAC-SHA256(secret, context || nonce || clientId). Null on missing input.</summary>
        public static byte[] ComputeResponse(string secret, byte[] nonce, ulong clientId)
        {
            if (string.IsNullOrEmpty(secret) || nonce == null || nonce.Length != NonceBytes)
                return null;

            var message = new byte[Context.Length + NonceBytes + sizeof(ulong)];
            Buffer.BlockCopy(Context, 0, message, 0, Context.Length);
            Buffer.BlockCopy(nonce, 0, message, Context.Length, NonceBytes);
            for (int i = 0; i < sizeof(ulong); i++)
                message[Context.Length + NonceBytes + i] = (byte)(clientId >> (8 * i));

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
                return hmac.ComputeHash(message);
        }

        /// <summary>Constant-time verification; false on any missing or malformed input.</summary>
        public static bool VerifyResponse(string secret, byte[] nonce, ulong clientId, byte[] response)
        {
            byte[] expected = ComputeResponse(secret, nonce, clientId);
            if (expected == null || response == null || response.Length != expected.Length)
                return false;

            // Constant-time: a byte-by-byte early-out compare leaks how many leading bytes
            // matched through response timing.
            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
                diff |= expected[i] ^ response[i];
            return diff == 0;
        }
    }
}

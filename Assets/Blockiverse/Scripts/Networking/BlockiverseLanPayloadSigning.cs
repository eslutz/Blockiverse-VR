using System;
using System.Security.Cryptography;
using System.Text;

namespace Blockiverse.Networking
{
    /// <summary>
    /// HMAC signing shared by the connection-approval payload and the LAN discovery beacon.
    /// Both authenticate the same thing — "this peer knows the session's join code" — so they
    /// share one implementation rather than two copies of the same crypto.
    /// </summary>
    public static class BlockiverseLanPayloadSigning
    {
        public static byte[] ComputeSignature(string body, string joinCode)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(joinCode ?? string.Empty));
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(body ?? string.Empty));
        }

        public static string ComputeSignatureBase64(string body, string joinCode) =>
            Convert.ToBase64String(ComputeSignature(body, joinCode));

        /// <summary>
        /// Length-independent comparison. Signature checks compare attacker-influenced bytes, so
        /// they must not short-circuit on the first mismatch.
        /// </summary>
        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];

            return difference == 0;
        }

        public static bool VerifySignatureBase64(string body, string joinCode, string signatureBase64)
        {
            byte[] actual;

            try
            {
                actual = Convert.FromBase64String(signatureBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                return false;
            }

            return FixedTimeEquals(ComputeSignature(body, joinCode), actual);
        }
    }
}

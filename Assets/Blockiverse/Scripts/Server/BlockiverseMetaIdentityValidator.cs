using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Blockiverse.Core;

namespace Blockiverse.Server
{
    /// <summary>
    /// Server-side verification of a client's Meta identity proof against Meta's
    /// user_nonce_validate endpoint (the User Verification flow: the headset fetches a one-shot
    /// nonce with Users.GetUserProof, sends it with its user id, and only Meta can say whether the
    /// pair is genuine). This is what turns "a GUID the client made up" into a real, revocable,
    /// per-account identity.
    ///
    /// The HTTP transport is injected so every decision in this class is EditMode-testable without
    /// the network; the production transport is one HttpClient POST. Meta's nonce is single-use,
    /// so a captured request cannot be replayed.
    /// </summary>
    public sealed class BlockiverseMetaIdentityValidator
    {
        public const string ValidationEndpoint = "https://graph.oculus.com/user_nonce_validate";

        readonly string appAccessToken;
        readonly Func<string, IReadOnlyDictionary<string, string>, Task<string>> postAsync;

        /// <param name="appId">The Meta app id.</param>
        /// <param name="appSecret">The app secret. Combined into the OC app access token; never logged.</param>
        /// <param name="postOverride">Test seam: (url, form fields) -> response body.</param>
        public BlockiverseMetaIdentityValidator(
            string appId,
            string appSecret,
            Func<string, IReadOnlyDictionary<string, string>, Task<string>> postOverride = null)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("Meta app id must not be empty.", nameof(appId));
            if (string.IsNullOrWhiteSpace(appSecret))
                throw new ArgumentException("Meta app secret must not be empty.", nameof(appSecret));

            appAccessToken = $"OC|{appId.Trim()}|{appSecret.Trim()}";
            postAsync = postOverride ?? PostWithHttpClient;
        }

        /// <summary>
        /// Validates one (userId, nonce) pair. The completion runs on the calling task's thread;
        /// callers marshal to the main thread themselves (the auth gate does).
        /// Any transport failure is INVALID, not valid: an unreachable Meta endpoint must never
        /// become an open door.
        /// </summary>
        public void Validate(ulong metaUserId, string nonce, Action<bool> completed)
        {
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));

            if (metaUserId == 0 || string.IsNullOrWhiteSpace(nonce))
            {
                completed(false);
                return;
            }

            var fields = new Dictionary<string, string>
            {
                ["access_token"] = appAccessToken,
                ["nonce"] = nonce,
                ["user_id"] = metaUserId.ToString(),
            };

            Task.Run(async () =>
            {
                bool valid = false;
                try
                {
                    string body = await postAsync(ValidationEndpoint, fields).ConfigureAwait(false);
                    valid = ParseIsValid(body);
                }
                catch (Exception exception)
                {
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Networking,
                        $"Meta identity validation failed for user {metaUserId}: {exception.Message}");
                }

                completed(valid);
            });
        }

        /// <summary>
        /// The endpoint answers {"is_valid": true} (or false, or an error object). Anything that
        /// is not an explicit true is invalid. Exposed for tests.
        /// </summary>
        public static bool ParseIsValid(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return false;

            // Tolerant of whitespace, intolerant of everything else: an error payload, a
            // quoted "true", or a truncated body must all fail.
            string compact = responseBody.Replace(" ", string.Empty)
                .Replace("\n", string.Empty).Replace("\r", string.Empty).Replace("\t", string.Empty);
            return compact.Contains("\"is_valid\":true");
        }

        static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        static async Task<string> PostWithHttpClient(string url, IReadOnlyDictionary<string, string> fields)
        {
            using var content = new FormUrlEncodedContent(fields);
            using HttpResponseMessage response = await SharedClient.PostAsync(url, content).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
    }
}

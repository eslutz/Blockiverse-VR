using System;
using Blockiverse.Core;
using Blockiverse.Networking;
using UnityEngine;

namespace Blockiverse.MetaPlatform
{
    /// <summary>
    /// Client half of Meta identity verification: fetches the signed-in account's id and a
    /// one-shot proof nonce (Users.GetUserProof) when a server's join challenge asks for identity.
    /// Registered on <see cref="BlockiverseServerAuthGate.IdentityProofSource"/> at startup;
    /// outside the Quest Android runtime it reports "unavailable" ((0, null)) and the server names
    /// the refusal, rather than the join dying on a timeout.
    /// </summary>
    public sealed class BlockiverseMetaIdentityProofSource : IBlockiverseIdentityProofSource
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // The gate never asks for a proof unless the server it is joining demands identity,
            // so registering unconditionally costs nothing on LAN sessions.
            BlockiverseServerAuthGate.IdentityProofSource ??= new BlockiverseMetaIdentityProofSource();
        }

        public void RequestProof(Action<ulong, string> completed)
        {
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                Oculus.Platform.Core.Initialize();

                // Two platform round trips: who is signed in, then a proof only Meta can mint for
                // that account. Either failing means no identity, which the server rejects with a
                // named reason.
                Oculus.Platform.Users.GetLoggedInUser().OnComplete(userMessage =>
                {
                    if (userMessage == null || userMessage.IsError || userMessage.Data == null)
                    {
                        completed(0, null);
                        return;
                    }

                    ulong userId = userMessage.Data.ID;
                    Oculus.Platform.Users.GetUserProof().OnComplete(proofMessage =>
                    {
                        if (proofMessage == null || proofMessage.IsError || proofMessage.Data == null ||
                            string.IsNullOrEmpty(proofMessage.Data.Value))
                        {
                            completed(0, null);
                            return;
                        }

                        completed(userId, proofMessage.Data.Value);
                    });
                });
            }
            catch (Exception exception)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Networking,
                    $"Could not fetch a Meta identity proof: {exception.Message}");
                completed(0, null);
            }
#else
            completed(0, null);
#endif
        }
    }
}

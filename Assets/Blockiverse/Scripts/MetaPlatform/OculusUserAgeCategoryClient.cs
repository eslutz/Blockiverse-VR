using System;
using Oculus.Platform;
using Oculus.Platform.Models;
using UnityEngine;
using OculusCore = Oculus.Platform.Core;

namespace Blockiverse.MetaPlatform
{
    public sealed class OculusUserAgeCategoryClient : IUserAgeCategoryClient
    {
        public void Get(Action<BlockiverseUserAgeCategoryState> completed)
        {
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                OculusCore.Initialize();
                Request<UserAccountAgeCategory> request = UserAgeCategory.Get();
                if (request == null)
                {
                    completed(BlockiverseUserAgeCategoryState.Unknown(
                        BlockiverseUserAgeCategorySource.Error,
                        "Meta Platform returned no age category request."));
                    return;
                }

                request.OnComplete(message => HandleCompleted(message, completed));
            }
            catch (Exception exception)
            {
                completed(BlockiverseUserAgeCategoryState.Unknown(
                    BlockiverseUserAgeCategorySource.Error,
                    exception.Message));
            }
#else
            completed(BlockiverseUserAgeCategoryState.Unknown(
                BlockiverseUserAgeCategorySource.UnsupportedRuntime,
                "Meta user age category is only available in Quest Android runtime."));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static void HandleCompleted(
            Message<UserAccountAgeCategory> message,
            Action<BlockiverseUserAgeCategoryState> completed)
        {
            if (message.IsError)
            {
                Error error = message.GetError();
                completed(BlockiverseUserAgeCategoryState.Unknown(
                    BlockiverseUserAgeCategorySource.Error,
                    error?.Message ?? "Meta user age category request failed."));
                return;
            }

            completed(new BlockiverseUserAgeCategoryState(
                Convert(message.Data.AgeCategory),
                BlockiverseUserAgeCategorySource.LiveApi,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                "Meta user age category resolved."));
        }
#endif

        static BlockiverseUserAgeCategory Convert(AccountAgeCategory category)
        {
            return category switch
            {
                AccountAgeCategory.Ch => BlockiverseUserAgeCategory.Child,
                AccountAgeCategory.Tn => BlockiverseUserAgeCategory.Teen,
                AccountAgeCategory.Ad => BlockiverseUserAgeCategory.Adult,
                _ => BlockiverseUserAgeCategory.Unknown,
            };
        }
    }
}

/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */


#if USING_META_XR_VOICE_SDK

using System.Collections.Generic;
using System.Linq;
using Meta.XR.Telemetry;
using UnityEngine;
using System.Threading.Tasks;
using Meta.WitAi.Data;
using Meta.WitAi.Windows;
using Meta.WitAi.Data.Configuration;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Meta.XR.BuildingBlocks.Editor
{
    /// <summary>
    /// Utility class for Voice SDK building blocks that provides Wit.ai configuration setup and validation.
    /// </summary>
    public class VoiceBlocksUtils
    {
        /// <summary>
        /// Ensures a valid Wit.ai configuration exists by opening the setup window if no client access token is found.
        /// </summary>
        /// <returns>A task that completes when the Wit.ai configuration is available.</returns>
        public static async Task GetWitConfig()
        {
            if (HasClientAccessToken())
            {
                return;
            }
            var tcs = new TaskCompletionSource<WitConfiguration>();
            WitWindowUtility.OpenSetupWindow(tcs.SetResult);
            await tcs.Task;

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 5000)
            {
                if (HasClientAccessToken())
                {
                    Debug.Log($"WitConfiguration was successfully created after {stopwatch.ElapsedMilliseconds}ms.");
                    return;
                }
                await Task.Delay(200);
            }

            IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "wit-configuration-timeout",
                "Timed out while waiting for WitConfiguration to be created.");
        }

        /// <summary>
        /// Checks whether a default Wit.ai configuration with a valid client access token exists in the project.
        /// </summary>
        /// <returns>True if a valid client access token is found; otherwise, false.</returns>
        public static bool HasClientAccessToken() {
            return WitDataCreation.FindDefaultWitConfig()?.GetClientAccessToken() != null;
        }
    }
}

#endif // USING_META_XR_VOICE_SDK

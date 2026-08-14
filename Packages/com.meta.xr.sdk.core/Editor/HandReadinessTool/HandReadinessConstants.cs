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

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Constants for the Hand Readiness Tool.
    /// Centralizes device name and UI strings for easy maintenance.
    /// </summary>
    internal static class HandReadinessConstants
    {
        /// <summary>
        /// The device name used in UI and messages.
        /// </summary>
        public const string DeviceName = "Hand Tracking";

        /// <summary>
        /// The main tool window title.
        /// </summary>
        public const string ToolTitle = "Hands optimizer";

        /// <summary>
        /// The report title shown on the results screen.
        /// </summary>
        public const string ReportTitle = "Hands Optimization Report";

        /// <summary>
        /// Identifier used for AgentBridge caller identity.
        /// </summary>
        public const string ToolIdentifier = "HandOptimizerTool";
    }
}

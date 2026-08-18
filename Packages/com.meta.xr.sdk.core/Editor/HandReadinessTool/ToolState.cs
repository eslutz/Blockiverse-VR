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
    /// Represents the current state/screen of the Hand Readiness Tool.
    /// </summary>
    public enum ToolState
    {
        /// <summary>Welcome screen with overview.</summary>
        Welcome,

        /// <summary>Standard vs AI-powered check selection.</summary>
        CheckType,

        /// <summary>Project description input (AI path only).</summary>
        ProjectDescription,

        /// <summary>Scanning in progress (includes AI analysis when enabled).</summary>
        Scanning,

        /// <summary>Results/report screen with categorized issues.</summary>
        Results,

        /// <summary>
        /// Post-readiness confirmation screen. Reached from Results when every
        /// recommendation has been resolved and the user clicks "Confirm readiness".
        /// </summary>
        Confirmation,

        /// <summary>To-do list summary before starting updates.</summary>
        TodoList,

        /// <summary>Update in progress / complete.</summary>
        Updating
    }
}

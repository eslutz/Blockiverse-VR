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

using Meta.XR.Editor.Id;
using Meta.XR.Editor.ToolingSupport;

namespace Meta.XR.Guides.Editor
{
    /// <summary>
    /// Provides a base class for creating guided setup wizard windows in the editor.
    /// </summary>
    public abstract class GuidedSetup : IIdentified
    {
        /// <summary>
        /// Displays the guided setup wizard window.
        /// </summary>
        /// <param name="origin">The origin that triggered the window to be shown.</param>
        /// <param name="forceShow">When true, the window is shown even if the user previously dismissed it.</param>
        public void ShowWindow(Origins origin, bool forceShow = false)
        {
            CreateWindow().Show(origin, forceShow);
        }

        internal abstract GuideWindow CreateWindow();

        /// <summary>
        /// Gets the unique identifier for this guided setup instance, derived from the concrete type name.
        /// </summary>
        public string Id => GetType().ToString();
    }
}

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

using System.Collections.Generic;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using UnityEditor;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Registers the Hands readiness tool (HandReadinessToolWindow) in the Meta XR SDK
    /// status menu under the Tools section. Mirrors the pattern used by
    /// `RuntimeOptimizerToolRegistration` and `AIToolsSetupWindow`.
    /// </summary>
    [InitializeOnLoad]
    internal static class HandReadinessToolRegistration
    {
        // Name is the tool's stable identity: it feeds the feature-ramp-up gate
        // key, usage telemetry, and the "New" badge. Keep it constant — renaming it
        // changes the ramp-up key and drops the tool from the menu. Relabel the
        // user-facing menu entry via DisplayName instead.
        private const string ToolName = "Hands readiness";
        private const string DisplayLabel = "Hands optimizer";
        private const string MenuDescription = "Optimize your project for hands-only Meta experiences";

        internal static readonly TextureContent StatusIcon = TextureContent.CreateContent(
            "icon_hand_tracking.png",
            new TextureContent.Category("Resources", pathKey: "Meta.XR.HandReadinessTool.Editor"),
            $"Open {DisplayLabel}");

        internal static readonly ToolDescriptor ToolDescriptor = new()
        {
            Name = ToolName,
            DisplayName = DisplayLabel,
            MenuDescription = MenuDescription,
            Icon = StatusIcon,
            Order = 5,
            AddToStatusMenu = true,
            MenuCategory = MenuCategory.Tools,
            AddToMenu = true,
            CanBeNew = true,
            EnableRampUp = true,
            OnClickDelegate = _ => HandReadinessToolWindow.ShowWindow(),
        };

        static HandReadinessToolRegistration()
        {
        }
    }
}

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

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor
{
    internal interface IAIToolsProvider
    {
        string Id { get; }
        ToolCardDescriptor ToolCard { get; }
        SetupAction[] GetSetupActions();
        Task<bool> VerifyAsync(string serviceExecutable, CancellationToken token);
        VisualElement BuildAdvancedSettings(Action onStateChanged);
        RegistrationInfo? GetRegistrationInfo(string serviceExecutable);

        /// <summary>
        /// Optional "Activate" UI for the provider (e.g. MetaXROperator OpenXR layer activation).
        /// Rendered at the bottom of the provider's column in the Install &amp; Setup step.
        /// Return null when the provider has nothing to contribute.
        /// </summary>
        VisualElement BuildActivateSection(Action onStateChanged);

        /// <summary>
        /// Optional project-level skills UI for the provider. Rendered as its own top-level
        /// step ("Install project-level skills"). Return null when the provider has no skills.
        /// </summary>
        VisualElement BuildSkillsSection(Action onStateChanged);

        /// <summary>
        /// Returns a prerequisite install descriptor that the wizard renders as a sub-step of
        /// Step 1 (above the AI Agent Bridge install). Return null when the provider has no
        /// visible prerequisite install (the default for providers that do not own one).
        /// </summary>
        PrerequisiteInstall GetPrerequisiteInstall();
    }

    /// <summary>
    /// Describes a one-time install that the AI Tools setup wizard surfaces as a
    /// visible sub-step of Step 1. The provider supplies the labels, status check,
    /// and async install action; the wizard handles button/spinner/status rendering.
    /// </summary>
    internal class PrerequisiteInstall
    {
        public string Label;
        public string Description;
        public string InstalledStatusText;
        public string NotInstalledStatusText;
        public string InstallButtonText;
        public string ReinstallButtonText;
        public string InstallingText;
        public Func<bool> IsInstalled;
        public Func<CancellationToken, Task<(bool success, string error)>> InstallAsync;
    }

    internal struct RegistrationInfo
    {
        public string Label;
        public string Description;
        public string Command;

        /// <summary>
        /// Optional documentation URL. When set, the wizard renders a "view docs" link next to
        /// the command block — used for providers that can't be auto-registered from the CLI
        /// (e.g. OpenCode, which is configured by editing opencode.json).
        /// </summary>
        public string DocsUrl;

        /// <summary>Display text for the <see cref="DocsUrl"/> link.</summary>
        public string DocsLinkText;

        /// <summary>
        /// Optional manual-setup copy shown under the command block when <see cref="ManualOnly"/>
        /// is true. Lets each provider supply its own instructions instead of the wizard hardcoding
        /// one provider's text. When null, the wizard falls back to a generic default.
        /// </summary>
        public string ManualInstructions;

        /// <summary>
        /// When false (default), the wizard shows a "Run command" button that auto-registers the
        /// MCP server. When true, the provider can only be set up manually: the wizard shows the
        /// command/path block and the docs link, but no Run button or auto-status.
        /// </summary>
        public bool ManualOnly;
    }

    internal struct ToolCardDescriptor
    {
        public string Label;
        public string Description;
        public string LearnMoreUrl;
        public Texture2D Icon;
        public int Order;
    }

    internal struct SetupContext
    {
        public string ServiceId;
        public string ServiceExecutable;
    }

    internal struct SetupAction
    {
        public string Label;
        public int Order;
        public Func<SetupContext, CancellationToken, Task<bool>> Execute;
    }
}

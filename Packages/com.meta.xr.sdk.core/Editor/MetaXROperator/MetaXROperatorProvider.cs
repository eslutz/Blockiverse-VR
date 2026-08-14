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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR.AI.AgentBridge;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Json;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using RLDSStyles = Meta.XR.Editor.UserInterface.RLDS.Styles;
using Label = UnityEngine.UIElements.Label;

namespace Meta.XR.Editor
{
    [InitializeOnLoad]
    internal class MetaXROperatorProvider : IAIToolsProvider
    {
        internal static readonly MetaXROperatorProvider Instance = new();

        static MetaXROperatorProvider()
        {
            AIToolsSetupRegistry.Register(Instance);
        }

        internal enum TransportMode
        {
            McpProxy,
            DirectSSE,
        }

        private const string DefaultSSEUrl = "http://localhost:8720/sse";
        private const string SkillsDirectoryName = "Skills";
        private const int VerifyTimeoutMs = 30000;

        internal TransportMode SelectedTransport { get; set; } = TransportMode.McpProxy;
        internal bool ShowMcpExplainer { get; set; }
        internal bool ShowTransportComparison { get; set; }
        internal string SkillsInstallResultMessage { get; set; }
        internal bool SkillsInstallResultSuccess { get; set; }
        private string _skillsInstallServiceId;

        public string Id => "meta-xr-operator";

        public ToolCardDescriptor ToolCard => new()
        {
            Label = AIToolsSetupStrings.Tools.MetaXROperatorLabel,
            Description = AIToolsSetupStrings.Tools.MetaXROperatorDescription,
            LearnMoreUrl = AIToolsSetupStrings.Links.MetaXROperatorLearnMoreUrl,
            Icon = TextureContent.CreateContent("feature_ai_agent.png",
                TextureContent.Categories.Generic).Image as Texture2D,
            Order = 0
        };

        public SetupAction[] GetSetupActions()
        {
            return new[]
            {
                new SetupAction
                {
                    Label = "Register meta-xr-operator",
                    Order = 10,
                    Execute = RegisterAsync
                }
            };
        }

        public async Task<bool> VerifyAsync(string serviceExecutable, CancellationToken token)
        {
            var serviceId = Meta.XR.AI.AgentBridge.Settings.SelectedServiceId.Value;
            var projectRoot = AIToolsSetupModel.GetCurrentProjectRoot();

            // OpenCode has no `mcp add`/`mcp list` CLI (manual-only registration; see
            // GetRegistrationInfo). Skip CLI verification so we don't spawn a guaranteed-failing
            // process — or launch OpenCode's interactive TUI on unknown args and hang until
            // VerifyTimeoutMs. Manual-only setups can't be auto-verified.
            if (serviceId == OpenCodeService.ServiceId)
                return false;

            // Gemini's `mcp list` is unreliable to parse
            // so read its settings.json directly when present; if no settings file exists, fall
            // through to `mcp list`. Every other CLI uses `mcp list`, which runs in Unity's
            // environment (the same one the panel registered from) and so authoritatively reflects
            // the config actually in use, honoring CODEX_HOME / CLAUDE_CONFIG_DIR, etc.
            if (serviceId == GeminiCliService.ServiceId)
            {
                var fromConfig = GeminiConfigHasOperator(projectRoot);
                if (fromConfig.HasValue)
                    return fromConfig.Value;
            }

            var resolved = AIToolsSetupModel.ResolveExecutablePath(serviceExecutable);
            var result = await AIToolsSetupModel.RunProcessAsync(
                resolved, "mcp list", token, VerifyTimeoutMs, projectRoot);
            if (token.IsCancellationRequested) return false;
            var combined = (result.stdout ?? string.Empty) + "\n" + (result.stderr ?? string.Empty);
            return result.exitCode >= 0 && !result.timedOut && OperatorAppearsInMcpList(combined);
        }

        // --- Gemini config-file check (Gemini's `mcp list` is too noisy to parse reliably) ---

        private const string OperatorMcpName = "meta-xr-operator";

        // Gemini defaults to project scope; also accept a user-scope registration. Returns null when
        // no settings file exists so the caller falls back to `mcp list`.
        private static bool? GeminiConfigHasOperator(string projectRoot)
        {
            var projectContent = string.IsNullOrEmpty(projectRoot)
                ? null
                : ReadFileOrNull(Path.Combine(projectRoot, ".gemini", "settings.json"));
            var userContent = ReadFileOrNull(
                Path.Combine(AIToolsSetupModel.GetUserHome(), ".gemini", "settings.json"));

            if (projectContent == null && userContent == null)
                return null;

            return GeminiSettingsContainOperator(projectContent)
                || GeminiSettingsContainOperator(userContent);
        }

        /// <summary>True when a Gemini settings.json registers meta-xr-operator under mcpServers.</summary>
        internal static bool GeminiSettingsContainOperator(string settingsJson)
        {
            if (string.IsNullOrEmpty(settingsJson))
                return false;
            try
            {
                return JsonObject.Parse(settingsJson)["mcpServers"]?[OperatorMcpName] != null;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadFileOrNull(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        // Gemini wraps list output in ANSI color codes; strip them so the line anchor below works.
        private static readonly System.Text.RegularExpressions.Regex AnsiEscapeRegex =
            new System.Text.RegularExpressions.Regex(@"\x1b\[[0-9;]*[A-Za-z]");

        // Matches the name only as a server entry (start of line), not mid-line file-path mentions.
        private static readonly System.Text.RegularExpressions.Regex OperatorListEntryRegex =
            new System.Text.RegularExpressions.Regex(
                @"(?m)^[\s\u2713\u2717\u26a0\u2022\-\*]*meta-xr-operator(?:\s|:|$)");

        /// <summary>
        /// Returns true when the meta-xr-operator MCP server appears as a registered entry in an
        /// `mcp list` output. ANSI color codes are stripped first, then a line-anchored match (not a
        /// bare substring) is applied so file-path mentions of "meta-xr-operator" in noisy CLI
        /// output don't register as false positives.
        /// </summary>
        internal static bool OperatorAppearsInMcpList(string output)
        {
            if (string.IsNullOrEmpty(output)) return false;
            var clean = AnsiEscapeRegex.Replace(output, string.Empty);
            return OperatorListEntryRegex.IsMatch(clean);
        }

        public VisualElement BuildAdvancedSettings(Action onStateChanged)
        {
            var container = new VisualElement();
            BuildMcpExplainer(container, onStateChanged);
            BuildTransportSettings(container, onStateChanged);
            return container;
        }

        public VisualElement BuildActivateSection(Action onStateChanged)
        {
            var container = new VisualElement();
            BuildActivateSection(container, onStateChanged);
            return container;
        }

        public VisualElement BuildSkillsSection(Action onStateChanged)
        {
            var container = new VisualElement();
            BuildSkillsSection(container, onStateChanged);
            return container;
        }

        public PrerequisiteInstall GetPrerequisiteInstall()
        {
            return new PrerequisiteInstall
            {
                Label = AIToolsSetupStrings.SubSteps.InstallMcpProxy,
                Description = AIToolsSetupStrings.Descriptions.InstallMcpProxy,
                InstalledStatusText = AIToolsSetupStrings.ConnectionStatus.ProxyInstalled,
                NotInstalledStatusText = AIToolsSetupStrings.ConnectionStatus.ProxyNotInstalled,
                InstallButtonText = AIToolsSetupStrings.Buttons.InstallMcpProxy,
                ReinstallButtonText = AIToolsSetupStrings.Buttons.ReinstallMcpProxy,
                InstallingText = AIToolsSetupStrings.Processing.InstallingProxy,
                IsInstalled = IsProxyInstalled,
                InstallAsync = async token =>
                {
                    var install = await Task.Run(() => InstallProxy(), token);
                    return (install.path != null, install.error);
                }
            };
        }

        // --- Setup actions ---

        private async Task<bool> RegisterAsync(SetupContext ctx, CancellationToken token)
        {
            if (SelectedTransport == TransportMode.McpProxy)
                return await RegisterProxyAsync(ctx, token);
            return await RegisterSSEAsync(ctx, token);
        }

        private async Task<bool> RegisterProxyAsync(SetupContext ctx, CancellationToken token)
        {
            var proxyPath = GetInstalledProxyPath();
            if (!File.Exists(proxyPath))
            {
                UnityEngine.Debug.LogError(
                    "[MetaXROperator] MCP Proxy is not installed. " +
                    "Click \"Install MCP Proxy\" in Step 1 of the AI Tools setup wizard.");
                return false;
            }

            var result = await AIToolsSetupModel.RunProcessAsync(
                ctx.ServiceExecutable,
                BuildProxyAddArgs(ctx.ServiceId, $"\"{proxyPath}\""), token,
                workingDirectory: AIToolsSetupModel.GetCurrentProjectRoot());
            if (token.IsCancellationRequested) return false;
            return result.exitCode == 0 || result.stderr.Contains("already exists");
        }

        /// <summary>
        /// Builds the `mcp add` argument string that registers the meta-xr-operator proxy as a
        /// stdio MCP server. Most CLIs (Claude, Codex, Devmate) use the `-- &lt;command&gt;` form to
        /// separate the launched command from the CLI's own flags. Gemini's parser (yargs) treats
        /// `--` as end-of-options and leaves its required &lt;commandOrUrl&gt; positional empty, so the
        /// proxy path must be passed positionally with no `--`.
        /// The caller supplies <paramref name="proxyPathToken"/> already quoted/expanded as needed
        /// (absolute + quoted for execution; "~"/"$env:" + unquoted for the displayed command so
        /// the shell still expands it).
        /// </summary>
        internal static string BuildProxyAddArgs(string serviceId, string proxyPathToken)
        {
            if (serviceId == GeminiCliService.ServiceId)
                return $"mcp add meta-xr-operator {proxyPathToken}";
            return $"mcp add meta-xr-operator -- {proxyPathToken}";
        }

        private async Task<bool> RegisterSSEAsync(SetupContext ctx, CancellationToken token)
        {
            var result = await AIToolsSetupModel.RunProcessAsync(
                ctx.ServiceExecutable,
                $"mcp add meta-xr-operator -t sse {DefaultSSEUrl}", token,
                workingDirectory: AIToolsSetupModel.GetCurrentProjectRoot());
            if (token.IsCancellationRequested) return false;
            return result.exitCode == 0 || result.stderr.Contains("already exists");
        }

        private Task<bool> ActivateAsync(SetupContext ctx, CancellationToken token)
        {
            EnsureMetaXROperatorActivated();
            return Task.FromResult(true);
        }

        private Task<bool> InstallSkillsAsync(SetupContext ctx, CancellationToken token)
        {
            var skills = InstallSkills(ctx.ServiceId);
            _skillsInstallServiceId = ctx.ServiceId;
            if (skills.error == null && skills.count > 0)
            {
                SkillsInstallResultMessage = string.Format(
                    AIToolsSetupStrings.Skills.InstalledFormat,
                    skills.count, skills.destination);
                SkillsInstallResultSuccess = true;
            }
            else if (skills.error != null)
            {
                SkillsInstallResultMessage = skills.error;
                SkillsInstallResultSuccess = false;
            }
            return Task.FromResult(true);
        }

        // --- Registration command ---

        public RegistrationInfo? GetRegistrationInfo(string serviceExecutable)
        {
            var serviceId = Meta.XR.AI.AgentBridge.Settings.SelectedServiceId.Value;

            // OpenCode has no `mcp add` CLI — MCP servers are configured by editing opencode.json.
            // Surface the proxy path to copy plus a link to OpenCode's docs instead of a runnable
            // command + auto-register button.
            if (serviceId == OpenCodeService.ServiceId)
            {
                return new RegistrationInfo
                {
                    Label = AIToolsSetupStrings.Registration.RuntimeToolsLabel,
                    Description = AIToolsSetupStrings.Registration.RuntimeToolsDescription,
                    Command = GetProxyPathForCommand(),
                    DocsUrl = AIToolsSetupStrings.Links.OpenCodeMcpDocsUrl,
                    DocsLinkText = AIToolsSetupStrings.OpenCode.DocsLinkText,
                    ManualInstructions = AIToolsSetupStrings.OpenCode.ManualInstructions,
                    ManualOnly = true
                };
            }

            string command;
            if (SelectedTransport == TransportMode.DirectSSE)
                command = $"{serviceExecutable} mcp add meta-xr-operator -t sse {DefaultSSEUrl}";
            else
                command = $"{serviceExecutable} {BuildProxyAddArgs(serviceId, GetProxyPathForCommand())}";

            return new RegistrationInfo
            {
                Label = AIToolsSetupStrings.Registration.RuntimeToolsLabel,
                Description = AIToolsSetupStrings.Registration.RuntimeToolsDescription,
                Command = command
            };
        }

        // --- Proxy ---

        internal static string GetInstalledProxyPath()
        {
            var userHome = AIToolsSetupModel.GetUserHome();
            var proxyName = AIToolsSetupModel.IsWindows()
                ? "meta-xr-operator-mcp-proxy.exe"
                : "meta-xr-operator-mcp-proxy";
            return Path.Combine(userHome, "meta-xr-operator", proxyName);
        }

        internal static string GetProxyPathForCommand()
        {
            if (AIToolsSetupModel.IsWindows())
                return @"$env:USERPROFILE\meta-xr-operator\meta-xr-operator-mcp-proxy.exe";
            return "~/meta-xr-operator/meta-xr-operator-mcp-proxy";
        }

        internal static string FindProxyBinary([CallerFilePath] string sourceFilePath = "")
        {
            string platformDir;
            string proxyName;

            if (AIToolsSetupModel.IsWindows())
            {
                platformDir = "Windows";
                proxyName = "meta-xr-operator-mcp-proxy.exe";
            }
            else if (AIToolsSetupModel.IsMac())
            {
                platformDir = "Mac";
                proxyName = "meta-xr-operator-mcp-proxy";
            }
            else
            {
                platformDir = "Linux";
                proxyName = "meta-xr-operator-mcp-proxy";
            }

            if (string.IsNullOrEmpty(sourceFilePath))
                return null;

            var scriptDir = Path.GetDirectoryName(sourceFilePath);
            if (!string.IsNullOrEmpty(scriptDir))
            {
                var path = Path.Combine(scriptDir, "Tools~", platformDir, proxyName);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        internal (string path, string error) InstallProxy()
        {
            try
            {
                var sourcePath = FindProxyBinary();
                if (sourcePath == null)
                    return (null, AIToolsSetupStrings.Errors.ProxyBinaryNotFound);

                var destPath = GetInstalledProxyPath();
                var destDir = Path.GetDirectoryName(destPath);
                Directory.CreateDirectory(destDir);

                if (File.Exists(destPath))
                {
                    try { File.Delete(destPath); }
                    catch (IOException)
                    {
                        return (null, string.Format(
                            AIToolsSetupStrings.Errors.ProxyOverwriteFormat, destPath));
                    }
                }

                using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read))
                using (var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                {
                    source.CopyTo(dest);
                }

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start("chmod", $"+x \"{destPath}\"")?.WaitForExit();

                return (destPath, null);
            }
            catch (Exception e)
            {
                return (null, string.Format(
                    AIToolsSetupStrings.Errors.ProxyInstallExceptionFormat, e.Message));
            }
        }

        internal bool IsProxyInstalled()
        {
            var path = GetInstalledProxyPath();
            return path != null && File.Exists(path);
        }

        // --- Activation ---

        internal static void EnsureMetaXROperatorActivated()
        {
            if (MetaXROperatorActivator.AreBinariesPresent() && !MetaXROperatorActivator.IsActivated)
                MetaXROperatorActivator.Activate();
        }

        // --- Skills ---

        internal static string FindSkillsDirectory([CallerFilePath] string sourceFilePath = "")
        {
            // 1. Proper UPM resolution: find the package that ships this assembly (the core SDK)
            //    and look up its Editor/MetaXROperator/Skills folder. Robust across install layouts
            //    (embedded, local, or Library/PackageCache) where the physical path varies.
            var packageSkills = ResolvePackageSkillsDirectory();
            if (packageSkills != null)
                return packageSkills;

            // 2. Embedded/source layout: Skills sits next to this source file.
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                var scriptDir = Path.GetDirectoryName(sourceFilePath);
                if (!string.IsNullOrEmpty(scriptDir))
                {
                    var path = Path.Combine(scriptDir, SkillsDirectoryName);
                    if (Directory.Exists(path))
                        return path;
                }
            }

            // 3. Known relative fallbacks.
            string[] basePaths =
            {
                "Packages/com.meta.xr.sdk.core/Editor/MetaXROperator",
                "Assets/Oculus/VR/Editor/MetaXROperator"
            };
            foreach (var basePath in basePaths)
            {
                var path = Path.Combine(basePath, SkillsDirectoryName);
                if (Directory.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Resolves the bundled Skills directory via the Unity Package Manager so we use the
        /// package's actual on-disk location rather than assuming a fixed path. Returns null
        /// when this code isn't part of a UPM package (e.g. running from Assets/).
        /// </summary>
        private static string ResolvePackageSkillsDirectory()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(MetaXROperatorProvider).Assembly);
                if (package != null && !string.IsNullOrEmpty(package.resolvedPath))
                {
                    var path = Path.Combine(
                        package.resolvedPath, "Editor", "MetaXROperator", SkillsDirectoryName);
                    if (Directory.Exists(path))
                        return path;
                }
            }
            catch
            {
                // PackageManager lookup unavailable — callers fall back to other strategies.
            }

            return null;
        }

        internal static string GetSkillsDestinationForService(string serviceId, string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot))
                return null;

            var service = AIServiceRegistry.GetService(serviceId);
            var subPath = service?.SkillsSubPath ?? Path.Combine(".ai", "skills");
            var combined = Path.Combine(projectRoot, subPath);

            // SkillsSubPath may use forward slashes (e.g. ".claude/skills"); normalize so the
            // whole path uses one consistent separator for the platform.
            return combined
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
        }

        internal static int CountBundledSkills()
        {
            var sourceDir = FindSkillsDirectory();
            if (sourceDir == null) return 0;
            var count = 0;
            foreach (var d in Directory.GetDirectories(sourceDir))
            {
                if (HasSkillEntryFile(d)) count++;
            }
            return count;
        }

        internal static int CountInstalledSkills(string serviceId, string projectRoot)
        {
            var sourceDir = FindSkillsDirectory();
            var destDir = GetSkillsDestinationForService(serviceId, projectRoot);
            if (sourceDir == null || destDir == null || !Directory.Exists(destDir))
                return 0;

            var count = 0;
            foreach (var skillDir in Directory.GetDirectories(sourceDir))
            {
                if (!HasSkillEntryFile(skillDir)) continue;
                var skillName = Path.GetFileName(skillDir);
                if (File.Exists(Path.Combine(destDir, skillName, "SKILL.md")))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Names of the skills bundled in the SDK package (folders under Skills/ that contain a
        /// SKILL.md), sorted for stable display.
        /// </summary>
        internal static List<string> GetBundledSkillNames()
        {
            var names = new List<string>();
            var sourceDir = FindSkillsDirectory();
            if (sourceDir == null)
                return names;

            foreach (var skillDir in Directory.GetDirectories(sourceDir))
            {
                if (HasSkillEntryFile(skillDir))
                    names.Add(Path.GetFileName(skillDir));
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        internal static bool IsSkillInstalled(string skillName, string serviceId, string projectRoot)
        {
            var destDir = GetSkillsDestinationForService(serviceId, projectRoot);
            if (string.IsNullOrEmpty(destDir))
                return false;
            return File.Exists(Path.Combine(destDir, skillName, "SKILL.md"));
        }

        internal (int count, string destination, string error) InstallSkills(
            string serviceId, string projectRoot = null)
        {
            try
            {
                projectRoot ??= AIToolsSetupModel.GetCurrentProjectRoot();
                if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
                    return (0, null, AIToolsSetupStrings.Errors.ProjectRootNotFound);

                var sourceDir = FindSkillsDirectory();
                if (sourceDir == null)
                    return (0, null, AIToolsSetupStrings.Errors.SkillsDirectoryNotFound);

                var destDir = GetSkillsDestinationForService(serviceId, projectRoot);
                Directory.CreateDirectory(destDir);

                var count = 0;
                foreach (var skillDir in Directory.GetDirectories(sourceDir))
                {
                    if (!HasSkillEntryFile(skillDir)) continue;
                    CopySkillDirectory(skillDir,
                        Path.Combine(destDir, Path.GetFileName(skillDir)));
                    count++;
                }

                return count > 0
                    ? (count, destDir, null)
                    : (0, destDir, AIToolsSetupStrings.Errors.NoSkillsFound);
            }
            catch (Exception e)
            {
                return (0, null,
                    string.Format(AIToolsSetupStrings.Errors.SkillsInstallExceptionFormat, e.Message));
            }
        }

        internal async Task InstallSkillsManualAsync(string serviceId, Action onStateChanged)
        {
            var projectRoot = AIToolsSetupModel.GetCurrentProjectRoot();
            var result = await Task.Run(() => InstallSkills(serviceId, projectRoot));

            _skillsInstallServiceId = serviceId;
            if (result.error == null)
            {
                SkillsInstallResultMessage = string.Format(
                    AIToolsSetupStrings.Skills.InstalledFormat,
                    result.count, result.destination);
                SkillsInstallResultSuccess = true;
            }
            else
            {
                SkillsInstallResultMessage = result.error;
                SkillsInstallResultSuccess = false;
            }

            onStateChanged?.Invoke();
        }

        private static bool HasSkillEntryFile(string skillDir)
        {
            return File.Exists(Path.Combine(skillDir, "SKILL.md"))
                || File.Exists(Path.Combine(skillDir, "skill.md"));
        }

        private static void CopySkillDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(file);
                if (string.Equals(fileName, "skill.md", StringComparison.OrdinalIgnoreCase))
                    fileName = "SKILL.md";

                File.Copy(file, Path.Combine(destDir, fileName), overwrite: true);
            }

            foreach (var childDir in Directory.GetDirectories(sourceDir))
            {
                CopySkillDirectory(childDir,
                    Path.Combine(destDir, Path.GetFileName(childDir)));
            }
        }

        // --- Advanced settings UI ---

        private void BuildMcpExplainer(VisualElement container, Action onStateChanged)
        {
            var toggleBtn = new UnityEngine.UIElements.Button(() =>
            {
                ShowMcpExplainer = !ShowMcpExplainer;
                onStateChanged?.Invoke();
            })
            {
                text = ShowMcpExplainer
                    ? AIToolsSetupStrings.Explainer.ToggleHide
                    : AIToolsSetupStrings.Explainer.ToggleShow
            };
            toggleBtn.AddToClassList(RLDSConstants.Button.TertiarySmall);
            toggleBtn.style.unityTextAlign = TextAnchor.MiddleLeft;
            toggleBtn.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            container.Add(toggleBtn);

            if (!ShowMcpExplainer)
                return;

            var panel = new VisualElement();
            panel.AddToClassList(RLDSConstants.Surface.Secondary);
            panel.style.borderTopLeftRadius = RLDSConstants.Radius.SizeSM;
            panel.style.borderTopRightRadius = RLDSConstants.Radius.SizeSM;
            panel.style.borderBottomLeftRadius = RLDSConstants.Radius.SizeSM;
            panel.style.borderBottomRightRadius = RLDSConstants.Radius.SizeSM;
            panel.style.paddingTop = RLDSConstants.Spacing.SizeSM;
            panel.style.paddingBottom = RLDSConstants.Spacing.SizeSM;
            panel.style.paddingLeft = RLDSConstants.Spacing.SizeSM;
            panel.style.paddingRight = RLDSConstants.Spacing.SizeSM;
            panel.style.marginBottom = RLDSConstants.Spacing.SizeSM;

            AddExplainerBlock(panel, AIToolsSetupStrings.Explainer.WhatIsMcpTitle,
                AIToolsSetupStrings.Explainer.WhatIsMcpBody);
            AddExplainerBlock(panel, AIToolsSetupStrings.Explainer.WhatDoesMetaXROperatorAddTitle,
                AIToolsSetupStrings.Explainer.WhatDoesMetaXROperatorAddBody);
            AddExplainerBlock(panel, AIToolsSetupStrings.Explainer.ScopeTitle,
                AIToolsSetupStrings.Explainer.ScopeBody, lastBlock: true);

            container.Add(panel);
        }

        private static void AddExplainerBlock(VisualElement panel, string title, string body,
            bool lastBlock = false)
        {
            var titleLabel = new Label(title);
            titleLabel.AddToClassList(RLDSConstants.Typography.Body1Label);
            titleLabel.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            panel.Add(titleLabel);

            var bodyLabel = new Label(body);
            bodyLabel.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            bodyLabel.style.whiteSpace = WhiteSpace.Normal;
            if (!lastBlock)
                bodyLabel.style.marginBottom = RLDSConstants.Spacing.SizeSM;
            panel.Add(bodyLabel);
        }

        private void BuildTransportSettings(VisualElement container, Action onStateChanged)
        {
            var transportLabel = new Label(AIToolsSetupStrings.Transport.Label);
            transportLabel.AddToClassList(RLDSConstants.Typography.Body1Label);
            transportLabel.style.marginBottom = RLDSConstants.Spacing.Size2XS;
            container.Add(transportLabel);

            var values = (TransportMode[])Enum.GetValues(typeof(TransportMode));
            var tabs = new SliderTab[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var label = values[i] == TransportMode.McpProxy
                    ? AIToolsSetupStrings.Transport.ProxyLabel
                    : AIToolsSetupStrings.Transport.SSELabel;
                tabs[i] = new SliderTab(values[i].ToString(), label);
                if (values[i] == SelectedTransport)
                    tabs[i].SetSelected(true);
            }

            var group = new SliderTabGroup(tabs, id =>
            {
                SelectedTransport = (TransportMode)Enum.Parse(typeof(TransportMode), id);
                onStateChanged?.Invoke();
            });

            var element = group.Build();
            element.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            container.Add(element);

            var compareBtn = new UnityEngine.UIElements.Button(() =>
            {
                ShowTransportComparison = !ShowTransportComparison;
                onStateChanged?.Invoke();
            })
            {
                text = ShowTransportComparison
                    ? AIToolsSetupStrings.Transport.CompareHide
                    : AIToolsSetupStrings.Transport.CompareShow
            };
            compareBtn.AddToClassList(RLDSConstants.Button.TertiarySmall);
            compareBtn.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            container.Add(compareBtn);

            if (ShowTransportComparison)
                container.Add(BuildTransportComparisonPanel());
        }

        private static VisualElement BuildTransportComparisonPanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList(RLDSConstants.Surface.Secondary);
            panel.style.borderTopLeftRadius = RLDSConstants.Radius.SizeSM;
            panel.style.borderTopRightRadius = RLDSConstants.Radius.SizeSM;
            panel.style.borderBottomLeftRadius = RLDSConstants.Radius.SizeSM;
            panel.style.borderBottomRightRadius = RLDSConstants.Radius.SizeSM;
            panel.style.paddingTop = RLDSConstants.Spacing.SizeSM;
            panel.style.paddingBottom = RLDSConstants.Spacing.SizeSM;
            panel.style.paddingLeft = RLDSConstants.Spacing.SizeSM;
            panel.style.paddingRight = RLDSConstants.Spacing.SizeSM;
            panel.style.marginBottom = RLDSConstants.Spacing.SizeSM;

            var panelTitle = new Label(AIToolsSetupStrings.TransportComparison.PanelTitle);
            panelTitle.AddToClassList(RLDSConstants.Typography.Body1Label);
            panelTitle.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            panel.Add(panelTitle);

            var table = new ComparisonTable(
                AIToolsSetupStrings.TransportComparison.Headers,
                AIToolsSetupStrings.TransportComparison.Rows,
                AIToolsSetupStrings.TransportComparison.ColumnWidths,
                AIToolsSetupStrings.TransportComparison.RowStyles);
            panel.Add(table.Build());

            var recommendation = new Label(AIToolsSetupStrings.TransportComparison.Recommendation);
            recommendation.AddToClassList(RLDSConstants.Typography.Meta);
            recommendation.style.whiteSpace = WhiteSpace.Normal;
            recommendation.style.marginTop = RLDSConstants.Spacing.SizeXS;
            recommendation.style.unityFontStyleAndWeight = FontStyle.Italic;
            panel.Add(recommendation);

            return panel;
        }

        private void BuildSkillsSection(VisualElement container, Action onStateChanged)
        {
            var serviceId = Meta.XR.AI.AgentBridge.Settings.SelectedServiceId.Value;

            var projectRoot = AIToolsSetupModel.GetCurrentProjectRoot();
            var skillNames = GetBundledSkillNames();
            var installedCount = 0;
            foreach (var name in skillNames)
            {
                if (IsSkillInstalled(name, serviceId, projectRoot))
                    installedCount++;
            }

            // Clear a stale success message right after the scan so it operates on the
            // same snapshot as the rest of the section.
            if (SkillsInstallResultMessage != null
                && _skillsInstallServiceId == serviceId
                && SkillsInstallResultSuccess
                && installedCount == 0)
            {
                SkillsInstallResultMessage = null;
                _skillsInstallServiceId = null;
            }

            var allInstalled = skillNames.Count > 0 && installedCount == skillNames.Count;

            var description = new Label(AIToolsSetupStrings.Descriptions.SkillsSection);
            description.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = RLDSConstants.Spacing.SizeSM;
            container.Add(description);

            var destination = GetSkillsDestinationForService(serviceId, projectRoot);
            if (!string.IsNullOrEmpty(destination))
            {
                var destLabel = new Label(
                    string.Format(AIToolsSetupStrings.Skills.DestinationFormat, destination));
                destLabel.AddToClassList(RLDSConstants.Typography.Meta);
                destLabel.style.whiteSpace = WhiteSpace.Normal;
                destLabel.style.marginBottom = RLDSConstants.Spacing.SizeSM;
                container.Add(destLabel);
            }

            if (skillNames.Count > 0)
            {
                container.Add(BuildSkillsList(skillNames, serviceId, projectRoot));
                AddSpacer(container, RLDSConstants.Spacing.SizeSM);
            }

            if (allInstalled)
            {
                // Always confirm the installed state, including across sessions when there was no
                // install action this session. Rendered as a disabled "Installed" button with a
                // plain checkmark (built from child elements so the icon and label don't overlap).
                var installedBtn = new UnityEngine.UIElements.Button();
                installedBtn.AddToClassList(RLDSConstants.Button.SecondarySmall);
                installedBtn.SetEnabled(false);
                installedBtn.style.flexDirection = FlexDirection.Row;
                installedBtn.style.alignItems = Align.Center;
                installedBtn.style.alignSelf = Align.FlexStart;

                var check = new VisualElement();
                check.style.width = RLDSConstants.IconSize.SizeMD;
                check.style.height = RLDSConstants.IconSize.SizeMD;
                check.style.flexShrink = 0;
                check.style.backgroundImage = TextureContent.CreateContent(
                    "check.png", TextureContent.Categories.Generic).Image as Texture2D;
                check.style.unityBackgroundImageTintColor = RLDSStyles.Colors.IconSecondary;
                check.style.marginRight = RLDSConstants.Spacing.Size3XS;
                installedBtn.Add(check);

                var installedLabel = new Label(AIToolsSetupStrings.Buttons.Installed);
                installedBtn.Add(installedLabel);

                container.Add(installedBtn);
            }
            else
            {
                var installCurrentBtn = new UnityEngine.UIElements.Button(() =>
                    _ = InstallSkillsManualAsync(serviceId, onStateChanged))
                {
                    text = string.Format(
                        AIToolsSetupStrings.Buttons.InstallSkillsFormat,
                        AIServiceRegistry.GetService(serviceId)?.DisplayName ?? serviceId)
                };
                installCurrentBtn.AddToClassList(RLDSConstants.Button.SecondarySmall);
                installCurrentBtn.style.alignSelf = Align.FlexStart;
                installCurrentBtn.style.marginBottom = RLDSConstants.Spacing.SizeXS;
                container.Add(installCurrentBtn);

                // Surface a failed/partial result from the last install attempt.
                if (SkillsInstallResultMessage != null
                    && _skillsInstallServiceId == serviceId
                    && !SkillsInstallResultSuccess)
                {
                    container.Add(BuildInlineErrorRow(SkillsInstallResultMessage));
                }
            }
        }

        /// <summary>
        /// Two-column list of the bundled skills, each with a status icon: a green check when the
        /// skill is installed for the selected service, a muted check otherwise.
        /// </summary>
        private static VisualElement BuildSkillsList(
            List<string> skillNames, string serviceId, string projectRoot)
        {
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;

            foreach (var name in skillNames)
            {
                var installed = IsSkillInstalled(name, serviceId, projectRoot);

                var item = new VisualElement();
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;
                item.style.width = Length.Percent(50);
                item.style.marginBottom = RLDSConstants.Spacing.Size2XS;

                var icon = new VisualElement();
                icon.AddToClassList(RLDSConstants.StatusNotice.Icon);
                icon.style.backgroundImage = (installed
                    ? UserInterface.Styles.Contents.CheckMaskIcon
                    : UserInterface.Styles.Contents.RemoveCircleMaskIcon).Image as Texture2D;
                icon.style.unityBackgroundImageTintColor = installed
                    ? RLDSStyles.Colors.TextPositive
                    : RLDSStyles.Colors.IconSecondary;
                icon.style.marginRight = RLDSConstants.Spacing.Size2XS;
                item.Add(icon);

                var label = new Label(name);
                label.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
                item.Add(label);

                grid.Add(item);
            }

            return grid;
        }

        private void BuildActivateSection(VisualElement container, Action onStateChanged)
        {
            var sectionTitle = new Label(AIToolsSetupStrings.Activate.SectionTitle);
            sectionTitle.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
            container.Add(sectionTitle);

            AddSpacer(container, RLDSConstants.Spacing.SizeSM);

            var hasBinaries = MetaXROperatorActivator.AreBinariesPresent();
            var isActivated = MetaXROperatorActivator.IsActivated;

            if (!hasBinaries)
            {
                // Long explanatory message — render as a wrapping warning label rather than a
                // single-line BadgeTag pill, which overflows the narrow column.
                var warning = new Label(AIToolsSetupStrings.Activate.BinariesNotFound);
                warning.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
                warning.style.whiteSpace = WhiteSpace.Normal;
                warning.style.color = RLDSStyles.Colors.TextWarning;
                warning.style.marginBottom = RLDSConstants.Spacing.SizeSM;
                container.Add(warning);
            }
            else
            {
                var statusRow = new VisualElement();
                statusRow.style.flexDirection = FlexDirection.Row;
                statusRow.style.alignItems = Align.Center;
                statusRow.style.marginBottom = RLDSConstants.Spacing.SizeSM;

                var badgeTag = new BadgeTag(
                    isActivated
                        ? AIToolsSetupStrings.Activate.Activated
                        : AIToolsSetupStrings.Activate.NotActivated,
                    isActivated ? BadgeTagType.Positive : BadgeTagType.Warning,
                    BadgeTagSize.Normal).Build();
                statusRow.Add(badgeTag);
                container.Add(statusRow);
            }

            var description = new Label(AIToolsSetupStrings.Descriptions.ActivateSection);
            description.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = RLDSConstants.Spacing.SizeSM;
            container.Add(description);

            if (isActivated)
            {
                var deactivateButton = new UnityEngine.UIElements.Button(() =>
                {
                    MetaXROperatorActivator.Deactivate();
                    onStateChanged?.Invoke();
                })
                {
                    text = AIToolsSetupStrings.Buttons.Deactivate
                };
                deactivateButton.AddToClassList(RLDSConstants.Button.SecondarySmall);
                deactivateButton.SetEnabled(hasBinaries);
                deactivateButton.style.alignSelf = Align.FlexStart;
                container.Add(deactivateButton);
            }
            else
            {
                var activateButton = new UnityEngine.UIElements.Button(() =>
                {
                    MetaXROperatorActivator.Activate();
                    onStateChanged?.Invoke();
                })
                {
                    text = AIToolsSetupStrings.Buttons.ActivateMetaXROperator
                };
                activateButton.AddToClassList(RLDSConstants.Button.SecondarySmall);
                activateButton.SetEnabled(hasBinaries);
                activateButton.style.alignSelf = Align.FlexStart;
                container.Add(activateButton);
            }
        }

        // --- UI helpers ---

        // Borderless inline error row (icon + wrapping label). The message is sanitized first
        // because Win32/exception messages can contain stray carriage returns or other line-break
        // characters that Unity renders as blank lines, inflating the wrapping label's height.
        private static VisualElement BuildInlineErrorRow(string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;

            var icon = new VisualElement();
            icon.AddToClassList(RLDSConstants.StatusNotice.Icon);
            icon.style.backgroundImage =
                UserInterface.Styles.Contents.ErrorMaskIcon.Image as Texture2D;
            icon.style.unityBackgroundImageTintColor = RLDSStyles.Colors.IconNegative;
            icon.style.flexShrink = 0;
            icon.style.marginRight = RLDSConstants.Spacing.Size2XS;
            row.Add(icon);

            var label = new Label(SanitizeMessage(text));
            label.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            row.Add(label);

            return row;
        }

        // Native error buffers can leave NUL padding (and other control characters) after the
        // actual text; Unity renders these as blank lines that inflate a wrapping label's height.
        // Strip control characters (except standard whitespace), then collapse whitespace runs into
        // single spaces and trim.
        private static string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;
            var stripped = System.Text.RegularExpressions.Regex.Replace(
                message, @"[\x00-\x08\x0E-\x1F\x7F]", "");
            return System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ").Trim();
        }

        private static void AddDivider(VisualElement container)
        {
            var divider = new VisualElement();
            divider.AddToClassList(RLDSConstants.Divider.Base);
            divider.style.marginTop = RLDSConstants.Spacing.SizeMD;
            divider.style.marginBottom = RLDSConstants.Spacing.SizeMD;
            container.Add(divider);
        }

        private static void AddSpacer(VisualElement container, float height)
        {
            var spacer = new VisualElement();
            spacer.style.height = height;
            container.Add(spacer);
        }
    }
}

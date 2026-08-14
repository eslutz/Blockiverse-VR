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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meta.MCPBridge.Editor;
using Meta.XR.AI.AgentBridge;
using Meta.XR.Editor.Id;
using UnityEditor;
using UnityEngine;
using AgentBridgeSettings = Meta.XR.AI.AgentBridge.Settings;

namespace Meta.XR.Editor
{
    internal class AIToolsSetupModel
    {
        internal enum StepState
        {
            Incomplete,
            Processing,
            Complete,
            Error
        }

        private const int ProcessTimeoutMs = 15000;
        private const int VerifyTimeoutMs = 30000;
        private const int StatusRefreshDelayMs = 2000;

        // --- Step state ---

        internal StepState Step1State => ComputeStep1State();
        internal StepState Step1BridgeState { get; private set; } = StepState.Incomplete;
        internal StepState Step2State { get; private set; } = StepState.Incomplete;
        internal StepState Step3State { get; private set; } = StepState.Incomplete;

        // Windows Firewall sub-step (Windows-only; tracked independently so it never blocks Step 1 on
        // other platforms or for users who haven't set it up yet).
        internal StepState FirewallState { get; private set; } = StepState.Incomplete;
        internal string FirewallError { get; private set; }

        internal string Step1Error => Step1BridgeError ?? FirstPrerequisiteError();
        internal string Step1BridgeError { get; private set; }
        internal string Step2Error { get; private set; }
        internal string Step3Error { get; private set; }

        internal string Step2SuccessDetail { get; set; }
        internal string Step3SuccessDetail { get; private set; }

        // --- Step 2 per-line state (bridge = Editor tools, runtime = Runtime tools) ---

        internal StepState Step2BridgeState { get; private set; } = StepState.Incomplete;
        internal string Step2BridgeError { get; private set; }
        internal string Step2BridgeSuccessDetail { get; private set; }

        internal StepState Step2RuntimeState { get; private set; } = StepState.Incomplete;
        internal string Step2RuntimeError { get; private set; }
        internal string Step2RuntimeSuccessDetail { get; private set; }

        // > 0 while one or more connection verifications are in flight (drives the "checking"
        // spinner). A counter rather than a bool so an outgoing (cancelled) verify's cleanup
        // can't clear the flag while a newer verify is still running.
        private int _verifyingCount;
        internal bool IsVerifying => _verifyingCount > 0;

        /// <summary>
        /// True when both products in the Install &amp; Setup step are connected (their MCP servers
        /// are registered/verified). Drives the "completed full setup" telemetry on close.
        /// </summary>
        internal bool IsFullSetupComplete =>
            Step2BridgeState == StepState.Complete && Step2RuntimeState == StepState.Complete;

        // --- Prerequisite (sub-step A of Step 1) tracking, keyed by provider id ---

        internal class PrerequisiteStatus
        {
            public StepState State;
            public string Error;
        }

        private readonly Dictionary<string, PrerequisiteStatus> _prerequisiteStates = new();
        internal IReadOnlyDictionary<string, PrerequisiteStatus> PrerequisiteStates => _prerequisiteStates;

        // --- Settings ---

        internal string SelectedServiceId { get; set; }
        internal bool ShowAdvancedSettings { get; set; }

        private CancellationTokenSource _cancellation;
        private Action _onStateChanged;

        internal AIToolsSetupModel()
        {
            SelectedServiceId = AgentBridgeSettings.SelectedServiceId.Value;
            RefreshStepStates();
        }

        internal void SetStateChangedCallback(Action callback)
        {
            _onStateChanged = callback;
        }

        internal void RefreshStepStates()
        {
            Step1BridgeState = AgentBridgeSettings.IsEnabled
                ? StepState.Complete
                : StepState.Incomplete;

            // Initialize / refresh prerequisite states from each provider
            foreach (var provider in AIToolsSetupRegistry.GetProviders())
            {
                var prereq = provider.GetPrerequisiteInstall();
                if (prereq == null)
                {
                    _prerequisiteStates.Remove(provider.Id);
                    continue;
                }

                // Don't clobber an in-flight Processing or sticky Error state.
                if (_prerequisiteStates.TryGetValue(provider.Id, out var existing)
                    && (existing.State == StepState.Processing || existing.State == StepState.Error))
                {
                    continue;
                }

                var installed = false;
                try { installed = prereq.IsInstalled?.Invoke() ?? false; }
                catch { installed = false; }

                _prerequisiteStates[provider.Id] = new PrerequisiteStatus
                {
                    State = installed ? StepState.Complete : StepState.Incomplete,
                    Error = null
                };
            }

            if (Step1State == StepState.Complete)
            {
                var port = McpBridgeSettings.Port.Value;
                var status = McpRegistration.GetClaudeCodeStatus(port);
                Step2State = status == McpRegistrationStatus.Registered
                    ? StepState.Complete
                    : StepState.Incomplete;

                if (Step2State == StepState.Complete)
                {
                    Step2SuccessDetail = string.Format(
                        AIToolsSetupStrings.ConnectionStatus.AssistantConnectedFormat,
                        GetSelectedServiceDisplayName());
                }
            }
            else
            {
                Step2State = StepState.Incomplete;
            }

            Step3State = Step2State == StepState.Complete ? StepState.Complete : StepState.Incomplete;
            if (Step3State == StepState.Complete)
            {
                Step3SuccessDetail = AIToolsSetupStrings.ConnectionStatus.ConnectionVerified;
            }

            RefreshFirewallState();
        }

        // --- Windows Firewall sub-step ---

        /// <summary>
        /// Re-derives <see cref="FirewallState"/> from the shared firewall cache. Called when the rule is
        /// changed outside the wizard (e.g. the Meta/Internal debug menu) so the open window stays in sync.
        /// </summary>
        internal void SyncFirewallState() => RefreshFirewallState();

        private void RefreshFirewallState()
        {
            // Not applicable off Windows — treat as satisfied so it never renders as pending.
            if (!IsWindows())
            {
                FirewallState = StepState.Complete;
                return;
            }

            // Don't clobber an in-flight configure or a sticky error from a previous attempt.
            if (FirewallState == StepState.Processing || FirewallState == StepState.Error)
            {
                return;
            }

            if (WindowsFirewallUtility.CachedStatus == FirewallRuleStatus.Unknown)
            {
                // Kick off a one-shot async check; the UI refreshes when it completes.
                FirewallState = StepState.Processing;
                WindowsFirewallUtility.RefreshStatus(() =>
                {
                    MapFirewallStateFromCache();
                    _onStateChanged?.Invoke();
                });
                return;
            }

            MapFirewallStateFromCache();
        }

        private void MapFirewallStateFromCache()
        {
            FirewallState = WindowsFirewallUtility.CachedStatus switch
            {
                FirewallRuleStatus.Configured => StepState.Complete,
                FirewallRuleStatus.NotSupported => StepState.Complete,
                FirewallRuleStatus.Checking => StepState.Processing,
                _ => StepState.Incomplete,
            };
        }

        internal async Task EnsureFirewallAsync()
        {
            if (FirewallState == StepState.Processing)
            {
                return;
            }

            FirewallState = StepState.Processing;
            FirewallError = null;
            _onStateChanged?.Invoke();

            try
            {
                var (success, error) = await WindowsFirewallUtility.EnsureInboundRuleAsync(
                    WindowsFirewallUtility.BridgePorts());

                if (success)
                {
                    FirewallState = StepState.Complete;
                    FirewallError = null;
                }
                else
                {
                    FirewallState = StepState.Error;
                    FirewallError = error ?? AIToolsSetupStrings.Firewall.NotConfigured;
                }
            }
            catch (Exception e)
            {
                FirewallState = StepState.Error;
                FirewallError = e.Message;
            }
            finally
            {
                _onStateChanged?.Invoke();
            }
        }

        private StepState ComputeStep1State()
        {
            var anyProcessing = Step1BridgeState == StepState.Processing;
            var anyError = Step1BridgeState == StepState.Error;
            var allComplete = Step1BridgeState == StepState.Complete;

            foreach (var kvp in _prerequisiteStates)
            {
                switch (kvp.Value.State)
                {
                    case StepState.Processing: anyProcessing = true; break;
                    case StepState.Error: anyError = true; break;
                    case StepState.Complete: break;
                    default: allComplete = false; break;
                }
                if (kvp.Value.State != StepState.Complete)
                    allComplete = false;
            }

            if (anyProcessing) return StepState.Processing;
            if (anyError) return StepState.Error;
            if (allComplete) return StepState.Complete;
            return StepState.Incomplete;
        }

        private string FirstPrerequisiteError()
        {
            foreach (var kvp in _prerequisiteStates)
            {
                if (kvp.Value.Error != null)
                    return kvp.Value.Error;
            }
            return null;
        }

        // --- Step 1: Install AI Agent Bridge ---

        internal void InstallBridgeAsync()
        {
            if (Step1BridgeState == StepState.Processing)
                return;


            Step1BridgeState = StepState.Processing;
            Step1BridgeError = null;
            _onStateChanged?.Invoke();

            try
            {
                // Use the full enable path (not a bare Enabled.SetValue) so installing from setup also
                // turns on Remote Agent auto-start and starts the Remote + MCP servers — a direct SetValue
                // bypasses OnEnabledChanged and would leave the servers stopped and auto-start off.
                AgentBridgeSettings.EnableAndInitialize();
                Step1BridgeState = StepState.Complete;
            }
            catch (OperationCanceledException)
            {
                Step1BridgeState = StepState.Incomplete;
            }
            catch (Exception e)
            {
                Step1BridgeState = StepState.Error;
                Step1BridgeError = e.Message;
            }
            finally
            {
                _onStateChanged?.Invoke();
            }
        }

        // --- Step 1 (sub-step A): Install MCP Proxy (or any provider prerequisite) ---

        internal async Task InstallPrerequisiteAsync(string providerId)
        {
            var provider = AIToolsSetupRegistry.GetProvider(providerId);
            var prereq = provider?.GetPrerequisiteInstall();
            if (prereq == null || prereq.InstallAsync == null)
                return;

            if (_prerequisiteStates.TryGetValue(providerId, out var existing)
                && existing.State == StepState.Processing)
            {
                return;
            }


            var status = new PrerequisiteStatus { State = StepState.Processing, Error = null };
            _prerequisiteStates[providerId] = status;

            var cts = new CancellationTokenSource();
            _cancellation = cts;
            var token = cts.Token;
            _onStateChanged?.Invoke();

            try
            {
                var result = await prereq.InstallAsync(token);
                if (token.IsCancellationRequested)
                {
                    status.State = StepState.Incomplete;
                }
                else if (result.success)
                {
                    status.State = StepState.Complete;
                    status.Error = null;
                }
                else
                {
                    status.State = StepState.Error;
                    status.Error = result.error ?? AIToolsSetupStrings.Errors.ProxyInstallFailed;
                }
            }
            catch (OperationCanceledException)
            {
                status.State = StepState.Incomplete;
            }
            catch (Exception e)
            {
                status.State = StepState.Error;
                status.Error = string.Format(
                    AIToolsSetupStrings.Errors.ProxyInstallExceptionFormat, e.Message);
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cancellation, cts))
                    _cancellation = null;
                _onStateChanged?.Invoke();
            }
        }

        // --- Step 2: Connect AI coding assistant ---

        internal string GetSelectedServiceDisplayName()
        {
            var services = AIServiceRegistry.GetAllServices();
            foreach (var service in services)
            {
                if (service.Id == SelectedServiceId)
                    return service.DisplayName;
            }
            return SelectedServiceId;
        }

        internal string GetSelectedServiceExecutable()
        {
            var service = AIServiceRegistry.GetService(SelectedServiceId);
            return service?.ExecutableName ?? SelectedServiceId;
        }

        internal bool CanAutoSetup()
        {
            return McpRegistration.CanAutoRegister(SelectedServiceId);
        }

        internal void OnSelectedServiceChanged(string id)
        {
            // Cancel any in-flight verification for the previous service so its (possibly slower)
            // result can't land after the switch and overwrite the new service's status.
            CancelCurrentOperation();

            SelectedServiceId = id;

            // Reset the per-product connection states so each column returns to its default
            // (Run command + "Waiting for connection") for the newly selected assistant. A
            // silent re-verify (triggered by the window) re-detects status for the new service.
            Step2BridgeState = StepState.Incomplete;
            Step2BridgeError = null;
            Step2BridgeSuccessDetail = null;
            Step2RuntimeState = StepState.Incomplete;
            Step2RuntimeError = null;
            Step2RuntimeSuccessDetail = null;
        }

        internal string GetBridgeRegistrationCommand()
        {
            var port = McpBridgeSettings.Port.Value;
            return McpRegistration.GetRegistrationCommand(SelectedServiceId, port);
        }

        internal async Task SetupBridgeAsync()
        {
            // If we can't run, notify the UI so any state that depends on the user's
            // click attempt (button enabled/disabled, focus, etc.) re-renders.
            if (Step2BridgeState == StepState.Processing
                || Step2RuntimeState == StepState.Processing
                || !CanAutoSetup())
            {
                _onStateChanged?.Invoke();
                return;
            }


            var cts = new CancellationTokenSource();
            _cancellation = cts;
            var token = cts.Token;
            Step2BridgeState = StepState.Processing;
            Step2BridgeError = null;
            _onStateChanged?.Invoke();

            try
            {
                AgentBridgeSettings.SelectedServiceId.SetValue(
                    SelectedServiceId, Origins.Menu, AgentBridgeSettings.Owner);

                UnityEngine.Debug.Assert(McpRegistration.CanAutoRegister(SelectedServiceId),
                    $"SetupBridgeAsync called with non-auto-registerable service: {SelectedServiceId}");
                var port = McpBridgeSettings.Port.Value;
                var bridgeSuccess = await McpRegistration.RegisterWithServiceAsync(
                    SelectedServiceId, port);

                if (token.IsCancellationRequested)
                {
                    Step2BridgeState = StepState.Incomplete;
                    return;
                }

                if (!bridgeSuccess)
                {
                    Step2BridgeState = StepState.Error;
                    Step2BridgeError = string.Format(
                        AIToolsSetupStrings.Errors.RegistrationFailedFormat,
                        GetSelectedServiceDisplayName(), "Registration returned unsuccessful");
                    return;
                }

                Step2BridgeState = StepState.Complete;
                Step2BridgeSuccessDetail = string.Format(
                    AIToolsSetupStrings.ConnectionStatus.AssistantConnectedFormat,
                    GetSelectedServiceDisplayName());
            }
            catch (OperationCanceledException)
            {
                Step2BridgeState = StepState.Incomplete;
            }
            catch (Exception e)
            {
                Step2BridgeState = StepState.Error;
                Step2BridgeError = string.Format(
                    AIToolsSetupStrings.Errors.SetupFailedFormat, e.Message);
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cancellation, cts))
                    _cancellation = null;
                _onStateChanged?.Invoke();
            }
        }

        internal async Task SetupProvidersAsync()
        {
            if (Step2RuntimeState == StepState.Processing
                || Step2BridgeState == StepState.Processing
                || !CanAutoSetup())
            {
                _onStateChanged?.Invoke();
                return;
            }


            var cts = new CancellationTokenSource();
            _cancellation = cts;
            var token = cts.Token;
            Step2RuntimeState = StepState.Processing;
            Step2RuntimeError = null;
            _onStateChanged?.Invoke();

            try
            {
                AgentBridgeSettings.SelectedServiceId.SetValue(
                    SelectedServiceId, Origins.Menu, AgentBridgeSettings.Owner);

                var ctx = new SetupContext
                {
                    ServiceId = SelectedServiceId,
                    ServiceExecutable = GetSelectedServiceExecutable()
                };

                foreach (var provider in AIToolsSetupRegistry.GetProvidersSorted())
                {
                    var actions = provider.GetSetupActions();
                    if (actions == null) continue;
                    foreach (var action in actions.OrderBy(a => a.Order))
                    {
                        await action.Execute(ctx, token);
                        if (token.IsCancellationRequested)
                        {
                            Step2RuntimeState = StepState.Incomplete;
                            return;
                        }
                    }
                }

                Step2RuntimeState = StepState.Complete;
                Step2RuntimeSuccessDetail = string.Format(
                    AIToolsSetupStrings.ConnectionStatus.RuntimeToolsRegisteredFormat,
                    GetSelectedServiceDisplayName());
            }
            catch (OperationCanceledException)
            {
                Step2RuntimeState = StepState.Incomplete;
            }
            catch (Exception e)
            {
                Step2RuntimeState = StepState.Error;
                Step2RuntimeError = string.Format(
                    AIToolsSetupStrings.Errors.SetupFailedFormat, e.Message);
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cancellation, cts))
                    _cancellation = null;
                _onStateChanged?.Invoke();
            }
        }

        // --- Step 3: Verify connection ---

        internal async Task VerifyConnectionAsync(bool silent = false)
        {
            if (!silent && Step3State == StepState.Processing)
                return;


            var cts = new CancellationTokenSource();
            _cancellation = cts;
            var token = cts.Token;

            _verifyingCount++;
            if (!silent)
            {
                Step3State = StepState.Processing;
                Step3Error = null;
            }
            _onStateChanged?.Invoke();


            try
            {
                var port = McpBridgeSettings.Port.Value;
                McpRegistration.RefreshClaudeCodeStatus(port);
                await Task.Delay(StatusRefreshDelayMs, token);
                if (token.IsCancellationRequested) { if (!silent) Step3State = StepState.Incomplete; return; }

                var bridgeStatus = McpRegistration.GetClaudeCodeStatus(port);
                var bridgeOk = bridgeStatus == McpRegistrationStatus.Registered;

                var exe = GetSelectedServiceExecutable();
                var allProvidersOk = true;
                foreach (var provider in AIToolsSetupRegistry.GetProviders())
                {
                    var ok = await provider.VerifyAsync(exe, token);
                    if (token.IsCancellationRequested) { if (!silent) Step3State = StepState.Incomplete; return; }
                    if (!ok) { allProvidersOk = false; break; }
                }

                // Bail before promoting if a switch/cancel landed after the last await — otherwise
                // a stale (e.g. previous-service) result could overwrite the current status.
                if (token.IsCancellationRequested)
                {
                    if (!silent) Step3State = StepState.Incomplete;
                    return;
                }

                // Drive the per-product column statuses independently so each card in the
                // Install & Setup step shows "Connection verified" as soon as its own MCP is
                // detected. Don't clobber an in-flight Run command (Processing).
                if (bridgeOk && Step2BridgeState != StepState.Processing)
                {
                    Step2BridgeState = StepState.Complete;
                    Step2BridgeSuccessDetail = string.Format(
                        AIToolsSetupStrings.ConnectionStatus.AssistantConnectedFormat,
                        GetSelectedServiceDisplayName());
                }
                if (allProvidersOk && Step2RuntimeState != StepState.Processing)
                {
                    Step2RuntimeState = StepState.Complete;
                    Step2RuntimeSuccessDetail = string.Format(
                        AIToolsSetupStrings.ConnectionStatus.RuntimeToolsRegisteredFormat,
                        GetSelectedServiceDisplayName());
                }

                if (bridgeOk && allProvidersOk)
                {
                    Step3State = StepState.Complete;
                    Step3SuccessDetail = AIToolsSetupStrings.ConnectionStatus.ConnectionVerified;

                    if (Step2State != StepState.Complete)
                    {
                        Step2State = StepState.Complete;
                        Step2SuccessDetail = string.Format(
                            AIToolsSetupStrings.ConnectionStatus.AssistantConnectedFormat,
                            GetSelectedServiceDisplayName());
                    }
                }
                else if (!silent)
                {
                    Step3State = StepState.Error;
                    if (!bridgeOk)
                    {
                        Step3Error = string.Format(
                            AIToolsSetupStrings.ConnectionStatus.ConnectionNotVerifiedFormat,
                            "meta-xr-unity-runtime not registered");
                    }
                    else
                    {
                        Step3Error = string.Format(
                            AIToolsSetupStrings.Errors.NotRegisteredFormat,
                            GetSelectedServiceDisplayName());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!silent) Step3State = StepState.Incomplete;
            }
            catch (Exception e)
            {
                if (!silent)
                {
                    Step3State = StepState.Error;
                    Step3Error = string.Format(AIToolsSetupStrings.Errors.VerifyFailedFormat, e.Message);
                }
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cancellation, cts))
                    _cancellation = null;
                _verifyingCount--;
                _onStateChanged?.Invoke();
            }
        }

        // --- Cancel ---

        internal void CancelCurrentOperation()
        {
            _cancellation?.Cancel();
        }

        // --- Platform utilities ---

        internal static bool IsWindows()
        {
            return Application.platform == RuntimePlatform.WindowsEditor;
        }

        internal static bool IsMac()
        {
            return Application.platform == RuntimePlatform.OSXEditor;
        }

        internal static string GetUserHome()
        {
            return IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }

        internal static string GetCurrentProjectRoot()
        {
            var assetsPath = Application.dataPath;
            return string.IsNullOrEmpty(assetsPath)
                ? null
                : Directory.GetParent(assetsPath)?.FullName;
        }

        internal static string ResolveExecutablePath(string fileName)
        {
            if (IsWindows() || fileName.Contains("/") || fileName.Contains(Path.DirectorySeparatorChar))
                return fileName;

            var shellPath = GetUserShellPath();
            if (shellPath == null)
                return fileName;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-l -c \"which {fileName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return fileName;
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                    return output;
            }
            catch
            {
                // Fall through
            }

            return fileName;
        }

        private static string _cachedUserShellPath;
        private static bool _shellPathResolved;

        internal static string GetUserShellPath()
        {
            if (_shellPathResolved)
                return _cachedUserShellPath;

            _shellPathResolved = true;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = "-l -c \"echo $PATH\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    _cachedUserShellPath = output;
                    return _cachedUserShellPath;
                }
            }
            catch
            {
                // Fall through
            }

            return null;
        }

        // --- Process execution ---

        internal static Task<(int exitCode, string stdout, string stderr, bool timedOut)> RunProcessAsync(
            string fileName, string arguments, CancellationToken token = default, int timeoutMs = ProcessTimeoutMs,
            string workingDirectory = null)
        {
            return Task.Run(() =>
            {
                var resolvedFileName = ResolveExecutablePath(fileName);
                var startInfo = new ProcessStartInfo
                {
                    FileName = resolvedFileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Run from the Unity project root when provided so project-scoped MCP
                // registrations (Gemini's default scope; Claude's cwd-keyed "local" scope) are
                // written to and read from the same directory.
                if (!string.IsNullOrEmpty(workingDirectory))
                    startInfo.WorkingDirectory = workingDirectory;

                if (!IsWindows())
                {
                    var shellPath = GetUserShellPath();
                    if (shellPath != null)
                        startInfo.Environment["PATH"] = shellPath;
                }

                using var process = Process.Start(startInfo);
                if (process == null)
                    return (-1, "", "Failed to start process", false);

                process.StandardInput.Close();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(timeoutMs);

                using var reg = timeoutCts.Token.Register(() =>
                {
                    try { process.Kill(); } catch { /* already exited */ }
                });

                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = stderrTask.GetAwaiter().GetResult();
                process.WaitForExit(5000);

                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                if (timeoutCts.IsCancellationRequested)
                {
                    return (-1, stdout, $"Process timed out after {timeoutMs / 1000} seconds", true);
                }

                return (process.ExitCode, stdout, stderr, false);
            }, token);
        }
    }
}

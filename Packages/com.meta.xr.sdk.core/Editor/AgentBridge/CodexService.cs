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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using Meta.XR.Json;
using UnityEngine;

namespace Meta.XR.AI.AgentBridge
{
    /// <summary>
    /// Service for OpenAI Codex CLI integration in Unity Editor.
    /// Uses a one-shot process-per-prompt model with streaming JSON output.
    /// </summary>
    [RegisterAIService(ServiceId, "OpenAI Codex", Priority = 30, ExecutableName = "codex", SkillsSubPath = ".codex/skills")]
    public class CodexService : AIServiceBase, IServiceSettingsUI, IServiceValidation
    {
        public const string ServiceId = "codex";

        private const int ValidationTimeoutSeconds = 10;

        public static class CodexSettings
        {
            private static readonly IIdentified Owner = new CodexDescriptor();

            internal static readonly UserString ExecutablePath = new UserString
            {
                Uid = nameof(ExecutablePath),
                Owner = Owner,
                Default = "",
                Label = "Executable Path",
                Tooltip = "Optional path to Codex CLI executable. Leave empty to use PATH environment variable.",
                SendTelemetry = false
            };

            private class CodexDescriptor : IIdentified
            {
                public string Id => "AgentBridge.Codex";
            }
        }

        private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
        private Process? _currentProcess;
        private readonly object _processLock = new();
        private bool _cancellationRequested;
        private bool _disposed;
        private ValidationResult _currentValidationResult = ValidationResult.Unknown();

        public override string ServiceName => "OpenAI Codex";
        public override bool HasActiveSession => !string.IsNullOrEmpty(ConversationManager.GetSessionId());
        public ValidationResult CurrentValidationResult => _currentValidationResult;

        public override async Task ProcessUserInputAsync(
            string userInput,
            CallerIdentity? caller,
            List<ImageAttachment>? images = null,
            CancellationToken cancellationToken = default,
            string? systemPrompt = null)
        {
            await _executionSemaphore.WaitAsync(cancellationToken);
            try
            {
                Log.Info($"Processing user input through Codex: {userInput}");

                ConversationManager.IsActive = true;
                ConversationManager.ClearError();
                ConversationManager.AddMessage("user", userInput, caller: caller);

                await ExecuteCodexAsync(userInput, systemPrompt, caller);

                ConversationManager.ClearError();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
                ConversationManager.SetError($"{ex.Message} ({ex.GetType().Name})");
            }
            finally
            {
                ConversationManager.IsActive = false;
                _executionSemaphore.Release();
            }
        }

        private async Task ExecuteCodexAsync(string userInput, string? systemPrompt, CallerIdentity? caller)
        {
            if (systemPrompt != null)
                Log.Warning("Codex CLI does not support system prompts — the systemPrompt parameter will be ignored.");

            var executable = ResolveExecutable(CodexSettings.ExecutablePath.Value, "codex");
            Log.Info($"Executing Codex (caller: {caller?.Id ?? "default"})");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            ApplyLoginShellPath(processStartInfo);

            processStartInfo.ArgumentList.Add("-q");
            processStartInfo.ArgumentList.Add(userInput);

            _cancellationRequested = false;

            using var process = new Process { StartInfo = processStartInfo };
            lock (_processLock) { _currentProcess = process; }

            var outputBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginErrorReadLine();

            var readTask = Task.Run(() =>
            {
                try
                {
                    string? line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        if (_cancellationRequested) break;
                        outputBuilder.AppendLine(line);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Error reading Codex output: {ex.Message}");
                }
            });

            var timeout = TimeSpan.FromMinutes(10);
            var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));

            if (!completed)
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("Codex CLI timed out after 10 minutes");
            }

            await Task.WhenAny(readTask, Task.Delay(1000));

            lock (_processLock) { _currentProcess = null; }

            var output = outputBuilder.ToString().Trim();
            if (!string.IsNullOrEmpty(output))
            {
                MainThreadDispatcher.ExecuteOnMainThread(() =>
                {
                    ConversationManager.AddMessage("assistant", output, caller: caller);
                });
            }
            else if (process.ExitCode != 0)
            {
                var stderr = stderrBuilder.ToString().Trim();
                throw new Exception($"Codex exited with code {process.ExitCode}: {stderr}");
            }
        }

        public override void ClearSession()
        {
            Log.Info("Clearing Codex session");
            ConversationManager.Clear();
        }

        public override async Task CancelCurrentOperationAsync()
        {
            Process? processToKill;

            lock (_processLock)
            {
                processToKill = _currentProcess;
                if (processToKill != null && !processToKill.HasExited)
                    _cancellationRequested = true;
                else
                    processToKill = null;
            }

            if (processToKill != null)
            {
                try
                {
                    Log.Info("Cancelling Codex operation");
                    processToKill.Kill();
                    await Task.Run(() => processToKill.WaitForExit(5000));
                }
                catch (Exception ex)
                {
                    Log.Warning($"Error killing Codex process: {ex.Message}");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _executionSemaphore?.Dispose();

                lock (_processLock)
                {
                    if (_currentProcess != null && !_currentProcess.HasExited)
                    {
                        try
                        {
                            _currentProcess.Kill();
                            _currentProcess.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"Error disposing process: {ex.Message}");
                        }
                    }
                    _currentProcess = null;
                }
            }

            _disposed = true;
            base.Dispose(disposing);
        }

        #region IServiceSettingsUI Implementation

        public void DrawSettingsUI()
        {
            UnityEditor.EditorGUILayout.LabelField("Codex Settings", UnityEditor.EditorStyles.boldLabel);
            UnityEditor.EditorGUILayout.LabelField("Executable Path", CodexSettings.ExecutablePath.Value);
        }

        public void DrawSettingsUI(Origins origins, IIdentified originData)
        {
            UnityEditor.EditorGUILayout.LabelField("Codex Settings", UnityEditor.EditorStyles.boldLabel);
            CodexSettings.ExecutablePath.DrawForGUI(origins, originData);
        }

        public void ResetSettingsToDefaults()
        {
            CodexSettings.ExecutablePath.Reset();
            ClearResolvedExecutablePath();
        }

        #endregion

        #region IServiceValidation Implementation

        public async Task<ValidationResult> ValidateConfigurationAsync()
        {
            _currentValidationResult = ValidationResult.Validating();

            try
            {
                var configuredPath = CodexSettings.ExecutablePath.Value;

                if (!string.IsNullOrEmpty(configuredPath) && !File.Exists(configuredPath))
                {
                    _currentValidationResult = ValidationResult.Invalid($"Executable not found: {configuredPath}");
                    return _currentValidationResult;
                }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = ResolveExecutable(configuredPath, "codex"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                processStartInfo.ArgumentList.Add("--version");

                ApplyLoginShellPath(processStartInfo);

                using var process = new Process { StartInfo = processStartInfo };
                var outputBuilder = new StringBuilder();

                process.Start();

                var readTask = Task.Run(() =>
                {
                    try
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        if (!string.IsNullOrEmpty(output))
                            outputBuilder.Append(output);
                    }
                    catch { }
                });

                var waitTask = Task.Run(() =>
                {
                    try { return process.WaitForExit(ValidationTimeoutSeconds * 1000); }
                    catch { return false; }
                });

                var completed = await waitTask;

                if (!completed)
                {
                    try { process.Kill(); } catch { }
                    _currentValidationResult = ValidationResult.Error("Codex CLI timed out");
                    return _currentValidationResult;
                }

                await Task.WhenAny(readTask, Task.Delay(500));

                if (process.ExitCode == 0)
                {
                    var version = outputBuilder.ToString().Trim();
                    if (string.IsNullOrEmpty(version)) version = "unknown version";
                    _currentValidationResult = ValidationResult.Valid($"Codex CLI available ({version})");
                }
                else
                {
                    _currentValidationResult = ValidationResult.Invalid("Codex CLI not found or not configured");
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _currentValidationResult = ValidationResult.Invalid("Codex CLI not found. Install OpenAI Codex or set the executable path.");
            }
            catch (Exception ex)
            {
                _currentValidationResult = ValidationResult.Error($"Validation error: {ex.Message}");
            }

            return _currentValidationResult;
        }

        #endregion
    }
}

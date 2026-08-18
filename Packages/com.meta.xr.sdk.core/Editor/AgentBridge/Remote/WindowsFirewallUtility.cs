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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles;

namespace Meta.XR.AI.AgentBridge
{
    /// <summary>
    /// Current state of the Windows Firewall inbound rule for the AI Tools bridge ports.
    /// </summary>
    internal enum FirewallRuleStatus
    {
        /// <summary>Not yet checked this session.</summary>
        Unknown,

        /// <summary>An asynchronous check (or configure) is in progress.</summary>
        Checking,

        /// <summary>A rule is present and covers the current bridge ports.</summary>
        Configured,

        /// <summary>No matching rule, or the rule does not cover the current ports.</summary>
        NotConfigured,

        /// <summary>Not applicable — only Windows requires this rule.</summary>
        NotSupported,
    }

    /// <summary>
    /// Creates and inspects a dedicated Windows Firewall inbound rule that allows devices on the
    /// local network (e.g., Quest headsets) to reach the AI Tools HTTP servers
    /// (<see cref="RemoteAgentServer"/> and the MCP Bridge) running in this Editor.
    ///
    /// Without this rule, Windows blocks inbound LAN connections on the Public profile, so the
    /// on-device AI Assistant never reaches the Editor until the user manually runs a
    /// <c>netsh advfirewall</c> command — and Unity's auto-created per-version rule does not persist
    /// that change across launches (the root cause of T275605030).
    ///
    /// This rule is:
    /// <list type="bullet">
    ///   <item>Port-scoped (TCP, the bridge ports only) — minimal surface, independent of the Unity version.</item>
    ///   <item>Idempotent — created once via a single elevated (UAC) operation, then persists; subsequent
    ///   sessions detect it and never re-prompt.</item>
    ///   <item>All-profiles (Domain/Private/Public) — works regardless of how the network is classified.</item>
    /// </list>
    ///
    /// Editor-only, Windows-only. All elevation happens through an explicit user action (a button),
    /// never silently during editor load.
    ///
    /// Threading: <see cref="EditorPrefs"/> and the <c>Port.Value</c> settings are main-thread-only, so
    /// they are read before spawning background work and written in the main-thread continuation. The
    /// background threads only touch processes and the in-memory <see cref="_configuredPorts"/> mirror.
    /// </summary>
    internal static class WindowsFirewallUtility
    {
        /// <summary>
        /// Stable rule name. Deliberately version-independent so the rule survives Unity upgrades
        /// (unlike Unity's auto-created "Unity {version} Editor" rule).
        /// </summary>
        internal const string RuleName = "Meta XR AI Tools Bridge";

        private const string LogPrefix = "[AgentBridge] Firewall:";
        private const string ConfiguredPortsPrefKey = "Meta.XR.AI.AgentBridge.Firewall.ConfiguredPorts";

        private const int ShowTimeoutMs = 10000;

        // Generous timeout: the elevated process is gated behind a UAC prompt the user must respond to.
        private const int ElevatedTimeoutMs = 120000;

        // Win32 error code ShellExecute returns when the user declines/cancels the UAC prompt.
        private const int ErrorCancelled = 1223;

        private static volatile FirewallRuleStatus _cachedStatus = FirewallRuleStatus.Unknown;

        /// <summary>Raised on the main thread whenever <see cref="CachedStatus"/> may have changed, so
        /// open UI (e.g. the AI Tools Setup window) can refresh even when the change came from elsewhere
        /// (the Meta/Internal debug menu, another panel, or a server-start check).</summary>
        internal static event Action? StatusChanged;

        // Logs the "rule missing" hint at most once per domain reload (both servers call
        // WarnIfNotConfigured on start, so without this it would log twice).
        private static volatile bool _warnedMissingThisSession;

        // In-memory mirror of the persisted "configured ports" hint (e.g. "48735,48736"). Source of truth
        // for background-thread comparisons; synced with EditorPrefs ONLY on the main thread.
        private static volatile string _configuredPorts = string.Empty;
        private static volatile bool _configuredPortsLoaded;

        /// <summary>
        /// Provider for the MCP Bridge port. Registered by the MCPBridge editor assembly at init so a
        /// single rule can cover both bridges without a circular assembly reference. Null when MCPBridge
        /// is not present in the project.
        /// </summary>
        internal static Func<int>? McpPortProvider;

        /// <summary>True only in the Windows Editor, where this firewall rule is meaningful.</summary>
        internal static bool IsSupported => Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>
        /// The last known status without triggering a (process-spawning) check. UI should read this each
        /// repaint and call <see cref="RefreshStatus(System.Action?)"/> on open / via a refresh button.
        /// </summary>
        internal static FirewallRuleStatus CachedStatus =>
            IsSupported ? _cachedStatus : FirewallRuleStatus.NotSupported;

        /// <summary>
        /// All distinct inbound TCP ports the AI Tools bridges currently listen on
        /// (Remote Agent Server + MCP Bridge, when present). Reads settings — call on the main thread.
        /// </summary>
        internal static int[] BridgePorts()
        {
            var ports = new List<int> { RemoteAgentSettings.Port.Value };
            var mcpPort = McpPortProvider?.Invoke();
            if (mcpPort.HasValue)
            {
                ports.Add(mcpPort.Value);
            }
            return NormalizePorts(ports);
        }

        #region Pure helpers (no process / IO — unit-testable)

        /// <summary>Sorts, de-duplicates, and drops out-of-range ports (keeps 1..65535).</summary>
        internal static int[] NormalizePorts(IEnumerable<int> ports)
        {
            if (ports == null)
            {
                return Array.Empty<int>();
            }
            return ports
                .Where(p => p > 0 && p <= 65535)
                .Distinct()
                .OrderBy(p => p)
                .ToArray();
        }

        /// <summary>Comma-separated port list for the netsh <c>localport=</c> argument (e.g. "48735,48736").</summary>
        internal static string BuildPortList(IEnumerable<int> ports) =>
            string.Join(",", NormalizePorts(ports));

        /// <summary>netsh arguments to create the inbound allow rule for the given ports, all profiles.</summary>
        internal static string BuildAddRuleArguments(string ruleName, IEnumerable<int> ports) =>
            $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow " +
            $"protocol=TCP localport={BuildPortList(ports)} profile=any enable=yes";

        /// <summary>netsh arguments to delete every rule with the given name.</summary>
        internal static string BuildDeleteRuleArguments(string ruleName) =>
            $"advfirewall firewall delete rule name=\"{ruleName}\"";

        /// <summary>netsh arguments to query the rule (read-only; does not require elevation).</summary>
        internal static string BuildShowRuleArguments(string ruleName) =>
            $"advfirewall firewall show rule name=\"{ruleName}\"";

        /// <summary>
        /// Interprets a <c>netsh ... show rule</c> result. netsh exits 0 and prints the rule when it
        /// exists, and exits 1 ("No rules match the specified criteria.") when it does not.
        /// </summary>
        internal static bool ParseRuleExists(int exitCode, string? output) => exitCode == 0;

        private static string FormatPorts(int[] ports) => string.Join(",", ports);

        #endregion

        #region EditorPrefs sync (MAIN THREAD ONLY)

        /// <summary>Main thread only. Loads the persisted hint + starts the dispatcher used for marshaling.</summary>
        private static void EnsureMainThreadServices()
        {
            MainThreadDispatcher.EnsureStarted();
            LoadConfiguredPortsIfNeeded();
        }

        /// <summary>Main thread only. Lazily loads the persisted configured-ports hint into the cache.</summary>
        private static void LoadConfiguredPortsIfNeeded()
        {
            if (_configuredPortsLoaded)
            {
                return;
            }
            _configuredPorts = EditorPrefs.GetString(ConfiguredPortsPrefKey, string.Empty);
            _configuredPortsLoaded = true;
        }

        /// <summary>Main thread only. Updates the in-memory mirror and persists it to EditorPrefs.</summary>
        private static void PersistConfiguredPorts(string value)
        {
            _configuredPorts = value;
            _configuredPortsLoaded = true;
            EditorPrefs.SetString(ConfiguredPortsPrefKey, value);
        }

        #endregion

        #region Rule inspection (read-only, no elevation)

        /// <summary>
        /// Returns true if a firewall rule named <see cref="RuleName"/> exists. Spawns a short-lived
        /// netsh process; safe to call off the main thread.
        /// </summary>
        internal static bool RuleExists()
        {
            if (!IsSupported)
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = BuildShowRuleArguments(RuleName),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return false;
                }

                var output = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(ShowTimeoutMs))
                {
                    try { process.Kill(); } catch { /* already exited */ }
                    return false;
                }

                return ParseRuleExists(process.ExitCode, output);
            }
            catch (Exception ex)
            {
                Log.Warning($"{LogPrefix} Could not query firewall rule: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Rule creation / removal (elevated)

        /// <summary>
        /// Ensures the inbound rule exists and covers <paramref name="ports"/>. Idempotent: returns success
        /// immediately when already configured (no UAC). Otherwise (re)creates the rule via a single
        /// elevated operation (one UAC prompt). Blocking — call from a background task. Does NOT touch
        /// EditorPrefs; the caller persists the configured ports on the main thread.
        /// </summary>
        internal static (bool success, string? error) EnsureInboundRuleBlocking(int[] ports)
        {
            if (!IsSupported)
            {
                return (false, "Windows Firewall configuration is only available in the Windows Editor.");
            }

            var normalized = NormalizePorts(ports);
            if (normalized.Length == 0)
            {
                return (false, "No valid bridge ports to allow.");
            }

            // Fast path: already configured for exactly these ports — no elevation needed.
            if (RuleExists() && _configuredPorts == FormatPorts(normalized))
            {
                return (true, null);
            }

            var batchPath = WriteEnsureBatch(normalized);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = batchPath,
                    UseShellExecute = true, // required for the "runas" verb
                    Verb = "runas",         // triggers the UAC elevation prompt
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return (false, "Failed to launch the firewall configuration process.");
                }

                if (!process.WaitForExit(ElevatedTimeoutMs))
                {
                    try { process.Kill(); } catch { /* already exited */ }
                    return (false, "Firewall configuration timed out.");
                }

                var exitCode = process.ExitCode;

                // Trust the live rule over the exit code: re-query to confirm the rule is actually present.
                if (RuleExists())
                {
                    Log.Info($"{LogPrefix} Inbound rule '{RuleName}' configured for ports {FormatPorts(normalized)}.");
                    return (true, null);
                }

                return (false, $"netsh did not create the firewall rule (exit code {exitCode}).");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return (false, "Administrator approval was declined. The firewall rule was not created.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                try { if (File.Exists(batchPath)) File.Delete(batchPath); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        /// <summary>
        /// Ensures the inbound rule for <paramref name="ports"/> (one UAC prompt if needed). On success,
        /// persists the configured ports and updates the cached status on the main thread. Call from the
        /// main thread.
        /// </summary>
        internal static Task<(bool success, string? error)> EnsureInboundRuleAsync(int[] ports)
        {
            EnsureMainThreadServices();
            var normalized = NormalizePorts(ports);
            var tcs = new TaskCompletionSource<(bool success, string? error)>();

            Task.Run(() =>
            {
                var result = EnsureInboundRuleBlocking(normalized);
                MarshalToMainThread(() =>
                {
                    if (result.success)
                    {
                        PersistConfiguredPorts(FormatPorts(normalized));
                        _cachedStatus = FirewallRuleStatus.Configured;
                    }
                    else
                    {
                        _cachedStatus = FirewallRuleStatus.NotConfigured;
                    }
                    tcs.TrySetResult(result);
                });
            });

            return tcs.Task;
        }

        /// <summary>
        /// Removes the rule (elevated). Blocking — call from a background task. No-op (success) when the
        /// rule is already absent. Does NOT touch EditorPrefs; the caller clears the hint on the main thread.
        /// </summary>
        internal static (bool success, string? error) RemoveRuleBlocking()
        {
            if (!IsSupported)
            {
                return (false, "Windows Firewall configuration is only available in the Windows Editor.");
            }

            if (!RuleExists())
            {
                return (true, null);
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = BuildDeleteRuleArguments(RuleName),
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return (false, "Failed to launch the firewall configuration process.");
                }

                if (!process.WaitForExit(ElevatedTimeoutMs))
                {
                    try { process.Kill(); } catch { /* already exited */ }
                    return (false, "Firewall configuration timed out.");
                }

                return (!RuleExists(), null);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return (false, "Administrator approval was declined. The firewall rule was not removed.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static string WriteEnsureBatch(int[] ports)
        {
            var path = Path.Combine(Path.GetTempPath(), $"meta_xr_ai_firewall_{Guid.NewGuid():N}.bat");

            // Delete any stale rule first (e.g. created for a previous port) so re-running never accumulates
            // duplicates; "&" / a separate line means a failed delete does not abort the add. Both commands
            // run inside one elevated session, so the user sees a single UAC prompt.
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine($"netsh {BuildDeleteRuleArguments(RuleName)} >nul 2>&1");
            sb.AppendLine($"netsh {BuildAddRuleArguments(RuleName, ports)}");
            sb.AppendLine("exit /b %errorlevel%");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        #endregion

        #region Status caching + async refresh

        /// <summary>
        /// Asynchronously refreshes <see cref="CachedStatus"/> for the current <see cref="BridgePorts"/>,
        /// then invokes <paramref name="onChanged"/> on the main thread (use it to repaint / rebuild UI).
        /// Call from the main thread.
        /// </summary>
        internal static void RefreshStatus(Action? onChanged = null) => RefreshStatus(BridgePorts(), onChanged);

        /// <summary>
        /// Asynchronously refreshes <see cref="CachedStatus"/> for <paramref name="ports"/>, then invokes
        /// <paramref name="onChanged"/> on the main thread. Call from the main thread.
        /// </summary>
        internal static void RefreshStatus(int[] ports, Action? onChanged = null)
        {
            if (!IsSupported)
            {
                _cachedStatus = FirewallRuleStatus.NotSupported;
                onChanged?.Invoke();
                return;
            }

            EnsureMainThreadServices();
            _cachedStatus = FirewallRuleStatus.Checking;
            var expected = FormatPorts(NormalizePorts(ports));

            Task.Run(() =>
            {
                var configured = RuleExists() && _configuredPorts == expected;
                _cachedStatus = configured ? FirewallRuleStatus.Configured : FirewallRuleStatus.NotConfigured;
                MarshalToMainThread(onChanged);
            });
        }

        /// <summary>
        /// Configures the rule for <paramref name="ports"/> (UAC), persisting the result and updating
        /// <see cref="CachedStatus"/> on the main thread. Call from the main thread. Non-blocking.
        /// </summary>
        internal static void Configure(int[] ports, Action? onChanged = null)
        {
            if (!IsSupported)
            {
                _cachedStatus = FirewallRuleStatus.NotSupported;
                onChanged?.Invoke();
                return;
            }

            EnsureMainThreadServices();
            _cachedStatus = FirewallRuleStatus.Checking;
            onChanged?.Invoke();

            var normalized = NormalizePorts(ports);
            Task.Run(() =>
            {
                var (success, error) = EnsureInboundRuleBlocking(normalized);
                MarshalToMainThread(() =>
                {
                    if (success)
                    {
                        PersistConfiguredPorts(FormatPorts(normalized));
                        _cachedStatus = FirewallRuleStatus.Configured;
                    }
                    else
                    {
                        _cachedStatus = FirewallRuleStatus.NotConfigured;
                        if (!string.IsNullOrEmpty(error))
                        {
                            UnityEngine.Debug.LogWarning($"{LogPrefix} {error}");
                        }
                    }
                    onChanged?.Invoke();
                });
            });
        }

        internal static void Remove(int[] ports, Action? onChanged = null)
        {
            EnsureMainThreadServices();
            _cachedStatus = FirewallRuleStatus.Checking;
            onChanged?.Invoke();

            Task.Run(() =>
            {
                var (success, error) = RemoveRuleBlocking();
                MarshalToMainThread(() =>
                {
                    if (success)
                    {
                        PersistConfiguredPorts(string.Empty);
                    }
                    else if (!string.IsNullOrEmpty(error))
                    {
                        UnityEngine.Debug.LogWarning($"{LogPrefix} {error}");
                    }
                    RefreshStatus(ports, onChanged);
                });
            });
        }

        /// <summary>
        /// On Windows, asynchronously verifies the inbound rule exists for the current bridge ports and,
        /// if not, logs a single actionable hint pointing the user at the "Configure" button. Never
        /// elevates and never prompts — safe to call from server start (main thread). No-ops once the rule
        /// is known configured this session.
        /// </summary>
        internal static void WarnIfNotConfigured()
        {
            if (!IsSupported || _cachedStatus == FirewallRuleStatus.Configured || _warnedMissingThisSession)
            {
                return;
            }

            EnsureMainThreadServices();
            var ports = BridgePorts();
            var expected = FormatPorts(ports);
            var portList = BuildPortList(ports);

            Task.Run(() =>
            {
                var configured = RuleExists() && _configuredPorts == expected;
                _cachedStatus = configured ? FirewallRuleStatus.Configured : FirewallRuleStatus.NotConfigured;
                if (configured)
                {
                    return;
                }

                _warnedMissingThisSession = true;

                // Unconditional (not the verbose-gated Log.Warning): this is an actionable, once-per-session
                // hint for the exact problem this feature solves.
                MarshalToMainThread(() => UnityEngine.Debug.LogWarning(
                    $"{LogPrefix} No inbound firewall rule found for ports {portList}. " +
                    "Devices on your network (e.g., a Quest headset) may be unable to reach the AI Tools " +
                    "servers. Open 'Meta > AI Tools' (or Preferences > Meta XR > AI Tools) and click " +
                    "'Configure' under Windows Firewall to fix this once."));
            });
        }

        /// <summary>
        /// Logs the live firewall state (rule existence vs. the stored ports hint) to the console.
        /// Internal testing helper used by the Meta/Internal debug menu — never elevates.
        /// </summary>
        internal static void LogStatusForTesting()
        {
            if (!IsSupported)
            {
                UnityEngine.Debug.Log($"{LogPrefix} Not applicable — only the Windows Editor needs this rule.");
                return;
            }

            EnsureMainThreadServices();
            var ports = BridgePorts();
            var expected = FormatPorts(ports);
            var portList = BuildPortList(ports);

            Task.Run(() =>
            {
                var exists = RuleExists();
                var hint = _configuredPorts;
                var matches = hint == expected;
                _cachedStatus = exists && matches ? FirewallRuleStatus.Configured : FirewallRuleStatus.NotConfigured;
                // Unconditional log (the debug menu must always print, regardless of VerboseLogging).
                MarshalToMainThread(() => UnityEngine.Debug.Log(
                    $"{LogPrefix} ports={portList} ruleExists={exists} storedHint='{hint}' " +
                    $"portsMatch={matches} => {_cachedStatus}"));
            });
        }

        private static void MarshalToMainThread(Action? action)
        {
            MainThreadDispatcher.ExecuteOnMainThread(() =>
            {
                action?.Invoke();
                StatusChanged?.Invoke();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            });
        }

        #endregion

        #region Settings UI (IMGUI — shared by the AgentBridge and MCPBridge preference panels)

        /// <summary>
        /// Draws the "Windows Firewall" status row and Configure/Remove buttons. No-op on non-Windows
        /// editors (the rule is not needed there). Uses the current <see cref="BridgePorts"/>.
        /// </summary>
        internal static void DrawSettingsGUI() => DrawSettingsGUI(BridgePorts());

        /// <summary>Draws the firewall settings row for an explicit set of ports.</summary>
        internal static void DrawSettingsGUI(int[] ports)
        {
            if (!IsSupported)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Windows Firewall", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Allows devices on your local network (e.g., Quest headsets) to reach the AI Tools servers. " +
                "Creates a one-time inbound rule for the bridge ports so you don't have to run a netsh " +
                "command each session.",
                EditorStyles.wordWrappedMiniLabel);

            if (_cachedStatus == FirewallRuleStatus.Unknown)
            {
                RefreshStatus(ports);
            }

            EditorGUILayout.BeginHorizontal();

            string statusLabel;
            Color statusColor;
            switch (_cachedStatus)
            {
                case FirewallRuleStatus.Configured:
                    statusLabel = $"● Configured (ports {BuildPortList(ports)})";
                    statusColor = Colors.SuccessColor;
                    break;
                case FirewallRuleStatus.Checking:
                    statusLabel = "○ Checking...";
                    statusColor = Colors.DisabledColor;
                    break;
                case FirewallRuleStatus.NotConfigured:
                    statusLabel = "▲ Not configured — inbound device connections may be blocked";
                    statusColor = new Color(1f, 0.65f, 0f); // orange warning
                    break;
                default:
                    statusLabel = "○ Unknown";
                    statusColor = Colors.DisabledColor;
                    break;
            }
            var statusStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = statusColor } };
            EditorGUILayout.LabelField("Status", statusLabel, statusStyle);

            using (new EditorGUI.DisabledScope(_cachedStatus == FirewallRuleStatus.Checking))
            {
                if (GUILayout.Button("↻", GUILayout.Width(24)))
                {
                    RefreshStatus(ports);
                }

                if (_cachedStatus == FirewallRuleStatus.Configured)
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(90)))
                    {
                        Remove(ports);
                    }
                }
                else
                {
                    if (GUILayout.Button("Configure", GUILayout.Width(90)))
                    {
                        Configure(ports);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}

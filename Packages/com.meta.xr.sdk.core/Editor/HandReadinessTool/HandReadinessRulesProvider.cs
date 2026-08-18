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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Meta.XR.Editor.Callbacks;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using TelemetryConstants = Meta.HandReadinessTool.Editor.HandReadinessTelemetryConstants;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Source of regex rules consumed by <see cref="HandReadinessCodeScanRules.Scan"/>.
    /// Fetches a static JSON asset via the Meta XR remote-content infrastructure
    /// (`framework_tools/remote_content_fetch/{contentId}`) at editor startup and
    /// swaps the active rule set if the payload is well-formed. Any failure path
    /// — network, parse, schema-version mismatch, all-rules-fail-to-compile —
    /// falls back to the baked-in <see cref="HandReadinessCodeScanRules.BakedInRules"/>
    /// so the standard scan keeps working offline / on first run.
    /// </summary>
    [InitializeOnLoad]
    internal static class HandReadinessRulesProvider
    {
        // Content id of the remote rules JSON. Bump
        // when uploading a new version; the cache file name below also pins the
        // on-disk cache per id (RemoteJsonContentDownloader keys its cache file
        // by the constructor's `fileName` arg, not the content id, so reusing
        // the same name across uploads is fine).
        private const ulong RulesContentId = 36479671521619922UL;
        private const string CacheFileName = "hrt_rules.json";

        // Schema gate. Rejects payloads with a different version to avoid trying
        // to parse a shape we don't understand. Bump alongside the JSON schema.
        private const int SupportedSchemaVersion = 1;

        // Per-Match timeout applied to remote-sourced regexes. Baked-in rules
        // are vetted and don't carry a timeout — this is purely to bound ReDoS
        // risk on untrusted patterns.
        private static readonly TimeSpan RemoteRegexTimeout = TimeSpan.FromMilliseconds(200);

        private static HandReadinessCodeScanRules.Rule[] _activeRules;

        /// <summary>
        /// Fired once after a successful remote swap. UI that has already
        /// rendered scan results may want to re-scan or invalidate caches.
        /// Not fired when we fall back to the baked-in rules.
        /// </summary>
        public static event Action OnRulesChanged;

        static HandReadinessRulesProvider()
        {
            InitializeOnLoad.Register(Initialize);
        }

        /// <summary>
        /// Returns the active rule set — remote when a successful fetch has
        /// completed, otherwise the baked-in fallback. Always non-null.
        /// </summary>
        public static HandReadinessCodeScanRules.Rule[] GetRules()
            => _activeRules ?? HandReadinessCodeScanRules.BakedInRules;

        private static void Initialize()
        {
#pragma warning disable CS4014
            FetchAndSwap();
#pragma warning restore CS4014
        }

        private static async Task FetchAndSwap()
        {
            try
            {
                var result = await RemoteJsonContent<RulesPayload>.Create(CacheFileName, RulesContentId);
                if (!result.IsSuccess)
                {
                    EmitFailure(TelemetryConstants.ErrorKind.RulesFetchFailed, result.ErrorMessage);
                    return;
                }

                var payload = result.Content;
                if (payload.version != SupportedSchemaVersion)
                {
                    EmitFailure(
                        TelemetryConstants.ErrorKind.RulesSchemaMismatch,
                        $"version={payload.version}, expected={SupportedSchemaVersion}",
                        payload.version);
                    return;
                }

                if (payload.rules == null || payload.rules.Length == 0)
                {
                    EmitFailure(TelemetryConstants.ErrorKind.RulesEmptyPayload, null, payload.version);
                    return;
                }

                var compiled = CompilePayload(payload);
                if (compiled.Count == 0)
                {
                    EmitFailure(TelemetryConstants.ErrorKind.RulesAllInvalid, null, payload.version);
                    return;
                }

                _activeRules = compiled.ToArray();
                EmitSuccess(compiled.Count, payload.version);
                OnRulesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                EmitFailure(TelemetryConstants.ErrorKind.Unknown, ex.Message);
            }
        }

        private static List<HandReadinessCodeScanRules.Rule> CompilePayload(RulesPayload payload)
        {
            var compiled = new List<HandReadinessCodeScanRules.Rule>(payload.rules.Length);
            foreach (var entry in payload.rules)
            {
                if (string.IsNullOrEmpty(entry.id) || string.IsNullOrEmpty(entry.pattern))
                {
                    Debug.LogWarning(
                        $"[HRT] RulesProvider: skipping rule with missing id or pattern (id='{entry.id}').");
                    continue;
                }

                Regex regex;
                try
                {
                    regex = new Regex(entry.pattern, RegexOptions.Compiled, RemoteRegexTimeout);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[HRT] RulesProvider: skipping rule '{entry.id}' — malformed regex: {ex.Message}");
                    continue;
                }

                compiled.Add(new HandReadinessCodeScanRules.Rule
                {
                    Id = entry.id,
                    Title = entry.title ?? entry.id,
                    Pattern = regex,
                    Problem = entry.problem ?? string.Empty,
                    Recommendation = entry.recommendation ?? string.Empty,
                    Steps = entry.steps != null
                        ? new List<string>(entry.steps)
                        : new List<string>(),
                });
            }
            return compiled;
        }

        private static void EmitSuccess(int ruleCount, int schemaVersion)
        {
            Debug.Log($"[HRT] RulesProvider: loaded {ruleCount} remote rule(s) (schema v{schemaVersion}).");
            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.RulesFetched,
                evt =>
                {
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Success, true);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.RuleSource, TelemetryConstants.RuleSourceValue.Remote);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.RuleCount, ruleCount);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.SchemaVersion, schemaVersion);
                });
        }

        private static void EmitFailure(string errorKind, string errorMessage, int? schemaVersion = null)
        {
            var bakedCount = HandReadinessCodeScanRules.BakedInRules?.Length ?? 0;
            Debug.Log(
                $"[HRT] RulesProvider: remote fetch did not swap rules ({errorKind}" +
                (string.IsNullOrEmpty(errorMessage) ? "" : $": {errorMessage}") +
                $") — falling back to {bakedCount} baked-in rule(s).");
            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.RulesFetched,
                evt =>
                {
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Success, false);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.RuleSource, TelemetryConstants.RuleSourceValue.Local);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.RuleCount, bakedCount);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.ErrorKind, errorKind);
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        evt.SetMetadata(TelemetryConstants.AnnotationType.ErrorMessage, errorMessage);
                    }
                    if (schemaVersion.HasValue)
                    {
                        evt.SetMetadata(TelemetryConstants.AnnotationType.SchemaVersion, schemaVersion.Value);
                    }
                });
        }

        // JsonUtility-compatible envelope. The struct constraint comes from
        // RemoteJsonContent<T>'s `where T : struct`. Arrays (not List<>) so
        // JsonUtility's single-level-generic limitation doesn't bite.
        [Serializable]
        internal struct RulesPayload
        {
            public int version;
            public RuleEntry[] rules;
        }

        [Serializable]
        internal struct RuleEntry
        {
            public string id;
            public string title;
            public string pattern;
            public string problem;
            public string recommendation;
            public string[] steps;
        }
    }
}

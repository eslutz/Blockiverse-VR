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
using System.Threading.Tasks;
using Meta.XR.Editor.Callbacks;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using TelemetryConstants = Meta.HandReadinessTool.Editor.HandReadinessTelemetryConstants;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Source of the AI-path skill/prompt content consumed by
    /// <see cref="KnowledgeLoader.BuildCompletePrompt"/>. Fetches a static JSON
    /// asset at editor startup that mirrors the three local Knowledge files —
    /// `Knowledge/SKILL.md` (system prompt) plus the two reference markdowns
    /// under `Knowledge/references/`. When the remote fetch fails or the payload
    /// is malformed the provider returns null/false and the loader falls back
    /// to reading the bundled markdown files from disk.
    /// </summary>
    [InitializeOnLoad]
    internal static class HandReadinessPromptProvider
    {
        private const ulong PromptContentId = 27108647752099792UL;
        private const string CacheFileName = "hrt_prompt.json";
        private const int SupportedSchemaVersion = 1;

        private static string _systemPrompt;
        private static Dictionary<string, RemoteReference> _references;

        public struct RemoteReference
        {
            public string Heading;
            public string Content;
        }

        static HandReadinessPromptProvider()
        {
            InitializeOnLoad.Register(Initialize);
        }

        /// <summary>
        /// Remote system prompt content. Null when the fetch has not yet
        /// completed or failed — callers fall back to the on-disk SKILL.md.
        /// </summary>
        public static string GetSystemPrompt() => _systemPrompt;

        /// <summary>
        /// Returns the remote content for a reference by name (e.g.
        /// `hand-tracking-patterns`), or null if no remote payload is loaded
        /// or no entry exists for that name.
        /// </summary>
        public static string GetReferenceContent(string name)
            => _references != null && _references.TryGetValue(name, out var r) ? r.Content : null;

        /// <summary>
        /// Returns the remote heading for a reference (rendered as `# {heading}`
        /// in the assembled prompt) or null when no remote payload is loaded.
        /// Callers should fall back to a hardcoded heading.
        /// </summary>
        public static string GetReferenceHeading(string name)
            => _references != null && _references.TryGetValue(name, out var r) ? r.Heading : null;

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
                var result = await RemoteJsonContent<PromptPayload>.Create(CacheFileName, PromptContentId);
                if (!result.IsSuccess)
                {
                    EmitFailure(TelemetryConstants.ErrorKind.PromptFetchFailed, result.ErrorMessage);
                    return;
                }

                var payload = result.Content;
                if (payload.version != SupportedSchemaVersion)
                {
                    EmitFailure(
                        TelemetryConstants.ErrorKind.PromptSchemaMismatch,
                        $"version={payload.version}, expected={SupportedSchemaVersion}",
                        payload.version);
                    return;
                }

                if (string.IsNullOrEmpty(payload.systemPrompt))
                {
                    EmitFailure(TelemetryConstants.ErrorKind.PromptEmptySystem, null, payload.version);
                    return;
                }

                var refs = BuildReferenceMap(payload.references);

                _systemPrompt = payload.systemPrompt;
                _references = refs;
                EmitSuccess(payload.systemPrompt.Length, refs.Count, payload.version);
            }
            catch (Exception ex)
            {
                EmitFailure(TelemetryConstants.ErrorKind.Unknown, ex.Message);
            }
        }

        private static Dictionary<string, RemoteReference> BuildReferenceMap(PromptReference[] refs)
        {
            var map = new Dictionary<string, RemoteReference>();
            if (refs == null) return map;

            foreach (var r in refs)
            {
                if (string.IsNullOrEmpty(r.name))
                {
                    Debug.LogWarning("[HRT] PromptProvider: skipping reference with missing name.");
                    continue;
                }
                if (string.IsNullOrEmpty(r.content))
                {
                    Debug.LogWarning(
                        $"[HRT] PromptProvider: skipping reference '{r.name}' with empty content.");
                    continue;
                }
                map[r.name] = new RemoteReference
                {
                    Heading = r.heading ?? string.Empty,
                    Content = r.content,
                };
            }
            return map;
        }

        private static void EmitSuccess(int systemPromptChars, int referenceCount, int schemaVersion)
        {
            Debug.Log(
                $"[HRT] PromptProvider: loaded remote prompt " +
                $"(systemPrompt={systemPromptChars}c, references={referenceCount}, schema v{schemaVersion}).");
            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.PromptFetched,
                evt =>
                {
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Success, true);
                    evt.SetMetadata(
                        TelemetryConstants.AnnotationType.PromptSource,
                        TelemetryConstants.PromptSourceValue.Remote);
                    evt.SetMetadata(
                        TelemetryConstants.AnnotationType.SystemPromptChars, systemPromptChars);
                    evt.SetMetadata(
                        TelemetryConstants.AnnotationType.ReferenceCount, referenceCount);
                    evt.SetMetadata(
                        TelemetryConstants.AnnotationType.SchemaVersion, schemaVersion);
                });
        }

        private static void EmitFailure(string errorKind, string errorMessage, int? schemaVersion = null)
        {
            Debug.Log(
                $"[HRT] PromptProvider: remote fetch did not load prompt ({errorKind}" +
                (string.IsNullOrEmpty(errorMessage) ? "" : $": {errorMessage}") +
                ") — falling back to bundled SKILL.md and reference markdowns.");
            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.PromptFetched,
                evt =>
                {
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Success, false);
                    evt.SetMetadata(
                        TelemetryConstants.AnnotationType.PromptSource,
                        TelemetryConstants.PromptSourceValue.Local);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.ErrorKind, errorKind);
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        evt.SetMetadata(TelemetryConstants.AnnotationType.ErrorMessage, errorMessage);
                    }
                    if (schemaVersion.HasValue)
                    {
                        evt.SetMetadata(
                            TelemetryConstants.AnnotationType.SchemaVersion, schemaVersion.Value);
                    }
                });
        }

        [Serializable]
        internal struct PromptPayload
        {
            public int version;
            public string systemPrompt;
            public PromptReference[] references;
        }

        [Serializable]
        internal struct PromptReference
        {
            public string name;
            public string heading;
            public string content;
        }
    }
}

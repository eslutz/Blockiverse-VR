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
using System.Text.RegularExpressions;
using Meta.XR.Telemetry;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Editor utilities colocated with the storage editor (no reflection).
    /// - Gets provider id from IUsesCredential.ProviderId property, or falls back to type name derivation for legacy support.
    /// - Migrates legacy lowercase provider ids (e.g., "openai") to CamelCase, preserving keys.
    /// - Finds all provider assets for a given provider id by searching type "t:&lt;ProviderId&gt;Provider".
    /// - IMPORTANT: Does NOT create storage if missing; logs an error instead.
    /// - NOTE: For production builds, consider adding a UPST rule to enforce that only one CredentialStorage
    ///   exists per project, rather than relying on runtime warnings. This would fail the build if multiple
    ///   storage assets are detected.
    /// </summary>
    public static class CredentialEditorUtil
    {
        /// <summary>
        /// Gets the existing credential storage asset in the project, or null if none exists.
        /// </summary>
        public static CredentialStorage StorageOrNull => FindExistingOrNull();

        /// <summary>
        /// Get provider id from IUsesCredential.ProviderId property if available, otherwise derives from type name.
        /// Example fallback: "LlamaApiProvider" -> "LlamaApi"
        /// </summary>
        public static string GetProviderId(AIProviderBase providerAsset)
        {
            if (!providerAsset)
            {
                return string.Empty;
            }

            if (providerAsset is IUsesCredential credProvider)
            {
                return credProvider.ProviderId;
            }

            var typeName = providerAsset.GetType().Name;
            typeName = Regex.Replace(typeName, "Provider$", "");
            return typeName.Trim();
        }

        private static bool EnsureStorageOrError(out CredentialStorage storage)
        {
            storage = StorageOrNull;
            if (storage)
            {
                return true;
            }

            IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "credential-storage-missing",
                "[CredentialStorage] No CredentialStorage asset found in the project. Did you " +
                "know you can store all your credential in a central storage?" +
                "Please create one via Assets > Create > Meta > AI > Credential Storage.");
            return false;
        }

        private static CredentialStorage FindExistingOrNull()
        {
            var guids = AssetDatabase.FindAssets("t:Meta.XR.BuildingBlocks.AIBlocks.CredentialStorage");
            if (guids is not { Length: > 0 })
            {
                return null;
            }

            if (guids.Length > 1)
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "credential-storage-multiple",
                    "[CredentialStorage] Multiple CredentialStorage assets found. " +
                    "Using the first one found; please keep only one in your project.");
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var first = AssetDatabase.LoadAssetAtPath<CredentialStorage>(path);
            return first;
        }

        /// <summary>
        /// Find the entry for the given provider id (case-insensitive).
        /// Checks CamelCase version first, then falls back to lowercase for legacy compatibility.
        /// If no storage exists, returns null and logs an error.
        /// </summary>
        private static CredentialStorage.Entry FindEntry(string providerId)
        {
            if (!EnsureStorageOrError(out var storage))
            {
                return null;
            }

            if (storage.TryGetEntry(providerId, out var entry))
            {
                return entry;
            }

            var legacy = providerId.ToLowerInvariant();
            return storage.TryGetEntry(legacy, out var legacyEntry) ? legacyEntry : null;
        }

        /// <summary>
        /// Migrate a legacy lowercase provider id to CamelCase format.
        /// </summary>
        private static void MigrateLegacyProviderId(CredentialStorage storage, string legacyId, string newId)
        {
            if (!storage || string.IsNullOrEmpty(legacyId) || string.IsNullOrEmpty(newId))
            {
                return;
            }

            if (!storage.TryGetEntry(legacyId, out var legacyEntry))
            {
                return;
            }

            if (storage.TryGetEntry(newId, out _))
            {
                return;
            }

            Undo.RecordObject(storage, "Migrate Legacy Provider ID");
            legacyEntry.providerId = newId;
            EditorUtility.SetDirty(storage);
        }

        /// <summary>
        /// Find entry and migrate legacy lowercase id if needed.
        /// </summary>
        private static CredentialStorage.Entry FindEntryWithMigration(string providerId)
        {
            var entry = FindEntry(providerId);
            if (entry == null || entry.providerId == providerId)
            {
                return entry;
            }

            var storage = StorageOrNull;
            if (storage)
            {
                MigrateLegacyProviderId(storage, entry.providerId, providerId);
            }

            return entry;
        }

        /// <summary>
        /// Ensures that a credential entry exists in the storage for the specified provider, creating one if missing.
        /// </summary>
        /// <param name="storage">The credential storage to check and update.</param>
        /// <param name="providerId">The identifier of the provider to ensure an entry for.</param>
        public static void EnsureProviderEntry(CredentialStorage storage, string providerId)
        {
            if (!storage)
            {
                return;
            }

            if (storage.TryGetEntry(providerId, out _))
            {
                return;
            }

            Undo.RecordObject(storage, "Register Provider");
            storage.AddProviderIfMissing(providerId);
            EditorUtility.SetDirty(storage);
        }

        /// <summary>
        /// Sets the API key for a provider entry in the storage, but only if the entry's key is currently empty.
        /// </summary>
        /// <param name="storage">The credential storage containing the provider entry.</param>
        /// <param name="providerId">The identifier of the provider whose key to seed.</param>
        /// <param name="key">The API key value to set if the existing key is empty.</param>
        public static void SeedKeyIfEmpty(CredentialStorage storage, string providerId, string key)
        {
            if (!storage || string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!storage.TryGetEntry(providerId, out var entry))
            {
                return;
            }

            if (!string.IsNullOrEmpty(entry.apiKey))
            {
                return;
            }

            Undo.RecordObject(storage, "Seed API Key");
            entry.apiKey = key;
            EditorUtility.SetDirty(storage);
        }

        /// <summary>
        /// Retrieves the stored API key for the given provider asset from credential storage, performing legacy ID migration if needed.
        /// </summary>
        /// <param name="providerAsset">The provider asset to look up the key for.</param>
        /// <returns>The stored API key, or an empty string if not found.</returns>
        public static string GetStorageKey(AIProviderBase providerAsset)
        {
            var pid = GetProviderId(providerAsset);
            var e = FindEntryWithMigration(pid);
            return e != null ? e.apiKey : string.Empty;
        }

        /// <summary>
        /// Selects and highlights the credential storage asset in the Unity Project window.
        /// </summary>
        public static void PingStorageAsset()
        {
            var storage = StorageOrNull;
            if (!storage)
            {
                IssueTracker.TrackError(IssueTracker.SDK.BuildingBlocks, "credential-storage-open-failed",
                    "[CredentialStorage] No CredentialStorage asset found to open.");
                return;
            }

            Selection.activeObject = storage;
            EditorGUIUtility.PingObject(storage);
        }

        /// <summary>
        /// Find all provider assets for a given provider id by type name "t:&lt;ProviderId&gt;Provider".
        /// e.g., "OpenAI" -> search "t:OpenAIProvider".
        /// Returns loaded ScriptableObject assets (nulls filtered).
        /// </summary>
        public static List<Object> FindProviderAssets(string providerId)
        {
            var results = new List<Object>();
            if (string.IsNullOrEmpty(providerId))
            {
                return results;
            }

            var typeFilter = $"t:{providerId}Provider";
            var guids = AssetDatabase.FindAssets(typeFilter);
            if (guids == null || guids.Length == 0)
            {
                return results;
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (obj != null)
                {
                    results.Add(obj);
                }
            }

            return results;
        }
    }
}

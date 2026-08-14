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
using System.IO;
using UnityEditor;
using UnityEngine;
using Meta.HandReadinessTool.Editor.UI;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Loads knowledge files (system prompts, implementation guides) for AI analysis.
    /// </summary>
    internal static class KnowledgeLoader
    {
        private const string KnowledgeFolderName = "Knowledge";
        private const string SkillPromptFileName = "SKILL.md";
        private const string ReferencesFolderName = "references";
        private const string HandTrackingKnowledgeFileName = "hand-tracking-patterns.md";
        private const string MigrationGuideFileName = "migration-guide.md";

        private static string _knowledgeFolderPath;

        /// <summary>
        /// Gets the path to the Knowledge folder.
        /// </summary>
        public static string KnowledgeFolderPath
        {
            get
            {
                if (string.IsNullOrEmpty(_knowledgeFolderPath))
                {
                    _knowledgeFolderPath = FindKnowledgeFolder();
                }
                return _knowledgeFolderPath;
            }
        }

        // Reference names used to look up remote content in
        // HandReadinessPromptProvider. Must match the `name` values in the
        // uploaded prompt JSON payload.
        private const string HandTrackingReferenceName = "hand-tracking-patterns";
        private const string MigrationGuideReferenceName = "migration-guide";

        // Default headings rendered as `# {heading}` in BuildCompletePrompt
        // when the remote payload doesn't override them.
        private const string HandTrackingDefaultHeading = "Hand Tracking Implementation Reference";
        private const string MigrationGuideDefaultHeading = "Controller-to-Hand Tracking Migration Guide";

        /// <summary>
        /// Loads the system prompt. Returns the remote payload when one has
        /// loaded via <see cref="HandReadinessPromptProvider"/>, otherwise
        /// reads the bundled `Knowledge/SKILL.md` from disk.
        /// </summary>
        public static string LoadSystemPrompt()
        {
            var remote = HandReadinessPromptProvider.GetSystemPrompt();
            if (!string.IsNullOrEmpty(remote)) return remote;
            var path = Path.Combine(KnowledgeFolderPath, SkillPromptFileName);
            return LoadFile(path, "Skill Prompt");
        }

        /// <summary>
        /// Loads the hand-tracking patterns reference. Remote-first with the
        /// bundled markdown as fallback.
        /// </summary>
        public static string LoadHandTrackingKnowledge()
        {
            var remote = HandReadinessPromptProvider.GetReferenceContent(HandTrackingReferenceName);
            if (!string.IsNullOrEmpty(remote)) return remote;
            var path = Path.Combine(KnowledgeFolderPath, ReferencesFolderName, HandTrackingKnowledgeFileName);
            return LoadFile(path, "Hand Tracking Knowledge");
        }

        /// <summary>
        /// Loads the migration guide reference. Remote-first with the bundled
        /// markdown as fallback.
        /// </summary>
        public static string LoadMigrationGuide()
        {
            var remote = HandReadinessPromptProvider.GetReferenceContent(MigrationGuideReferenceName);
            if (!string.IsNullOrEmpty(remote)) return remote;
            var path = Path.Combine(KnowledgeFolderPath, ReferencesFolderName, MigrationGuideFileName);
            return LoadFile(path, "Migration Guide");
        }

        /// <summary>
        /// Builds the complete AI prompt by concatenating system prompt and knowledge files.
        /// </summary>
        /// <param name="projectDescription">Optional user-provided project description.</param>
        /// <param name="priorAiSuggestions">
        /// AI suggestions from a previous scan, if this is a re-scan. When non-empty, the prompt
        /// asks the AI to map fresh recommendations back to prior IDs and to flag prior items it
        /// believes are now resolved by the current code state.
        /// </param>
        /// <returns>The complete prompt to send to the AI.</returns>
        public static string BuildCompletePrompt(
            string projectDescription = null,
            List<IssueData> priorAiSuggestions = null)
        {
            var systemPrompt = LoadSystemPrompt();
            var handTrackingKnowledge = LoadHandTrackingKnowledge();

            if (string.IsNullOrEmpty(systemPrompt))
            {
                Debug.LogError("[HRT] Failed to load system prompt. AI analysis may not work correctly.");
                return null;
            }

            var promptBuilder = new System.Text.StringBuilder();

            // Add system prompt
            promptBuilder.AppendLine(systemPrompt);
            promptBuilder.AppendLine();

            // Add hand tracking knowledge as reference. Heading comes from the
            // remote payload when present so it's tweakable alongside the body.
            if (!string.IsNullOrEmpty(handTrackingKnowledge))
            {
                var heading = HandReadinessPromptProvider.GetReferenceHeading(HandTrackingReferenceName);
                if (string.IsNullOrEmpty(heading)) heading = HandTrackingDefaultHeading;
                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine($"# {heading}");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine(handTrackingKnowledge);
                promptBuilder.AppendLine();
            }

            // Add migration guide if available
            var migrationGuide = LoadMigrationGuide();
            if (!string.IsNullOrEmpty(migrationGuide))
            {
                var heading = HandReadinessPromptProvider.GetReferenceHeading(MigrationGuideReferenceName);
                if (string.IsNullOrEmpty(heading)) heading = MigrationGuideDefaultHeading;
                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine($"# {heading}");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine(migrationGuide);
                promptBuilder.AppendLine();
            }

            // Add project description if provided
            if (!string.IsNullOrEmpty(projectDescription))
            {
                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("# Developer's Project Description");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("The developer has provided the following description of their project:");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("```");
                promptBuilder.AppendLine(projectDescription);
                promptBuilder.AppendLine("```");
                promptBuilder.AppendLine();
            }

            bool isRescan = priorAiSuggestions != null && priorAiSuggestions.Count > 0;
            if (isRescan)
            {
                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("# Previous Recommendations (this is a re-scan)");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("The user already ran this analysis once and saw the recommendations below. Some they marked complete via the UI; others remain pending. Use these IDs in `previousId` and `resolvedFromPrior` fields when you compose the new response.");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("| ID | User-marked status | Title | Description |");
                promptBuilder.AppendLine("|----|--------------------|-------|-------------|");
                foreach (var prior in priorAiSuggestions)
                {
                    var id = prior.TaskUid ?? "(missing)";
                    var status = prior.IsFixed ? "RESOLVED (user)" : "PENDING";
                    var title = (prior.Title ?? "").Replace("|", "\\|");
                    var desc = (prior.Description ?? "").Replace("|", "\\|").Replace("\n", " ");
                    if (desc.Length > 200) desc = desc.Substring(0, 197) + "...";
                    promptBuilder.AppendLine($"| {id} | {status} | {title} | {desc} |");
                }
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Rules for re-scan output:");
                promptBuilder.AppendLine("- For each recommendation in your new `suggestions` list that maps to one above, set `previousId` to that prior ID. Otherwise omit `previousId` (it is a new finding).");
                promptBuilder.AppendLine("- Reuse the same `taskUid` value across re-scans for the same recommendation when possible. New recommendations should have fresh `taskUid` slugs of the form `hrt-ai:short-slug`.");
                promptBuilder.AppendLine("- For each prior recommendation you believe is now resolved by the current code (regardless of whether the user marked it), include its ID in the `resolvedFromPrior` array.");
                promptBuilder.AppendLine("- Items the user already marked as `RESOLVED (user)` should typically NOT reappear in your `suggestions` list, unless the code state contradicts the user's claim — in which case include them again with the same `previousId` so the UI can show that you are re-flagging.");
                promptBuilder.AppendLine();
            }

            // Add Unity-specific JSON response format requirement
            // (This is NOT in SKILL.md because Claude Code users get conversational output)
            promptBuilder.AppendLine("---");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("# Response Format (Unity Tool)");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("You are being called from a Unity Editor tool. Your response MUST end with a JSON block wrapped in ```json and ``` markers.");
            promptBuilder.AppendLine("You may include natural language analysis BEFORE the JSON block.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("```json");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"analysisComplete\": true,");
            promptBuilder.AppendLine("  \"projectType\": \"string describing the type of game/experience\",");
            promptBuilder.AppendLine("  \"suggestions\": [");
            promptBuilder.AppendLine("    {");
            promptBuilder.AppendLine("      \"taskUid\": \"hrt-ai:short-stable-slug\",");
            promptBuilder.AppendLine("      \"previousId\": \"hrt-ai:matching-prior-id-or-omit\",");
            promptBuilder.AppendLine("      \"title\": \"Short title (max 60 chars)\",");
            promptBuilder.AppendLine("      \"description\": \"What was found and what should change\",");
            promptBuilder.AppendLine("      \"currentImplementation\": \"What you found in their code\",");
            promptBuilder.AppendLine("      \"handTrackingAdaptation\": \"How to enhance with hand tracking\",");
            promptBuilder.AppendLine("      \"implementationSteps\": [\"Step 1\", \"Step 2\"],");
            promptBuilder.AppendLine("      \"complexity\": \"Low|Medium|High\",");
            promptBuilder.AppendLine("      \"priority\": \"High|Medium|Low\"");
            promptBuilder.AppendLine("    }");
            promptBuilder.AppendLine("  ],");
            promptBuilder.AppendLine("  \"resolvedFromPrior\": [\"hrt-ai:prior-id-1\", \"hrt-ai:prior-id-2\"]");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine("```");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Notes on the new fields:");
            promptBuilder.AppendLine("- `taskUid` should be a stable slug for cross-scan identity. Use the form `hrt-ai:short-slug`.");
            promptBuilder.AppendLine("- `previousId` is only meaningful on a re-scan and should be omitted on the first scan or when the recommendation is genuinely new.");
            promptBuilder.AppendLine("- `resolvedFromPrior` is only meaningful on a re-scan. Empty array on the first scan.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Please analyze the user's Unity project and provide hand-tracking adaptation suggestions.");
            promptBuilder.AppendLine("Search through the project's scripts to understand the codebase, then provide your suggestions in the JSON format above.");

            return promptBuilder.ToString();
        }

        private static string LoadFile(string path, string fileDescription)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"[HRT] {fileDescription} path is null or empty.");
                return null;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[HRT] {fileDescription} not found at: {path}");
                return null;
            }

            try
            {
                var content = File.ReadAllText(path);
                return content;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HRT] Failed to load {fileDescription}: {ex.Message}");
                return null;
            }
        }

        private static string FindKnowledgeFolder()
        {
            // Try to find the Knowledge folder relative to this script
            var guids = AssetDatabase.FindAssets("t:Script AIResponseParser");
            foreach (var guid in guids)
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (scriptPath.Contains("HandReadinessTool"))
                {
                    var toolFolder = Path.GetDirectoryName(scriptPath);
                    var knowledgePath = Path.Combine(toolFolder, KnowledgeFolderName);

                    // Convert to absolute path
                    var projectPath = Path.GetDirectoryName(Application.dataPath);
                    var absolutePath = Path.Combine(projectPath, knowledgePath);

                    if (Directory.Exists(absolutePath))
                    {
                        return absolutePath;
                    }
                }
            }

            // Fallback: try known paths
            var fallbackPaths = new[]
            {
                "Assets/Oculus/VR/Editor/HandReadinessTool/Knowledge",
                "Packages/com.meta.xr.sdk.core/Editor/HandReadinessTool/Knowledge"
            };

            foreach (var relativePath in fallbackPaths)
            {
                var projectPath = Path.GetDirectoryName(Application.dataPath);
                var absolutePath = Path.Combine(projectPath, relativePath);
                if (Directory.Exists(absolutePath))
                {
                    return absolutePath;
                }
            }

            Debug.LogError("[HRT] Could not find Knowledge folder. AI features may not work correctly.");
            return null;
        }
    }
}

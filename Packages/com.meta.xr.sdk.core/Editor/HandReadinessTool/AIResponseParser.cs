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
using UnityEngine;
using Meta.HandReadinessTool.Editor.UI;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Parses AI responses to extract structured suggestion data.
    /// Uses Unity's built-in JsonUtility for JSON parsing.
    /// </summary>
    internal static class AIResponseParser
    {
        /// <summary>
        /// Result of parsing an AI response.
        /// </summary>
        public class ParseResult
        {
            public bool Success { get; set; }
            public string ProjectType { get; set; }
            public List<IssueData> Suggestions { get; set; }
            // Prior TaskUids the AI says are now resolved by the current code state.
            // The merge step marks the matching prior items as IsFixed=true.
            public List<string> ResolvedFromPrior { get; set; }
            public string NoSuggestionsReason { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Internal JSON structure matching the AI response format.
        /// Uses Unity's JsonUtility with [Serializable] attribute.
        /// </summary>
        [Serializable]
        private class AIResponse
        {
            public bool analysisComplete;
            public string projectType;
            public List<AISuggestion> suggestions;
            public List<string> resolvedFromPrior;
            public string noSuggestionsReason;
        }

        [Serializable]
        private class AISuggestion
        {
            public string title;
            public string description;
            public string currentImplementation;
            public string handTrackingAdaptation;
            public List<string> implementationSteps;
            public string complexity;
            public string priority;
            public string initialPrompt;
            // Stable id for cross-scan matching. AI is asked to keep this consistent
            // across re-scans for "the same recommendation". Falls back to a slug of
            // the title when omitted.
            public string taskUid;
            // Set when this fresh recommendation maps to one from the prior scan.
            public string previousId;
        }

        /// <summary>
        /// Extracts the JSON block from the AI response text.
        /// The JSON is expected to be wrapped in ```json and ``` markers, or be a raw JSON object.
        /// </summary>
        public static string ExtractJsonBlock(string responseText)
        {
            if (string.IsNullOrEmpty(responseText))
            {
                Debug.LogWarning("[HRT] ExtractJsonBlock: responseText is null or empty");
                return null;
            }

            // Find all ```json ... ``` blocks and use the last one (the AI's response, not examples)
            var jsonBlockMatches = Regex.Matches(
                responseText,
                @"```json\s*([\s\S]*?)\s*```",
                RegexOptions.IgnoreCase);

            if (jsonBlockMatches.Count > 0)
            {
                // Look for a block that contains "analysisComplete" - that's our actual response
                // Start from the end since the AI's response should be last
                for (int i = jsonBlockMatches.Count - 1; i >= 0; i--)
                {
                    var match = jsonBlockMatches[i];
                    var extracted = match.Groups[1].Value.Trim();
                    if (extracted.Contains("\"analysisComplete\""))
                    {
                        return extracted;
                    }
                }

                // If no block has analysisComplete, use the last one as fallback
                var lastMatch = jsonBlockMatches[jsonBlockMatches.Count - 1];
                var lastExtracted = lastMatch.Groups[1].Value.Trim();
                return lastExtracted;
            }

            // Fallback: find the JSON object by looking for {"analysisComplete" pattern
            int startIndex = responseText.IndexOf("{\"analysisComplete\"");
            if (startIndex < 0)
            {
                startIndex = responseText.IndexOf("{\n  \"analysisComplete\"");
            }
            if (startIndex < 0)
            {
                startIndex = responseText.IndexOf("{ \"analysisComplete\"");
            }
            if (startIndex < 0)
            {
                var match = Regex.Match(responseText, @"\{\s*""analysisComplete""");
                if (match.Success)
                {
                    startIndex = match.Index;
                }
            }

            if (startIndex < 0)
            {
                Debug.LogWarning($"[HRT] ExtractJsonBlock: Could not find JSON start pattern. Response preview:\n{responseText.Substring(0, Mathf.Min(1000, responseText.Length))}");
                return null;
            }

            // Use brace matching to find the complete JSON object
            int braceCount = 0;
            int endIndex = -1;
            bool inString = false;
            bool escaped = false;

            for (int i = startIndex; i < responseText.Length; i++)
            {
                char c = responseText[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if (c == '{')
                        braceCount++;
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            endIndex = i;
                            break;
                        }
                    }
                }
            }

            if (endIndex > startIndex)
            {
                return responseText.Substring(startIndex, endIndex - startIndex + 1);
            }

            Debug.LogWarning($"[HRT] ExtractJsonBlock: Brace matching failed. braceCount={braceCount}, inString={inString}");
            return null;
        }

        /// <summary>
        /// Sanitizes JSON by escaping unescaped control characters inside string values.
        /// This handles cases where the AI outputs literal newlines inside JSON strings.
        /// </summary>
        private static string SanitizeJsonString(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            // The AI is outputting malformed JSON with unescaped quotes and newlines.
            // Instead of trying to track quote state (which breaks on malformed JSON),
            // we'll use a different approach: process each line and look for patterns.

            // First, let's try a simple regex-based approach:
            // Replace literal newlines that appear inside JSON string values.
            // We identify these by looking for newlines that are NOT preceded by
            // a comma, colon, bracket, or brace (which would indicate structural JSON).

            var result = new System.Text.StringBuilder(json.Length + 1000);
            var lines = json.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Remove any carriage returns
                line = line.Replace("\r", "");

                result.Append(line);

                // Don't add anything after the last line
                if (i < lines.Length - 1)
                {
                    // Check if this looks like a line that ends inside a string
                    // (i.e., it doesn't end with a structural JSON character after trimming)
                    var trimmed = line.TrimEnd();

                    // Lines that end a JSON value typically end with: , ] } "
                    // Lines that start a JSON structure typically end with: : [ { "
                    // If a line ends with something else, the newline is probably inside a string
                    if (trimmed.Length > 0)
                    {
                        char lastChar = trimmed[trimmed.Length - 1];

                        // These are valid line-ending characters in formatted JSON
                        bool isStructuralEnd = lastChar == ',' || lastChar == '{' || lastChar == '}' ||
                                               lastChar == '[' || lastChar == ']' || lastChar == ':';

                        // Also check if line ends with a closing quote followed by optional comma
                        bool endsWithQuote = trimmed.EndsWith("\"") || trimmed.EndsWith("\",");

                        if (isStructuralEnd || endsWithQuote)
                        {
                            // This is a normal JSON line break - keep it
                            result.Append('\n');
                        }
                        else
                        {
                            // This newline is probably inside a string - escape it
                            result.Append("\\n");
                        }
                    }
                    else
                    {
                        // Empty line - keep the newline
                        result.Append('\n');
                    }
                }
            }

            var sanitized = result.ToString();

            return sanitized;
        }

        /// <summary>
        /// Parses the AI response text and extracts suggestions as IssueData objects.
        /// Uses Unity's JsonUtility for parsing.
        /// </summary>
        public static ParseResult Parse(string responseText)
        {
            var result = new ParseResult
            {
                Success = false,
                Suggestions = new List<IssueData>(),
                ResolvedFromPrior = new List<string>()
            };

            try
            {
                var jsonText = ExtractJsonBlock(responseText);
                if (string.IsNullOrEmpty(jsonText))
                {
                    result.ErrorMessage = "No valid JSON block found in AI response.";
                    return result;
                }

                // Sanitize JSON to escape unescaped control characters in strings
                var sanitizedJson = SanitizeJsonString(jsonText);

                // Use Unity's JsonUtility for parsing
                AIResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<AIResponse>(sanitizedJson);
                }
                catch (Exception)
                {
                    // If sanitized JSON still fails, try parsing the original
                    Debug.LogWarning("[HRT] Sanitized JSON failed to parse, trying original");
                    try
                    {
                        response = JsonUtility.FromJson<AIResponse>(jsonText);
                    }
                    catch (Exception ex)
                    {
                        result.ErrorMessage = $"JSON parsing error: {ex.Message}";
                        return result;
                    }
                }

                if (response == null)
                {
                    result.ErrorMessage = "Failed to parse JSON response - result was null.";
                    return result;
                }

                // Extract top-level fields
                result.ProjectType = response.projectType;
                result.NoSuggestionsReason = response.noSuggestionsReason;
                if (response.resolvedFromPrior != null)
                {
                    result.ResolvedFromPrior = response.resolvedFromPrior;
                }

                // Extract suggestions
                if (response.suggestions != null)
                {
                    foreach (var suggestion in response.suggestions)
                    {
                        try
                        {
                            if (suggestion == null) continue;

                            // Extract each field
                            var title = suggestion.title ?? "Untitled Suggestion";
                            var description = suggestion.description;
                            var currentImpl = suggestion.currentImplementation;
                            var handTrackingAdapt = suggestion.handTrackingAdaptation;
                            var complexity = suggestion.complexity;
                            var priority = suggestion.priority;

                            // initialPrompt is problematic - if we can't get it, build from other fields
                            var initialPrompt = suggestion.initialPrompt;
                            if (string.IsNullOrEmpty(initialPrompt))
                            {
                                initialPrompt = BuildInitialPrompt(title, description, currentImpl, handTrackingAdapt);
                            }

                            // Extract implementation steps
                            var steps = suggestion.implementationSteps ?? new List<string>();

                            var taskUid = !string.IsNullOrEmpty(suggestion.taskUid)
                                ? suggestion.taskUid
                                : "hrt-ai:" + Slugify(title);

                            var issue = new IssueData
                            {
                                Title = title,
                                Description = description,
                                Priority = ParsePriority(priority),
                                Complexity = ParseComplexity(complexity),
                                IsAISuggestion = true,
                                IsFixed = false,
                                RequiresAI = false,
                                InitialPrompt = initialPrompt,
                                CurrentImplementation = currentImpl,
                                HandTrackingAdaptation = handTrackingAdapt,
                                ImplementationSteps = steps,
                                TaskUid = taskUid,
                                PreviousTaskUid = suggestion.previousId
                            };
                            result.Suggestions.Add(issue);
                        }
                        catch (Exception ex)
                        {
                            // Log but continue - don't fail the whole parse for one bad suggestion
                            Debug.LogWarning($"[HRT] Failed to parse one suggestion: {ex.Message}");
                        }
                    }
                }

                result.Success = result.Suggestions.Count > 0 || !string.IsNullOrEmpty(result.NoSuggestionsReason);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error parsing AI response: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Builds an initial prompt from other fields when the AI's initialPrompt is malformed.
        /// </summary>
        private static string BuildInitialPrompt(string title, string description, string currentImpl, string handTrackingAdapt)
        {
            var sb = new System.Text.StringBuilder();

            // Add discussion mode instruction at the start
            sb.AppendLine("## IMPORTANT: Discussion Mode Only");
            sb.AppendLine("You are in DISCUSSION MODE. You must NEVER edit, modify, or write code directly.");
            sb.AppendLine("Instead, you should:");
            sb.AppendLine("- Analyze the codebase and explain what you find");
            sb.AppendLine("- Discuss implementation approaches and trade-offs");
            sb.AppendLine("- Provide guidance, examples, and explanations");
            sb.AppendLine("- Point the user to relevant files and patterns");
            sb.AppendLine();
            sb.AppendLine("When the user asks you to implement something, respond with detailed guidance on HOW they should do it, but do NOT make the changes yourself.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            sb.AppendLine($"# Help me understand how to implement: {title}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(description))
            {
                sb.AppendLine("## Description");
                sb.AppendLine(description);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(currentImpl))
            {
                sb.AppendLine("## Current Implementation");
                sb.AppendLine(currentImpl);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(handTrackingAdapt))
            {
                sb.AppendLine("## Recommended Hand Tracking Adaptation");
                sb.AppendLine(handTrackingAdapt);
                sb.AppendLine();
            }

            sb.AppendLine("Please discuss how I should implement this change step by step. Remember: provide guidance only, do not edit any files.");

            return sb.ToString();
        }

        private static IssuePriority ParsePriority(string priorityString)
        {
            if (string.IsNullOrEmpty(priorityString))
                return IssuePriority.Medium;

            return priorityString.ToLowerInvariant() switch
            {
                "high" => IssuePriority.High,
                "medium" => IssuePriority.Medium,
                "low" => IssuePriority.Low,
                _ => IssuePriority.Medium
            };
        }

        private static IssueComplexity ParseComplexity(string complexityString)
        {
            if (string.IsNullOrEmpty(complexityString))
                return IssueComplexity.Medium;

            return complexityString.ToLowerInvariant() switch
            {
                "high" => IssueComplexity.High,
                "medium" => IssueComplexity.Medium,
                "low" => IssueComplexity.Low,
                _ => IssueComplexity.Medium
            };
        }

        /// <summary>
        /// Lowercase, replace non-alphanumeric runs with a single hyphen, trim, truncate.
        /// Used as a deterministic fallback when the AI omits `taskUid` so we still
        /// have a stable id for the merge step.
        /// </summary>
        private static string Slugify(string input)
        {
            if (string.IsNullOrEmpty(input)) return "untitled";
            var slug = Regex.Replace(input.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            if (slug.Length > 80) slug = slug.Substring(0, 80);
            return string.IsNullOrEmpty(slug) ? "untitled" : slug;
        }
    }
}

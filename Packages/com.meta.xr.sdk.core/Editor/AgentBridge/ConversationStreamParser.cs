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

using Meta.XR.Json;

namespace Meta.XR.AI.AgentBridge
{
    /// <summary>
    /// Shared types for parsing conversation content from AI service stream output.
    /// Service-specific parsing logic is co-located with each service:
    /// - ClaudeCodeService: GetConversationContent (for Claude Code CLI)
    /// - DevmateService: GetConversationContentFromJson (for Devmate Unity Bridge)
    /// - AcpParser: GetConversationContentFromAcp (for Gemini/ACP)
    /// </summary>
    public static class ConversationStreamParser
    {
        private const int MaxContextLength = 120;

        /// <summary>
        /// Result of conversation content detection.
        /// Used by all AI services to represent parsed message content.
        /// </summary>
        public class ConversationInfo
        {
            /// <summary>Indicates whether the parsed result contains meaningful content.</summary>
            public bool HasContent { get; set; }
            /// <summary>The text content of the parsed message.</summary>
            public string Content { get; set; } = "";
            /// <summary>The type of message (e.g., "user", "assistant", "thinking", "tool_use", "tool_result").</summary>
            public string MessageType { get; set; } = "";
            /// <summary>The name of the tool being invoked, if applicable.</summary>
            public string? ToolName { get; set; }
            /// <summary>The method name for tool calls, if applicable.</summary>
            public string? Method { get; set; }
            /// <summary>The target of the tool call, if applicable.</summary>
            public string? Target { get; set; }
            /// <summary>The rationale or reasoning behind the message, if applicable.</summary>
            public string? Rationale { get; set; }
            /// <summary>Additional JSON data associated with the message.</summary>
            public JsonObject? AdditionalData { get; set; }
            /// <summary>The unique identifier for the message, used for streaming update correlation.</summary>
            public string? MessageId { get; set; }
            /// <summary>Indicates whether this message is still being streamed.</summary>
            public bool IsStreaming { get; set; }
            /// <summary>
            /// If true, Content is a delta (append to existing). If false, Content is the full text (replace).
            /// Claude Code CLI sends deltas, while Devmate Unity Bridge sends full accumulated text.
            /// </summary>
            public bool IsDelta { get; set; }
        }

        internal static string FormatToolContent(
            string? toolName,
            string? method,
            string? target,
            string? rationale,
            JsonObject? inputJson)
        {
            var toolDisplay = !string.IsNullOrEmpty(method)
                ? $"{toolName}/{method}"
                : toolName;

            var result = $"[{toolDisplay}]";

            if (!string.IsNullOrEmpty(target))
            {
                result += $" ({target})";
            }

            if (!string.IsNullOrEmpty(rationale))
            {
                result += $" {rationale}";
                return result;
            }

            if (inputJson == null)
                return result;

            var context = ExtractToolContext(toolName, inputJson);
            if (!string.IsNullOrEmpty(context))
            {
                result += $" {context}";
            }

            return result;
        }

        private static string? ExtractToolContext(string? toolName, JsonObject input)
        {
            if (string.IsNullOrEmpty(toolName))
                return null;

            string? context;
            switch (toolName)
            {
                case "Bash":
                    context = input["description"]?.ToString()
                              ?? input["command"]?.ToString();
                    break;
                case "Read":
                case "Write":
                case "Edit":
                    context = input["file_path"]?.ToString();
                    break;
                case "Grep":
                    var pattern = input["pattern"]?.ToString();
                    var grepPath = input["path"]?.ToString();
                    if (pattern == null) return null;
                    context = $"\"{pattern}\"";
                    if (grepPath != null)
                        context += $" in {grepPath}";
                    break;
                case "Glob":
                    context = input["pattern"]?.ToString();
                    break;
                case "Skill":
                    context = input["skill"]?.ToString();
                    break;
                case "ToolSearch":
                    context = input["query"]?.ToString();
                    break;
                case "Agent":
                    context = input["description"]?.ToString();
                    break;
                default:
                    context = input["description"]?.ToString()
                              ?? input["query"]?.ToString()
                              ?? input["file_path"]?.ToString()
                              ?? input["command"]?.ToString();
                    break;
            }

            return Truncate(context, MaxContextLength);
        }

        private static string? Truncate(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            text = text!.Replace("\n", " ").Replace("\r", "");
            if (text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength - 3) + "...";
        }
    }
}

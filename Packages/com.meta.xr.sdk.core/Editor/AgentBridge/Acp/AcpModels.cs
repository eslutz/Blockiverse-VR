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

using System.Collections.Generic;
using Meta.XR.Json;

namespace Meta.XR.AI.AgentBridge.Acp
{
    // -----------------------------------------------------------------------
    //  JSON-RPC 2.0 envelopes
    // -----------------------------------------------------------------------

    internal class JsonRpcRequest
    {
        [McpJsonProperty("jsonrpc")] public string Jsonrpc = "2.0";
        [McpJsonProperty("id")] public int Id;
        [McpJsonProperty("method")] public string Method = "";
        [McpJsonProperty("params", NullHandling = McpJsonNullHandling.Ignore)]
        public object? Params;
    }

    internal class JsonRpcResponse
    {
        [McpJsonProperty("jsonrpc")] public string Jsonrpc = "2.0";
        [McpJsonProperty("id")] public int? Id;
        [McpJsonProperty("result", NullHandling = McpJsonNullHandling.Ignore)]
        public JsonNode? Result;
        [McpJsonProperty("error", NullHandling = McpJsonNullHandling.Ignore)]
        public JsonRpcError? Error;
    }

    internal class JsonRpcNotification
    {
        [McpJsonProperty("jsonrpc")] public string Jsonrpc = "2.0";
        [McpJsonProperty("method")] public string Method = "";
        [McpJsonProperty("params", NullHandling = McpJsonNullHandling.Ignore)]
        public JsonNode? Params;
    }

    internal class JsonRpcError
    {
        [McpJsonProperty("code")] public int Code;
        [McpJsonProperty("message")] public string Message = "";
        [McpJsonProperty("data", NullHandling = McpJsonNullHandling.Ignore)]
        public JsonNode? Data;
    }

    // -----------------------------------------------------------------------
    //  Client -> Agent requests
    // -----------------------------------------------------------------------

    internal class InitializeParams
    {
        [McpJsonProperty("protocolVersion")] public int ProtocolVersion = 1;
        [McpJsonProperty("clientCapabilities", NullHandling = McpJsonNullHandling.Ignore)]
        public ClientCapabilities? ClientCapabilities;
        [McpJsonProperty("clientInfo", NullHandling = McpJsonNullHandling.Ignore)]
        public ClientInfo? ClientInfo;
    }

    internal class ClientCapabilities
    {
        [McpJsonProperty("streaming", NullHandling = McpJsonNullHandling.Ignore)]
        public bool? Streaming;
    }

    internal class ClientInfo
    {
        [McpJsonProperty("name")] public string Name = "";
        [McpJsonProperty("version")] public string Version = "";
    }

    internal class InitializeResult
    {
        [McpJsonProperty("protocolVersion")] public int ProtocolVersion;
        [McpJsonProperty("agentCapabilities", NullHandling = McpJsonNullHandling.Ignore)]
        public AgentCapabilities? AgentCapabilities;
        [McpJsonProperty("agentInfo", NullHandling = McpJsonNullHandling.Ignore)]
        public AgentInfo? AgentInfo;
    }

    internal class AgentCapabilities
    {
        [McpJsonProperty("streaming", NullHandling = McpJsonNullHandling.Ignore)]
        public bool? Streaming;
    }

    internal class AgentInfo
    {
        [McpJsonProperty("name")] public string Name = "";
        [McpJsonProperty("version")] public string Version = "";
    }

    internal class NewSessionParams
    {
        [McpJsonProperty("cwd")] public string Cwd = "";
        [McpJsonProperty("mcpServers")] public List<object> McpServers = new();
    }

    internal class NewSessionResult
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
    }

    internal class PromptParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("prompt")] public List<ContentBlock> Prompt = new();
    }

    internal class PromptResult
    {
        [McpJsonProperty("stopReason", NullHandling = McpJsonNullHandling.Ignore)]
        public string? StopReason;
    }

    internal class CancelNotificationParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
    }

    // -----------------------------------------------------------------------
    //  Content blocks
    // -----------------------------------------------------------------------

    internal class ContentBlock
    {
        [McpJsonProperty("type")] public string Type = "";
    }

    internal class TextContentBlock : ContentBlock
    {
        [McpJsonProperty("text")] public string Text = "";

        public TextContentBlock()
        {
            Type = "text";
        }
    }

    internal class ImageContentBlock : ContentBlock
    {
        [McpJsonProperty("data")] public string Data = "";
        [McpJsonProperty("mimeType")] public string MimeType = "";

        public ImageContentBlock()
        {
            Type = "image";
        }
    }

    // -----------------------------------------------------------------------
    //  Agent -> Client requests
    // -----------------------------------------------------------------------

    internal class ReadTextFileParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("path")] public string Path = "";
    }

    internal class ReadTextFileResult
    {
        [McpJsonProperty("content")] public string Content = "";
    }

    internal class WriteTextFileParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("path")] public string Path = "";
        [McpJsonProperty("content")] public string Content = "";
    }

    internal class RequestPermissionParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("toolCall", NullHandling = McpJsonNullHandling.Ignore)]
        public JsonNode? ToolCall;
        [McpJsonProperty("options")] public List<PermissionOption> Options = new();
    }

    internal class PermissionOption
    {
        [McpJsonProperty("id")] public string Id = "";
        [McpJsonProperty("label")] public string Label = "";
        [McpJsonProperty("description", NullHandling = McpJsonNullHandling.Ignore)]
        public string? Description;
    }

    internal class RequestPermissionResult
    {
        [McpJsonProperty("outcome")] public PermissionOutcome Outcome = new();
    }

    internal class PermissionOutcome
    {
        [McpJsonProperty("outcome")] public string OutcomeType = "selected";
        [McpJsonProperty("optionId")] public string OptionId = "";
    }

    internal class CreateTerminalParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("command")] public string Command = "";
        [McpJsonProperty("args", NullHandling = McpJsonNullHandling.Ignore)]
        public List<string>? Args;
        [McpJsonProperty("cwd", NullHandling = McpJsonNullHandling.Ignore)]
        public string? Cwd;
    }

    internal class CreateTerminalResult
    {
        [McpJsonProperty("terminalId")] public string TerminalId = "";
    }

    internal class TerminalOutputParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("terminalId")] public string TerminalId = "";
    }

    internal class TerminalOutputResult
    {
        [McpJsonProperty("output")] public string Output = "";
        [McpJsonProperty("truncated")] public bool Truncated;
        [McpJsonProperty("exitStatus", NullHandling = McpJsonNullHandling.Ignore)]
        public ExitStatus? ExitStatus;
    }

    internal class ExitStatus
    {
        [McpJsonProperty("type")] public string Type = "";
        [McpJsonProperty("code", NullHandling = McpJsonNullHandling.Ignore)]
        public int? Code;
    }

    internal class KillTerminalParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("terminalId")] public string TerminalId = "";
    }

    internal class ReleaseTerminalParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("terminalId")] public string TerminalId = "";
    }

    internal class WaitForTerminalExitParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("terminalId")] public string TerminalId = "";
        [McpJsonProperty("timeoutMs", NullHandling = McpJsonNullHandling.Ignore)]
        public int? TimeoutMs;
    }

    internal class WaitForTerminalExitResult
    {
        [McpJsonProperty("exitStatus", NullHandling = McpJsonNullHandling.Ignore)]
        public ExitStatus? ExitStatus;
        [McpJsonProperty("timedOut")] public bool TimedOut;
    }

    // -----------------------------------------------------------------------
    //  Session update notifications (agent -> client)
    // -----------------------------------------------------------------------

    internal class SessionUpdateParams
    {
        [McpJsonProperty("sessionId")] public string SessionId = "";
        [McpJsonProperty("update")] public JsonObject Update = new();

        /// <summary>
        /// Gets the session update type string from the update object.
        /// </summary>
        public string? UpdateType => Update["sessionUpdate"]?.ToString();
    }

    // -----------------------------------------------------------------------
    //  Typed update helpers (parsed from SessionUpdateParams.Update)
    // -----------------------------------------------------------------------

    internal class AgentMessageChunk
    {
        [McpJsonProperty("content")] public JsonNode? Content;
    }

    internal class AgentThoughtChunk
    {
        [McpJsonProperty("content")] public JsonNode? Content;
    }

    internal class ToolCallStart
    {
        [McpJsonProperty("toolCallId")] public string ToolCallId = "";
        [McpJsonProperty("title")] public string Title = "";
        [McpJsonProperty("kind", NullHandling = McpJsonNullHandling.Ignore)]
        public string? Kind;
        [McpJsonProperty("status", NullHandling = McpJsonNullHandling.Ignore)]
        public string? Status;
    }

    internal class ToolCallProgress
    {
        [McpJsonProperty("toolCallId")] public string ToolCallId = "";
        [McpJsonProperty("status", NullHandling = McpJsonNullHandling.Ignore)]
        public string? Status;
        [McpJsonProperty("content", NullHandling = McpJsonNullHandling.Ignore)]
        public JsonArray? Content;
    }

    internal class ToolCallComplete
    {
        [McpJsonProperty("toolCallId")] public string ToolCallId = "";
        [McpJsonProperty("status", NullHandling = McpJsonNullHandling.Ignore)]
        public string? Status;
        [McpJsonProperty("content", NullHandling = McpJsonNullHandling.Ignore)]
        public JsonArray? Content;
    }

    internal class UsageUpdate
    {
        [McpJsonProperty("inputTokens", NullHandling = McpJsonNullHandling.Ignore)]
        public int? InputTokens;
        [McpJsonProperty("outputTokens", NullHandling = McpJsonNullHandling.Ignore)]
        public int? OutputTokens;
    }

    internal class PlanUpdate
    {
        [McpJsonProperty("content")] public JsonNode? Content;
    }
}

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

using Meta.XR.Json;

namespace Meta.MCPBridge.Schemas
{
    internal interface ISchema
    {
    }

    internal class BaseSchema : ISchema
    {
        [McpJsonProperty("id")] internal string Id { get; set; }

        [McpJsonProperty("jsonrpc")] internal string JsonRPC => "2.0";
    }

    internal class InitializationSchema : ResponseSchema
    {
        [McpJsonProperty("serverInfo")]
        internal JsonObject ServerInfo = new()
        {
            ["name"] = "Unity MCP Server",
            ["version"] = "1.0.0"
        };

        [McpJsonProperty("protocolVersion")] internal string ProtocolVersion => "2025-11-25";

        [McpJsonProperty("capabilities")]
        internal JsonObject Capabilities => new()
        {
            ["tools"] = new JsonObject()
        };
    }
}

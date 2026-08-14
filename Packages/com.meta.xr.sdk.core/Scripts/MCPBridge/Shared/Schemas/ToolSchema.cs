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
using Meta.XR.Json;

namespace Meta.MCPBridge.Schemas
{
    internal class ToolSchema : ISchema
    {
        [McpJsonProperty("name")] internal string Name { get; set; }

        [McpJsonProperty("description")] internal string Description { get; set; }

        [McpJsonProperty("remarks", NullHandling = McpJsonNullHandling.Ignore)]
        internal string[] Remarks { get; set; }

        [McpJsonProperty("inputSchema")] internal ToolInputSchema InputSchema { get; set; }

        // Extension field: Per-method detailed schemas for AI discoverability
        [McpJsonProperty("x-methods", NullHandling = McpJsonNullHandling.Ignore)]
        internal Dictionary<string, MethodDetailSchema> Methods { get; set; }
    }

    /// <summary>
    /// Detailed per-method schema for AI discoverability.
    /// Provides structured information about each tool method including
    /// return types, remarks, and per-method parameter details.
    /// </summary>
    internal class MethodDetailSchema : ISchema
    {
        [McpJsonProperty("description", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Description { get; set; }

        [McpJsonProperty("returns", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Returns { get; set; }

        [McpJsonProperty("returnType", NullHandling = McpJsonNullHandling.Ignore)]
        internal string ReturnType { get; set; }

        [McpJsonProperty("remarks", NullHandling = McpJsonNullHandling.Ignore)]
        internal string[] Remarks { get; set; }

        [McpJsonProperty("parameters", NullHandling = McpJsonNullHandling.Ignore)]
        internal Dictionary<string, ParameterDetailSchema> Parameters { get; set; }
    }

    /// <summary>
    /// Detailed parameter information for method schemas.
    /// </summary>
    internal class ParameterDetailSchema : ISchema
    {
        [McpJsonProperty("type", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Type { get; set; }

        [McpJsonProperty("csharpType", NullHandling = McpJsonNullHandling.Ignore)]
        internal string CSharpType { get; set; }

        [McpJsonProperty("required", NullHandling = McpJsonNullHandling.Ignore)]
        internal bool? Required { get; set; }

        [McpJsonProperty("default", NullHandling = McpJsonNullHandling.Ignore)]
        internal object Default { get; set; }

        [McpJsonProperty("description", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Description { get; set; }
    }

    internal class ToolInputSchema : ISchema
    {
        [McpJsonProperty("type")] internal string Type { get; set; } = "object";

        [McpJsonProperty("properties")] internal Dictionary<string, ToolPropertySchema> Properties { get; set; }

        [McpJsonProperty("required")] internal string[] Required { get; set; }
    }

    internal class ToolPropertySchema : ISchema
    {
        [McpJsonProperty("type", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Type { get; set; }

        [McpJsonProperty("description", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Description { get; set; }

        [McpJsonProperty("enum", NullHandling = McpJsonNullHandling.Ignore)]
        internal string[] Enum { get; set; }

        [McpJsonProperty("oneOf", NullHandling = McpJsonNullHandling.Ignore)]
        internal EnumValueSchema[] OneOf { get; set; }

        // Extension fields for AI discoverability (x- prefix follows JSON Schema vendor extension convention)
        [McpJsonProperty("x-csharp-type", NullHandling = McpJsonNullHandling.Ignore)]
        internal string CSharpType { get; set; }

        [McpJsonProperty("x-remark", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Remark { get; set; }

        [McpJsonProperty("x-used-by", NullHandling = McpJsonNullHandling.Ignore)]
        internal string[] UsedBy { get; set; }
    }

    internal class EnumValueSchema : ISchema
    {
        [McpJsonProperty("const")]
        internal string Const { get; set; }

        [McpJsonProperty("description", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Description { get; set; }

        [McpJsonProperty("title", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Title { get; set; }
    }

    internal class MethodSchema : ISchema
    {
        [McpJsonProperty("description", NullHandling = McpJsonNullHandling.Ignore)]
        internal string Description { get; set; }

        [McpJsonProperty("x-return-type", NullHandling = McpJsonNullHandling.Ignore)]
        internal string ReturnType { get; set; }

        [McpJsonProperty("x-return-type-description", NullHandling = McpJsonNullHandling.Ignore)]
        internal string ReturnTypeDescription { get; set; }

        [McpJsonProperty("x-remarks", NullHandling = McpJsonNullHandling.Ignore)]
        internal string[] Remarks { get; set; }
    }

    internal class ToolListResultSchema : ResultSchema
    {
        [McpJsonProperty("tools")] internal IEnumerable<ToolSchema> Tools { get; set; }
    }

    internal class ToolCallResultSchema : ResultSchema
    {
        [McpJsonProperty("content")] internal IEnumerable<ToolCallDataSchema> Content { get; set; }
    }

    internal class ToolCallDataSchema : ISchema
    {
        [McpJsonProperty("type")] internal string Type => "text";
        [McpJsonProperty("text")] internal string Text { get; set; }
    }
}

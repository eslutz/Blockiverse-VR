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
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Meta.MCPBridge.Attributes;
using Meta.MCPBridge.Schemas;
using Meta.MCPBridge.Services;
using Meta.MCPBridge.Utils;
using Meta.XR.Json;

namespace Meta.MCPBridge.Definitions
{
    internal class Method : Definition<ToolAttribute, MethodSchema>
    {
        internal Method(Tool owner, MethodInfo method, ToolAttribute attribute) : base(attribute)
        {
            Owner = owner;
            MethodInfo = method;
        }

        internal string ReturnType => MethodInfo.ReturnType.GetJsonSchemaType();

        [McpJsonProperty("return type description", NullHandling = McpJsonNullHandling.Ignore)]
        internal string ReturnTypeDescription => Attribute.Returns;

        private Tool Owner { get; }
        internal MethodInfo MethodInfo { get; }

        internal override string Name
        {
            get => MethodInfo.Name;
            set => throw new NotImplementedException();
        }

        internal async Task<ToolCallResultSchema> Call(LocalExecutor executor, JsonObject arguments)
        {
            if (!Registry.TryGet(Owner.Type, out var service))
                throw new InvalidOperationException($"Could not find service {Owner.Name}");

            var argumentObjects = ArgumentConverter.ConvertJsonObjectToArguments(arguments, MethodInfo);

            object returnValue;

            try
            {
                returnValue = MethodInfo.Invoke(service, argumentObjects);

                if (returnValue is Task task)
                {
                    await task;

                    // If it's Task<T>, get the Result property
                    if (task.GetType().IsGenericType)
                    {
                        var resultProperty = task.GetType().GetProperty("Result");
                        returnValue = resultProperty?.GetValue(task);
                    }
                    else
                    {
                        returnValue = null;
                    }
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // Never reached
            }

            var responseData = new JsonObject
            {
                ["success"] = true
            };

            if (returnValue != null && MethodInfo.ReturnType != typeof(void))
                responseData["return value"] = ReturnValueConverter.ConvertReturnValueToJsonObjectSimple(returnValue);

            var result = new ToolCallResultSchema
            {
                Content = new[]
                {
                    new ToolCallDataSchema()
                    {
                        Text = responseData.ToString(JsonFormatting.None)
                    }
                }
            };
            return result;
        }

        internal override MethodSchema ToSchema()
        {
            var schema = new MethodSchema
            {
                Description = Attribute.Description,

                ReturnType = MethodInfo.ReturnType.GetJsonSchemaType(),
                ReturnTypeDescription = Attribute.Returns
            };


            // Add remarks if available
            if (Attribute.Remarks.Count > 0)
            {
                schema.Remarks = Attribute.Remarks.ToArray();
            }

            return schema;
        }
    }
}

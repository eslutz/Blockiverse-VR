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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Meta.XR.Json
{
    /// <summary>
    /// Attribute to specify the JSON property name for serialization.
    /// Replaces Newtonsoft.Json.JsonPropertyAttribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class McpJsonPropertyAttribute : Attribute
    {
        public string Name { get; }
        public McpJsonNullHandling NullHandling { get; set; } = McpJsonNullHandling.Include;

        public McpJsonPropertyAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Marks a dictionary property to receive unmapped JSON properties during deserialization.
    /// Replaces Newtonsoft.Json.JsonExtensionDataAttribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class McpJsonExtensionDataAttribute : Attribute
    {
    }

    /// <summary>
    /// Settings for JSON serialization.
    /// Replaces Newtonsoft.Json.JsonSerializerSettings.
    /// </summary>
    public class McpJsonSettings
    {
        public McpJsonNullHandling NullHandling { get; set; } = McpJsonNullHandling.Include;

        public static readonly McpJsonSettings Default = new();
    }

    /// <summary>
    /// Exception thrown during JSON operations.
    /// Replaces Newtonsoft.Json.JsonException.
    /// </summary>
    public class McpJsonException : Exception
    {
        public McpJsonException(string message) : base(message) { }
        public McpJsonException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// High-level JSON serialization/deserialization using reflection and McpJsonProperty attributes.
    /// Replaces Newtonsoft.Json.JsonConvert.
    /// </summary>
    public static class McpJsonConvert
    {
        private const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        #region Serialize

        public static string Serialize(object obj, McpJsonSettings settings = null)
        {
            var node = SerializeToNode(obj, settings);
            return node.ToJsonString();
        }

        public static JsonNode SerializeToNode(object obj, McpJsonSettings settings = null)
        {
            if (obj == null) return JsonValue.Null;
            settings ??= McpJsonSettings.Default;
            return SerializeObject(obj, settings);
        }

        private static JsonNode SerializeObject(object obj, McpJsonSettings settings)
        {
            if (obj == null) return JsonValue.Null;

            var type = obj.GetType();

            // Primitives
            if (IsPrimitive(type)) return PrimitiveToNode(obj);

            // JsonNode passthrough
            if (obj is JsonNode node) return node;

            // String
            if (obj is string s) return new JsonValue(s);

            // Enums
            if (type.IsEnum) return new JsonValue(obj.ToString());

            // Arrays and collections
            if (obj is IEnumerable enumerable && !(obj is string) && !(obj is IDictionary))
            {
                var arr = new JsonArray();
                foreach (var item in enumerable)
                    arr.Add(SerializeObject(item, settings));
                return arr;
            }

            // Dictionaries
            if (obj is IDictionary dict)
            {
                var jobj = new JsonObject();
                foreach (DictionaryEntry entry in dict)
                {
                    var key = entry.Key?.ToString() ?? "null";
                    jobj[key] = SerializeObject(entry.Value, settings);
                }
                return jobj;
            }

            // Complex objects with McpJsonProperty attributes
            return SerializeComplexObject(obj, type, settings);
        }

        private static JsonObject SerializeComplexObject(object obj, Type type, McpJsonSettings settings)
        {
            var result = new JsonObject();
            var members = GetAnnotatedMembers(type);
            bool hasAnnotations = false;
            MemberInfo extensionMember = null;

            foreach (var (member, attr, extAttr) in members)
            {
                if (extAttr != null)
                {
                    extensionMember = member;
                    continue;
                }

                if (attr != null)
                {
                    hasAnnotations = true;
                    var value = GetMemberValue(member, obj);

                    // Check null handling
                    bool skipNull = attr.NullHandling == McpJsonNullHandling.Ignore
                                    || settings.NullHandling == McpJsonNullHandling.Ignore;
                    if (value == null && skipNull)
                        continue;

                    result[attr.Name] = SerializeObject(value, settings);
                }
            }

            // If no annotations found, fall back to serializing all public properties and fields
            if (!hasAnnotations)
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    try
                    {
                        var value = prop.GetValue(obj);
                        bool skipNull = settings.NullHandling == McpJsonNullHandling.Ignore;
                        if (value == null && skipNull) continue;
                        result[prop.Name] = SerializeObject(value, settings);
                    }
                    catch { /* skip unreadable properties */ }
                }
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        var value = field.GetValue(obj);
                        bool skipNull = settings.NullHandling == McpJsonNullHandling.Ignore;
                        if (value == null && skipNull) continue;
                        result[field.Name] = SerializeObject(value, settings);
                    }
                    catch { /* skip unreadable fields */ }
                }
            }

            // Include extension data
            if (extensionMember != null)
            {
                var extData = GetMemberValue(extensionMember, obj);
                if (extData is IDictionary<string, JsonNode> nodeDict)
                {
                    foreach (var kvp in nodeDict)
                        result[kvp.Key] = kvp.Value;
                }
                else if (extData is IDictionary dict)
                {
                    foreach (DictionaryEntry entry in dict)
                    {
                        var key = entry.Key?.ToString();
                        if (key != null)
                            result[key] = SerializeObject(entry.Value, settings);
                    }
                }
            }

            return result;
        }

        #endregion

        #region Deserialize

        public static T Deserialize<T>(string json)
        {
            var node = JsonParser.Parse(json);
            return (T)ConvertNodeToType(node, typeof(T));
        }

        public static object ConvertNodeToType(JsonNode node, Type targetType)
        {
            if (node == null || node.Type == JsonNodeType.Null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    return Activator.CreateInstance(targetType);
                return null;
            }

            // Handle nullable
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
                targetType = underlyingType;

            // JsonNode target types
            if (targetType == typeof(JsonNode) || targetType == typeof(JsonObject) ||
                targetType == typeof(JsonArray) || targetType == typeof(JsonValue))
            {
                return node;
            }

            // Primitives from JsonValue
            if (node is JsonValue jv)
            {
                if (targetType == typeof(string)) return jv.ToString();
                if (targetType == typeof(int)) return jv.GetValue<int>();
                if (targetType == typeof(long)) return jv.GetValue<long>();
                if (targetType == typeof(double)) return jv.GetValue<double>();
                if (targetType == typeof(float)) return jv.GetValue<float>();
                if (targetType == typeof(decimal)) return jv.GetValue<decimal>();
                if (targetType == typeof(bool)) return jv.GetValue<bool>();
                if (targetType == typeof(DateTime)) return jv.GetValue<DateTime>();
                if (targetType == typeof(Guid))
                {
                    var str = jv.ToString();
                    return Guid.Parse(str);
                }
                if (targetType == typeof(object)) return jv.RawValue;
                if (targetType.IsEnum)
                {
                    if (jv.Type == JsonNodeType.String)
                        return Enum.Parse(targetType, jv.ToString(), true);
                    return Enum.ToObject(targetType, jv.GetValue<int>());
                }
            }

            // JsonObject → complex type
            if (node is JsonObject jsonObj)
            {
                // Dictionary target
                if (typeof(IDictionary).IsAssignableFrom(targetType) ||
                    (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
                {
                    return DeserializeDictionary(jsonObj, targetType);
                }

                return DeserializeComplexObject(jsonObj, targetType);
            }

            // JsonArray → array/list
            if (node is JsonArray jsonArr)
            {
                return DeserializeCollection(jsonArr, targetType);
            }

            throw new McpJsonException($"Cannot convert {node.Type} to {targetType.Name}");
        }

        private static object DeserializeComplexObject(JsonObject jsonObj, Type targetType)
        {
            object instance;
            try
            {
                instance = Activator.CreateInstance(targetType, true);
            }
            catch (Exception ex)
            {
                throw new McpJsonException($"Cannot create instance of {targetType.Name}: {ex.Message}", ex);
            }

            var members = GetAnnotatedMembers(targetType);
            var mappedKeys = new HashSet<string>(StringComparer.Ordinal);
            MemberInfo extensionMember = null;
            bool hasAnnotations = false;

            foreach (var (member, attr, extAttr) in members)
            {
                if (extAttr != null)
                {
                    extensionMember = member;
                    continue;
                }

                if (attr == null) continue;
                hasAnnotations = true;

                var jsonName = attr.Name;
                mappedKeys.Add(jsonName);

                if (!jsonObj.TryGetValue(jsonName, out var valueNode)) continue;

                var memberType = GetMemberType(member);
                if (memberType == null) continue;

                // Skip read-only properties
                if (member is PropertyInfo pi && !pi.CanWrite) continue;

                try
                {
                    var value = ConvertNodeToType(valueNode, memberType);
                    SetMemberValue(member, instance, value);
                }
                catch (Exception ex)
                {
                    throw new McpJsonException(
                        $"Error setting {member.Name} on {targetType.Name}: {ex.Message}", ex);
                }
            }

            // If no annotations found, fall back to public properties and fields by name
            // Uses case-insensitive matching to handle camelCase JSON ↔ PascalCase C# fields
            if (!hasAnnotations)
            {
                foreach (var prop in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
                    if (!jsonObj.TryGetValue(prop.Name, out var valueNode)
                        && !jsonObj.TryGetValue(prop.Name, StringComparison.OrdinalIgnoreCase, out valueNode)) continue;
                    try
                    {
                        var value = ConvertNodeToType(valueNode, prop.PropertyType);
                        prop.SetValue(instance, value);
                    }
                    catch { /* skip on error */ }
                }
                foreach (var field in targetType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!jsonObj.TryGetValue(field.Name, out var valueNode)
                        && !jsonObj.TryGetValue(field.Name, StringComparison.OrdinalIgnoreCase, out valueNode)) continue;
                    try
                    {
                        var value = ConvertNodeToType(valueNode, field.FieldType);
                        field.SetValue(instance, value);
                    }
                    catch { /* skip on error */ }
                }
            }

            // Handle extension data
            if (extensionMember != null)
            {
                var memberType = GetMemberType(extensionMember);
                if (memberType != null &&
                    (typeof(IDictionary).IsAssignableFrom(memberType) ||
                     (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(IDictionary<,>)) ||
                     typeof(IDictionary<string, JsonNode>).IsAssignableFrom(memberType)))
                {
                    var extDict = GetMemberValue(extensionMember, instance) as IDictionary<string, JsonNode>;
                    if (extDict == null)
                    {
                        extDict = new Dictionary<string, JsonNode>();
                        SetMemberValue(extensionMember, instance, extDict);
                    }

                    foreach (var prop in jsonObj.Properties())
                    {
                        if (!mappedKeys.Contains(prop.Name))
                        {
                            extDict[prop.Name] = prop.Value;
                        }
                    }
                }
            }

            return instance;
        }

        private static object DeserializeDictionary(JsonObject jsonObj, Type targetType)
        {
            Type valueType = typeof(object);
            if (targetType.IsGenericType)
            {
                var args = targetType.GetGenericArguments();
                if (args.Length == 2) valueType = args[1];
            }

            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
            var dict = (IDictionary)Activator.CreateInstance(dictType);

            foreach (var prop in jsonObj.Properties())
            {
                dict[prop.Name] = ConvertNodeToType(prop.Value, valueType);
            }

            return dict;
        }

        private static object DeserializeCollection(JsonArray jsonArr, Type targetType)
        {
            Type elementType;

            if (targetType.IsArray)
            {
                elementType = targetType.GetElementType();
                var array = Array.CreateInstance(elementType, jsonArr.Count);
                for (int i = 0; i < jsonArr.Count; i++)
                    array.SetValue(ConvertNodeToType(jsonArr[i], elementType), i);
                return array;
            }

            if (targetType.IsGenericType)
            {
                var genDef = targetType.GetGenericTypeDefinition();
                elementType = targetType.GetGenericArguments()[0];

                if (genDef == typeof(List<>) || genDef == typeof(IEnumerable<>) ||
                    genDef == typeof(IList<>) || genDef == typeof(ICollection<>))
                {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = (IList)Activator.CreateInstance(listType);
                    foreach (var item in jsonArr)
                        list.Add(ConvertNodeToType(item, elementType));
                    return list;
                }
            }

            // Fallback: try as object array
            var objArray = new object[jsonArr.Count];
            for (int i = 0; i < jsonArr.Count; i++)
                objArray[i] = ConvertNodeToType(jsonArr[i], typeof(object));
            return objArray;
        }

        #endregion

        #region ObjectToNode (JToken.FromObject equivalent)

        public static JsonNode ObjectToNode(object value)
        {
            if (value == null) return JsonValue.Null;
            if (value is JsonNode node) return node;

            var type = value.GetType();

            if (value is string s) return new JsonValue(s);
            if (IsPrimitive(type)) return PrimitiveToNode(value);
            if (type.IsEnum) return new JsonValue(value.ToString());
            if (value is DateTime dt) return new JsonValue(dt);
            if (value is Guid g) return new JsonValue(g.ToString());

            if (value is IDictionary dict)
            {
                var obj = new JsonObject();
                foreach (DictionaryEntry entry in dict)
                    obj[entry.Key?.ToString() ?? "null"] = ObjectToNode(entry.Value);
                return obj;
            }

            if (value is IEnumerable enumerable)
            {
                var arr = new JsonArray();
                foreach (var item in enumerable)
                    arr.Add(ObjectToNode(item));
                return arr;
            }

            // Complex object - serialize using attributes
            return SerializeToNode(value);
        }

        #endregion

        #region Helpers

        private static bool IsPrimitive(Type type)
        {
            return type == typeof(int) || type == typeof(long) || type == typeof(short) ||
                   type == typeof(byte) || type == typeof(double) || type == typeof(float) ||
                   type == typeof(decimal) || type == typeof(bool);
        }

        private static JsonNode PrimitiveToNode(object value)
        {
            if (value is int i) return new JsonValue(i);
            if (value is long l) return new JsonValue(l);
            if (value is double d) return new JsonValue(d);
            if (value is float f) return new JsonValue((double)f);
            if (value is decimal dec) return new JsonValue((double)dec);
            if (value is bool b) return new JsonValue(b);
            if (value is short sh) return new JsonValue((int)sh);
            if (value is byte by) return new JsonValue((int)by);
            return new JsonValue(value);
        }

        private static IEnumerable<(MemberInfo member, McpJsonPropertyAttribute attr, McpJsonExtensionDataAttribute extAttr)>
            GetAnnotatedMembers(Type type)
        {
            var result = new List<(MemberInfo, McpJsonPropertyAttribute, McpJsonExtensionDataAttribute)>();

            foreach (var prop in type.GetProperties(MemberFlags))
            {
                var jsonAttr = prop.GetCustomAttribute<McpJsonPropertyAttribute>();
                var extAttr = prop.GetCustomAttribute<McpJsonExtensionDataAttribute>();
                if (jsonAttr != null || extAttr != null)
                    result.Add((prop, jsonAttr, extAttr));
            }

            foreach (var field in type.GetFields(MemberFlags))
            {
                var jsonAttr = field.GetCustomAttribute<McpJsonPropertyAttribute>();
                var extAttr = field.GetCustomAttribute<McpJsonExtensionDataAttribute>();
                if (jsonAttr != null || extAttr != null)
                    result.Add((field, jsonAttr, extAttr));
            }

            // Walk base types
            var baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                foreach (var prop in baseType.GetProperties(MemberFlags | BindingFlags.DeclaredOnly))
                {
                    var jsonAttr = prop.GetCustomAttribute<McpJsonPropertyAttribute>();
                    var extAttr = prop.GetCustomAttribute<McpJsonExtensionDataAttribute>();
                    if (jsonAttr != null || extAttr != null)
                    {
                        // Don't add if already found in derived type
                        if (!result.Any(r => r.Item1.Name == prop.Name))
                            result.Add((prop, jsonAttr, extAttr));
                    }
                }
                foreach (var field in baseType.GetFields(MemberFlags | BindingFlags.DeclaredOnly))
                {
                    var jsonAttr = field.GetCustomAttribute<McpJsonPropertyAttribute>();
                    var extAttr = field.GetCustomAttribute<McpJsonExtensionDataAttribute>();
                    if (jsonAttr != null || extAttr != null)
                    {
                        if (!result.Any(r => r.Item1.Name == field.Name))
                            result.Add((field, jsonAttr, extAttr));
                    }
                }
                baseType = baseType.BaseType;
            }

            return result;
        }

        private static object GetMemberValue(MemberInfo member, object obj)
        {
            if (member is PropertyInfo prop) return prop.GetValue(obj);
            if (member is FieldInfo field) return field.GetValue(obj);
            return null;
        }

        private static void SetMemberValue(MemberInfo member, object obj, object value)
        {
            if (member is PropertyInfo prop && prop.CanWrite)
                prop.SetValue(obj, value);
            else if (member is FieldInfo field)
                field.SetValue(obj, value);
        }

        private static Type GetMemberType(MemberInfo member)
        {
            if (member is PropertyInfo prop) return prop.PropertyType;
            if (member is FieldInfo field) return field.FieldType;
            return null;
        }

        #endregion
    }
}

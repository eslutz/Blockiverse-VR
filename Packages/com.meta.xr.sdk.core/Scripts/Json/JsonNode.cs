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
using System.Globalization;

namespace Meta.XR.Json
{
    public enum JsonNodeType
    {
        Null,
        String,
        Integer,
        Float,
        Boolean,
        Object,
        Array
    }

    public enum JsonFormatting
    {
        None,
        Indented
    }

    public enum McpJsonNullHandling
    {
        Include,
        Ignore
    }

    /// <summary>
    /// Base class for all JSON nodes. Replaces Newtonsoft.Json.Linq.JToken.
    /// </summary>
    public abstract class JsonNode
    {
        public abstract JsonNodeType Type { get; }

        public virtual JsonNode this[string key]
        {
            get => null;
            set => throw new InvalidOperationException($"Cannot set string-keyed property on {GetType().Name}");
        }

        public virtual JsonNode this[int index]
        {
            get => throw new InvalidOperationException($"Cannot index {GetType().Name} with integer");
        }

        public T Value<T>()
        {
            if (this is JsonValue jv)
                return jv.GetValue<T>();
            throw new InvalidCastException($"Cannot get value of type {typeof(T).Name} from {Type}");
        }

        public virtual object ToObject(Type targetType)
        {
            return McpJsonConvert.ConvertNodeToType(this, targetType);
        }

        public T ToObject<T>()
        {
            return (T)ToObject(typeof(T));
        }

        public override string ToString()
        {
            return ToJsonString();
        }

        public string ToString(JsonFormatting formatting)
        {
            return ToJsonString(formatting == JsonFormatting.Indented);
        }

        public abstract string ToJsonString(bool indented = false, int depth = 0);

        public static JsonNode FromObject(object value)
        {
            return McpJsonConvert.ObjectToNode(value);
        }

        public static JsonNode Parse(string json)
        {
            return JsonParser.Parse(json);
        }

        // Implicit conversions from primitives
        public static implicit operator JsonNode(string value) =>
            value == null ? JsonValue.Null : new JsonValue(value);
        public static implicit operator JsonNode(int value) => new JsonValue(value);
        public static implicit operator JsonNode(long value) => new JsonValue(value);
        public static implicit operator JsonNode(double value) => new JsonValue(value);
        public static implicit operator JsonNode(float value) => new JsonValue((double)value);
        public static implicit operator JsonNode(bool value) => new JsonValue(value);
        public static implicit operator JsonNode(decimal value) => new JsonValue((double)value);
    }

    /// <summary>
    /// Represents a primitive JSON value (string, number, boolean, null).
    /// Replaces Newtonsoft.Json.Linq.JValue.
    /// </summary>
    public class JsonValue : JsonNode
    {
        public static readonly JsonValue Null = new JsonValue();

        private readonly object _value;

        private JsonValue() { _value = null; }
        public JsonValue(string value) { _value = value; }
        public JsonValue(int value) { _value = value; }
        public JsonValue(long value) { _value = value; }
        public JsonValue(double value) { _value = value; }
        public JsonValue(bool value) { _value = value; }
        public JsonValue(object value) { _value = value; }

        public override JsonNodeType Type
        {
            get
            {
                if (_value == null) return JsonNodeType.Null;
                if (_value is string) return JsonNodeType.String;
                if (_value is bool) return JsonNodeType.Boolean;
                if (_value is int || _value is long || _value is short || _value is byte) return JsonNodeType.Integer;
                if (_value is double || _value is float || _value is decimal) return JsonNodeType.Float;
                return JsonNodeType.String;
            }
        }

        public object RawValue => _value;

        public static JsonValue CreateNull() => Null;

        public T GetValue<T>()
        {
            if (_value == null)
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    throw new InvalidCastException($"Cannot convert null to {typeof(T).Name}");
                return default;
            }
            if (_value is T typed) return typed;
            try
            {
                var targetType = typeof(T);
                targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                // Handle numeric conversions
                if (targetType == typeof(int)) return (T)(object)Convert.ToInt32(_value, CultureInfo.InvariantCulture);
                if (targetType == typeof(long)) return (T)(object)Convert.ToInt64(_value, CultureInfo.InvariantCulture);
                if (targetType == typeof(double)) return (T)(object)Convert.ToDouble(_value, CultureInfo.InvariantCulture);
                if (targetType == typeof(float)) return (T)(object)Convert.ToSingle(_value, CultureInfo.InvariantCulture);
                if (targetType == typeof(decimal)) return (T)(object)Convert.ToDecimal(_value, CultureInfo.InvariantCulture);
                if (targetType == typeof(bool)) return (T)(object)Convert.ToBoolean(_value, CultureInfo.InvariantCulture);
                if (targetType == typeof(string)) return (T)(object)_value.ToString();
                if (targetType == typeof(DateTime))
                {
                    if (_value is string s)
                        return (T)(object)DateTime.Parse(s, CultureInfo.InvariantCulture);
                    return (T)(object)Convert.ToDateTime(_value, CultureInfo.InvariantCulture);
                }
                if (targetType == typeof(Guid))
                {
                    if (_value is string gs)
                        return (T)(object)Guid.Parse(gs);
                }

                return (T)Convert.ChangeType(_value, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new InvalidCastException($"Cannot convert {_value.GetType().Name} '{_value}' to {typeof(T).Name}", ex);
            }
        }

        public override string ToString()
        {
            return _value?.ToString() ?? "";
        }

        public override string ToJsonString(bool indented = false, int depth = 0)
        {
            if (_value == null) return "null";
            if (_value is string s) return JsonWriter.EscapeString(s);
            if (_value is bool b) return b ? "true" : "false";
            if (_value is int i) return i.ToString(CultureInfo.InvariantCulture);
            if (_value is long l) return l.ToString(CultureInfo.InvariantCulture);
            if (_value is double d) return FormatDouble(d);
            if (_value is float f) return FormatDouble(f);
            if (_value is decimal dec) return dec.ToString(CultureInfo.InvariantCulture);
            if (_value is DateTime dt) return JsonWriter.EscapeString(dt.ToString("o"));
            if (_value is Guid g) return JsonWriter.EscapeString(g.ToString());
            return JsonWriter.EscapeString(_value.ToString());
        }

        private static string FormatDouble(double value)
        {
            if (double.IsNaN(value)) return "\"NaN\"";
            if (double.IsPositiveInfinity(value)) return "\"Infinity\"";
            if (double.IsNegativeInfinity(value)) return "\"-Infinity\"";
            var s = value.ToString("R", CultureInfo.InvariantCulture);
            // Ensure the output looks like a floating-point number
            if (!s.Contains('.') && !s.Contains('E') && !s.Contains('e'))
                s += ".0";
            return s;
        }
    }

    /// <summary>
    /// Represents a key-value pair from a JsonObject.Properties() enumeration.
    /// </summary>
    public struct JsonObjectProperty
    {
        public string Name { get; }
        public JsonNode Value { get; }

        public JsonObjectProperty(string name, JsonNode value)
        {
            Name = name;
            Value = value;
        }
    }
}

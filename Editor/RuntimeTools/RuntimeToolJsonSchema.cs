using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace DotCraft.Editor.RuntimeTools
{
    internal static class RuntimeToolJsonSchema
    {
        private static readonly CamelCaseNamingStrategy CamelCaseNaming = new()
        {
            ProcessDictionaryKeys = false,
            OverrideSpecifiedNames = false
        };

        public static bool TryBuildInputSchema(
            MethodInfo method,
            out Dictionary<string, object> schema,
            out IReadOnlyList<RuntimeToolParameter> parameters,
            out string error)
        {
            schema = null;
            parameters = Array.Empty<RuntimeToolParameter>();
            error = string.Empty;

            var properties = new Dictionary<string, object>();
            var required = new List<string>();
            var parameterList = new List<RuntimeToolParameter>();

            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType.IsByRef || parameter.IsOut)
                {
                    error = $"Parameter '{parameter.Name}' uses ref/out, which is not supported.";
                    return false;
                }

                var jsonName = GetJsonName(parameter);
                if (string.IsNullOrWhiteSpace(jsonName))
                {
                    error = $"Parameter '{parameter.Name}' does not have a usable JSON name.";
                    return false;
                }

                if (properties.ContainsKey(jsonName))
                {
                    error = $"Parameter JSON name '{jsonName}' is declared more than once.";
                    return false;
                }

                if (!TryBuildSchemaForType(parameter.ParameterType, out var parameterSchema, out error))
                {
                    error = $"Parameter '{parameter.Name}' has unsupported type '{parameter.ParameterType.FullName}': {error}";
                    return false;
                }

                AddDescription(parameterSchema, parameter.GetCustomAttribute<DescriptionAttribute>()?.Description);
                ApplySchemaHint(parameterSchema, parameter);
                properties[jsonName] = parameterSchema;
                parameterList.Add(new RuntimeToolParameter(parameter, jsonName));

                if (!parameter.IsOptional && !parameter.HasDefaultValue)
                    required.Add(jsonName);
            }

            schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            if (required.Count > 0)
                schema["required"] = required;

            parameters = parameterList;
            return true;
        }

        public static bool IsTopLevelStringProperty(Dictionary<string, object> schema, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return false;

            if (!schema.TryGetValue("properties", out var propertiesValue)
                || propertiesValue is not Dictionary<string, object> properties
                || !properties.TryGetValue(propertyName, out var propertySchema)
                || propertySchema is not Dictionary<string, object> propertySchemaObject
                || !propertySchemaObject.TryGetValue("type", out var typeValue))
            {
                return false;
            }

            return string.Equals(typeValue as string, "string", StringComparison.Ordinal);
        }

        private static bool TryBuildSchemaForType(
            Type type,
            out Dictionary<string, object> schema,
            out string error,
            HashSet<Type> visiting = null)
        {
            schema = null;
            error = string.Empty;
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string)
                || type == typeof(char)
                || type == typeof(Guid)
                || type == typeof(Uri)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset))
            {
                schema = new Dictionary<string, object> { ["type"] = "string" };
                return true;
            }

            if (type == typeof(bool))
            {
                schema = new Dictionary<string, object> { ["type"] = "boolean" };
                return true;
            }

            if (IsIntegerType(type))
            {
                schema = new Dictionary<string, object> { ["type"] = "integer" };
                return true;
            }

            if (IsNumberType(type))
            {
                schema = new Dictionary<string, object> { ["type"] = "number" };
                return true;
            }

            if (type.IsEnum)
            {
                schema = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = Enum.GetNames(type)
                };
                return true;
            }

            if (typeof(JObject).IsAssignableFrom(type) || typeof(JToken).IsAssignableFrom(type) || type == typeof(object))
            {
                schema = new Dictionary<string, object> { ["type"] = "object" };
                return true;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                error = "UnityEngine.Object parameters are not supported; pass stable ids, paths, or names instead.";
                return false;
            }

            if (TryGetDictionaryValueType(type, out var dictionaryValueType))
            {
                if (!TryBuildSchemaForType(dictionaryValueType, out var valueSchema, out error, visiting))
                    return false;

                schema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = valueSchema
                };
                return true;
            }

            if (TryGetEnumerableElementType(type, out var elementType))
            {
                if (!TryBuildSchemaForType(elementType, out var itemSchema, out error, visiting))
                    return false;

                schema = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = itemSchema
                };
                return true;
            }

            if (type.IsPointer || typeof(Delegate).IsAssignableFrom(type))
            {
                error = "Pointer and delegate types are not supported.";
                return false;
            }

            if (type.Namespace != null
                && type.Namespace.StartsWith("System", StringComparison.Ordinal))
            {
                error = "Unsupported system type.";
                return false;
            }

            return TryBuildObjectSchema(type, out schema, out error, visiting);
        }

        private static bool TryBuildObjectSchema(
            Type type,
            out Dictionary<string, object> schema,
            out string error,
            HashSet<Type> visiting)
        {
            schema = null;
            error = string.Empty;
            visiting ??= new HashSet<Type>();

            if (!visiting.Add(type))
            {
                schema = new Dictionary<string, object> { ["type"] = "object" };
                return true;
            }

            try
            {
                var properties = new Dictionary<string, object>();
                var required = new List<string>();

                foreach (var property in type
                             .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !IsJsonIgnored(p)))
                {
                    var jsonName = GetJsonName(property);
                    if (string.IsNullOrWhiteSpace(jsonName) || properties.ContainsKey(jsonName))
                        continue;

                    if (!TryBuildSchemaForType(property.PropertyType, out var propertySchema, out error, visiting))
                        return false;

                    AddDescription(propertySchema, property.GetCustomAttribute<DescriptionAttribute>()?.Description);
                    ApplySchemaHint(propertySchema, property);
                    properties[jsonName] = propertySchema;

                    if (IsJsonRequired(property))
                        required.Add(jsonName);
                }

                foreach (var field in type
                             .GetFields(BindingFlags.Public | BindingFlags.Instance)
                             .Where(f => !IsJsonIgnored(f)))
                {
                    var jsonName = GetJsonName(field);
                    if (string.IsNullOrWhiteSpace(jsonName) || properties.ContainsKey(jsonName))
                        continue;

                    if (!TryBuildSchemaForType(field.FieldType, out var fieldSchema, out error, visiting))
                        return false;

                    AddDescription(fieldSchema, field.GetCustomAttribute<DescriptionAttribute>()?.Description);
                    ApplySchemaHint(fieldSchema, field);
                    properties[jsonName] = fieldSchema;

                    if (IsJsonRequired(field))
                        required.Add(jsonName);
                }

                schema = new Dictionary<string, object>
                {
                    ["type"] = "object"
                };

                if (properties.Count > 0)
                    schema["properties"] = properties;
                if (required.Count > 0)
                    schema["required"] = required;

                return true;
            }
            finally
            {
                visiting.Remove(type);
            }
        }

        private static bool IsIntegerType(Type type)
        {
            return type == typeof(byte)
                   || type == typeof(sbyte)
                   || type == typeof(short)
                   || type == typeof(ushort)
                   || type == typeof(int)
                   || type == typeof(uint)
                   || type == typeof(long)
                   || type == typeof(ulong);
        }

        private static bool IsNumberType(Type type)
        {
            return type == typeof(float)
                   || type == typeof(double)
                   || type == typeof(decimal);
        }

        private static bool TryGetEnumerableElementType(Type type, out Type elementType)
        {
            elementType = null;

            if (type == typeof(string))
                return false;

            if (type.IsArray && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            var enumerableType = GetGenericInterface(type, typeof(IEnumerable<>));
            if (enumerableType == null)
                return false;

            elementType = enumerableType.GetGenericArguments()[0];
            return true;
        }

        private static bool TryGetDictionaryValueType(Type type, out Type valueType)
        {
            valueType = null;
            var dictionaryType = GetGenericInterface(type, typeof(IDictionary<,>));
            if (dictionaryType == null)
                return false;

            var args = dictionaryType.GetGenericArguments();
            if (args[0] != typeof(string))
                return false;

            valueType = args[1];
            return true;
        }

        private static Type GetGenericInterface(Type type, Type genericDefinition)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition)
                return type;

            return type
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericDefinition);
        }

        private static string GetJsonName(ParameterInfo parameter)
        {
            var jsonName = parameter.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
            return !string.IsNullOrWhiteSpace(jsonName)
                ? jsonName
                : CamelCaseNaming.GetPropertyName(parameter.Name ?? string.Empty, false);
        }

        private static string GetJsonName(MemberInfo member)
        {
            var jsonName = member.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
            return !string.IsNullOrWhiteSpace(jsonName)
                ? jsonName
                : CamelCaseNaming.GetPropertyName(member.Name, false);
        }

        private static bool IsJsonIgnored(MemberInfo member) =>
            member.GetCustomAttribute<JsonIgnoreAttribute>() != null;

        private static bool IsJsonRequired(MemberInfo member)
        {
            var required = member.GetCustomAttribute<JsonPropertyAttribute>()?.Required ?? Required.Default;
            return required is Required.Always or Required.AllowNull;
        }

        private static void AddDescription(Dictionary<string, object> schema, string description)
        {
            if (!string.IsNullOrWhiteSpace(description))
                schema["description"] = description.Trim();
        }

        private static void ApplySchemaHint(Dictionary<string, object> schema, ICustomAttributeProvider provider)
        {
            var hint = provider.GetCustomAttributes(typeof(AgentToolSchemaHintAttribute), false)
                .OfType<AgentToolSchemaHintAttribute>()
                .FirstOrDefault();
            if (hint == null)
                return;

            if (hint.Minimum != int.MinValue)
                schema["minimum"] = hint.Minimum;

            if (hint.EnumValues != null && hint.EnumValues.Length > 0)
            {
                if (schema.TryGetValue("type", out var typeValue)
                    && string.Equals(typeValue as string, "array", StringComparison.Ordinal)
                    && schema.TryGetValue("items", out var itemsValue)
                    && itemsValue is Dictionary<string, object> itemsSchema)
                {
                    itemsSchema["enum"] = hint.EnumValues;
                }
                else
                {
                    schema["enum"] = hint.EnumValues;
                }
            }
        }
    }
}

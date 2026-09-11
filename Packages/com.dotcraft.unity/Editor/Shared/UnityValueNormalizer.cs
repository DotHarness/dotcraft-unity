using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#if DOTCRAFT_ATTACH
namespace DotCraft.Unity
#else
namespace DotCraft.Editor
#endif
{
    /// <summary>Projects results into bounded plain values using shared Unity type serialization.</summary>
    public static class UnityValueNormalizer
    {
        const int MaxNormalizedDepth = 4;
        const int MaxNormalizedItems = 32;
        static readonly UnityJsonConverter Converter = new UnityJsonConverter();
        public static object Normalize(object value)
        {
            return NormalizeValue(value, 0);
        }

        private static object NormalizeValue(object value, int depth)
        {
            if (value == null)
                return null;

            if (depth >= MaxNormalizedDepth)
                return value.ToString();

            if (value is JToken jsonToken) return NormalizeToken(jsonToken, depth);
            var type = value.GetType();
            if (value is string
                || value is bool
                || value is byte
                || value is sbyte
                || value is short
                || value is ushort
                || value is int
                || value is uint
                || value is long
                || value is ulong
                || value is float
                || value is double
                || value is decimal)
            {
                return value;
            }

            if (value is DateTime dateTime)
                return dateTime.ToString("O");

            if (value is DateTimeOffset dateTimeOffset)
                return dateTimeOffset.ToString("O");

            if (value is Guid guid)
                return guid.ToString("D");

            if (type.IsEnum)
                return value.ToString();

            if (Converter.CanConvert(type))
                return NormalizeToken(JToken.FromObject(value, JsonSerializer.Create(new JsonSerializerSettings { Converters = { Converter } })), depth);

            if (value is IDictionary dictionary)
            {
                var normalized = new Dictionary<string, object>();
                var count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count++ >= MaxNormalizedItems)
                        break;

                    normalized[entry.Key?.ToString() ?? string.Empty] = NormalizeValue(entry.Value, depth + 1);
                }

                return normalized;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var normalized = new List<object>();
                foreach (var item in enumerable)
                {
                    if (normalized.Count >= MaxNormalizedItems)
                        break;

                    normalized.Add(NormalizeValue(item, depth + 1));
                }

                return normalized;
            }

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).Take(MaxNormalizedItems);
            var result = new Dictionary<string, object>();
            foreach (var property in properties)
            {
                try { result[property.Name] = NormalizeValue(property.GetValue(value), depth + 1); }
                catch { result[property.Name] = "<unavailable>"; }
            }
            return result.Count == 0 ? (object)value.ToString() : result;
        }

        static object NormalizeToken(JToken token, int depth)
        {
            if (token is JValue value) return value.Value;
            if (token is JObject obj) return obj.Properties().Take(MaxNormalizedItems)
                .ToDictionary(p => p.Name, p => NormalizeValue(p.Value, depth + 1));
            if (token is JArray array) return array.Take(MaxNormalizedItems).Select(v => NormalizeValue(v, depth + 1)).ToList();
            return token.ToString();
        }
    }
}

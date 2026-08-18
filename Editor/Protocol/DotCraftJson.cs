using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace DotCraft.Editor.Protocol
{
    internal static class DotCraftJson
    {
        private static readonly DefaultContractResolver CamelCaseContractResolver = new()
        {
            NamingStrategy = new CamelCaseNamingStrategy
            {
                ProcessDictionaryKeys = false,
                OverrideSpecifiedNames = false
            }
        };

        public static readonly JsonSerializerSettings CompactSettings = CreateSettings(Formatting.None);
        public static readonly JsonSerializerSettings IndentedSettings = CreateSettings(Formatting.Indented);
        public static readonly JsonSerializer CompactSerializer = JsonSerializer.Create(CompactSettings);

        private static JsonSerializerSettings CreateSettings(Formatting formatting)
        {
            return new JsonSerializerSettings
            {
                ContractResolver = CamelCaseContractResolver,
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = formatting
            };
        }

        public static string Serialize(object value) =>
            JsonConvert.SerializeObject(value, CompactSettings);

        public static string SerializeIndented(object value) =>
            JsonConvert.SerializeObject(value, IndentedSettings);

        public static T Deserialize<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json, CompactSettings);

        public static T ToObject<T>(JToken token) =>
            token == null || token.Type == JTokenType.Null
                ? default
                : token.ToObject<T>(CompactSerializer);
    }
}

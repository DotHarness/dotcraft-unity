using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.Protocol
{
    #region JSON-RPC 2.0 Base Types

    /// <summary>
    /// Represents a JSON-RPC 2.0 request.
    /// </summary>
    public sealed class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public JToken Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; } = "";

        [JsonProperty("params")]
        public JToken Params { get; set; }

        [JsonIgnore]
        public bool IsNotification => Id == null || Id.Type == JTokenType.Null || Id.Type == JTokenType.Undefined;
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 response.
    /// </summary>
    public sealed class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public JToken Id { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public JsonRpcError Error { get; set; }
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 error.
    /// </summary>
    public sealed class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 notification (no id, no response expected).
    /// </summary>
    public sealed class JsonRpcNotification
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("method")]
        public string Method { get; set; } = "";

        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
        public object Params { get; set; }
    }

    #endregion
}

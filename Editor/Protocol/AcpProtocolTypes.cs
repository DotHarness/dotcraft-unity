using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.Protocol
{
    #region ACP Initialize

    /// <summary>
    /// Parameters for the initialize method.
    /// </summary>
    public sealed class InitializeParams
    {
        [JsonProperty("protocolVersion")]
        public int ProtocolVersion { get; set; }

        [JsonProperty("clientCapabilities")]
        public ClientCapabilities ClientCapabilities { get; set; }

        [JsonProperty("clientInfo")]
        public ClientInfo ClientInfo { get; set; }
    }

    public sealed class ClientInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public sealed class ClientCapabilities
    {
        [JsonProperty("fs")]
        [JsonConverter(typeof(BoolOrObjectConverter<FsCapabilities>))]
        public FsCapabilities Fs { get; set; }

        [JsonProperty("terminal")]
        [JsonConverter(typeof(BoolOrObjectConverter<TerminalCapabilities>))]
        public TerminalCapabilities Terminal { get; set; }

        /// <summary>
        /// Extension method prefixes supported by this client (e.g. ["_unity"]).
        /// The agent uses this list to decide which extension tool families to register.
        /// </summary>
        [JsonProperty("extensions", NullValueHandling = NullValueHandling.Ignore)]
        public string[] Extensions { get; set; }

        [JsonProperty("_meta", NullValueHandling = NullValueHandling.Ignore)]
        public ClientCapabilitiesMeta Meta { get; set; }
    }

    public sealed class ClientCapabilitiesMeta
    {
        [JsonProperty("dotcraft", NullValueHandling = NullValueHandling.Ignore)]
        public DotCraftClientCapabilities DotCraft { get; set; }
    }

    public sealed class DotCraftClientCapabilities
    {
        /// <summary>
        /// Runtime tools implemented by this ACP client and exposed through DotCraft dynamic tools.
        /// </summary>
        [JsonProperty("runtimeTools", NullValueHandling = NullValueHandling.Ignore)]
        public List<AcpRuntimeToolDescriptor> RuntimeTools { get; set; }
    }

    public sealed class AcpRuntimeToolDescriptor
    {
        [JsonProperty("namespace", NullValueHandling = NullValueHandling.Ignore)]
        public string Namespace { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("inputSchema", NullValueHandling = NullValueHandling.Ignore)]
        public object InputSchema { get; set; }

        [JsonProperty("acpMethod")]
        public string AcpMethod { get; set; } = "";

        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)]
        public string Kind { get; set; }

        [JsonProperty("deferLoading", NullValueHandling = NullValueHandling.Ignore)]
        public bool? DeferLoading { get; set; }

        [JsonProperty("approval", NullValueHandling = NullValueHandling.Ignore)]
        public AcpRuntimeToolApprovalDescriptor Approval { get; set; }
    }

    public sealed class AcpRuntimeToolApprovalDescriptor
    {
        [JsonProperty("kind")]
        public string Kind { get; set; } = "";

        [JsonProperty("targetArgument")]
        public string TargetArgument { get; set; } = "";

        [JsonProperty("operation", NullValueHandling = NullValueHandling.Ignore)]
        public string Operation { get; set; }

        [JsonProperty("operationArgument", NullValueHandling = NullValueHandling.Ignore)]
        public string OperationArgument { get; set; }
    }

    public sealed class FsCapabilities
    {
        [JsonProperty("readTextFile")]
        public bool ReadTextFile { get; set; }

        [JsonProperty("writeTextFile")]
        public bool WriteTextFile { get; set; }

        public static FsCapabilities All => new() { ReadTextFile = true, WriteTextFile = true };
    }

    public sealed class TerminalCapabilities
    {
        [JsonProperty("create")]
        public bool Create { get; set; }

        public static TerminalCapabilities All => new() { Create = true };
    }

    /// <summary>
    /// Handles ACP capability fields that may be either a boolean shorthand or an object.
    /// </summary>
    public sealed class BoolOrObjectConverter<T> : JsonConverter<T> where T : class
    {
        public override T ReadJson(
            JsonReader reader,
            System.Type objectType,
            T existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Boolean)
            {
                if (reader.Value is bool enabled && !enabled)
                    return null;

                var allProp = objectType.GetProperty("All",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return allProp?.GetValue(null) as T
                       ?? System.Activator.CreateInstance<T>();
            }

            if (reader.TokenType == JsonToken.Null || reader.TokenType == JsonToken.Undefined)
                return null;

            return JToken.Load(reader).ToObject<T>(serializer);
        }

        public override void WriteJson(JsonWriter writer, T value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }

    public sealed class InitializeResult
    {
        [JsonProperty("protocolVersion")]
        public int ProtocolVersion { get; set; }

        [JsonProperty("agentCapabilities")]
        public AgentCapabilities AgentCapabilities { get; set; } = new();

        [JsonProperty("agentInfo")]
        public AgentInfo AgentInfo { get; set; } = new();

        [JsonProperty("authMethods", NullValueHandling = NullValueHandling.Ignore)]
        public AuthMethod[] AuthMethods { get; set; }
    }

    #endregion

    #region ACP Authentication

    public sealed class AuthMethod
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }
    }

    public sealed class AuthenticateParams
    {
        [JsonProperty("methodId")]
        public string MethodId { get; set; } = "";
    }

    public sealed class AuthenticateResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }
    }

    public sealed class AgentCapabilities
    {
        [JsonProperty("loadSession")]
        public bool LoadSession { get; set; }

        [JsonProperty("listSessions")]
        public bool ListSessions { get; set; }

        [JsonProperty("promptCapabilities")]
        public PromptCapabilities PromptCapabilities { get; set; }

        [JsonProperty("_meta", NullValueHandling = NullValueHandling.Ignore)]
        public AgentCapabilitiesMeta Meta { get; set; }
    }

    public sealed class AgentCapabilitiesMeta
    {
        [JsonProperty("dotcraft", NullValueHandling = NullValueHandling.Ignore)]
        public DotCraftAgentCapabilities DotCraft { get; set; }
    }

    public sealed class DotCraftAgentCapabilities
    {
        [JsonProperty("sessionDelete")]
        public bool SessionDelete { get; set; }
    }

    public sealed class PromptCapabilities
    {
        [JsonProperty("text")]
        public bool Text { get; set; } = true;

        [JsonProperty("image")]
        public bool Image { get; set; }

        [JsonProperty("audio")]
        public bool Audio { get; set; }

        [JsonProperty("embeddedContext")]
        public bool EmbeddedContext { get; set; }
    }

    public sealed class AgentInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    #endregion

    #region ACP Session/New

    /// <summary>ACP spec: env entry for MCP server (array of name/value).</summary>
    public sealed class AcpEnvVariable
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("value")]
        public string Value { get; set; } = "";
    }

    /// <summary>ACP spec: HTTP header for MCP server (array of name/value).</summary>
    public sealed class AcpHttpHeader
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("value")]
        public string Value { get; set; } = "";
    }

    public sealed class SessionNewParams
    {
        [JsonProperty("cwd")]
        public string Cwd { get; set; }

        [JsonProperty("mcpServers")]
        public List<AcpMcpServer> McpServers { get; set; }
    }

    public sealed class AcpMcpServer
    {
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("command", NullValueHandling = NullValueHandling.Ignore)]
        public string Command { get; set; }

        [JsonProperty("args", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Args { get; set; }

        [JsonProperty("env", NullValueHandling = NullValueHandling.Ignore)]
        public List<AcpEnvVariable> Env { get; set; }

        [JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
        public string Url { get; set; }

        [JsonProperty("headers", NullValueHandling = NullValueHandling.Ignore)]
        public List<AcpHttpHeader> Headers { get; set; }
    }

    public sealed class SessionNewResult
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonProperty("configOptions", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConfigOption> ConfigOptions { get; set; }
    }

    #endregion

    #region ACP Session/Load

    public sealed class SessionLoadParams
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonProperty("cwd")]
        public string Cwd { get; set; }

        [JsonProperty("mcpServers")]
        public List<AcpMcpServer> McpServers { get; set; }
    }

    public sealed class SessionLoadResult
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonProperty("configOptions", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConfigOption> ConfigOptions { get; set; }
    }

    #endregion

    #region ACP Session/List

    public sealed class SessionListParams
    {
        [JsonProperty("cwd")]
        public string Cwd { get; set; }

        [JsonProperty("cursor")]
        public string Cursor { get; set; }
    }

    public sealed class SessionListResult
    {
        [JsonProperty("sessions")]
        public List<SessionListEntry> Sessions { get; set; } = new();

        [JsonProperty("nextCursor", NullValueHandling = NullValueHandling.Ignore)]
        public string NextCursor { get; set; }
    }

    public sealed class SessionListEntry
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("updatedAt")]
        public string UpdatedAt { get; set; }

        [JsonProperty("cwd")]
        public string Cwd { get; set; }
    }

    #endregion

    #region DotCraft Session Delete

    public sealed class SessionDeleteParams
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";
    }

    public sealed class SessionDeleteResult
    {
    }

    #endregion

    #region ACP Session/Prompt

    public sealed class SessionPromptParams
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonProperty("prompt")]
        public List<AcpContentBlock> Prompt { get; set; } = new();

        [JsonProperty("command", NullValueHandling = NullValueHandling.Ignore)]
        public string Command { get; set; }
    }

    public sealed class AcpContentBlock
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }

        [JsonProperty("resource", NullValueHandling = NullValueHandling.Ignore)]
        public AcpEmbeddedResource Resource { get; set; }
    }

    public sealed class AcpEmbeddedResource
    {
        [JsonProperty("uri")]
        public string Uri { get; set; } = "";

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }
    }

    public sealed class SessionPromptResult
    {
        [JsonProperty("stopReason")]
        public string StopReason { get; set; } = "end_turn";
    }

    #endregion

    #region ACP Session/Update (Notification)

    public sealed class SessionUpdateParams
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonProperty("update")]
        public AcpSessionUpdate Update { get; set; } = new();
    }

    public sealed class AcpSessionUpdate
    {
        [JsonProperty("sessionUpdate")]
        public string SessionUpdate { get; set; } = "";

        [JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
        public object Content { get; set; }

        [JsonProperty("toolCallId", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolCallId { get; set; }

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)]
        public string Kind { get; set; }

        [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
        public string Status { get; set; }

        [JsonProperty("fileLocations", NullValueHandling = NullValueHandling.Ignore)]
        public List<AcpFileLocation> FileLocations { get; set; }

        [JsonProperty("entries", NullValueHandling = NullValueHandling.Ignore)]
        public List<AcpPlanEntry> Entries { get; set; }

        [JsonProperty("commands", NullValueHandling = NullValueHandling.Ignore)]
        public List<AcpSlashCommand> Commands { get; set; }

        [JsonProperty("configOptions", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConfigOption> ConfigOptions { get; set; }
    }

    public sealed class AcpFileLocation
    {
        [JsonProperty("uri")]
        public string Uri { get; set; } = "";
    }

    public sealed class AcpPlanEntry
    {
        [JsonProperty("content")]
        public string Content { get; set; } = "";

        [JsonProperty("priority")]
        public string Priority { get; set; } = AcpPlanEntryPriority.Medium;

        [JsonProperty("status")]
        public string Status { get; set; } = AcpToolStatus.Pending;
    }

    #endregion

    #region ACP Session/SetConfigOption

    public sealed class SessionSetConfigOptionParams
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonProperty("configId")]
        public string ConfigId { get; set; } = "";

        [JsonProperty("value")]
        public string Value { get; set; } = "";
    }

    public sealed class SessionSetConfigOptionResult
    {
        [JsonProperty("configOptions")]
        public List<ConfigOption> ConfigOptions { get; set; } = new();
    }

    #endregion

    #region ACP Session/Cancel (Notification)

    public sealed class SessionCancelParams
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";
    }

    #endregion

    #region ACP RequestPermission (Agent → Client)

    public sealed class RequestPermissionParams
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonProperty("toolCall")]
        public AcpToolCallInfo ToolCall { get; set; } = new();

        [JsonProperty("options")]
        public List<PermissionOption> Options { get; set; } = new();
    }

    public sealed class AcpToolCallInfo
    {
        [JsonProperty("toolCallId")]
        public string ToolCallId { get; set; } = "";

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)]
        public string Kind { get; set; }

        [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
        public string Status { get; set; }
    }

    public sealed class PermissionOption
    {
        [JsonProperty("optionId")]
        public string OptionId { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("kind")]
        public string Kind { get; set; } = "";
    }

    public sealed class RequestPermissionResult
    {
        [JsonProperty("outcome")]
        public PermissionOutcome Outcome { get; set; } = new();
    }

    public sealed class PermissionOutcome
    {
        [JsonProperty("outcome")]
        public string Outcome { get; set; } = "";

        [JsonProperty("optionId", NullValueHandling = NullValueHandling.Ignore)]
        public string OptionId { get; set; }
    }

    #endregion

    #region ACP fs/readTextFile (Agent → Client)

    public sealed class FsReadTextFileParams
    {
        [JsonProperty("path")]
        public string Path { get; set; } = "";

        [JsonProperty("offset", NullValueHandling = NullValueHandling.Ignore)]
        public int? Offset { get; set; }

        [JsonProperty("limit", NullValueHandling = NullValueHandling.Ignore)]
        public int? Limit { get; set; }
    }

    public sealed class FsReadTextFileResult
    {
        [JsonProperty("content")]
        public string Content { get; set; } = "";
    }

    #endregion

    #region ACP fs/writeTextFile (Agent → Client)

    public sealed class FsWriteTextFileParams
    {
        [JsonProperty("path")]
        public string Path { get; set; } = "";

        [JsonProperty("content")]
        public string Content { get; set; } = "";
    }

    public sealed class FsWriteTextFileResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }
    }

    #endregion

    #region ACP terminal/* (Agent → Client)

    public sealed class TerminalCreateParams
    {
        [JsonProperty("command")]
        public string Command { get; set; } = "";

        [JsonProperty("cwd", NullValueHandling = NullValueHandling.Ignore)]
        public string Cwd { get; set; }

        [JsonProperty("env", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Env { get; set; }
    }

    public sealed class TerminalCreateResult
    {
        [JsonProperty("terminalId")]
        public string TerminalId { get; set; } = "";
    }

    public sealed class TerminalGetOutputParams
    {
        [JsonProperty("terminalId")]
        public string TerminalId { get; set; } = "";
    }

    public sealed class TerminalGetOutputResult
    {
        [JsonProperty("output")]
        public string Output { get; set; } = "";

        [JsonProperty("exitCode", NullValueHandling = NullValueHandling.Ignore)]
        public int? ExitCode { get; set; }
    }

    public sealed class TerminalWaitForExitParams
    {
        [JsonProperty("terminalId")]
        public string TerminalId { get; set; } = "";

        [JsonProperty("timeout", NullValueHandling = NullValueHandling.Ignore)]
        public int? Timeout { get; set; }
    }

    public sealed class TerminalKillParams
    {
        [JsonProperty("terminalId")]
        public string TerminalId { get; set; } = "";
    }

    public sealed class TerminalReleaseParams
    {
        [JsonProperty("terminalId")]
        public string TerminalId { get; set; } = "";
    }

    #endregion

    #region ACP Slash Commands

    public sealed class AcpSlashCommand
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("inputHint", NullValueHandling = NullValueHandling.Ignore)]
        public string InputHint { get; set; }
    }

    #endregion

    #region ACP Config Options

    public sealed class ConfigOption
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)]
        public string Category { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = "select";

        [JsonProperty("currentValue")]
        public string CurrentValue { get; set; } = "";

        [JsonProperty("options")]
        public List<ConfigOptionValue> Options { get; set; } = new();
    }

    public sealed class ConfigOptionValue
    {
        [JsonProperty("value")]
        public string Value { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }
    }

    #endregion

    #region ACP Constants

    public static class AcpMethods
    {
        // Client → Agent
        public const string Initialize = "initialize";
        public const string Authenticate = "authenticate";
        public const string SessionNew = "session/new";
        public const string SessionLoad = "session/load";
        public const string SessionList = "session/list";
        public const string DotCraftSessionDelete = "_dotcraft/session_delete";
        public const string SessionPrompt = "session/prompt";
        public const string SessionCancel = "session/cancel";
        public const string SessionSetConfigOption = "session/set_config_option";

        // Agent → Client
        public const string SessionUpdate = "session/update";
        public const string RequestPermission = "session/request_permission";
        public const string FsReadTextFile = "fs/readTextFile";
        public const string FsWriteTextFile = "fs/writeTextFile";
        public const string TerminalCreate = "terminal/create";
        public const string TerminalGetOutput = "terminal/getOutput";
        public const string TerminalWaitForExit = "terminal/waitForExit";
        public const string TerminalKill = "terminal/kill";
        public const string TerminalRelease = "terminal/release";
    }

    public static class AcpToolKind
    {
        public const string Read = "read";
        public const string Edit = "edit";
        public const string Delete = "delete";
        public const string Move = "move";
        public const string Search = "search";
        public const string Execute = "execute";
        public const string Think = "think";
        public const string Fetch = "fetch";
        public const string Unity = "unity";
        public const string Other = "other";
    }

    public static class AcpToolStatus
    {
        public const string Pending = "pending";
        public const string InProgress = "in_progress";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public static class AcpPlanEntryPriority
    {
        public const string High = "high";
        public const string Medium = "medium";
        public const string Low = "low";
    }

    public static class AcpPermissionKind
    {
        public const string AllowOnce = "allow_once";
        public const string AllowAlways = "allow_always";
        public const string RejectOnce = "reject_once";
    }

    public static class AcpStopReason
    {
        public const string EndTurn = "end_turn";
        public const string ToolUse = "tool_use";
        public const string MaxTokens = "max_tokens";
        public const string Cancelled = "cancelled";
    }

    public static class AcpUpdateKind
    {
        public const string AgentMessageChunk = "agent_message_chunk";
        public const string UserMessageChunk = "user_message_chunk";
        public const string AgentThoughtChunk = "agent_thought_chunk";
        public const string ToolCall = "tool_call";
        public const string ToolCallUpdate = "tool_call_update";
        public const string Plan = "plan";
        public const string ConfigOptionsUpdate = "config_option_update";
        public const string LegacyConfigOptionsUpdate = "config_options_update";
        public const string AvailableCommandsUpdate = "available_commands_update";
        public const string CurrentModeUpdate = "current_mode_update";
    }

    #endregion
}

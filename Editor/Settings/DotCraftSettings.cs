using System;
using System.Collections.Generic;
using System.IO;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DotCraft.Editor.Settings
{
    /// <summary>
    /// A single MCP server entry stored in DotCraft settings.
    /// Supports both stdio and http transports.
    /// </summary>
    [Serializable]
    public sealed class McpServerEntry
    {
        /// <summary>Display name for the server (used as the MCP server name in the ACP protocol).</summary>
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        /// <summary>Whether this server is included when creating or loading sessions.</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>Transport type: "stdio" or "http".</summary>
        [JsonProperty("transport")]
        public string Transport { get; set; } = "stdio";

        // stdio-specific fields

        /// <summary>Executable command (stdio transport only).</summary>
        [JsonProperty("command", NullValueHandling = NullValueHandling.Ignore)]
        public string Command { get; set; }

        /// <summary>Command-line arguments (stdio transport only).</summary>
        [JsonProperty("arguments", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Arguments { get; set; }

        /// <summary>Environment variables to inject into the server process (stdio transport only).</summary>
        [JsonProperty("environmentVariables", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> EnvironmentVariables { get; set; }

        // http-specific fields

        /// <summary>Server URL (http transport only).</summary>
        [JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
        public string Url { get; set; }

        /// <summary>HTTP headers (http transport only).</summary>
        [JsonProperty("headers", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Headers { get; set; }
    }

    /// <summary>
    /// Configuration settings for DotCraft Unity Client.
    /// Stored in UserSettings/DotCraftSettings.json (per-user, not in version control).
    /// </summary>
    [Serializable]
    public sealed class DotCraftSettings
    {
        public const string AgentConnectionDotCraft = "dotcraft";
        public const string AgentConnectionCustomAcp = "customAcp";
        public const string DotCraftAppServerLocalHub = "localHub";
        public const string DotCraftAppServerRemote = "remote";

        private static DotCraftSettings _instance;
        private static readonly string SettingsPath = "UserSettings/DotCraftSettings.json";

        public static DotCraftSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = LoadOrCreate();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Connection profile used by the editor client.
        /// "dotcraft" uses DotCraft-specific Hub discovery before starting the ACP bridge.
        /// "customAcp" preserves the raw command/arguments startup path for other ACP agents.
        /// </summary>
        [JsonProperty("agentConnection")]
        public string AgentConnection { get; set; } = AgentConnectionDotCraft;

        /// <summary>
        /// DotCraft AppServer discovery mode when AgentConnection is "dotcraft".
        /// </summary>
        [JsonProperty("dotCraftAppServer")]
        public string DotCraftAppServer { get; set; } = DotCraftAppServerLocalHub;

        /// <summary>
        /// Command to execute DotCraft (e.g., "dotnet" or full path to executable).
        /// </summary>
        [JsonProperty("dotCraftCommand")]
        public string DotCraftCommand { get; set; } = "dotcraft";

        /// <summary>
        /// Arguments passed to DotCraft command.
        /// Example: "run --project /path/to/DotCraft -- --acp"
        /// </summary>
        [JsonProperty("dotCraftArguments")]
        public string DotCraftArguments { get; set; } = "-acp";

        /// <summary>
        /// Remote AppServer WebSocket URL used by DotCraft remote mode.
        /// </summary>
        [JsonProperty("remoteAppServerUrl")]
        public string RemoteAppServerUrl { get; set; } = "";

        /// <summary>
        /// Optional token used by DotCraft remote AppServer mode.
        /// </summary>
        [JsonProperty("remoteAppServerToken")]
        public string RemoteAppServerToken { get; set; } = "";

        /// <summary>
        /// Working directory for DotCraft process. Defaults to Unity project root.
        /// </summary>
        [JsonProperty("workspacePath")]
        public string WorkspacePath { get; set; } = "";

        /// <summary>
        /// Environment variables to inject into DotCraft process.
        /// Use for API keys and other configuration.
        /// </summary>
        [JsonProperty("environmentVariables")]
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

        /// <summary>
        /// Automatically reconnect after Domain Reload.
        /// </summary>
        [JsonProperty("autoReconnect")]
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// Enable verbose logging for debugging.
        /// </summary>
        [JsonProperty("verboseLogging")]
        public bool VerboseLogging { get; set; }

        /// <summary>
        /// Show agent reasoning text in the chat UI.
        /// When disabled, the chat still shows lightweight thinking status rows.
        /// </summary>
        [JsonProperty("showThinkingContent")]
        public bool ShowThinkingContent { get; set; }

        /// <summary>
        /// Timeout in seconds for ACP requests.
        /// </summary>
        [JsonProperty("requestTimeoutSeconds")]
        public int RequestTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum number of messages to keep in chat history.
        /// </summary>
        [JsonProperty("maxHistoryMessages")]
        public int MaxHistoryMessages { get; set; } = 1000;

        /// <summary>
        /// Declare built-in Unity runtime tools and enable their _unity/* handlers.
        /// Disable if using external Unity integration.
        /// </summary>
        [JsonProperty("enableBuiltinUnityTools")]
        public bool EnableBuiltinUnityTools { get; set; } = true;

        /// <summary>
        /// Starts the local localhost handoff server used by DotCraft App Binding.
        /// </summary>
        [JsonProperty("enableAppBindingLocalServer")]
        public bool EnableAppBindingLocalServer { get; set; } = true;

        /// <summary>
        /// Per-tool enablement for attribute-discovered DotCraft runtime tools.
        /// Unknown tools default to disabled to keep model tool surface and token use explicit.
        /// </summary>
        [JsonProperty("dynamicToolEnabledById")]
        public Dictionary<string, bool> DynamicToolEnabledById { get; set; } = new();

        /// <summary>
        /// MCP servers to inject into every new DotCraft session via the ACP mcpServers field.
        /// These supplement any servers already configured in .craft/config.json.
        /// </summary>
        [JsonProperty("mcpServers")]
        public List<McpServerEntry> McpServers { get; set; } = new();

        /// <summary>
        /// Gets the effective workspace path (falls back to project root).
        /// </summary>
        [JsonIgnore]
        public string EffectiveWorkspacePath =>
            string.IsNullOrEmpty(WorkspacePath)
                ? Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath
                : WorkspacePath;

        /// <summary>
        /// Loads settings from file, or creates default settings if file doesn't exist.
        /// </summary>
        public static DotCraftSettings LoadOrCreate()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    var json = File.ReadAllText(SettingsPath);
                    return FromJson(json);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DotCraft] Failed to load settings: {ex.Message}. Using defaults.");
                    return new DotCraftSettings();
                }
            }
            return new DotCraftSettings();
        }

        internal static DotCraftSettings FromJson(string json)
        {
            var hasAgentConnection = HasJsonProperty(json, "agentConnection");
            var settings = DotCraftJson.Deserialize<DotCraftSettings>(json) ?? new DotCraftSettings();
            settings.NormalizeAfterLoad(hasAgentConnection);
            return settings;
        }

        internal string ToJson() => DotCraftJson.SerializeIndented(this);

        private static bool HasJsonProperty(string json, string propertyName)
        {
            try
            {
                return JObject.Parse(json).Property(propertyName) != null;
            }
            catch
            {
                return false;
            }
        }

        private void NormalizeAfterLoad(bool hasAgentConnection)
        {
            DotCraftCommand = string.IsNullOrWhiteSpace(DotCraftCommand) ? "dotcraft" : DotCraftCommand.Trim();
            DotCraftArguments ??= "";
            WorkspacePath ??= "";
            RemoteAppServerUrl ??= "";
            RemoteAppServerToken ??= "";
            EnvironmentVariables ??= new Dictionary<string, string>();
            DynamicToolEnabledById ??= new Dictionary<string, bool>();
            McpServers ??= new List<McpServerEntry>();

            if (!hasAgentConnection)
            {
                AgentConnection = IsLegacyDefaultDotCraftStartup()
                    ? AgentConnectionDotCraft
                    : AgentConnectionCustomAcp;
                DotCraftAppServer = DotCraftAppServerLocalHub;
            }

            if (AgentConnection != AgentConnectionDotCraft && AgentConnection != AgentConnectionCustomAcp)
                AgentConnection = AgentConnectionDotCraft;

            if (DotCraftAppServer != DotCraftAppServerLocalHub && DotCraftAppServer != DotCraftAppServerRemote)
                DotCraftAppServer = DotCraftAppServerLocalHub;
        }

        private bool IsLegacyDefaultDotCraftStartup()
        {
            return string.Equals(DotCraftCommand?.Trim(), "dotcraft", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(DotCraftArguments?.Trim(), "-acp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Saves settings to file.
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(SettingsPath, ToJson());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Failed to save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates settings and returns any errors.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(DotCraftCommand))
            {
                errors.Add(AgentConnection == AgentConnectionDotCraft
                    ? "DotCraft command is not configured."
                    : "ACP command is not configured.");
            }

            if (AgentConnection == AgentConnectionCustomAcp && string.IsNullOrWhiteSpace(DotCraftArguments))
            {
                errors.Add("ACP arguments are not configured.");
            }

            if (AgentConnection == AgentConnectionDotCraft
                && DotCraftAppServer == DotCraftAppServerRemote
                && string.IsNullOrWhiteSpace(RemoteAppServerUrl))
            {
                errors.Add("Remote AppServer URL is not configured.");
            }
            else if (AgentConnection == AgentConnectionDotCraft
                     && DotCraftAppServer == DotCraftAppServerRemote
                     && !IsWebSocketUrl(RemoteAppServerUrl))
            {
                errors.Add("Remote AppServer URL must start with ws:// or wss://.");
            }

            if (RequestTimeoutSeconds < 1 || RequestTimeoutSeconds > 300)
            {
                errors.Add("Request timeout must be between 1 and 300 seconds.");
            }

            return errors;
        }

        private static bool IsWebSocketUrl(string value)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
                return false;
            return string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks whether the effective workspace contains a .craft directory.
        /// Returns true when the workspace is ready; false otherwise, with a human-readable
        /// message that explains what is missing and how to fix it.
        /// </summary>
        public bool ValidateWorkspace(out string errorMessage)
        {
            var workspace = EffectiveWorkspacePath;

            if (string.IsNullOrEmpty(workspace) || !Directory.Exists(workspace))
            {
                errorMessage = $"Workspace directory does not exist: \"{workspace}\".\n" +
                               "Set a valid Workspace Path in Project Settings > DotCraft.";
                return false;
            }

            var craftDir = Path.Combine(workspace, ".craft");
            if (!Directory.Exists(craftDir))
            {
                errorMessage = $"The workspace \"{workspace}\" does not contain a .craft directory.\n" +
                               "Run `dotcraft` in that directory first, " +
                               "or change the Workspace Path in Project Settings > DotCraft to a directory " +
                               "that already has a .craft folder.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Resets settings to defaults.
        /// </summary>
        public void ResetToDefaults()
        {
            AgentConnection = AgentConnectionDotCraft;
            DotCraftAppServer = DotCraftAppServerLocalHub;
            DotCraftCommand = "dotcraft";
            DotCraftArguments = "-acp";
            RemoteAppServerUrl = "";
            RemoteAppServerToken = "";
            WorkspacePath = "";
            EnvironmentVariables = new Dictionary<string, string>();
            AutoReconnect = true;
            VerboseLogging = false;
            ShowThinkingContent = false;
            RequestTimeoutSeconds = 30;
            MaxHistoryMessages = 1000;
            EnableBuiltinUnityTools = true;
            EnableAppBindingLocalServer = true;
            DynamicToolEnabledById = new Dictionary<string, bool>();
            McpServers = new List<McpServerEntry>();
        }
    }
}

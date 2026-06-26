using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.AppBinding
{
    internal sealed class AppServerDynamicToolSpec
    {
        [JsonProperty("namespace", NullValueHandling = NullValueHandling.Ignore)]
        public string Namespace { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema", NullValueHandling = NullValueHandling.Ignore)]
        public JToken InputSchema { get; set; }

        [JsonProperty("deferLoading", NullValueHandling = NullValueHandling.Ignore)]
        public bool? DeferLoading { get; set; }

        [JsonProperty("approval", NullValueHandling = NullValueHandling.Ignore)]
        public AppServerToolApprovalDescriptor Approval { get; set; }
    }

    internal sealed class AppServerToolApprovalDescriptor
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("targetArgument")]
        public string TargetArgument { get; set; }

        [JsonProperty("operation", NullValueHandling = NullValueHandling.Ignore)]
        public string Operation { get; set; }

        [JsonProperty("operationArgument", NullValueHandling = NullValueHandling.Ignore)]
        public string OperationArgument { get; set; }
    }

    internal sealed class AppBindingToolCatalogEntry
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }

        [JsonProperty("risk")]
        public string Risk { get; set; }

        [JsonProperty("defaultExposure")]
        public string DefaultExposure { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }
    }

    internal sealed class AppBindingScopeInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("risk")]
        public string Risk { get; set; }

        [JsonProperty("defaultSelected", NullValueHandling = NullValueHandling.Ignore)]
        public bool? DefaultSelected { get; set; }
    }

    internal sealed class AppBindingConnectionRequestInfo
    {
        [JsonProperty("appId")]
        public string AppId { get; set; }

        [JsonProperty("connectionRequestId")]
        public string ConnectionRequestId { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("workspaceLabel")]
        public string WorkspaceLabel { get; set; }

        [JsonProperty("userLabel")]
        public string UserLabel { get; set; }

        [JsonProperty("expiresAt")]
        public DateTimeOffset ExpiresAt { get; set; }
    }

    internal sealed class AppBindingConnectionStatus
    {
        [JsonProperty("appId")]
        public string AppId { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }
    }

    internal sealed class AppBindingRequestInfo
    {
        [JsonProperty("appId")]
        public string AppId { get; set; }

        [JsonProperty("bindingRequestId")]
        public string BindingRequestId { get; set; }

        [JsonProperty("threadId")]
        public string ThreadId { get; set; }

        [JsonProperty("threadTitle", NullValueHandling = NullValueHandling.Ignore)]
        public string ThreadTitle { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("requestedScopes")]
        public List<string> RequestedScopes { get; set; } = new();

        [JsonProperty("scopeCatalog")]
        public List<AppBindingScopeInfo> ScopeCatalog { get; set; } = new();

        [JsonProperty("requestedTools")]
        public List<string> RequestedTools { get; set; } = new();

        [JsonProperty("toolCatalog")]
        public List<AppBindingToolCatalogEntry> ToolCatalog { get; set; } = new();

        [JsonProperty("expiresAt")]
        public DateTimeOffset ExpiresAt { get; set; }
    }

    internal sealed class AppBindingWire
    {
        [JsonProperty("bindingId")]
        public string BindingId { get; set; }

        [JsonProperty("threadId")]
        public string ThreadId { get; set; }

        [JsonProperty("appId")]
        public string AppId { get; set; }

        [JsonProperty("attachedToolCount")]
        public int AttachedToolCount { get; set; }
    }

    internal sealed class AppBindingAcceptResponse
    {
        [JsonProperty("binding")]
        public AppBindingWire Binding { get; set; }
    }

    internal sealed class AppBindingAttachToolsResponse
    {
        [JsonProperty("binding")]
        public AppBindingWire Binding { get; set; }

        [JsonProperty("acceptedToolCount")]
        public int AcceptedToolCount { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; } = new();
    }

    internal sealed class AppServerDynamicToolCall
    {
        public string ThreadId { get; set; }
        public string TurnId { get; set; }
        public string CallId { get; set; }
        public string Namespace { get; set; }
        public string Tool { get; set; }
        public JToken Arguments { get; set; }
    }

    internal sealed class AppServerDynamicToolResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("contentItems", NullValueHandling = NullValueHandling.Ignore)]
        public List<AppServerToolContentItem> ContentItems { get; set; }

        [JsonProperty("structuredResult", NullValueHandling = NullValueHandling.Ignore)]
        public object StructuredResult { get; set; }

        [JsonProperty("errorCode", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorCode { get; set; }

        [JsonProperty("errorMessage", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        public static AppServerDynamicToolResult Ok(object structuredResult)
        {
            return new AppServerDynamicToolResult
            {
                Success = true,
                StructuredResult = structuredResult
            };
        }

        public static AppServerDynamicToolResult Failed(string code, string message)
        {
            return new AppServerDynamicToolResult
            {
                Success = false,
                ErrorCode = code,
                ErrorMessage = message,
                ContentItems = new List<AppServerToolContentItem>
                {
                    new AppServerToolContentItem { Type = "text", Text = message }
                }
            };
        }
    }

    internal sealed class AppServerToolContentItem
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }
}

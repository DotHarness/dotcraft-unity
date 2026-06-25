using System;
using System.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpGatewayProbeResult
    {
        public McpGatewayProbeResult(
            bool success,
            string status,
            string error,
            string[] toolNames)
        {
            Success = success;
            Status = status ?? string.Empty;
            Error = error ?? string.Empty;
            ToolNames = toolNames ?? Array.Empty<string>();
        }

        public bool Success { get; }

        public string Status { get; }

        public string Error { get; }

        public string[] ToolNames { get; }

        public int ToolCount => ToolNames.Length;

        public string ToolSummary =>
            ToolNames.Length == 0 ? "No tools" : string.Join(", ", ToolNames.Take(8));

        public static McpGatewayProbeResult Failed(string status, string error) =>
            new(false, status, error, Array.Empty<string>());
    }
}

using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal static class ToolGatewayAdapters
    {
        public static object ProjectTools(string format)
        {
            var normalized = string.IsNullOrWhiteSpace(format)
                ? "canonical"
                : format.Trim().ToLowerInvariant();
            var tools = UnityToolGateway.Instance.ListTools();

            switch (normalized)
            {
                case "canonical":
                    return new
                    {
                        tools = tools.Select(tool => new
                        {
                            name = tool.Name,
                            description = tool.Description,
                            inputSchema = tool.InputSchema
                        }).ToArray()
                    };
                case "openai-responses":
                    return new
                    {
                        tools = tools.Select(tool => new
                        {
                            type = "function",
                            name = tool.Name,
                            description = tool.Description,
                            parameters = tool.InputSchema,
                            strict = false
                        }).ToArray()
                    };
                case "openai-chat":
                    return new
                    {
                        tools = tools.Select(tool => new
                        {
                            type = "function",
                            function = new
                            {
                                name = tool.Name,
                                description = tool.Description,
                                parameters = tool.InputSchema
                            }
                        }).ToArray()
                    };
                case "claude":
                    return new
                    {
                        tools = tools.Select(tool => new
                        {
                            name = tool.Name,
                            description = tool.Description,
                            input_schema = tool.InputSchema
                        }).ToArray()
                    };
                default:
                    throw new InvalidOperationException(
                        $"Unsupported gateway tool format '{format}'. Use canonical, openai-responses, openai-chat, or claude.");
            }
        }

        public static object ProjectGatewayResult(ToolGatewayResult result)
        {
            return new
            {
                success = result.Success,
                name = result.Name,
                result = result.StructuredResult,
                text = result.Text,
                errorCode = result.ErrorCode,
                errorMessage = result.ErrorMessage,
                durationMs = result.DurationMs
            };
        }

        public static object ProjectMcpTool(ToolGatewayToolSpec tool)
        {
            return new
            {
                name = tool.Name,
                description = tool.Description,
                inputSchema = tool.InputSchema
            };
        }

        public static object ProjectMcpToolResult(ToolGatewayResult result)
        {
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = string.IsNullOrWhiteSpace(result.Text)
                            ? JToken.FromObject(result.StructuredResult ?? new { }).ToString(Newtonsoft.Json.Formatting.None)
                            : result.Text
                    }
                },
                structuredContent = result.StructuredResult ?? ProjectGatewayResult(result),
                isError = !result.Success
            };
        }
    }
}

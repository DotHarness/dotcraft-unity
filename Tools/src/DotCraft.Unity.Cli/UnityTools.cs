using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Unity.Cli;

internal static class UnityTools
{
    public static readonly string ProductVersion =
        typeof(UnityTools).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("Product version metadata is missing.");
    public const string McpSdkVersion = "2.2.0";
    public const string RuntimeIdentifier = "win-x64";
    public const string ExecuteCSharp = "unity_execute_csharp";
    public const string Recompile = "unity_recompile";
    public const int DefaultRecompileYieldMs = 60_000;
    public const int MaxRecompileYieldMs = 120_000;

    public static IReadOnlyList<UnityToolDefinition> All { get; } =
    [
        new(ExecuteCSharp,
            "Compile and execute C# in the running Unity Editor, from an inline snippet or a saved script file.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "code": {
                  "type": "string",
                  "description": "C# method body to compile and execute. Use return to provide a result. Provide either code or path."
                },
                "path": {
                  "type": "string",
                  "description": "Project-relative or absolute path of a saved C# script to execute instead of code."
                },
                "args": {
                  "type": "object",
                  "description": "Values passed to the script as the Args JObject."
                },
                "mode": {
                  "type": "string",
                  "enum": ["editor", "playmode"],
                  "description": "Execution mode."
                }
              },
              "additionalProperties": false
            }
            """)),
        new(Recompile,
            "Import changed assets and compile scripts once, then wait for that compilation and the Domain Reload it triggers. Returns compiler errors when compilation fails. Never call it again for a compilation that is still running.",
            Schema($$"""
            {
              "type": "object",
              "properties": {
                "yieldTimeMs": {
                  "type": "integer",
                  "minimum": 0,
                  "maximum": {{MaxRecompileYieldMs}},
                  "description": "Milliseconds to wait for compilation and reload before returning the current state. Defaults to {{DefaultRecompileYieldMs}}."
                }
              },
              "additionalProperties": false
            }
            """))
    ];

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

internal sealed record UnityToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] JsonElement InputSchema);

internal sealed class UnityToolResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    public static UnityToolResult Failure(string name, string code, string message) => new()
    {
        Name = name, Success = false, ErrorCode = code, ErrorMessage = message, Text = message
    };
}

using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.Unity.Cli;

internal sealed class AttachMcpTool(UnityToolDefinition definition, AttachToolClient client) : McpServerTool
{
    public override Tool ProtocolTool { get; } = new()
    {
        Name = definition.Name,
        Description = definition.Description,
        InputSchema = definition.InputSchema.Clone()
    };

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await client.CallAsync(definition.Name, request.Params.Arguments, cancellationToken).ConfigureAwait(false);
        var structured = result.Result is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } value
            ? value.Clone()
            : JsonSerializer.SerializeToElement(new
            {
                success = result.Success,
                name = result.Name,
                errorCode = result.ErrorCode,
                errorMessage = result.ErrorMessage,
                durationMs = result.DurationMs
            });
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = string.IsNullOrWhiteSpace(result.Text) ? structured.GetRawText() : result.Text }],
            StructuredContent = structured,
            IsError = !result.Success
        };
    }
}

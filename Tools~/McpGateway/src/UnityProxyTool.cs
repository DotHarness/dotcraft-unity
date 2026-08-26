using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.Unity.McpGateway;

internal sealed class UnityProxyTool : McpServerTool
{
    private static readonly IReadOnlyList<object> EmptyMetadata = Array.Empty<object>();
    private readonly UnityToolGatewayClient _toolGatewayClient;
    private readonly ClientPresenceState _presence;
    private readonly Tool _protocolTool;

    public UnityProxyTool(
        ToolManifestEntry entry,
        UnityToolGatewayClient toolGatewayClient,
        ClientPresenceState presence)
    {
        _toolGatewayClient = toolGatewayClient;
        _presence = presence;
        _protocolTool = new Tool
        {
            Name = entry.Name,
            Description = entry.Description,
            InputSchema = entry.InputSchema.Clone()
        };
    }

    public override Tool ProtocolTool => _protocolTool;

    public override IReadOnlyList<object> Metadata => EmptyMetadata;

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _toolGatewayClient
            .CallAsync(_protocolTool.Name, request.Params.Arguments, cancellationToken, _presence.SessionId)
            .ConfigureAwait(false);

        var structured = BuildStructuredContent(result);
        var text = string.IsNullOrWhiteSpace(result.Text)
            ? structured.GetRawText()
            : result.Text;

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = structured,
            IsError = !result.Success
        };
    }

    private static JsonElement BuildStructuredContent(UnityToolGatewayResult result)
    {
        if (result.Result is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } value)
            return value.Clone();

        return JsonSerializer.SerializeToElement(new
        {
            success = result.Success,
            name = result.Name,
            errorCode = result.ErrorCode,
            errorMessage = result.ErrorMessage,
            durationMs = result.DurationMs
        });
    }
}

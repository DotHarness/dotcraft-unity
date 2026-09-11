using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Unity.Cli;

internal sealed record AttachTarget(int Pid, string? Project, string? Editor);

internal interface IAttachConnection : IAsyncDisposable
{
    IReadOnlyList<AttachTarget> List();
    Task<JsonObject> ConnectAsync(int pid, CancellationToken cancellationToken);
    Task<JsonObject> ExecuteAsync(string code, JsonObject? args, CancellationToken cancellationToken);
}

internal sealed class AttachConnection : IAttachConnection
{
    private readonly string _owner = "gateway-" + Guid.NewGuid().ToString("N");
    private UnityAttachService? _service;
    private UnityAttachService Service => _service ??= UnityAttachService.CreateDefault();

    public IReadOnlyList<AttachTarget> List()
    {
        var targets = JsonSerializer.SerializeToElement(Service.List());
        return targets.EnumerateArray().Select(value => new AttachTarget(
            value.GetProperty("pid").GetInt32(),
            value.TryGetProperty("project", out var project) ? project.GetString() : null,
            value.TryGetProperty("editor", out var editor) ? editor.GetString() : null)).ToArray();
    }

    public Task<JsonObject> ConnectAsync(int pid, CancellationToken cancellationToken) =>
        Service.Connect(_owner, pid, cancellationToken);

    public Task<JsonObject> ExecuteAsync(string code, JsonObject? args, CancellationToken cancellationToken) =>
        Service.Execute(_owner, code, args, false, 1000, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_service != null) await _service.DisposeAsync().ConfigureAwait(false);
    }
}

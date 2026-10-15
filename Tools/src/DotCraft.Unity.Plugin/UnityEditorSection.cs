using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Context.WorldState;

namespace DotCraft.Unity;

internal sealed class UnityEditorSection(Func<string, UnityEditorState?> read) : IWorldStateSection
{
    private const int MaximumValueChars = 512;
    private const int MaximumErrors = 20;
    private const string Heading = "## Unity Editor";

    public string Id => "unity_editor";

    public JsonNode? Snapshot(WorldStateContext context)
    {
        var values = new JsonObject();
        if (read(context.Thread.Id) is { } state)
        {
            values["editor"] = WorldStateHash.Of(Render(state));
            if (state.Connection == UnityConnectionState.Exited) values["exited"] = true;
        }
        return values;
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        if (read(context.Thread.Id) is { } state) return Render(state);
        var previouslyConnected = previous.Kind switch
        {
            PreviousSectionKind.Known => previous.Value is JsonObject values && values.ContainsKey("editor") && !values.ContainsKey("exited"),
            PreviousSectionKind.Unknown => true,
            _ => false
        };
        return previouslyConnected ? $"{Heading}\nThis task is no longer connected to a Unity Editor." : null;
    }

    internal static string Render(UnityEditorState state)
    {
        var text = new StringBuilder(Heading).Append('\n');
        text.Append("Project: ").Append(Bound(state.Project ?? "unknown")).Append('\n');
        text.Append("Editor: Unity ").Append(Bound(state.Version ?? "unknown")).Append(" (PID ").Append(state.Pid).Append(")\n");
        var connection = state.Connection;
        text.Append("Connection: ").Append(connection switch
        {
            UnityConnectionState.Ready => "ready",
            UnityConnectionState.Reloading => "reloading scripts",
            UnityConnectionState.Recovering => "recovering after script reload",
            UnityConnectionState.RecoveryFailed => "recovery after script reload failed",
            _ => "Editor no longer running"
        }).Append('\n');
        if (connection == UnityConnectionState.Exited || state.Bridge is not { } bridge) return text.ToString().TrimEnd();

        if (bridge.PlayMode is "edit" or "playing" or "paused")
            text.Append("Play mode: ").Append(bridge.PlayMode).Append('\n');
        if (bridge.Compiling)
            text.Append("Compilation: compiling\n");
        else if (bridge.Compile is { FinishedUtc: not null, Succeeded: true })
            text.Append("Compilation: last compile succeeded\n");
        else if (bridge.Compile is { FinishedUtc: not null, Succeeded: false } failed)
        {
            text.Append("Compilation: last compile failed with ").Append(failed.ErrorCount)
                .Append(failed.ErrorCount == 1 ? " error\n" : " errors\n");
            var errors = (failed.Errors ?? []).Take(MaximumErrors).ToArray();
            foreach (var error in errors)
                text.Append("- ").Append(Bound(error.Message ?? "")).Append('\n');
            if (failed.ErrorCount > errors.Length)
                text.Append("- ").Append(failed.ErrorCount - errors.Length).Append(" more not listed\n");
        }
        return text.ToString().TrimEnd();
    }

    private static string Bound(string value)
    {
        var singleLine = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var length = Math.Min(singleLine.Length, MaximumValueChars);
        if (length < singleLine.Length && char.IsHighSurrogate(singleLine[length - 1])) length--;
        return length == singleLine.Length ? singleLine : singleLine[..length] + "...";
    }
}

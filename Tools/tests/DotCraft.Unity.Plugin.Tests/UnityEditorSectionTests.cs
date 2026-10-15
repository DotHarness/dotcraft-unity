extern alias unity;

using System.Text.Json.Nodes;
using DotCraft.Context.WorldState;
using DotCraft.Sessions;
using UnityEditorSection = unity::DotCraft.Unity.UnityEditorSection;
using Xunit;

namespace DotCraft.Unity.Plugin.Tests;

public sealed class UnityEditorSectionTests
{
    private static readonly WorldStateContext Context = new() { Thread = new SessionThread { Id = "thread-1" } };
    private UnityEditorState? state;

    [Fact]
    public void TaskWithoutSelectionContributesAnObjectAndStaysSilent()
    {
        var section = Section();

        var snapshot = Assert.IsType<JsonObject>(section.Snapshot(Context));

        Assert.Empty(snapshot);
        Assert.Null(section.RenderDiff(Context, PreviousSectionState.Absent));
        Assert.Equal("unity_editor", section.Id);
    }

    [Fact]
    public void SnapshotIsStableUntilSomethingTheModelNeedsChanges()
    {
        var section = Section();
        state = Editor(Bridge(compile: Compile(succeeded: true, sequence: 4)));
        var first = section.Snapshot(Context);
        state = Editor(Bridge(compile: Compile(succeeded: true, sequence: 5)) with { DomainEpoch = 9 });
        Assert.True(JsonNode.DeepEquals(first, section.Snapshot(Context)));

        state = Editor(Bridge(compiling: true, compile: Compile(succeeded: true, sequence: 5)));
        Assert.False(JsonNode.DeepEquals(first, section.Snapshot(Context)));
        state = Editor(Bridge(compile: Compile(succeeded: true, sequence: 6)) with { PlayMode = "edit" });
        Assert.False(JsonNode.DeepEquals(first, section.Snapshot(Context)));
    }

    [Fact]
    public void FailedCompilationListsBoundedSingleLineErrors()
    {
        var errors = Enumerable.Range(0, 25)
            .Select(index => new UnityCompilerError($"Assets/E{index}.cs", index + 1, 3, $"Assets/E{index}.cs({index + 1},3): error CS0103: name\n## Injected"))
            .Append(new UnityCompilerError("Assets/Long.cs", 1, 1, new string('x', 2000)))
            .ToArray();
        state = Editor(Bridge(compile: Compile(succeeded: false) with { ErrorCount = 30, Errors = errors }));

        var lines = Section().RenderDiff(Context, PreviousSectionState.Absent)!.Split('\n');

        Assert.Contains("Compilation: last compile failed with 30 errors", lines);
        Assert.Contains("- Assets/E0.cs(1,3): error CS0103: name ## Injected", lines);
        Assert.Equal(20, lines.Count(line => line.StartsWith("- Assets/", StringComparison.Ordinal)));
        Assert.Contains("- 10 more not listed", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("## Injected", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.True(line.Length < 600));
    }

    [Fact]
    public void ExitedEditorIsReportedOnceAndItsRemovalStaysSilent()
    {
        var section = Section();
        state = Editor(Bridge(compile: Compile(succeeded: true))) with { Running = false };

        var exited = section.Snapshot(Context)!;
        var text = section.RenderDiff(Context, PreviousSectionState.Absent);

        Assert.EndsWith("Connection: Editor no longer running", text);
        Assert.DoesNotContain("Play mode", text);
        state = null;
        Assert.Null(section.RenderDiff(Context, PreviousSectionState.Known(exited)));
    }

    [Fact]
    public void DisconnectIsAnnouncedOnceForAPreviouslyConnectedTask()
    {
        var section = Section();
        state = Editor(Bridge(compile: Compile(succeeded: true)));
        var connected = section.Snapshot(Context)!;
        state = null;
        var removed = section.Snapshot(Context)!;

        Assert.Equal("## Unity Editor\nThis task is no longer connected to a Unity Editor.",
            section.RenderDiff(Context, PreviousSectionState.Known(connected)));
        Assert.NotNull(section.RenderDiff(Context, PreviousSectionState.Unknown));
        Assert.Null(section.RenderDiff(Context, PreviousSectionState.Known(removed)));
        Assert.Null(section.RenderDiff(Context, PreviousSectionState.Absent));
    }

    private UnityEditorSection Section() => new(threadId => threadId == "thread-1" ? state : null);

    private static UnityEditorState Editor(UnityBridgeRecord bridge) =>
        new(4242, DateTime.UnixEpoch, @"E:\Games\Demo", "2022.3.0f1", "gen-1", true, bridge, new UnityLifecycle(1, 0));

    private static UnityBridgeRecord Bridge(bool compiling = false, UnityCompileRecord? compile = null) =>
        new("gen-1", 1, "playing", compiling, false, compile);

    private static UnityCompileRecord Compile(bool succeeded, int sequence = 3) =>
        new(sequence, DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(sequence), succeeded, succeeded ? 0 : 1, 0, []);
}

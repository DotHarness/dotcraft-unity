using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Unity;

internal sealed class UnityToolSource(UnityAttachService service, string workspace) : AIFunctionToolSource
{
    public override string SourceId => "DotCraft.Unity";

    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        var tools = new UnityTools(service, workspace, IsPlanMode(context));
        return
        [
            DotCraft.GeneratedTools.Unity.GeneratedToolFunctions.UnityTools_List(tools),
            DotCraft.GeneratedTools.Unity.GeneratedToolFunctions.UnityTools_Connect(tools),
            DotCraft.GeneratedTools.Unity.GeneratedToolFunctions.UnityTools_Status(tools),
            DotCraft.GeneratedTools.Unity.GeneratedToolFunctions.UnityTools_Execute(tools),
            DotCraft.GeneratedTools.Unity.GeneratedToolFunctions.UnityTools_Wait(tools),
            DotCraft.GeneratedTools.Unity.GeneratedToolFunctions.UnityTools_Disconnect(tools)
        ];
    }

    protected override string? GetNamespace(AIFunction function, ToolPlanningContext context) => "unity";

    protected override ToolPolicyHints GetPolicyHints(AIFunction function, ToolPlanningContext context) => new(
        RequiresApproval: function.Name is "connect" or "execute" or "disconnect",
        ReadOnly: function.Name is "list" or "status");

    protected override ToolPresentationDescriptor? GetPresentation(AIFunction function, ToolPlanningContext context) => null;

    private static bool IsPlanMode(ToolPlanningContext context) =>
        context.Mode.Equals("plan", StringComparison.OrdinalIgnoreCase);
}

internal sealed class UnityTools(UnityAttachService service, string workspace, bool planMode)
{
    [GeneratedTool(Name = "list")]
    [ToolRpc]
    [Description("List local Unity Editors without attaching.")]
    public ValueTask<ToolExecutionResult> List(CancellationToken cancellationToken = default) => ExecuteAsync(
        () => Task.FromResult(new JsonObject
        {
            ["state"] = "completed",
            ["result"] = JsonSerializer.SerializeToNode(service.List())
        }), cancellationToken);

    [GeneratedTool(Name = "connect")]
    [ToolRpc]
    [Description("Connect this task to a Unity Editor by PID, or omit PID to reconnect its selected Editor after script reload.")]
    public ValueTask<ToolExecutionResult> Connect(
        ToolInvocationContext context,
        [Range(1, int.MaxValue)]
        [Description("Unity Editor process ID. Omit it to reconnect the Editor already selected by this task.")] int? pid = null,
        CancellationToken cancellationToken = default) =>
        planMode
            ? ValueTask.FromResult(ModeDenied())
            : ExecuteAsync(() => service.Connect(context.ThreadId, pid, cancellationToken), cancellationToken);

    [GeneratedTool(Name = "status")]
    [ToolRpc]
    [Description("Read the selected Editor state without reconnecting.")]
    public ValueTask<ToolExecutionResult> Status(
        ToolInvocationContext context,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => service.Status(context.ThreadId), cancellationToken);

    [GeneratedTool(Name = "execute")]
    [ToolRpc]
    [Description("Execute C# method-body statements on the selected Editor's main thread. Supports cross-frame await and optional background execution.")]
    public ValueTask<ToolExecutionResult> Execute(
        ToolInvocationContext context,
        [Description("Inline C# method-body statements. Specify exactly one of code or path.")] string? code = null,
        [Description("Host-side C# script path. Specify exactly one of code or path.")] string? path = null,
        [Description("Optional JSON values available to the snippet as Args.")] JsonObject? args = null,
        [Description("Return an execution ID when the code is still running after the initial wait.")] bool runInBackground = false,
        [Range(0, 30000)]
        [Description("Milliseconds to wait for a background execution before returning its current state.")] int yieldTimeMs = 1000,
        CancellationToken cancellationToken = default)
    {
        if (planMode) return ValueTask.FromResult(ModeDenied());
        if ((code is null) == (path is null))
            return ValueTask.FromResult(Failure(ToolErrorCodes.InputInvalid, "Specify exactly one of code or path."));

        return ExecuteAsync(async () =>
        {
            if (path is not null)
                code = await File.ReadAllTextAsync(
                    Path.GetFullPath(path, context.WorkspacePath ?? workspace), cancellationToken);
            return await service.Execute(
                context.ThreadId, code!, args, runInBackground, yieldTimeMs, cancellationToken);
        }, cancellationToken);
    }

    [GeneratedTool(Name = "wait")]
    [ToolRpc]
    [Description("Wait for, inspect, or cooperatively cancel a background Unity execution.")]
    public ValueTask<ToolExecutionResult> Wait(
        ToolInvocationContext context,
        [Description("Execution ID returned by unity.execute.")] string executionId,
        [Range(0, 30000)]
        [Description("Milliseconds to wait for completion before returning the current state.")] int yieldTimeMs = 1000,
        [Description("Request cooperative cancellation before returning the latest state.")] bool terminate = false,
        CancellationToken cancellationToken = default) =>
        planMode && terminate
            ? ValueTask.FromResult(ModeDenied())
            : ExecuteAsync(
                () => service.Wait(context.ThreadId, executionId, yieldTimeMs, terminate, cancellationToken),
                cancellationToken);

    [GeneratedTool(Name = "disconnect")]
    [ToolRpc]
    [Description("Disconnect this task from its selected Unity Editor.")]
    public ValueTask<ToolExecutionResult> Disconnect(
        ToolInvocationContext context,
        CancellationToken cancellationToken = default) =>
        planMode
            ? ValueTask.FromResult(ModeDenied())
            : ExecuteAsync(() => service.Disconnect(context.ThreadId), cancellationToken);

    private static async ValueTask<ToolExecutionResult> ExecuteAsync(
        Func<Task<JsonObject>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Complete(await operation());
        }
        catch (OperationCanceledException) { return Failure("UnityOutcomeUnknown", "The call stopped waiting. Execution may have started; do not replay automatically."); }
        catch (ArgumentException error) { return Failure(ToolErrorCodes.InputInvalid, error.Message); }
        catch (UnityTargetException error) { return Failure(error.Code, error.Message); }
        catch (Exception error) { return Failure("UnityAttachFailed", error.Message); }
    }

    private static ToolExecutionResult Complete(JsonObject result)
    {
        var json = result.ToJsonString();
        return result["state"]?.GetValue<string>() is "completed" or "queued" or "running" or "cancelled" or "lost" or "unknown"
            ? ToolExecutionResult.Succeeded(json, JsonSerializer.SerializeToElement(result))
            : Failure(result["errorCode"]?.GetValue<string>() ?? "UnityRequestIncomplete", json);
    }

    private static ToolExecutionResult ModeDenied() =>
        Failure("UnityModeDenied", "Unity mutations are unavailable in Plan mode.");

    private static ToolExecutionResult Failure(string code, string text) =>
        ToolExecutionResult.Failed(new ToolError(code, text), text);
}

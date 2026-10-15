using System.Text;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.Unity;

internal sealed class UnityExecutionNotifier(UnityAttachService service, IServiceProvider services)
{
    private const string TriggerKind = "app";
    private const string TriggerLabel = "Unity";
    private const string DisplayText = "Unity execution finished";
    private const int MaximumConsecutiveTurns = 3;
    private const int MaximumOutputChars = 4000;

    public async Task RunAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            if (!service.HasPendingNotices) continue;
            foreach (var notice in await service.CollectFinishedExecutions(stopping))
                await DeliverAsync(notice, stopping);
        }
    }

    private async Task DeliverAsync(UnityExecutionNotice notice, CancellationToken cancellationToken)
    {
        try
        {
            if (services.GetService(typeof(ISessionService)) is not ISessionService sessions) return;
            var thread = await sessions.GetThreadAsync(notice.ThreadId, cancellationToken);
            if (thread.Status != ThreadStatus.Active) return;

            var modelText = ModelText(notice);
            IList<AIContent> content = [new TextContent(modelText)];
            var input = new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "text", Text = DisplayText }],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = modelText }],
                DisplayText = DisplayText
            };
            using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo { Kind = TriggerKind, Label = TriggerLabel, RefId = notice.ExecutionId });

            var activeTurn = thread.Turns.LastOrDefault(static turn =>
                turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
            if (activeTurn is not null)
            {
                try
                {
                    await sessions.SteerTurnAsync(thread.Id, activeTurn.Id, content, null, cancellationToken, input);
                    return;
                }
                catch (InvalidOperationException) { }
            }
            if (TrailingUnityTurns(thread) >= MaximumConsecutiveTurns) return;
            await sessions.EnqueueTurnInputAsync(thread.Id, content, null, cancellationToken, input);
            await sessions.TryStartNextQueuedTurnAsync(thread.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }

    private static int TrailingUnityTurns(SessionThread thread)
    {
        var count = 0;
        for (var index = thread.Turns.Count - 1; index >= 0; index--)
        {
            if (thread.Turns[index].Input?.AsUserMessage is not { TriggerKind: TriggerKind, TriggerLabel: TriggerLabel }) break;
            count++;
        }
        return count;
    }

    internal static string ModelText(UnityExecutionNotice notice)
    {
        var state = notice.Result["state"]?.GetValue<string>() ?? "unknown";
        var text = new StringBuilder("<unity_execution_notification>\n");
        text.Append("Background Unity execution ").Append(notice.ExecutionId).Append(" finished with state ").Append(state).Append(".\n");
        if (notice.Result["result"] is { } result) text.Append("Result: ").Append(Bound(result.ToJsonString())).Append('\n');
        if (notice.Result["errorCode"]?.GetValue<string>() is { } code) text.Append("Error code: ").Append(Bound(code)).Append('\n');
        if (notice.Result["error"]?.GetValue<string>() is { } error) text.Append("Error: ").Append(Bound(error)).Append('\n');
        return text.Append("</unity_execution_notification>").ToString();
    }

    private static string Bound(string value)
    {
        if (value.Length <= MaximumOutputChars) return value;
        var length = char.IsHighSurrogate(value[MaximumOutputChars - 1]) ? MaximumOutputChars - 1 : MaximumOutputChars;
        return value[..length] + "...";
    }
}

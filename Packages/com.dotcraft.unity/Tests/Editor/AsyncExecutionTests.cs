using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Execution;
using DotCraft.Editor.Extensions;
using DotCraft.Editor.ToolGateway;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DotCraft.Editor.Tests
{
    public sealed class AsyncExecutionTests
    {
        [UnityTest]
        public IEnumerator SnippetResumesOnMainThreadAfterMultipleFrames()
        {
            var task = Execute("var thread = Thread.CurrentThread.ManagedThreadId; await ctx.WaitFrames(2); return Thread.CurrentThread.ManagedThreadId == thread;");
            Assert.That(task.IsCompleted, Is.False);
            yield return Await(task);
            Assert.That(task.Result.Success, Is.True, task.Result.ErrorMessage);
            Assert.That(task.Result.ReturnValue, Is.True);
        }

        [UnityTest]
        public IEnumerator CancellationUnwindsPendingFrameAwait()
        {
            using var cancellation = new CancellationTokenSource();
            var task = Execute("await ctx.WaitUntil(() => false); return 1;", cancellation.Token);
            Assert.That(task.IsCompleted, Is.False);
            cancellation.Cancel();
            yield return Await(task);
            Assert.That(task.IsCanceled, Is.True);
        }

        [UnityTest]
        public IEnumerator GatewayCancellationReachesTheRunningSnippet()
        {
            using var cancellation = new CancellationTokenSource();
            var task = UnityToolRegistry.Instance.CallAsync("unity_execute_csharp",
                new JObject { ["code"] = "await ctx.WaitUntil(() => false); return 1;" }, cancellation.Token);
            Assert.That(task.IsCompleted, Is.False);
            cancellation.Cancel();
            yield return Await(task);
            Assert.That(task.IsCanceled, Is.True);
        }

        [UnityTest]
        public IEnumerator PredicateFailureBecomesExecutionError()
        {
            var task = Execute("await ctx.WaitUntil(() => throw new InvalidOperationException(\"predicate failed\")); return 1;");
            yield return Await(task);
            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.ErrorMessage, Does.Contain("predicate failed"));
        }

        [UnityTest]
        public IEnumerator CancelledQueuedActionNeverRuns()
        {
            var called = false;
            using var cancellation = new CancellationTokenSource();
            var task = MainThreadDispatcher.RunOnMainThread(() => { called = true; return 1; }, cancellationToken: cancellation.Token);
            cancellation.Cancel();
            yield return null;
            yield return null;
            Assert.That(task.IsCanceled, Is.True);
            Assert.That(called, Is.False);
        }

        [UnityTest]
        public IEnumerator ExpiredQueuedActionNeverRuns()
        {
            var called = false;
            var task = MainThreadDispatcher.RunOnMainThread(() => { called = true; return 1; }, timeoutMs: 1);
            Assert.That(SpinWait.SpinUntil(() => task.IsCompleted, 2000), Is.True);
            yield return null;
            yield return null;
            Assert.That(task.IsCanceled, Is.True);
            Assert.That(called, Is.False);
        }

        [Test]
        public void InvalidatedSchedulerCancelsAwaitWithoutReplayingContinuation()
        {
            var scheduler = new UnityContinuationScheduler(() => 0);
            var context = new UnityExecutionContext(CancellationToken.None, scheduler.Schedule);
            var mutations = 0;
            async Task Run() { await context.WaitFrame(); mutations++; }
            var task = Run();
            scheduler.Invalidate();
            scheduler.Pump();
            Assert.That(task.IsCanceled, Is.True);
            Assert.That(mutations, Is.Zero);
        }

        [Test]
        public void ScheduledCancellationIsObservedByAwaitResult()
        {
            using var cancellation = new CancellationTokenSource();
            var scheduler = new UnityContinuationScheduler(() => 0);
            var context = new UnityExecutionContext(cancellation.Token, scheduler.Schedule);
            cancellation.Cancel();
            async Task Run() { await context.WaitFrame(); }
            var task = Run();
            scheduler.Pump();
            Assert.That(task.IsCanceled, Is.True);
        }

        static Task<ExecutionResult> Execute(string code, CancellationToken token = default) =>
            ExecutionRouter.Instance.ExecuteAsync(new ExecutionRequest(UnityExecutionEngines.CSharp, UnityExecutionModes.Editor, code), token);

        static IEnumerator Await(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Execution did not complete before the test deadline.");
        }
    }
}

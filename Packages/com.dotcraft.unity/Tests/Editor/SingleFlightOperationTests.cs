using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Connection;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class SingleFlightOperationTests
    {
        [Test]
        public async Task RunAsync_ConcurrentCallersShareFirstAttempt()
        {
            using var singleFlight = new SingleFlightOperation<int>();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocationCount = 0;

            Task<int> Start(int value) => singleFlight.RunAsync(async _ =>
            {
                Interlocked.Increment(ref invocationCount);
                await release.Task;
                return value;
            });

            var tasks = Enumerable.Range(1, 8).Select(Start).ToArray();
            release.TrySetResult(true);

            var results = await Task.WhenAll(tasks);
            Assert.That(invocationCount, Is.EqualTo(1));
            Assert.That(results, Is.All.EqualTo(1));
        }

        [Test]
        public void RunAsync_FailureClearsAttemptAndAllowsRetry()
        {
            using var singleFlight = new SingleFlightOperation<bool>();
            var invocationCount = 0;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await singleFlight.RunAsync(_ =>
                {
                    Interlocked.Increment(ref invocationCount);
                    throw new InvalidOperationException("expected");
                }));

            var result = singleFlight.RunAsync(_ =>
            {
                Interlocked.Increment(ref invocationCount);
                return Task.FromResult(true);
            }).GetAwaiter().GetResult();

            Assert.That(result, Is.True);
            Assert.That(invocationCount, Is.EqualTo(2));
        }

        [Test]
        public async Task CancelAndWaitAsync_CancelsAttemptAndAllowsRetry()
        {
            using var singleFlight = new SingleFlightOperation<bool>();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var attempt = singleFlight.RunAsync(async ct =>
            {
                started.TrySetResult(true);
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return true;
            });

            await started.Task;
            await singleFlight.CancelAndWaitAsync();
            Assert.That(attempt.IsCanceled, Is.True);
            Assert.That(singleFlight.IsRunning, Is.False);

            Assert.That(await singleFlight.RunAsync(_ => Task.FromResult(true)), Is.True);
        }
    }
}

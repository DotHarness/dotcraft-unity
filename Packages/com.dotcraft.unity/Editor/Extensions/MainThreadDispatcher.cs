using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace DotCraft.Editor.Extensions
{
    /// <summary>Moves background protocol work onto Unity's main thread.</summary>
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> Queue = new();

        // Bound each update so tool traffic cannot monopolize the Editor loop.
        private const int MaxFrameTimeMs = 16;

        static MainThreadDispatcher()
        {
            EditorApplication.update += ProcessQueue;
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            Queue.Enqueue(action);
        }

        /// <summary>Queues a function, skipping work cancelled or timed out before dispatch.</summary>
        public static Task<T> RunOnMainThread<T>(Func<T> func, int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            var work = new QueuedWork<T>(_ => Task.FromResult(func()), timeoutMs, cancellationToken, false);
            Queue.Enqueue(work.Run);
            return work.Task;
        }

        public static Task RunOnMainThread(Func<Task> func, int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            return RunOnMainThread(async _ => { await func(); return true; }, timeoutMs, cancellationToken);
        }

        public static Task<T> RunOnMainThread<T>(Func<Task<T>> func, int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            return RunOnMainThread(_ => func(), timeoutMs, cancellationToken);
        }

        /// <summary>Passes a linked caller/deadline token into the dispatched operation.</summary>
        public static Task<T> RunOnMainThread<T>(Func<CancellationToken, Task<T>> func, int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            var work = new QueuedWork<T>(func, timeoutMs, cancellationToken);
            Queue.Enqueue(work.Run);
            return work.Task;
        }

        sealed class QueuedWork<T>
        {
            readonly Func<CancellationToken, Task<T>> func;
            readonly TaskCompletionSource<T> completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly CancellationTokenSource lifetime;
            readonly CancellationTokenRegistration registration;
            readonly bool logExceptions;
            int state;

            internal QueuedWork(Func<CancellationToken, Task<T>> func, int timeoutMs, CancellationToken cancellationToken, bool logExceptions = true)
            {
                this.func = func;
                this.logExceptions = logExceptions;
                lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (timeoutMs >= 0) lifetime.CancelAfter(timeoutMs);
                registration = lifetime.Token.Register(() =>
                {
                    Interlocked.CompareExchange(ref state, 2, 0);
                    completion.TrySetCanceled();
                });
            }

            internal Task<T> Task => completion.Task;

            internal async void Run()
            {
                try
                {
                    if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                    lifetime.Token.ThrowIfCancellationRequested();
                    completion.TrySetResult(await func(lifetime.Token));
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { if (logExceptions) Debug.LogException(ex); completion.TrySetException(ex); }
                finally { registration.Dispose(); lifetime.Dispose(); }
            }
        }

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == 1;

        public static void RunOrEnqueue(Action action)
        {
            if (IsMainThread)
            {
                action?.Invoke();
            }
            else
            {
                Enqueue(action);
            }
        }

        private static void ProcessQueue()
        {
            var stopwatch = Stopwatch.StartNew();
            int processed = 0;
            const int maxActionsPerFrame = 100;

            while (processed < maxActionsPerFrame && Queue.TryDequeue(out var action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
                processed++;

                if (stopwatch.ElapsedMilliseconds >= MaxFrameTimeMs)
                {
                    break;
                }
            }
        }

    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// Coalesces overlapping asynchronous operations into one shared attempt.
    /// The first caller owns the operation parameters and cancellation token.
    /// </summary>
    internal sealed class SingleFlightOperation<TResult> : IDisposable
    {
        private readonly object _sync = new();
        private Task<TResult> _activeTask;
        private CancellationTokenSource _activeCts;
        private int _generation;
        private bool _disposed;

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                    return _activeTask != null && !_activeTask.IsCompleted;
            }
        }

        public Task<TResult> RunAsync(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken ct = default)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            TaskCompletionSource<TResult> completion;
            CancellationTokenSource operationCts;
            int generation;

            lock (_sync)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SingleFlightOperation<TResult>));

                if (_activeTask != null && !_activeTask.IsCompleted)
                    return _activeTask;

                generation = ++_generation;
                operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _activeCts = operationCts;
                _activeTask = completion.Task;
            }

            _ = ExecuteAsync(operation, operationCts, completion, generation);
            return completion.Task;
        }

        public async Task CancelAndWaitAsync()
        {
            Task<TResult> activeTask;
            CancellationTokenSource activeCts;

            lock (_sync)
            {
                activeTask = _activeTask;
                activeCts = _activeCts;
            }

            activeCts?.Cancel();
            if (activeTask == null)
                return;

            try
            {
                await activeTask.ConfigureAwait(false);
            }
            catch
            {
                // Cancellation and operation failures are observed by the original callers.
            }
        }

        private async Task ExecuteAsync(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationTokenSource operationCts,
            TaskCompletionSource<TResult> completion,
            int generation)
        {
            try
            {
                completion.TrySetResult(await operation(operationCts.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
            {
                completion.TrySetCanceled();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                lock (_sync)
                {
                    if (_generation == generation)
                    {
                        _activeTask = null;
                        _activeCts = null;
                    }
                }

                operationCts.Dispose();
            }
        }

        public void Dispose()
        {
            CancellationTokenSource activeCts;
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                activeCts = _activeCts;
            }

            activeCts?.Cancel();
        }
    }
}

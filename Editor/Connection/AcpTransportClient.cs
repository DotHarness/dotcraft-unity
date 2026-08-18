using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// stdio-based JSON-RPC transport for ACP on the Unity side.
    /// Symmetric implementation to DotCraft's AcpTransport.
    /// </summary>
    public sealed class AcpTransportClient : IDisposable
    {
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _writeLock = new();
        private int _nextOutgoingId;
        private bool _isRunning;

        // Pending requests awaiting response (Client→Agent)
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JToken>> _pendingRequests = new();

        // Incoming Agent→Client requests queue
        private readonly SemaphoreSlim _incomingSemaphore = new(0);

        // Request handlers
        private readonly ConcurrentDictionary<string, Func<JToken, Task<object>>> _requestHandlers = new();

        // Extension method handlers keyed by prefix (e.g. "_unity/")
        private readonly ConcurrentDictionary<string, Func<string, JToken, Task<object>>> _extensionHandlers = new();

        private Task _readerLoopTask;
        private CancellationTokenSource _readerCts;

        /// <summary>
        /// Event raised when a session/update notification is received.
        /// </summary>
        public event Action<JToken> OnSessionUpdate;

        /// <summary>
        /// Event raised when any notification is received.
        /// </summary>
        public event Action<string, JToken> OnNotification;

        /// <summary>
        /// Event raised when transport encounters an error.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Initializes the transport with the given streams.
        /// </summary>
        public void Initialize(Stream input, Stream output)
        {
            if (_readerLoopTask != null && !_readerLoopTask.IsCompleted)
                throw new InvalidOperationException("Cannot initialize ACP transport while its reader loop is active.");

            DisposeStreamWrappers();

            StreamReader reader = null;
            StreamWriter writer = null;
            try
            {
                reader = new StreamReader(input, Encoding.UTF8);
                writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true };
                lock (_writeLock)
                {
                    _reader = reader;
                    _writer = writer;
                }
            }
            catch
            {
                DisposeStreamWrapper(writer);
                DisposeStreamWrapper(reader);
                throw;
            }
        }

        /// <summary>
        /// Starts the background reader loop.
        /// </summary>
        public void StartReaderLoop()
        {
            if (_isRunning) return;
            if (_reader == null || _writer == null)
                throw new InvalidOperationException("Transport not initialized.");

            _isRunning = true;
            _readerCts = new CancellationTokenSource();
            _readerLoopTask = Task.Run(() => ReaderLoopAsync(_readerCts.Token));
        }

        /// <summary>
        /// Synchronously marks the reader loop as stopped and cancels its token.
        /// Call this before killing the process so that the reader loop sees the
        /// cancellation before it encounters the EOF from the closed pipe, preventing
        /// a spurious "Connection closed by agent." error during intentional shutdown.
        /// </summary>
        public void CancelReaderLoop()
        {
            _isRunning = false;
            _readerCts?.Cancel();
        }

        /// <summary>
        /// Stops the reader loop.
        /// </summary>
        public async Task StopReaderLoopAsync()
        {
            _isRunning = false;
            _readerCts?.Cancel();

            var readerLoopTask = _readerLoopTask;
            if (readerLoopTask != null)
            {
                // ReadLineAsync has no CancellationToken overload, so the loop
                // can only exit when the underlying stream closes. Use a timeout
                // to avoid blocking indefinitely if that hasn't happened yet.
                try
                {
                    // ConfigureAwait(false) prevents the continuation from being posted back to
                    // the Unity SynchronizationContext. Without it, calling .Wait() on this
                    // method from the main thread (e.g. Dispose) would deadlock for the full
                    // 2-second timeout even though the reader loop exits almost immediately after
                    // the process is killed.
                    await Task.WhenAny(readerLoopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }
            }

            _readerCts?.Dispose();
            _readerCts = null;
            _readerLoopTask = null;
            CancelPendingRequests();
        }

        /// <summary>
        /// Registers a handler for Agent→Client requests.
        /// </summary>
        public void RegisterHandler(string method, Func<JToken, Task<object>> handler)
        {
            _requestHandlers[method] = handler;
        }

        /// <summary>
        /// Registers a handler for extension methods matching the given prefix (e.g. "_unity/").
        /// The method name is passed as a separate parameter instead of being injected into params.
        /// </summary>
        public void RegisterExtensionHandler(string prefix, Func<string, JToken, Task<object>> handler)
        {
            _extensionHandlers[prefix] = handler;
        }

        /// <summary>
        /// Unregisters an extension method prefix handler.
        /// </summary>
        public void UnregisterExtensionHandler(string prefix)
        {
            _extensionHandlers.TryRemove(prefix, out _);
        }

        /// <summary>
        /// Unregisters a handler.
        /// </summary>
        public void UnregisterHandler(string method)
        {
            _requestHandlers.TryRemove(method, out _);
        }

        /// <summary>
        /// Sends a request to the Agent and awaits the response.
        /// </summary>
        public async Task<JToken> SendRequestAsync(string method, object @params, CancellationToken ct = default, TimeSpan? timeout = null)
        {
            if (_writer == null)
                throw new InvalidOperationException("Transport not initialized.");

            var id = Interlocked.Increment(ref _nextOutgoingId);
            var tcs = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[id] = tcs;

            try
            {
                var request = new
                {
                    jsonrpc = "2.0",
                    id,
                    method,
                    @params
                };

                WriteLine(request);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
                using var reg = cts.Token.Register(() => tcs.TrySetCanceled());

                return await tcs.Task;
            }
            finally
            {
                _pendingRequests.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// Sends a notification to the Agent (no response expected).
        /// </summary>
        public void SendNotification(string method, object @params = null)
        {
            if (_writer == null)
                throw new InvalidOperationException("Transport not initialized.");

            var notification = new JsonRpcNotification { Method = method, Params = @params };
            WriteLine(notification);
        }

        /// <summary>
        /// Sends a response to an Agent→Client request.
        /// </summary>
        public void SendResponse(JToken id, object result)
        {
            if (_writer == null) return;

            var response = new JsonRpcResponse { Id = id, Result = result };
            WriteLine(response);
        }

        /// <summary>
        /// Sends an error response to an Agent→Client request.
        /// </summary>
        public void SendError(JToken id, int code, string message, object data = null)
        {
            if (_writer == null) return;

            var response = new JsonRpcResponse
            {
                Id = id,
                Error = new JsonRpcError { Code = code, Message = message, Data = data }
            };
            WriteLine(response);
        }

        private async Task ReaderLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _reader != null)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null)
                    {
                        // EOF — only report if unexpected (not an intentional shutdown)
                        if (!ct.IsCancellationRequested)
                            OnError?.Invoke("Connection closed by agent.");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (DotCraftSettings.Instance.VerboseLogging)
                    {
                        Debug.Log($"[DotCraft ←] {line}");
                    }

                    ProcessMessage(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                OnError?.Invoke($"Reader error: {ex.Message}");
            }
            catch (Exception)
            {
                // ignored
            }
        }

        internal void ProcessMessage(string line)
        {
            JObject root;
            try
            {
                root = JObject.Parse(line);
            }
            catch (JsonException)
            {
                // Non-JSON lines (e.g. startup diagnostics) are expected; skip silently.
                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] Skipping non-JSON line: {line}");
                }
                return;
            }

            // Check if this is a response to one of our requests
            var idProp = root["id"];
            if (idProp != null &&
                root["method"] == null &&
                idProp.Type == JTokenType.Integer)
            {
                var id = idProp.ToObject<int>();
                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    var resultProp = root["result"];
                    var errorProp = root["error"];
                    if (resultProp != null)
                    {
                        tcs.TrySetResult(resultProp);
                    }
                    else if (errorProp != null)
                    {
                        tcs.TrySetException(new AcpTransportException(errorProp.ToString(Formatting.None)));
                    }
                    else
                    {
                        tcs.TrySetResult(null);
                    }
                }
                return;
            }

            // Check if this is a request from the Agent
            var methodProp = root["method"];
            if (methodProp != null)
            {
                var method = methodProp.ToObject<string>();
                var id = root["id"];
                var @params = root["params"];

                // Check if it's a notification (no id)
                if (id == null)
                {
                    HandleNotification(method, @params);
                }
                else
                {
                    // It's a request - need to respond
                    HandleRequestAsync(method, @params, id).Forget();
                }
            }
        }

        private void HandleNotification(string method, JToken @params)
        {
            if (method == AcpMethods.SessionUpdate && @params != null)
            {
                OnSessionUpdate?.Invoke(@params);
            }

            OnNotification?.Invoke(method, @params);
        }

        private async Task HandleRequestAsync(string method, JToken @params, JToken id)
        {
            try
            {
                if (_requestHandlers.TryGetValue(method, out var handler))
                {
                    var result = await handler(ParamsOrEmptyObject(@params));
                    SendResponse(id, result);
                }
                else if (TryGetExtensionHandler(method, out var extHandler))
                {
                    // Extension method handling - pass method name separately
                    var result = await extHandler(method, ParamsOrEmptyObject(@params));
                    SendResponse(id, result);
                }
                else
                {
                    SendError(id, -32601, $"Method not found: {method}");
                }
            }
            catch (AcpRequestException ex)
            {
                SendError(id, ex.Code, ex.Message, ex.ErrorData);
            }
            catch (Exception ex)
            {
                SendError(id, -32603, $"Internal error: {ex.Message}");
            }
        }

        private static JToken ParamsOrEmptyObject(JToken parameters)
        {
            return parameters == null
                   || parameters.Type == JTokenType.Null
                   || parameters.Type == JTokenType.Undefined
                ? new JObject()
                : parameters;
        }

        private bool TryGetExtensionHandler(string method, out Func<string, JToken, Task<object>> handler)
        {
            foreach (var kvp in _extensionHandlers)
            {
                if (method.StartsWith(kvp.Key, StringComparison.Ordinal))
                {
                    handler = kvp.Value;
                    return true;
                }
            }

            handler = null;
            return false;
        }

        private void WriteLine(object message)
        {
            var json = DotCraftJson.Serialize(message);

            if (DotCraftSettings.Instance.VerboseLogging)
            {
                Debug.Log($"[DotCraft →] {json}");
            }

            lock (_writeLock)
            {
                _writer?.WriteLine(json);
            }
        }

        public void Dispose()
        {
            StopReaderLoopAsync().Wait(TimeSpan.FromSeconds(2));
            DisposeStreamWrappers();
            _incomingSemaphore?.Dispose();

            CancelPendingRequests();
        }

        private void DisposeStreamWrappers()
        {
            StreamReader reader;
            StreamWriter writer;
            lock (_writeLock)
            {
                reader = _reader;
                writer = _writer;
                _reader = null;
                _writer = null;
            }

            // The process manager may have already closed and disposed the redirected
            // stdio streams. Dispose each wrapper independently so a stale closed stream
            // cannot prevent the next transport from being initialized.
            DisposeStreamWrapper(writer);
            DisposeStreamWrapper(reader);
        }

        private static void DisposeStreamWrapper(IDisposable wrapper)
        {
            if (wrapper == null)
                return;

            try
            {
                wrapper.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Expected when the owning process has already disposed redirected stdio.
            }
            catch (IOException)
            {
                // Expected when the redirected pipe closes during intentional teardown.
            }
        }

        private void CancelPendingRequests()
        {
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }
    }

    public sealed class AcpTransportException : Exception
    {
        public AcpTransportException(string message) : base(message) { }
    }

    internal static class TaskExtensions
    {
        public static async void Forget(this Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    internal sealed class AcpRequestException : Exception
    {
        public AcpRequestException(int code, string message, object errorData = null)
            : base(message)
        {
            Code = code;
            ErrorData = errorData;
        }

        public int Code { get; }

        public object ErrorData { get; }
    }
}

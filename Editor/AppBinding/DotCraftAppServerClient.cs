using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.AppBinding
{
    internal sealed class DotCraftAppServerClient : IDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JToken>> _pending = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private Func<AppServerDynamicToolCall, CancellationToken, Task<AppServerDynamicToolResult>> _toolHandler;
        private Func<string, JToken, CancellationToken, Task> _notificationHandler;
        private CancellationTokenSource _readCts;
        private Task _readLoop;
        private long _nextId;
        private int _disconnectNotified;
        private bool _disposed;

        public bool IsConnected => _socket.State == WebSocketState.Open;

        public event Action<string> Disconnected;

        public static async Task<DotCraftAppServerClient> ConnectAsync(string endpoint, CancellationToken ct)
        {
            var client = new DotCraftAppServerClient();
            await client._socket.ConnectAsync(new Uri(endpoint), ct).ConfigureAwait(false);
            client._readCts = new CancellationTokenSource();
            client._readLoop = Task.Run(() => client.ReadLoopAsync(client._readCts.Token));
            return client;
        }

        public void SetDynamicToolHandler(Func<AppServerDynamicToolCall, CancellationToken, Task<AppServerDynamicToolResult>> handler)
        {
            _toolHandler = handler;
        }

        public void SetNotificationHandler(Func<string, JToken, CancellationToken, Task> handler)
        {
            _notificationHandler = handler;
        }

        public async Task InitializeAsync(CancellationToken ct)
        {
            await SendRequestAsync("initialize", new
            {
                protocolVersion = "1.0",
                clientInfo = new
                {
                    name = "dotcraft-unity",
                    version = "0.1.6"
                },
                capabilities = new { }
            }, ct).ConfigureAwait(false);

            await SendNotificationAsync("initialized", new { }, ct).ConfigureAwait(false);
        }

        public async Task<AppBindingConnectionRequestInfo> GetAppConnectionRequestAsync(
            string appId,
            string connectionRequestId,
            string requestToken,
            CancellationToken ct)
        {
            var result = await SendRequestAsync("app/connection/request/get", new
            {
                appId,
                connectionRequestId,
                requestToken
            }, ct).ConfigureAwait(false);
            return DotCraftJson.ToObject<AppBindingConnectionRequestInfo>(result);
        }

        public async Task<AppBindingConnectionStatus> CompleteAppConnectionAsync(
            string connectionRequestId,
            string requestToken,
            string appId,
            string accountLabel,
            CancellationToken ct)
        {
            var result = await SendRequestAsync("app/connection/connect", new
            {
                connectionRequestId,
                requestToken,
                appId,
                accountLabel,
                connectionProof = new
                {
                    client = "dotcraft-unity",
                    project = accountLabel
                }
            }, ct).ConfigureAwait(false);
            return DotCraftJson.ToObject<AppBindingConnectionStatus>(result);
        }

        public async Task<AppBindingRequestInfo> GetAppBindingRequestAsync(
            string appId,
            string bindingRequestId,
            string requestToken,
            CancellationToken ct)
        {
            var result = await SendRequestAsync("app/binding/request/get", new
            {
                appId,
                bindingRequestId,
                requestToken
            }, ct).ConfigureAwait(false);
            return DotCraftJson.ToObject<AppBindingRequestInfo>(result);
        }

        public async Task<AppBindingAcceptResponse> AcceptAppBindingAsync(
            string bindingRequestId,
            string requestToken,
            string grantId,
            string[] grantedScopes,
            string approvedBy,
            CancellationToken ct)
        {
            var result = await SendRequestAsync("app/binding/accept", new
            {
                bindingRequestId,
                requestToken,
                grantId,
                grantedScopes,
                approvalMode = "appAccepted",
                approvedBy,
                auditRef = "dotcraft-unity"
            }, ct).ConfigureAwait(false);
            return DotCraftJson.ToObject<AppBindingAcceptResponse>(result);
        }

        public async Task<AppBindingAttachToolsResponse> AttachToolsAsync(
            string bindingId,
            string threadId,
            string appId,
            string grantId,
            UnityAppBindingToolAttachment attachment,
            CancellationToken ct)
        {
            var result = await SendRequestAsync("app/binding/attachTools", new
            {
                bindingId,
                threadId,
                appId,
                grantId,
                tools = attachment.Tools,
                toolCatalog = attachment.ToolCatalog,
                directToolNames = attachment.DirectToolNames,
                deferredToolNames = attachment.DeferredToolNames,
                grantProof = new
                {
                    client = "dotcraft-unity",
                    generatedAt = DateTimeOffset.UtcNow
                }
            }, ct).ConfigureAwait(false);
            return DotCraftJson.ToObject<AppBindingAttachToolsResponse>(result);
        }

        private async Task<JToken> SendRequestAsync(string method, object @params, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            try
            {
                await SendObjectAsync(new
                {
                    jsonrpc = "2.0",
                    id,
                    method,
                    @params
                }, ct).ConfigureAwait(false);

                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private Task SendNotificationAsync(string method, object @params, CancellationToken ct)
        {
            return SendObjectAsync(new
            {
                jsonrpc = "2.0",
                method,
                @params
            }, ct);
        }

        private async Task SendObjectAsync(object payload, CancellationToken ct)
        {
            var json = DotCraftJson.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            string disconnectReason = null;
            try
            {
                while (!ct.IsCancellationRequested
                       && (_socket.State == WebSocketState.Open
                           || _socket.State == WebSocketState.CloseReceived
                           || _socket.State == WebSocketState.CloseSent))
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        disconnectReason = "DotCraft AppServer closed the connection.";
                        break;
                    }

                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                        continue;

                    var payload = Encoding.UTF8.GetString(message.ToArray());
                    message.SetLength(0);
                    await DispatchPayloadAsync(payload, ct).ConfigureAwait(false);
                }

                disconnectReason ??= "DotCraft AppServer connection closed.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                disconnectReason = null;
            }
            catch (Exception ex)
            {
                disconnectReason = ex.Message;
                foreach (var pending in _pending.Values)
                    pending.TrySetException(ex);
            }
            finally
            {
                if (!_disposed && !string.IsNullOrWhiteSpace(disconnectReason))
                {
                    var error = new InvalidOperationException(disconnectReason);
                    foreach (var pending in _pending.Values)
                        pending.TrySetException(error);
                    NotifyDisconnected(disconnectReason);
                }
            }
        }

        private async Task DispatchPayloadAsync(string payload, CancellationToken ct)
        {
            var root = JObject.Parse(payload);
            var method = root.Value<string>("method");
            if (!string.IsNullOrEmpty(method))
            {
                var id = root["id"];
                if (id != null && id.Type != JTokenType.Null)
                {
                    await HandleServerRequestAsync(id.DeepClone(), method, root["params"] ?? new JObject(), ct)
                        .ConfigureAwait(false);
                }
                else if (_notificationHandler != null)
                {
                    try
                    {
                        await _notificationHandler(method, root["params"] ?? new JObject(), ct)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Notification handlers are best-effort local state refreshes.
                    }
                }
                return;
            }

            var responseId = root.Value<long?>("id");
            if (responseId == null || !_pending.TryRemove(responseId.Value, out var tcs))
                return;

            var error = root["error"];
            if (error != null && error.Type != JTokenType.Null)
            {
                tcs.TrySetException(new InvalidOperationException(error.ToString()));
                return;
            }

            tcs.TrySetResult(root["result"] ?? new JObject());
        }

        private async Task HandleServerRequestAsync(JToken id, string method, JToken @params, CancellationToken ct)
        {
            AppServerDynamicToolResult result;
            if (method == "item/tool/call" && _toolHandler != null)
            {
                try
                {
                    result = await _toolHandler(new AppServerDynamicToolCall
                    {
                        ThreadId = @params.Value<string>("threadId"),
                        TurnId = @params.Value<string>("turnId"),
                        CallId = @params.Value<string>("callId"),
                        Namespace = @params.Value<string>("namespace"),
                        Tool = @params.Value<string>("tool"),
                        Arguments = @params["arguments"] ?? new JObject()
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = AppServerDynamicToolResult.Failed("UnityToolFailed", ex.Message);
                }
            }
            else
            {
                result = AppServerDynamicToolResult.Failed("UnsupportedRequest", $"Unsupported AppServer request '{method}'.");
            }

            await SendObjectAsync(new
            {
                jsonrpc = "2.0",
                id,
                result
            }, CancellationToken.None).ConfigureAwait(false);
        }

        private void NotifyDisconnected(string reason)
        {
            if (Interlocked.Exchange(ref _disconnectNotified, 1) != 0)
                return;
            Disconnected?.Invoke(reason);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _readCts?.Cancel();
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                {
                    _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dotcraft-unity closing", CancellationToken.None)
                        .ContinueWith(_ => _socket.Dispose());
                }
                else
                {
                    _socket.Dispose();
                }
            }
            catch
            {
                _socket.Dispose();
            }
            finally
            {
                _readCts?.Dispose();
            }
        }
    }
}

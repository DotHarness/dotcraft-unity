using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Settings;
using DotCraft.Editor.ToolGateway;
using Process = System.Diagnostics.Process;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class ToolGatewayHttpRequest
    {
        public string Method { get; set; }
        public string Target { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Body { get; set; } = string.Empty;
    }

    internal sealed class ToolGatewayHttpParseException : Exception
    {
        public ToolGatewayHttpParseException(int status, string reason, string message)
            : base(message)
        {
            Status = status;
            Reason = reason;
        }

        public int Status { get; }
        public string Reason { get; }
    }

    internal sealed class ToolGatewayLocalServer : IDisposable
    {
        private const int MaxHeaderBytes = 64 * 1024;
        private const int MaxBodyBytes = 4 * 1024 * 1024;
        private const int AcceptPollMilliseconds = 25;
        private const int RestartReleaseWaitMilliseconds = 1500;
        private const int StartRetryWaitMilliseconds = 2500;
        private const int StopWaitMilliseconds = 1000;
        private const int StaleShutdownWaitMilliseconds = 1200;
        private const string AdminShutdownPath = "/dotcraft/admin/shutdown";
        private const string AdminStatusPath = "/dotcraft/admin/status";
        private const string SessionTokenPrefix = "DotCraft_ToolGateway_ShutdownToken_";

        private static readonly object LifecycleGate = new();
        private static readonly object RegistryGate = new();
        private static readonly Dictionary<int, ToolGatewayLocalServer> ServersByPort = new();
        private static int _nextInstanceId;

        private readonly object _gate = new();
        private readonly HashSet<TcpClient> _clients = new();
        private readonly List<Task> _clientTasks = new();
        private readonly int _instanceId;
        private readonly int _port;
        private readonly string _sessionTokenKey;

        private CancellationTokenSource _cts;
        private bool _disposed;
        private bool _isRunning;
        private string _lastError;
        private TcpListener _listener;
        private Socket _listenerSocket;
        private Thread _pumpThread;
        private string _shutdownToken;

        public ToolGatewayLocalServer()
            : this(ToolGatewayMcpProtocol.DefaultPort)
        {
        }

        internal ToolGatewayLocalServer(int port)
        {
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

            _port = port;
            _sessionTokenKey = SessionTokenPrefix + _port;
            _instanceId = Interlocked.Increment(ref _nextInstanceId);
            AppDomain.CurrentDomain.DomainUnload += OnHostTeardown;
            AppDomain.CurrentDomain.ProcessExit += OnHostTeardown;
        }

        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    return _isRunning;
                }
            }
        }

        public string LastError
        {
            get
            {
                lock (_gate)
                {
                    return _lastError;
                }
            }
        }

        public string ListenUrl => $"http://127.0.0.1:{_port}/dotcraft/";

        public void Start()
        {
            lock (LifecycleGate)
            {
                if (IsRunning)
                    return;

                var stoppedOwnedServer = false;
                if (HasLocalResources())
                {
                    StopCore(waitForHandlers: true, clearSessionToken: true);
                    stoppedOwnedServer = true;
                }

                if (StopRegisteredServerForPort())
                    stoppedOwnedServer = true;

                if (stoppedOwnedServer && !WaitUntilPortCanBind(TimeSpan.FromMilliseconds(RestartReleaseWaitMilliseconds)))
                {
                    Debug.LogWarning(
                        $"[DotCraft] MCP Tool Gateway port {_port} was not released immediately after stopping an owned server " +
                        $"(pid {Process.GetCurrentProcess().Id}, instance {_instanceId}). {DescribePortOwner()}");
                }

                StartWithRetries(
                    allowStaleShutdown: !stoppedOwnedServer,
                    operation: stoppedOwnedServer ? "start-after-owned-stop" : "start");
            }
        }

        public void Restart()
        {
            lock (LifecycleGate)
            {
                StopCore(waitForHandlers: true, clearSessionToken: true);
                if (!WaitUntilPortCanBind(TimeSpan.FromMilliseconds(RestartReleaseWaitMilliseconds)))
                {
                    Debug.LogWarning(
                        $"[DotCraft] MCP Tool Gateway port {_port} is still occupied after restart stop phase " +
                        $"(pid {Process.GetCurrentProcess().Id}, instance {_instanceId}). {DescribePortOwner()}");
                }

                StartWithRetries(allowStaleShutdown: false, operation: "restart");
            }
        }

        public void Stop()
        {
            lock (LifecycleGate)
            {
                StopCore(waitForHandlers: true, clearSessionToken: true);
            }
        }

        private void StartWithRetries(bool allowStaleShutdown, string operation)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(StartRetryWaitMilliseconds);
            var attemptedStaleShutdown = false;
            var attempts = 0;
            Exception lastError = null;

            while (true)
            {
                attempts++;
                if (TryStartListener(out lastError))
                    return;

                if (!IsAddressInUse(lastError))
                    break;

                if (allowStaleShutdown && !attemptedStaleShutdown && HasSessionShutdownToken())
                {
                    attemptedStaleShutdown = true;
                    TryStopStaleServer(lastError);
                }

                if (DateTime.UtcNow >= deadline)
                    break;

                Thread.Sleep(GetStartRetryDelayMilliseconds(attempts));
            }

            var message = BuildStartFailureMessage(lastError);
            lock (_gate)
            {
                _isRunning = false;
                _lastError = message;
            }

            LogStartFailureDiagnostics(operation, attempts, lastError);
            Debug.LogError($"[DotCraft] MCP Tool Gateway failed to start: {message}");
        }

        private bool TryStartListener(out Exception error)
        {
            error = null;
            TcpListener listener = null;
            Socket listenerSocket = null;
            CancellationTokenSource cts = null;
            try
            {
                cts = new CancellationTokenSource();
                listener = new TcpListener(IPAddress.Loopback, _port);
                listenerSocket = listener.Server;
                ConfigureListenerSocket(listenerSocket);
                listener.Start();

                var token = Guid.NewGuid().ToString("N");
                SessionState.SetString(_sessionTokenKey, token);
                var thread = new Thread(() => AcceptPumpLoop(listener, cts.Token))
                {
                    IsBackground = true,
                    Name = $"DotCraft MCP Tool Gateway {_port}"
                };

                lock (_gate)
                {
                    _cts = cts;
                    _isRunning = true;
                    _lastError = null;
                    _listener = listener;
                    _listenerSocket = listenerSocket;
                    _pumpThread = thread;
                    _shutdownToken = token;
                }

                RegisterCurrentServer();
                thread.Start();
                
                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] MCP Tool Gateway started on 127.0.0.1:{_port} (pid {Process.GetCurrentProcess().Id}, instance {_instanceId}).");
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                try
                {
                    cts?.Cancel();
                    CloseListener(listener, listenerSocket);
                }
                catch
                {
                }

                cts?.Dispose();
                return false;
            }
        }

        private void AcceptPumpLoop(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!listener.Pending())
                    {
                        if (!WaitForNextPoll(ct))
                            break;
                        continue;
                    }

                    var client = listener.AcceptTcpClient();
                    ConfigureAcceptedClient(client);
                    TrackClient(client);
                    TrackClientTask(Task.Run(() => HandleTrackedClientAsync(client, ct)));
                    PruneCompletedClientTasks();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (!ct.IsCancellationRequested)
                        RecordServerWarning(ex.Message);
                    break;
                }
                catch (InvalidOperationException ex)
                {
                    if (!ct.IsCancellationRequested)
                        RecordServerWarning(ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                        break;
                    RecordServerWarning(ex.Message);
                    if (!WaitForNextPoll(ct))
                        break;
                }
            }
        }

        private static bool WaitForNextPoll(CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return false;

            try
            {
                return !ct.WaitHandle.WaitOne(AcceptPollMilliseconds);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private static int GetStartRetryDelayMilliseconds(int attempt)
        {
            return Math.Min(50 + (attempt * 75), 350);
        }

        private static void ConfigureListenerSocket(Socket socket)
        {
            if (socket == null)
                return;

            if (ShouldSetExclusiveAddressUse())
            {
                try
                {
                    socket.ExclusiveAddressUse = true;
                }
                catch
                {
                }
            }

            try
            {
                socket.LingerState = new LingerOption(true, 0);
            }
            catch
            {
            }
        }

        private static bool ShouldSetExclusiveAddressUse()
        {
            return Environment.OSVersion.Platform != PlatformID.Win32NT;
        }

        private static void ConfigureAcceptedClient(TcpClient client)
        {
            if (client == null)
                return;

            try
            {
                client.NoDelay = true;
            }
            catch
            {
            }

            try
            {
                client.Client.LingerState = new LingerOption(true, 0);
            }
            catch
            {
            }
        }

        private void TrackClient(TcpClient client)
        {
            lock (_gate)
            {
                _clients.Add(client);
            }
        }

        private void TrackClientTask(Task task)
        {
            lock (_gate)
            {
                _clientTasks.Add(task);
            }
        }

        private void PruneCompletedClientTasks()
        {
            lock (_gate)
            {
                _clientTasks.RemoveAll(task => task.IsCompleted);
            }
        }

        private async Task HandleTrackedClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                await HandleClientAsync(client, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[DotCraft] MCP Tool Gateway client error: {ex.Message}");
                }
            }
            finally
            {
                lock (_gate)
                {
                    _clients.Remove(client);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    var request = await ReadHttpRequestAsync(stream, ct).ConfigureAwait(false);
                    if (request == null || string.IsNullOrWhiteSpace(request.Method) || string.IsNullOrWhiteSpace(request.Target))
                    {
                        await WriteResponseAsync(stream, 400, "Bad Request", "Missing request line.", ct).ConfigureAwait(false);
                        return;
                    }

                    if (!IsAllowedOrigin(request.Headers))
                    {
                        await WriteResponseAsync(stream, 403, "Forbidden", "Invalid request origin.", ct).ConfigureAwait(false);
                        return;
                    }

                    if (ToolGatewayHttpHandler.CanHandle(request.Target))
                    {
                        var response = await ToolGatewayHttpHandler.HandleAsync(
                            new ToolGatewayHttpRequestContext
                            {
                                Method = request.Method,
                                Target = request.Target,
                                Headers = request.Headers,
                                Body = request.Body
                            },
                            ct).ConfigureAwait(false);
                        await WriteRawResponseAsync(stream, response, ct).ConfigureAwait(false);
                        return;
                    }

                    if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteResponseAsync(stream, 405, "Method Not Allowed", "Only standard MCP Gateway methods are supported.", ct).ConfigureAwait(false);
                        return;
                    }

                    if (TryHandleAdminRequest(request.Target, stream, ct, out var adminTask))
                    {
                        await adminTask.ConfigureAwait(false);
                        return;
                    }

                    await WriteResponseAsync(stream, 404, "Not Found", "Only the standard MCP Gateway is available.", ct)
                        .ConfigureAwait(false);
                }
                catch (ToolGatewayHttpParseException ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            await WriteResponseAsync(stream, ex.Status, ex.Reason, ex.Message, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            // The client may have disconnected while the error response was being written.
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            await WriteResponseAsync(stream, 500, "Internal Server Error", ex.Message, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            // The client may have disconnected while the error response was being written.
                        }
                    }
                }
            }
        }

        private static async Task<ToolGatewayHttpRequest> ReadHttpRequestAsync(
            NetworkStream stream,
            CancellationToken ct)
        {
            var buffer = new byte[4096];
            var bytes = new List<byte>();
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (read == 0)
                    break;

                for (var i = 0; i < read; i++)
                    bytes.Add(buffer[i]);

                headerEnd = FindHeaderEnd(bytes);
                if (headerEnd < 0 && bytes.Count > MaxHeaderBytes)
                {
                    throw new ToolGatewayHttpParseException(
                        431,
                        "Request Header Fields Too Large",
                        "HTTP request headers are too large.");
                }
            }

            if (headerEnd < 0)
                return null;
            if (headerEnd > MaxHeaderBytes)
            {
                throw new ToolGatewayHttpParseException(
                    431,
                    "Request Header Fields Too Large",
                    "HTTP request headers are too large.");
            }

            var headerText = Encoding.ASCII.GetString(bytes.Take(headerEnd).ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
                return null;

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
                return null;

            var request = new ToolGatewayHttpRequest
            {
                Method = requestLine[0],
                Target = requestLine[1]
            };

            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                var key = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1).Trim();
                if ((string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    && request.Headers.ContainsKey(key))
                {
                    throw BadRequest($"Duplicate HTTP {key} header.");
                }

                request.Headers[key] = value;
            }

            var bodyStart = headerEnd + 4;
            var bodyReader = new BufferedHttpBodyReader(
                stream,
                bytes.Skip(bodyStart).ToArray());
            var hasContentLength = request.Headers.TryGetValue("Content-Length", out var rawContentLength);
            if (request.Headers.TryGetValue("Transfer-Encoding", out var transferEncoding))
            {
                if (hasContentLength)
                    throw BadRequest("HTTP requests cannot include both Content-Length and Transfer-Encoding.");

                ValidateChunkedTransferEncoding(transferEncoding);
                request.Body = await ReadChunkedBodyAsync(bodyReader, ct).ConfigureAwait(false);
                return request;
            }

            var contentLength = 0;
            if (hasContentLength
                && (!int.TryParse(
                        rawContentLength,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out contentLength)
                    || contentLength < 0))
            {
                throw BadRequest("Invalid HTTP Content-Length header.");
            }

            if (contentLength > MaxBodyBytes)
                throw PayloadTooLarge();

            if (contentLength > 0)
            {
                var bodyBytes = new byte[contentLength];
                if (!await bodyReader.ReadExactlyAsync(bodyBytes, 0, contentLength, ct).ConfigureAwait(false))
                    throw BadRequest("HTTP request body ended before Content-Length bytes were received.");

                request.Body = Encoding.UTF8.GetString(bodyBytes);
            }

            return request;
        }

        private static void ValidateChunkedTransferEncoding(string rawTransferEncoding)
        {
            var codings = (rawTransferEncoding ?? string.Empty)
                .Split(',')
                .Select(value => value.Trim())
                .ToArray();
            if (codings.Length == 0 || codings.Any(string.IsNullOrWhiteSpace))
                throw BadRequest("Invalid HTTP Transfer-Encoding header.");

            var chunkedIndexes = codings
                .Select((coding, index) => new { coding, index })
                .Where(entry => string.Equals(entry.coding, "chunked", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index)
                .ToArray();
            if (chunkedIndexes.Length > 1
                || (chunkedIndexes.Length == 1 && chunkedIndexes[0] != codings.Length - 1))
            {
                throw BadRequest("HTTP chunked transfer coding must appear once and as the final coding.");
            }

            if (codings.Length == 1 && chunkedIndexes.Length == 1)
                return;

            throw new ToolGatewayHttpParseException(
                501,
                "Not Implemented",
                "HTTP transfer codings other than chunked are not supported.");
        }

        private static async Task<string> ReadChunkedBodyAsync(
            BufferedHttpBodyReader reader,
            CancellationToken ct)
        {
            using var body = new MemoryStream();
            var metadataBytes = 0;

            while (true)
            {
                var chunkLine = await ReadChunkMetadataLineAsync(reader, metadataBytes, ct).ConfigureAwait(false);
                metadataBytes = chunkLine.MetadataBytes;
                var separator = chunkLine.Value.IndexOf(';');
                var rawChunkSize = (separator >= 0 ? chunkLine.Value.Substring(0, separator) : chunkLine.Value).Trim();
                if (rawChunkSize.Length == 0
                    || !ulong.TryParse(
                        rawChunkSize,
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out var chunkSize))
                {
                    throw BadRequest("Invalid HTTP chunk size.");
                }

                if (chunkSize == 0)
                {
                    while (true)
                    {
                        var trailerLine = await ReadChunkMetadataLineAsync(reader, metadataBytes, ct).ConfigureAwait(false);
                        metadataBytes = trailerLine.MetadataBytes;
                        if (trailerLine.Value.Length == 0)
                            return Encoding.UTF8.GetString(body.ToArray());

                        var trailerSeparator = trailerLine.Value.IndexOf(':');
                        if (trailerSeparator <= 0)
                            throw BadRequest("Invalid HTTP chunk trailer.");
                    }
                }

                if (chunkSize > (ulong)(MaxBodyBytes - body.Length))
                    throw PayloadTooLarge();

                if (!await reader.CopyExactlyAsync(body, (int)chunkSize, ct).ConfigureAwait(false))
                    throw BadRequest("HTTP chunk data ended before the declared chunk size was received.");

                metadataBytes = await ReadRequiredCrlfAsync(reader, metadataBytes, ct).ConfigureAwait(false);
            }
        }

        private static async Task<ChunkMetadataLine> ReadChunkMetadataLineAsync(
            BufferedHttpBodyReader reader,
            int metadataBytes,
            CancellationToken ct)
        {
            var lineBytes = new List<byte>();
            while (true)
            {
                var value = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (value < 0)
                    throw BadRequest("HTTP chunked body ended before its terminating chunk.");

                metadataBytes = AddChunkMetadataBytes(metadataBytes, 1);
                if (value == '\n')
                    throw BadRequest("HTTP chunk metadata must use CRLF line endings.");

                if (value != '\r')
                {
                    lineBytes.Add((byte)value);
                    continue;
                }

                var lineFeed = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (lineFeed < 0)
                    throw BadRequest("HTTP chunk metadata ended before its CRLF line ending.");

                metadataBytes = AddChunkMetadataBytes(metadataBytes, 1);
                if (lineFeed != '\n')
                    throw BadRequest("HTTP chunk metadata must use CRLF line endings.");

                return new ChunkMetadataLine(
                    Encoding.ASCII.GetString(lineBytes.ToArray()),
                    metadataBytes);
            }
        }

        private static async Task<int> ReadRequiredCrlfAsync(
            BufferedHttpBodyReader reader,
            int metadataBytes,
            CancellationToken ct)
        {
            var carriageReturn = await reader.ReadByteAsync(ct).ConfigureAwait(false);
            var lineFeed = await reader.ReadByteAsync(ct).ConfigureAwait(false);
            if (carriageReturn != '\r' || lineFeed != '\n')
                throw BadRequest("HTTP chunk data must end with CRLF.");

            return AddChunkMetadataBytes(metadataBytes, 2);
        }

        private static int AddChunkMetadataBytes(int current, int additional)
        {
            if (current > MaxHeaderBytes - additional)
                throw BadRequest("HTTP chunk metadata is too large.");

            return current + additional;
        }

        private static ToolGatewayHttpParseException BadRequest(string message)
        {
            return new ToolGatewayHttpParseException(400, "Bad Request", message);
        }

        private static ToolGatewayHttpParseException PayloadTooLarge()
        {
            return new ToolGatewayHttpParseException(413, "Payload Too Large", "HTTP request body is too large.");
        }

        private readonly struct ChunkMetadataLine
        {
            public ChunkMetadataLine(string value, int metadataBytes)
            {
                Value = value;
                MetadataBytes = metadataBytes;
            }

            public string Value { get; }
            public int MetadataBytes { get; }
        }

        private sealed class BufferedHttpBodyReader
        {
            private readonly byte[] _buffer = new byte[4096];
            private readonly byte[] _initialBytes;
            private readonly NetworkStream _stream;
            private int _bufferCount;
            private int _bufferOffset;
            private int _initialOffset;

            public BufferedHttpBodyReader(NetworkStream stream, byte[] initialBytes)
            {
                _stream = stream;
                _initialBytes = initialBytes ?? Array.Empty<byte>();
            }

            public async Task<int> ReadByteAsync(CancellationToken ct)
            {
                if (!await EnsureAvailableAsync(ct).ConfigureAwait(false))
                    return -1;

                if (_initialOffset < _initialBytes.Length)
                    return _initialBytes[_initialOffset++];

                return _buffer[_bufferOffset++];
            }

            public async Task<bool> ReadExactlyAsync(
                byte[] destination,
                int offset,
                int count,
                CancellationToken ct)
            {
                var remaining = count;
                while (remaining > 0)
                {
                    if (!await EnsureAvailableAsync(ct).ConfigureAwait(false))
                        return false;

                    var copied = CopyAvailable(destination, offset, remaining);
                    offset += copied;
                    remaining -= copied;
                }

                return true;
            }

            public async Task<bool> CopyExactlyAsync(Stream destination, int count, CancellationToken ct)
            {
                var remaining = count;
                while (remaining > 0)
                {
                    if (!await EnsureAvailableAsync(ct).ConfigureAwait(false))
                        return false;

                    if (_initialOffset < _initialBytes.Length)
                    {
                        var copied = Math.Min(remaining, _initialBytes.Length - _initialOffset);
                        destination.Write(_initialBytes, _initialOffset, copied);
                        _initialOffset += copied;
                        remaining -= copied;
                        continue;
                    }

                    var buffered = Math.Min(remaining, _bufferCount - _bufferOffset);
                    destination.Write(_buffer, _bufferOffset, buffered);
                    _bufferOffset += buffered;
                    remaining -= buffered;
                }

                return true;
            }

            private async Task<bool> EnsureAvailableAsync(CancellationToken ct)
            {
                if (_initialOffset < _initialBytes.Length || _bufferOffset < _bufferCount)
                    return true;

                _bufferOffset = 0;
                _bufferCount = await _stream.ReadAsync(_buffer, 0, _buffer.Length, ct).ConfigureAwait(false);
                return _bufferCount > 0;
            }

            private int CopyAvailable(byte[] destination, int offset, int count)
            {
                if (_initialOffset < _initialBytes.Length)
                {
                    var copied = Math.Min(count, _initialBytes.Length - _initialOffset);
                    Buffer.BlockCopy(_initialBytes, _initialOffset, destination, offset, copied);
                    _initialOffset += copied;
                    return copied;
                }

                var buffered = Math.Min(count, _bufferCount - _bufferOffset);
                Buffer.BlockCopy(_buffer, _bufferOffset, destination, offset, buffered);
                _bufferOffset += buffered;
                return buffered;
            }
        }

        private static int FindHeaderEnd(IReadOnlyList<byte> bytes)
        {
            for (var i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == (byte)'\r'
                    && bytes[i - 2] == (byte)'\n'
                    && bytes[i - 1] == (byte)'\r'
                    && bytes[i] == (byte)'\n')
                {
                    return i - 3;
                }
            }

            return -1;
        }

        private static bool IsAllowedOrigin(IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null || !headers.TryGetValue("Origin", out var origin) || string.IsNullOrWhiteSpace(origin))
                return true;

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;

            return string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryHandleAdminRequest(
            string target,
            NetworkStream stream,
            CancellationToken ct,
            out Task responseTask)
        {
            responseTask = null;
            var uri = new Uri("http://127.0.0.1" + target, UriKind.Absolute);
            var isShutdown = string.Equals(uri.AbsolutePath, AdminShutdownPath, StringComparison.Ordinal);
            var isStatus = string.Equals(uri.AbsolutePath, AdminStatusPath, StringComparison.Ordinal);
            if (!isShutdown && !isStatus)
                return false;

            if (!ValidateAdminRequest(uri.Query))
            {
                responseTask = WriteResponseAsync(stream, 403, "Forbidden", "Invalid admin token.", ct);
                return true;
            }

            responseTask = isShutdown
                ? ShutdownAfterResponseAsync(stream, ct)
                : WriteAdminStatusAsync(stream, ct);
            return true;
        }

        private bool ValidateAdminRequest(string queryText)
        {
            var query = ParseQuery(queryText);
            var expectedProcessId = Process.GetCurrentProcess().Id.ToString();
            return query.TryGetValue("pid", out var processId)
                   && string.Equals(processId, expectedProcessId, StringComparison.Ordinal)
                   && query.TryGetValue("token", out var token)
                   && string.Equals(token, GetShutdownToken(), StringComparison.Ordinal);
        }

        private async Task ShutdownAfterResponseAsync(NetworkStream stream, CancellationToken ct)
        {
            await WriteResponseAsync(stream, 200, "OK", "DotCraft MCP Tool Gateway stopped.", ct)
                .ConfigureAwait(false);
            
            if (DotCraftSettings.Instance.VerboseLogging)
            {
                Debug.Log($"[DotCraft] MCP Tool Gateway admin shutdown accepted on port {_port} (pid {Process.GetCurrentProcess().Id}, instance {_instanceId}).");
            }

            StopFromBackgroundHandler();
        }

        private Task WriteAdminStatusAsync(NetworkStream stream, CancellationToken ct)
        {
            int clientCount;
            int taskCount;
            bool pumpAlive;
            bool running;
            lock (_gate)
            {
                clientCount = _clients.Count;
                taskCount = _clientTasks.Count(task => !task.IsCompleted);
                pumpAlive = _pumpThread?.IsAlive == true;
                running = _isRunning;
            }

            var message =
                $"DotCraft MCP Tool Gateway status: running={running}, port={_port}, " +
                $"pid={Process.GetCurrentProcess().Id}, instance={_instanceId}, clients={clientCount}, " +
                $"handlers={taskCount}, pumpAlive={pumpAlive}.";
            return WriteResponseAsync(stream, 200, "OK", message, ct);
        }

        private void StopFromBackgroundHandler()
        {
            lock (LifecycleGate)
            {
                StopCore(waitForHandlers: false, clearSessionToken: false);
            }
        }

        private bool TryStopStaleServer(Exception startError)
        {
            if (!IsAddressInUse(startError))
                return false;

            var token = SessionState.GetString(_sessionTokenKey, "");
            if (string.IsNullOrWhiteSpace(token))
                return false;

            try
            {
                var target = $"{AdminShutdownPath}?pid={Process.GetCurrentProcess().Id}&token={Uri.EscapeDataString(token)}";
                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] MCP Tool Gateway port {_port} is occupied; attempting token-protected stale shutdown.");
                }
                
                var response = InvokeLoopbackGet(target, StaleShutdownWaitMilliseconds);
                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] MCP Gateway stale shutdown response for port {_port}: {response}");
                }
                
                if (WaitUntilPortCanBind(TimeSpan.FromMilliseconds(StaleShutdownWaitMilliseconds)))
                    return true;

                Debug.LogWarning($"[DotCraft] MCP Gateway stale shutdown request completed but port {_port} is still occupied. {DescribePortOwner()}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] MCP Gateway stale shutdown attempt failed for port {_port} " + $"({ex.GetType().Name}): {ex.Message}. {DescribePortOwner()}");
                return false;
            }
        }

        private string InvokeLoopbackGet(string target, int timeoutMilliseconds)
        {
            using var client = new TcpClient();
            ConfigureAcceptedClient(client);
            var connect = client.BeginConnect(IPAddress.Loopback, _port, null, null);
            if (!connect.AsyncWaitHandle.WaitOne(timeoutMilliseconds))
                throw new TimeoutException("Timed out connecting to stale MCP Tool Gateway.");
            client.EndConnect(connect);

            using var stream = client.GetStream();
            stream.ReadTimeout = timeoutMilliseconds;
            stream.WriteTimeout = timeoutMilliseconds;
            var request =
                $"GET {target} HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{_port}\r\n" +
                "Connection: close\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);

            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var statusLine = reader.ReadLine();
            if (statusLine == null || !statusLine.Contains(" 200 "))
                throw new InvalidOperationException("Stale MCP Tool Gateway rejected shutdown.");
            return statusLine;
        }

        private bool WaitUntilPortCanBind(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            do
            {
                if (CanBindOnce())
                    return true;

                Thread.Sleep(50);
            } while (DateTime.UtcNow < deadline);

            return false;
        }

        private bool CanBindOnce()
        {
            TcpListener probe = null;
            Socket probeSocket = null;
            try
            {
                probe = new TcpListener(IPAddress.Loopback, _port);
                probeSocket = probe.Server;
                ConfigureListenerSocket(probeSocket);
                probe.Start();
                return true;
            }
            catch (SocketException ex) when (IsAddressInUse(ex))
            {
                return false;
            }
            finally
            {
                CloseListener(probe, probeSocket);
            }
        }

        private static void CloseListener(TcpListener listener, Socket listenerSocket)
        {
            try
            {
                listener?.Stop();
            }
            catch
            {
                // ignored
            }

            CloseSocket(listenerSocket);
        }

        private static void CloseSocket(Socket socket)
        {
            if (socket == null)
                return;

            try
            {
                socket.LingerState = new LingerOption(true, 0);
            }
            catch
            {
                // ignored
            }

            try
            {
                socket.Close(0);
            }
            catch
            {
                try
                {
                    socket.Close();
                }
                catch
                {
                }
            }

            try
            {
                socket.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        private static void CloseClient(TcpClient client)
        {
            if (client == null)
                return;

            ConfigureAcceptedClient(client);
            try
            {
                client.Client.Close(0);
            }
            catch
            {
                // ignored
            }

            try
            {
                client.Close();
            }
            catch
            {
                // ignored
            }

            try
            {
                client.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var text = query?.TrimStart('?') ?? "";
            if (string.IsNullOrEmpty(text))
                return result;

            foreach (var pair in text.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                    continue;
                var index = pair.IndexOf('=');
                var key = index < 0 ? pair : pair.Substring(0, index);
                var value = index < 0 ? "" : pair.Substring(index + 1);
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace("+", "%20"));
            }
            return result;
        }

        private static bool IsAddressInUse(Exception ex)
        {
            return ex is SocketException socketException
                   && socketException.SocketErrorCode == SocketError.AddressAlreadyInUse;
        }

        private bool HasSessionShutdownToken()
        {
            return !string.IsNullOrWhiteSpace(SessionState.GetString(_sessionTokenKey, ""));
        }

        private bool HasLocalResources()
        {
            lock (_gate)
            {
                return _isRunning || _listener != null || _listenerSocket != null || _pumpThread != null || _clients.Count > 0;
            }
        }

        internal static void ResetShutdownTokenForTests(int port)
        {
            ClearShutdownTokenForPort(port);
        }

        internal static string GetShutdownTokenForTests(int port)
        {
            return SessionState.GetString(SessionTokenPrefix + port, "");
        }

        private static void ClearShutdownTokenForPort(int port)
        {
            try
            {
                SessionState.SetString(SessionTokenPrefix + port, "");
            }
            catch
            {
            }
        }

        internal int ActiveClientCountForTests
        {
            get
            {
                lock (_gate)
                {
                    return _clients.Count;
                }
            }
        }

        private string BuildStartFailureMessage(Exception ex)
        {
            if (!IsAddressInUse(ex))
                return ex?.Message ?? "Unknown error.";

            var token = SessionState.GetString(_sessionTokenKey, "");
            return string.IsNullOrWhiteSpace(token)
                ? $"Port {_port} is already in use. Another Unity Editor or local process may already own the DotCraft MCP Gateway."
                : $"Port {_port} is already in use. DotCraft tried to stop a previously recorded MCP Gateway, but the port is still occupied; restart Unity or close another Unity Editor using this port.";
        }

        private void LogStartFailureDiagnostics(string operation, int attempts, Exception ex)
        {
            var exceptionType = ex?.GetType().Name ?? "none";
            var socketError = ex is SocketException socketException
                ? socketException.SocketErrorCode.ToString()
                : "none";
            Debug.LogWarning(
                $"[DotCraft] MCP Tool Gateway start diagnostics: operation={operation}, port={_port}, " +
                $"pid={Process.GetCurrentProcess().Id}, instance={_instanceId}, attempts={attempts}, " +
                $"exception={exceptionType}, socketError={socketError}. {DescribePortOwner()}");
        }

        private string DescribePortOwner()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return "Port owner diagnostics are only available on Windows.";

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    Arguments = "-ano -p tcp",
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(startInfo);
                if (process == null)
                    return "Port owner diagnostics unavailable: netstat did not start.";

                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(1000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // ignored
                    }

                    return "Port owner diagnostics timed out.";
                }

                var marker = ":" + _port;
                var lines = output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Contains(marker))
                    .Take(4)
                    .ToArray();

                return lines.Length == 0
                    ? "No netstat TCP owner entry was found for this port."
                    : "netstat: " + string.Join(" | ", lines);
            }
            catch (Exception ex)
            {
                return $"Port owner diagnostics failed ({ex.GetType().Name}): {ex.Message}";
            }
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            int status,
            string reason,
            string message,
            CancellationToken ct)
        {
            var body = BuildHtml(message);
            var bytes = Encoding.UTF8.GetBytes(body);
            var headers =
                $"HTTP/1.1 {status} {reason}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {bytes.Length}\r\n" +
                "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        }

        private static async Task WriteRawResponseAsync(
            NetworkStream stream,
            ToolGatewayHttpResponse response,
            CancellationToken ct)
        {
            var body = response?.Body ?? string.Empty;
            var bytes = Encoding.UTF8.GetBytes(body);
            var status = response?.Status ?? 500;
            var reason = response?.Reason ?? "Internal Server Error";
            var contentType = response == null ? "text/plain; charset=utf-8" : response.ContentType;
            var headers =
                $"HTTP/1.1 {status} {reason}\r\n";
            if (!string.IsNullOrWhiteSpace(contentType))
                headers += $"Content-Type: {contentType}\r\n";
            if (response?.Headers != null)
            {
                foreach (var header in response.Headers)
                    headers += $"{header.Key}: {header.Value}\r\n";
            }
            headers +=
                $"Content-Length: {bytes.Length}\r\n" +
                "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            if (bytes.Length > 0)
                await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        }

        private static string BuildHtml(string message)
        {
            var escaped = WebUtility.HtmlEncode(message ?? "");
            return "<!doctype html><html><head><meta charset=\"utf-8\"><title>dotcraft-unity</title></head>" +
                   "<body style=\"font-family:system-ui,sans-serif;margin:40px;line-height:1.5\">" +
                   "<h1>dotcraft-unity MCP Gateway</h1>" +
                   $"<p>{escaped}</p>" +
                   "<p>You can return to DotCraft.</p>" +
                   "</body></html>";
        }

        private void RecordServerWarning(string message)
        {
            lock (_gate)
            {
                _lastError = message;
            }
            Debug.LogWarning($"[DotCraft] MCP Tool Gateway error: {message}");
        }

        private string GetShutdownToken()
        {
            lock (_gate)
            {
                return _shutdownToken;
            }
        }

        private void RegisterCurrentServer()
        {
            lock (RegistryGate)
            {
                ServersByPort[_port] = this;
            }
        }

        private void UnregisterCurrentServer()
        {
            lock (RegistryGate)
            {
                if (ServersByPort.TryGetValue(_port, out var registered) && ReferenceEquals(registered, this))
                    ServersByPort.Remove(_port);
            }
        }

        private bool StopRegisteredServerForPort()
        {
            ToolGatewayLocalServer existing = null;
            lock (RegistryGate)
            {
                if (ServersByPort.TryGetValue(_port, out var registered) && !ReferenceEquals(registered, this))
                    existing = registered;
            }

            if (existing == null)
                return false;

            existing.StopCore(waitForHandlers: true, clearSessionToken: true);
            return true;
        }

        private void StopCore(bool waitForHandlers, bool clearSessionToken)
        {
            CancellationTokenSource cts;
            TcpClient[] clients;
            TcpListener listener;
            Socket listenerSocket;
            Thread pumpThread;
            Task[] tasks;
            bool wasRunning;
            var currentTaskId = Task.CurrentId;

            lock (_gate)
            {
                cts = _cts;
                clients = _clients.ToArray();
                listener = _listener;
                listenerSocket = _listenerSocket;
                pumpThread = _pumpThread;
                tasks = _clientTasks
                    .Where(task => !task.IsCompleted && (!currentTaskId.HasValue || task.Id != currentTaskId.Value))
                    .ToArray();
                wasRunning = _isRunning || listener != null || listenerSocket != null || pumpThread != null || clients.Length > 0;
                _clients.Clear();
                _cts = null;
                _isRunning = false;
                _listener = null;
                _listenerSocket = null;
                _pumpThread = null;
                _shutdownToken = null;
            }

            UnregisterCurrentServer();

            try
            {
                cts?.Cancel();
                CloseListener(listener, listenerSocket);
            }
            catch
            {
            }

            foreach (var client in clients)
                CloseClient(client);

            if (pumpThread != null && pumpThread != Thread.CurrentThread)
            {
                try
                {
                    pumpThread.Join(StopWaitMilliseconds);
                }
                catch
                {
                }
            }

            var allHandlersCompleted = tasks.Length == 0;
            if (waitForHandlers && tasks.Length > 0)
            {
                try
                {
                    allHandlersCompleted = Task.WaitAll(tasks, StopWaitMilliseconds);
                }
                catch
                {
                    allHandlersCompleted = false;
                }
            }

            if (allHandlersCompleted)
            {
                lock (_gate)
                {
                    _clientTasks.RemoveAll(task => task.IsCompleted);
                }
                cts?.Dispose();
            }
            else
            {
                lock (_gate)
                {
                    foreach (var task in tasks)
                        _clientTasks.Remove(task);
                    _clientTasks.RemoveAll(task => task.IsCompleted);
                }
            }

            if (clearSessionToken)
                ClearShutdownTokenForPort(_port);

            if (wasRunning && DotCraftSettings.Instance.VerboseLogging)
            {
                var portReleased = CanBindOnce();
                Debug.Log(
                    $"[DotCraft] MCP Tool Gateway stopped on port {_port} " +
                    $"(pid {Process.GetCurrentProcess().Id}, instance {_instanceId}, " +
                    $"handlersCompleted={allHandlersCompleted}, portReleased={portReleased}).");
            }
        }

        private void OnHostTeardown(object sender, EventArgs e)
        {
            lock (LifecycleGate)
            {
                StopCore(waitForHandlers: false, clearSessionToken: false);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            AppDomain.CurrentDomain.DomainUnload -= OnHostTeardown;
            AppDomain.CurrentDomain.ProcessExit -= OnHostTeardown;
            Stop();
        }
    }
}

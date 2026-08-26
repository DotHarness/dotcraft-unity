using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class UnityToolGatewayServer : IDisposable
    {
        private const int MaxHeaderBytes = 64 * 1024;
        private const int MaxBodyBytes = 4 * 1024 * 1024;
        private readonly object _gate = new();
        private readonly UnityToolGatewayHandler _handler;
        private readonly HashSet<TcpClient> _clients = new();
        private CancellationTokenSource _cancellation;
        private TcpListener _listener;
        private string _lastError;

        public UnityToolGatewayServer(UnityToolGatewayHandler handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool IsRunning { get; private set; }

        public int Port { get; private set; }

        public string LastError
        {
            get
            {
                lock (_gate)
                    return _lastError;
            }
        }

        public string Endpoint => IsRunning
            ? $"http://127.0.0.1:{Port}{UnityToolGatewayHandler.BasePath}"
            : string.Empty;

        public void Start()
        {
            lock (_gate)
            {
                if (IsRunning)
                    return;

                try
                {
                    _cancellation = new CancellationTokenSource();
                    _listener = new TcpListener(IPAddress.Loopback, 0);
                    _listener.Start();
                    Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                    IsRunning = true;
                    _lastError = null;
                    _ = Task.Run(() => AcceptLoopAsync(_listener, _cancellation.Token));
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    IsRunning = false;
                    Port = 0;
                    throw;
                }
            }
        }

        public void Stop()
        {
            CancellationTokenSource cancellation;
            TcpListener listener;
            TcpClient[] clients;
            lock (_gate)
            {
                cancellation = _cancellation;
                listener = _listener;
                clients = _clients.ToArray();
                _clients.Clear();
                _cancellation = null;
                _listener = null;
                IsRunning = false;
                Port = 0;
            }

            try
            {
                cancellation?.Cancel();
                listener?.Stop();
                foreach (var client in clients)
                    client.Dispose();
            }
            finally
            {
                cancellation?.Dispose();
            }
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        RecordError(ex.Message);
                    break;
                }

                lock (_gate)
                    _clients.Add(client);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IPAddress.IsLoopback(remote.Address))
                    return;

                using (client)
                using (var stream = client.GetStream())
                {
                    var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (request == null)
                    {
                        await WriteResponseAsync(
                            stream,
                            ToolGatewayHttpResponse.Error(400, "Bad Request", "InvalidRequest", "Missing HTTP request."),
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (!IsLoopbackHost(request.Headers)
                        || !IsAllowedOrigin(request.Headers))
                    {
                        await WriteResponseAsync(
                            stream,
                            ToolGatewayHttpResponse.Error(403, "Forbidden", "ForbiddenOrigin", "Only loopback requests are allowed."),
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var context = new ToolGatewayHttpRequestContext
                    {
                        Method = request.Method,
                        Target = request.Target,
                        Headers = request.Headers,
                        Body = request.Body
                    };
                    using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    using var disconnectMonitor = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var disconnectTask = MonitorClientDisconnectAsync(
                        client.Client,
                        disconnectMonitor.Token,
                        requestCancellation);
                    ToolGatewayHttpResponse response;
                    try
                    {
                        response = await _handler.HandleAsync(context, requestCancellation.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        disconnectMonitor.Cancel();
                        await disconnectTask.ConfigureAwait(false);
                    }
                    await WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
                }
            }
            // A client hanging up mid-request is ordinary and says nothing about gateway health.
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RecordError(ex.Message);
            }
            finally
            {
                lock (_gate)
                    _clients.Remove(client);
            }
        }

        private static async Task<ToolGatewayHttpRequest> ReadRequestAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var bytes = new MemoryStream();
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return null;
                bytes.Write(buffer, 0, read);
                if (bytes.Length > MaxHeaderBytes + MaxBodyBytes)
                    throw new InvalidDataException("HTTP request is too large.");
                headerEnd = FindHeaderEnd(bytes.GetBuffer(), (int)bytes.Length);
                if (headerEnd < 0 && bytes.Length > MaxHeaderBytes)
                    throw new InvalidDataException("HTTP headers are too large.");
            }

            var raw = bytes.ToArray();
            var headerText = Encoding.ASCII.GetString(raw, 0, headerEnd);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
                return null;

            var request = new ToolGatewayHttpRequest
            {
                Method = requestLine[0],
                Target = requestLine[1]
            };
            foreach (var line in lines.Skip(1))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;
                request.Headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }

            var contentLength = 0;
            if (request.Headers.TryGetValue("Content-Length", out var contentLengthText)
                && (!int.TryParse(contentLengthText, out contentLength)
                    || contentLength < 0
                    || contentLength > MaxBodyBytes))
            {
                throw new InvalidDataException("Invalid Content-Length.");
            }

            const int separatorLength = 4;
            var bodyOffset = headerEnd + separatorLength;
            var availableBodyBytes = raw.Length - bodyOffset;
            while (availableBodyBytes < contentLength)
            {
                var read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, contentLength - availableBodyBytes), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("HTTP body ended before Content-Length.");
                bytes.Write(buffer, 0, read);
                availableBodyBytes += read;
            }

            raw = bytes.ToArray();
            request.Body = contentLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(raw, bodyOffset, contentLength);
            return request;
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            ToolGatewayHttpResponse response,
            CancellationToken cancellationToken)
        {
            var bodyBytes = string.IsNullOrEmpty(response.Body)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(response.Body);
            var headers = new StringBuilder()
                .Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(response.Reason).Append("\r\n")
                .Append("Connection: close\r\n")
                .Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            if (!string.IsNullOrWhiteSpace(response.ContentType))
                headers.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
            headers.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken).ConfigureAwait(false);
            if (bodyBytes.Length > 0)
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length, cancellationToken).ConfigureAwait(false);
        }

        private static async Task MonitorClientDisconnectAsync(
            Socket socket,
            CancellationToken stopToken,
            CancellationTokenSource requestCancellation)
        {
            try
            {
                while (!stopToken.IsCancellationRequested)
                {
                    if (socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0)
                    {
                        requestCancellation.Cancel();
                        return;
                    }

                    await Task.Delay(50, stopToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                requestCancellation.Cancel();
            }
        }

        private static int FindHeaderEnd(byte[] bytes, int count)
        {
            for (var index = 0; index <= count - 4; index++)
            {
                if (bytes[index] == '\r' && bytes[index + 1] == '\n'
                    && bytes[index + 2] == '\r' && bytes[index + 3] == '\n')
                    return index;
            }
            return -1;
        }

        private static bool IsLoopbackHost(IReadOnlyDictionary<string, string> headers)
        {
            return headers.TryGetValue("Host", out var host)
                   && Uri.TryCreate("http://" + host, UriKind.Absolute, out var uri)
                   && uri.IsLoopback;
        }

        private static bool IsAllowedOrigin(IReadOnlyDictionary<string, string> headers)
        {
            if (!headers.TryGetValue("Origin", out var origin) || string.IsNullOrWhiteSpace(origin))
                return true;
            return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
        }

        private void RecordError(string message)
        {
            lock (_gate)
                _lastError = OneLine(message);
        }

        /// <summary>Mono pads Winsock error text with NULs, which would stretch every status surface.</summary>
        private static string OneLine(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            var builder = new StringBuilder(message.Length);
            foreach (var character in message)
            {
                if (character == ' ')
                    continue;

                var normalized = char.IsControl(character) ? ' ' : character;
                if (normalized == ' ' && (builder.Length == 0 || builder[builder.Length - 1] == ' '))
                    continue;

                builder.Append(normalized);
            }

            return builder.ToString().TrimEnd();
        }

        private sealed class ToolGatewayHttpRequest
        {
            public string Method { get; set; }

            public string Target { get; set; }

            public Dictionary<string, string> Headers { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public string Body { get; set; } = string.Empty;
        }
    }
}

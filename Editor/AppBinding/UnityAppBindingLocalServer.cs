using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DotCraft.Editor.AppBinding
{
    internal sealed class UnityAppBindingHandoff
    {
        public string Operation { get; set; }
        public string AppId { get; set; }
        public string RequestId { get; set; }
        public string RequestToken { get; set; }
        public string Endpoint { get; set; }
        public string RawUrl { get; set; }
    }

    internal sealed class UnityAppBindingLocalServer : IDisposable
    {
        private readonly Func<UnityAppBindingHandoff, CancellationToken, Task<string>> _handler;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoop;

        public UnityAppBindingLocalServer(Func<UnityAppBindingHandoff, CancellationToken, Task<string>> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool IsRunning { get; private set; }

        public string LastError { get; private set; }

        public string ListenUrl => $"http://127.0.0.1:{UnityAppBindingConstants.LocalServerPort}/dotcraft/";

        public void Start()
        {
            if (IsRunning)
                return;

            Stop();
            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, UnityAppBindingConstants.LocalServerPort);
                _listener.Start();
                IsRunning = true;
                LastError = null;
                _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                IsRunning = false;
                LastError = ex.Message;
                Debug.LogError($"[DotCraft] App Binding local server failed to start: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch
            {
            }
            finally
            {
                IsRunning = false;
                _listener = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, ct), ct);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        LastError = ex.Message;
                        Debug.LogWarning($"[DotCraft] App Binding local server error: {ex.Message}");
                    }
                    client?.Dispose();
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen: true))
            {
                try
                {
                    var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(requestLine))
                    {
                        await WriteResponseAsync(stream, 400, "Bad Request", "Missing request line.", ct).ConfigureAwait(false);
                        return;
                    }

                    string header;
                    do
                    {
                        header = await reader.ReadLineAsync().ConfigureAwait(false);
                    } while (!string.IsNullOrEmpty(header));

                    var parts = requestLine.Split(' ');
                    if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteResponseAsync(stream, 405, "Method Not Allowed", "Only GET is supported.", ct).ConfigureAwait(false);
                        return;
                    }

                    var handoff = ParseHandoff(parts[1]);
                    var message = await _handler(handoff, ct).ConfigureAwait(false);
                    await WriteResponseAsync(stream, 200, "OK", message, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await WriteResponseAsync(stream, 500, "Internal Server Error", ex.Message, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }

        internal static UnityAppBindingHandoff ParseHandoff(string target)
        {
            var uri = new Uri("http://127.0.0.1" + target, UriKind.Absolute);
            var path = uri.AbsolutePath.Trim('/');
            var segments = path.Split('/');
            if (segments.Length != 2
                || !string.Equals(segments[0], "dotcraft", StringComparison.Ordinal)
                || (segments[1] != "connect" && segments[1] != "bind"))
            {
                throw new InvalidOperationException("Unsupported App Binding handoff path.");
            }

            var query = ParseQuery(uri.Query);
            return new UnityAppBindingHandoff
            {
                Operation = segments[1],
                AppId = GetRequired(query, "app"),
                RequestId = GetRequired(query, "request"),
                RequestToken = GetRequired(query, "token"),
                Endpoint = GetRequired(query, "endpoint"),
                RawUrl = uri.ToString()
            };
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

        private static string GetRequired(Dictionary<string, string> query, string key)
        {
            if (!query.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Missing App Binding handoff parameter '{key}'.");
            return value;
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

        private static string BuildHtml(string message)
        {
            var escaped = WebUtility.HtmlEncode(message ?? "");
            return "<!doctype html><html><head><meta charset=\"utf-8\"><title>dotcraft-unity</title></head>" +
                   "<body style=\"font-family:system-ui,sans-serif;margin:40px;line-height:1.5\">" +
                   "<h1>dotcraft-unity App Binding</h1>" +
                   $"<p>{escaped}</p>" +
                   "<p>You can return to DotCraft.</p>" +
                   "</body></html>";
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

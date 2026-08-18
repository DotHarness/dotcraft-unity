using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class ToolGatewayHttpRequestContext
    {
        public string Method { get; set; }

        public string Target { get; set; }

        public IReadOnlyDictionary<string, string> Headers { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Body { get; set; } = string.Empty;

        public string GetHeader(string name)
        {
            if (string.IsNullOrEmpty(name) || Headers == null)
                return null;

            return Headers.TryGetValue(name, out var value) ? value : null;
        }
    }

    internal sealed class ToolGatewayToolSpec
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public JObject InputSchema { get; set; }
    }

    internal sealed class ToolGatewayResult
    {
        public bool Success { get; set; }

        public string Name { get; set; }

        public object StructuredResult { get; set; }

        public string Text { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public long DurationMs { get; set; }

        public static ToolGatewayResult Ok(string name, object structuredResult, string text, long durationMs)
        {
            return new ToolGatewayResult
            {
                Success = true,
                Name = name,
                StructuredResult = structuredResult,
                Text = text,
                DurationMs = durationMs
            };
        }

        public static ToolGatewayResult Failed(
            string name,
            string errorCode,
            string errorMessage,
            long durationMs,
            object structuredResult = null)
        {
            return new ToolGatewayResult
            {
                Success = false,
                Name = name,
                StructuredResult = structuredResult,
                Text = string.IsNullOrWhiteSpace(errorMessage)
                    ? $"{name} failed."
                    : $"{name} failed: {errorMessage}",
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                DurationMs = durationMs
            };
        }
    }

    internal sealed class ToolGatewayHttpResponse
    {
        public int Status { get; set; }

        public string Reason { get; set; }

        public string ContentType { get; set; }

        public string Body { get; set; }

        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static ToolGatewayHttpResponse Json(object body, int status = 200, string reason = "OK")
        {
            return new ToolGatewayHttpResponse
            {
                Status = status,
                Reason = reason,
                ContentType = "application/json; charset=utf-8",
                Body = DotCraft.Editor.Protocol.DotCraftJson.Serialize(body)
            };
        }

        public static ToolGatewayHttpResponse Text(
            string body,
            string contentType,
            int status = 200,
            string reason = "OK")
        {
            return new ToolGatewayHttpResponse
            {
                Status = status,
                Reason = reason,
                ContentType = contentType,
                Body = body ?? string.Empty
            };
        }

        public static ToolGatewayHttpResponse NoBody(int status, string reason)
        {
            return new ToolGatewayHttpResponse
            {
                Status = status,
                Reason = reason,
                Body = null
            };
        }

        public static ToolGatewayHttpResponse Accepted()
        {
            return NoBody(202, "Accepted");
        }

        public static ToolGatewayHttpResponse Error(int status, string reason, string message)
        {
            return Json(new
            {
                success = false,
                errorCode = reason.Replace(" ", string.Empty),
                errorMessage = message
            }, status, reason);
        }

        public ToolGatewayHttpResponse WithHeader(string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(name) && value != null)
                Headers[name] = value;
            return this;
        }
    }

    internal sealed class ToolGatewayMcpSession
    {
        public string Id { get; set; }

        public string ProtocolVersion { get; set; }

        public bool Initialized { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime LastSeenUtc { get; set; }

        [JsonIgnore]
        public Dictionary<string, ToolGatewayMcpRequestTracker> InFlightRequests { get; } =
            new(StringComparer.Ordinal);
    }

    internal sealed class ToolGatewayMcpRequestTracker : IDisposable
    {
        private readonly CancellationTokenSource _clientCancellation;
        private readonly CancellationTokenSource _linkedCancellation;

        public ToolGatewayMcpRequestTracker(CancellationToken parentToken)
        {
            _clientCancellation = new CancellationTokenSource();
            _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                parentToken,
                _clientCancellation.Token);
        }

        public CancellationToken Token => _linkedCancellation.Token;

        public bool CancelledByClient { get; private set; }

        public void CancelFromClient()
        {
            CancelledByClient = true;
            _clientCancellation.Cancel();
        }

        public void Dispose()
        {
            _linkedCancellation.Dispose();
            _clientCancellation.Dispose();
        }
    }

    internal static class ToolGatewayMcpSessionStore
    {
        private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromHours(2);
        private static readonly object Gate = new();
        private static readonly Dictionary<string, ToolGatewayMcpSession> Sessions = new(StringComparer.Ordinal);
        private static readonly string StorePath = Path.Combine(
            Path.GetTempPath(),
            $"dotcraft-unity-mcp-sessions-{Process.GetCurrentProcess().Id}.json");
        private static Func<DateTime> _utcNow = () => DateTime.UtcNow;
        private static TimeSpan _idleTimeout = DefaultIdleTimeout;

        static ToolGatewayMcpSessionStore()
        {
            LoadPersistedSessions();
        }

        public static ToolGatewayMcpSession Create(string protocolVersion)
        {
            var now = UtcNow;
            var session = new ToolGatewayMcpSession
            {
                Id = CreateSessionId(),
                ProtocolVersion = protocolVersion,
                CreatedUtc = now,
                LastSeenUtc = now
            };

            lock (Gate)
            {
                Sessions[session.Id] = session;
                SaveLocked();
            }

            return session;
        }

        public static bool TryGet(string sessionId, out ToolGatewayMcpSession session)
        {
            session = null;
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            lock (Gate)
            {
                if (!Sessions.TryGetValue(sessionId, out session))
                    return false;

                if (IsExpired(session))
                {
                    RemoveLocked(sessionId);
                    SaveLocked();
                    session = null;
                    return false;
                }

                session.LastSeenUtc = UtcNow;
                SaveLocked();
                return true;
            }
        }

        public static bool Contains(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            lock (Gate)
            {
                if (!Sessions.TryGetValue(sessionId, out var session))
                    return false;

                if (IsExpired(session))
                {
                    RemoveLocked(sessionId);
                    SaveLocked();
                    return false;
                }

                return true;
            }
        }

        public static bool MarkInitialized(string sessionId, out ToolGatewayMcpSession session)
        {
            if (!TryGet(sessionId, out session))
                return false;

            lock (Gate)
            {
                session.Initialized = true;
                session.LastSeenUtc = UtcNow;
                SaveLocked();
            }

            return true;
        }

        public static bool Remove(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            lock (Gate)
            {
                var removed = RemoveLocked(sessionId);
                if (removed)
                    SaveLocked();
                return removed;
            }
        }

        public static bool TryTrackRequest(
            string sessionId,
            string requestKey,
            CancellationToken parentToken,
            out ToolGatewayMcpRequestTracker tracker)
        {
            tracker = null;
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(requestKey))
                return false;

            lock (Gate)
            {
                if (!Sessions.TryGetValue(sessionId, out var session))
                    return false;

                if (IsExpired(session))
                {
                    RemoveLocked(sessionId);
                    SaveLocked();
                    return false;
                }

                if (session.InFlightRequests.TryGetValue(requestKey, out var existing))
                {
                    existing.CancelFromClient();
                    existing.Dispose();
                }

                tracker = new ToolGatewayMcpRequestTracker(parentToken);
                session.InFlightRequests[requestKey] = tracker;
                session.LastSeenUtc = UtcNow;
                SaveLocked();
                return true;
            }
        }

        public static bool CancelRequest(string sessionId, string requestKey)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(requestKey))
                return false;

            lock (Gate)
            {
                if (!Sessions.TryGetValue(sessionId, out var session))
                    return false;

                if (IsExpired(session))
                {
                    RemoveLocked(sessionId);
                    SaveLocked();
                    return false;
                }

                if (!session.InFlightRequests.TryGetValue(requestKey, out var tracker))
                    return false;

                tracker.CancelFromClient();
                session.LastSeenUtc = UtcNow;
                SaveLocked();
                return true;
            }
        }

        public static void CompleteRequest(
            string sessionId,
            string requestKey,
            ToolGatewayMcpRequestTracker tracker)
        {
            if (tracker == null)
                return;

            lock (Gate)
            {
                if (Sessions.TryGetValue(sessionId ?? string.Empty, out var session)
                    && session.InFlightRequests.TryGetValue(requestKey ?? string.Empty, out var existing)
                    && ReferenceEquals(existing, tracker))
                {
                    session.InFlightRequests.Remove(requestKey);
                }
            }

            tracker.Dispose();
        }

        internal static void ResetForTests()
        {
            lock (Gate)
            {
                DisposeTrackersLocked();
                Sessions.Clear();
                _utcNow = () => DateTime.UtcNow;
                _idleTimeout = DefaultIdleTimeout;
                TryDeleteStoreFile();
            }
        }

        internal static void ReloadFromPersistentStoreForTests()
        {
            lock (Gate)
            {
                DisposeTrackersLocked();
                Sessions.Clear();
                LoadPersistedSessions();
            }
        }

        internal static void SetClockForTests(Func<DateTime> utcNow)
        {
            lock (Gate)
                _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal static void SetIdleTimeoutForTests(TimeSpan idleTimeout)
        {
            lock (Gate)
                _idleTimeout = idleTimeout <= TimeSpan.Zero ? DefaultIdleTimeout : idleTimeout;
        }

        private static string CreateSessionId()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static void LoadPersistedSessions()
        {
            if (!File.Exists(StorePath))
                return;

            string raw;
            try
            {
                raw = File.ReadAllText(StorePath);
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(raw))
                return;

            try
            {
                var array = JArray.Parse(raw);
                foreach (var item in array.OfType<JObject>())
                {
                    var sessionId = item.Value<string>("id");
                    if (!IsVisibleAscii(sessionId))
                        continue;

                    var session = new ToolGatewayMcpSession
                    {
                        Id = sessionId,
                        ProtocolVersion = item.Value<string>("protocolVersion"),
                        Initialized = item.Value<bool?>("initialized") == true,
                        CreatedUtc = FromTicks(item.Value<long?>("createdUtcTicks")),
                        LastSeenUtc = FromTicks(item.Value<long?>("lastSeenUtcTicks"))
                    };

                    if (string.IsNullOrWhiteSpace(session.ProtocolVersion) || IsExpired(session))
                        continue;

                    Sessions[sessionId] = session;
                }

                SaveLocked();
            }
            catch
            {
                Sessions.Clear();
                TryDeleteStoreFile();
            }
        }

        private static DateTime UtcNow => _utcNow();

        private static bool IsExpired(ToolGatewayMcpSession session)
        {
            if (session == null)
                return true;

            var lastSeen = session.LastSeenUtc == default
                ? session.CreatedUtc
                : session.LastSeenUtc;

            return lastSeen != default && UtcNow - lastSeen > _idleTimeout;
        }

        private static bool RemoveLocked(string sessionId)
        {
            if (!Sessions.TryGetValue(sessionId, out var session))
                return false;

            foreach (var tracker in session.InFlightRequests.Values)
            {
                tracker.CancelFromClient();
                tracker.Dispose();
            }

            session.InFlightRequests.Clear();
            return Sessions.Remove(sessionId);
        }

        private static void DisposeTrackersLocked()
        {
            foreach (var session in Sessions.Values)
            {
                foreach (var tracker in session.InFlightRequests.Values)
                    tracker.Dispose();

                session.InFlightRequests.Clear();
            }
        }

        private static void SaveLocked()
        {
            if (Sessions.Count == 0)
            {
                TryDeleteStoreFile();
                return;
            }

            var array = new JArray(Sessions.Values.Select(session => new JObject
            {
                ["id"] = session.Id,
                ["protocolVersion"] = session.ProtocolVersion,
                ["initialized"] = session.Initialized,
                ["createdUtcTicks"] = session.CreatedUtc.Ticks,
                ["lastSeenUtcTicks"] = session.LastSeenUtc.Ticks
            }));

            try
            {
                File.WriteAllText(StorePath, array.ToString(Formatting.None));
            }
            catch
            {
                // Persistence only supports domain-reload recovery; in-memory sessions still work.
            }
        }

        private static void TryDeleteStoreFile()
        {
            try
            {
                if (File.Exists(StorePath))
                    File.Delete(StorePath);
            }
            catch
            {
            }
        }

        private static DateTime FromTicks(long? ticks)
        {
            return ticks.HasValue && ticks.Value > 0
                ? new DateTime(ticks.Value, DateTimeKind.Utc)
                : UtcNow;
        }

        private static bool IsVisibleAscii(string value)
        {
            return !string.IsNullOrEmpty(value)
                   && value.All(ch => ch >= '!' && ch <= '~');
        }
    }
}

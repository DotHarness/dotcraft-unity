using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class McpClientSession
    {
        public string SessionId { get; set; } = string.Empty;

        public string ClientName { get; set; } = string.Empty;

        public string ClientTitle { get; set; } = string.Empty;

        public string ClientVersion { get; set; } = string.Empty;

        public int ProcessId { get; set; }

        public DateTime ConnectedAtUtc { get; set; }

        public DateTime LastSeenUtc { get; set; }

        /// <summary>
        /// Protocol revisions from 2026-07-28 onward have no initialize handshake, so a client may
        /// never identify itself.
        /// </summary>
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ClientTitle))
                    return ClientTitle;
                return string.IsNullOrWhiteSpace(ClientName) ? "Unknown client" : ClientName;
            }
        }

        public McpClientSession Clone() => (McpClientSession)MemberwiseClone();
    }

    /// <summary>
    /// Tracks which MCP clients are attached, fed by presence heartbeats.
    ///
    /// Mutations arrive on TcpListener threads while reads come from the Editor main thread, so this
    /// type touches no Unity API.
    /// </summary>
    internal sealed class McpClientSessionRegistry
    {
        public const string StateClosing = "closing";

        internal const int MaxStringLength = 128;
        internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(45);
        internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

        private static readonly Lazy<McpClientSessionRegistry> LazyInstance =
            new(() => new McpClientSessionRegistry());

        private readonly object _gate = new();
        private readonly Dictionary<string, McpClientSession> _sessions = new(StringComparer.Ordinal);

        public static McpClientSessionRegistry Instance => LazyInstance.Value;

        public int ActiveCount => Snapshot(DateTime.UtcNow).Count;

        /// <returns>True only when the change is visible in the UI; a last-seen refresh returns false.</returns>
        public bool Upsert(McpClientSession update, DateTime utcNow)
        {
            if (update == null || string.IsNullOrEmpty(update.SessionId))
                return false;

            lock (_gate)
            {
                if (_sessions.TryGetValue(update.SessionId, out var existing))
                {
                    var changed =
                        !string.Equals(existing.ClientName, update.ClientName, StringComparison.Ordinal)
                        || !string.Equals(existing.ClientTitle, update.ClientTitle, StringComparison.Ordinal)
                        || !string.Equals(existing.ClientVersion, update.ClientVersion, StringComparison.Ordinal)
                        || !IsLive(existing, utcNow);

                    existing.ClientName = update.ClientName;
                    existing.ClientTitle = update.ClientTitle;
                    existing.ClientVersion = update.ClientVersion;
                    existing.ProcessId = update.ProcessId;
                    existing.LastSeenUtc = utcNow;
                    return changed;
                }

                update.ConnectedAtUtc = utcNow;
                update.LastSeenUtc = utcNow;
                _sessions[update.SessionId] = update;
                return true;
            }
        }

        public bool Remove(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            lock (_gate)
                return _sessions.Remove(sessionId);
        }

        /// <summary>Refreshes activity from a tool call, between heartbeats.</summary>
        public void Touch(string sessionId, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(sessionId))
                return;

            lock (_gate)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                    session.LastSeenUtc = utcNow;
            }
        }

        /// <summary>Drops expired sessions and sessions whose gateway process is gone.</summary>
        public bool Sweep(DateTime utcNow)
        {
            lock (_gate)
            {
                var dead = _sessions.Values
                    .Where(session => !IsLive(session, utcNow) || !IsProcessAlive(session.ProcessId))
                    .Select(session => session.SessionId)
                    .ToArray();

                foreach (var sessionId in dead)
                    _sessions.Remove(sessionId);

                return dead.Length > 0;
            }
        }

        /// <summary>Live sessions, most recent activity first. Expiry applies here too, so no timer is required.</summary>
        public IReadOnlyList<McpClientSession> Snapshot(DateTime utcNow)
        {
            lock (_gate)
            {
                return _sessions.Values
                    .Where(session => IsLive(session, utcNow))
                    .OrderByDescending(session => session.LastSeenUtc)
                    .ThenBy(session => session.SessionId, StringComparer.Ordinal)
                    .Select(session => session.Clone())
                    .ToArray();
            }
        }

        public void Clear()
        {
            lock (_gate)
                _sessions.Clear();
        }

        internal string Serialize()
        {
            lock (_gate)
                return DotCraftJson.Serialize(_sessions.Values.ToArray());
        }

        internal void Restore(string json, DateTime utcNow)
        {
            var restored = DotCraftJson.Deserialize<McpClientSession[]>(json);
            if (restored == null)
                return;

            lock (_gate)
            {
                foreach (var session in restored)
                {
                    if (session == null || string.IsNullOrEmpty(session.SessionId))
                        continue;

                    session.ConnectedAtUtc = DateTime.SpecifyKind(session.ConnectedAtUtc, DateTimeKind.Utc);
                    session.LastSeenUtc = DateTime.SpecifyKind(session.LastSeenUtc, DateTimeKind.Utc);

                    if (IsLive(session, utcNow) && IsProcessAlive(session.ProcessId))
                        _sessions[session.SessionId] = session;
                }
            }
        }

        private static bool IsLive(McpClientSession session, DateTime utcNow) =>
            utcNow - session.LastSeenUtc < Ttl;

        private static bool IsProcessAlive(int processId)
        {
            if (processId <= 0)
                return true;

            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Client strings are truncated and stripped so they cannot wreck the dropdown layout.</summary>
        internal static bool TryParse(JObject body, out McpClientSession session, out bool isClosing)
        {
            session = null;
            isClosing = false;

            var sessionId = body?.Value<string>("sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            var client = body["client"] as JObject;
            isClosing = string.Equals(body.Value<string>("state"), StateClosing, StringComparison.Ordinal);
            session = new McpClientSession
            {
                SessionId = Sanitize(sessionId),
                ClientName = Sanitize(client?.Value<string>("name")),
                ClientTitle = Sanitize(client?.Value<string>("title")),
                ClientVersion = Sanitize(client?.Value<string>("version")),
                ProcessId = body.Value<int?>("processId") ?? 0
            };
            return true;
        }

        internal static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder();
            foreach (var character in value)
            {
                if (builder.Length >= MaxStringLength)
                    break;
                if (!char.IsControl(character))
                    builder.Append(character);
            }

            return builder.ToString().Trim();
        }
    }
}

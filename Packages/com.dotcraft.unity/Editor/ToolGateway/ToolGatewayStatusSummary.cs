using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotCraft.Editor.ToolGateway
{
    internal enum ToolGatewayIndicatorState
    {
        Hidden,
        Idle,
        Active,
        Warning
    }

    internal sealed class McpClientRow
    {
        public string SessionId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string ActivityText { get; set; } = string.Empty;
    }

    /// <summary>
    /// View model for the status-bar indicator and its connections dropdown.
    /// </summary>
    internal sealed class ToolGatewayStatusSummary
    {
        private ToolGatewayStatusSummary(
            bool isRunning,
            string lastError,
            AgentPresenceSnapshot agent,
            IReadOnlyList<McpClientRow> clients)
        {
            IsRunning = isRunning;
            LastError = lastError ?? string.Empty;
            Agent = agent ?? AgentPresenceSnapshot.Absent;
            Clients = clients ?? Array.Empty<McpClientRow>();

            IsVisible = IsRunning || Agent.IsActive;
            Indicator = ResolveIndicator();
            HeaderSubtitle = ResolveHeaderSubtitle();
            Tooltip = ResolveTooltip();
            StructureKey = ResolveStructureKey();
        }

        public static ToolGatewayStatusSummary Empty { get; } =
            new(false, string.Empty, AgentPresenceSnapshot.Absent, Array.Empty<McpClientRow>());

        public bool IsVisible { get; }

        public bool IsRunning { get; }

        public string LastError { get; }

        public AgentPresenceSnapshot Agent { get; }

        public IReadOnlyList<McpClientRow> Clients { get; }

        public string HeaderSubtitle { get; }

        public string Tooltip { get; }

        public ToolGatewayIndicatorState Indicator { get; }

        /// <summary>
        /// Changes only when the dropdown needs rebuilding, so the tick can refresh text without
        /// touching the visual tree.
        /// </summary>
        public string StructureKey { get; }

        public static ToolGatewayStatusSummary FromState(
            bool isRunning,
            string lastError,
            AgentPresenceSnapshot agent,
            IReadOnlyList<McpClientSession> sessions,
            DateTime nowUtc)
        {
            var rows = (sessions ?? Array.Empty<McpClientSession>())
                .Where(session => session != null)
                .OrderByDescending(session => session.LastSeenUtc)
                .ThenBy(session => session.SessionId, StringComparer.Ordinal)
                .Select(session => new McpClientRow
                {
                    SessionId = session.SessionId,
                    Name = string.IsNullOrWhiteSpace(session.ClientVersion)
                        ? session.DisplayName
                        : $"{session.DisplayName} {session.ClientVersion}",
                    ActivityText = ToolGatewayRelativeTime.Since(session.LastSeenUtc, nowUtc)
                })
                .ToArray();

            return new ToolGatewayStatusSummary(isRunning, lastError, agent, rows);
        }

        private ToolGatewayIndicatorState ResolveIndicator()
        {
            if (!IsVisible)
                return ToolGatewayIndicatorState.Hidden;
            if (!string.IsNullOrWhiteSpace(LastError))
                return ToolGatewayIndicatorState.Warning;
            if (Clients.Count > 0 || Agent.IsConnected)
                return ToolGatewayIndicatorState.Active;
            return ToolGatewayIndicatorState.Idle;
        }

        private string ResolveHeaderSubtitle()
        {
            if (!IsRunning)
                return "Gateway is stopped.";

            var parts = new List<string>();
            if (Agent.IsActive)
                parts.Add("1 agent");
            if (Clients.Count > 0)
                parts.Add(Clients.Count == 1 ? "1 client" : $"{Clients.Count} clients");

            return parts.Count == 0
                ? "Gateway running · waiting for clients"
                : "Gateway running · " + string.Join(", ", parts);
        }

        private string ResolveTooltip()
        {
            if (!IsVisible)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(LastError))
                return $"DotCraft Unity: gateway error — {LastError}";
            return $"DotCraft Unity — {HeaderSubtitle} Click for details.";
        }

        private string ResolveStructureKey()
        {
            var builder = new StringBuilder();
            builder.Append(IsRunning ? '1' : '0');
            builder.Append(string.IsNullOrWhiteSpace(LastError) ? '0' : '1');
            builder.Append(Agent.IsWindowOpen ? '1' : '0');
            builder.Append(Agent.IsConnecting ? '1' : '0');
            builder.Append(Agent.IsConnected ? '1' : '0');
            builder.Append('|').Append(Agent.Name).Append('|').Append(Agent.Version);

            foreach (var client in Clients)
                builder.Append('|').Append(client.SessionId).Append(client.Name);

            return builder.ToString();
        }
    }
}

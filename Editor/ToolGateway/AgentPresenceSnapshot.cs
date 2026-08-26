using System;
using DotCraft.Editor.Connection;
using DotCraft.Editor.Protocol;

namespace DotCraft.Editor.ToolGateway
{
    /// <summary>
    /// Immutable view of the Assistant agent connection. Value equality stops a repeated status
    /// update from repainting.
    /// </summary>
    internal sealed class AgentPresenceSnapshot : IEquatable<AgentPresenceSnapshot>
    {
        public const string DefaultAgentName = "DotCraft";

        private AgentPresenceSnapshot(
            bool isWindowOpen,
            bool isConnecting,
            bool isConnected,
            string name,
            string version,
            DateTime? connectedAtUtc)
        {
            IsWindowOpen = isWindowOpen;
            IsConnecting = isConnecting;
            IsConnected = isConnected;
            Name = name ?? string.Empty;
            Version = version ?? string.Empty;
            ConnectedAtUtc = connectedAtUtc;
        }

        public bool IsWindowOpen { get; }

        public bool IsConnecting { get; }

        public bool IsConnected { get; }

        public string Name { get; }

        public string Version { get; }

        public DateTime? ConnectedAtUtc { get; }

        public bool IsActive => IsConnected || IsConnecting;

        public static AgentPresenceSnapshot Absent { get; } = new(false, false, false, null, null, null);

        public static AgentPresenceSnapshot WindowOpen() => new(true, false, false, null, null, null);

        public static AgentPresenceSnapshot Connecting() =>
            new(true, true, false, DefaultAgentName, null, null);

        public static AgentPresenceSnapshot Connected(string name, string version, DateTime? connectedAtUtc) =>
            new(true, false, true, string.IsNullOrWhiteSpace(name) ? DefaultAgentName : name, version, connectedAtUtc);

        public static AgentPresenceSnapshot FromClient(AcpClient client, DateTime? connectedAtUtc) =>
            Connected(ResolveName(client?.AgentInfo), client?.AgentInfo?.Version, connectedAtUtc);

        internal static string ResolveName(AgentInfo info)
        {
            if (!string.IsNullOrWhiteSpace(info?.Title))
                return info.Title;
            if (!string.IsNullOrWhiteSpace(info?.Name))
                return info.Name;
            return DefaultAgentName;
        }

        public bool Equals(AgentPresenceSnapshot other)
        {
            if (other is null)
                return false;

            return IsWindowOpen == other.IsWindowOpen
                   && IsConnecting == other.IsConnecting
                   && IsConnected == other.IsConnected
                   && string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && string.Equals(Version, other.Version, StringComparison.Ordinal)
                   && Nullable.Equals(ConnectedAtUtc, other.ConnectedAtUtc);
        }

        public override bool Equals(object obj) => Equals(obj as AgentPresenceSnapshot);

        public override int GetHashCode() =>
            (IsWindowOpen, IsConnecting, IsConnected, Name, Version, ConnectedAtUtc).GetHashCode();
    }
}

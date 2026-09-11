using System;
using DotCraft.Editor.Extensions;

namespace DotCraft.Editor.ToolGateway
{
    /// <summary>
    /// Publishes the Assistant agent connection to the status bar, which cannot reach into the
    /// window that owns the <c>AcpClient</c>.
    /// </summary>
    internal static class DotCraftAgentPresence
    {
        private static object s_owner;

        public static AgentPresenceSnapshot Current { get; private set; } = AgentPresenceSnapshot.Absent;

        /// <summary>Raised on the Unity main thread whenever <see cref="Current"/> changes.</summary>
        public static event Action Changed;

        /// <summary>An actively connected owner wins, so a second window cannot clobber a live one.</summary>
        public static void Publish(object owner, AgentPresenceSnapshot snapshot)
        {
            if (owner == null || snapshot == null)
                return;

            if (snapshot.IsActive)
            {
                s_owner = owner;
            }
            else if (s_owner != null && !ReferenceEquals(s_owner, owner))
            {
                return;
            }
            else
            {
                s_owner = snapshot.IsWindowOpen ? owner : null;
            }

            Apply(snapshot);
        }

        /// <summary>A no-op when another owner holds the slot.</summary>
        public static void Clear(object owner)
        {
            if (owner == null)
                return;
            if (s_owner != null && !ReferenceEquals(s_owner, owner))
                return;

            s_owner = null;
            Apply(AgentPresenceSnapshot.Absent);
        }

        internal static void ResetForTests()
        {
            s_owner = null;
            Current = AgentPresenceSnapshot.Absent;
            Changed = null;
        }

        private static void Apply(AgentPresenceSnapshot snapshot)
        {
            if (snapshot.Equals(Current))
                return;

            Current = snapshot;
            MainThreadDispatcher.RunOrEnqueue(() => Changed?.Invoke());
        }
    }
}

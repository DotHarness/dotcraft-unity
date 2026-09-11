using System;

namespace DotCraft.Editor.ToolGateway
{
    /// <summary>Reads the live singletons and the clock, keeping FromState a pure function.</summary>
    internal static class ToolGatewayStatusSource
    {
        public static ToolGatewayStatusSummary Capture()
        {
            var runtime = UnityToolGatewayRuntime.Instance;
            return ToolGatewayStatusSummary.FromState(
                runtime.IsRunning,
                runtime.LastError,
                DotCraftAgentPresence.Current,
                runtime.ClientSessions,
                DateTime.UtcNow);
        }
    }
}

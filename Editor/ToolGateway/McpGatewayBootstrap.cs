using DotCraft.Editor.Settings;
using DotCraft.Editor.Extensions;
using UnityEditor;

namespace DotCraft.Editor.ToolGateway
{
    [InitializeOnLoad]
    internal static class McpGatewayBootstrap
    {
        static McpGatewayBootstrap()
        {
            EditorApplication.delayCall += () =>
            {
                MainThreadDispatcher.RunOrEnqueue(() => { });
                McpGatewayRuntime.Instance.ApplySettings();
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => McpGatewayRuntime.Instance.Shutdown();
            EditorApplication.quitting += () => McpGatewayRuntime.Instance.Shutdown();
        }

        public static void ApplySettings()
        {
            McpGatewayRuntime.Instance.ApplySettings();
        }
    }
}

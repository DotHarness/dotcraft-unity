using DotCraft.Editor.Extensions;
using UnityEditor;

namespace DotCraft.Editor.ToolGateway
{
    [InitializeOnLoad]
    internal static class UnityToolGatewayBootstrap
    {
        static UnityToolGatewayBootstrap()
        {
            EditorApplication.delayCall += () =>
            {
                MainThreadDispatcher.RunOrEnqueue(() => { });
                UnityToolGatewayRuntime.Instance.ApplySettings();
            };
            AssemblyReloadEvents.beforeAssemblyReload += UnityToolGatewayRuntime.Instance.Shutdown;
            EditorApplication.quitting += UnityToolGatewayRuntime.Instance.Shutdown;
        }

        public static void ApplySettings()
        {
            UnityToolGatewayRuntime.Instance.ApplySettings();
        }
    }
}

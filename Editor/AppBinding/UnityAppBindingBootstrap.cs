using DotCraft.Editor.Settings;
using DotCraft.Editor.Extensions;
using UnityEditor;

namespace DotCraft.Editor.AppBinding
{
    [InitializeOnLoad]
    internal static class UnityAppBindingBootstrap
    {
        static UnityAppBindingBootstrap()
        {
            EditorApplication.delayCall += () =>
            {
                MainThreadDispatcher.RunOrEnqueue(() => { });
                UnityAppBindingService.Instance.ApplySettings();
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => UnityAppBindingService.Instance.Shutdown();
            EditorApplication.quitting += () => UnityAppBindingService.Instance.Shutdown();
        }

        public static void ApplySettings()
        {
            if (DotCraftSettings.Instance.EnableAppBindingLocalServer)
                UnityAppBindingService.Instance.StartLocalServer();
            else
                UnityAppBindingService.Instance.StopLocalServer();
        }
    }
}

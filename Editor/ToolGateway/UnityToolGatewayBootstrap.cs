using System;
using DotCraft.Editor.Extensions;
using UnityEditor;

namespace DotCraft.Editor.ToolGateway
{
    [InitializeOnLoad]
    internal static class UnityToolGatewayBootstrap
    {
        /// <summary>Survives domain reload; cleared on Editor restart.</summary>
        internal const string SessionsStateKey = "DotCraft.ToolGateway.Sessions";

        private const double SweepIntervalSeconds = 1.0;

        private static double s_nextSweepTime;
        private static string s_persistedSessions;

        static UnityToolGatewayBootstrap()
        {
            RestoreSessions();
            EditorApplication.update += StartRuntimeWhenEditorIsReady;
            EditorApplication.update += SweepWhenDue;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += UnityToolGatewayRuntime.Instance.Shutdown;
        }

        public static void ApplySettings()
        {
            UnityToolGatewayRuntime.Instance.ApplySettings();
        }

        private static void StartRuntimeWhenEditorIsReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= StartRuntimeWhenEditorIsReady;
            MainThreadDispatcher.RunOrEnqueue(() => { });
            UnityToolGatewayRuntime.Instance.ApplySettings();
        }

        private static void SweepWhenDue()
        {
            if (EditorApplication.timeSinceStartup < s_nextSweepTime)
                return;

            s_nextSweepTime = EditorApplication.timeSinceStartup + SweepIntervalSeconds;

            if (McpClientSessionRegistry.Instance.Sweep(DateTime.UtcNow))
                UnityToolGatewayRuntime.Instance.NotifySessionsChanged();

            PersistSessionsIfChanged();
        }

        private static void OnBeforeAssemblyReload()
        {
            PersistSessions();
            UnityToolGatewayRuntime.Instance.Shutdown();
        }

        private static void PersistSessionsIfChanged()
        {
            var serialized = McpClientSessionRegistry.Instance.Serialize();
            if (string.Equals(serialized, s_persistedSessions, StringComparison.Ordinal))
                return;

            s_persistedSessions = serialized;
            SessionState.SetString(SessionsStateKey, serialized);
        }

        private static void PersistSessions()
        {
            s_persistedSessions = McpClientSessionRegistry.Instance.Serialize();
            SessionState.SetString(SessionsStateKey, s_persistedSessions);
        }

        private static void RestoreSessions()
        {
            var serialized = SessionState.GetString(SessionsStateKey, string.Empty);
            if (string.IsNullOrWhiteSpace(serialized))
                return;

            McpClientSessionRegistry.Instance.Restore(serialized, DateTime.UtcNow);
            s_persistedSessions = McpClientSessionRegistry.Instance.Serialize();
        }
    }
}

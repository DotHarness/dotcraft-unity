using System;
using System.IO;
using DotCraft.Editor.AppBinding;
using UnityEngine;

namespace DotCraft.Editor.McpSetup
{
    internal static class McpGatewaySetupDefaults
    {
        public static readonly string[] ReadOnlyToolNames =
        {
            "unity_scene_query",
            "unity_get_selection",
            "unity_get_console_logs",
            "unity_get_project_info"
        };

        public static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        public static string Endpoint =>
            $"http://127.0.0.1:{UnityAppBindingConstants.LocalServerPort}/dotcraft/mcp";

        public static McpInstallOptions CreateOptions(McpInstallPreset preset) =>
            new(Endpoint, preset, ReadOnlyToolNames);

        public static bool IsLoopbackEndpoint(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                return false;

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                   && (string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase));
        }
    }
}

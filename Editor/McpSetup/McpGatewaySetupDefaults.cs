using System.IO;
using UnityEngine;

namespace DotCraft.Editor.McpSetup
{
    internal static class McpGatewaySetupDefaults
    {
        public static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        public static McpInstallOptions CreateOptions() =>
            new(McpGatewayInstaller.InstalledExecutablePath, ProjectRoot);
    }
}

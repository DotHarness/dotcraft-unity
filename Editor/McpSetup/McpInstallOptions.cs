using System.Collections.Generic;
using System.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpInstallOptions
    {
        public const string ServerName = "dotcraft-unity";

        public McpInstallOptions(
            string endpoint,
            McpInstallPreset preset = McpInstallPreset.Recommended,
            IEnumerable<string> readOnlyToolNames = null)
        {
            Endpoint = endpoint ?? string.Empty;
            Preset = preset;
            ReadOnlyToolNames = (readOnlyToolNames ?? McpGatewaySetupDefaults.ReadOnlyToolNames).ToArray();
        }

        public string Endpoint { get; }

        public McpInstallPreset Preset { get; }

        public string[] ReadOnlyToolNames { get; }

        public bool UseCodexReadOnly => Preset == McpInstallPreset.CodexReadOnly;
    }
}

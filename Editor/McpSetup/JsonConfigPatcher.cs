using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal static class JsonConfigPatcher
    {
        public static McpPatchPreview PreviewInstall(
            string path,
            string before,
            string serverName,
            JObject serverConfig)
        {
            try
            {
                var root = string.IsNullOrWhiteSpace(before)
                    ? new JObject()
                    : JObject.Parse(before);

                if (root["mcpServers"] != null && root["mcpServers"]?.Type != JTokenType.Object)
                    return McpPatchPreview.Invalid(path, before, "mcpServers must be a JSON object.");

                if (root["mcpServers"] == null)
                    root["mcpServers"] = new JObject();

                var servers = (JObject)root["mcpServers"];
                servers[serverName] = serverConfig.DeepClone();
                var after = root.ToString(Formatting.Indented) + Environment.NewLine;
                return new McpPatchPreview(
                    path,
                    before,
                    after,
                    !string.Equals(before, after, StringComparison.Ordinal),
                    true);
            }
            catch (JsonException ex)
            {
                return McpPatchPreview.Invalid(path, before, $"Invalid JSON: {ex.Message}");
            }
        }

        public static McpPatchPreview PreviewUninstall(
            string path,
            string before,
            string serverName)
        {
            if (string.IsNullOrWhiteSpace(before))
                return new McpPatchPreview(path, before, before, false, true);

            try
            {
                var root = JObject.Parse(before);
                if (root["mcpServers"] is not JObject servers)
                    return new McpPatchPreview(path, before, before, false, true);

                var property = servers.Property(serverName);
                if (property == null)
                    return new McpPatchPreview(path, before, before, false, true);

                property.Remove();
                var after = root.ToString(Formatting.Indented) + Environment.NewLine;
                return new McpPatchPreview(path, before, after, true, true);
            }
            catch (JsonException ex)
            {
                return McpPatchPreview.Invalid(path, before, $"Invalid JSON: {ex.Message}");
            }
        }
    }
}

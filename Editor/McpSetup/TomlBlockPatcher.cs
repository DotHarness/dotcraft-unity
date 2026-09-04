using System;
using System.Text;

namespace DotCraft.Editor.McpSetup
{
    internal static class TomlBlockPatcher
    {
        private const string BeginMarker = "# BEGIN dotcraft-unity MCP Gateway";
        private const string EndMarker = "# END dotcraft-unity MCP Gateway";
        private const string TableHeader = "[mcp_servers.dotcraft_unity]";

        public static McpPatchPreview PreviewInstall(
            string path,
            string before,
            McpInstallOptions options)
        {
            var block = BuildBlock(options);
            var after = ReplaceManagedBlock(before ?? string.Empty, block, out _);
            return new McpPatchPreview(
                path,
                before,
                after,
                !string.Equals(before ?? string.Empty, after, StringComparison.Ordinal),
                true);
        }

        public static McpPatchPreview PreviewUninstall(string path, string before)
        {
            var after = RemoveManagedBlock(before ?? string.Empty, out var removed);
            return new McpPatchPreview(path, before, after, removed, true);
        }

        private static string BuildBlock(McpInstallOptions options)
        {
            var builder = new StringBuilder();
            builder.AppendLine(BeginMarker);
            builder.AppendLine(TableHeader);
            builder.AppendLine($"command = {Quote(options.Command)}");
            builder.AppendLine($"args = [{Quote("mcp")}, {Quote("--project-root")}, {Quote(options.ProjectRoot)}]");
            builder.AppendLine("enabled = true");
            builder.AppendLine("tool_timeout_sec = 60");
            builder.AppendLine("default_tools_approval_mode = \"prompt\"");
            builder.AppendLine(EndMarker);
            return builder.ToString();
        }

        private static string ReplaceManagedBlock(string before, string block, out bool replaced)
        {
            replaced = false;
            if (TryFindMarkedBlock(before, out var start, out var end))
            {
                replaced = true;
                return before.Substring(0, start)
                       + TrimOneTrailingBlankLine(block)
                       + before.Substring(end);
            }

            var prefix = before;
            if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith("\n", StringComparison.Ordinal))
                prefix += Environment.NewLine;
            if (!string.IsNullOrWhiteSpace(prefix) && !prefix.EndsWith("\n\n", StringComparison.Ordinal))
                prefix += Environment.NewLine;

            return prefix + block;
        }

        private static string RemoveManagedBlock(string before, out bool removed)
        {
            removed = false;
            if (TryFindMarkedBlock(before, out var start, out var end))
            {
                removed = true;
                return before.Substring(0, start) + before.Substring(end);
            }

            return before;
        }

        private static bool TryFindMarkedBlock(string text, out int start, out int end)
        {
            start = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                end = -1;
                return false;
            }

            var markerEnd = text.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (markerEnd < 0)
            {
                end = -1;
                return false;
            }

            end = markerEnd + EndMarker.Length;
            end = ConsumeLineEnding(text, end);
            return true;
        }

        private static int ConsumeLineEnding(string text, int index)
        {
            if (index < text.Length && text[index] == '\r')
                index++;
            if (index < text.Length && text[index] == '\n')
                index++;
            return index;
        }

        private static string TrimOneTrailingBlankLine(string value)
        {
            if (value.EndsWith("\n\n", StringComparison.Ordinal))
                return value.Substring(0, value.Length - 1);
            return value;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"") + "\"";
        }
    }
}

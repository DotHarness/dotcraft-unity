using System;
using System.Linq;
using System.Text;

namespace DotCraft.Editor.McpSetup
{
    internal static class TextDiffPreview
    {
        public static string Build(string before, string after)
        {
            before ??= string.Empty;
            after ??= string.Empty;
            if (string.Equals(before, after, StringComparison.Ordinal))
                return "No changes.";

            if (string.IsNullOrEmpty(before))
                return "New file:\n" + after;

            var beforeLines = SplitLines(before);
            var afterLines = SplitLines(after);
            var builder = new StringBuilder();
            builder.AppendLine("--- Current");
            builder.AppendLine("+++ After");

            var max = Math.Max(beforeLines.Length, afterLines.Length);
            for (var i = 0; i < max; i++)
            {
                var oldLine = i < beforeLines.Length ? beforeLines[i] : null;
                var newLine = i < afterLines.Length ? afterLines[i] : null;
                if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(oldLine))
                        builder.AppendLine("  " + oldLine);
                    continue;
                }

                if (oldLine != null)
                    builder.AppendLine("- " + oldLine);
                if (newLine != null)
                    builder.AppendLine("+ " + newLine);
            }

            return builder.ToString();
        }

        private static string[] SplitLines(string value) =>
            value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Where((line, index) => index > 0 || line.Length > 0)
                .ToArray();
    }
}

namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpPatchPreview
    {
        public McpPatchPreview(
            string path,
            string before,
            string after,
            bool hasChanges,
            bool isValid,
            string error = null)
        {
            Path = path ?? string.Empty;
            Before = before ?? string.Empty;
            After = after ?? string.Empty;
            HasChanges = hasChanges;
            IsValid = isValid;
            Error = error ?? string.Empty;
        }

        public string Path { get; }

        public string Before { get; }

        public string After { get; }

        public bool HasChanges { get; }

        public bool IsValid { get; }

        public string Error { get; }

        public static McpPatchPreview Invalid(string path, string before, string error) =>
            new(path, before, before, false, false, error);
    }
}

namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpInstallResult
    {
        public McpInstallResult(
            bool success,
            string path,
            bool changed,
            string message = null,
            string error = null)
        {
            Success = success;
            Path = path ?? string.Empty;
            Changed = changed;
            Message = message ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }

        public string Path { get; }

        public bool Changed { get; }

        public string Message { get; }

        public string Error { get; }

        public static McpInstallResult Failed(string path, string error) =>
            new(false, path, false, error: error);
    }
}

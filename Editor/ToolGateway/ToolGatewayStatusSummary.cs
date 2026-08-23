namespace DotCraft.Editor.ToolGateway
{
    internal sealed class ToolGatewayStatusSummary
    {
        private ToolGatewayStatusSummary(
            bool isRunning,
            string packageVersion,
            string manifestRevision,
            int toolCount,
            string lastError)
        {
            IsRunning = isRunning;
            PackageVersion = packageVersion ?? string.Empty;
            ManifestRevision = manifestRevision ?? string.Empty;
            ToolCount = toolCount;
            LastError = lastError ?? string.Empty;
            IsVisible = isRunning;
            Tooltip = isRunning
                ? $"DotCraft Unity Tool Gateway is running with {toolCount} tools. Click for status and actions."
                : string.Empty;
        }

        public static ToolGatewayStatusSummary Empty { get; } =
            new(false, string.Empty, string.Empty, 0, string.Empty);

        public bool IsVisible { get; }

        public bool IsRunning { get; }

        public string PackageVersion { get; }

        public string ManifestRevision { get; }

        public int ToolCount { get; }

        public string LastError { get; }

        public string Tooltip { get; }

        public static ToolGatewayStatusSummary FromState(
            bool isRunning,
            string packageVersion,
            string manifestRevision,
            int toolCount,
            string lastError)
        {
            return new ToolGatewayStatusSummary(
                isRunning,
                packageVersion,
                manifestRevision,
                toolCount,
                lastError);
        }
    }
}

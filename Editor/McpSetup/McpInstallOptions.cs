namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpInstallOptions
    {
        public const string ServerName = "dotcraft-unity";

        public McpInstallOptions(string endpoint)
        {
            Endpoint = endpoint ?? string.Empty;
        }

        public string Endpoint { get; }
    }
}

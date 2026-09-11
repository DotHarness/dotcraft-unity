namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpInstallOptions
    {
        public const string ServerName = "dotcraft-unity";

        public McpInstallOptions(string command, string projectRoot)
        {
            Command = command;
            ProjectRoot = projectRoot;
        }

        public string Command { get; }

        public string ProjectRoot { get; }
    }
}

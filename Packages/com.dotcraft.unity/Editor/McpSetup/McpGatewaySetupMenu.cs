using UnityEditor;

namespace DotCraft.Editor.McpSetup
{
    internal static class McpGatewaySetupMenu
    {
        [MenuItem("Tools/DotCraft/MCP Gateway Setup")]
        public static void OpenWindow()
        {
            McpGatewaySetupWindow.ShowWindow();
        }
    }
}

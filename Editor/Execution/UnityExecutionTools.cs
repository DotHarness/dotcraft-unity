using System.ComponentModel;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;

namespace DotCraft.Editor.Execution
{
    internal static class UnityExecutionTools
    {
        [AgentTool(
            Namespace = "unity",
            Name = "unity_execute_csharp",
            Description = "Compile and execute optional leading C# using directives followed by method-body statements in the Unity Editor process.",
            Kind = AcpToolKind.Execute,
            DeferLoading = false)]
        public static async Task<object> ExecuteCSharp(
            [Description("Optional leading using directives followed by C# method-body statements. Use return to provide a result.")]
            string code,
            [Description("Execution mode: editor or playmode.")]
            [AgentToolSchemaHint(EnumValues = new[] { UnityExecutionModes.Editor, UnityExecutionModes.PlayMode })]
            string mode = UnityExecutionModes.Editor)
        {
            return await ExecutionRouter.Instance.ExecuteAsync(
                new ExecutionRequest(UnityExecutionEngines.CSharp, mode, code));
        }
    }
}

using System.ComponentModel;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.Execution
{
    internal static class UnityExecutionTools
    {
        [AgentTool(
            Namespace = "unity",
            Name = "unity_execute_csharp",
            Description = "Compile and execute C# in the Unity Editor process, from an inline snippet or a saved script file.",
            Kind = AcpToolKind.Execute,
            DeferLoading = false)]
        public static async Task<object> ExecuteCSharp(
            [Description("Optional leading using directives followed by C# method-body statements. Use return to provide a result. Provide either code or path.")]
            string code = null,
            [Description("Execution mode: editor or playmode.")]
            [AgentToolSchemaHint(EnumValues = new[] { UnityExecutionModes.Editor, UnityExecutionModes.PlayMode })]
            string mode = UnityExecutionModes.Editor,
            [Description("Project-relative path of a saved C# script to execute instead of code, for example .craft/scripts/console-read.cs.")]
            string path = null,
            [Description("Values passed to the script as the Args JObject.")]
            JObject args = null)
        {
            return await ExecutionRouter.Instance.ExecuteAsync(
                new ExecutionRequest(UnityExecutionEngines.CSharp, mode, code, path, args));
        }
    }
}

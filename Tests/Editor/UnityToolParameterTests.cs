using System.Linq;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class UnityToolParameterTests
    {
        [Test]
        public void SceneQueryAcceptsMissingOptionalParameters()
        {
            var result = RuntimeToolInvoker.InvokeAsync(
                FindBuiltinTool("_unity/scene_query"),
                new JObject()).Result;
            var json = DotCraftJson.Serialize(result);

            Assert.That(json, Does.Contain("\"objects\""));
        }

        [Test]
        public void ConsoleLogsParseTypesAndLimitFromJTokenParameters()
        {
            var parameters = JObject.Parse("{\"types\":[\"error\",\"warning\"],\"limit\":1}");

            var result = RuntimeToolInvoker.InvokeAsync(
                FindBuiltinTool("_unity/get_console_logs"),
                parameters).Result;
            var json = DotCraftJson.Serialize(result);

            Assert.That(json, Does.Contain("\"logs\""));
        }

        private static RuntimeToolDefinition FindBuiltinTool(string acpMethod)
        {
            var snapshot = RuntimeToolCatalog.Discover();
            return snapshot.Tools.Single(tool => tool.Descriptor.AcpMethod == acpMethod);
        }
    }
}

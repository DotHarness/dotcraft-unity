using System.Linq;
using System.Reflection;
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

        [Test]
        public void BuiltinToolsUseStableUnityExtensionMethods()
        {
            AssertBuiltinTool("unity_execute_csharp", "_unity/execute_csharp");
            AssertBuiltinTool("unity_scene_query", "_unity/scene_query");
            AssertBuiltinTool("unity_get_selection", "_unity/get_selection");
            AssertBuiltinTool("unity_get_console_logs", "_unity/get_console_logs");
            AssertBuiltinTool("unity_get_project_info", "_unity/get_project_info");
        }

        [Test]
        public void PluginToolsUseGeneratedDynamicExtensionMethods()
        {
            var method = typeof(UnityToolParameterTests).GetMethod(
                nameof(PluginRuntimeToolForDiscovery),
                BindingFlags.NonPublic | BindingFlags.Static);
            var snapshot = RuntimeToolCatalog.Discover();
            var tool = snapshot.Tools.Single(t => t.Method == method);

            Assert.That(tool.Source, Is.EqualTo(RuntimeToolSource.Plugin));
            Assert.That(tool.Descriptor.AcpMethod, Does.StartWith("_unity/dynamic/test_plugin_runtime_tool_"));
        }

        private static RuntimeToolDefinition FindBuiltinTool(string acpMethod)
        {
            var snapshot = RuntimeToolCatalog.Discover();
            return snapshot.Tools.Single(tool => tool.Descriptor.AcpMethod == acpMethod);
        }

        private static void AssertBuiltinTool(string name, string acpMethod)
        {
            var snapshot = RuntimeToolCatalog.Discover();
            var tool = snapshot.Tools.Single(t =>
                t.Source == RuntimeToolSource.Builtin && t.Descriptor.Name == name);

            Assert.That(tool.Descriptor.AcpMethod, Is.EqualTo(acpMethod));
        }

        [AgentTool(
            Name = "test_plugin_runtime_tool",
            Description = "Test plugin runtime tool.")]
        private static object PluginRuntimeToolForDiscovery()
        {
            return new { ok = true };
        }
    }
}

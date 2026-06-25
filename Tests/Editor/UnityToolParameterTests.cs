using System.Linq;
using System.Reflection;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class UnityToolParameterTests
    {
        [Test]
        public void BuiltinToolsUseStableUnityExtensionMethods()
        {
            AssertBuiltinTool("unity_execute_csharp", "_unity/execute_csharp");
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

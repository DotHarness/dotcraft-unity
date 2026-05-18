using System.Linq;
using System.Reflection;
using DotCraft.Editor.AppBinding;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using DotCraft.Editor.Settings;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class AppBindingTests
    {
        [Test]
        public void LocalServerParsesHandoffUrl()
        {
            var handoff = UnityAppBindingLocalServer.ParseHandoff(
                "/dotcraft/bind?app=com.dotharness.dotcraft-unity&request=bind_req_1&token=tok%2B1&endpoint=ws%3A%2F%2F127.0.0.1%3A1234%2Fappserver%3Ftoken%3Dabc");

            Assert.That(handoff.Operation, Is.EqualTo("bind"));
            Assert.That(handoff.AppId, Is.EqualTo("com.dotharness.dotcraft-unity"));
            Assert.That(handoff.RequestId, Is.EqualTo("bind_req_1"));
            Assert.That(handoff.RequestToken, Is.EqualTo("tok+1"));
            Assert.That(handoff.Endpoint, Is.EqualTo("ws://127.0.0.1:1234/appserver?token=abc"));
        }

        [Test]
        public void AgentToolAttributeStoresAppBindingMetadata()
        {
            var tool = FindTool(nameof(AppBindingMetadataTool));

            Assert.That(tool.AppBinding.Scope, Is.EqualTo("unity.read"));
            Assert.That(tool.AppBinding.Risk, Is.EqualTo("read"));
            Assert.That(tool.AppBinding.Exposure, Is.EqualTo("direct"));
        }

        [Test]
        public void ToolCatalogAdapterMapsEnabledToolsToUnityNamespace()
        {
            var readTool = FindTool(nameof(AppBindingMetadataTool));
            var editTool = FindTool(nameof(AppBindingInferredEditTool));
            var settings = new DotCraftSettings
            {
                EnableBuiltinUnityTools = false
            };
            settings.DynamicToolEnabledById[readTool.Id] = true;
            settings.DynamicToolEnabledById[editTool.Id] = true;

            var attachment = UnityAppBindingToolCatalogAdapter.Build(
                settings,
                new[] { "unity.read", "unity.edit" });

            var readSpec = attachment.Tools.Single(tool => tool.Name == "test_appbinding_metadata_tool");
            var readCatalog = attachment.ToolCatalog.Single(tool => tool.Name == "test_appbinding_metadata_tool");
            Assert.That(readSpec.Namespace, Is.EqualTo("unity"));
            Assert.That(readCatalog.Scope, Is.EqualTo("unity.read"));
            Assert.That(readCatalog.Risk, Is.EqualTo("read"));
            Assert.That(attachment.DirectToolNames, Does.Contain("test_appbinding_metadata_tool"));

            var editCatalog = attachment.ToolCatalog.Single(tool => tool.Name == "test_appbinding_inferred_edit_tool");
            Assert.That(editCatalog.Scope, Is.EqualTo("unity.edit"));
            Assert.That(editCatalog.Risk, Is.EqualTo("mutate"));
            Assert.That(attachment.DeferredToolNames, Does.Contain("test_appbinding_inferred_edit_tool"));
        }

        [Test]
        public void ToolCatalogAdapterFiltersByGrantedScopes()
        {
            var editTool = FindTool(nameof(AppBindingInferredEditTool));
            var settings = new DotCraftSettings
            {
                EnableBuiltinUnityTools = false
            };
            settings.DynamicToolEnabledById[editTool.Id] = true;

            var attachment = UnityAppBindingToolCatalogAdapter.Build(settings, new[] { "unity.read" });

            Assert.That(attachment.Tools, Is.Empty);
            Assert.That(attachment.ToolCatalog, Is.Empty);
        }

        private static RuntimeToolDefinition FindTool(string methodName)
        {
            var method = typeof(AppBindingTests).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            return RuntimeToolCatalog.Discover().Tools.Single(tool => tool.Method == method);
        }

        [AgentTool(
            Namespace = "custom_plugin",
            Name = "test_appbinding_metadata_tool",
            Description = "Test App Binding metadata.",
            Kind = AcpToolKind.Read,
            AppBindingScope = "unity.read",
            AppBindingRisk = "read",
            AppBindingExposure = "direct")]
        private static object AppBindingMetadataTool()
        {
            return new { ok = true };
        }

        [AgentTool(
            Name = "test_appbinding_inferred_edit_tool",
            Description = "Test inferred App Binding metadata.",
            Kind = AcpToolKind.Edit)]
        private static object AppBindingInferredEditTool()
        {
            return new { ok = true };
        }
    }
}

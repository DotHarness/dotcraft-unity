using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComponentDescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DotCraft.Editor.AppBinding;
using DotCraft.Editor.RuntimeTools;
using DotCraft.Editor.Settings;
using DotCraft.Editor.ToolGateway;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.Tests
{
    public sealed class ToolGatewayTests
    {
        private const string ExecuteCSharpToolName = "unity_execute_csharp";

        private static readonly string[] BuiltinToolNames =
        {
            ExecuteCSharpToolName
        };

        private static readonly string[] RemovedReadToolNames =
        {
            "unity_scene_query",
            "unity_get_selection",
            "unity_get_console_logs",
            "unity_get_project_info"
        };

        [Test]
        public void GatewayListsEnabledRuntimeToolsByDefault()
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var tools = UnityToolGateway.Instance.ListTools();
                var names = tools.Select(tool => tool.Name).ToArray();

                foreach (var toolName in BuiltinToolNames)
                    Assert.That(names, Does.Contain(toolName));
                foreach (var toolName in RemovedReadToolNames)
                    Assert.That(names, Does.Not.Contain(toolName));

                Assert.That(names, Is.EquivalentTo(BuiltinToolNames));
                Assert.That(names, Does.Not.Contain("ExecuteCSharp"));
                Assert.That(names, Does.Not.Contain("execute_csharp"));
                Assert.That(tools.Single(tool => tool.Name == ExecuteCSharpToolName)
                    .InputSchema["properties"]?["code"], Is.Not.Null);
            });
        }

        [Test]
        public void GatewayRespectsCSharpAutomationSetting()
        {
            WithGatewaySettings(enableCSharpAutomation: false, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var names = UnityToolGateway.Instance.ListTools()
                    .Select(tool => tool.Name)
                    .ToArray();

                foreach (var toolName in BuiltinToolNames)
                    Assert.That(names, Does.Not.Contain(toolName));
            });
        }

        [Test]
        public void GatewayExposesEnabledPluginToolsOnly()
        {
            var enabled = FindPluginTool("test_gateway_plugin_echo");
            var disabled = FindPluginTool("test_gateway_plugin_disabled");
            var conflictingPlugin = FindPluginTool(ExecuteCSharpToolName);

            WithGatewaySettings(
                enableCSharpAutomation: false,
                enabledPluginToolIds: new[] { enabled.Id, conflictingPlugin.Id },
                () =>
            {
                var names = UnityToolGateway.Instance.ListTools()
                    .Select(tool => tool.Name)
                    .ToArray();

                Assert.That(names, Does.Contain(enabled.Descriptor.Name));
                Assert.That(names, Does.Not.Contain(disabled.Descriptor.Name));
                Assert.That(names, Does.Not.Contain(ExecuteCSharpToolName));
            });
        }

        [Test]
        public void GatewayKeepsExecuteCSharpReservedWhenPluginUsesSameName()
        {
            var conflictingPlugin = FindPluginTool(ExecuteCSharpToolName);

            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: new[] { conflictingPlugin.Id }, () =>
            {
                var names = UnityToolGateway.Instance.ListTools()
                    .Select(tool => tool.Name)
                    .ToArray();

                Assert.That(names.Count(name => name == ExecuteCSharpToolName), Is.EqualTo(1));

                var result = CallMcpTool(
                    ExecuteCSharpToolName,
                    new JObject
                    {
                        ["code"] = "return 21 + 21;",
                        ["mode"] = "editor"
                    },
                    timeoutMilliseconds: 10000);

                Assert.That(result?["isError"]?.Value<bool>(), Is.False);
                Assert.That(result?["structuredContent"]?["returnValue"]?.Value<int>(), Is.EqualTo(42));
            });
        }

        [Test]
        public void McpInitializeReturnsToolCapability()
        {
            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                "POST",
                "/dotcraft/mcp",
                @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""protocolVersion"":""2025-11-25"",""capabilities"":{},""clientInfo"":{""name"":""test"",""version"":""1""}}}",
                CancellationToken.None));
            var root = JObject.Parse(response.Body);

            Assert.That(response.Status, Is.EqualTo(200));
            Assert.That(root["result"]?["capabilities"]?["tools"], Is.Not.Null);
            Assert.That(root["result"]?["serverInfo"]?["name"]?.Value<string>(), Is.EqualTo("dotcraft-unity"));
            Assert.That(root["result"]?["instructions"]?.Value<string>(), Does.Contain(ExecuteCSharpToolName));
        }

        [Test]
        public void McpToolsListExposesEnabledRuntimeTools()
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                    "POST",
                    "/dotcraft/mcp",
                    @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list"",""params"":{}}",
                    CancellationToken.None));
                var tools = (JArray)JObject.Parse(response.Body)["result"]?["tools"];
                var names = tools?.Select(tool => tool["name"]?.Value<string>()).ToArray();

                Assert.That(tools, Is.Not.Null);
                foreach (var toolName in BuiltinToolNames)
                    Assert.That(names, Does.Contain(toolName));
                foreach (var toolName in RemovedReadToolNames)
                    Assert.That(names, Does.Not.Contain(toolName));

                Assert.That(names, Does.Not.Contain("ExecuteCSharp"));
                Assert.That(names, Does.Not.Contain("execute_csharp"));
                Assert.That(tools.Single(tool => tool["name"]?.Value<string>() == ExecuteCSharpToolName)
                    ["inputSchema"]?["required"]?[0]?.Value<string>(), Is.EqualTo("code"));
            });
        }

        [Test]
        public void McpToolsCallRunsExecuteCSharp()
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var result = CallMcpTool(
                    ExecuteCSharpToolName,
                    new JObject
                    {
                        ["code"] = "return 21 + 21;",
                        ["mode"] = "editor"
                    },
                    timeoutMilliseconds: 10000);

                Assert.That(result?["isError"]?.Value<bool>(), Is.False);
                Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.True);
                Assert.That(result?["structuredContent"]?["returnValue"]?.Value<int>(), Is.EqualTo(42));
            });
        }

        [Test]
        public void McpToolsCallCanCreateGameObjectInEditorMode()
        {
            var name = $"DotCraft Gateway Test {Guid.NewGuid():N}";

            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                try
                {
                    var result = CallMcpTool(
                        ExecuteCSharpToolName,
                        new JObject
                        {
                            ["code"] = $"var go = new GameObject({JToken.FromObject(name).ToString(Formatting.None)}); return go.name;",
                            ["mode"] = "editor"
                        },
                        timeoutMilliseconds: 10000);

                    Assert.That(result?["isError"]?.Value<bool>(), Is.False);
                    Assert.That(result?["structuredContent"]?["returnValue"]?.Value<string>(), Is.EqualTo(name));
                    Assert.That(GameObject.Find(name), Is.Not.Null);
                }
                finally
                {
                    var created = GameObject.Find(name);
                    if (created != null)
                        UnityEngine.Object.DestroyImmediate(created);
                }
            });
        }

        [Test]
        public void McpToolsCallInvalidCSharpReturnsCompilerDiagnostics()
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var result = CallMcpTool(
                    ExecuteCSharpToolName,
                    new JObject
                    {
                        ["code"] = "return ;",
                        ["mode"] = "editor"
                    },
                    timeoutMilliseconds: 10000);

                Assert.That(result?["isError"]?.Value<bool>(), Is.True);
                Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.False);
                Assert.That(result?["structuredContent"]?["errorCode"]?.Value<string>(), Is.EqualTo("CompilationFailed"));
                Assert.That((JArray)result?["structuredContent"]?["diagnostics"], Is.Not.Empty);
            });
        }

        [Test]
        public void McpToolsCallPlaymodeFailsClearlyWhenEditorIsNotPlaying()
        {
            Assume.That(EditorApplication.isPlaying, Is.False);

            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var result = CallMcpTool(
                    ExecuteCSharpToolName,
                    new JObject
                    {
                        ["code"] = "return 1;",
                        ["mode"] = "playmode"
                    },
                    timeoutMilliseconds: 10000);

                Assert.That(result?["isError"]?.Value<bool>(), Is.True);
                Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.False);
                Assert.That(result?["structuredContent"]?["errorCode"]?.Value<string>(), Is.EqualTo("UnityNotInPlayMode"));
            });
        }

        [TestCase("unity_scene_query")]
        [TestCase("unity_get_selection")]
        [TestCase("unity_get_console_logs")]
        [TestCase("unity_get_project_info")]
        public void McpToolsCallDoesNotExposeRemovedReadTools(string toolName)
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var result = CallMcpTool(toolName, new JObject(), timeoutMilliseconds: 10000);

                Assert.That(result?["isError"]?.Value<bool>(), Is.True);
                Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.False);
                Assert.That(result?["structuredContent"]?["errorCode"]?.Value<string>(), Is.EqualTo("ToolNotFound"));
            });
        }

        [Test]
        public void McpToolsCallRunsEnabledPluginRuntimeTool()
        {
            var plugin = FindPluginTool("test_gateway_plugin_echo");

            WithGatewaySettings(enableCSharpAutomation: false, enabledPluginToolIds: new[] { plugin.Id }, () =>
            {
                var result = CallMcpTool(
                    plugin.Descriptor.Name,
                    new JObject { ["value"] = "hello" },
                    timeoutMilliseconds: 10000);

                Assert.That(result?["isError"]?.Value<bool>(), Is.False);
                Assert.That(result?["structuredContent"]?["echoed"]?.Value<string>(), Is.EqualTo("hello"));
            });
        }

        [Test]
        public void McpToolsCallRuntimeToolArgumentErrorReturnsToolError()
        {
            var plugin = FindPluginTool("test_gateway_plugin_requires_int");

            WithGatewaySettings(enableCSharpAutomation: false, enabledPluginToolIds: new[] { plugin.Id }, () =>
            {
                var result = CallMcpTool(
                    plugin.Descriptor.Name,
                    new JObject { ["count"] = "not an int" },
                    timeoutMilliseconds: 10000);

                Assert.That(result?["isError"]?.Value<bool>(), Is.True);
                Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.False);
                Assert.That(result?["structuredContent"]?["errorCode"]?.Value<string>(), Is.EqualTo("InvalidArguments"));
            });
        }

        [TestCase("ExecuteCSharp")]
        [TestCase("execute_csharp")]
        public void McpToolsCallDoesNotAcceptLegacyExecuteCSharpAliases(string toolName)
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var result = CallMcpTool(
                    toolName,
                    new JObject
                    {
                        ["code"] = "return 1;",
                        ["mode"] = "editor"
                    });

                Assert.That(result?["isError"]?.Value<bool>(), Is.True);
                Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.False);
                Assert.That(result?["structuredContent"]?["errorCode"]?.Value<string>(), Is.EqualTo("ToolNotFound"));
            });
        }

        [Test]
        public void HttpToolProjectionSupportsCanonicalOpenAiAndClaudeFormats()
        {
            var plugin = FindPluginTool("test_gateway_plugin_echo");

            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: new[] { plugin.Id }, () =>
            {
                var canonical = GetProjectedTools("canonical");
                var responses = GetProjectedTools("openai-responses");
                var chat = GetProjectedTools("openai-chat");
                var claude = GetProjectedTools("claude");

                AssertProjectedToolNames(canonical, "canonical", ExecuteCSharpToolName, plugin.Descriptor.Name);
                AssertProjectedToolNames(responses, "openai-responses", ExecuteCSharpToolName, plugin.Descriptor.Name);
                AssertProjectedToolNames(chat, "openai-chat", ExecuteCSharpToolName, plugin.Descriptor.Name);
                AssertProjectedToolNames(claude, "claude", ExecuteCSharpToolName, plugin.Descriptor.Name);
                Assert.That(responses["tools"]?[0]?["type"]?.Value<string>(), Is.EqualTo("function"));
                Assert.That(claude["tools"]?[0]?["input_schema"], Is.Not.Null);
            });
        }

        [Test]
        public void LocalServerRespondsToMcpInitialize()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);

            var response = WaitForResult(SendPostAsync(
                port,
                "/dotcraft/mcp",
                @"{""jsonrpc"":""2.0"",""id"":5,""method"":""initialize"",""params"":{""protocolVersion"":""2025-11-25"",""capabilities"":{},""clientInfo"":{""name"":""test"",""version"":""1""}}}"),
                timeoutMilliseconds: 10000);

            Assert.That(response, Does.Contain("HTTP/1.1 200 OK"));
            Assert.That(response, Does.Contain(@"""tools"""));
            server.Stop();
            AssertCanBind(port);
        }

        private static JToken CallMcpTool(string name, JObject arguments, int timeoutMilliseconds = 3000)
        {
            var body = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 3,
                ["method"] = "tools/call",
                ["params"] = new JObject
                {
                    ["name"] = name,
                    ["arguments"] = arguments ?? new JObject()
                }
            }.ToString(Formatting.None);

            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                    "POST",
                    "/dotcraft/mcp",
                    body,
                    CancellationToken.None),
                timeoutMilliseconds);

            Assert.That(response.Status, Is.EqualTo(200));
            return JObject.Parse(response.Body)["result"];
        }

        private static JObject GetProjectedTools(string format)
        {
            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                "GET",
                $"/dotcraft/gateway/tools?format={format}",
                string.Empty,
                CancellationToken.None));
            return JObject.Parse(response.Body);
        }

        private static void AssertProjectedToolNames(
            JObject projected,
            string format,
            params string[] expectedNames)
        {
            var tools = (JArray)projected["tools"];
            var names = format == "openai-chat"
                ? tools.Select(tool => tool["function"]?["name"]?.Value<string>()).ToArray()
                : tools.Select(tool => tool["name"]?.Value<string>()).ToArray();

            foreach (var expectedName in expectedNames)
                Assert.That(names, Does.Contain(expectedName));

            Assert.That(names, Does.Not.Contain("ExecuteCSharp"));
            Assert.That(names, Does.Not.Contain("execute_csharp"));
        }

        private static void WithGatewaySettings(
            bool enableCSharpAutomation,
            IEnumerable<string> enabledPluginToolIds,
            Action action)
        {
            var settings = DotCraftSettings.Instance;
            var originalEnableCSharpAutomation = settings.EnableCSharpAutomation;
            var originalDynamicTools = settings.DynamicToolEnabledById == null
                ? new Dictionary<string, bool>(StringComparer.Ordinal)
                : new Dictionary<string, bool>(settings.DynamicToolEnabledById, StringComparer.Ordinal);

            try
            {
                settings.EnableCSharpAutomation = enableCSharpAutomation;
                settings.DynamicToolEnabledById = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var id in enabledPluginToolIds ?? Array.Empty<string>())
                    settings.DynamicToolEnabledById[id] = true;

                action();
            }
            finally
            {
                settings.EnableCSharpAutomation = originalEnableCSharpAutomation;
                settings.DynamicToolEnabledById = originalDynamicTools;
            }
        }

        private static RuntimeToolDefinition FindPluginTool(string name)
        {
            return RuntimeToolCatalog.Discover().Tools.Single(tool =>
                tool.Source == RuntimeToolSource.Plugin
                && string.Equals(tool.Descriptor.Name, name, StringComparison.Ordinal));
        }

        private static UnityAppBindingLocalServer CreateNoopLocalServer(int port)
        {
            return new UnityAppBindingLocalServer(
                (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult("ok");
                },
                port);
        }

        private static int GetFreeLoopbackPort()
        {
            var listener = CreateRestartCompatibleListener(0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static TcpListener CreateRestartCompatibleListener(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                listener.ExclusiveAddressUse = true;
            return listener;
        }

        private static void AssertCanBind(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = CreateRestartCompatibleListener(port);
                listener.Start();
            }
            finally
            {
                listener?.Stop();
            }
        }

        private static async Task<string> SendPostAsync(int port, string target, string body)
        {
            using var client = new TcpClient();
            await Task.Factory.FromAsync(
                client.BeginConnect(IPAddress.Loopback, port, null, null),
                client.EndConnect).ConfigureAwait(false);

            using var stream = client.GetStream();
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var request =
                $"POST {target} HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\n" +
                "Accept: application/json, text/event-stream\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);

            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        private static T WaitForResult<T>(Task<T> task, int timeoutMilliseconds = 3000)
        {
            if (!task.Wait(timeoutMilliseconds))
                Assert.Fail($"Timed out waiting for task after {timeoutMilliseconds} ms.");
            return task.GetAwaiter().GetResult();
        }

        [AgentTool(
            Name = "test_gateway_plugin_echo",
            Description = "Echo a value through a test plugin runtime tool.")]
        private static object GatewayPluginEcho(
            [ComponentDescriptionAttribute("Value to echo.")] string value = "ok")
        {
            return new { echoed = value };
        }

        [AgentTool(
            Name = "test_gateway_plugin_disabled",
            Description = "Disabled test plugin runtime tool.")]
        private static object GatewayPluginDisabled()
        {
            return new { disabled = true };
        }

        [AgentTool(
            Name = "test_gateway_plugin_requires_int",
            Description = "Return a required integer through a test plugin runtime tool.")]
        private static object GatewayPluginRequiresInt(
            [ComponentDescriptionAttribute("Required count.")] int count)
        {
            return new { count };
        }

        [AgentTool(
            Name = ExecuteCSharpToolName,
            Description = "Conflicting test plugin runtime tool.")]
        private static object GatewayPluginConflictsWithExecuteCSharp()
        {
            return new { conflict = true };
        }
    }
}

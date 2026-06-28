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
        private const string McpProtocolVersion = ToolGatewayMcpProtocol.ProtocolVersion;
        private const string PackageVersion = ToolGatewayMcpProtocol.ServerVersion;

        private static TaskCompletionSource<bool> s_cancelableToolStarted;
        private static TaskCompletionSource<bool> s_cancelableToolCancelled;

        private static readonly string[] BuiltinToolNames =
        {
            ExecuteCSharpToolName
        };

        [SetUp]
        public void SetUp()
        {
            ToolGatewayMcpSessionStore.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ToolGatewayMcpSessionStore.ResetForTests();
        }

        [Test]
        public void GatewayListsEnabledRuntimeToolsByDefault()
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var tools = UnityToolGateway.Instance.ListTools();
                var names = tools.Select(tool => tool.Name).ToArray();

                foreach (var toolName in BuiltinToolNames)
                    Assert.That(names, Does.Contain(toolName));

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
            var response = InitializeMcpSession(out var sessionId);
            var root = JObject.Parse(response.Body);

            Assert.That(response.Status, Is.EqualTo(200));
            Assert.That(sessionId, Is.Not.Empty);
            Assert.That(sessionId.All(ch => ch >= '!' && ch <= '~'), Is.True);
            Assert.That(root["result"]?["protocolVersion"]?.Value<string>(), Is.EqualTo(McpProtocolVersion));
            Assert.That(root["result"]?["capabilities"]?["tools"], Is.Not.Null);
            Assert.That(root["result"]?["serverInfo"]?["name"]?.Value<string>(), Is.EqualTo("dotcraft-unity"));
            Assert.That(root["result"]?["serverInfo"]?["version"]?.Value<string>(), Is.EqualTo(PackageVersion));
            Assert.That(root["result"]?["instructions"]?.Value<string>(), Does.Contain(ExecuteCSharpToolName));
        }

        [Test]
        public void McpInitializeNegotiatesUnsupportedVersionToSupportedVersion()
        {
            var response = InitializeMcpSession(out _, requestedVersion: "1900-01-01");
            var root = JObject.Parse(response.Body);

            Assert.That(response.Status, Is.EqualTo(200));
            Assert.That(root["result"]?["protocolVersion"]?.Value<string>(), Is.EqualTo(McpProtocolVersion));
        }

        [Test]
        public void McpPostRequiresStreamableHttpAcceptHeader()
        {
            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                new ToolGatewayHttpRequestContext
                {
                    Method = ToolGatewayMcpProtocol.HttpMethods.Post,
                    Target = ToolGatewayMcpProtocol.Paths.Mcp,
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Accept"] = "application/json"
                    },
                    Body = McpInitializeRequest().ToString(Formatting.None)
                },
                CancellationToken.None));

            Assert.That(response.Status, Is.EqualTo(406));

            var missing = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                new ToolGatewayHttpRequestContext
                {
                    Method = ToolGatewayMcpProtocol.HttpMethods.Post,
                    Target = ToolGatewayMcpProtocol.Paths.Mcp,
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    Body = McpInitializeRequest().ToString(Formatting.None)
                },
                CancellationToken.None));
            Assert.That(missing.Status, Is.EqualTo(406));
        }

        [TestCase("", McpJsonRpcErrorCodes.ParseError)]
        [TestCase("[]", McpJsonRpcErrorCodes.InvalidRequest)]
        [TestCase("{}", McpJsonRpcErrorCodes.InvalidRequest)]
        [TestCase(@"{""jsonrpc"":""1.0"",""id"":1,""method"":""ping""}", McpJsonRpcErrorCodes.InvalidRequest)]
        [TestCase(@"{""jsonrpc"":""2.0"",""id"":null,""method"":""ping""}", McpJsonRpcErrorCodes.InvalidRequest)]
        [TestCase(@"{""jsonrpc"":""2.0"",""id"":true,""method"":""ping""}", McpJsonRpcErrorCodes.InvalidRequest)]
        public void McpPostRejectsMalformedJsonRpc(string body, int expectedCode)
        {
            var response = SendMcpRawPost(body);

            AssertJsonRpcError(response, expectedCode, expectedStatus: 400);
        }

        [Test]
        public void McpInitializeRejectsSessionHeader()
        {
            var sessionId = CreateInitializedMcpSession();

            var duplicate = SendMcpPost(McpInitializeRequest(), sessionId);
            AssertJsonRpcError(
                duplicate,
                McpJsonRpcErrorCodes.InvalidRequest,
                expectedStatus: 400);

            var stale = SendMcpPost(McpInitializeRequest(), "stale-session-id");
            Assert.That(stale.Status, Is.EqualTo(404));
        }

        [Test]
        public void McpInitializedNotificationMustNotCarryId()
        {
            var sessionId = InitializeMcpSession(out var createdSessionId).Headers[ToolGatewayMcpProtocol.Headers.McpSessionId];
            Assert.That(sessionId, Is.EqualTo(createdSessionId));

            var response = SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 9,
                ["method"] = ToolGatewayMcpProtocol.Notifications.Initialized
            }, sessionId);

            AssertJsonRpcError(
                response,
                McpJsonRpcErrorCodes.InvalidRequest,
                expectedStatus: 400);
        }

        [Test]
        public void McpToolsListExposesEnabledRuntimeTools()
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var sessionId = CreateInitializedMcpSession();
                var response = SendMcpPost(McpRequest(2, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()), sessionId);
                var tools = (JArray)JObject.Parse(response.Body)["result"]?["tools"];
                var names = tools?.Select(tool => tool["name"]?.Value<string>()).ToArray();

                Assert.That(tools, Is.Not.Null);
                foreach (var toolName in BuiltinToolNames)
                    Assert.That(names, Does.Contain(toolName));

                Assert.That(names, Does.Not.Contain("ExecuteCSharp"));
                Assert.That(names, Does.Not.Contain("execute_csharp"));
                Assert.That(tools.Single(tool => tool["name"]?.Value<string>() == ExecuteCSharpToolName)
                    ["inputSchema"]?["required"]?[0]?.Value<string>(), Is.EqualTo("code"));
            });
        }

        [Test]
        public void McpToolsListValidatesParamsAndAcceptsCursor()
        {
            var sessionId = CreateInitializedMcpSession();

            var badParams = SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 20,
                ["method"] = ToolGatewayMcpProtocol.Methods.ToolsList,
                ["params"] = true
            }, sessionId);
            AssertJsonRpcError(badParams, McpJsonRpcErrorCodes.InvalidParams);

            var badCursor = SendMcpPost(McpRequest(21, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject
            {
                ["cursor"] = 123
            }), sessionId);
            AssertJsonRpcError(badCursor, McpJsonRpcErrorCodes.InvalidParams);

            var ok = SendMcpPost(McpRequest(22, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject
            {
                ["cursor"] = "ignored-for-now"
            }), sessionId);
            var root = JObject.Parse(ok.Body);
            Assert.That(ok.Status, Is.EqualTo(200));
            Assert.That(root["result"]?["tools"], Is.Not.Null);
            Assert.That(root["result"]?["nextCursor"], Is.Null);
        }

        [Test]
        public void McpToolsCallValidatesProtocolParams()
        {
            var sessionId = CreateInitializedMcpSession();

            AssertJsonRpcInvalidParams(SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 23,
                ["method"] = ToolGatewayMcpProtocol.Methods.ToolsCall
            }, sessionId));

            AssertJsonRpcInvalidParams(SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 24,
                ["method"] = ToolGatewayMcpProtocol.Methods.ToolsCall,
                ["params"] = new JObject
                {
                    ["name"] = "",
                    ["arguments"] = new JObject()
                }
            }, sessionId));

            AssertJsonRpcInvalidParams(SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 25,
                ["method"] = ToolGatewayMcpProtocol.Methods.ToolsCall,
                ["params"] = new JObject
                {
                    ["name"] = ExecuteCSharpToolName,
                    ["arguments"] = new JArray()
                }
            }, sessionId));

            AssertJsonRpcInvalidParams(SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 26,
                ["method"] = ToolGatewayMcpProtocol.Methods.ToolsCall,
                ["params"] = new JObject
                {
                    ["name"] = ExecuteCSharpToolName,
                    ["arguments"] = new JObject(),
                    ["_meta"] = new JObject
                    {
                        ["progressToken"] = new JObject()
                    }
                }
            }, sessionId));

            AssertJsonRpcInvalidParams(SendMcpToolCall(
                "missing_tool",
                new JObject(),
                sessionId,
                requestId: 27));
        }

        [Test]
        public void McpInitializedNotificationRequiresValidSessionAndEnablesOperations()
        {
            var missing = SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = ToolGatewayMcpProtocol.Notifications.Initialized
            });
            Assert.That(missing.Status, Is.EqualTo(400));

            InitializeMcpSession(out var sessionId);
            var beforeInitialized = SendMcpPost(McpRequest(10, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()), sessionId);
            var beforeRoot = JObject.Parse(beforeInitialized.Body);
            Assert.That(beforeInitialized.Status, Is.EqualTo(200));
            Assert.That(beforeRoot["error"]?["code"]?.Value<int>(), Is.EqualTo(McpProtocolErrorCodes.SessionNotInitialized));

            var ping = SendMcpPost(McpRequest(17, ToolGatewayMcpProtocol.Methods.Ping, new JObject()), sessionId);
            Assert.That(ping.Status, Is.EqualTo(200));
            Assert.That(JObject.Parse(ping.Body)["result"], Is.Not.Null);

            var initialized = SendInitializedNotification(sessionId);
            Assert.That(initialized.Status, Is.EqualTo(202));
            Assert.That(string.IsNullOrEmpty(initialized.Body), Is.True);

            var afterInitialized = SendMcpPost(McpRequest(11, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()), sessionId);
            var afterRoot = JObject.Parse(afterInitialized.Body);
            Assert.That(afterInitialized.Status, Is.EqualTo(200));
            Assert.That(afterRoot["result"]?["tools"], Is.Not.Null);
        }

        [Test]
        public void McpLoggingCapabilityIsNotDeclared()
        {
            var init = InitializeMcpSession(out var sessionId);
            var initRoot = JObject.Parse(init.Body);
            Assert.That(initRoot["result"]?["capabilities"]?["logging"], Is.Null);

            var initialized = SendInitializedNotification(sessionId);
            Assert.That(initialized.Status, Is.EqualTo(202));

            var response = SendMcpPost(McpRequest(18, ToolGatewayMcpProtocol.Methods.LoggingSetLevel, new JObject
            {
                ["level"] = "debug"
            }), sessionId);
            var root = JObject.Parse(response.Body);
            Assert.That(root["error"]?["code"]?.Value<int>(), Is.EqualTo(McpJsonRpcErrorCodes.MethodNotFound));
        }

        [Test]
        public void McpClientJsonRpcResponsesReturnAcceptedNoBody()
        {
            var sessionId = CreateInitializedMcpSession();
            var response = SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 19,
                ["result"] = new JObject()
            }, sessionId);

            Assert.That(response.Status, Is.EqualTo(202));
            Assert.That(string.IsNullOrEmpty(response.Body), Is.True);
        }

        [Test]
        public void McpRequestsRequireSessionAndRecoverAfterStaleSession()
        {
            var missing = SendMcpPost(McpRequest(12, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()));
            Assert.That(missing.Status, Is.EqualTo(400));

            var sessionId = CreateInitializedMcpSession();
            ToolGatewayMcpSessionStore.Remove(sessionId);

            var stale = SendMcpPost(McpRequest(13, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()), sessionId);
            Assert.That(stale.Status, Is.EqualTo(404));

            var reinitialized = InitializeMcpSession(out var newSessionId);
            Assert.That(reinitialized.Status, Is.EqualTo(200));
            Assert.That(newSessionId, Is.Not.EqualTo(sessionId));
        }

        [Test]
        public void McpRejectsUnsupportedProtocolVersionHeader()
        {
            var sessionId = CreateInitializedMcpSession();
            var response = SendMcpPost(
                McpRequest(14, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()),
                sessionId,
                protocolVersion: "1900-01-01");
            var root = JObject.Parse(response.Body);

            Assert.That(response.Status, Is.EqualTo(400));
            Assert.That(root["error"]?["code"]?.Value<int>(), Is.EqualTo(McpProtocolErrorCodes.UnsupportedProtocolVersion));
            Assert.That(root["error"]?["data"]?["supported"]?[0]?.Value<string>(), Is.EqualTo(McpProtocolVersion));
        }

        [Test]
        public void McpRejectsUnsupportedProtocolVersionHeaderOnInitialize()
        {
            var response = SendMcpPost(
                McpInitializeRequest(),
                protocolVersion: "1900-01-01",
                includeProtocolVersionWithoutSession: true);
            var root = JObject.Parse(response.Body);

            Assert.That(response.Status, Is.EqualTo(400));
            Assert.That(root["error"]?["code"]?.Value<int>(), Is.EqualTo(McpProtocolErrorCodes.UnsupportedProtocolVersion));
        }

        [Test]
        public void McpGetReturnsMethodNotAllowedWithoutSession()
        {
            var response = SendMcp(ToolGatewayMcpProtocol.HttpMethods.Get, string.Empty, sessionId: null);

            Assert.That(response.Status, Is.EqualTo(405));
        }

        [Test]
        public void McpDeleteTerminatesSession()
        {
            var sessionId = CreateInitializedMcpSession();
            var deleted = SendMcp(ToolGatewayMcpProtocol.HttpMethods.Delete, string.Empty, sessionId);
            Assert.That(deleted.Status, Is.EqualTo(204));

            var stale = SendMcpPost(McpRequest(15, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()), sessionId);
            Assert.That(stale.Status, Is.EqualTo(404));

            var staleDelete = SendMcp(ToolGatewayMcpProtocol.HttpMethods.Delete, string.Empty, sessionId);
            Assert.That(staleDelete.Status, Is.EqualTo(404));
        }

        [Test]
        public void McpSessionExpiresAfterIdleTimeoutAndCanReinitialize()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            ToolGatewayMcpSessionStore.SetClockForTests(() => now);
            ToolGatewayMcpSessionStore.SetIdleTimeoutForTests(TimeSpan.FromMinutes(5));

            var sessionId = CreateInitializedMcpSession();
            now = now.AddMinutes(6);

            var expired = SendMcpPost(McpRequest(16, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()), sessionId);
            Assert.That(expired.Status, Is.EqualTo(404));

            var reinitialized = InitializeMcpSession(out var newSessionId);
            Assert.That(reinitialized.Status, Is.EqualTo(200));
            Assert.That(newSessionId, Is.Not.EqualTo(sessionId));
        }

        [Test]
        public void McpSessionStoreReloadsFromPersistentProcessStore()
        {
            var sessionId = CreateInitializedMcpSession();
            ToolGatewayMcpSessionStore.ReloadFromPersistentStoreForTests();

            var response = SendMcpPost(McpRequest(16, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()), sessionId);
            var root = JObject.Parse(response.Body);

            Assert.That(response.Status, Is.EqualTo(200));
            Assert.That(root["result"]?["tools"], Is.Not.Null);
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
                var sessionId = CreateInitializedMcpSession();
                var response = SendMcpToolCall(toolName, new JObject(), sessionId, timeoutMilliseconds: 10000);

                AssertJsonRpcInvalidParams(response);
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

        [Test]
        public void McpToolsCallInjectsCancellationTokenAndCancellationReturnsAccepted()
        {
            var plugin = FindPluginTool("test_gateway_plugin_cancelable");
            s_cancelableToolStarted = NewCompletionSource();
            s_cancelableToolCancelled = NewCompletionSource();

            WithGatewaySettings(enableCSharpAutomation: false, enabledPluginToolIds: new[] { plugin.Id }, () =>
            {
                var listedTool = UnityToolGateway.Instance.ListTools()
                    .Single(tool => tool.Name == plugin.Descriptor.Name);
                Assert.That(listedTool.InputSchema["properties"]?["cancellationToken"], Is.Null);

                var sessionId = CreateInitializedMcpSession();
                var callTask = ToolGatewayHttpHandler.HandleAsync(
                    new ToolGatewayHttpRequestContext
                    {
                        Method = ToolGatewayMcpProtocol.HttpMethods.Post,
                        Target = ToolGatewayMcpProtocol.Paths.Mcp,
                        Headers = BuildMcpHeaders(sessionId),
                        Body = McpToolCallRequest(
                            31,
                            plugin.Descriptor.Name,
                            new JObject()).ToString(Formatting.None)
                    },
                    CancellationToken.None);

                Assert.That(WaitForResult(s_cancelableToolStarted.Task, timeoutMilliseconds: 10000), Is.True);

            var cancelled = SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = ToolGatewayMcpProtocol.Notifications.Cancelled,
                ["params"] = new JObject
                    {
                        ["requestId"] = 31,
                        ["reason"] = "test cancellation"
                    }
                }, sessionId);

                Assert.That(cancelled.Status, Is.EqualTo(202));
                Assert.That(WaitForResult(s_cancelableToolCancelled.Task, timeoutMilliseconds: 10000), Is.True);

                var response = WaitForResult(callTask, timeoutMilliseconds: 10000);
                Assert.That(response.Status, Is.EqualTo(202));
                Assert.That(string.IsNullOrEmpty(response.Body), Is.True);
            });
        }

        [Test]
        public void McpCancelledNotificationIgnoresUnknownRequest()
        {
            var sessionId = CreateInitializedMcpSession();
            var response = SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = ToolGatewayMcpProtocol.Notifications.Cancelled,
                ["params"] = new JObject
                {
                    ["requestId"] = "already-finished"
                }
            }, sessionId);

            Assert.That(response.Status, Is.EqualTo(202));
            Assert.That(string.IsNullOrEmpty(response.Body), Is.True);
        }

        [TestCase("ExecuteCSharp")]
        [TestCase("execute_csharp")]
        public void McpToolsCallDoesNotAcceptLegacyExecuteCSharpAliases(string toolName)
        {
            WithGatewaySettings(enableCSharpAutomation: true, enabledPluginToolIds: Array.Empty<string>(), () =>
            {
                var sessionId = CreateInitializedMcpSession();
                var response = SendMcpToolCall(
                    toolName,
                    new JObject
                    {
                        ["code"] = "return 1;",
                        ["mode"] = "editor"
                    },
                    sessionId);

                AssertJsonRpcInvalidParams(response);
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
        public void LocalServerReadsAndWritesMcpSessionHeaders()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);

            var response = WaitForResult(SendPostAsync(
                port,
                ToolGatewayMcpProtocol.Paths.Mcp,
                McpInitializeRequest().ToString(Formatting.None)),
                timeoutMilliseconds: 10000);
            var sessionId = ReadHttpHeader(response, ToolGatewayMcpProtocol.Headers.McpSessionId);

            Assert.That(response, Does.Contain("HTTP/1.1 200 OK"));
            Assert.That(sessionId, Is.Not.Empty);

            var initialized = WaitForResult(SendPostAsync(
                    port,
                    ToolGatewayMcpProtocol.Paths.Mcp,
                    new JObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["method"] = ToolGatewayMcpProtocol.Notifications.Initialized
                    }.ToString(Formatting.None),
                    new Dictionary<string, string>
                    {
                        [ToolGatewayMcpProtocol.Headers.McpSessionId] = sessionId,
                        [ToolGatewayMcpProtocol.Headers.McpProtocolVersion] = McpProtocolVersion
                    }),
                timeoutMilliseconds: 10000);
            Assert.That(initialized, Does.Contain("HTTP/1.1 202 Accepted"));

            var tools = WaitForResult(SendPostAsync(
                    port,
                    ToolGatewayMcpProtocol.Paths.Mcp,
                    McpRequest(5, ToolGatewayMcpProtocol.Methods.ToolsList, new JObject()).ToString(Formatting.None),
                    new Dictionary<string, string>
                    {
                        [ToolGatewayMcpProtocol.Headers.McpSessionId] = sessionId,
                        [ToolGatewayMcpProtocol.Headers.McpProtocolVersion] = McpProtocolVersion
                    }),
                timeoutMilliseconds: 10000);
            Assert.That(tools, Does.Contain("HTTP/1.1 200 OK"));
            Assert.That(tools, Does.Contain(@"""tools"""));

            var get = WaitForResult(SendHttpAsync(
                    port,
                    ToolGatewayMcpProtocol.HttpMethods.Get,
                    ToolGatewayMcpProtocol.Paths.Mcp),
                timeoutMilliseconds: 10000);
            Assert.That(get, Does.Contain("HTTP/1.1 405 Method Not Allowed"));

            var deleted = WaitForResult(SendHttpAsync(
                    port,
                    ToolGatewayMcpProtocol.HttpMethods.Delete,
                    ToolGatewayMcpProtocol.Paths.Mcp,
                    extraHeaders: new Dictionary<string, string>
                    {
                        [ToolGatewayMcpProtocol.Headers.McpSessionId] = sessionId,
                        [ToolGatewayMcpProtocol.Headers.McpProtocolVersion] = McpProtocolVersion
                    }),
                timeoutMilliseconds: 10000);
            Assert.That(deleted, Does.Contain("HTTP/1.1 204 No Content"));
            Assert.That(deleted, Does.Contain("Content-Length: 0"));

            server.Stop();
            AssertCanBind(port);
        }

        private static ToolGatewayHttpResponse InitializeMcpSession(
            out string sessionId,
            string requestedVersion = McpProtocolVersion)
        {
            var response = SendMcpPost(McpInitializeRequest(requestedVersion));
            Assert.That(response.Headers.TryGetValue(ToolGatewayMcpProtocol.Headers.McpSessionId, out sessionId), Is.True);
            return response;
        }

        private static string CreateInitializedMcpSession()
        {
            InitializeMcpSession(out var sessionId);
            var initialized = SendInitializedNotification(sessionId);
            Assert.That(initialized.Status, Is.EqualTo(202));
            return sessionId;
        }

        private static ToolGatewayHttpResponse SendInitializedNotification(string sessionId)
        {
            return SendMcpPost(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = ToolGatewayMcpProtocol.Notifications.Initialized
            }, sessionId);
        }

        private static JObject McpInitializeRequest(string requestedVersion = McpProtocolVersion)
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = ToolGatewayMcpProtocol.Methods.Initialize,
                ["params"] = new JObject
                {
                    ["protocolVersion"] = requestedVersion,
                    ["capabilities"] = new JObject(),
                    ["clientInfo"] = new JObject
                    {
                        ["name"] = "test",
                        ["version"] = "1"
                    }
                }
            };
        }

        private static JObject McpRequest(int id, string method, JObject @params)
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = @params ?? new JObject()
            };
        }

        private static JObject McpToolCallRequest(int id, string name, JObject arguments)
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = ToolGatewayMcpProtocol.Methods.ToolsCall,
                ["params"] = new JObject
                {
                    ["name"] = name,
                    ["arguments"] = arguments ?? new JObject()
                }
            };
        }

        private static ToolGatewayHttpResponse SendMcpPost(
            JObject body,
            string sessionId = null,
            string protocolVersion = McpProtocolVersion,
            bool includeProtocolVersionWithoutSession = false,
            int timeoutMilliseconds = 3000)
        {
            return SendMcp(
                ToolGatewayMcpProtocol.HttpMethods.Post,
                body?.ToString(Formatting.None) ?? string.Empty,
                sessionId,
                protocolVersion,
                includeProtocolVersionWithoutSession,
                timeoutMilliseconds);
        }

        private static ToolGatewayHttpResponse SendMcpRawPost(
            string body,
            string sessionId = null,
            string protocolVersion = McpProtocolVersion,
            bool includeProtocolVersionWithoutSession = false,
            int timeoutMilliseconds = 3000)
        {
            return SendMcp(
                ToolGatewayMcpProtocol.HttpMethods.Post,
                body ?? string.Empty,
                sessionId,
                protocolVersion,
                includeProtocolVersionWithoutSession,
                timeoutMilliseconds);
        }

        private static ToolGatewayHttpResponse SendMcpToolCall(
            string name,
            JObject arguments,
            string sessionId,
            int requestId = 3,
            int timeoutMilliseconds = 3000)
        {
            return SendMcpPost(
                McpToolCallRequest(requestId, name, arguments),
                sessionId,
                timeoutMilliseconds: timeoutMilliseconds);
        }

        private static ToolGatewayHttpResponse SendMcp(
            string method,
            string body,
            string sessionId = null,
            string protocolVersion = McpProtocolVersion,
            bool includeProtocolVersionWithoutSession = false,
            int timeoutMilliseconds = 3000)
        {
            var headers = BuildMcpHeaders(
                sessionId,
                protocolVersion,
                includeProtocolVersionWithoutSession);

            return WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                    new ToolGatewayHttpRequestContext
                    {
                        Method = method,
                        Target = ToolGatewayMcpProtocol.Paths.Mcp,
                        Headers = headers,
                        Body = body
                    },
                    CancellationToken.None),
                timeoutMilliseconds);
        }

        private static Dictionary<string, string> BuildMcpHeaders(
            string sessionId = null,
            string protocolVersion = McpProtocolVersion,
            bool includeProtocolVersionWithoutSession = false)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ToolGatewayMcpProtocol.Headers.Accept] =
                    $"{ToolGatewayMcpProtocol.MediaTypes.Json}, {ToolGatewayMcpProtocol.MediaTypes.EventStream}"
            };
            if (!string.IsNullOrWhiteSpace(sessionId))
                headers[ToolGatewayMcpProtocol.Headers.McpSessionId] = sessionId;
            if (!string.IsNullOrWhiteSpace(protocolVersion)
                && (!string.IsNullOrWhiteSpace(sessionId) || includeProtocolVersionWithoutSession))
            {
                headers[ToolGatewayMcpProtocol.Headers.McpProtocolVersion] = protocolVersion;
            }

            return headers;
        }

        private static void AssertJsonRpcInvalidParams(ToolGatewayHttpResponse response)
        {
            AssertJsonRpcError(response, McpJsonRpcErrorCodes.InvalidParams);
        }

        private static void AssertJsonRpcError(
            ToolGatewayHttpResponse response,
            int expectedCode,
            int expectedStatus = 200)
        {
            var root = JObject.Parse(response.Body);
            Assert.That(response.Status, Is.EqualTo(expectedStatus));
            Assert.That(root["error"]?["code"]?.Value<int>(), Is.EqualTo(expectedCode));
        }

        private static JToken CallMcpTool(string name, JObject arguments, int timeoutMilliseconds = 3000)
        {
            var sessionId = CreateInitializedMcpSession();
            var response = SendMcpPost(
                McpToolCallRequest(3, name, arguments),
                sessionId,
                timeoutMilliseconds: timeoutMilliseconds);

            Assert.That(response.Status, Is.EqualTo(200));
            return JObject.Parse(response.Body)["result"];
        }

        private static JObject GetProjectedTools(string format)
        {
            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                ToolGatewayMcpProtocol.HttpMethods.Get,
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

        private static async Task<string> SendPostAsync(
            int port,
            string target,
            string body,
            IReadOnlyDictionary<string, string> extraHeaders = null)
        {
            return await SendHttpAsync(
                port,
                "POST",
                target,
                body,
                extraHeaders).ConfigureAwait(false);
        }

        private static async Task<string> SendHttpAsync(
            int port,
            string method,
            string target,
            string body = "",
            IReadOnlyDictionary<string, string> extraHeaders = null)
        {
            using var client = new TcpClient();
            await Task.Factory.FromAsync(
                client.BeginConnect(IPAddress.Loopback, port, null, null),
                client.EndConnect).ConfigureAwait(false);

            using var stream = client.GetStream();
            var bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            var request =
                $"{method} {target} HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\n" +
                "Accept: application/json, text/event-stream\r\n";
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                request += "Content-Type: application/json\r\n";
            if (extraHeaders != null)
            {
                foreach (var header in extraHeaders)
                    request += $"{header.Key}: {header.Value}\r\n";
            }

            request +=
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);

            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        private static string ReadHttpHeader(string response, string name)
        {
            using var reader = new StringReader(response ?? string.Empty);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    break;

                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                if (string.Equals(line.Substring(0, separator), name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(separator + 1).Trim();
            }

            return string.Empty;
        }

        private static T WaitForResult<T>(Task<T> task, int timeoutMilliseconds = 3000)
        {
            if (!task.Wait(timeoutMilliseconds))
                Assert.Fail($"Timed out waiting for task after {timeoutMilliseconds} ms.");
            return task.GetAwaiter().GetResult();
        }

        private static TaskCompletionSource<bool> NewCompletionSource()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            Name = "test_gateway_plugin_cancelable",
            Description = "Long-running cancellable test plugin runtime tool.")]
        private static async Task<object> GatewayPluginCancelable(CancellationToken cancellationToken)
        {
            s_cancelableToolStarted?.TrySetResult(true);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                s_cancelableToolCancelled?.TrySetResult(true);
                throw;
            }

            return new { completed = true };
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

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.AppBinding;
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
        [Test]
        public void GatewayListsOnlyCanonicalExecuteCSharp()
        {
            var tools = UnityToolGateway.Instance.ListTools();

            Assert.That(tools.Count, Is.EqualTo(1));
            Assert.That(tools[0].Name, Is.EqualTo("ExecuteCSharp"));
            Assert.That(tools[0].InputSchema["properties"]?["code"], Is.Not.Null);
            Assert.That(tools[0].InputSchema["properties"]?["mode"]?["enum"], Is.Not.Null);
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
        }

        [Test]
        public void McpToolsListExposesOnlyExecuteCSharp()
        {
            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                "POST",
                "/dotcraft/mcp",
                @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list"",""params"":{}}",
                CancellationToken.None));
            var tools = (JArray)JObject.Parse(response.Body)["result"]?["tools"];

            Assert.That(tools, Is.Not.Null);
            Assert.That(tools, Has.Count.EqualTo(1));
            Assert.That(tools[0]?["name"]?.Value<string>(), Is.EqualTo("ExecuteCSharp"));
            Assert.That(tools[0]?["inputSchema"]?["required"]?[0]?.Value<string>(), Is.EqualTo("code"));
        }

        [Test]
        public void McpToolsCallRunsExecuteCSharp()
        {
            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                "POST",
                "/dotcraft/mcp",
                @"{""jsonrpc"":""2.0"",""id"":3,""method"":""tools/call"",""params"":{""name"":""ExecuteCSharp"",""arguments"":{""code"":""return 21 + 21;"",""mode"":""editor""}}}",
                CancellationToken.None),
                timeoutMilliseconds: 10000);
            var result = JObject.Parse(response.Body)["result"];

            Assert.That(result?["isError"]?.Value<bool>(), Is.False);
            Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.True);
            Assert.That(result?["structuredContent"]?["returnValue"]?.Value<int>(), Is.EqualTo(42));
        }

        [Test]
        public void McpToolsCallCanCreateGameObjectInEditorMode()
        {
            var name = $"DotCraft Gateway Test {Guid.NewGuid():N}";
            var body = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 33,
                ["method"] = "tools/call",
                ["params"] = new JObject
                {
                    ["name"] = "ExecuteCSharp",
                    ["arguments"] = new JObject
                    {
                        ["code"] = $"var go = new GameObject({JToken.FromObject(name).ToString(Formatting.None)}); return go.name;",
                        ["mode"] = "editor"
                    }
                }
            }.ToString(Formatting.None);

            try
            {
                var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                    "POST",
                    "/dotcraft/mcp",
                    body,
                    CancellationToken.None),
                    timeoutMilliseconds: 10000);
                var result = JObject.Parse(response.Body)["result"];

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
        }

        [Test]
        public void McpToolsCallInvalidCSharpReturnsCompilerDiagnostics()
        {
            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                "POST",
                "/dotcraft/mcp",
                @"{""jsonrpc"":""2.0"",""id"":4,""method"":""tools/call"",""params"":{""name"":""ExecuteCSharp"",""arguments"":{""code"":""return ;"",""mode"":""editor""}}}",
                CancellationToken.None),
                timeoutMilliseconds: 10000);
            var result = JObject.Parse(response.Body)["result"];

            Assert.That(result?["isError"]?.Value<bool>(), Is.True);
            Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.False);
            Assert.That(result?["structuredContent"]?["errorCode"]?.Value<string>(), Is.EqualTo("CompilationFailed"));
            Assert.That((JArray)result?["structuredContent"]?["diagnostics"], Is.Not.Empty);
        }

        [Test]
        public void McpToolsCallPlaymodeFailsClearlyWhenEditorIsNotPlaying()
        {
            Assume.That(EditorApplication.isPlaying, Is.False);

            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                "POST",
                "/dotcraft/mcp",
                @"{""jsonrpc"":""2.0"",""id"":44,""method"":""tools/call"",""params"":{""name"":""ExecuteCSharp"",""arguments"":{""code"":""return 1;"",""mode"":""playmode""}}}",
                CancellationToken.None),
                timeoutMilliseconds: 10000);
            var result = JObject.Parse(response.Body)["result"];

            Assert.That(result?["isError"]?.Value<bool>(), Is.True);
            Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.False);
            Assert.That(result?["structuredContent"]?["errorCode"]?.Value<string>(), Is.EqualTo("UnityNotInPlayMode"));
        }

        [Test]
        public void HttpToolProjectionSupportsCanonicalOpenAiAndClaudeFormats()
        {
            var canonical = GetProjectedTools("canonical");
            var responses = GetProjectedTools("openai-responses");
            var chat = GetProjectedTools("openai-chat");
            var claude = GetProjectedTools("claude");

            Assert.That(canonical["tools"]?[0]?["name"]?.Value<string>(), Is.EqualTo("ExecuteCSharp"));
            Assert.That(responses["tools"]?[0]?["type"]?.Value<string>(), Is.EqualTo("function"));
            Assert.That(responses["tools"]?[0]?["name"]?.Value<string>(), Is.EqualTo("ExecuteCSharp"));
            Assert.That(chat["tools"]?[0]?["function"]?["name"]?.Value<string>(), Is.EqualTo("ExecuteCSharp"));
            Assert.That(claude["tools"]?[0]?["input_schema"], Is.Not.Null);
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

        private static JObject GetProjectedTools(string format)
        {
            var response = WaitForResult(ToolGatewayHttpHandler.HandleAsync(
                "GET",
                $"/dotcraft/gateway/tools?format={format}",
                string.Empty,
                CancellationToken.None));
            return JObject.Parse(response.Body);
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
    }
}

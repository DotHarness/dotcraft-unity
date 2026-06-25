using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.McpSetup;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class McpSetupTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-unity-mcp-setup-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_tempRoot) && Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [Test]
        public void ClaudeProviderCreatesMergesAndUninstallsProjectMcpJson()
        {
            var provider = new ClaudeCodeMcpConfigProvider();
            var path = Path.Combine(_tempRoot, ".mcp.json");
            File.WriteAllText(path, @"{""mcpServers"":{""existing"":{""command"":""node""}}}");

            var result = provider.Install(_tempRoot, Options());
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Changed, Is.True);

            var root = JObject.Parse(File.ReadAllText(path));
            Assert.That(root["mcpServers"]?["existing"]?["command"]?.Value<string>(), Is.EqualTo("node"));
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["type"]?.Value<string>(), Is.EqualTo("http"));
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["url"]?.Value<string>(), Is.EqualTo(McpGatewaySetupDefaults.Endpoint));

            var uninstall = provider.Uninstall(_tempRoot);
            Assert.That(uninstall.Success, Is.True, uninstall.Error);
            root = JObject.Parse(File.ReadAllText(path));
            Assert.That(root["mcpServers"]?["existing"], Is.Not.Null);
            Assert.That(root["mcpServers"]?["dotcraft-unity"], Is.Null);
        }

        [Test]
        public void CursorProviderCreatesProjectMcpJson()
        {
            var provider = new CursorMcpConfigProvider();
            var result = provider.Install(_tempRoot, Options());

            Assert.That(result.Success, Is.True, result.Error);
            var path = Path.Combine(_tempRoot, ".cursor", "mcp.json");
            var root = JObject.Parse(File.ReadAllText(path));
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["url"]?.Value<string>(), Is.EqualTo(McpGatewaySetupDefaults.Endpoint));
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["type"], Is.Null);
        }

        [Test]
        public void CodexProviderReplacesManagedBlockAndPreservesUnrelatedToml()
        {
            var provider = new CodexMcpConfigProvider();
            var path = Path.Combine(_tempRoot, ".codex", "config.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "[model]\nname = \"gpt-5\"\n\n[mcp_servers.dotcraft_unity]\nurl = \"http://old/mcp\"\nenabled = false\n\n[mcp_servers.dotcraft_unity.tools.old]\napproval_mode = \"approve\"\n\n[mcp_servers.other]\ncommand = \"node\"\n");

            var result = provider.Install(_tempRoot, Options());
            Assert.That(result.Success, Is.True, result.Error);

            var toml = File.ReadAllText(path);
            Assert.That(toml, Does.Contain("[model]"));
            Assert.That(toml, Does.Contain("[mcp_servers.other]"));
            Assert.That(toml, Does.Contain("# BEGIN dotcraft-unity MCP Gateway"));
            Assert.That(toml, Does.Contain($"url = \"{McpGatewaySetupDefaults.Endpoint}\""));
            Assert.That(toml, Does.Not.Contain("http://old/mcp"));
            Assert.That(toml, Does.Not.Contain("[mcp_servers.dotcraft_unity.tools.old]"));

            var uninstall = provider.Uninstall(_tempRoot);
            Assert.That(uninstall.Success, Is.True, uninstall.Error);
            toml = File.ReadAllText(path);
            Assert.That(toml, Does.Contain("[model]"));
            Assert.That(toml, Does.Contain("[mcp_servers.other]"));
            Assert.That(toml, Does.Not.Contain("[mcp_servers.dotcraft_unity]"));
        }

        [Test]
        public void CodexProviderDoesNotWriteToolAllowlist()
        {
            var provider = new CodexMcpConfigProvider();
            var result = provider.Install(_tempRoot, Options());

            Assert.That(result.Success, Is.True, result.Error);
            var toml = File.ReadAllText(Path.Combine(_tempRoot, ".codex", "config.toml"));
            Assert.That(toml, Does.Not.Contain("enabled_tools"));
        }

        [Test]
        public void InstallerCreatesBackupAndRepeatedInstallIsIdempotent()
        {
            var provider = new CodexMcpConfigProvider();
            var path = Path.Combine(_tempRoot, ".codex", "config.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "approval_policy = \"on-request\"\n");

            var first = provider.Install(_tempRoot, Options());
            Assert.That(first.Success, Is.True, first.Error);
            Assert.That(first.Changed, Is.True);
            Assert.That(first.BackupPath, Is.Not.Empty);
            Assert.That(File.Exists(first.BackupPath), Is.True);

            var second = provider.Install(_tempRoot, Options());
            Assert.That(second.Success, Is.True, second.Error);
            Assert.That(second.Changed, Is.False);

            var toml = File.ReadAllText(path);
            Assert.That(CountOccurrences(toml, "[mcp_servers.dotcraft_unity]"), Is.EqualTo(1));
        }

        [Test]
        public void JsonProviderRejectsInvalidJsonWithoutWriting()
        {
            var provider = new ClaudeCodeMcpConfigProvider();
            var path = Path.Combine(_tempRoot, ".mcp.json");
            File.WriteAllText(path, "{ invalid");

            var result = provider.Install(_tempRoot, Options());
            Assert.That(result.Success, Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ invalid"));
        }

        [Test]
        public async Task GatewayProbeReportsStoppedServer()
        {
            var probe = new McpGatewayStatusProbe((_, _, _, _) =>
                throw new InvalidOperationException("connection refused"));

            var result = await probe.ProbeAsync(McpGatewaySetupDefaults.Endpoint, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("Stopped"));
            Assert.That(result.Error, Does.Contain("connection refused"));
        }

        [Test]
        public async Task GatewayProbeReportsInitializeFailure()
        {
            var probe = new McpGatewayStatusProbe((_, _, _, _) => Task.FromResult(
                new McpGatewayProbeHttpResponse(200, @"{""jsonrpc"":""2.0"",""id"":1,""error"":{""code"":-32601,""message"":""nope""}}")));

            var result = await probe.ProbeAsync(McpGatewaySetupDefaults.Endpoint, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("MCP error"));
            Assert.That(result.Error, Does.Contain("nope"));
        }

        [Test]
        public async Task GatewayProbeReportsInvalidJson()
        {
            var probe = new McpGatewayStatusProbe((_, _, _, _) => Task.FromResult(
                new McpGatewayProbeHttpResponse(200, "not json")));

            var result = await probe.ProbeAsync(McpGatewaySetupDefaults.Endpoint, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("Invalid response"));
        }

        [Test]
        public async Task GatewayProbeReportsNoTools()
        {
            var probe = new McpGatewayStatusProbe(SuccessfulProbeResponder(Array.Empty<string>()));

            var result = await probe.ProbeAsync(McpGatewaySetupDefaults.Endpoint, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ToolCount, Is.EqualTo(0));
            Assert.That(result.Status, Is.EqualTo("Connected, no tools exposed"));
        }

        [Test]
        public async Task GatewayProbeReportsToolNames()
        {
            var probe = new McpGatewayStatusProbe(SuccessfulProbeResponder("unity_execute_csharp"));

            var result = await probe.ProbeAsync(McpGatewaySetupDefaults.Endpoint, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ToolNames, Is.EqualTo(new[] { "unity_execute_csharp" }));
        }

        private static McpInstallOptions Options() =>
            new(McpGatewaySetupDefaults.Endpoint);

        private static Func<string, string, string, CancellationToken, Task<McpGatewayProbeHttpResponse>> SuccessfulProbeResponder(
            params string[] toolNames)
        {
            return (_, _, body, _) =>
            {
                var method = JObject.Parse(body).Value<string>("method");
                if (method == "initialize")
                {
                    return Task.FromResult(new McpGatewayProbeHttpResponse(200,
                        @"{""jsonrpc"":""2.0"",""id"":1,""result"":{""capabilities"":{""tools"":{}}}}"));
                }

                var tools = new JArray(toolNames.Select(name => new JObject { ["name"] = name }));
                var response = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 2,
                    ["result"] = new JObject { ["tools"] = tools }
                };
                return Task.FromResult(new McpGatewayProbeHttpResponse(200, response.ToString()));
            };
        }

        private static int CountOccurrences(string value, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }

            return count;
        }
    }
}

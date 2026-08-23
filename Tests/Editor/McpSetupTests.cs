using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DotCraft.Editor.McpSetup;
using UnityEditor;
using UnityEngine;
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
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["type"]?.Value<string>(), Is.EqualTo("stdio"));
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["command"]?.Value<string>(), Is.EqualTo(GatewayCommand));
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["args"]?.Values<string>(),
                Is.EqualTo(new[] { "--project-root", _tempRoot }));

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
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["command"]?.Value<string>(), Is.EqualTo(GatewayCommand));
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["args"]?.Values<string>(),
                Is.EqualTo(new[] { "--project-root", _tempRoot }));
            Assert.That(root["mcpServers"]?["dotcraft-unity"]?["type"], Is.Null);
        }

        [Test]
        public void CodexProviderReplacesManagedBlockAndPreservesUnrelatedToml()
        {
            var provider = new CodexMcpConfigProvider();
            var path = Path.Combine(_tempRoot, ".codex", "config.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "[model]\nname = \"gpt-5\"\n\n[mcp_servers.other]\ncommand = \"node\"\n");

            var result = provider.Install(_tempRoot, Options());
            Assert.That(result.Success, Is.True, result.Error);

            var toml = File.ReadAllText(path);
            Assert.That(toml, Does.Contain("[model]"));
            Assert.That(toml, Does.Contain("[mcp_servers.other]"));
            Assert.That(toml, Does.Contain("# BEGIN dotcraft-unity MCP Gateway"));
            Assert.That(toml, Does.Contain($"command = \"{GatewayCommand.Replace("\\", "\\\\")}\""));
            Assert.That(toml, Does.Contain("args = [\"--project-root\""));

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
        public void ProvidersExposeProjectSkillTargets()
        {
            Assert.That(new CodexMcpConfigProvider().SkillRelativePath, Is.EqualTo(".agents/skills/dotcraft-unity"));
            Assert.That(new CursorMcpConfigProvider().SkillRelativePath, Is.EqualTo(".agents/skills/dotcraft-unity"));
            Assert.That(new ClaudeCodeMcpConfigProvider().SkillRelativePath, Is.EqualTo(".claude/skills/dotcraft-unity"));
        }

        [Test]
        public void AgentSkillInstallerInstallsFetchedSkillFiles()
        {
            var installer = SkillInstaller(
                SkillFile("SKILL.md", "---\nname: dotcraft-unity\ndescription: Test\n---\n"),
                SkillFile("references/api.md", "# API\n"));

            var result = installer.Install(_tempRoot, ".agents/skills/dotcraft-unity");

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Changed, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_tempRoot, ".agents", "skills", "dotcraft-unity", "SKILL.md")),
                Does.Contain("dotcraft-unity"));
            Assert.That(File.Exists(Path.Combine(_tempRoot, ".agents", "skills", "dotcraft-unity", "references", "api.md")),
                Is.True);
        }

        [Test]
        public void AgentSkillInstallerIsIdempotentWhenFilesMatch()
        {
            var installer = SkillInstaller(SkillFile("SKILL.md", "---\nname: dotcraft-unity\ndescription: Test\n---\n"));

            var first = installer.Install(_tempRoot, ".agents/skills/dotcraft-unity");
            var second = installer.Install(_tempRoot, ".agents/skills/dotcraft-unity");

            Assert.That(first.Success, Is.True, first.Error);
            Assert.That(first.Changed, Is.True);
            Assert.That(second.Success, Is.True, second.Error);
            Assert.That(second.Changed, Is.False);
        }

        [Test]
        public void AgentSkillInstallerReplacesExistingChangedSkill()
        {
            var target = Path.Combine(_tempRoot, ".agents", "skills", "dotcraft-unity");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "SKILL.md"), "old");
            var installer = SkillInstaller(SkillFile("SKILL.md", "new"));

            var result = installer.Install(_tempRoot, ".agents/skills/dotcraft-unity");

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Changed, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(target, "SKILL.md")), Is.EqualTo("new"));
        }

        [Test]
        public void AgentSkillInstallerRejectsPathsOutsideProjectRoot()
        {
            var installer = SkillInstaller(SkillFile("SKILL.md", "test"));

            var result = installer.Install(_tempRoot, "../dotcraft-unity-skill");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("project root"));
        }

        [Test]
        public void AgentSkillInstallerRequiresSkillMarkdown()
        {
            var installer = SkillInstaller(SkillFile("references/api.md", "# API\n"));

            var result = installer.Install(_tempRoot, ".agents/skills/dotcraft-unity");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("SKILL.md"));
        }

        [Test]
        public void LocalPackageSkillSourceReportsMissingBundledSkill()
        {
            var missing = Path.Combine(_tempRoot, "missing-skill");
            var ex = Assert.Throws<InvalidOperationException>(
                () => new LocalPackageAgentSkillSourceFetcher(missing).Fetch());

            Assert.That(ex.Message, Does.Contain("Bundled dotcraft-unity skill not found in package."));
        }

        [Test]
        public void McpSetupWindowConstructorDoesNotCreateSkillInstaller()
        {
            var window = ScriptableObject.CreateInstance<McpGatewaySetupWindow>();
            try
            {
                var field = typeof(McpGatewaySetupWindow).GetField(
                    "_skillInstaller",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(field.GetValue(window), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void InstallerUpdatesAndRepeatedInstallIsIdempotent()
        {
            var provider = new CodexMcpConfigProvider();
            var path = Path.Combine(_tempRoot, ".codex", "config.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "approval_policy = \"on-request\"\n");

            var first = provider.Install(_tempRoot, Options());
            Assert.That(first.Success, Is.True, first.Error);
            Assert.That(first.Changed, Is.True);

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

        private string GatewayCommand => Path.Combine(_tempRoot, "dotcraft-unity-mcp.exe");

        private McpInstallOptions Options() =>
            new(GatewayCommand, _tempRoot);

        private static AgentSkillInstaller SkillInstaller(params AgentSkillFile[] files) =>
            new(new MemorySkillSourceFetcher(files));

        private static AgentSkillFile SkillFile(string path, string content) =>
            new(path, Encoding.UTF8.GetBytes(content));

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

        private sealed class MemorySkillSourceFetcher : IAgentSkillSourceFetcher
        {
            private readonly IReadOnlyList<AgentSkillFile> _files;

            public MemorySkillSourceFetcher(IReadOnlyList<AgentSkillFile> files)
            {
                _files = files;
            }

            public IReadOnlyList<AgentSkillFile> Fetch() => _files;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class UnityToolGatewayDiscovery
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("packageVersion")]
        public string PackageVersion { get; set; }

        [JsonProperty("processId")]
        public int ProcessId { get; set; }

        [JsonProperty("endpoint")]
        public string Endpoint { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }
    }

    internal sealed class UnityToolManifest
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("packageVersion")]
        public string PackageVersion { get; set; }

        [JsonProperty("revision")]
        public string Revision { get; set; }

        [JsonProperty("tools")]
        public IReadOnlyList<UnityToolSpec> Tools { get; set; } = Array.Empty<UnityToolSpec>();
    }

    internal sealed class UnityToolGatewayState
    {
        public const int SchemaVersion = 1;
        public const string TokenHeader = "X-DotCraft-Unity-Token";
        public const string SessionHeader = "X-DotCraft-Unity-Session";

        private readonly string _discoveryPath;
        private readonly string _manifestPath;
        private readonly object _gate = new();
        private UnityToolManifest _manifest;

        public UnityToolGatewayState(string projectRoot = null)
        {
            var root = string.IsNullOrWhiteSpace(projectRoot)
                ? Directory.GetParent(Application.dataPath)?.FullName
                : Path.GetFullPath(projectRoot);
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Unity project root could not be resolved.");

            var stateDirectory = Path.Combine(root, "UserSettings", "DotCraft");
            _discoveryPath = Path.Combine(stateDirectory, "dotcraft-unity.json");
            _manifestPath = Path.Combine(stateDirectory, "tools.json");
        }

        public string DiscoveryPath => _discoveryPath;

        public string ManifestPath => _manifestPath;

        public UnityToolManifest CurrentManifest
        {
            get
            {
                lock (_gate)
                    return _manifest;
            }
        }

        public UnityToolManifest RefreshManifest()
        {
            var tools = UnityToolRegistry.Instance.ListTools()
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .Select(CloneTool)
                .ToArray();
            var canonicalTools = new JArray(tools.Select(tool => new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description ?? string.Empty,
                ["inputSchema"] = tool.InputSchema?.DeepClone() ?? new JObject { ["type"] = "object" }
            }));
            var revision = "sha256:" + ComputeSha256(canonicalTools.ToString(Formatting.None));
            var manifest = new UnityToolManifest
            {
                SchemaVersion = SchemaVersion,
                PackageVersion = DotCraftPackageInfo.Version,
                Revision = revision,
                Tools = tools
            };

            lock (_gate)
            {
                if (_manifest == null || !string.Equals(_manifest.Revision, revision, StringComparison.Ordinal))
                    WriteAtomic(_manifestPath, DotCraftJson.Serialize(manifest));
                _manifest = manifest;
            }

            return manifest;
        }

        public UnityToolGatewayDiscovery PublishDiscovery(string endpoint, string token)
        {
            var discovery = new UnityToolGatewayDiscovery
            {
                SchemaVersion = SchemaVersion,
                PackageVersion = DotCraftPackageInfo.Version,
                ProcessId = Process.GetCurrentProcess().Id,
                Endpoint = endpoint,
                Token = token
            };
            WriteAtomic(_discoveryPath, DotCraftJson.Serialize(discovery));
            return discovery;
        }

        public void RemoveDiscovery(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || !File.Exists(_discoveryPath))
                return;

            try
            {
                var discovery = JsonConvert.DeserializeObject<UnityToolGatewayDiscovery>(
                    File.ReadAllText(_discoveryPath));
                if (discovery != null
                    && discovery.ProcessId == Process.GetCurrentProcess().Id
                    && string.Equals(discovery.Token, token, StringComparison.Ordinal))
                {
                    File.Delete(_discoveryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        internal static string CreateToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static UnityToolSpec CloneTool(UnityToolSpec tool)
        {
            return new UnityToolSpec
            {
                Name = tool.Name,
                Description = tool.Description ?? string.Empty,
                InputSchema = (JObject)(tool.InputSchema?.DeepClone() ?? new JObject { ["type"] = "object" })
            };
        }

        private static string ComputeSha256(string value)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
            return string.Concat(hash.Select(valueByte => valueByte.ToString("x2")));
        }

        private static void WriteAtomic(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, content ?? string.Empty, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.ToolGateway;
using Newtonsoft.Json;
using UnityEditor.PackageManager;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpGatewayArtifactManifest
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("rid")]
        public string RuntimeIdentifier { get; set; }

        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }
    }

    internal sealed class McpGatewayInstallStatus
    {
        public bool IsInstalled { get; set; }

        public string Version { get; set; }

        public string ExecutablePath { get; set; }

        public string Error { get; set; }
    }

    internal sealed class McpGatewayVersionInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("rid")]
        public string RuntimeIdentifier { get; set; }

        [JsonProperty("mcpSdkVersion")]
        public string McpSdkVersion { get; set; }
    }

    internal static class McpGatewayInstaller
    {
        private const string GatewayFileName = "dotcraft-unity-mcp.exe";
        private const string ArtifactManifestFileName = "gateway-artifact.json";
        private const string ThirdPartyNoticesFileName = "THIRD-PARTY-NOTICES.txt";
        private const string ReleaseBaseUrl = "https://github.com/DotHarness/dotcraft-unity/releases/download/";
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

        private static string InstalledDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft",
            "unity",
            "mcp-gateway",
            DotCraftPackageInfo.Version);

        public static string InstalledExecutablePath => Path.Combine(InstalledDirectory, GatewayFileName);

        private static string InstalledManifestPath => Path.Combine(InstalledDirectory, ArtifactManifestFileName);

        public static McpGatewayInstallStatus GetStatus()
        {
            var path = InstalledExecutablePath;
            try
            {
                if (!File.Exists(path) && !File.Exists(InstalledManifestPath))
                {
                    return new McpGatewayInstallStatus
                    {
                        IsInstalled = false,
                        Version = DotCraftPackageInfo.Version,
                        ExecutablePath = path
                    };
                }

                var artifact = ReadArtifactManifest(InstalledManifestPath);
                ValidateArtifactManifest(artifact);
                var valid = File.Exists(path)
                            && string.Equals(ComputeSha256(path), artifact.Sha256, StringComparison.OrdinalIgnoreCase)
                            && ValidateExecutableVersion(path, artifact.Version, out _);
                return new McpGatewayInstallStatus
                {
                    IsInstalled = valid,
                    Version = artifact.Version,
                    ExecutablePath = path,
                    Error = valid ? null : "Installed MCP Gateway failed version or SHA-256 validation."
                };
            }
            catch (Exception ex)
            {
                return new McpGatewayInstallStatus
                {
                    IsInstalled = false,
                    Version = DotCraftPackageInfo.Version,
                    ExecutablePath = path,
                    Error = ex.Message
                };
            }
        }

        public static async Task<McpInstallResult> InstallAsync()
        {
            var current = GetStatus();
            if (current.IsInstalled)
                return new McpInstallResult(true, current.ExecutablePath, false, message: "MCP Gateway is already current.");

            Directory.CreateDirectory(InstalledDirectory);
            var executableTemporaryPath = InstalledExecutablePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var manifestTemporaryPath = InstalledManifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var manifestJson = await HttpClient.GetStringAsync(GetReleaseAssetUri(ArtifactManifestFileName));
                var artifact = DotCraftJson.Deserialize<McpGatewayArtifactManifest>(manifestJson);
                ValidateArtifactManifest(artifact);

                await DownloadFileAsync(GetReleaseAssetUri(artifact.FileName), executableTemporaryPath);
                if (!string.Equals(ComputeSha256(executableTemporaryPath), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded MCP Gateway failed SHA-256 validation.");
                if (!ValidateExecutableVersion(executableTemporaryPath, artifact.Version, out var versionError))
                    throw new InvalidDataException(versionError);

                ReplaceFile(executableTemporaryPath, InstalledExecutablePath);
                File.WriteAllText(manifestTemporaryPath, manifestJson);
                ReplaceFile(manifestTemporaryPath, InstalledManifestPath);
                InstallThirdPartyNotices();

                return new McpInstallResult(true, InstalledExecutablePath, true, message: "MCP Gateway installed.");
            }
            catch (Exception ex)
            {
                return McpInstallResult.Failed(InstalledExecutablePath, ex.Message);
            }
            finally
            {
                DeleteIfExists(executableTemporaryPath);
                DeleteIfExists(manifestTemporaryPath);
            }
        }

        private static Uri GetReleaseAssetUri(string fileName) =>
            new($"{ReleaseBaseUrl}v{DotCraftPackageInfo.Version}/{fileName}");

        private static async Task DownloadFileAsync(Uri uri, string destination)
        {
            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var source = await response.Content.ReadAsStreamAsync();
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target);
        }

        private static McpGatewayArtifactManifest ReadArtifactManifest(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Installed MCP Gateway artifact manifest is missing.", path);

            var artifact = DotCraftJson.Deserialize<McpGatewayArtifactManifest>(File.ReadAllText(path));
            if (artifact == null)
                throw new InvalidDataException("MCP Gateway artifact manifest is invalid.");
            return artifact;
        }

        private static void ValidateArtifactManifest(McpGatewayArtifactManifest artifact)
        {
            if (artifact == null
                || !string.Equals(artifact.Version, DotCraftPackageInfo.Version, StringComparison.Ordinal)
                || !string.Equals(artifact.RuntimeIdentifier, "win-x64", StringComparison.Ordinal)
                || !string.Equals(artifact.FileName, GatewayFileName, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(artifact.Sha256))
            {
                throw new InvalidDataException("MCP Gateway release manifest does not match the package.");
            }
        }

        private static void InstallThirdPartyNotices()
        {
            var package = PackageInfo.FindForAssetPath($"Packages/{DotCraftPackageInfo.PackageId}");
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
                throw new InvalidOperationException("DotCraft Unity package path could not be resolved.");

            var source = Path.Combine(package.resolvedPath, "Tools~", "McpGateway", ThirdPartyNoticesFileName);
            File.Copy(source, Path.Combine(InstalledDirectory, ThirdPartyNoticesFileName), true);
        }

        private static void ReplaceFile(string source, string destination)
        {
            if (File.Exists(destination))
                File.Replace(source, destination, null);
            else
                File.Move(source, destination);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }

        private static bool ValidateExecutableVersion(string path, string expectedVersion, out string error)
        {
            error = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "version --json",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    error = "MCP Gateway version process could not be started.";
                    return false;
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(10000))
                {
                    process.Kill();
                    error = "MCP Gateway version check timed out.";
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    error = $"MCP Gateway version check failed with exit code {process.ExitCode}: {stderr}";
                    return false;
                }

                var version = DotCraftJson.Deserialize<McpGatewayVersionInfo>(stdout);
                if (version == null
                    || !string.Equals(version.Version, expectedVersion, StringComparison.Ordinal)
                    || !string.Equals(version.RuntimeIdentifier, "win-x64", StringComparison.Ordinal)
                    || !string.Equals(version.McpSdkVersion, "2.2.0", StringComparison.Ordinal))
                {
                    error = "MCP Gateway executable version metadata does not match the package artifact.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"MCP Gateway version validation failed: {ex.Message}";
                return false;
            }
        }
    }
}

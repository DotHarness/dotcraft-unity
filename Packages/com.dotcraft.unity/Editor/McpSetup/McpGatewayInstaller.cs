using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DotCraft.Editor.Execution;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.ToolGateway;
using Newtonsoft.Json;

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

        [JsonProperty("protocolVersion")]
        public int ProtocolVersion { get; set; }

    }

    internal static class McpGatewayInstaller
    {
        private const string GatewayFileName = "dotcraft-unity.exe";
        private const string ArtifactManifestFileName = "gateway-artifact.json";
        private const string ThirdPartyNoticesFileName = "THIRD-PARTY-NOTICES.txt";
        private const string InstalledNoticesFileName = "dotcraft-unity.NOTICES.txt";
        private const string ReleaseBaseUrl = "https://github.com/DotHarness/dotcraft-unity/releases/latest/download/";
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

        private static string RootDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft",
            "bin");

        public static string InstalledExecutablePath => Path.Combine(RootDirectory, GatewayFileName);

        public static bool HasExecutable => File.Exists(InstalledExecutablePath);

        public static McpGatewayInstallStatus GetStatus()
        {
            var path = InstalledExecutablePath;
            try
            {
                if (!File.Exists(path))
                {
                    return new McpGatewayInstallStatus
                    {
                        IsInstalled = false,
                        ExecutablePath = path
                    };
                }

                var valid = ValidateExecutable(path, expectedVersion: null, out var version, out var error);
                return new McpGatewayInstallStatus
                {
                    IsInstalled = valid,
                    Version = version?.Version,
                    ExecutablePath = path,
                    Error = valid ? null : error
                };
            }
            catch (Exception ex)
            {
                return new McpGatewayInstallStatus
                {
                    IsInstalled = false,
                    ExecutablePath = path,
                    Error = ex.Message
                };
            }
        }

        public static async Task<McpInstallResult> InstallAsync()
        {
            var current = GetStatus();
            if (current.IsInstalled)
                return new McpInstallResult(true, current.ExecutablePath, false, message: "dotcraft-unity CLI is already compatible.");

            Directory.CreateDirectory(RootDirectory);
            var executableTemporaryPath = InstalledExecutablePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var noticesPath = Path.Combine(RootDirectory, InstalledNoticesFileName);
            var noticesTemporaryPath = noticesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var manifestJson = await HttpClient.GetStringAsync(GetReleaseAssetUri(ArtifactManifestFileName));
                var artifact = DotCraftJson.Deserialize<McpGatewayArtifactManifest>(manifestJson);
                ValidateArtifactManifest(artifact);

                await DownloadFileAsync(GetReleaseAssetUri(artifact.FileName), executableTemporaryPath);
                if (!string.Equals(ComputeSha256(executableTemporaryPath), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded dotcraft-unity CLI failed SHA-256 validation.");
                if (!ValidateExecutable(executableTemporaryPath, artifact.Version, out _, out var versionError))
                    throw new InvalidDataException(versionError);

                await DownloadFileAsync(GetReleaseAssetUri(ThirdPartyNoticesFileName), noticesTemporaryPath);
                ReplaceFile(noticesTemporaryPath, noticesPath);
                ReplaceFile(executableTemporaryPath, InstalledExecutablePath);
                AddToUserPath(RootDirectory);

                return new McpInstallResult(true, InstalledExecutablePath, true, message: "dotcraft-unity CLI installed.");
            }
            catch (Exception ex)
            {
                return McpInstallResult.Failed(InstalledExecutablePath, ex.Message);
            }
            finally
            {
                DeleteIfExists(executableTemporaryPath);
                DeleteIfExists(noticesTemporaryPath);
            }
        }

        private static Uri GetReleaseAssetUri(string fileName) =>
            new($"{ReleaseBaseUrl}{fileName}");

        private static async Task DownloadFileAsync(Uri uri, string destination)
        {
            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var source = await response.Content.ReadAsStreamAsync();
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target);
        }

        private static void ValidateArtifactManifest(McpGatewayArtifactManifest artifact)
        {
            if (artifact == null
                || string.IsNullOrWhiteSpace(artifact.Version)
                || !string.Equals(artifact.RuntimeIdentifier, "win-x64", StringComparison.Ordinal)
                || !string.Equals(artifact.FileName, GatewayFileName, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(artifact.Sha256))
            {
                throw new InvalidDataException("dotcraft-unity release manifest is invalid.");
            }
        }

        private static void AddToUserPath(string directory)
        {
            var fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
            var entries = userPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!entries.Any(entry => string.Equals(entry.TrimEnd('\\', '/'), fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(fullPath);
                Environment.SetEnvironmentVariable("Path", string.Join(";", entries), EnvironmentVariableTarget.User);
            }
            var processEntries = (Environment.GetEnvironmentVariable("Path") ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!processEntries.Any(entry => string.Equals(entry.TrimEnd('\\', '/'), fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                processEntries.Add(fullPath);
                Environment.SetEnvironmentVariable("Path", string.Join(";", processEntries));
            }
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

        private static bool ValidateExecutable(string path, string expectedVersion,
            out McpGatewayVersionInfo version, out string error)
        {
            version = null;
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
                    error = "dotcraft-unity version process could not be started.";
                    return false;
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(10000))
                {
                    process.Kill();
                    error = "dotcraft-unity version check timed out.";
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    error = $"dotcraft-unity version check failed with exit code {process.ExitCode}: {stderr}";
                    return false;
                }

                version = DotCraftJson.Deserialize<McpGatewayVersionInfo>(stdout);
                if (version == null
                    || !string.Equals(version.RuntimeIdentifier, "win-x64", StringComparison.Ordinal)
                    || version.ProtocolVersion != ExternalCSharpCompiler.ProtocolVersion
                    || (!string.IsNullOrWhiteSpace(expectedVersion)
                        && !string.Equals(version.Version, expectedVersion, StringComparison.Ordinal)))
                {
                    error = "dotcraft-unity executable metadata or protocol is unsupported.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"dotcraft-unity version validation failed: {ex.Message}";
                return false;
            }
        }
    }
}

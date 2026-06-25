using System;
using System.IO;

namespace DotCraft.Editor.McpSetup
{
    internal abstract class McpClientConfigProviderBase : IMcpClientConfigProvider
    {
        public abstract string DisplayName { get; }

        public abstract string RelativePath { get; }

        public virtual bool IsRecommendedByDefault => true;

        public bool IsConfigured(string projectRoot)
        {
            var path = ResolvePath(projectRoot);
            var before = ReadExisting(path);
            if (string.IsNullOrWhiteSpace(before))
                return false;

            var preview = PreviewUninstall(path, before);
            return preview.IsValid && preview.HasChanges;
        }

        public abstract string GetSetupHint(McpInstallOptions options);

        public abstract McpPatchPreview Preview(string projectRoot, McpInstallOptions options);

        public McpInstallResult Install(string projectRoot, McpInstallOptions options)
        {
            var preview = Preview(projectRoot, options);
            if (!preview.IsValid)
                return McpInstallResult.Failed(preview.Path, preview.Error);

            if (!preview.HasChanges)
                return new McpInstallResult(true, preview.Path, false, message: "Already up to date.");

            try
            {
                var directory = Path.GetDirectoryName(preview.Path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var backupPath = ConfigBackup.CreateBackupIfExists(preview.Path);
                File.WriteAllText(preview.Path, preview.After);
                return new McpInstallResult(
                    true,
                    preview.Path,
                    true,
                    backupPath,
                    string.IsNullOrEmpty(backupPath) ? "Installed." : "Installed with backup.");
            }
            catch (Exception ex)
            {
                return McpInstallResult.Failed(preview.Path, ex.Message);
            }
        }

        public McpInstallResult Uninstall(string projectRoot)
        {
            var path = ResolvePath(projectRoot);
            var before = ReadExisting(path);
            var preview = PreviewUninstall(path, before);
            if (!preview.IsValid)
                return McpInstallResult.Failed(path, preview.Error);

            if (!preview.HasChanges)
                return new McpInstallResult(true, path, false, message: "No dotcraft-unity server block found.");

            try
            {
                var backupPath = ConfigBackup.CreateBackupIfExists(path);
                File.WriteAllText(path, preview.After);
                return new McpInstallResult(
                    true,
                    path,
                    true,
                    backupPath,
                    string.IsNullOrEmpty(backupPath) ? "Removed." : "Removed with backup.");
            }
            catch (Exception ex)
            {
                return McpInstallResult.Failed(path, ex.Message);
            }
        }

        protected string ResolvePath(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("Project root is required.", nameof(projectRoot));

            return Path.Combine(projectRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        protected static string ReadExisting(string path) =>
            File.Exists(path) ? File.ReadAllText(path) : string.Empty;

        protected abstract McpPatchPreview PreviewUninstall(string path, string before);
    }
}

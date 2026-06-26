using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal interface IAgentSkillSourceFetcher
    {
        IReadOnlyList<AgentSkillFile> Fetch();
    }

    internal sealed class AgentSkillFile
    {
        public AgentSkillFile(string relativePath, byte[] content)
        {
            RelativePath = NormalizeRelativePath(relativePath);
            Content = content ?? Array.Empty<byte>();
        }

        public string RelativePath { get; }

        public byte[] Content { get; }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Skill file path is required.", nameof(relativePath));

            return relativePath.Replace('\\', '/').Trim('/');
        }
    }

    internal sealed class AgentSkillInstallResult
    {
        public AgentSkillInstallResult(
            bool success,
            string path,
            bool changed,
            string backupPath = null,
            string message = null,
            string error = null)
        {
            Success = success;
            Path = path ?? string.Empty;
            Changed = changed;
            BackupPath = backupPath ?? string.Empty;
            Message = message ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }

        public string Path { get; }

        public bool Changed { get; }

        public string BackupPath { get; }

        public string Message { get; }

        public string Error { get; }

        public static AgentSkillInstallResult Failed(string path, string error) =>
            new(false, path, false, error: error);
    }

    internal sealed class AgentSkillInstaller
    {
        private readonly IAgentSkillSourceFetcher _fetcher;

        public AgentSkillInstaller(IAgentSkillSourceFetcher fetcher)
        {
            _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        }

        public static AgentSkillInstaller CreateDefault() =>
            new(new LocalPackageAgentSkillSourceFetcher());

        public AgentSkillInstallResult Install(string projectRoot, string skillRelativePath)
        {
            string targetPath = null;
            string tempPath = null;
            try
            {
                targetPath = ResolveTargetPath(projectRoot, skillRelativePath);
                var files = _fetcher.Fetch();
                ValidateSkillFiles(files);

                if (Directory.Exists(targetPath) && DirectoryMatches(targetPath, files))
                {
                    return new AgentSkillInstallResult(
                        true,
                        targetPath,
                        false,
                        message: "Skill already up to date.");
                }

                var parentPath = Path.GetDirectoryName(targetPath);
                if (string.IsNullOrEmpty(parentPath))
                    throw new InvalidOperationException("Skill target directory is invalid.");

                Directory.CreateDirectory(parentPath);
                tempPath = Path.Combine(
                    parentPath,
                    Path.GetFileName(targetPath) + ".tmp-" + Guid.NewGuid().ToString("N"));

                WriteSkillFiles(tempPath, files);

                var backupPath = string.Empty;
                if (Directory.Exists(targetPath))
                {
                    backupPath = UniqueBackupPath(targetPath);
                    Directory.Move(targetPath, backupPath);
                }

                try
                {
                    Directory.Move(tempPath, targetPath);
                    tempPath = null;
                }
                catch
                {
                    RestoreBackupIfNeeded(targetPath, backupPath);
                    throw;
                }

                return new AgentSkillInstallResult(
                    true,
                    targetPath,
                    true,
                    backupPath,
                    string.IsNullOrEmpty(backupPath) ? "Skill installed." : "Skill installed with backup.");
            }
            catch (Exception ex)
            {
                return AgentSkillInstallResult.Failed(targetPath, ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPath) && Directory.Exists(tempPath))
                    Directory.Delete(tempPath, recursive: true);
            }
        }

        private static void ValidateSkillFiles(IReadOnlyList<AgentSkillFile> files)
        {
            if (files == null || files.Count == 0)
                throw new InvalidOperationException("Skill source is empty.");

            if (!files.Any(file => string.Equals(file.RelativePath, "SKILL.md", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Skill source has no SKILL.md.");

            foreach (var file in files)
                ValidateRelativeFilePath(file.RelativePath);
        }

        private static bool DirectoryMatches(string targetPath, IReadOnlyList<AgentSkillFile> files)
        {
            var expected = files
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var actual = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativeFilePath(GetRelativePath(targetPath, path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (actual.Length != expected.Length)
                return false;

            for (var i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(actual[i], expected[i].RelativePath, StringComparison.OrdinalIgnoreCase))
                    return false;

                var actualPath = Path.Combine(targetPath, actual[i].Replace('/', Path.DirectorySeparatorChar));
                if (!File.ReadAllBytes(actualPath).SequenceEqual(expected[i].Content))
                    return false;
            }

            return true;
        }

        private static void WriteSkillFiles(string targetPath, IReadOnlyList<AgentSkillFile> files)
        {
            Directory.CreateDirectory(targetPath);
            foreach (var file in files)
            {
                var path = ResolveFilePath(targetPath, file.RelativePath);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, file.Content);
            }
        }

        private static string ResolveTargetPath(string projectRoot, string skillRelativePath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(skillRelativePath))
                throw new ArgumentException("Skill path is required.", nameof(skillRelativePath));
            if (Path.IsPathRooted(skillRelativePath))
                throw new InvalidOperationException("Skill path must be project-relative.");

            var root = Path.GetFullPath(projectRoot);
            var target = Path.GetFullPath(Path.Combine(
                root,
                skillRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            EnsureWithinDirectory(root, target, "Skill target must stay inside the project root.");
            return target;
        }

        private static string ResolveFilePath(string skillRoot, string relativeFilePath)
        {
            ValidateRelativeFilePath(relativeFilePath);
            var root = Path.GetFullPath(skillRoot);
            var path = Path.GetFullPath(Path.Combine(
                root,
                relativeFilePath.Replace('/', Path.DirectorySeparatorChar)));

            EnsureWithinDirectory(root, path, "Skill file path must stay inside the skill directory.");
            return path;
        }

        private static void ValidateRelativeFilePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidOperationException("Skill file path is empty.");
            if (Path.IsPathRooted(relativePath))
                throw new InvalidOperationException("Skill file path must be relative.");

            var parts = relativePath.Replace('\\', '/').Split('/');
            if (parts.Any(part => part == ".." || part.Length == 0))
                throw new InvalidOperationException("Skill file path contains an invalid segment.");
        }

        private static void EnsureWithinDirectory(string rootPath, string path, string message)
        {
            var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(path);
            var comparison = Environment.OSVersion.Platform == PlatformID.Unix
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (!target.StartsWith(root, comparison))
                throw new InvalidOperationException(message);
        }

        private static string UniqueBackupPath(string targetPath)
        {
            var basePath = targetPath + ".bak-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var path = basePath;
            var index = 1;
            while (Directory.Exists(path) || File.Exists(path))
            {
                path = basePath + "-" + index;
                index++;
            }

            return path;
        }

        private static void RestoreBackupIfNeeded(string targetPath, string backupPath)
        {
            if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath) || Directory.Exists(targetPath))
                return;

            Directory.Move(backupPath, targetPath);
        }

        private static string GetRelativePath(string rootPath, string path)
        {
            var rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(rootPath)));
            var pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static string NormalizeRelativeFilePath(string relativePath) =>
            relativePath.Replace('\\', '/').Trim('/');
    }
}

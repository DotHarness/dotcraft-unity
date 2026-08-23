using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class LocalPackageAgentSkillSourceFetcher : IAgentSkillSourceFetcher
    {
        private const string SkillRelativePath = "Plugins~/dotcraft-unity/skills/dotcraft-unity";

        private readonly string _sourcePath;

        public LocalPackageAgentSkillSourceFetcher()
            : this(ResolveBundledSkillPath())
        {
        }

        internal LocalPackageAgentSkillSourceFetcher(string sourcePath)
        {
            _sourcePath = sourcePath ?? string.Empty;
        }

        public IReadOnlyList<AgentSkillFile> Fetch()
        {
            if (string.IsNullOrWhiteSpace(_sourcePath) || !Directory.Exists(_sourcePath))
                throw new InvalidOperationException("Bundled dotcraft-unity skill not found in package.");

            var root = Path.GetFullPath(_sourcePath);
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(path => new AgentSkillFile(GetRelativePath(root, path), File.ReadAllBytes(path)))
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();
        }

        private static string ResolveBundledSkillPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AgentSkillInstaller).Assembly);
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                return string.Empty;

            return Path.Combine(packageInfo.resolvedPath, SkillRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetRelativePath(string rootPath, string path)
        {
            var rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(rootPath)));
            var pathUri = new Uri(Path.GetFullPath(path));
            var relative = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
            return relative.Replace('\\', '/').Trim('/');
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
    }
}

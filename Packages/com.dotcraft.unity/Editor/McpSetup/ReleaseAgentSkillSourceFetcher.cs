using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using DotCraft.Editor.ToolGateway;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class ReleaseAgentSkillSourceFetcher : IAgentSkillSourceFetcher
    {
        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(1) };
        private readonly string _version = DotCraftPackageInfo.Version;

        public async Task<IReadOnlyList<AgentSkillFile>> FetchAsync()
        {
            var uri = $"https://github.com/DotHarness/dotcraft-unity/releases/download/v{_version}/dotcraft-unity-skill-{_version}.zip";
            return ReadArchive(await Client.GetByteArrayAsync(uri));
        }

        internal static IReadOnlyList<AgentSkillFile> ReadArchive(byte[] bytes)
        {
            const string prefix = "skills/dotcraft-unity/";
            using var stream = new MemoryStream(bytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var files = new List<AgentSkillFile>();
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName.Replace('\\', '/');
                if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal))
                    continue;

                using var source = entry.Open();
                using var content = new MemoryStream();
                source.CopyTo(content);
                files.Add(new AgentSkillFile(path.Substring(prefix.Length), content.ToArray()));
            }
            return files;
        }
    }
}

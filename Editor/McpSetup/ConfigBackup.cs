using System;
using System.IO;

namespace DotCraft.Editor.McpSetup
{
    internal static class ConfigBackup
    {
        public static string CreateBackupIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var backupPath = path + "." + timestamp + ".bak";
            var suffix = 1;
            while (File.Exists(backupPath))
            {
                backupPath = path + "." + timestamp + "." + suffix + ".bak";
                suffix++;
            }

            File.Copy(path, backupPath);
            return backupPath;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DotCraft.Editor.Connection
{
    internal readonly struct ProcessLaunchCommand
    {
        public ProcessLaunchCommand(string fileName, string arguments, string resolvedCommandPath)
        {
            FileName = fileName;
            Arguments = arguments ?? string.Empty;
            ResolvedCommandPath = resolvedCommandPath;
        }

        public string FileName { get; }
        public string Arguments { get; }
        public string ResolvedCommandPath { get; }
    }

    internal static class ProcessCommandResolver
    {
        private static readonly string[] DefaultWindowsPathExtensions =
        {
            ".COM",
            ".EXE",
            ".BAT",
            ".CMD"
        };

        public static ProcessLaunchCommand Resolve(
            string command,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            var normalizedCommand = NormalizeCommand(command);
            var normalizedArguments = arguments ?? string.Empty;

#if UNITY_EDITOR_WIN
            var resolvedCommand = ResolveWindowsCommandPath(
                normalizedCommand,
                workingDirectory,
                environmentVariables);

            if (IsWindowsBatchFile(resolvedCommand))
            {
                return new ProcessLaunchCommand(
                    GetCommandProcessor(environmentVariables),
                    BuildWindowsBatchFileArguments(resolvedCommand, normalizedArguments),
                    resolvedCommand);
            }

            return new ProcessLaunchCommand(resolvedCommand, normalizedArguments, resolvedCommand);
#else
            return new ProcessLaunchCommand(normalizedCommand, normalizedArguments, normalizedCommand);
#endif
        }

        internal static string ResolveWindowsCommandPath(
            string command,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            var normalizedCommand = NormalizeCommand(command);
            if (string.IsNullOrWhiteSpace(normalizedCommand))
                return normalizedCommand;

            var pathExtensions = GetWindowsPathExtensions(environmentVariables);

            if (LooksLikePath(normalizedCommand))
            {
                var candidate = MakePathAbsolute(normalizedCommand, workingDirectory);
                return TryResolveExecutableCandidate(candidate, pathExtensions, out var resolved)
                    ? resolved
                    : normalizedCommand;
            }

            foreach (var directory in GetWindowsSearchPath(environmentVariables))
            {
                var candidate = CombinePathSafely(directory, normalizedCommand);
                if (candidate == null)
                    continue;

                if (TryResolveExecutableCandidate(candidate, pathExtensions, out var resolved))
                    return resolved;
            }

            if (IsDotCraftCommandName(normalizedCommand))
            {
                foreach (var candidate in GetDotCraftDesktopInstallCandidates(environmentVariables))
                {
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return normalizedCommand;
        }

        internal static bool IsWindowsCommandResolvable(
            string command,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            var resolved = ResolveWindowsCommandPath(command, workingDirectory, environmentVariables);
            return !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved);
        }

        internal static bool IsWindowsBatchFile(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
        }

        internal static string BuildWindowsBatchFileArguments(string batchFilePath, string arguments)
        {
            var commandLine = DotCraftProcessManager.QuoteCommandLineArgument(batchFilePath);
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                commandLine += " " + arguments;
            }

            return "/d /s /c \"" + EscapeForCmdExe(commandLine) + "\"";
        }

        internal static string NormalizeCommand(string command)
        {
            var normalized = command?.Trim() ?? string.Empty;
            if (normalized.Length >= 2
                && ((normalized[0] == '"' && normalized[^1] == '"')
                    || (normalized[0] == '\'' && normalized[^1] == '\'')))
            {
                normalized = normalized.Substring(1, normalized.Length - 2).Trim();
            }

            return normalized;
        }

        private static string GetCommandProcessor(IReadOnlyDictionary<string, string> environmentVariables)
        {
            var configured = GetEnvironmentValue(environmentVariables, "COMSPEC");
            return string.IsNullOrWhiteSpace(configured) ? "cmd.exe" : configured.Trim();
        }

        private static bool LooksLikePath(string command)
        {
            return Path.IsPathRooted(command)
                   || command.IndexOf(Path.DirectorySeparatorChar) >= 0
                   || command.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
        }

        private static string MakePathAbsolute(string path, string workingDirectory)
        {
            if (Path.IsPathRooted(path))
                return path;

            var baseDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory;

            try
            {
                return Path.GetFullPath(Path.Combine(baseDirectory, path));
            }
            catch
            {
                return path;
            }
        }

        private static string CombinePathSafely(string directory, string command)
        {
            try
            {
                return Path.Combine(directory, command);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryResolveExecutableCandidate(
            string candidate,
            IReadOnlyList<string> pathExtensions,
            out string resolved)
        {
            resolved = null;
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            if (File.Exists(candidate))
            {
                resolved = candidate;
                return true;
            }

            if (!string.IsNullOrEmpty(Path.GetExtension(candidate)))
                return false;

            foreach (var extension in pathExtensions)
            {
                var extendedCandidate = candidate + extension;
                if (File.Exists(extendedCandidate))
                {
                    resolved = extendedCandidate;
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<string> GetWindowsPathExtensions(
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            var raw = GetEnvironmentValue(environmentVariables, "PATHEXT");
            if (string.IsNullOrWhiteSpace(raw))
                return DefaultWindowsPathExtensions;

            var extensions = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(';'))
            {
                var extension = part.Trim();
                if (string.IsNullOrWhiteSpace(extension))
                    continue;

                if (!extension.StartsWith(".", StringComparison.Ordinal))
                    extension = "." + extension;

                if (seen.Add(extension))
                    extensions.Add(extension);
            }

            return extensions.Count == 0 ? DefaultWindowsPathExtensions : extensions;
        }

        private static IEnumerable<string> GetWindowsSearchPath(
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            var raw = GetEnvironmentValue(environmentVariables, "PATH");
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(Path.PathSeparator))
            {
                var directory = NormalizeCommand(part);
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                if (seen.Add(directory))
                    yield return directory;
            }
        }

        private static bool IsDotCraftCommandName(string command)
        {
            var fileName = Path.GetFileName(command);
            return string.Equals(fileName, "dotcraft", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(fileName, "dotcraft.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetDotCraftDesktopInstallCandidates(
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            var localAppData = GetEnvironmentValue(environmentVariables, "LOCALAPPDATA");
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(
                    localAppData,
                    "Programs",
                    "dotcraft-desktop",
                    "DotCraftDesktop",
                    "resources",
                    "bin",
                    "dotcraft.exe");
                yield return Path.Combine(
                    localAppData,
                    "Programs",
                    "DotCraftDesktop",
                    "resources",
                    "bin",
                    "dotcraft.exe");
                yield return Path.Combine(
                    localAppData,
                    "Programs",
                    "DotCraft",
                    "resources",
                    "bin",
                    "dotcraft.exe");
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "DotCraft", "resources", "bin", "dotcraft.exe");
                yield return Path.Combine(programFiles, "DotCraftDesktop", "resources", "bin", "dotcraft.exe");
            }
        }

        private static string GetEnvironmentValue(
            IReadOnlyDictionary<string, string> environmentVariables,
            string key)
        {
            if (environmentVariables != null)
            {
                foreach (var kv in environmentVariables)
                {
                    if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
                }
            }

            return Environment.GetEnvironmentVariable(key);
        }

        private static string EscapeForCmdExe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '%':
                        builder.Append("%%");
                        break;
                    case '^':
                        builder.Append("^^");
                        break;
                    case '&':
                    case '|':
                    case '<':
                    case '>':
                        builder.Append('^').Append(ch);
                        break;
                    default:
                        builder.Append(ch);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}

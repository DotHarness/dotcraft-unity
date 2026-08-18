using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Connection;
using DotCraft.Editor.Settings;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class DotCraftProcessManagerTests
    {
        [Test]
        public void ResolveWindowsCommandPath_UsesPathAndPathext()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var executablePath = Path.Combine(tempDirectory, "dotcraft.exe");
                File.WriteAllText(executablePath, string.Empty);

                var environment = new Dictionary<string, string>
                {
                    ["PATH"] = tempDirectory,
                    ["PATHEXT"] = ".EXE;.CMD"
                };

                var resolved = ProcessCommandResolver.ResolveWindowsCommandPath(
                    "dotcraft",
                    tempDirectory,
                    environment);

                Assert.That(resolved, Is.EqualTo(executablePath).IgnoreCase);
            }
            finally
            {
                DeleteDirectory(tempDirectory);
            }
        }

        [Test]
        public void ResolveWindowsCommandPath_UsesDotCraftDesktopInstallFallback()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var executablePath = Path.Combine(
                    tempDirectory,
                    "Programs",
                    "dotcraft-desktop",
                    "DotCraftDesktop",
                    "resources",
                    "bin",
                    "dotcraft.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
                File.WriteAllText(executablePath, string.Empty);

                var environment = new Dictionary<string, string>
                {
                    ["PATH"] = string.Empty,
                    ["PATHEXT"] = ".EXE;.CMD",
                    ["LOCALAPPDATA"] = tempDirectory
                };

                var resolved = ProcessCommandResolver.ResolveWindowsCommandPath(
                    "dotcraft",
                    tempDirectory,
                    environment);

                Assert.That(resolved, Is.EqualTo(executablePath));
            }
            finally
            {
                DeleteDirectory(tempDirectory);
            }
        }

        [Test]
        public void ResolveWindowsCommandPath_HandlesQuotedAbsoluteExecutablePath()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var executablePath = Path.Combine(tempDirectory, "dotcraft.exe");
                File.WriteAllText(executablePath, string.Empty);

                var resolved = ProcessCommandResolver.ResolveWindowsCommandPath(
                    $"\"{executablePath}\"",
                    tempDirectory,
                    null);

                Assert.That(resolved, Is.EqualTo(executablePath));
            }
            finally
            {
                DeleteDirectory(tempDirectory);
            }
        }

        [Test]
        public void ResolveWindowsCommandPath_ReturnsOriginalCommandWhenMissing()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var environment = new Dictionary<string, string>
                {
                    ["PATH"] = tempDirectory,
                    ["PATHEXT"] = ".EXE;.CMD"
                };

                const string command = "definitely_missing_dotcraft_command";
                var resolved = ProcessCommandResolver.ResolveWindowsCommandPath(
                    command,
                    tempDirectory,
                    environment);

                Assert.That(resolved, Is.EqualTo(command));
                Assert.That(
                    ProcessCommandResolver.IsWindowsCommandResolvable(command, tempDirectory, environment),
                    Is.False);
            }
            finally
            {
                DeleteDirectory(tempDirectory);
            }
        }

        [Test]
        public void QuoteCommandLineArgument_DoesNotDoubleInteriorBackslashes()
        {
            var quoted = DotCraftProcessManager.QuoteCommandLineArgument(
                @"C:\Program Files\DotCraft\dotcraft.cmd");

            Assert.That(quoted, Is.EqualTo(@"""C:\Program Files\DotCraft\dotcraft.cmd"""));
        }

        [Test]
        public void BuildWindowsBatchFileArguments_EscapesCmdSensitiveRemoteUrlCharacters()
        {
            var remote = DotCraftProcessManager.QuoteCommandLineArgument(
                "ws://127.0.0.1/ws?token=a%2Fb&x=1");

            var arguments = ProcessCommandResolver.BuildWindowsBatchFileArguments(
                @"C:\Tools\dotcraft.cmd",
                "-acp --remote " + remote);

            Assert.That(arguments, Does.Contain("/d /s /c"));
            Assert.That(arguments, Does.Contain("dotcraft.cmd"));
            Assert.That(arguments, Does.Contain("a%%2Fb"));
            Assert.That(arguments, Does.Contain("^&x=1"));
        }

#if UNITY_EDITOR_WIN
        [Test]
        public async Task StartAsync_ConcurrentCallsStartOneProcessAndOneErrorReader()
        {
            var tempDirectory = CreateTempDirectory();
            var builderEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBuilder = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var builderCalls = 0;
            var stderrLines = new List<string>();
            var stderrReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var powershell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoLogo -NoProfile -NonInteractive -Command \"[Console]::Error.WriteLine('single-flight-stderr'); Start-Sleep -Seconds 30\"",
                WorkingDirectory = tempDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var manager = new DotCraftProcessManager(async (_, ct) =>
            {
                Interlocked.Increment(ref builderCalls);
                builderEntered.TrySetResult(true);
                using var registration = ct.Register(() => releaseBuilder.TrySetCanceled());
                await releaseBuilder.Task;
                return startInfo;
            });
            manager.OnErrorOutput += line =>
            {
                lock (stderrLines)
                    stderrLines.Add(line);
                if (line == "single-flight-stderr")
                    stderrReceived.TrySetResult(true);
            };

            try
            {
                var settings = new DotCraftSettings
                {
                    AgentConnection = DotCraftSettings.AgentConnectionCustomAcp,
                    DotCraftCommand = powershell,
                    DotCraftArguments = startInfo.Arguments,
                    WorkspacePath = tempDirectory
                };

                var starts = Enumerable.Range(0, 8)
                    .Select(_ => manager.StartAsync(settings))
                    .ToArray();

                await builderEntered.Task;
                releaseBuilder.TrySetResult(true);

                Assert.That(await Task.WhenAll(starts), Is.All.EqualTo(true));
                Assert.That(builderCalls, Is.EqualTo(1));
                Assert.That(manager.ProcessStartCountForTests, Is.EqualTo(1));
                Assert.That(manager.ErrorReaderStartCountForTests, Is.EqualTo(1));

                var stderrCompleted = await Task.WhenAny(
                    stderrReceived.Task,
                    Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.That(stderrCompleted, Is.SameAs(stderrReceived.Task));
                lock (stderrLines)
                    Assert.That(stderrLines.Count(line => line == "single-flight-stderr"), Is.EqualTo(1));

                await manager.StopAsync(TimeSpan.FromMilliseconds(200));
                Assert.That(manager.IsAlive, Is.False);
            }
            finally
            {
                releaseBuilder.TrySetResult(true);
                manager.Kill();
                DeleteDirectory(tempDirectory);
            }
        }

        [Test]
        public void CreateProcessStartInfo_ResolvesBareExecutableOnWindows()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var executablePath = Path.Combine(tempDirectory, "dotcraft.exe");
                File.WriteAllText(executablePath, string.Empty);

                var environment = new Dictionary<string, string>
                {
                    ["PATH"] = tempDirectory,
                    ["PATHEXT"] = ".EXE;.CMD"
                };

                var startInfo = DotCraftProcessManager.CreateProcessStartInfo(
                    "dotcraft",
                    "hub",
                    tempDirectory,
                    environment,
                    redirectStreams: false);

                Assert.That(startInfo.FileName, Is.EqualTo(executablePath).IgnoreCase);
                Assert.That(startInfo.Arguments, Is.EqualTo("hub"));
                Assert.That(startInfo.UseShellExecute, Is.False);
            }
            finally
            {
                DeleteDirectory(tempDirectory);
            }
        }

        [Test]
        public void CreateProcessStartInfo_WrapsBatchFileThroughCmdOnWindows()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var batchPath = Path.Combine(tempDirectory, "dotcraft.cmd");
                File.WriteAllText(batchPath, "@echo off");

                var startInfo = DotCraftProcessManager.CreateProcessStartInfo(
                    batchPath,
                    "-acp --remote " + DotCraftProcessManager.QuoteCommandLineArgument("ws://127.0.0.1/ws?token=a%2Fb"),
                    tempDirectory,
                    null,
                    redirectStreams: true);

                Assert.That(Path.GetFileName(startInfo.FileName), Is.EqualTo("cmd.exe").IgnoreCase);
                Assert.That(startInfo.Arguments, Does.Contain("/d /s /c"));
                Assert.That(startInfo.Arguments, Does.Contain("dotcraft.cmd"));
                Assert.That(startInfo.Arguments, Does.Contain("a%%2Fb"));
                Assert.That(startInfo.RedirectStandardInput, Is.True);
                Assert.That(startInfo.UseShellExecute, Is.False);
            }
            finally
            {
                DeleteDirectory(tempDirectory);
            }
        }
#endif

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "dotcraft-unity-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteDirectory(string directory)
        {
            try
            {
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for editor tests.
            }
        }
    }
}

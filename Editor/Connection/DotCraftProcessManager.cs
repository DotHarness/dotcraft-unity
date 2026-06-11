using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Settings;
using Debug = UnityEngine.Debug;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// Manages the DotCraft subprocess lifecycle.
    /// Handles starting, stopping, monitoring, and restarting the process.
    /// </summary>
    public sealed class DotCraftProcessManager : IDisposable
    {
        private Process _process;
        private readonly StringBuilder _errorOutput = new();
        private readonly DotCraftHubClient _hubClient = new();
        private Task _errorReadTask;
        private CancellationTokenSource _errorReadCts;

        public event Action OnProcessExited;
        public event Action<string> OnErrorOutput;

        public bool IsAlive => _process != null && !_process.HasExited;
        public Process Process => _process;
        public int? ProcessId => _process?.Id;
        public DateTime? StartTime { get; private set; }

        /// <summary>
        /// Starts the DotCraft process with redirected stdio.
        /// </summary>
        public bool Start(DotCraftSettings settings)
        {
            return Task.Run(() => StartAsync(settings)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Starts the configured ACP process. DotCraft local mode first resolves the AppServer via Hub.
        /// </summary>
        public async Task<bool> StartAsync(DotCraftSettings settings, CancellationToken ct = default)
        {
            if (IsAlive)
            {
                Debug.LogWarning("[DotCraft] Process is already running.");
                return true;
            }

            _errorOutput.Clear();

            ProcessStartInfo startInfo = null;
            try
            {
                startInfo = await BuildProcessStartInfoAsync(settings, ct);
                _process = Process.Start(startInfo);

                if (_process == null)
                {
                    Debug.LogError("[DotCraft] Failed to start process.");
                    return false;
                }

                StartTime = DateTime.Now;

                // Start reading stderr in background
                _errorReadCts = new CancellationTokenSource();
                _errorReadTask = ReadErrorOutputAsync(_errorReadCts.Token);

                // Set up exit handler
                _process.EnableRaisingEvents = true;
                _process.Exited += HandleProcessExited;

                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] Process started (PID: {_process.Id})");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError(FormatStartFailureMessage(ex, startInfo, settings));
                return false;
            }
        }

        /// <summary>
        /// Stops the DotCraft process gracefully.
        /// </summary>
        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (_process == null || _process.HasExited)
                return;

            timeout ??= TimeSpan.FromSeconds(3);

            try
            {
                // Close stdin to signal graceful shutdown
                _process.StandardInput.Close();

                // Wait for graceful exit
                if (await WaitForExitAsync(_process, timeout.Value))
                {
                    if (DotCraftSettings.Instance.VerboseLogging)
                    {
                        Debug.Log("[DotCraft] Process exited gracefully.");
                    }
                }
                else
                {
                    // Force kill if timeout
                    _process.Kill();
                    Debug.LogWarning("[DotCraft] Process was force-killed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] Error stopping process: {ex.Message}");
                try
                {
                    _process?.Kill();
                }
                catch
                {
                    // ignored
                }
            }
            finally
            {
                CleanupProcess();
            }
        }

        /// <summary>
        /// Kills the process immediately.
        /// </summary>
        public void Kill()
        {
            if (_process == null || _process.HasExited)
                return;

            try
            {
                _process.Kill();
                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log("[DotCraft] Process killed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] Error killing process: {ex.Message}");
            }
            finally
            {
                CleanupProcess();
            }
        }

        /// <summary>
        /// Restarts the DotCraft process.
        /// </summary>
        public async Task<bool> RestartAsync(DotCraftSettings settings, CancellationToken ct = default)
        {
            await StopAsync();
            await Task.Delay(500, ct);
            return await StartAsync(settings, ct);
        }

        private async Task<ProcessStartInfo> BuildProcessStartInfoAsync(
            DotCraftSettings settings,
            CancellationToken ct)
        {
            var command = settings.DotCraftCommand?.Trim();
            if (string.IsNullOrWhiteSpace(command))
                throw new InvalidOperationException("DotCraft command is not configured.");

            if (settings.AgentConnection == DotCraftSettings.AgentConnectionCustomAcp)
            {
                return CreateProcessStartInfo(
                    command,
                    settings.DotCraftArguments ?? "",
                    settings.EffectiveWorkspacePath,
                    settings.EnvironmentVariables,
                    redirectStreams: true);
            }

            var args = await BuildDotCraftAcpBridgeArgumentsAsync(settings, ct);
            return CreateProcessStartInfo(
                command,
                args,
                settings.EffectiveWorkspacePath,
                settings.EnvironmentVariables,
                redirectStreams: true);
        }

        private async Task<string> BuildDotCraftAcpBridgeArgumentsAsync(
            DotCraftSettings settings,
            CancellationToken ct)
        {
            string endpoint;
            if (settings.DotCraftAppServer == DotCraftSettings.DotCraftAppServerRemote)
            {
                endpoint = settings.RemoteAppServerUrl?.Trim();
                if (string.IsNullOrWhiteSpace(endpoint))
                    throw new InvalidOperationException("Remote AppServer URL is not configured.");
            }
            else
            {
                endpoint = await _hubClient.EnsureAppServerWebSocketAsync(
                    settings.DotCraftCommand,
                    settings.EffectiveWorkspacePath,
                    settings.EnvironmentVariables,
                    ct);
            }

            var builder = new StringBuilder();
            builder.Append("-acp --remote ");
            builder.Append(QuoteCommandLineArgument(endpoint));

            if (settings.DotCraftAppServer == DotCraftSettings.DotCraftAppServerRemote
                && !string.IsNullOrWhiteSpace(settings.RemoteAppServerToken))
            {
                builder.Append(" --token ");
                builder.Append(QuoteCommandLineArgument(settings.RemoteAppServerToken.Trim()));
            }

            return builder.ToString();
        }

        internal static ProcessStartInfo CreateProcessStartInfo(
            string command,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables,
            bool redirectStreams)
        {
#if UNITY_EDITOR_OSX
            // macOS: Use zsh -cl to properly load PATH.
            var fileName = "/bin/zsh";
            var processArguments = $"-cl {QuoteShellArgument($"{QuoteCommandLineArgument(command)} {arguments}")}";
#else
            var launchCommand = ProcessCommandResolver.Resolve(
                command,
                arguments,
                workingDirectory,
                environmentVariables);
            var fileName = launchCommand.FileName;
            var processArguments = launchCommand.Arguments;
#endif

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = processArguments,
                RedirectStandardInput = redirectStreams,
                RedirectStandardOutput = redirectStreams,
                RedirectStandardError = redirectStreams,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            if (redirectStreams)
            {
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
            }

            // Inject environment variables
            if (environmentVariables != null)
            {
                foreach (var kv in environmentVariables)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                    {
                        startInfo.EnvironmentVariables[kv.Key] = kv.Value ?? "";
                    }
                }
            }

            return startInfo;
        }

        internal static string QuoteCommandLineArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            if (value.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"', '%', '&', '|', '<', '>', '^' }) < 0)
                return value;

            var builder = new StringBuilder();
            builder.Append('"');

            var backslashCount = 0;
            foreach (var ch in value)
            {
                if (ch == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (ch == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append('"');
                    backslashCount = 0;
                    continue;
                }

                builder.Append('\\', backslashCount);
                backslashCount = 0;
                builder.Append(ch);
            }

            builder.Append('\\', backslashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private static string QuoteShellArgument(string value)
        {
            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }

        private static string FormatStartFailureMessage(
            Exception exception,
            ProcessStartInfo startInfo,
            DotCraftSettings settings)
        {
            var builder = new StringBuilder();
            builder.Append("[DotCraft] Failed to start process: ");
            builder.Append(RedactSensitiveText(exception.Message));

            if (startInfo != null)
            {
                builder.AppendLine();
                builder.Append("OriginalCommand='");
                builder.Append(RedactSensitiveText(settings?.DotCraftCommand ?? ""));
                builder.Append("', ApplicationName='");
                builder.Append(RedactSensitiveText(startInfo.FileName ?? ""));
                builder.Append("', Arguments='");
                builder.Append(RedactSensitiveText(startInfo.Arguments ?? ""));
                builder.Append("', WorkingDirectory='");
                builder.Append(startInfo.WorkingDirectory ?? "");
                builder.Append("'.");
            }

#if UNITY_EDITOR_WIN
            builder.AppendLine();
            builder.Append(
                "Hint: On Windows, if Unity cannot resolve the command from PATH, " +
                "set Project Settings > DotCraft > DotCraft Command to the full path of dotcraft.exe.");
#endif

            return builder.ToString();
        }

        private static string RedactSensitiveText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var redacted = Regex.Replace(
                value,
                @"(?i)([?&]token=)[^'""\s&]+",
                "$1<redacted>");

            return Regex.Replace(
                redacted,
                @"(?i)(--token\s+)(?:""[^""]*""|'[^']*'|\S+)",
                "$1<redacted>");
        }

        private async Task ReadErrorOutputAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _process != null && !_process.HasExited)
                {
                    var line = await _process.StandardError.ReadLineAsync();
                    if (line == null) break;

                    _errorOutput.AppendLine(line);
                    OnErrorOutput?.Invoke(line);

                    if (DotCraftSettings.Instance.VerboseLogging)
                    {
                        Debug.Log($"[DotCraft stderr] {line}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Suppress I/O exceptions that result from the process/stream being
                // disposed during an intentional shutdown (CTS already cancelled).
                if (!ct.IsCancellationRequested)
                    Debug.LogWarning($"[DotCraft] Error reading stderr: {ex.Message}");
            }
        }

        private void HandleProcessExited(object sender, EventArgs e)
        {
            if (DotCraftSettings.Instance.VerboseLogging)
            {
                Debug.Log("[DotCraft] Process exited.");
            }

            OnProcessExited?.Invoke();
        }

        private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();

            process.Exited += OnExited;

            if (process.HasExited)
            {
                process.Exited -= OnExited;
                return true;
            }

            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(timeout)
            );

            return completedTask == tcs.Task;

            void OnExited(object sender, EventArgs e)
            {
                process.Exited -= OnExited;
                tcs.TrySetResult(true);
            }
        }

        private void CleanupProcess()
        {
            if (_errorReadCts != null)
            {
                _errorReadCts.Cancel();
                _errorReadCts.Dispose();
                _errorReadCts = null;
            }

            try
            {
                _errorReadTask?.Wait(TimeSpan.FromMilliseconds(200));
            }
            catch
            {
                // ignored
            }

            if (_process != null)
            {
                _process.Exited -= HandleProcessExited;
                _process.Dispose();
                _process = null;
            }

            StartTime = null;
        }

        public void Dispose()
        {
            StopAsync().Wait(TimeSpan.FromSeconds(5));
            CleanupProcess();
        }
    }
}

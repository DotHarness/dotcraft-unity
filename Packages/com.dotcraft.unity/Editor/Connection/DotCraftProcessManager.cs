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
        private readonly StringBuilder _errorOutput = new();
        private readonly DotCraftHubClient _hubClient = new();
        private readonly Func<DotCraftSettings, CancellationToken, Task<ProcessStartInfo>> _startInfoBuilderOverride;
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly CancellationTokenSource _disposeCts = new();
        private readonly object _stateSync = new();
        private ProcessContext _current;
        private CancellationTokenSource _activeStartCts;
        private bool _disposed;

        public DotCraftProcessManager()
        {
        }

        internal DotCraftProcessManager(
            Func<DotCraftSettings, CancellationToken, Task<ProcessStartInfo>> startInfoBuilderOverride)
        {
            _startInfoBuilderOverride = startInfoBuilderOverride;
        }

        public event Action OnProcessExited;
        public event Action<string> OnErrorOutput;

        public bool IsAlive => IsProcessAlive(CurrentContext?.Process);
        public Process Process => CurrentContext?.Process;
        public int? ProcessId => CurrentContext?.Process.Id;
        public DateTime? StartTime { get; private set; }

        internal int ProcessStartCountForTests { get; private set; }
        internal int ErrorReaderStartCountForTests { get; private set; }

        private ProcessContext CurrentContext
        {
            get
            {
                lock (_stateSync)
                    return _current;
            }
        }

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
            using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            var gateAcquired = false;
            try
            {
                await _lifecycleGate.WaitAsync(startCts.Token);
                gateAcquired = true;
                lock (_stateSync)
                    _activeStartCts = startCts;
                return await StartCoreAsync(settings, startCts.Token);
            }
            catch (OperationCanceledException) when (startCts.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                lock (_stateSync)
                {
                    if (ReferenceEquals(_activeStartCts, startCts))
                        _activeStartCts = null;
                }

                if (gateAcquired)
                    _lifecycleGate.Release();
            }
        }

        /// <summary>
        /// Stops the DotCraft process gracefully.
        /// </summary>
        public async Task StopAsync(TimeSpan? timeout = null)
        {
            CancelActiveStart();
            timeout ??= TimeSpan.FromSeconds(3);
            await _lifecycleGate.WaitAsync();
            try
            {
                await StopCoreAsync(CurrentContext, timeout.Value);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        /// <summary>
        /// Kills the process immediately.
        /// </summary>
        public void Kill()
        {
            CancelActiveStart();
            _lifecycleGate.Wait();
            try
            {
                var context = CurrentContext;
                MarkStopping(context);
                KillProcess(context?.Process, logSuccess: true);
                CleanupProcessAsync(context).GetAwaiter().GetResult();
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        /// <summary>
        /// Restarts the DotCraft process.
        /// </summary>
        public async Task<bool> RestartAsync(DotCraftSettings settings, CancellationToken ct = default)
        {
            CancelActiveStart();
            using var restartCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            var gateAcquired = false;
            try
            {
                await _lifecycleGate.WaitAsync(restartCts.Token);
                gateAcquired = true;
                await StopCoreAsync(CurrentContext, TimeSpan.FromSeconds(3));
                await Task.Delay(500, restartCts.Token);
                lock (_stateSync)
                    _activeStartCts = restartCts;
                return await StartCoreAsync(settings, restartCts.Token);
            }
            finally
            {
                lock (_stateSync)
                {
                    if (ReferenceEquals(_activeStartCts, restartCts))
                        _activeStartCts = null;
                }
                if (gateAcquired)
                    _lifecycleGate.Release();
            }
        }

        private async Task<bool> StartCoreAsync(DotCraftSettings settings, CancellationToken ct)
        {
            if (_disposed)
                return false;

            var existing = CurrentContext;
            if (IsProcessAlive(existing?.Process))
            {
                Debug.LogWarning("[DotCraft] Process is already running.");
                return true;
            }

            if (existing != null)
                await CleanupProcessAsync(existing);

            _errorOutput.Clear();
            ProcessStartInfo startInfo = null;
            ProcessContext startedContext = null;
            try
            {
                startInfo = _startInfoBuilderOverride == null
                    ? await BuildProcessStartInfoAsync(settings, ct)
                    : await _startInfoBuilderOverride(settings, ct);
                ct.ThrowIfCancellationRequested();

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    Debug.LogError("[DotCraft] Failed to start process.");
                    return false;
                }

                var context = new ProcessContext(process);
                startedContext = context;
                context.ExitedHandler = (_, _) => HandleProcessExited(context);
                lock (_stateSync)
                {
                    if (_disposed)
                    {
                        KillProcess(process, logSuccess: false);
                        process.Dispose();
                        context.ErrorReadCts.Dispose();
                        return false;
                    }
                    _current = context;
                    StartTime = DateTime.Now;
                    ProcessStartCountForTests++;
                }

                process.Exited += context.ExitedHandler;
                process.EnableRaisingEvents = true;
                context.ErrorReadTask = ReadErrorOutputAsync(context, context.ErrorReadCts.Token);
                ErrorReaderStartCountForTests++;

                if (DotCraftSettings.Instance.VerboseLogging)
                    Debug.Log($"[DotCraft] Process started (PID: {process.Id})");

                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (startedContext != null)
                {
                    MarkStopping(startedContext);
                    KillProcess(startedContext.Process, logSuccess: false);
                    await CleanupProcessAsync(startedContext);
                }
                return false;
            }
            catch (Exception ex)
            {
                if (startedContext != null)
                {
                    MarkStopping(startedContext);
                    KillProcess(startedContext.Process, logSuccess: false);
                    await CleanupProcessAsync(startedContext);
                }
                Debug.LogError(FormatStartFailureMessage(ex, startInfo, settings));
                return false;
            }
        }

        private async Task StopCoreAsync(ProcessContext context, TimeSpan timeout)
        {
            if (context == null)
                return;

            MarkStopping(context);
            var process = context.Process;
            try
            {
                if (IsProcessAlive(process))
                {
                    process.StandardInput.Close();
                    if (await WaitForExitAsync(process, timeout))
                    {
                        if (DotCraftSettings.Instance.VerboseLogging)
                            Debug.Log("[DotCraft] Process exited gracefully.");
                    }
                    else
                    {
                        KillProcess(process, logSuccess: false);
                        Debug.LogWarning("[DotCraft] Process was force-killed.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] Error stopping process: {ex.Message}");
                KillProcess(process, logSuccess: false);
            }
            finally
            {
                await CleanupProcessAsync(context);
            }
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

        private async Task ReadErrorOutputAsync(ProcessContext context, CancellationToken ct)
        {
            var process = context.Process;
            try
            {
                while (!ct.IsCancellationRequested && IsProcessAlive(process))
                {
                    var line = await process.StandardError.ReadLineAsync();
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

        private void HandleProcessExited(ProcessContext context)
        {
            lock (_stateSync)
            {
                if (!ReferenceEquals(_current, context) || context.IsStopping || _disposed)
                    return;
            }

            if (DotCraftSettings.Instance.VerboseLogging)
                Debug.Log("[DotCraft] Process exited.");

            OnProcessExited?.Invoke();
        }

        private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
        {
            if (!IsProcessAlive(process))
                return true;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.Exited += OnExited;

            if (!IsProcessAlive(process))
            {
                process.Exited -= OnExited;
                return true;
            }

            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(timeout)
            );

            process.Exited -= OnExited;
            return completedTask == tcs.Task;

            void OnExited(object sender, EventArgs e)
            {
                process.Exited -= OnExited;
                tcs.TrySetResult(true);
            }
        }

        private async Task CleanupProcessAsync(ProcessContext context)
        {
            if (context == null)
                return;

            MarkStopping(context);
            context.ErrorReadCts.Cancel();
            var errorReadTask = context.ErrorReadTask;
            if (errorReadTask != null)
            {
                try
                {
                    await Task.WhenAny(errorReadTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
                }
                catch
                {
                    // ignored
                }
            }

            lock (_stateSync)
            {
                if (ReferenceEquals(_current, context))
                {
                    _current = null;
                    StartTime = null;
                }
            }

            try
            {
                context.Process.Exited -= context.ExitedHandler;
                context.Process.Dispose();
            }
            catch
            {
                // ignored
            }
            context.ErrorReadCts.Dispose();
        }

        private void CancelActiveStart()
        {
            lock (_stateSync)
                _activeStartCts?.Cancel();
        }

        private static void MarkStopping(ProcessContext context)
        {
            if (context != null)
                Interlocked.Exchange(ref context.Stopping, 1);
        }

        private static bool IsProcessAlive(Process process)
        {
            if (process == null)
                return false;

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void KillProcess(Process process, bool logSuccess)
        {
            if (!IsProcessAlive(process))
                return;

            try
            {
                process.Kill();
                if (logSuccess && DotCraftSettings.Instance.VerboseLogging)
                    Debug.Log("[DotCraft] Process killed.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] Error killing process: {ex.Message}");
            }
        }

        public void Dispose()
        {
            lock (_stateSync)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            _disposeCts.Cancel();
            CancelActiveStart();
            Kill();
            _disposeCts.Dispose();
            _lifecycleGate.Dispose();
        }

        private sealed class ProcessContext
        {
            public ProcessContext(Process process)
            {
                Process = process;
            }

            public Process Process { get; }
            public CancellationTokenSource ErrorReadCts { get; } = new();
            public Task ErrorReadTask { get; set; }
            public EventHandler ExitedHandler { get; set; }
            public int Stopping;
            public bool IsStopping => Volatile.Read(ref Stopping) != 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.McpSetup;
using Newtonsoft.Json;

namespace DotCraft.Editor.Execution
{
    internal interface IExternalCSharpCompiler
    {
        Task<ExternalCompilationResult> CompileAsync(string code, string sourceLabel,
            IReadOnlyList<string> references, CancellationToken cancellationToken);
    }

    internal sealed class ExternalCompilationResult
    {
        public bool Success { get; set; }
        public byte[] Assembly { get; set; }
        public string EntryType { get; set; }
        public List<ExecutionDiagnostic> Diagnostics { get; set; } = new();
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal sealed class ExternalCSharpCompiler : IExternalCSharpCompiler
    {
        internal const int ProtocolVersion = 1;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
        private readonly string _executablePath;

        public ExternalCSharpCompiler(string executablePath = null)
        {
            _executablePath = executablePath ?? McpGatewayInstaller.InstalledExecutablePath;
        }

        public async Task<ExternalCompilationResult> CompileAsync(string code, string sourceLabel,
            IReadOnlyList<string> references, CancellationToken cancellationToken)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || !Environment.Is64BitProcess)
                return Failure("UnityCompilerUnsupported", "External C# compilation requires Windows x64.");
            if (!File.Exists(_executablePath))
                return Failure("UnityCompilerUnavailable", $"Install dotcraft-unity at '{_executablePath}'.");

            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = "compiler",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process process;
            try
            {
                process = Process.Start(startInfo);
                if (process == null)
                    return Failure("UnityCompilerUnavailable", "The dotcraft-unity compiler process could not be started.");
            }
            catch (Exception exception)
            {
                return Failure("UnityCompilerUnavailable", exception.Message);
            }

            using (process)
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(Timeout);
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    var request = new CompilerRequest
                    {
                        ProtocolVersion = ProtocolVersion,
                        Code = code,
                        SourceLabel = sourceLabel,
                        References = references
                    };
                    await process.StandardInput.WriteAsync(JsonConvert.SerializeObject(request));
                    process.StandardInput.Close();
                    await WaitForExitAsync(process, deadline.Token);
                    process.WaitForExit();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Kill(process);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    return Failure("UnityCompilerTimeout", "External C# compilation timed out.");
                }
                catch (Exception exception)
                {
                    Kill(process);
                    return Failure("UnityCompilerFailed", exception.Message);
                }

                var output = await stdout;
                var error = await stderr;
                if (process.ExitCode != 0)
                    return Failure("UnityCompilerFailed", string.IsNullOrWhiteSpace(error)
                        ? $"The compiler exited with code {process.ExitCode}."
                        : error.Trim());

                CompilerResponse response;
                try { response = JsonConvert.DeserializeObject<CompilerResponse>(output); }
                catch (Exception exception) { return Failure("UnityCompilerFailed", exception.Message); }
                if (response == null)
                    return Failure("UnityCompilerFailed", "The compiler returned an empty response.");
                if (response.ProtocolVersion != ProtocolVersion)
                    return Failure("UnityCompilerProtocolMismatch", $"Unsupported compiler protocol {response.ProtocolVersion}.");
                if (!response.Success)
                {
                    return new ExternalCompilationResult
                    {
                        Success = false,
                        ErrorCode = response.ErrorCode ?? "UnityCompilerFailed",
                        ErrorMessage = response.ErrorMessage ?? "External C# compilation failed.",
                        Diagnostics = response.Diagnostics ?? new List<ExecutionDiagnostic>()
                    };
                }
                if (string.IsNullOrWhiteSpace(response.AssemblyBase64)
                    || string.IsNullOrWhiteSpace(response.EntryType))
                {
                    return Failure("UnityCompilerFailed", "The compiler returned an incomplete success response.");
                }

                try
                {
                    return new ExternalCompilationResult
                    {
                        Success = true,
                        Assembly = Convert.FromBase64String(response.AssemblyBase64 ?? ""),
                        EntryType = response.EntryType,
                        Diagnostics = response.Diagnostics ?? new List<ExecutionDiagnostic>()
                    };
                }
                catch (FormatException exception)
                {
                    return Failure("UnityCompilerFailed", exception.Message);
                }
            }
        }

        private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => completion.TrySetResult(true);
            if (process.HasExited) completion.TrySetResult(true);
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
                await completion.Task;
        }

        private static void Kill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch { }
        }

        private static ExternalCompilationResult Failure(string code, string message) => new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message
        };

        private sealed class CompilerRequest
        {
            [JsonProperty("protocolVersion")] public int ProtocolVersion { get; set; }
            [JsonProperty("code")] public string Code { get; set; }
            [JsonProperty("sourceLabel")] public string SourceLabel { get; set; }
            [JsonProperty("references")] public IReadOnlyList<string> References { get; set; }
        }

        private sealed class CompilerResponse
        {
            [JsonProperty("protocolVersion")] public int ProtocolVersion { get; set; }
            [JsonProperty("success")] public bool Success { get; set; }
            [JsonProperty("assemblyBase64")] public string AssemblyBase64 { get; set; }
            [JsonProperty("entryType")] public string EntryType { get; set; }
            [JsonProperty("diagnostics")] public List<ExecutionDiagnostic> Diagnostics { get; set; }
            [JsonProperty("errorCode")] public string ErrorCode { get; set; }
            [JsonProperty("errorMessage")] public string ErrorMessage { get; set; }
        }
    }
}

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.Execution
{
    internal static class UnityExecutionEngines
    {
        public const string CSharp = "csharp";
    }

    internal static class UnityExecutionModes
    {
        public const string Editor = "editor";
        public const string PlayMode = "playmode";
    }

    internal sealed class ExecutionRequest
    {
        public ExecutionRequest(
            string engine,
            string mode,
            string code,
            string path = null,
            JObject inputs = null)
        {
            Engine = engine;
            Mode = mode;
            Code = code;
            Path = path;
            Inputs = inputs;
        }

        public string Engine { get; }

        public string Mode { get; }

        public string Code { get; }

        /// <summary>Project-relative or absolute path of a script file to execute instead of <see cref="Code"/>.</summary>
        public string Path { get; }

        public JObject Inputs { get; }
    }

    internal sealed class ExecutionResult
    {
        public bool Success { get; set; }

        public string Mode { get; set; }

        public object ReturnValue { get; set; }

        public List<ExecutionDiagnostic> Diagnostics { get; set; } = new();

        public List<ExecutionLogEntry> Logs { get; set; } = new();

        public long DurationMs { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public static ExecutionResult Ok(
            string mode,
            object returnValue,
            List<ExecutionLogEntry> logs,
            long durationMs,
            List<ExecutionDiagnostic> diagnostics = null)
        {
            return new ExecutionResult
            {
                Success = true,
                Mode = mode,
                ReturnValue = returnValue,
                Diagnostics = diagnostics ?? new List<ExecutionDiagnostic>(),
                Logs = logs ?? new List<ExecutionLogEntry>(),
                DurationMs = durationMs
            };
        }

        public static ExecutionResult Failed(
            string mode,
            string errorCode,
            string errorMessage,
            long durationMs,
            List<ExecutionDiagnostic> diagnostics = null,
            List<ExecutionLogEntry> logs = null)
        {
            return new ExecutionResult
            {
                Success = false,
                Mode = mode,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Diagnostics = diagnostics ?? new List<ExecutionDiagnostic>(),
                Logs = logs ?? new List<ExecutionLogEntry>(),
                DurationMs = durationMs
            };
        }
    }

    internal sealed class ExecutionDiagnostic
    {
        public string Id { get; set; }

        public string Severity { get; set; }

        public string Message { get; set; }

        public int? Line { get; set; }

        public int? Column { get; set; }
    }

    internal sealed class ExecutionLogEntry
    {
        public string Type { get; set; }

        public string Message { get; set; }

        public string StackTrace { get; set; }
    }
}

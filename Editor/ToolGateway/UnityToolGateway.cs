using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Execution;
using DotCraft.Editor.Extensions;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class UnityToolGateway
    {
        private const string ExecuteCSharpToolName = "ExecuteCSharp";

        private static readonly Lazy<UnityToolGateway> LazyInstance =
            new(() => new UnityToolGateway());

        private readonly IReadOnlyList<ToolGatewayToolSpec> _tools;

        private UnityToolGateway()
        {
            _tools = new[]
            {
                new ToolGatewayToolSpec
                {
                    Name = ExecuteCSharpToolName,
                    Description = "Compile and execute C# in the running Unity Editor.",
                    InputSchema = JObject.Parse(
                        @"{
  ""type"": ""object"",
  ""properties"": {
    ""code"": {
      ""type"": ""string"",
      ""description"": ""C# method body to compile and execute. Use return to provide a result.""
    },
    ""mode"": {
      ""type"": ""string"",
      ""enum"": [""editor"", ""playmode""],
      ""description"": ""Execution mode.""
    }
  },
  ""required"": [""code""],
  ""additionalProperties"": false
}")
                }
            };
        }

        public static UnityToolGateway Instance => LazyInstance.Value;

        public IReadOnlyList<ToolGatewayToolSpec> ListTools()
        {
            return _tools;
        }

        public async Task<ToolGatewayResult> CallAsync(string name, JToken arguments, CancellationToken ct)
        {
            var normalizedName = NormalizeToolName(name);
            var stopwatch = Stopwatch.StartNew();

            if (!string.Equals(normalizedName, ExecuteCSharpToolName, StringComparison.Ordinal))
            {
                return ToolGatewayResult.Failed(
                    normalizedName ?? string.Empty,
                    "ToolNotFound",
                    $"Unity Tool Gateway does not expose tool '{name}'.",
                    stopwatch.ElapsedMilliseconds);
            }

            var args = arguments as JObject ?? new JObject();
            var code = args.Value<string>("code");
            var mode = args.Value<string>("mode") ?? UnityExecutionModes.Editor;
            if (string.IsNullOrWhiteSpace(code))
            {
                return ToolGatewayResult.Failed(
                    ExecuteCSharpToolName,
                    "InvalidArguments",
                    "ExecuteCSharp requires a non-empty 'code' argument.",
                    stopwatch.ElapsedMilliseconds);
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await RunExecuteCSharpAsync(code, mode).ConfigureAwait(false);
                var durationMs = stopwatch.ElapsedMilliseconds;
                if (!result.Success)
                {
                    return ToolGatewayResult.Failed(
                        ExecuteCSharpToolName,
                        result.ErrorCode,
                        result.ErrorMessage,
                        durationMs,
                        result);
                }

                return ToolGatewayResult.Ok(
                    ExecuteCSharpToolName,
                    result,
                    FormatSuccessText(result),
                    durationMs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ToolGatewayResult.Failed(
                    ExecuteCSharpToolName,
                    "GatewayException",
                    $"{ex.GetType().Name}: {ex.Message}",
                    stopwatch.ElapsedMilliseconds);
            }
        }

        private static Task<ExecutionResult> RunExecuteCSharpAsync(string code, string mode)
        {
            var request = new ExecutionRequest(UnityExecutionEngines.CSharp, mode, code);
            if (MainThreadDispatcher.IsMainThread)
                return ExecutionRouter.Instance.ExecuteAsync(request);

            return MainThreadDispatcher.RunOnMainThread(
                () => ExecutionRouter.Instance.ExecuteAsync(request),
                timeoutMs: 60000);
        }

        private static string NormalizeToolName(string name)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return trimmed;

            const string unityPrefix = "unity.";
            return trimmed.StartsWith(unityPrefix, StringComparison.Ordinal)
                ? trimmed.Substring(unityPrefix.Length)
                : trimmed;
        }

        private static string FormatSuccessText(ExecutionResult result)
        {
            if (result?.ReturnValue == null)
                return "ExecuteCSharp completed.";

            if (result.ReturnValue is string text)
                return text;

            return $"ExecuteCSharp returned {result.ReturnValue}.";
        }
    }
}

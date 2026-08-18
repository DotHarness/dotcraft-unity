using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Execution;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.RuntimeTools
{
    internal sealed class RuntimeToolOutcome
    {
        public bool Success { get; private set; }

        public string Name { get; private set; }

        public object StructuredResult { get; private set; }

        public string Text { get; private set; }

        public string ErrorCode { get; private set; }

        public string ErrorMessage { get; private set; }

        public long DurationMs { get; private set; }

        public static async Task<RuntimeToolOutcome> InvokeAsync(
            RuntimeToolDefinition tool,
            JToken arguments,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var rawResult = await RuntimeToolInvoker.InvokeAsync(
                        tool,
                        arguments ?? new JObject(),
                        cancellationToken)
                    .ConfigureAwait(false);
                return FromResult(tool.Descriptor.Name, rawResult, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsArgumentException(ex))
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failed(
                    tool.Descriptor.Name,
                    "ToolExecutionException",
                    $"{ex.GetType().Name}: {ex.Message}",
                    stopwatch.ElapsedMilliseconds);
            }
        }

        public static RuntimeToolOutcome FromResult(
            string name,
            object result,
            long durationMs)
        {
            if (result is ExecutionResult executionResult && !executionResult.Success)
            {
                return Failed(
                    name,
                    executionResult.ErrorCode,
                    executionResult.ErrorMessage,
                    durationMs,
                    executionResult);
            }

            return new RuntimeToolOutcome
            {
                Success = true,
                Name = name,
                StructuredResult = result,
                Text = FormatSuccessText(name, result),
                DurationMs = durationMs
            };
        }

        public static RuntimeToolOutcome Failed(
            string name,
            string errorCode,
            string errorMessage,
            long durationMs,
            object structuredResult = null)
        {
            return new RuntimeToolOutcome
            {
                Success = false,
                Name = name,
                StructuredResult = structuredResult,
                Text = string.IsNullOrWhiteSpace(errorMessage)
                    ? $"{name} failed."
                    : $"{name} failed: {errorMessage}",
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                DurationMs = durationMs
            };
        }

        public static bool IsArgumentException(Exception ex)
        {
            return ex is JsonException
                   || ex is ArgumentException
                   || ex is FormatException
                   || ex is InvalidCastException;
        }

        private static string FormatSuccessText(string name, object result)
        {
            if (result is not ExecutionResult executionResult || executionResult.ReturnValue == null)
                return $"{name} completed.";

            if (executionResult.ReturnValue is string text)
                return text;

            return $"{name} returned {executionResult.ReturnValue}.";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Execution;
using DotCraft.Editor.Extensions;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using DotCraft.Editor.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class UnityToolGateway
    {
        private const string ExecuteCSharpToolName = "unity_execute_csharp";

        private static readonly Lazy<UnityToolGateway> LazyInstance =
            new(() => new UnityToolGateway());

        private UnityToolGateway()
        {
        }

        public static UnityToolGateway Instance => LazyInstance.Value;

        public IReadOnlyList<ToolGatewayToolSpec> ListTools()
        {
            return BuildRegistry()
                .Select(registration => registration.Spec)
                .ToList();
        }

        public async Task<ToolGatewayResult> CallAsync(string name, JToken arguments, CancellationToken ct)
        {
            var normalizedName = NormalizeToolName(name);
            var stopwatch = Stopwatch.StartNew();
            var registry = BuildRegistry()
                .ToDictionary(registration => registration.Spec.Name, StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(normalizedName)
                || !registry.TryGetValue(normalizedName, out var registration))
            {
                return ToolGatewayResult.Failed(
                    normalizedName ?? string.Empty,
                    "ToolNotFound",
                    $"Unity Tool Gateway does not expose enabled tool '{name}'.",
                    stopwatch.ElapsedMilliseconds);
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                return await registration.InvokeAsync(arguments ?? new JObject(), ct, stopwatch)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ToolGatewayResult.Failed(
                    registration.Spec.Name,
                    "GatewayException",
                    $"{ex.GetType().Name}: {ex.Message}",
                    stopwatch.ElapsedMilliseconds);
            }
        }

        private static IReadOnlyList<GatewayToolRegistration> BuildRegistry()
        {
            var settings = DotCraftSettings.Instance;
            var registrations = new List<GatewayToolRegistration>();
            var reservedNames = new HashSet<string>(StringComparer.Ordinal);

            if (settings.EnableBuiltinUnityTools)
                AddExecuteCSharpRegistration(registrations, reservedNames);
            else
                reservedNames.Add(ExecuteCSharpToolName);

            var dynamicToolEnabledById = settings.DynamicToolEnabledById
                                         ?? new Dictionary<string, bool>();
            var enabledPluginToolIds = new HashSet<string>(
                dynamicToolEnabledById
                    .Where(pair => pair.Value)
                    .Select(pair => pair.Key),
                StringComparer.Ordinal);

            var resolution = RuntimeToolCatalog.ResolveEnabledTools(
                RuntimeToolCatalog.Discover(),
                settings.EnableBuiltinUnityTools,
                id => enabledPluginToolIds.Contains(id),
                reservedNames.ToArray());

            foreach (var tool in resolution.Tools)
                AddRuntimeToolRegistration(registrations, reservedNames, tool);

            return registrations;
        }

        private static void AddExecuteCSharpRegistration(
            ICollection<GatewayToolRegistration> registrations,
            ISet<string> reservedNames)
        {
            if (!reservedNames.Add(ExecuteCSharpToolName))
                return;

            registrations.Add(new GatewayToolRegistration(
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
                },
                InvokeExecuteCSharpAsync));
        }

        private static void AddRuntimeToolRegistration(
            ICollection<GatewayToolRegistration> registrations,
            ISet<string> reservedNames,
            RuntimeToolDefinition tool)
        {
            if (!reservedNames.Add(tool.Descriptor.Name))
                return;

            registrations.Add(new GatewayToolRegistration(
                new ToolGatewayToolSpec
                {
                    Name = tool.Descriptor.Name,
                    Description = tool.Descriptor.Description,
                    InputSchema = tool.Descriptor.InputSchema == null
                        ? new JObject { ["type"] = "object" }
                        : JObject.FromObject(tool.Descriptor.InputSchema, DotCraftJson.CompactSerializer)
                },
                (args, ct, stopwatch) => InvokeRuntimeToolAsync(tool, args, ct, stopwatch)));
        }

        private static async Task<ToolGatewayResult> InvokeExecuteCSharpAsync(
            JToken arguments,
            CancellationToken ct,
            Stopwatch stopwatch)
        {
            var args = arguments as JObject ?? new JObject();
            var code = args.Value<string>("code");
            var mode = args.Value<string>("mode") ?? UnityExecutionModes.Editor;
            if (string.IsNullOrWhiteSpace(code))
            {
                return ToolGatewayResult.Failed(
                    ExecuteCSharpToolName,
                    "InvalidArguments",
                    "unity_execute_csharp requires a non-empty 'code' argument.",
                    stopwatch.ElapsedMilliseconds);
            }

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
                FormatExecuteCSharpSuccessText(result),
                durationMs);
        }

        private static async Task<ToolGatewayResult> InvokeRuntimeToolAsync(
            RuntimeToolDefinition tool,
            JToken arguments,
            CancellationToken ct,
            Stopwatch stopwatch)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await InvokeRuntimeToolOnMainThreadAsync(tool, arguments ?? new JObject())
                    .ConfigureAwait(false);

                if (result is ExecutionResult executionResult && !executionResult.Success)
                {
                    return ToolGatewayResult.Failed(
                        tool.Descriptor.Name,
                        executionResult.ErrorCode,
                        executionResult.ErrorMessage,
                        stopwatch.ElapsedMilliseconds,
                        executionResult);
                }

                return ToolGatewayResult.Ok(
                    tool.Descriptor.Name,
                    result,
                    $"{tool.Descriptor.Name} completed.",
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsArgumentException(ex))
            {
                return ToolGatewayResult.Failed(
                    tool.Descriptor.Name,
                    "InvalidArguments",
                    $"{ex.GetType().Name}: {ex.Message}",
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                return ToolGatewayResult.Failed(
                    tool.Descriptor.Name,
                    "ToolExecutionException",
                    $"{ex.GetType().Name}: {ex.Message}",
                    stopwatch.ElapsedMilliseconds);
            }
        }

        private static Task<object> InvokeRuntimeToolOnMainThreadAsync(
            RuntimeToolDefinition tool,
            JToken arguments)
        {
            if (MainThreadDispatcher.IsMainThread)
                return RuntimeToolInvoker.InvokeAsync(tool, arguments);

            return MainThreadDispatcher.RunOnMainThread(
                () => RuntimeToolInvoker.InvokeAsync(tool, arguments),
                timeoutMs: 60000);
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

        private static bool IsArgumentException(Exception ex)
        {
            return ex is JsonException
                   || ex is ArgumentException
                   || ex is FormatException
                   || ex is InvalidCastException;
        }

        private static string FormatExecuteCSharpSuccessText(ExecutionResult result)
        {
            if (result?.ReturnValue == null)
                return "unity_execute_csharp completed.";

            if (result.ReturnValue is string text)
                return text;

            return $"unity_execute_csharp returned {result.ReturnValue}.";
        }

        private sealed class GatewayToolRegistration
        {
            private readonly Func<JToken, CancellationToken, Stopwatch, Task<ToolGatewayResult>> _invoke;

            public GatewayToolRegistration(
                ToolGatewayToolSpec spec,
                Func<JToken, CancellationToken, Stopwatch, Task<ToolGatewayResult>> invoke)
            {
                Spec = spec;
                _invoke = invoke;
            }

            public ToolGatewayToolSpec Spec { get; }

            public Task<ToolGatewayResult> InvokeAsync(
                JToken arguments,
                CancellationToken ct,
                Stopwatch stopwatch)
            {
                return _invoke(arguments, ct, stopwatch);
            }
        }
    }
}

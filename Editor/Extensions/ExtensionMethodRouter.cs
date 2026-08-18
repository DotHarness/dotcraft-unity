using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using DotCraft.Editor.RuntimeTools;
using DotCraft.Editor.Connection;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.Extensions
{
    /// <summary>
    /// Routes ACP extension method requests (_unity/*) to registered handlers.
    /// Handlers are executed on the main thread via MainThreadDispatcher.
    /// </summary>
    public sealed class ExtensionMethodRouter
    {
        private readonly ConcurrentDictionary<string, Func<JToken, Task<object>>> _handlers = new();
        private readonly HashSet<string> _dynamicRuntimeMethods = new();

        /// <summary>
        /// Registers a handler for an extension method.
        /// </summary>
        public void RegisterHandler(string method, Func<JToken, Task<object>> handler)
        {
            _handlers[method] = handler;
        }

        /// <summary>
        /// Registers a synchronous handler for an extension method.
        /// </summary>
        public void RegisterHandler(string method, Func<JToken, object> handler)
        {
            _handlers[method] = paramsJson => Task.FromResult(handler(paramsJson));
        }

        /// <summary>
        /// Handles an extension method request.
        /// </summary>
        public async Task<object> HandleAsync(string method, JToken paramsJson)
        {
            if (!_handlers.TryGetValue(method, out var handler))
                throw new AcpRequestException(-32601, $"Method not found: {method}");

            // Execute on main thread. Protocol exceptions are handled by the transport.
            return await MainThreadDispatcher.RunOnMainThread(() => handler(paramsJson));
        }

        /// <summary>
        /// Checks if a handler is registered for the given method.
        /// </summary>
        public bool HasHandler(string method) => _handlers.ContainsKey(method);

        /// <summary>
        /// Registers the currently enabled attribute-discovered runtime tools.
        /// Previous dynamic runtime handlers are removed first so reconnect uses the latest settings.
        /// </summary>
        internal void RegisterRuntimeTools(IEnumerable<RuntimeToolDefinition> runtimeTools)
        {
            foreach (var method in _dynamicRuntimeMethods)
                _handlers.TryRemove(method, out _);
            _dynamicRuntimeMethods.Clear();

            if (runtimeTools == null)
                return;

            foreach (var runtimeTool in runtimeTools)
            {
                var acpMethod = runtimeTool.Descriptor.AcpMethod;
                RegisterHandler(acpMethod, paramsJson => InvokeRuntimeToolAsync(runtimeTool, paramsJson));
                _dynamicRuntimeMethods.Add(acpMethod);
            }
        }

        private static async Task<object> InvokeRuntimeToolAsync(
            RuntimeToolDefinition runtimeTool,
            JToken paramsJson)
        {
            RuntimeToolOutcome outcome;
            try
            {
                outcome = await RuntimeToolOutcome.InvokeAsync(
                        runtimeTool,
                        paramsJson,
                        System.Threading.CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (RuntimeToolOutcome.IsArgumentException(ex))
            {
                throw new AcpRequestException(
                    -32602,
                    $"Invalid parameters for {runtimeTool.Descriptor.AcpMethod}: {ex.Message}");
            }

            return new AcpRuntimeToolCallResult
            {
                Success = outcome.Success,
                ContentItems = new List<AcpRuntimeToolContentItem>
                {
                    new AcpRuntimeToolContentItem
                    {
                        Text = outcome.Text
                    }
                },
                StructuredContent = outcome.StructuredResult,
                ErrorCode = outcome.ErrorCode,
                ErrorMessage = outcome.ErrorMessage
            };
        }
    }
}

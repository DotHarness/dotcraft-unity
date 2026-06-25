using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using DotCraft.Editor.RuntimeTools;
using Newtonsoft.Json.Linq;
using UnityEngine;

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
            {
                return new { error = $"Method not found: {method}" };
            }

            try
            {
                // Execute on main thread
                return await MainThreadDispatcher.RunOnMainThread(() => handler(paramsJson));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Extension method error ({method}): {ex.Message}");
                return new { error = ex.Message };
            }
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
                RegisterHandler(acpMethod, paramsJson => RuntimeToolInvoker.InvokeAsync(runtimeTool, paramsJson));
                _dynamicRuntimeMethods.Add(acpMethod);
            }
        }
    }
}

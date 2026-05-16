using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json.Linq;
using DotCraft.Editor.RuntimeTools;
using UnityEditor;
using UnityEngine;
using UComponent = UnityEngine.Component;

namespace DotCraft.Editor.Extensions
{
    /// <summary>
    /// Routes ACP extension method requests (_unity/*) to registered handlers.
    /// Handlers are executed on the main thread via MainThreadDispatcher.
    /// Only read-only handlers are provided. For full Unity manipulation capabilities,
    /// install SkillsForUnity package.
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

    #region Scene Handlers

    /// <summary>
    /// Handles read-only scene query operations.
    /// </summary>
    public static class UnitySceneHandlers
    {
        /// <summary>
        /// Queries the Unity scene hierarchy and returns GameObject information.
        /// </summary>
        [Description("Query Unity scene hierarchy with optional component details.")]
        [AgentTool(
            Namespace = "unity",
            Name = "unity_scene_query",
            Kind = AcpToolKind.Unity)]
        [DotCraftBuiltinRuntimeTool(AcpMethod = "_unity/scene_query")]
        public static Task<object> HandleSceneQuery(
            [Description("Optional case-insensitive text to match against GameObject names or paths.")]
            string query = null,
            [Description("Include component type names for each returned GameObject.")]
            bool includeComponents = false,
            [Description("Maximum hierarchy depth to include.")]
            [AgentToolSchemaHint(Minimum = 1)]
            int maxDepth = 10)
        {
            var results = new List<object>();

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    var info = GetGameObjectInfo(root, "", includeComponents, maxDepth);
                    if (string.IsNullOrEmpty(query) ||
                        info.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        info.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(info);
                    }
                    else
                    {
                        // Search children
                        SearchChildren(root.transform, query, includeComponents, maxDepth, results);
                    }
                }
            }

            return Task.FromResult<object>(new { objects = results });
        }

        private static void SearchChildren(
            Transform parent,
            string query,
            bool includeComponents,
            int maxDepth,
            List<object> results,
            int depth = 0)
        {
            if (depth >= maxDepth) return;

            foreach (Transform child in parent)
            {
                var info = GetGameObjectInfo(child.gameObject, "", includeComponents, maxDepth - depth);
                if (info.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    info.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(info);
                }
                SearchChildren(child, query, includeComponents, maxDepth, results, depth + 1);
            }
        }

        private static GameObjectInfo GetGameObjectInfo(
            GameObject go,
            string parentPath,
            bool includeComponents,
            int maxDepth,
            int depth = 0)
        {
            var path = string.IsNullOrEmpty(parentPath) ? $"/{go.name}" : $"{parentPath}/{go.name}";

            var info = new GameObjectInfo
            {
                Name = go.name,
                Path = path,
                InstanceId = go.GetInstanceID(),
                Active = go.activeSelf
            };

            if (includeComponents)
            {
                var components = new List<string>();
                foreach (var comp in go.GetComponents<UComponent>())
                {
                    if (comp != null)
                    {
                        components.Add(comp.GetType().Name);
                    }
                }
                info.Components = components;
            }

            if (depth < maxDepth)
            {
                var children = new List<GameObjectInfo>();
                foreach (Transform child in go.transform)
                {
                    children.Add(GetGameObjectInfo(child.gameObject, path, includeComponents, maxDepth, depth + 1));
                }
                info.Children = children;
            }

            return info;
        }

        /// <summary>
        /// Gets the currently selected objects in the Unity Editor.
        /// </summary>
        [Description("Read the current Unity Editor selection.")]
        [AgentTool(
            Namespace = "unity",
            Name = "unity_get_selection",
            Kind = AcpToolKind.Unity)]
        [DotCraftBuiltinRuntimeTool(AcpMethod = "_unity/get_selection")]
        public static Task<object> HandleGetSelection()
        {
            var selected = Selection.gameObjects;
            var results = new List<GameObjectInfo>();

            foreach (var go in selected)
            {
                results.Add(GetGameObjectInfo(go, "", true, 0));
            }

            return Task.FromResult<object>(new { selectedObjects = results });
        }

        private class GameObjectInfo
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public int InstanceId { get; set; }
            public bool Active { get; set; }
            public List<string> Components { get; set; } = new();
            public List<GameObjectInfo> Children { get; set; } = new();
        }
    }

    #endregion

    #region Editor Handlers

    /// <summary>
    /// Collects Unity console log entries via Application.logMessageReceived.
    /// Thread-safe and capacity-limited.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityConsoleLogCollector
    {
        private static readonly object _lock = new();
        private static readonly List<ConsoleLogEntry> _logs = new();
        private const int MaxLogEntries = 2000;

        static UnityConsoleLogCollector()
        {
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private static void OnLogMessageReceived(string message, string stackTrace, LogType type)
        {
            var entry = new ConsoleLogEntry
            {
                Type = type switch
                {
                    LogType.Error => "error",
                    LogType.Assert => "error",
                    LogType.Exception => "error",
                    LogType.Warning => "warning",
                    _ => "log"
                },
                Message = message,
                StackTrace = stackTrace,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            lock (_lock)
            {
                _logs.Add(entry);
                // Trim oldest entries if over capacity
                while (_logs.Count > MaxLogEntries)
                {
                    _logs.RemoveAt(0);
                }
            }
        }

        public static List<ConsoleLogEntry> GetLogs(string[] types, int limit)
        {
            lock (_lock)
            {
                IEnumerable<ConsoleLogEntry> filtered = _logs;

                if (types != null && types.Length > 0)
                {
                    var typeSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
                    filtered = filtered.Where(e => typeSet.Contains(e.Type));
                }

                return filtered.TakeLast(limit).ToList();
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
        }

        public class ConsoleLogEntry
        {
            public string Type { get; set; } = "";
            public string Message { get; set; } = "";
            public string StackTrace { get; set; }
            public string Timestamp { get; set; } = "";
        }
    }

    /// <summary>
    /// Handles read-only Unity Editor operations.
    /// </summary>
    public static class UnityEditorHandlers
    {
        /// <summary>
        /// Gets recent Unity console log entries.
        /// </summary>
        [Description("Retrieve recent Unity Console log entries.")]
        [AgentTool(
            Namespace = "unity",
            Name = "unity_get_console_logs",
            Kind = AcpToolKind.Unity)]
        [DotCraftBuiltinRuntimeTool(AcpMethod = "_unity/get_console_logs")]
        public static Task<object> HandleGetConsoleLogs(
            [Description("Optional log types to include.")]
            [AgentToolSchemaHint(EnumValues = new string[] { "error", "warning", "log" })]
            string[] types = null,
            [Description("Maximum number of recent entries to return.")]
            [AgentToolSchemaHint(Minimum = 1)]
            int limit = 50)
        {
            var logs = UnityConsoleLogCollector.GetLogs(types, limit);

            return Task.FromResult<object>(new { logs });
        }
    }

    #endregion

    #region Project Handlers

    /// <summary>
    /// Handles read-only Unity project information queries.
    /// </summary>
    public static class UnityProjectHandlers
    {
        /// <summary>
        /// Gets Unity project information including version and installed packages.
        /// </summary>
        [Description("Read Unity version, project name, project path, and package information.")]
        [AgentTool(
            Namespace = "unity",
            Name = "unity_get_project_info",
            Kind = AcpToolKind.Unity)]
        [DotCraftBuiltinRuntimeTool(AcpMethod = "_unity/get_project_info")]
        public static Task<object> HandleGetProjectInfo()
        {
            var info = new
            {
                projectName = PlayerSettings.productName,
                unityVersion = Application.unityVersion,
                projectPath = Application.dataPath,
                packages = GetInstalledPackages()
            };

            return Task.FromResult<object>(info);
        }

        private static List<string> GetInstalledPackages()
        {
            var packages = new List<string>();

            try
            {
                var manifestPath = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    "Packages",
                    "manifest.json"
                );

                if (File.Exists(manifestPath))
                {
                    var json = File.ReadAllText(manifestPath);
                    var doc = JObject.Parse(json);

                    if (doc["dependencies"] is JObject deps)
                    {
                        foreach (var prop in deps.Properties())
                        {
                            packages.Add(prop.Name);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return packages;
        }
    }

    #endregion
}

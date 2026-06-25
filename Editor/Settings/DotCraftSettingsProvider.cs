using System;
using System.Collections.Generic;
using System.Linq;
using DotCraft.Editor.AppBinding;
using DotCraft.Editor.McpSetup;
using DotCraft.Editor.RuntimeTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.Settings
{
    /// <summary>
    /// Settings provider for DotCraft configuration in Project Settings window.
    /// Uses UIElements for a modern settings UI.
    /// </summary>
    public sealed class DotCraftSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/DotCraft";
        private DotCraftSettings _settings;
        private VisualElement _rootElement;
        private RuntimeToolCatalogSnapshot _runtimeToolCatalog;

        private SerializedObject _serializedObject;
        private SerializedProperty _dotCraftCommand;
        private SerializedProperty _dotCraftArguments;
        private SerializedProperty _workspacePath;
        private SerializedProperty _autoReconnect;
        private SerializedProperty _verboseLogging;
        private SerializedProperty _requestTimeoutSeconds;
        private SerializedProperty _maxHistoryMessages;

        // Per-server foldout open/closed state (index matches McpServers list)
        private readonly List<bool> _mcpServerFoldouts = new();

        private static readonly string[] TransportOptions = { "stdio", "http" };
        private static readonly string[] AgentConnectionOptions =
        {
            DotCraftSettings.AgentConnectionDotCraft,
            DotCraftSettings.AgentConnectionCustomAcp
        };
        private static readonly string[] AgentConnectionLabels = { "DotCraft", "Custom ACP" };
        private static readonly string[] DotCraftAppServerOptions =
        {
            DotCraftSettings.DotCraftAppServerLocalHub,
            DotCraftSettings.DotCraftAppServerRemote
        };
        private static readonly string[] DotCraftAppServerLabels = { "Local via Hub", "Remote AppServer" };

        public DotCraftSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
            : base(path, scope)
        {
            label = "DotCraft";
            keywords = new HashSet<string>(new[] { "DotCraft", "AI", "Agent", "ACP" });
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new DotCraftSettingsProvider(SettingsPath);
            return provider;
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _settings = DotCraftSettings.Instance;
            _rootElement = rootElement;
            RefreshRuntimeToolCatalog();
            base.OnActivate(searchContext, rootElement);
        }

        public override void OnDeactivate()
        {
            _settings?.Save();
            base.OnDeactivate();
        }

        public override void OnGUI(string searchContext)
        {
            // Draw the Project Settings panel with IMGUI.
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space(10);

            DrawOverviewSection();

            EditorGUILayout.Space(10);

            DrawInEditorAgentChatSection();

            EditorGUILayout.Space(10);

            DrawMcpToolGatewaySection();

            EditorGUILayout.Space(10);

            DrawUnityToolsSection();

            EditorGUILayout.Space(10);

            DrawAdvancedDotCraftSection();

            EditorGUILayout.Space(10);

            // General settings validation
            var errors = _settings.Validate();
            if (errors.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Warning);
            }

            // Workspace / .craft directory validation
            if (!_settings.ValidateWorkspace(out var workspaceError))
            {
                EditorGUILayout.HelpBox(workspaceError, MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Workspace OK: \"{_settings.EffectiveWorkspacePath}\" contains a .craft directory.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset to Defaults", GUILayout.Width(120)))
                {
                    _settings.ResetToDefaults();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Save", GUILayout.Width(80)))
                {
                    _settings.Save();
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                _settings.Save();
            }
        }

        private void DrawOverviewSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("DotCraft Unity", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Use coding agents with Unity Editor: chat inside Unity, or expose Unity tools to external MCP clients.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(6);

                var service = UnityAppBindingService.Instance;
                var counts = GetEnabledToolCounts();
                DrawReadonlyRow("In-Editor Agent Chat", _settings.AgentConnection == DotCraftSettings.AgentConnectionDotCraft
                    ? "DotCraft profile"
                    : "Custom ACP Agent");
                DrawReadonlyRow("Local Tool Gateway", service.IsLocalServerRunning ? "Running" : "Stopped");
                DrawReadonlyRow("MCP Endpoint", McpGatewaySetupDefaults.Endpoint);
                DrawReadonlyRow("Enabled Unity Tools", $"{counts.builtIn} built-in, {counts.custom} custom");

                EditorGUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Assistant", GUILayout.Width(130)))
                        global::DotCraft.Editor.Window.DotCraftEditorWindow.ShowWindow();

                    if (GUILayout.Button("Setup MCP Clients", GUILayout.Width(150)))
                        McpGatewaySetupWindow.ShowWindow();

                    if (GUILayout.Button("Restart Gateway", GUILayout.Width(130)))
                    {
                        _settings.EnableAppBindingLocalServer = true;
                        _settings.Save();
                        UnityAppBindingService.Instance.RestartLocalServer();
                    }

                    if (GUILayout.Button("Copy Endpoint", GUILayout.Width(120)))
                        EditorGUIUtility.systemCopyBuffer = McpGatewaySetupDefaults.Endpoint;
                }
            }
        }

        private void DrawInEditorAgentChatSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("In-Editor Agent Chat", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Use Unity as an ACP client and chat with a coding agent inside the Editor.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(5);

                DrawConnectionSettings();

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);
                    if (GUILayout.Button("Open DotCraft Assistant", GUILayout.Width(180)))
                        global::DotCraft.Editor.Window.DotCraftEditorWindow.ShowWindow();
                }
            }
        }

        private void DrawConnectionSettings()
        {
            EditorGUI.indentLevel++;

            var connectionIndex = IndexOfOrDefault(
                AgentConnectionOptions,
                _settings.AgentConnection,
                DotCraftSettings.AgentConnectionDotCraft);
            connectionIndex = EditorGUILayout.Popup(
                new GUIContent("Agent", "Use DotCraft's Hub-aware ACP bridge, or configure a raw custom ACP agent."),
                connectionIndex,
                AgentConnectionLabels);
            _settings.AgentConnection = AgentConnectionOptions[connectionIndex];

            if (_settings.AgentConnection == DotCraftSettings.AgentConnectionDotCraft)
            {
                _settings.DotCraftCommand = EditorGUILayout.TextField(
                    new GUIContent("DotCraft Command", "Command used to start DotCraft Hub and the ACP bridge."),
                    _settings.DotCraftCommand);

                _settings.WorkspacePath = EditorGUILayout.TextField(
                    new GUIContent("Workspace Path", "Working directory for DotCraft (empty = Unity project root)"),
                    _settings.WorkspacePath);

                var appServerIndex = IndexOfOrDefault(
                    DotCraftAppServerOptions,
                    _settings.DotCraftAppServer,
                    DotCraftSettings.DotCraftAppServerLocalHub);
                appServerIndex = EditorGUILayout.Popup(
                    new GUIContent("DotCraft AppServer", "Local mode uses Hub to discover or start the workspace AppServer."),
                    appServerIndex,
                    DotCraftAppServerLabels);
                _settings.DotCraftAppServer = DotCraftAppServerOptions[appServerIndex];

                if (_settings.DotCraftAppServer == DotCraftSettings.DotCraftAppServerRemote)
                {
                    _settings.RemoteAppServerUrl = EditorGUILayout.TextField(
                        new GUIContent("Remote URL", "Remote AppServer WebSocket URL passed to dotcraft -acp --remote."),
                        _settings.RemoteAppServerUrl);
                    _settings.RemoteAppServerToken = EditorGUILayout.PasswordField(
                        new GUIContent("Remote Token", "Optional bearer token passed to dotcraft -acp --token."),
                        _settings.RemoteAppServerToken);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Unity will start or reuse DotCraft Hub, ensure the workspace AppServer, then launch the ACP bridge with --remote.",
                        MessageType.Info);
                }
            }
            else
            {
                _settings.DotCraftCommand = EditorGUILayout.TextField(
                    new GUIContent("Command", "Command to execute the ACP agent."),
                    _settings.DotCraftCommand);

                _settings.DotCraftArguments = EditorGUILayout.TextField(
                    new GUIContent("Arguments", "Arguments passed to the ACP command."),
                    _settings.DotCraftArguments);

                _settings.WorkspacePath = EditorGUILayout.TextField(
                    new GUIContent("Workspace Path", "Working directory for the ACP process (empty = Unity project root)"),
                    _settings.WorkspacePath);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawMcpToolGatewaySection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("MCP Tool Gateway", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Expose enabled Unity tools to Claude Code, Codex, Cursor, and other MCP-compatible coding agents. This path is independent from the in-editor chat connection.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUI.indentLevel++;

                var enabled = EditorGUILayout.Toggle(
                    new GUIContent("Enable Local Tool Gateway", "Listen on localhost for MCP clients and DotCraft App Binding handoffs."),
                    _settings.EnableAppBindingLocalServer);
                if (enabled != _settings.EnableAppBindingLocalServer)
                {
                    _settings.EnableAppBindingLocalServer = enabled;
                    UnityAppBindingBootstrap.ApplySettings();
                }

                var service = UnityAppBindingService.Instance;
                var counts = GetEnabledToolCounts();
                DrawReadonlyRow("Endpoint", McpGatewaySetupDefaults.Endpoint);
                DrawReadonlyRow("Status", service.IsLocalServerRunning ? "Running" : "Stopped");
                DrawReadonlyRow("Tools", $"{counts.builtIn} built-in, {counts.custom} custom");
                if (!string.IsNullOrWhiteSpace(service.LastError))
                    EditorGUILayout.HelpBox(service.LastError, MessageType.Error);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);
                    if (GUILayout.Button("Setup MCP Clients", GUILayout.Width(150)))
                        McpGatewaySetupWindow.ShowWindow();
                    if (GUILayout.Button("Restart Gateway", GUILayout.Width(130)))
                    {
                        _settings.EnableAppBindingLocalServer = true;
                        _settings.Save();
                        service.RestartLocalServer();
                    }
                    if (GUILayout.Button("Copy Endpoint", GUILayout.Width(120)))
                        EditorGUIUtility.systemCopyBuffer = McpGatewaySetupDefaults.Endpoint;
                }

                EditorGUI.indentLevel--;
            }
        }

        private static int IndexOfOrDefault(string[] options, string value, string defaultValue)
        {
            var index = Array.IndexOf(options, value);
            if (index >= 0)
                return index;
            index = Array.IndexOf(options, defaultValue);
            return index >= 0 ? index : 0;
        }

        private void DrawUnityToolsSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Unity Tools", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Choose which Unity tools are available to DotCraft and to MCP clients connected through the Gateway.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUI.indentLevel++;

                var enableBuiltinTools = EditorGUILayout.Toggle(
                    new GUIContent("Enable Built-in Unity Tools",
                        "Expose built-in Unity read tools and unity_execute_csharp through the DotCraft profile and MCP Tool Gateway."),
                    _settings.EnableBuiltinUnityTools);
                if (enableBuiltinTools != _settings.EnableBuiltinUnityTools)
                {
                    _settings.EnableBuiltinUnityTools = enableBuiltinTools;
                    UnityAppBindingService.Instance.RefreshHandoffSnapshot();
                }

                EditorGUILayout.LabelField(
                    "Built-in: scene query, selection, console logs, project info, and C# automation via unity_execute_csharp.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Custom Project Tools", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Static Editor methods marked with AgentToolAttribute are discovered here. Enabled custom tools are available to DotCraft and to MCP clients through the Gateway.",
                    MessageType.Info);

                _runtimeToolCatalog ??= RuntimeToolCatalog.Discover();

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);
                    if (GUILayout.Button("Refresh Custom Tools", GUILayout.Width(160)))
                    {
                        RefreshRuntimeToolCatalog();
                        UnityAppBindingService.Instance.RefreshHandoffSnapshot();
                    }

                    if (GUILayout.Button("Setup MCP Clients", GUILayout.Width(150)))
                        McpGatewaySetupWindow.ShowWindow();
                }

                var pluginTools = _runtimeToolCatalog.Tools
                    .Where(tool => tool.Source == RuntimeToolSource.Plugin)
                    .ToList();

                if (pluginTools.Count == 0)
                {
                    EditorGUILayout.LabelField("No custom project tools discovered.", EditorStyles.miniLabel);
                }
                else
                {
                    foreach (var tool in pluginTools)
                    {
                        EditorGUILayout.Space(4);
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            var enabled = _settings.DynamicToolEnabledById.TryGetValue(tool.Id, out var stored)
                                          && stored;
                            var nextEnabled = EditorGUILayout.ToggleLeft(
                                new GUIContent(
                                    tool.DisplayName,
                                    $"Tool id: {tool.Id}\nACP method: {tool.Descriptor.AcpMethod}"),
                                enabled);

                            if (nextEnabled != enabled)
                            {
                                if (nextEnabled)
                                    _settings.DynamicToolEnabledById[tool.Id] = true;
                                else
                                    _settings.DynamicToolEnabledById.Remove(tool.Id);
                                UnityAppBindingService.Instance.RefreshHandoffSnapshot();
                            }

                            if (!string.IsNullOrWhiteSpace(tool.Descriptor.Description))
                            {
                                EditorGUILayout.LabelField(
                                    tool.Descriptor.Description,
                                    EditorStyles.wordWrappedMiniLabel);
                            }
                        }
                    }
                }

                if (_runtimeToolCatalog.Diagnostics.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        string.Join("\n", _runtimeToolCatalog.Diagnostics.Take(6)) +
                        (_runtimeToolCatalog.Diagnostics.Count > 6 ? "\n..." : ""),
                        MessageType.Warning);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawAdvancedDotCraftSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Advanced DotCraft", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "DotCraft-specific settings for App Binding, ACP sessions, environment variables, and MCP servers injected into DotCraft sessions.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(8);
                DrawAppBindingStatusRows();

                EditorGUILayout.Space(8);
                DrawGeneralSettingsSection();

                EditorGUILayout.Space(8);
                DrawEnvironmentVariablesSection();

                EditorGUILayout.Space(8);
                DrawMcpServersSection();
            }
        }

        private void DrawAppBindingStatusRows()
        {
            EditorGUILayout.LabelField("DotCraft App Binding", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Advanced handoffs for DotCraft Desktop, TUI, automations, and AppServer workflows. The Local Tool Gateway setting above controls the shared localhost server.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUI.indentLevel++;
            var service = UnityAppBindingService.Instance;
            DrawReadonlyRow("Handoff URL", service.LocalServerUrl);

            var bindings = service.ActiveBindings.ToList();
            if (bindings.Count == 0)
            {
                EditorGUILayout.LabelField("Active Bindings", "None", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var binding in bindings)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            "Active Binding",
                            $"{binding.ThreadId} ({binding.ToolCount} tools)",
                            EditorStyles.miniLabel);
                        if (GUILayout.Button("Remove", GUILayout.Width(80)))
                            service.RemoveActiveBinding(binding.BindingId);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawGeneralSettingsSection()
        {
            EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _settings.AutoReconnect = EditorGUILayout.Toggle(
                new GUIContent("Auto Reconnect", "Automatically reconnect after Domain Reload"),
                _settings.AutoReconnect);

            _settings.VerboseLogging = EditorGUILayout.Toggle(
                new GUIContent("Verbose Logging", "Enable detailed logging for debugging"),
                _settings.VerboseLogging);

            _settings.ShowThinkingContent = EditorGUILayout.Toggle(
                new GUIContent("Show Thinking Content",
                    "Show agent reasoning text in expandable chat rows. When disabled, DotCraft still shows live thinking status."),
                _settings.ShowThinkingContent);

            _settings.RequestTimeoutSeconds = EditorGUILayout.IntSlider(
                new GUIContent("Request Timeout (s)", "Timeout for ACP requests in seconds"),
                _settings.RequestTimeoutSeconds, 5, 120);

            _settings.MaxHistoryMessages = EditorGUILayout.IntField(
                new GUIContent("Max History Messages", "Maximum number of messages to keep in history"),
                _settings.MaxHistoryMessages);

            EditorGUI.indentLevel--;
        }

        private void DrawEnvironmentVariablesSection()
        {
            EditorGUILayout.LabelField("Environment Variables", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Add environment variables like API keys. These are injected into the in-editor agent process only; MCP Gateway setup never writes secrets.",
                MessageType.Info);

            EditorGUI.indentLevel++;

            var keys = new List<string>(_settings.EnvironmentVariables.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var key = EditorGUILayout.TextField(keys[i], GUILayout.Width(150));
                var value = EditorGUILayout.TextField(_settings.EnvironmentVariables[keys[i]]);

                if (key != keys[i])
                {
                    _settings.EnvironmentVariables.Remove(keys[i]);
                    if (!string.IsNullOrEmpty(key))
                    {
                        _settings.EnvironmentVariables[key] = value;
                    }
                }
                else
                {
                    _settings.EnvironmentVariables[keys[i]] = value;
                }

                if (GUILayout.Button("×", GUILayout.Width(25)))
                {
                    _settings.EnvironmentVariables.Remove(keys[i]);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15);
            if (GUILayout.Button("+ Add Variable", GUILayout.Width(120)))
            {
                _settings.EnvironmentVariables["NEW_KEY"] = "";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        private (int builtIn, int custom) GetEnabledToolCounts()
        {
            _runtimeToolCatalog ??= RuntimeToolCatalog.Discover();
            var enabledPluginToolIds = new HashSet<string>(
                _settings.DynamicToolEnabledById
                    .Where(pair => pair.Value)
                    .Select(pair => pair.Key),
                StringComparer.Ordinal);
            var resolution = RuntimeToolCatalog.ResolveEnabledTools(
                _runtimeToolCatalog,
                _settings.EnableBuiltinUnityTools,
                id => enabledPluginToolIds.Contains(id));

            return (
                resolution.Tools.Count(tool => tool.Source == RuntimeToolSource.Builtin),
                resolution.Tools.Count(tool => tool.Source == RuntimeToolSource.Plugin));
        }

        private static void DrawReadonlyRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(140));
                EditorGUILayout.LabelField(value ?? string.Empty, EditorStyles.miniLabel);
            }
        }

        private void RefreshRuntimeToolCatalog()
        {
            _runtimeToolCatalog = RuntimeToolCatalog.Discover();
        }

        private void DrawMcpServersSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("MCP Servers for DotCraft Sessions", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "MCP servers defined here are injected into every new DotCraft session via the ACP " +
                    "mcpServers field. This does not configure dotcraft-unity as an MCP server for external clients; use MCP Tool Gateway Setup above for that.",
                    MessageType.Info);

                // Ensure foldout list is in sync with server list length
                while (_mcpServerFoldouts.Count < _settings.McpServers.Count)
                    _mcpServerFoldouts.Add(true);
                while (_mcpServerFoldouts.Count > _settings.McpServers.Count)
                    _mcpServerFoldouts.RemoveAt(_mcpServerFoldouts.Count - 1);

                int removeIndex = -1;

                for (int i = 0; i < _settings.McpServers.Count; i++)
                {
                    var server = _settings.McpServers[i];
                    EditorGUILayout.Space(4);

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        // Header row: foldout + enabled toggle + remove button
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var label = string.IsNullOrWhiteSpace(server.Name) ? $"Server {i + 1}" : server.Name;
                            _mcpServerFoldouts[i] = EditorGUILayout.Foldout(_mcpServerFoldouts[i], label, true, EditorStyles.foldoutHeader);

                            GUILayout.FlexibleSpace();

                            server.Enabled = EditorGUILayout.ToggleLeft(
                                "Enabled", server.Enabled, GUILayout.Width(70));

                            if (GUILayout.Button("Remove", GUILayout.Width(65)))
                                removeIndex = i;
                        }

                        if (!_mcpServerFoldouts[i])
                            continue;

                        EditorGUI.indentLevel++;

                        server.Name = EditorGUILayout.TextField(
                            new GUIContent("Name", "Unique name for this MCP server"),
                            server.Name);

                        // Transport popup
                        var transportIndex = server.Transport == "http" ? 1 : 0;
                        var newTransportIndex = EditorGUILayout.Popup(
                            new GUIContent("Transport", "Communication transport: stdio or http"),
                            transportIndex, TransportOptions);
                        server.Transport = TransportOptions[newTransportIndex];

                        if (server.Transport == "stdio")
                        {
                            DrawStdioFields(server);
                        }
                        else
                        {
                            DrawHttpFields(server);
                        }

                        EditorGUI.indentLevel--;
                    }
                }

                // Remove outside the loop to avoid modifying the list mid-iteration
                if (removeIndex >= 0)
                {
                    _settings.McpServers.RemoveAt(removeIndex);
                    _mcpServerFoldouts.RemoveAt(removeIndex);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);

                    if (GUILayout.Button("+ Add stdio Server", GUILayout.Width(140)))
                    {
                        _settings.McpServers.Add(new McpServerEntry { Transport = "stdio" });
                        _mcpServerFoldouts.Add(true);
                    }

                    if (GUILayout.Button("+ Add http Server", GUILayout.Width(130)))
                    {
                        _settings.McpServers.Add(new McpServerEntry { Transport = "http" });
                        _mcpServerFoldouts.Add(true);
                    }
                }
            }
        }

        private static void DrawStdioFields(McpServerEntry server)
        {
            server.Command = EditorGUILayout.TextField(
                new GUIContent("Command", "Executable to launch (e.g. npx, node, python)"),
                server.Command ?? "");

            // Arguments — one per line in a text area, displayed joined
            EditorGUILayout.LabelField(new GUIContent("Arguments", "One argument per line"));
            server.Arguments ??= new List<string>();
            var argsText = string.Join("\n", server.Arguments);
            var newArgsText = EditorGUILayout.TextArea(argsText, GUILayout.MinHeight(40));
            if (newArgsText != argsText)
            {
                server.Arguments = new List<string>(
                    newArgsText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }

            // Environment variables
            EditorGUILayout.LabelField("Environment Variables", EditorStyles.boldLabel);
            server.EnvironmentVariables ??= new Dictionary<string, string>();

            var envKeys = new List<string>(server.EnvironmentVariables.Keys);
            for (int j = 0; j < envKeys.Count; j++)
            {
                EditorGUILayout.BeginHorizontal();
                var envKey = EditorGUILayout.TextField(envKeys[j], GUILayout.Width(150));
                var envVal = EditorGUILayout.TextField(server.EnvironmentVariables[envKeys[j]]);

                if (envKey != envKeys[j])
                {
                    server.EnvironmentVariables.Remove(envKeys[j]);
                    if (!string.IsNullOrEmpty(envKey))
                        server.EnvironmentVariables[envKey] = envVal;
                }
                else
                {
                    server.EnvironmentVariables[envKeys[j]] = envVal;
                }

                if (GUILayout.Button("×", GUILayout.Width(25)))
                {
                    server.EnvironmentVariables.Remove(envKeys[j]);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15);
            if (GUILayout.Button("+ Add Env Var", GUILayout.Width(110)))
                server.EnvironmentVariables["NEW_KEY"] = "";
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawHttpFields(McpServerEntry server)
        {
            server.Url = EditorGUILayout.TextField(
                new GUIContent("URL", "HTTP endpoint for the MCP server (e.g. https://mcp.example.com/mcp)"),
                server.Url ?? "");

            // Headers
            EditorGUILayout.LabelField("Headers", EditorStyles.boldLabel);
            server.Headers ??= new Dictionary<string, string>();

            var headerKeys = new List<string>(server.Headers.Keys);
            for (int j = 0; j < headerKeys.Count; j++)
            {
                EditorGUILayout.BeginHorizontal();
                var hKey = EditorGUILayout.TextField(headerKeys[j], GUILayout.Width(150));
                var hVal = EditorGUILayout.TextField(server.Headers[headerKeys[j]]);

                if (hKey != headerKeys[j])
                {
                    server.Headers.Remove(headerKeys[j]);
                    if (!string.IsNullOrEmpty(hKey))
                        server.Headers[hKey] = hVal;
                }
                else
                {
                    server.Headers[headerKeys[j]] = hVal;
                }

                if (GUILayout.Button("×", GUILayout.Width(25)))
                {
                    server.Headers.Remove(headerKeys[j]);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15);
            if (GUILayout.Button("+ Add Header", GUILayout.Width(100)))
                server.Headers["Authorization"] = "Bearer ";
            EditorGUILayout.EndHorizontal();
        }
    }
}

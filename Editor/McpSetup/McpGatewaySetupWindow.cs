using System;
using System.Linq;
using System.Text;
using System.Threading;
using DotCraft.Editor.AppBinding;
using DotCraft.Editor.Settings;
using DotCraft.Editor.ToolGateway;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpGatewaySetupWindow : EditorWindow
    {
        private readonly McpGatewayStatusProbe _probe = new();
        private IMcpClientConfigProvider[] _providers;
        private bool[] _selected;
        private McpInstallPreset _preset = McpInstallPreset.Recommended;
        private Vector2 _scroll;
        private string _previewText = "Click Preview Changes to inspect the project-level config updates.";
        private string _resultText = string.Empty;
        private McpGatewayProbeResult _probeResult;
        private bool _isTestingGateway;
        private CancellationTokenSource _probeCts;

        public static void ShowWindow()
        {
            var window = GetWindow<McpGatewaySetupWindow>("DotCraft MCP Gateway Setup");
            window.minSize = new Vector2(560, 520);
            window.Show();
        }

        private void OnEnable()
        {
            _providers = McpGatewaySetupProviders.CreateAll();
            _selected = _providers.Select(provider => provider.IsRecommendedByDefault).ToArray();
        }

        private void OnDisable()
        {
            _probeCts?.Cancel();
            _probeCts?.Dispose();
            _probeCts = null;
        }

        private void OnGUI()
        {
            EnsureState();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(10);
            DrawGatewaySection();
            EditorGUILayout.Space(10);
            DrawTargetsSection();
            EditorGUILayout.Space(10);
            DrawPresetSection();
            EditorGUILayout.Space(10);
            DrawActionsSection();
            EditorGUILayout.Space(10);
            DrawPreviewSection();
            EditorGUILayout.EndScrollView();
        }

        private void DrawGatewaySection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("MCP Tool Gateway", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Connect external MCP clients to the enabled Unity tool surface while this Editor is running.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(6);

                var service = UnityAppBindingService.Instance;
                DrawReadonlyRow("Project Root", McpGatewaySetupDefaults.ProjectRoot);
                DrawReadonlyRow("MCP Endpoint", McpGatewaySetupDefaults.Endpoint);
                DrawReadonlyRow("Gateway Status", service.IsLocalServerRunning ? "Running" : "Stopped");
                DrawReadonlyRow("Enabled Tools", $"{UnityToolGateway.Instance.ListTools().Count} tool(s)");

                if (!string.IsNullOrWhiteSpace(service.LastError))
                    EditorGUILayout.HelpBox(service.LastError, MessageType.Warning);

                if (_probeResult != null)
                {
                    var type = _probeResult.Success ? MessageType.Info : MessageType.Warning;
                    var text = _probeResult.Success
                        ? $"{_probeResult.Status}: {_probeResult.ToolSummary}"
                        : $"{_probeResult.Status}: {_probeResult.Error}";
                    EditorGUILayout.HelpBox(text, type);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(_isTestingGateway ? "Testing..." : "Test Gateway", GUILayout.Width(120)))
                        TestGateway();

                    if (GUILayout.Button("Enable / Restart Gateway", GUILayout.Width(170)))
                        EnableAndRestartGateway();

                    if (GUILayout.Button("Copy Endpoint", GUILayout.Width(120)))
                        EditorGUIUtility.systemCopyBuffer = McpGatewaySetupDefaults.Endpoint;

                    if (GUILayout.Button("Open Project Root", GUILayout.Width(130)))
                        EditorUtility.RevealInFinder(McpGatewaySetupDefaults.ProjectRoot);
                }
            }
        }

        private void DrawTargetsSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Targets", EditorStyles.boldLabel);
                for (var i = 0; i < _providers.Length; i++)
                {
                    var provider = _providers[i];
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        _selected[i] = EditorGUILayout.ToggleLeft(
                            $"{provider.DisplayName}  ({provider.RelativePath})",
                            _selected[i],
                            EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(provider.GetSetupHint(BuildOptions()), EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }

        private void DrawPresetSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);
                _preset = (McpInstallPreset)EditorGUILayout.Popup(
                    "Mode",
                    (int)_preset,
                    new[] { "Recommended", "Codex Read-only" });

                if (_preset == McpInstallPreset.CodexReadOnly)
                {
                    EditorGUILayout.HelpBox(
                        "Codex config will include an enabled_tools allowlist for read-only Unity tools. Claude Code and Cursor still rely on their own MCP approval controls.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Recommended uses prompt approval where supported and exposes the enabled Unity tool surface.",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawActionsSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview Changes", GUILayout.Height(26)))
                    BuildPreview();

                if (GUILayout.Button("Install / Update", GUILayout.Height(26)))
                    InstallSelected();

                if (GUILayout.Button("Uninstall", GUILayout.Height(26)))
                    UninstallSelected();
            }

            if (!string.IsNullOrWhiteSpace(_resultText))
                EditorGUILayout.HelpBox(_resultText, MessageType.Info);
        }

        private void DrawPreviewSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                _previewText = EditorGUILayout.TextArea(_previewText, GUILayout.MinHeight(190));
            }
        }

        private void BuildPreview()
        {
            var builder = new StringBuilder();
            var options = BuildOptions();
            foreach (var provider in SelectedProviders())
            {
                var preview = provider.Preview(McpGatewaySetupDefaults.ProjectRoot, options);
                builder.AppendLine(provider.DisplayName);
                builder.AppendLine(preview.Path);
                if (!preview.IsValid)
                {
                    builder.AppendLine("Error: " + preview.Error);
                }
                else
                {
                    builder.AppendLine(preview.HasChanges ? TextDiffPreview.Build(preview.Before, preview.After) : "No changes.");
                }

                builder.AppendLine();
            }

            _previewText = builder.Length == 0 ? "No targets selected." : builder.ToString();
            _resultText = string.Empty;
        }

        private void InstallSelected()
        {
            var builder = new StringBuilder();
            var options = BuildOptions();
            foreach (var provider in SelectedProviders())
            {
                var result = provider.Install(McpGatewaySetupDefaults.ProjectRoot, options);
                AppendResult(builder, provider, result);
            }

            _resultText = builder.Length == 0 ? "No targets selected." : builder.ToString();
            BuildPreview();
        }

        private void UninstallSelected()
        {
            var builder = new StringBuilder();
            foreach (var provider in SelectedProviders())
            {
                var result = provider.Uninstall(McpGatewaySetupDefaults.ProjectRoot);
                AppendResult(builder, provider, result);
            }

            _resultText = builder.Length == 0 ? "No targets selected." : builder.ToString();
            BuildPreview();
        }

        private async void TestGateway()
        {
            if (_isTestingGateway)
                return;

            _isTestingGateway = true;
            _probeResult = null;
            _probeCts?.Cancel();
            _probeCts?.Dispose();
            _probeCts = new CancellationTokenSource();
            try
            {
                _probeResult = await _probe.ProbeAsync(McpGatewaySetupDefaults.Endpoint, _probeCts.Token);
            }
            catch (Exception ex)
            {
                _probeResult = McpGatewayProbeResult.Failed("Probe failed", ex.Message);
            }
            finally
            {
                _isTestingGateway = false;
                Repaint();
            }
        }

        private static void EnableAndRestartGateway()
        {
            var settings = DotCraftSettings.Instance;
            settings.EnableAppBindingLocalServer = true;
            settings.Save();
            UnityAppBindingService.Instance.RestartLocalServer();
        }

        private McpInstallOptions BuildOptions() =>
            McpGatewaySetupDefaults.CreateOptions(_preset);

        private IMcpClientConfigProvider[] SelectedProviders() =>
            _providers.Where((_, index) => _selected[index]).ToArray();

        private static void AppendResult(
            StringBuilder builder,
            IMcpClientConfigProvider provider,
            McpInstallResult result)
        {
            var status = result.Success
                ? result.Changed ? "updated" : "unchanged"
                : "failed";
            builder.Append(provider.DisplayName).Append(": ").Append(status);
            if (!string.IsNullOrWhiteSpace(result.Error))
                builder.Append(" - ").Append(result.Error);
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
                builder.Append(" (backup: ").Append(result.BackupPath).Append(")");
            builder.AppendLine();
        }

        private static void DrawReadonlyRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(105));
                EditorGUILayout.SelectableLabel(value ?? string.Empty, EditorStyles.miniLabel, GUILayout.Height(16));
            }
        }

        private void EnsureState()
        {
            if (_providers == null || _providers.Length == 0)
                _providers = McpGatewaySetupProviders.CreateAll();
            if (_selected == null || _selected.Length != _providers.Length)
                _selected = _providers.Select(provider => provider.IsRecommendedByDefault).ToArray();
        }
    }
}

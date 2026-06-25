using System;
using System.Collections.Generic;
using System.Threading;
using DotCraft.Editor.AppBinding;
using DotCraft.Editor.Settings;
using DotCraft.Editor.ToolGateway;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpGatewaySetupWindow : EditorWindow
    {
        private readonly McpGatewayStatusProbe _probe = new();
        private readonly List<ClientCardView> _clientCards = new();

        private IMcpClientConfigProvider[] _providers;
        private McpGatewayProbeResult _probeResult;
        private bool _isTestingGateway;
        private CancellationTokenSource _probeCts;

        private VisualElement _statusDot;
        private Label _statusText;
        private Label _statusSub;
        private Label _toolsValue;
        private Button _testButton;
        private VisualElement _gatewayBanner;
        private Label _gatewayBannerText;
        private VisualElement _probeBanner;
        private Label _probeBannerText;

        public static void ShowWindow()
        {
            var window = GetWindow<McpGatewaySetupWindow>("DotCraft MCP Gateway Setup");
            window.minSize = new Vector2(540, 520);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureProviders();
        }

        private void OnDisable()
        {
            CancelProbe();
        }

        public void CreateGUI()
        {
            EnsureProviders();

            var root = rootVisualElement;
            root.Clear();
            GatewayPanelView.ApplyStyle(root);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gw-scroll");
            var content = new VisualElement();
            content.AddToClassList("gw-root");
            scroll.Add(content);
            root.Add(scroll);

            content.Add(GatewayPanelView.BrandHeader(
                "MCP Tool Gateway",
                "Connect external MCP clients to Unity C# automation and enabled custom tools while this Editor is running."));

            content.Add(BuildGatewayCard());
            content.Add(BuildClientsSection());

            RefreshGatewayStatus();
            RefreshProbeBanner();
            foreach (var card in _clientCards)
                RefreshChip(card);
        }

        private VisualElement BuildGatewayCard()
        {
            var card = GatewayPanelView.Card();

            var statusRow = new VisualElement();
            statusRow.AddToClassList("gw-status-row");

            _statusDot = new VisualElement();
            _statusDot.AddToClassList("gw-dot");
            statusRow.Add(_statusDot);

            _statusText = new Label("Gateway status");
            _statusText.AddToClassList("gw-status-text");
            statusRow.Add(_statusText);

            _statusSub = new Label(string.Empty);
            _statusSub.AddToClassList("gw-status-sub");
            statusRow.Add(_statusSub);

            card.Add(statusRow);

            var endpoint = new VisualElement();
            endpoint.AddToClassList("gw-endpoint");
            var endpointUrl = new Label(McpGatewaySetupDefaults.Endpoint);
            endpointUrl.AddToClassList("gw-endpoint-url");
            endpoint.Add(endpointUrl);
            var copyButton = GatewayPanelView.Button(
                "Copy",
                () => GatewayPanelView.CopyToClipboard(McpGatewaySetupDefaults.Endpoint),
                "gw-btn--mini");
            endpoint.Add(copyButton);
            card.Add(endpoint);

            var rootRow = GatewayPanelView.KeyValueRow("Project Root", McpGatewaySetupDefaults.ProjectRoot, out var rootValue);
            rootValue.tooltip = McpGatewaySetupDefaults.ProjectRoot;
            var revealButton = GatewayPanelView.Button(
                "Reveal",
                () => EditorUtility.RevealInFinder(McpGatewaySetupDefaults.ProjectRoot),
                "gw-btn--mini");
            rootRow.Add(revealButton);
            card.Add(rootRow);

            card.Add(GatewayPanelView.KeyValueRow("Enabled Tools", string.Empty, out _toolsValue));

            _probeBanner = GatewayPanelView.Banner("gw-banner--info", out _probeBannerText);
            card.Add(_probeBanner);

            _gatewayBanner = GatewayPanelView.Banner("gw-banner--warn", out _gatewayBannerText);
            card.Add(_gatewayBanner);

            var buttons = new VisualElement();
            buttons.AddToClassList("gw-btn-row");
            _testButton = GatewayPanelView.Button("Test Gateway", TestGateway);
            buttons.Add(_testButton);
            buttons.Add(GatewayPanelView.Button("Enable / Restart Gateway", EnableAndRestartGateway, "gw-btn--primary"));
            card.Add(buttons);

            return card;
        }

        private VisualElement BuildClientsSection()
        {
            var section = new VisualElement();
            section.Add(GatewayPanelView.SectionLabel("CONNECT A CLIENT"));

            _clientCards.Clear();
            foreach (var provider in _providers)
                section.Add(BuildClientCard(provider));

            return section;
        }

        private VisualElement BuildClientCard(IMcpClientConfigProvider provider)
        {
            var card = new VisualElement();
            card.AddToClassList("gw-client");

            var head = new VisualElement();
            head.AddToClassList("gw-client-head");

            var name = new Label(provider.DisplayName);
            name.AddToClassList("gw-client-name");
            head.Add(name);

            var path = new Label(provider.RelativePath);
            path.AddToClassList("gw-client-path");
            head.Add(path);

            var chip = GatewayPanelView.Chip("Not set up", "gw-chip--muted");
            head.Add(chip);
            card.Add(head);

            var hint = new Label(provider.GetSetupHint(BuildOptions()));
            hint.AddToClassList("gw-client-hint");
            card.Add(hint);

            var result = new Label();
            result.AddToClassList("gw-client-result");
            result.style.display = DisplayStyle.None;
            card.Add(result);

            var view = new ClientCardView(provider, chip, result);
            _clientCards.Add(view);

            var buttons = new VisualElement();
            buttons.AddToClassList("gw-btn-row");
            buttons.Add(GatewayPanelView.Button("Install / Update", () => InstallClient(view), "gw-btn--primary"));
            buttons.Add(GatewayPanelView.Button("Remove", () => UninstallClient(view), "gw-btn--danger"));
            card.Add(buttons);

            return card;
        }

        private void InstallClient(ClientCardView view)
        {
            var result = view.Provider.Install(McpGatewaySetupDefaults.ProjectRoot, BuildOptions());
            ShowClientResult(view, result);
            RefreshChip(view);
            RefreshGatewayStatus();
        }

        private void UninstallClient(ClientCardView view)
        {
            var result = view.Provider.Uninstall(McpGatewaySetupDefaults.ProjectRoot);
            ShowClientResult(view, result);
            RefreshChip(view);
            RefreshGatewayStatus();
        }

        private static void ShowClientResult(ClientCardView view, McpInstallResult result)
        {
            var status = result.Success
                ? result.Changed ? "Updated" : "No changes needed"
                : "Failed";

            var message = status;
            if (!string.IsNullOrWhiteSpace(result.Message) && result.Success && result.Changed)
                message = result.Message;
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
                message += $"  ·  backup: {System.IO.Path.GetFileName(result.BackupPath)}";
            if (!string.IsNullOrWhiteSpace(result.Error))
                message = $"{status}: {result.Error}";

            view.Result.text = message;
            view.Result.EnableInClassList("gw-client-result--error", !result.Success);
            view.Result.style.display = DisplayStyle.Flex;
        }

        private void RefreshChip(ClientCardView view)
        {
            var configured = false;
            try
            {
                configured = view.Provider.IsConfigured(McpGatewaySetupDefaults.ProjectRoot);
            }
            catch
            {
                configured = false;
            }

            GatewayPanelView.SetChip(view.Chip, configured ? "Configured" : "Not set up", configured);
        }

        private void RefreshGatewayStatus()
        {
            if (_statusDot == null)
                return;

            var service = UnityAppBindingService.Instance;
            var running = service.IsLocalServerRunning;

            _statusDot.EnableInClassList("gw-dot--on", running);
            _statusDot.EnableInClassList("gw-dot--off", !running);
            _statusText.text = running ? "Gateway running" : "Gateway stopped";
            _statusSub.text = running ? "Listening on localhost" : "Enable it to accept MCP clients";

            var toolCount = UnityToolGateway.Instance.ListTools().Count;
            _toolsValue.text = $"{toolCount} tool(s) exposed";

            var error = service.LastError;
            GatewayPanelView.SetBanner(
                _gatewayBanner,
                _gatewayBannerText,
                string.IsNullOrWhiteSpace(error) ? null : error,
                "gw-banner--warn");
        }

        private void RefreshProbeBanner()
        {
            if (_probeBanner == null)
                return;

            if (_probeResult == null)
            {
                _probeBanner.style.display = DisplayStyle.None;
                return;
            }

            var success = _probeResult.Success;
            var text = success
                ? $"{_probeResult.Status}: {_probeResult.ToolSummary}"
                : $"{_probeResult.Status}: {_probeResult.Error}";

            GatewayPanelView.SetBanner(
                _probeBanner,
                _probeBannerText,
                text,
                success ? "gw-banner--info" : "gw-banner--error");
        }

        private async void TestGateway()
        {
            if (_isTestingGateway)
                return;

            _isTestingGateway = true;
            _probeResult = null;
            UpdateTestButton();
            RefreshProbeBanner();

            CancelProbe();
            _probeCts = new CancellationTokenSource();
            try
            {
                _probeResult = await _probe.ProbeAsync(McpGatewaySetupDefaults.Endpoint, _probeCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _probeResult = McpGatewayProbeResult.Failed("Probe failed", ex.Message);
            }
            finally
            {
                _isTestingGateway = false;
                UpdateTestButton();
                RefreshProbeBanner();
            }
        }

        private void UpdateTestButton()
        {
            if (_testButton == null)
                return;

            _testButton.text = _isTestingGateway ? "Testing..." : "Test Gateway";
            _testButton.SetEnabled(!_isTestingGateway);
        }

        private void EnableAndRestartGateway()
        {
            var settings = DotCraftSettings.Instance;
            settings.EnableAppBindingLocalServer = true;
            settings.Save();
            UnityAppBindingService.Instance.RestartLocalServer();
            RefreshGatewayStatus();
        }

        private void CancelProbe()
        {
            _probeCts?.Cancel();
            _probeCts?.Dispose();
            _probeCts = null;
        }

        private static McpInstallOptions BuildOptions() =>
            McpGatewaySetupDefaults.CreateOptions();

        private void EnsureProviders()
        {
            if (_providers == null || _providers.Length == 0)
                _providers = McpGatewaySetupProviders.CreateAll();
        }

        private sealed class ClientCardView
        {
            public ClientCardView(IMcpClientConfigProvider provider, Label chip, Label result)
            {
                Provider = provider;
                Chip = chip;
                Result = result;
            }

            public IMcpClientConfigProvider Provider { get; }

            public Label Chip { get; }

            public Label Result { get; }
        }
    }
}

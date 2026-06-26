using System.Collections.Generic;
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
        private readonly List<ClientCardView> _clientCards = new();

        private IMcpClientConfigProvider[] _providers;
        private AgentSkillInstaller _skillInstaller;

        private VisualElement _statusDot;
        private Label _statusText;
        private Label _statusSub;
        private Label _toolsValue;
        private VisualElement _gatewayBanner;
        private Label _gatewayBannerText;

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
            var copyButton = GatewayPanelView.CopyIconButton(
                "Copy MCP endpoint",
                () => GatewayPanelView.CopyToClipboard(McpGatewaySetupDefaults.Endpoint));
            endpoint.Add(copyButton);
            card.Add(endpoint);

            var rootRow = GatewayPanelView.KeyValueRow("Project Root", McpGatewaySetupDefaults.ProjectRoot, out var rootValue);
            rootValue.tooltip = McpGatewaySetupDefaults.ProjectRoot;
            var revealButton = GatewayPanelView.IconButton(
                "Reveal project root",
                () => EditorUtility.RevealInFinder(McpGatewaySetupDefaults.ProjectRoot),
                "↗",
                "FolderOpened Icon",
                "d_FolderOpened Icon",
                "Folder Icon",
                "d_Folder Icon");
            rootRow.Add(revealButton);
            card.Add(rootRow);

            card.Add(GatewayPanelView.KeyValueRow("Enabled Tools", string.Empty, out _toolsValue));

            _gatewayBanner = GatewayPanelView.Banner("gw-banner--warn", out _gatewayBannerText);
            card.Add(_gatewayBanner);

            var buttons = new VisualElement();
            buttons.AddToClassList("gw-btn-row");
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

            var skillPath = new Label("Skill: " + provider.SkillRelativePath);
            skillPath.AddToClassList("gw-client-hint");
            card.Add(skillPath);

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
            AgentSkillInstallResult skillResult = null;
            if (result.Success)
                skillResult = SkillInstaller.Install(McpGatewaySetupDefaults.ProjectRoot, view.Provider.SkillRelativePath);

            ShowClientInstallResult(view, result, skillResult);
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

        private static void ShowClientInstallResult(
            ClientCardView view,
            McpInstallResult mcpResult,
            AgentSkillInstallResult skillResult)
        {
            if (!mcpResult.Success || skillResult == null)
            {
                ShowClientResult(view, mcpResult);
                return;
            }

            var mcpStatus = mcpResult.Changed ? "MCP updated" : "MCP current";
            var skillStatus = skillResult.Success
                ? skillResult.Changed ? "skill installed" : "skill current"
                : "skill failed: " + skillResult.Error;

            var message = mcpStatus + "  ·  " + skillStatus;
            var backups = new List<string>();
            if (!string.IsNullOrWhiteSpace(mcpResult.BackupPath))
                backups.Add(System.IO.Path.GetFileName(mcpResult.BackupPath));
            if (!string.IsNullOrWhiteSpace(skillResult.BackupPath))
                backups.Add(System.IO.Path.GetFileName(skillResult.BackupPath));
            if (backups.Count > 0)
                message += "  ·  backup: " + string.Join(", ", backups);

            view.Result.text = message;
            view.Result.EnableInClassList("gw-client-result--error", !skillResult.Success);
            view.Result.style.display = DisplayStyle.Flex;
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

        private void EnableAndRestartGateway()
        {
            var settings = DotCraftSettings.Instance;
            settings.EnableAppBindingLocalServer = true;
            settings.Save();
            UnityAppBindingService.Instance.RestartLocalServer();
            RefreshGatewayStatus();
        }

        private static McpInstallOptions BuildOptions() =>
            McpGatewaySetupDefaults.CreateOptions();

        private AgentSkillInstaller SkillInstaller =>
            _skillInstaller ??= AgentSkillInstaller.CreateDefault();

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

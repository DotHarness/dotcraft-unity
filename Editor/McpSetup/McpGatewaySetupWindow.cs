using System.Collections.Generic;
using DotCraft.Editor.ToolGateway;
using DotCraft.Editor.Settings;
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
        private Label _gatewayValue;
        private Label _manifestValue;
        private Label _toolsValue;
        private VisualElement _gatewayBanner;
        private Label _gatewayBannerText;
        private string _gatewayOperationError;

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
                "MCP Gateway",
                "Connect MCP clients through a persistent stdio MCP Gateway that automatically reconnects to this Unity project."));

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

            _statusText = new Label("Unity Tool Gateway status");
            _statusText.AddToClassList("gw-status-text");
            statusRow.Add(_statusText);

            _statusSub = new Label(string.Empty);
            _statusSub.AddToClassList("gw-status-sub");
            statusRow.Add(_statusSub);

            card.Add(statusRow);

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

            card.Add(GatewayPanelView.KeyValueRow(
                "Package / MCP Gateway",
                string.Empty,
                out _gatewayValue));
            card.Add(GatewayPanelView.KeyValueRow("Tool Manifest", string.Empty, out _manifestValue));
            card.Add(GatewayPanelView.KeyValueRow("Enabled Tools", string.Empty, out _toolsValue));

            _gatewayBanner = GatewayPanelView.Banner("gw-banner--warn", out _gatewayBannerText);
            card.Add(_gatewayBanner);

            var buttons = new VisualElement();
            buttons.AddToClassList("gw-btn-row");
            buttons.Add(GatewayPanelView.Button("Install MCP Gateway", InstallGateway));
            buttons.Add(GatewayPanelView.Button("Enable / Restart Unity Tool Gateway", EnableAndRestartToolGateway, "gw-btn--primary"));
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

        private async void InstallClient(ClientCardView view)
        {
            var gatewayResult = await McpGatewayInstaller.InstallAsync();
            _gatewayOperationError = gatewayResult.Success ? null : gatewayResult.Error;
            if (!gatewayResult.Success)
            {
                ShowClientResult(view, gatewayResult);
                RefreshGatewayStatus();
                return;
            }

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

            view.Result.text = mcpStatus + "  ·  " + skillStatus;
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

            var service = UnityToolGatewayRuntime.Instance;
            var running = service.IsRunning;
            var gateway = McpGatewayInstaller.GetStatus();

            _statusDot.EnableInClassList("gw-dot--on", running);
            _statusDot.EnableInClassList("gw-dot--off", !running);
            _statusText.text = running ? "Unity Tool Gateway running" : "Unity Tool Gateway stopped";
            _statusSub.text = running
                ? "MCP Gateway calls can reach this Editor"
                : "MCP clients stay connected while Unity is unavailable";

            _gatewayValue.text = gateway.IsInstalled
                ? $"{DotCraftPackageInfo.Version} / {gateway.Version} installed"
                : $"{DotCraftPackageInfo.Version} / not installed";
            _gatewayValue.tooltip = gateway.ExecutablePath;
            _manifestValue.text = ShortRevision(service.ManifestRevision);
            _toolsValue.text = $"{service.ToolCount} tool(s) exposed";

            var error = !string.IsNullOrWhiteSpace(_gatewayOperationError)
                ? _gatewayOperationError
                : string.IsNullOrWhiteSpace(gateway.Error) ? service.LastError : gateway.Error;
            GatewayPanelView.SetBanner(
                _gatewayBanner,
                _gatewayBannerText,
                string.IsNullOrWhiteSpace(error) ? null : error,
                "gw-banner--warn");
        }

        private async void InstallGateway()
        {
            var result = await McpGatewayInstaller.InstallAsync();
            _gatewayOperationError = result.Success ? null : result.Error;
            RefreshGatewayStatus();
        }

        private void EnableAndRestartToolGateway()
        {
            var settings = DotCraftSettings.Instance;
            settings.EnableToolGateway = true;
            settings.Save();
            UnityToolGatewayRuntime.Instance.Restart();
            RefreshGatewayStatus();
        }

        private static string ShortRevision(string revision)
        {
            if (string.IsNullOrWhiteSpace(revision))
                return "Unavailable";
            return revision.Length <= 24 ? revision : revision.Substring(0, 24) + "…";
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

using System;
using System.Collections.Generic;
using DotCraft.Editor.McpSetup;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.ToolGateway
{
    /// <summary>
    /// Borderless dropdown shown from the tool gateway status indicator, listing what is attached to
    /// this Editor: the Assistant agent and every MCP client.
    /// </summary>
    internal sealed class ToolGatewayStatusDropdown : EditorWindow
    {
        private const float Width = 320f;
        private const float MinHeight = 190f;
        private const float MaxHeight = 460f;
        private const float RowHeight = 24f;
        private const float SectionHeight = 26f;
        private const float ChromeHeight = 106f;
        private const float ErrorBannerHeight = 54f;
        private const long TickIntervalMilliseconds = 1000;

        private ToolGatewayStatusSummary _summary = ToolGatewayStatusSummary.Empty;
        private readonly Dictionary<string, Label> _clientActivity = new(StringComparer.Ordinal);

        private Label _subtitleLabel;
        private VisualElement _agentSection;
        private VisualElement _clientSection;
        private Label _clientSectionLabel;
        private VisualElement _banner;
        private Label _bannerText;
        private Label _agentActivity;
        private string _appliedStructureKey;
        private bool _subscribed;
        private bool _dirty;

        public static void Show(Rect screenActivatorRect, ToolGatewayStatusSummary summary)
        {
            var window = CreateInstance<ToolGatewayStatusDropdown>();
            window._summary = summary ?? ToolGatewayStatusSummary.Empty;
            window.ShowAsDropDown(screenActivatorRect, new Vector2(Width, EstimateHeight(window._summary)));
        }

        /// <summary>
        /// The dropdown size must be known before it opens, so it is estimated from the row count.
        /// </summary>
        internal static float EstimateHeight(ToolGatewayStatusSummary summary)
        {
            summary ??= ToolGatewayStatusSummary.Empty;

            var height = ChromeHeight
                         + SectionHeight + RowHeight
                         + SectionHeight + Mathf.Max(1, summary.Clients.Count) * RowHeight;
            if (!string.IsNullOrWhiteSpace(summary.LastError))
                height += ErrorBannerHeight;

            return Mathf.Clamp(height, MinHeight, MaxHeight);
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed)
                return;

            DotCraftAgentPresence.Changed += OnPresenceChanged;
            UnityToolGatewayRuntime.Instance.StatusChanged += OnPresenceChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            DotCraftAgentPresence.Changed -= OnPresenceChanged;
            UnityToolGatewayRuntime.Instance.StatusChanged -= OnPresenceChanged;
            _subscribed = false;
        }

        /// <summary>
        /// Only flags work: this can fire before CreateGUI has built the tree.
        /// </summary>
        private void OnPresenceChanged() => _dirty = true;

        public void CreateGUI()
        {
            GatewayPanelView.ApplyStyle(rootVisualElement);

            var panel = new VisualElement();
            panel.AddToClassList("gw-dropdown");
            rootVisualElement.Add(panel);

            panel.Add(GatewayPanelView.BrandHeader("DotCraft Unity", _summary.HeaderSubtitle, out _subtitleLabel));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gw-scroll");
            panel.Add(scroll);

            scroll.Add(GatewayPanelView.SectionLabel("ASSISTANT"));
            _agentSection = new VisualElement();
            scroll.Add(_agentSection);

            _clientSectionLabel = GatewayPanelView.SectionLabel("MCP CLIENTS");
            _clientSectionLabel.style.marginTop = 8;
            scroll.Add(_clientSectionLabel);
            _clientSection = new VisualElement();
            scroll.Add(_clientSection);

            _banner = GatewayPanelView.Banner("gw-banner--warn", out _bannerText);
            scroll.Add(_banner);

            panel.Add(BuildFooter());

            Apply(_summary);
            rootVisualElement.schedule.Execute(Tick).Every(TickIntervalMilliseconds);
        }

        private void Tick()
        {
            var summary = ToolGatewayStatusSource.Capture();
            if (_dirty)
            {
                _dirty = false;
                Apply(summary);
                return;
            }

            ApplyActivity(summary);
        }

        private void Apply(ToolGatewayStatusSummary summary)
        {
            _summary = summary ?? ToolGatewayStatusSummary.Empty;

            if (!string.Equals(_summary.StructureKey, _appliedStructureKey, StringComparison.Ordinal))
            {
                _appliedStructureKey = _summary.StructureKey;
                RebuildAgentSection(_summary);
                RebuildClientSection(_summary);
                GatewayPanelView.SetBanner(_banner, _bannerText, _summary.LastError, "gw-banner--warn");
            }

            ApplyActivity(_summary);
        }

        private void ApplyActivity(ToolGatewayStatusSummary summary)
        {
            _subtitleLabel.text = summary.HeaderSubtitle;

            if (_agentActivity != null)
                _agentActivity.text = ResolveAgentActivity(summary.Agent);

            foreach (var client in summary.Clients)
            {
                if (_clientActivity.TryGetValue(client.SessionId, out var label))
                    label.text = client.ActivityText;
            }
        }

        private void RebuildAgentSection(ToolGatewayStatusSummary summary)
        {
            _agentSection.Clear();
            _agentActivity = null;

            var agent = summary.Agent;
            if (!agent.IsActive)
            {
                _agentSection.Add(EmptyRow("Not connected"));
                return;
            }

            var name = string.IsNullOrWhiteSpace(agent.Version) ? agent.Name : $"{agent.Name} {agent.Version}";
            _agentSection.Add(ConnectionRow(agent.IsConnected, name, out _agentActivity));
        }

        private void RebuildClientSection(ToolGatewayStatusSummary summary)
        {
            _clientSection.Clear();
            _clientActivity.Clear();
            _clientSectionLabel.text = $"MCP CLIENTS ({summary.Clients.Count})";

            if (summary.Clients.Count == 0)
            {
                _clientSection.Add(EmptyRow(summary.IsRunning ? "None connected" : "Gateway stopped"));
                return;
            }

            foreach (var client in summary.Clients)
            {
                _clientSection.Add(ConnectionRow(true, client.Name, out var activity));
                _clientActivity[client.SessionId] = activity;
            }
        }

        private static string ResolveAgentActivity(AgentPresenceSnapshot agent)
        {
            if (agent.IsConnecting)
                return "connecting";
            return agent.ConnectedAtUtc.HasValue
                ? ToolGatewayRelativeTime.DurationSince(agent.ConnectedAtUtc.Value, DateTime.UtcNow)
                : string.Empty;
        }

        private static VisualElement ConnectionRow(bool isOn, string name, out Label activity)
        {
            var row = new VisualElement();
            row.AddToClassList("gw-connection");

            var dot = new VisualElement();
            dot.AddToClassList("gw-dot");
            dot.AddToClassList(isOn ? "gw-dot--on" : "gw-dot--off");
            row.Add(dot);

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("gw-connection-name");
            row.Add(nameLabel);

            activity = new Label();
            activity.AddToClassList("gw-connection-activity");
            row.Add(activity);
            return row;
        }

        private static VisualElement EmptyRow(string text)
        {
            var row = new VisualElement();
            row.AddToClassList("gw-connection");

            var label = new Label(text);
            label.AddToClassList("gw-connection-empty");
            row.Add(label);
            return row;
        }

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList("gw-footer");
            footer.Add(FooterButton("Assistant", () =>
            {
                Close();
                ToolGatewayStatusBarActions.OpenAssistant();
            }));
            footer.Add(FooterButton("Setup MCP", () =>
            {
                Close();
                ToolGatewayStatusBarActions.OpenSetup();
            }, "gw-btn--primary"));
            footer.Add(FooterButton("Settings", () =>
            {
                Close();
                ToolGatewayStatusBarActions.OpenSettings();
            }));
            return footer;
        }

        private static Button FooterButton(string text, Action onClick, params string[] classes)
        {
            var button = GatewayPanelView.Button(text, onClick, classes);
            button.style.flexGrow = 1;
            return button;
        }
    }
}

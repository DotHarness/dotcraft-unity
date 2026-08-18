using DotCraft.Editor.McpSetup;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.ToolGateway
{
    /// <summary>
    /// Borderless UI Toolkit dropdown shown from the bottom-right MCP status indicator.
    /// </summary>
    internal sealed class McpGatewayStatusDropdown : EditorWindow
    {
        private const float Width = 340f;

        private McpGatewayStatusSummary _summary = McpGatewayStatusSummary.Empty;

        public static void Show(Rect screenActivatorRect, McpGatewayStatusSummary summary)
        {
            var window = CreateInstance<McpGatewayStatusDropdown>();
            window._summary = summary ?? McpGatewayStatusSummary.Empty;
            window.ShowAsDropDown(screenActivatorRect, new Vector2(Width, window.EstimateHeight()));
        }

        private float EstimateHeight()
        {
            var height = 202f;
            if (!string.IsNullOrWhiteSpace(_summary.LastError))
                height += 54f;
            return height;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            GatewayPanelView.ApplyStyle(root);

            var panel = new VisualElement();
            panel.AddToClassList("gw-dropdown");
            root.Add(panel);

            panel.Add(GatewayPanelView.BrandHeader(
                "DotCraft Unity",
                _summary.IsRunning ? "MCP Tool Gateway is running." : "MCP Tool Gateway is stopped."));
            panel.Add(GatewayPanelView.Divider());

            panel.Add(GatewayPanelView.KeyValueRow(
                "MCP Gateway",
                _summary.IsRunning ? "Running" : "Stopped",
                out _));

            if (string.IsNullOrWhiteSpace(_summary.Endpoint))
            {
                panel.Add(GatewayPanelView.KeyValueRow("MCP Endpoint", "Unavailable", out _));
            }
            else
            {
                var endpoint = new VisualElement();
                endpoint.AddToClassList("gw-endpoint");

                var url = new Label(_summary.Endpoint);
                url.AddToClassList("gw-endpoint-url");
                endpoint.Add(url);

                endpoint.Add(GatewayPanelView.CopyIconButton(
                    "Copy MCP endpoint",
                    () =>
                    {
                        McpGatewayStatusBarActions.CopyMcpUrl(_summary.Endpoint);
                        Close();
                    }));
                panel.Add(endpoint);
            }

            if (!string.IsNullOrWhiteSpace(_summary.LastError))
            {
                var banner = GatewayPanelView.Banner("gw-banner--warn", out var bannerText);
                GatewayPanelView.SetBanner(banner, bannerText, _summary.LastError, "gw-banner--warn");
                panel.Add(banner);
            }

            panel.Add(GatewayPanelView.Divider());
            panel.Add(BuildFooter());
        }

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList("gw-footer");

            footer.Add(FooterButton("Assistant", () =>
            {
                Close();
                McpGatewayStatusBarActions.OpenAssistant();
            }));
            footer.Add(FooterButton("Setup MCP", () =>
            {
                Close();
                McpGatewayStatusBarActions.OpenSetup();
            }, "gw-btn--primary"));
            footer.Add(FooterButton("Settings", () =>
            {
                Close();
                McpGatewayStatusBarActions.OpenSettings();
            }));

            return footer;
        }

        private static Button FooterButton(string text, System.Action onClick, params string[] classes)
        {
            var button = GatewayPanelView.Button(text, onClick, classes);
            button.style.flexGrow = 1;
            return button;
        }
    }
}

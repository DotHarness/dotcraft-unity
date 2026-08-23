using DotCraft.Editor.McpSetup;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.ToolGateway
{
    /// <summary>
    /// Borderless UI Toolkit dropdown shown from the bottom-right tool gateway status indicator.
    /// </summary>
    internal sealed class ToolGatewayStatusDropdown : EditorWindow
    {
        private const float Width = 340f;
        private ToolGatewayStatusSummary _summary = ToolGatewayStatusSummary.Empty;

        public static void Show(Rect screenActivatorRect, ToolGatewayStatusSummary summary)
        {
            var window = CreateInstance<ToolGatewayStatusDropdown>();
            window._summary = summary ?? ToolGatewayStatusSummary.Empty;
            window.ShowAsDropDown(screenActivatorRect, new Vector2(Width, window.EstimateHeight()));
        }

        private float EstimateHeight()
        {
            var height = 224f;
            if (!string.IsNullOrWhiteSpace(_summary.LastError))
                height += 54f;
            return height;
        }

        public void CreateGUI()
        {
            var panel = new VisualElement();
            GatewayPanelView.ApplyStyle(rootVisualElement);
            panel.AddToClassList("gw-dropdown");
            rootVisualElement.Add(panel);

            panel.Add(GatewayPanelView.BrandHeader(
                "DotCraft Unity",
                _summary.IsRunning ? "Gateway is running." : "Gateway is stopped."));
            panel.Add(GatewayPanelView.Divider());
            panel.Add(GatewayPanelView.KeyValueRow(
                "Gateway",
                _summary.IsRunning ? "Running" : "Stopped",
                out _));
            panel.Add(GatewayPanelView.KeyValueRow("Package", _summary.PackageVersion, out _));
            panel.Add(GatewayPanelView.KeyValueRow("Tools", _summary.ToolCount.ToString(), out _));
            panel.Add(GatewayPanelView.KeyValueRow(
                "Manifest",
                ShortRevision(_summary.ManifestRevision),
                out _));

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

        private static Button FooterButton(string text, System.Action onClick, params string[] classes)
        {
            var button = GatewayPanelView.Button(text, onClick, classes);
            button.style.flexGrow = 1;
            return button;
        }

        private static string ShortRevision(string revision)
        {
            if (string.IsNullOrWhiteSpace(revision))
                return "Unavailable";
            return revision.Length <= 20 ? revision : revision.Substring(0, 20) + "…";
        }
    }
}

using DotCraft.Editor.McpSetup;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.AppBinding
{
    /// <summary>
    /// Borderless UIToolkit dropdown shown from the bottom-right status-bar logo. Replaces the
    /// legacy IMGUI <c>PopupWindowContent</c> with a panel that matches the DotCraft design system.
    /// </summary>
    internal sealed class UnityAppBindingStatusDropdown : EditorWindow
    {
        private const float Width = 340f;

        private UnityAppBindingStatusSummary _summary = UnityAppBindingStatusSummary.Empty;

        public static void Show(Rect screenActivatorRect, UnityAppBindingStatusSummary summary)
        {
            var window = CreateInstance<UnityAppBindingStatusDropdown>();
            window._summary = summary ?? UnityAppBindingStatusSummary.Empty;
            window.ShowAsDropDown(screenActivatorRect, new Vector2(Width, window.EstimateHeight()));
        }

        private float EstimateHeight()
        {
            var height = 226f;
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

            panel.Add(GatewayPanelView.BrandHeader("DotCraft Unity", BuildCaption()));
            panel.Add(GatewayPanelView.Divider());

            panel.Add(GatewayPanelView.KeyValueRow(
                "Local Server",
                _summary.IsLocalServerRunning ? "Running" : "Stopped",
                out _));
            panel.Add(GatewayPanelView.KeyValueRow("Active Bindings", FormatBindings(), out _));

            if (string.IsNullOrWhiteSpace(_summary.GatewayMcpUrl))
            {
                panel.Add(GatewayPanelView.KeyValueRow("MCP Endpoint", "Unavailable", out _));
            }
            else
            {
                var endpoint = new VisualElement();
                endpoint.AddToClassList("gw-endpoint");

                var url = new Label(_summary.GatewayMcpUrl);
                url.AddToClassList("gw-endpoint-url");
                endpoint.Add(url);

                endpoint.Add(GatewayPanelView.Button(
                    "Copy",
                    () =>
                    {
                        UnityAppBindingStatusBarActions.CopyMcpUrl(_summary.GatewayMcpUrl);
                        Close();
                    },
                    "gw-btn--mini"));
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
                UnityAppBindingStatusBarActions.OpenAssistant();
            }));
            footer.Add(FooterButton("Setup MCP", () =>
            {
                Close();
                UnityAppBindingStatusBarActions.OpenSetup();
            }, "gw-btn--primary"));
            footer.Add(FooterButton("Settings", () =>
            {
                Close();
                UnityAppBindingStatusBarActions.OpenSettings();
            }));

            return footer;
        }

        private static Button FooterButton(string text, System.Action onClick, params string[] classes)
        {
            var button = GatewayPanelView.Button(text, onClick, classes);
            button.style.flexGrow = 1;
            return button;
        }

        private string BuildCaption()
        {
            if (!_summary.IsLocalServerRunning)
                return "Local server is stopped.";

            return _summary.BindingCount <= 0
                ? "MCP Tool Gateway is running. No DotCraft bindings are active."
                : $"MCP Tool Gateway is running with {_summary.ThreadCount} bound thread(s).";
        }

        private string FormatBindings()
        {
            if (_summary.BindingCount <= 0)
                return "None";

            return $"{_summary.ThreadCount} thread(s), {_summary.ToolCount} tool(s)";
        }
    }
}

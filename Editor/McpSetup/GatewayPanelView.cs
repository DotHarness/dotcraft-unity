using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.McpSetup
{
    /// <summary>
    /// Shared UIToolkit primitives for the DotCraft gateway surfaces (MCP Setup window and
    /// the status-bar dropdown), styled by <c>GatewayPanel.uss</c>.
    /// </summary>
    internal static class GatewayPanelView
    {
        public static void ApplyStyle(VisualElement root)
        {
            var sheet = DotCraftResources.LoadStyleSheet("GatewayPanel");
            if (sheet != null && !root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);
        }

        public static VisualElement BrandHeader(string title, string subtitle)
        {
            var header = new VisualElement();
            header.AddToClassList("gw-header");

            var logo = DotCraftResources.LoadEditorTexture("DotCraftLogo");
            if (logo != null)
            {
                var logoElement = new VisualElement();
                logoElement.AddToClassList("gw-header-logo");
                logoElement.style.backgroundImage = new StyleBackground(logo);
                header.Add(logoElement);
            }

            var text = new VisualElement();
            text.AddToClassList("gw-header-text");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("gw-title");
            text.Add(titleLabel);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.AddToClassList("gw-subtitle");
                text.Add(subtitleLabel);
            }

            header.Add(text);
            return header;
        }

        public static VisualElement Card()
        {
            var card = new VisualElement();
            card.AddToClassList("gw-card");
            return card;
        }

        public static Label SectionLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("gw-section-label");
            return label;
        }

        public static VisualElement KeyValueRow(string label, string value, out Label valueLabel)
        {
            var row = new VisualElement();
            row.AddToClassList("gw-row");

            var key = new Label(label);
            key.AddToClassList("gw-row-label");
            row.Add(key);

            valueLabel = new Label(value);
            valueLabel.AddToClassList("gw-row-value");
            row.Add(valueLabel);
            return row;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("gw-btn");
            foreach (var cls in classes)
            {
                if (!string.IsNullOrEmpty(cls))
                    button.AddToClassList(cls);
            }

            return button;
        }

        public static Label Chip(string text, string variantClass)
        {
            var chip = new Label(text);
            chip.AddToClassList("gw-chip");
            if (!string.IsNullOrEmpty(variantClass))
                chip.AddToClassList(variantClass);
            return chip;
        }

        public static void SetChip(Label chip, string text, bool ok)
        {
            chip.text = text;
            chip.RemoveFromClassList("gw-chip--ok");
            chip.RemoveFromClassList("gw-chip--muted");
            chip.AddToClassList(ok ? "gw-chip--ok" : "gw-chip--muted");
        }

        public static VisualElement Banner(string variantClass, out Label textLabel)
        {
            var banner = new VisualElement();
            banner.AddToClassList("gw-banner");
            if (!string.IsNullOrEmpty(variantClass))
                banner.AddToClassList(variantClass);

            textLabel = new Label();
            textLabel.AddToClassList("gw-banner-text");
            banner.Add(textLabel);
            banner.style.display = DisplayStyle.None;
            return banner;
        }

        public static void SetBanner(VisualElement banner, Label textLabel, string message, string variantClass)
        {
            if (banner == null || textLabel == null)
                return;

            if (string.IsNullOrWhiteSpace(message))
            {
                banner.style.display = DisplayStyle.None;
                return;
            }

            banner.RemoveFromClassList("gw-banner--info");
            banner.RemoveFromClassList("gw-banner--warn");
            banner.RemoveFromClassList("gw-banner--error");
            if (!string.IsNullOrEmpty(variantClass))
                banner.AddToClassList(variantClass);

            textLabel.text = message;
            banner.style.display = DisplayStyle.Flex;
        }

        public static VisualElement Divider()
        {
            var divider = new VisualElement();
            divider.AddToClassList("gw-divider");
            return divider;
        }

        public static void CopyToClipboard(string value)
        {
            EditorGUIUtility.systemCopyBuffer = value ?? string.Empty;
        }
    }
}

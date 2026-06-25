using System;
using DotCraft.Editor.McpSetup;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.AppBinding
{
    internal static class UnityAppBindingStatusBarActions
    {
        internal static Action OpenAssistantOverride { get; set; }

        internal static Action OpenSettingsOverride { get; set; }

        internal static Action OpenSetupOverride { get; set; }

        internal static Action<string> CopyMcpUrlOverride { get; set; }

        internal static Action<Rect, UnityAppBindingStatusSummary> OpenStatusPopupOverride { get; set; }

        internal static void OpenStatusPopup(Rect activatorRect, UnityAppBindingStatusSummary summary)
        {
            if (OpenStatusPopupOverride != null)
            {
                OpenStatusPopupOverride(activatorRect, summary);
                return;
            }

            var screenRect = GUIUtility.GUIToScreenRect(activatorRect);
            UnityAppBindingStatusDropdown.Show(screenRect, summary ?? UnityAppBindingStatusSummary.Empty);
        }

        internal static void OpenAssistant()
        {
            if (OpenAssistantOverride != null)
            {
                OpenAssistantOverride();
                return;
            }

            global::DotCraft.Editor.Window.DotCraftEditorWindow.ShowWindow();
        }

        internal static void OpenSettings()
        {
            if (OpenSettingsOverride != null)
            {
                OpenSettingsOverride();
                return;
            }

            SettingsService.OpenProjectSettings("Project/DotCraft");
        }

        internal static void OpenSetup()
        {
            if (OpenSetupOverride != null)
            {
                OpenSetupOverride();
                return;
            }

            McpGatewaySetupWindow.ShowWindow();
        }

        internal static void CopyMcpUrl(string url)
        {
            if (CopyMcpUrlOverride != null)
            {
                CopyMcpUrlOverride(url);
                return;
            }

            EditorGUIUtility.systemCopyBuffer = url ?? string.Empty;
        }
    }
}

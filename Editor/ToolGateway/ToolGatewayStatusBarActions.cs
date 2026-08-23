using System;
using DotCraft.Editor.McpSetup;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.ToolGateway
{
    internal static class ToolGatewayStatusBarActions
    {
        internal static Action OpenAssistantOverride { get; set; }

        internal static Action OpenSettingsOverride { get; set; }

        internal static Action OpenSetupOverride { get; set; }

        internal static Action<Rect, ToolGatewayStatusSummary> OpenStatusPopupOverride { get; set; }

        internal static void OpenStatusPopup(Rect activatorRect, ToolGatewayStatusSummary summary)
        {
            if (OpenStatusPopupOverride != null)
            {
                OpenStatusPopupOverride(activatorRect, summary);
                return;
            }

            var screenRect = GUIUtility.GUIToScreenRect(activatorRect);
            ToolGatewayStatusDropdown.Show(screenRect, summary ?? ToolGatewayStatusSummary.Empty);
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
    }
}

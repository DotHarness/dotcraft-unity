using System;

namespace DotCraft.Editor.AppBinding
{
    internal static class UnityAppBindingStatusBarActions
    {
        internal static Action OpenAssistantOverride { get; set; }

        internal static void OpenAssistant()
        {
            if (OpenAssistantOverride != null)
            {
                OpenAssistantOverride();
                return;
            }

            global::DotCraft.Editor.Window.DotCraftEditorWindow.ShowWindow();
        }
    }
}

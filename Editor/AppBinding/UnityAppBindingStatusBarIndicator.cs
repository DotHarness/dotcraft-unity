using System;
using System.Reflection;
using DotCraft.Editor.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.AppBinding
{
    /// <summary>
    /// Injects a compact DotCraft App Binding indicator into Unity's bottom-right status bar.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityAppBindingStatusBarIndicator
    {
        internal const string IndicatorName = "dotcraft-app-binding-status-indicator";

        private const float DefaultRightOffset = 104f;
        private const float IndicatorWidth = 30f;
        private const float IndicatorHeight = 19f;
        private const float PeerSpacing = 4f;
        private const float MaxPeerWidth = 160f;
        private const float MaxStatusBarPeerHeight = 24f;
        private const float MaxStatusBarPeerTop = 3f;

        private static IMGUIContainer s_indicator;
        private static Texture2D s_logo;
        private static GUIStyle s_fallbackLabelStyle;
        private static UnityAppBindingStatusSummary s_summary = UnityAppBindingStatusSummary.Empty;
        private static bool s_updateRegistered;
        private static bool s_loggedReflectionFailure;
        private static bool s_retryScheduled;

        static UnityAppBindingStatusBarIndicator()
        {
            ScheduleInject();
        }

        private static void TryInject()
        {
            if (s_indicator != null && s_indicator.panel != null)
                return;

            var root = GetStatusBarVisualTree();
            if (root == null)
            {
                LogReflectionFailureOnce();
                ScheduleInject();
                return;
            }

            var existing = root.Q<IMGUIContainer>(IndicatorName);
            if (existing != null)
            {
                s_indicator = existing;
            }
            else
            {
                s_indicator = CreateIndicator(root);
            }

            if (!s_updateRegistered)
            {
                EditorApplication.update += UpdateState;
                s_updateRegistered = true;
            }

            UpdateState();
        }

        private static void ScheduleInject()
        {
            if (s_retryScheduled)
                return;

            s_retryScheduled = true;
            EditorApplication.delayCall += () =>
            {
                s_retryScheduled = false;
                TryInject();
            };
        }

        private static IMGUIContainer CreateIndicator(VisualElement root)
        {
            var container = new IMGUIContainer(DrawIndicator)
            {
                name = IndicatorName,
                pickingMode = PickingMode.Position
            };

            container.style.position = Position.Absolute;
            container.style.right = ResolveRightOffset(root, null);
            container.style.top = 0;
            container.style.height = IndicatorHeight;
            container.style.width = IndicatorWidth;

            root.Add(container);
            container.BringToFront();
            return container;
        }

        private static VisualElement GetStatusBarVisualTree()
        {
            try
            {
                var editorAssembly = typeof(UnityEditor.Editor).Assembly;
                var appStatusBarType = editorAssembly.GetType("UnityEditor.AppStatusBar");
                if (appStatusBarType == null)
                    return null;

                var instanceField = appStatusBarType.GetField("s_AppStatusBar", BindingFlags.Static | BindingFlags.NonPublic);
                var appStatusBar = instanceField?.GetValue(null);
                if (appStatusBar == null)
                    return null;

                var guiViewType = editorAssembly.GetType("UnityEditor.GUIView");
                var visualTreeProperty = guiViewType?.GetProperty(
                    "visualTree",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                return visualTreeProperty?.GetValue(appStatusBar) as VisualElement;
            }
            catch
            {
                return null;
            }
        }

        private static void UpdateState()
        {
            if (s_indicator == null || s_indicator.panel == null)
            {
                s_indicator = null;
                ScheduleInject();
                return;
            }

            s_indicator.style.right = ResolveRightOffset(s_indicator.parent, s_indicator);
            s_summary = UnityAppBindingStatusSummary.FromBindings(UnityAppBindingService.Instance.ActiveBindings);
            s_indicator.style.display = s_summary.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            s_indicator.tooltip = s_summary.Tooltip;
            s_indicator.MarkDirtyRepaint();
        }

        internal static float ResolveRightOffset(VisualElement root, VisualElement self)
        {
            if (root == null)
                return DefaultRightOffset;

            var offset = DefaultRightOffset;
            foreach (var child in root.Children())
            {
                if (child == null || ReferenceEquals(child, self))
                    continue;

                if (!TryGetStatusBarPeerBounds(child, out var right, out var width))
                    continue;

                offset = Mathf.Max(offset, right + width + PeerSpacing);
            }

            return offset;
        }

        private static bool TryGetStatusBarPeerBounds(VisualElement element, out float right, out float width)
        {
            right = 0;
            width = 0;

            if (element.style.display.value == DisplayStyle.None)
                return false;

            if (element.style.position.value != Position.Absolute)
                return false;

            if (!TryGetPixelLength(element.style.right, out right)
                || !TryGetPixelLength(element.style.width, out width))
            {
                return false;
            }

            if (width <= 0 || width > MaxPeerWidth)
                return false;

            if (TryGetPixelLength(element.style.height, out var height) && height > MaxStatusBarPeerHeight)
                return false;

            return !TryGetPixelLength(element.style.top, out var top) || top <= MaxStatusBarPeerTop;
        }

        private static bool TryGetPixelLength(StyleLength length, out float value)
        {
            value = 0;
            if (length.keyword != StyleKeyword.Undefined
                || length.value.unit != LengthUnit.Pixel)
            {
                return false;
            }

            value = length.value.value;
            return true;
        }

        private static void DrawIndicator()
        {
            if (!s_summary.IsVisible)
                return;

            var totalRect = new Rect(0, 0, IndicatorWidth, IndicatorHeight);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && totalRect.Contains(evt.mousePosition))
            {
                UnityAppBindingStatusBarActions.OpenAssistant();
                evt.Use();
            }

            var logo = GetLogo();
            var logoRect = new Rect(2, 1.5f, 16, 16);
            if (logo != null)
            {
                GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit, true);
            }
            else
            {
                DrawFallbackLogo(logoRect);
            }

            EditorGUI.DrawRect(new Rect(22f, 4f, 5f, 5f), new Color(0.3f, 0.85f, 0.4f));
        }

        private static Texture2D GetLogo()
        {
            if (s_logo == null)
                s_logo = DotCraftResources.LoadEditorTexture("DotCraftLogo");
            return s_logo;
        }

        private static void DrawFallbackLogo(Rect rect)
        {
            if (s_fallbackLabelStyle == null)
            {
                s_fallbackLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 9,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }

            EditorGUI.DrawRect(rect, new Color(0.12f, 0.35f, 0.9f, 0.95f));
            s_fallbackLabelStyle.normal.textColor = Color.white;
            GUI.Label(rect, "D", s_fallbackLabelStyle);
        }

        private static void LogReflectionFailureOnce()
        {
            if (s_loggedReflectionFailure || !DotCraftSettings.Instance.VerboseLogging)
                return;

            s_loggedReflectionFailure = true;
            Debug.Log("[DotCraft] App Binding status bar indicator could not find UnityEditor.AppStatusBar; continuing without the status bar logo.");
        }
    }
}

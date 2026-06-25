using System;
using System.Reflection;
using DotCraft.Editor.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotCraft.Editor.AppBinding
{
    /// <summary>
    /// Injects a compact DotCraft status indicator into Unity's bottom-right status bar area.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityAppBindingStatusBarIndicator
    {
        internal const string IndicatorName = "dotcraft-app-binding-status-indicator";

        private const float StatusBarRightOffset = 76f;
        private const float IndicatorWidth = 30f;
        private const float IndicatorHeight = 19f;
        private const float PeerSpacing = 4f;
        private const float MaxPeerWidth = 160f;
        private const float MaxComputedPeerRightOffset = StatusBarRightOffset + MaxPeerWidth;
        private const float MaxStatusBarPeerHeight = 24f;
        private const float MaxStatusBarPeerTop = 3f;
        private const float LogoSize = 16f;
        private const float LogoLeft = 2f;
        private const float StatusDotSize = 5f;
        private const float StatusDotRight = 6f;
        private const float StatusDotVerticalNudge = 2.5f;
        private const double InjectRetryIntervalSeconds = 1.0;
        private const long LayoutRefreshIntervalMilliseconds = 1000;

        private static IMGUIContainer s_indicator;
        private static VisualElement s_statusBarRoot;
        private static VisualElement s_configuredIndicator;
        private static IVisualElementScheduledItem s_layoutRefreshSchedule;
        private static Texture2D s_logo;
        private static GUIStyle s_fallbackLabelStyle;
        private static UnityAppBindingStatusSummary s_summary = UnityAppBindingStatusSummary.Empty;
        private static bool s_serviceEventsRegistered;
        private static bool s_loggedReflectionFailure;
        private static bool s_immediateInjectScheduled;
        private static bool s_retryRegistered;
        private static double s_nextRetryTime;

        static UnityAppBindingStatusBarIndicator()
        {
            ScheduleInject(immediate: true);
        }

        private static void TryInject()
        {
            EnsureServiceEventsRegistered();
            if (s_indicator != null && s_indicator.panel != null)
            {
                RefreshIndicator();
                return;
            }

            var root = GetStatusBarVisualTree();
            if (root == null)
            {
                LogReflectionFailureOnce();
                ScheduleInject();
                return;
            }

            CancelRetry();
            RegisterStatusBarRoot(root);
            var existing = root.Q<IMGUIContainer>(IndicatorName);
            if (existing != null)
            {
                s_indicator = existing;
            }
            else
            {
                s_indicator = CreateIndicator(root);
            }

            ConfigureIndicator(s_indicator);
            RefreshIndicator();
        }

        private static void ScheduleInject(bool immediate = false)
        {
            if (immediate)
            {
                if (s_immediateInjectScheduled)
                    return;

                s_immediateInjectScheduled = true;
                EditorApplication.delayCall += () =>
                {
                    s_immediateInjectScheduled = false;
                    TryInject();
                };
                return;
            }

            s_nextRetryTime = EditorApplication.timeSinceStartup + InjectRetryIntervalSeconds;
            if (s_retryRegistered)
                return;

            s_retryRegistered = true;
            EditorApplication.update += RetryInjectWhenDue;
        }

        private static void RetryInjectWhenDue()
        {
            if (EditorApplication.timeSinceStartup < s_nextRetryTime)
                return;

            CancelRetry();
            TryInject();
        }

        private static void CancelRetry()
        {
            if (!s_retryRegistered)
                return;

            EditorApplication.update -= RetryInjectWhenDue;
            s_retryRegistered = false;
        }

        private static void EnsureServiceEventsRegistered()
        {
            if (s_serviceEventsRegistered)
                return;

            UnityAppBindingService.Instance.StatusChanged += OnStatusChanged;
            s_serviceEventsRegistered = true;
        }

        private static void OnStatusChanged()
        {
            TryInject();
        }

        private static void RegisterStatusBarRoot(VisualElement root)
        {
            if (ReferenceEquals(s_statusBarRoot, root))
                return;

            if (s_statusBarRoot != null)
                s_statusBarRoot.UnregisterCallback<GeometryChangedEvent>(OnStatusBarGeometryChanged);

            s_statusBarRoot = root;
            s_statusBarRoot.RegisterCallback<GeometryChangedEvent>(OnStatusBarGeometryChanged);
        }

        private static void ConfigureIndicator(IMGUIContainer container)
        {
            if (ReferenceEquals(s_configuredIndicator, container))
                return;

            UnconfigureIndicator();
            s_configuredIndicator = container;
            container.RegisterCallback<AttachToPanelEvent>(OnIndicatorAttached);
            container.RegisterCallback<DetachFromPanelEvent>(OnIndicatorDetached);
            s_layoutRefreshSchedule = container.schedule.Execute(RefreshLayout).Every(LayoutRefreshIntervalMilliseconds);
        }

        private static void UnconfigureIndicator()
        {
            if (s_configuredIndicator != null)
            {
                s_configuredIndicator.UnregisterCallback<AttachToPanelEvent>(OnIndicatorAttached);
                s_configuredIndicator.UnregisterCallback<DetachFromPanelEvent>(OnIndicatorDetached);
            }

            s_layoutRefreshSchedule?.Pause();
            s_layoutRefreshSchedule = null;
            s_configuredIndicator = null;
        }

        private static void OnStatusBarGeometryChanged(GeometryChangedEvent evt)
        {
            RefreshLayout();
        }

        private static void OnIndicatorAttached(AttachToPanelEvent evt)
        {
            RefreshIndicator();
        }

        private static void OnIndicatorDetached(DetachFromPanelEvent evt)
        {
            if (!ReferenceEquals(evt.target, s_indicator))
                return;

            UnconfigureIndicator();
            s_indicator = null;
            ScheduleInject();
        }

        private static IMGUIContainer CreateIndicator(VisualElement root)
        {
            var container = new IMGUIContainer(DrawIndicator)
            {
                name = IndicatorName,
                pickingMode = PickingMode.Position
            };

            container.style.position = Position.Absolute;
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

        private static void RefreshIndicator()
        {
            if (s_indicator == null || s_indicator.panel == null)
            {
                s_indicator = null;
                ScheduleInject();
                return;
            }

            RefreshLayout();
            var service = UnityAppBindingService.Instance;
            s_summary = UnityAppBindingStatusSummary.FromState(
                service.IsLocalServerRunning,
                service.LocalServerUrl,
                service.LastError,
                service.ActiveBindings);
            s_indicator.style.display = s_summary.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            s_indicator.tooltip = s_summary.Tooltip;
            s_indicator.MarkDirtyRepaint();
        }

        private static void RefreshLayout()
        {
            if (s_indicator == null || s_indicator.panel == null)
                return;

            s_indicator.style.right = ResolveRightOffset(s_indicator.parent, s_indicator);
            s_indicator.BringToFront();
        }

        internal static float ResolveRightOffset(VisualElement root, VisualElement self)
        {
            if (root == null)
                return StatusBarRightOffset;

            var offset = StatusBarRightOffset;
            foreach (var child in root.Children())
            {
                if (child == null || ReferenceEquals(child, self))
                    continue;

                if (!TryGetStatusBarPeerBounds(root, child, out var right, out var width))
                    continue;

                offset = Mathf.Max(offset, right + width + PeerSpacing);
            }

            return ClampRightOffset(root, offset);
        }

        private static bool TryGetStatusBarPeerBounds(
            VisualElement root,
            VisualElement element,
            out float right,
            out float width)
        {
            right = 0;
            width = 0;

            if (element.style.display.value == DisplayStyle.None
                || element.resolvedStyle.display == DisplayStyle.None)
                return false;

            if (element.style.position.value != Position.Absolute
                && element.resolvedStyle.position != Position.Absolute)
                return false;

            if (!TryGetComputedStatusBarPeerBounds(root, element, out right, out width)
                && !TryGetStyledStatusBarPeerBounds(element, out right, out width))
                return false;

            if (width <= 0 || width > MaxPeerWidth)
                return false;

            return true;
        }

        private static bool TryGetComputedStatusBarPeerBounds(
            VisualElement root,
            VisualElement element,
            out float right,
            out float width)
        {
            right = 0;
            width = 0;

            var rootWidth = ResolveElementWidth(root);
            if (rootWidth <= 0)
                return false;

            var layout = element.layout;
            if (!IsUsableLength(layout.width) || !IsFiniteLength(layout.x))
                return false;

            if (IsUsableLength(layout.height) && layout.height > MaxStatusBarPeerHeight)
                return false;

            if (IsUsableLength(layout.y) && layout.y > MaxStatusBarPeerTop)
                return false;

            width = layout.width;
            right = rootWidth - layout.xMax;
            var maxComputedRightOffset = Mathf.Max(MaxComputedPeerRightOffset, rootWidth * 0.5f);
            if (!IsFiniteLength(right) || right < 0 || right > maxComputedRightOffset)
                return false;

            right = Mathf.Max(0, right);
            return true;
        }

        private static bool TryGetStyledStatusBarPeerBounds(VisualElement element, out float right, out float width)
        {
            right = 0;
            width = 0;

            if (!TryGetPixelLength(element.style.right, out right)
                || !TryGetPixelLength(element.style.width, out width))
                return false;

            if (TryGetPixelLength(element.style.height, out var height) && height > MaxStatusBarPeerHeight)
                return false;

            return !TryGetPixelLength(element.style.top, out var top) || top <= MaxStatusBarPeerTop;
        }

        private static float ClampRightOffset(VisualElement root, float offset)
        {
            var rootWidth = ResolveElementWidth(root);
            if (rootWidth <= 0)
                return offset;

            var maxOffset = Mathf.Max(0, rootWidth - IndicatorWidth);
            return Mathf.Clamp(offset, 0, maxOffset);
        }

        private static float ResolveElementWidth(VisualElement element)
        {
            if (element == null)
                return 0;

            if (IsUsableLength(element.layout.width))
                return element.layout.width;

            if (IsUsableLength(element.resolvedStyle.width))
                return element.resolvedStyle.width;

            return TryGetPixelLength(element.style.width, out var width) ? width : 0;
        }

        private static bool IsUsableLength(float value)
        {
            return value > 0 && IsFiniteLength(value);
        }

        private static bool IsFiniteLength(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
                UnityAppBindingStatusBarActions.OpenStatusPopup(totalRect, s_summary);
                evt.Use();
            }

            var logo = GetLogo();
            var logoTop = (IndicatorHeight - LogoSize) * 0.5f;
            var logoRect = new Rect(LogoLeft, logoTop, LogoSize, LogoSize);
            if (logo != null)
            {
                GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit, true);
            }
            else
            {
                DrawFallbackLogo(logoRect);
            }

            var statusDotLeft = IndicatorWidth - StatusDotRight - StatusDotSize;
            var statusDotTop = logoTop + StatusDotVerticalNudge;
            EditorGUI.DrawRect(
                new Rect(statusDotLeft, statusDotTop, StatusDotSize, StatusDotSize),
                new Color(0.3f, 0.85f, 0.4f));
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

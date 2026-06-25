using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.AppBinding
{
    internal sealed class UnityAppBindingStatusPopup : PopupWindowContent
    {
        private const float Width = 360f;
        private const float Height = 196f;
        private const float AboveActivatorGap = 6f;

        private readonly UnityAppBindingStatusSummary _summary;
        private static GUIStyle s_captionStyle;
        private static GUIStyle s_urlStyle;
        private static GUIStyle s_statusStyle;

        public UnityAppBindingStatusPopup(UnityAppBindingStatusSummary summary)
        {
            _summary = summary ?? UnityAppBindingStatusSummary.Empty;
        }

        internal static Rect ResolveStatusBarActivatorRect(Rect activatorRect, UnityAppBindingStatusSummary summary)
        {
            var height = ResolveHeight(summary ?? UnityAppBindingStatusSummary.Empty);
            return new Rect(
                activatorRect.x,
                activatorRect.y - height - AboveActivatorGap,
                activatorRect.width,
                activatorRect.height);
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(Width, ResolveHeight(_summary));
        }

        public override void OnGUI(Rect rect)
        {
            EnsureStyles();

            EditorGUILayout.Space(10);
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("DotCraft Unity", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(BuildCaption(), s_captionStyle);
            }

            DrawSeparator();
            DrawRow("Local Server", _summary.IsLocalServerRunning ? "Running" : "Stopped", s_statusStyle);
            DrawRow("Active Bindings", FormatBindings(), EditorStyles.miniLabel);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("MCP Endpoint", EditorStyles.miniBoldLabel);
            if (string.IsNullOrWhiteSpace(_summary.GatewayMcpUrl))
            {
                EditorGUILayout.LabelField("Unavailable", EditorStyles.miniLabel);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(_summary.GatewayMcpUrl, s_urlStyle, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Copy", GUILayout.Width(64)))
                    {
                        UnityAppBindingStatusBarActions.CopyMcpUrl(_summary.GatewayMcpUrl);
                        editorWindow?.Close();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(_summary.LastError))
            {
                EditorGUILayout.HelpBox(_summary.LastError, MessageType.Warning);
            }

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Assistant", GUILayout.Height(24)))
                {
                    editorWindow?.Close();
                    UnityAppBindingStatusBarActions.OpenAssistant();
                }

                if (GUILayout.Button("Setup MCP", GUILayout.Width(96), GUILayout.Height(24)))
                {
                    editorWindow?.Close();
                    UnityAppBindingStatusBarActions.OpenSetup();
                }

                if (GUILayout.Button("Settings", GUILayout.Width(96), GUILayout.Height(24)))
                {
                    editorWindow?.Close();
                    UnityAppBindingStatusBarActions.OpenSettings();
                }
            }
        }

        private static void EnsureStyles()
        {
            s_captionStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true
            };

            s_urlStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                clipping = TextClipping.Clip
            };

            s_statusStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold
            };
        }

        private static float ResolveHeight(UnityAppBindingStatusSummary summary)
        {
            return string.IsNullOrWhiteSpace(summary.LastError) ? Height : Height + 44f;
        }

        private void DrawRow(string label, string value, GUIStyle valueStyle)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(110));
                EditorGUILayout.LabelField(value, valueStyle);
            }
        }

        private static void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            rect.y += 4f;
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.12f)
                : new Color(0f, 0f, 0f, 0.18f));
            EditorGUILayout.Space(8);
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

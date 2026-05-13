using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UObject = UnityEngine.Object;

namespace DotCraft.Editor.UI
{
    /// <summary>
    /// Common interface for chat items.
    /// </summary>
    public interface IChatItem
    {
        VisualElement Element { get; }
    }

    /// <summary>
    /// Manages the chat message list UI.
    /// </summary>
    public sealed class ChatPanel
    {
        private const string LegacyReasoningPrefix = "\ud83d\udcad\n";

        private readonly ScrollView _container;
        private readonly List<IChatItem> _messages = new();
        private readonly Dictionary<object, bool> _foldoutStates = new();
        private int _currentTurnStartIndex = -1;
        private DateTime _currentTurnStartedAt;

        // Current message being built (for chunk merging)
        private MessageItem _currentAgentMessage;
        private ReasoningItem _currentThought;

        // Active tool calls keyed by toolCallId for concurrent/interleaved tool tracking.
        // _lastToolCall acts as a fallback when the agent omits toolCallId.
        private readonly Dictionary<string, ToolCallItem> _activeToolCalls = new();
        private ToolCallItem _lastToolCall;

        // Welcome panel and typing indicator references
        private VisualElement _welcomePanel;
        private VisualElement _typingIndicator;
        private VisualElement _reasoningSpinner;
        private Label _reasoningLabel;

        public ChatPanel(ScrollView container)
        {
            _container = container;
        }

        /// <summary>
        /// Sets the welcome panel element. It is shown when the message list is empty
        /// and hidden as soon as the first message is added.
        /// </summary>
        public void SetWelcomePanel(VisualElement panel)
        {
            _welcomePanel = panel;
            UpdateWelcomePanelVisibility();
        }

        /// <summary>
        /// Sets the typing indicator element. Shown during agent streaming, hidden otherwise.
        /// </summary>
        public void SetTypingIndicator(VisualElement indicator)
        {
            _typingIndicator = indicator;
        }

        // Typing animation state
        private IVisualElementScheduledItem _typingAnimSchedule;
        private readonly Stopwatch _typingStopwatch = new();
        private PlanItem _currentPlan;

        /// <summary>
        /// Shows or hides the typing indicator. When shown, animates the three dots
        /// in a wave pattern using a scheduled callback (USS lacks keyframe animations).
        /// </summary>
        public void ShowTypingIndicator(bool show)
        {
            if (_typingIndicator == null) return;

            EnsureTypingIndicatorContent();
            _typingIndicator.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

            if (show)
            {
                _typingStopwatch.Restart();
                _typingAnimSchedule ??= _typingIndicator.schedule
                    .Execute(AnimateTypingIndicator)
                    .Every(250);
                _typingAnimSchedule.Resume();
                AnimateTypingIndicator();
            }
            else
            {
                _typingStopwatch.Stop();
                _typingAnimSchedule?.Pause();
            }
        }

        /// <summary>
        /// Updates the reasoning spinner and elapsed time while the agent is running.
        /// </summary>
        private void AnimateTypingIndicator()
        {
            if (_typingIndicator == null) return;

            if (_reasoningLabel != null)
            {
                var elapsed = _typingStopwatch.Elapsed;
                _reasoningLabel.text = elapsed.TotalSeconds < 1
                    ? "Reasoning..."
                    : $"Reasoning... ({FormatElapsed(elapsed)})";
            }
        }

        private void EnsureTypingIndicatorContent()
        {
            if (_reasoningLabel != null && _reasoningSpinner != null)
                return;

            _reasoningSpinner = _typingIndicator.Q<VisualElement>("reasoning-spinner");
            _reasoningLabel = _typingIndicator.Q<Label>("reasoning-label");

            if (_reasoningSpinner == null || _reasoningLabel == null)
            {
                _typingIndicator.Clear();
                _reasoningSpinner = UIHelper.CreateElement("reasoning-spinner");
                _reasoningSpinner.name = "reasoning-spinner";
                _reasoningLabel = UIHelper.CreateLabel("Reasoning...", "reasoning-label");
                _reasoningLabel.name = "reasoning-label";
                _typingIndicator.Add(_reasoningSpinner);
                _typingIndicator.Add(_reasoningLabel);
            }
        }

        internal static string FormatElapsed(TimeSpan elapsed)
        {
            return elapsed.TotalSeconds < 60
                ? $"{Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalSeconds))}s"
                : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        }

        /// <summary>
        /// Inserts an inline approval card into the chat flow instead of using a floating overlay.
        /// </summary>
        public void HandleApprovalRequest(RequestPermissionParams request, Action<RequestPermissionResult> callback)
        {
            FinalizeCurrentAgentMessage();
            FinalizeCurrentThought();

            var approvalItem = new ApprovalItem(request, callback);
            _messages.Add(approvalItem);
            _container.Add(approvalItem.Element);
        }

        private void UpdateWelcomePanelVisibility()
        {
            if (_welcomePanel != null)
                _welcomePanel.style.display = _messages.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HideWelcomePanel()
        {
            if (_welcomePanel != null)
                _welcomePanel.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Handles a session update notification.
        /// </summary>
        public void HandleSessionUpdate(AcpSessionUpdate update)
        {
            switch (update.SessionUpdate)
            {
                case AcpUpdateKind.AgentMessageChunk:
                    HandleAgentMessageChunk(update);
                    break;

                case AcpUpdateKind.UserMessageChunk:
                    HandleUserMessageChunk(update);
                    break;

                case AcpUpdateKind.AgentThoughtChunk:
                    HandleAgentThoughtChunk(update);
                    break;

                case AcpUpdateKind.ToolCall:
                    HandleToolCall(update);
                    break;

                case AcpUpdateKind.ToolCallUpdate:
                    HandleToolCallUpdate(update);
                    break;

                case AcpUpdateKind.Plan:
                    HandlePlan(update);
                    break;

                case AcpUpdateKind.AvailableCommandsUpdate:
                    // Handle available commands update if needed
                    break;

                case AcpUpdateKind.ConfigOptionsUpdate:
                case AcpUpdateKind.LegacyConfigOptionsUpdate:
                    // Handle config options update if needed
                    break;

                case AcpUpdateKind.CurrentModeUpdate:
                    // Handle mode update if needed
                    break;
            }

            // Hide welcome panel whenever any content arrives (covers history replay on reconnect)
            if (_messages.Count > 0)
                HideWelcomePanel();
        }

        /// <summary>
        /// Adds a user message to the chat.
        /// </summary>
        public void AddUserMessage(string text, List<UnityEngine.Object> attachments = null)
        {
            BeginNewTurn();
            _currentPlan = null;
            // Do NOT clear _activeToolCalls here: late-arriving tool_call_update completions
            // from the previous turn should still find and update their cards correctly.

            HideWelcomePanel();

            var item = new MessageItem(MessageType.User);
            item.SetContent(text);
            _messages.Add(item);
            _container.Add(item.Element);

            // Add attachment indicators
            if (attachments != null)
            {
                foreach (var asset in attachments)
                {
                    var attachmentItem = new AttachmentChipItem(asset);
                    attachmentItem.Element.AddToClassList("sent-attachment");
                    _messages.Add(attachmentItem);
                    _container.Add(attachmentItem.Element);
                }
            }
        }

        private void HandleAgentMessageChunk(AcpSessionUpdate update)
        {
            var text = GetContentText(update.Content);
            if (TryHandleLegacyReasoningMessageChunk(text))
                return;

            // Merge with previous message if same type
            if (_currentAgentMessage != null)
            {
                _currentAgentMessage.AppendText(text);
            }
            else
            {
                _currentAgentMessage = new MessageItem(MessageType.Agent);
                _messages.Add(_currentAgentMessage);
                _container.Add(_currentAgentMessage.Element);
                // Use AppendText so the first chunk enters streaming mode (plain-text label).
                // SetContent would call RenderFull on incomplete markdown and can cause an
                // infinite loop in the renderer (e.g. a bare "##" token from the LLM).
                _currentAgentMessage.AppendText(text);
            }
        }

        private void HandleUserMessageChunk(AcpSessionUpdate update)
        {
            BeginNewTurn();
            _currentPlan = null;

            var text = GetContentText(update.Content);
            var item = new MessageItem(MessageType.User);
            item.SetContent(text);
            _messages.Add(item);
            _container.Add(item.Element);
        }

        private void HandleAgentThoughtChunk(AcpSessionUpdate update)
        {
            var text = GetContentText(update.Content);
            AppendThoughtText(text);
        }

        private bool TryHandleLegacyReasoningMessageChunk(string text)
        {
            if (string.IsNullOrEmpty(text) ||
                !text.StartsWith(LegacyReasoningPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var reasoningText = text.Substring(LegacyReasoningPrefix.Length);
            if (!string.IsNullOrEmpty(reasoningText))
                AppendThoughtText(reasoningText);

            return true;
        }

        private void AppendThoughtText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            ShowTypingIndicator(false);

            if (_currentThought != null)
            {
                _currentThought.AppendText(text);
            }
            else
            {
                _currentThought = new ReasoningItem();
                _messages.Add(_currentThought);
                _container.Add(_currentThought.Element);
                _currentThought.AppendText(text);
            }
        }

        private void HandleToolCall(AcpSessionUpdate update)
        {
            // Finalize any in-progress streamed messages before showing a tool call
            // (FinalizeCurrentAgentMessage/Thought also nulls the references)
            FinalizeCurrentAgentMessage();
            FinalizeCurrentThought();

            var toolCall = new ToolCallItem();
            toolCall.SetTitle(update.Title ?? update.ToolCallId);
            toolCall.SetKind(update.Kind ?? "other");
            toolCall.SetStatus(update.Status ?? AcpToolStatus.Pending);

            if (update.Content != null)
            {
                try
                {
                    var contentJson = DotCraftJson.SerializeIndented(update.Content);
                    toolCall.SetOutput(contentJson);
                }
                catch { }
            }

            // Set file locations if provided
            if (update.FileLocations != null && update.FileLocations.Count > 0)
                toolCall.SetFileLocations(update.FileLocations);

            // Register in the per-id map so concurrent tool calls can each be updated correctly.
            if (!string.IsNullOrEmpty(update.ToolCallId))
                _activeToolCalls[update.ToolCallId] = toolCall;

            _lastToolCall = toolCall;
            _messages.Add(toolCall);
            _container.Add(toolCall.Element);
        }

        private void HandleToolCallUpdate(AcpSessionUpdate update)
        {
            // Resolve target card: prefer explicit toolCallId lookup, fall back to last card.
            ToolCallItem target = null;
            if (!string.IsNullOrEmpty(update.ToolCallId))
            {
                _activeToolCalls.TryGetValue(update.ToolCallId, out target);
                if (target == null)
                {
                    // Update arrived for an id we no longer track (already completed, or history
                    // replay edge case). Log in verbose mode and skip silently.
                    if (DotCraftSettings.Instance.VerboseLogging)
                        UnityEngine.Debug.Log($"[DotCraft] tool_call_update for unknown id '{update.ToolCallId}' — skipped.");
                    return;
                }
            }
            else
            {
                // Agent did not supply toolCallId; fall back to the most-recently-added card.
                target = _lastToolCall;
            }

            if (target == null) return;

            if (!string.IsNullOrEmpty(update.Title))
                target.SetTitle(update.Title);

            if (!string.IsNullOrEmpty(update.Status))
            {
                target.SetStatus(update.Status);

                // Remove from the active map once terminal so future updates for the same id
                // do not accidentally land on a recycled/new card in a later turn.
                if (!string.IsNullOrEmpty(update.ToolCallId) &&
                    (update.Status == AcpToolStatus.Completed || update.Status == AcpToolStatus.Failed))
                {
                    _activeToolCalls.Remove(update.ToolCallId);
                    if (_lastToolCall == target)
                        _lastToolCall = null;
                }
            }

            if (update.Content != null)
            {
                try
                {
                    var contentJson = DotCraftJson.SerializeIndented(update.Content);
                    target.AppendOutput(contentJson);
                }
                catch { }
            }

            // Update file locations if provided
            if (update.FileLocations != null && update.FileLocations.Count > 0)
                target.SetFileLocations(update.FileLocations);
        }

        private void HandlePlan(AcpSessionUpdate update)
        {
            FinalizeCurrentAgentMessage();
            FinalizeCurrentThought();

            if (_currentPlan == null || _currentPlan.IsTerminal)
            {
                _currentPlan = new PlanItem();
                _messages.Add(_currentPlan);
                _container.Add(_currentPlan.Element);
            }

            _currentPlan.SetEntries(update.Entries);
        }

        private string GetContentText(object content)
        {
            if (content == null) return "";

            if (content is JToken json)
            {
                if (json.Type == JTokenType.Object)
                {
                    var textProp = json["text"];
                    if (textProp != null)
                    {
                        return textProp.ToObject<string>() ?? "";
                    }
                }

                return json.Type == JTokenType.String
                    ? json.ToObject<string>() ?? ""
                    : json.ToString(Formatting.None);
            }

            return content.ToString();
        }

        /// <summary>
        /// Clears all messages.
        /// </summary>
        public void Clear()
        {
            _container.Clear();
            _messages.Clear();
            _currentAgentMessage = null;
            _currentThought = null;
            _currentPlan = null;
            _currentTurnStartIndex = -1;
            _activeToolCalls.Clear();
            _lastToolCall = null;
            ShowTypingIndicator(false);
            UpdateWelcomePanelVisibility();
        }

        /// <summary>
        /// Finalizes any in-progress streaming messages. Call this when the prompt turn ends
        /// (stop_reason: end_turn) so the last agent message gets full markdown rendering.
        /// </summary>
        public void FinalizeStreaming()
        {
            FinalizeCurrentAgentMessage();
            FinalizeCurrentThought();
            ShowTypingIndicator(false);
            CompleteCurrentTurn();
        }

        private void BeginNewTurn()
        {
            FinalizeCurrentAgentMessage();
            FinalizeCurrentThought();
            CompleteCurrentTurn();

            _currentTurnStartIndex = _messages.Count;
            _currentTurnStartedAt = DateTime.Now;
        }

        private void CompleteCurrentTurn()
        {
            if (_currentTurnStartIndex < 0 || _currentTurnStartIndex >= _messages.Count)
            {
                _currentTurnStartIndex = -1;
                return;
            }

            AggregateCompletedToolRuns(_currentTurnStartIndex);
            FoldCompletedTurn(_currentTurnStartIndex, DateTime.Now - _currentTurnStartedAt);
            _currentTurnStartIndex = -1;
        }

        private void AggregateCompletedToolRuns(int startIndex)
        {
            var i = Mathf.Max(0, startIndex);
            while (i < _messages.Count)
            {
                var firstTool = _messages[i] as ToolCallItem;
                if (firstTool == null || !firstTool.IsTerminal)
                {
                    i++;
                    continue;
                }

                var run = new List<ToolCallItem> { firstTool };
                var j = i + 1;
                while (j < _messages.Count)
                {
                    var nextTool = _messages[j] as ToolCallItem;
                    if (nextTool == null || !nextTool.IsTerminal)
                        break;

                    run.Add(nextTool);
                    j++;
                }

                if (run.Count > 1)
                {
                    ReplaceToolRunWithGroup(i, run);
                }

                i++;
            }
        }

        private void ReplaceToolRunWithGroup(int messageIndex, List<ToolCallItem> run)
        {
            var content = _container.contentContainer;
            var visualIndex = content.IndexOf(run[0].Element);
            if (visualIndex < 0)
                visualIndex = content.childCount;

            foreach (var item in run)
                item.Element.RemoveFromHierarchy();

            var group = new ToolCallGroupItem(run);
            _messages.RemoveRange(messageIndex, run.Count);
            _messages.Insert(messageIndex, group);
            content.Insert(visualIndex, group.Element);
        }

        private void FoldCompletedTurn(int startIndex, TimeSpan elapsed)
        {
            var endIndex = _messages.Count;
            var lastAgentIndex = -1;
            var pinnedPlanIndex = -1;

            for (var i = endIndex - 1; i >= startIndex; i--)
            {
                if (lastAgentIndex < 0)
                {
                    var message = _messages[i] as MessageItem;
                    if (message != null && message.Type == MessageType.Agent)
                        lastAgentIndex = i;
                }

                if (pinnedPlanIndex < 0 && _messages[i] is PlanItem)
                    pinnedPlanIndex = i;

                if (lastAgentIndex >= 0 && pinnedPlanIndex >= 0)
                    break;
            }

            if (lastAgentIndex < 0)
                return;

            var foldedItems = new List<IChatItem>();
            for (var i = startIndex; i < endIndex; i++)
            {
                if (i == lastAgentIndex || i == pinnedPlanIndex)
                    continue;

                if (IsUserChatItem(_messages[i]))
                    continue;

                if (_messages[i] is TurnCollapsedSummaryItem)
                    continue;

                foldedItems.Add(_messages[i]);
            }

            if (foldedItems.Count == 0)
                return;

            var firstFoldedIndex = _messages.IndexOf(foldedItems[0]);
            var content = _container.contentContainer;
            var visualIndex = content.IndexOf(foldedItems[0].Element);
            if (visualIndex < 0)
                visualIndex = content.childCount;

            foreach (var item in foldedItems)
            {
                item.Element.RemoveFromHierarchy();
                _messages.Remove(item);
            }

            var summary = new TurnCollapsedSummaryItem(elapsed, foldedItems);
            firstFoldedIndex = Mathf.Clamp(firstFoldedIndex, 0, _messages.Count);
            _messages.Insert(firstFoldedIndex, summary);
            visualIndex = Mathf.Clamp(visualIndex, 0, content.childCount);
            content.Insert(visualIndex, summary.Element);
        }

        private void FinalizeCurrentAgentMessage()
        {
            _currentAgentMessage?.FinalizeContent();
            _currentAgentMessage = null;
        }

        private void FinalizeCurrentThought()
        {
            if (_currentThought == null)
                return;

            var keepVisible = _currentThought.FinalizeContent();
            if (!keepVisible)
            {
                _container.Remove(_currentThought.Element);
                _messages.Remove(_currentThought);
            }
            _currentThought = null;
        }

        private static bool IsUserChatItem(IChatItem item)
        {
            var message = item as MessageItem;
            return (message != null && message.Type == MessageType.User)
                || item is AttachmentChipItem;
        }
    }

    public enum MessageType
    {
        User,
        Agent,
        Thinking,
        ToolCall,
        Plan
    }

    internal static class ChatPanelText
    {
        public static string ToolGroupSummary(IReadOnlyList<ToolCallItem> items)
        {
            var counts = items
                .GroupBy(item => item.DisplayTitle)
                .Select(group => $"{group.Key} {group.Count()} {(group.Count() == 1 ? "time" : "times")}");

            return $"Called {string.Join(", ", counts)}";
        }

        public static string ProcessedIn(TimeSpan elapsed)
        {
            return $"Processed in {ChatPanel.FormatElapsed(elapsed)}";
        }
    }

    /// <summary>
    /// Lightweight reasoning row for agent thought chunks.
    /// Reasoning content is hidden unless the user opts in through DotCraft settings.
    /// </summary>
    public sealed class ReasoningItem : IChatItem
    {
        private readonly VisualElement _element;
        private readonly VisualElement _contentContainer;
        private readonly Label _summaryLabel;
        private readonly Label _contentLabel;
        private readonly Button _toggle;
        private readonly StringBuilder _content = new();
        private readonly Stopwatch _stopwatch = new();

        private bool _expanded;
        private bool _isStreaming = true;
        private TimeSpan _elapsed;

        public VisualElement Element => _element;

        public ReasoningItem()
        {
            _element = UIHelper.CreateElement("reasoning-item", "collapsed");

            var header = UIHelper.CreateElement("reasoning-header");
            _toggle = UIHelper.CreateButton("▶", "reasoning-toggle");
            _toggle.clicked += () =>
            {
                if (CanExpand)
                    SetExpanded(!_expanded);
            };
            header.Add(_toggle);

            _summaryLabel = UIHelper.CreateLabel("Thinking...", "reasoning-summary");
            header.Add(_summaryLabel);
            _element.Add(header);

            _contentContainer = UIHelper.CreateElement("reasoning-content-container");
            _contentLabel = UIHelper.CreateLabel("", "reasoning-content");
            _contentLabel.style.whiteSpace = WhiteSpace.Normal;
            _contentContainer.Add(_contentLabel);
            _element.Add(_contentContainer);

            SetExpanded(false);
            Refresh();
        }

        public void AppendText(string text)
        {
            if (!_stopwatch.IsRunning && _isStreaming)
                _stopwatch.Start();

            _content.Append(text);
            _contentLabel.text = _content.ToString();
            Refresh();
        }

        public bool FinalizeContent()
        {
            if (!_isStreaming)
                return DotCraftSettings.Instance.ShowThinkingContent;

            _isStreaming = false;
            if (_stopwatch.IsRunning)
                _stopwatch.Stop();
            _elapsed = _stopwatch.Elapsed;
            Refresh();
            return DotCraftSettings.Instance.ShowThinkingContent;
        }

        private bool CanExpand =>
            DotCraftSettings.Instance.ShowThinkingContent && _content.Length > 0;

        private void SetExpanded(bool expanded)
        {
            _expanded = expanded && CanExpand;
            _contentContainer.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _toggle.text = _expanded ? "▼" : "▶";

            if (_expanded)
                _element.RemoveFromClassList("collapsed");
            else
                _element.AddToClassList("collapsed");
        }

        private void Refresh()
        {
            _summaryLabel.text = _isStreaming
                ? "Thinking..."
                : $"Thought {ChatPanel.FormatElapsed(_elapsed)}";

            _toggle.style.display = CanExpand ? DisplayStyle.Flex : DisplayStyle.None;
            if (!CanExpand && _expanded)
                SetExpanded(false);
        }
    }

    /// <summary>
    /// Represents a single message in the chat.
    /// </summary>
    public sealed class MessageItem : IChatItem
    {
        private readonly VisualElement _element;
        private readonly VisualElement _contentContainer;
        private readonly MessageType _type;
        private readonly StringBuilder _content = new();

        private const int StreamingMarkdownRenderIntervalMs = 200;
        private const int LongStreamingMarkdownRenderIntervalMs = 300;
        private const int LongStreamingMarkdownThreshold = 24000;

        // Streaming state: Agent messages use throttled markdown rendering while
        // other message types keep the cheap plain-text streaming path.
        private bool _isStreaming;
        private Label _streamingLabel;
        private bool _streamingMarkdownDirty;
        private int _contentRevision;
        private int _lastRenderedRevision = -1;
        private IVisualElementScheduledItem _streamingMarkdownSchedule;
        private int _streamingMarkdownIntervalMs;

        // Collapse state for thinking messages
        private bool _collapsed;
        private Button _collapseToggle;

        public VisualElement Element => _element;
        public MessageType Type => _type;

        public MessageItem(MessageType type)
        {
            _type = type;
            _element = UIHelper.CreateElement("message-item");

            switch (type)
            {
                case MessageType.User:
                    _element.AddToClassList("user");
                    break;
                case MessageType.Agent:
                    _element.AddToClassList("agent");
                    break;
                case MessageType.Thinking:
                    _element.AddToClassList("thinking");
                    break;
            }

            // Message body container (header + content)
            var body = UIHelper.CreateElement("message-body");

            var header = UIHelper.CreateElement("message-header");

            var sender = type switch
            {
                MessageType.User => "You",
                MessageType.Agent => "DotCraft",
                MessageType.Thinking => "Thinking",
                _ => "Unknown"
            };
            header.Add(UIHelper.CreateLabel(sender, "message-sender"));
            header.Add(UIHelper.CreateLabel(DateTime.Now.ToString("HH:mm"), "message-time"));

            // Collapse toggle for thinking messages
            if (type == MessageType.Thinking)
            {
                _collapseToggle = UIHelper.CreateButton("▼", "message-collapse-toggle");
                _collapseToggle.style.display = DisplayStyle.Flex;
                _collapseToggle.clicked += () => SetCollapsed(!_collapsed);
                header.Add(_collapseToggle);
            }

            body.Add(header);

            _contentContainer = UIHelper.CreateElement("message-content-container");
            body.Add(_contentContainer);

            _element.Add(body);

            if (type == MessageType.Thinking)
                SetCollapsed(true);
        }

        /// <summary>
        /// Toggles collapsed state for thinking messages.
        /// </summary>
        public void SetCollapsed(bool collapsed)
        {
            _collapsed = collapsed;
            if (_contentContainer != null)
                _contentContainer.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_collapseToggle != null)
                _collapseToggle.text = collapsed ? "▶" : "▼";
            if (collapsed)
                _element.AddToClassList("collapsed");
            else
                _element.RemoveFromClassList("collapsed");
        }

        public void SetContent(string text)
        {
            _content.Clear();
            _content.Append(text);
            _isStreaming = false;
            _streamingLabel = null;
            StopStreamingMarkdownSchedule();
            _contentRevision++;
            RenderFull();
        }

        /// <summary>
        /// Appends a streaming chunk. Agent messages are markdown-rendered with a
        /// throttled scheduler; other message types use a plain-text fast path.
        /// </summary>
        public void AppendText(string text)
        {
            _content.Append(text);
            _contentRevision++;

            if (_type == MessageType.Agent)
            {
                AppendAgentStreamingText();
                return;
            }

            if (_isStreaming)
            {
                // Fast path: update the streaming label text directly (no DOM rebuild)
                if (_streamingLabel != null)
                    _streamingLabel.text = _content.ToString();
                return;
            }

            // First appended chunk: enter streaming mode — create the plain-text label once
            _isStreaming = true;
            _contentContainer.Clear();
            _streamingLabel = UIHelper.CreateLabel(_content.ToString(), "message-content");
            _streamingLabel.style.whiteSpace = WhiteSpace.Normal;
            _contentContainer.Add(_streamingLabel);
        }

        /// <summary>
        /// Called when streaming is done. Performs one final full render for agent messages.
        /// </summary>
        public void FinalizeContent()
        {
            if (!_isStreaming) return;
            _isStreaming = false;
            _streamingLabel = null;
            StopStreamingMarkdownSchedule();
            RenderFull();
        }

        private void AppendAgentStreamingText()
        {
            if (!_isStreaming)
            {
                _isStreaming = true;
                RenderStreamingMarkdownNow();
                EnsureStreamingMarkdownSchedule();
                return;
            }

            _streamingMarkdownDirty = true;
            EnsureStreamingMarkdownSchedule();
        }

        private void EnsureStreamingMarkdownSchedule()
        {
            var interval = _content.Length > LongStreamingMarkdownThreshold
                ? LongStreamingMarkdownRenderIntervalMs
                : StreamingMarkdownRenderIntervalMs;

            if (_streamingMarkdownSchedule == null || _streamingMarkdownIntervalMs != interval)
            {
                _streamingMarkdownSchedule?.Pause();
                _streamingMarkdownSchedule = _element.schedule
                    .Execute(RenderStreamingMarkdownIfDirty)
                    .Every(interval);
                _streamingMarkdownIntervalMs = interval;
            }

            _streamingMarkdownSchedule.Resume();
        }

        private void StopStreamingMarkdownSchedule()
        {
            _streamingMarkdownSchedule?.Pause();
            _streamingMarkdownDirty = false;
        }

        private void RenderStreamingMarkdownIfDirty()
        {
            if (!_isStreaming || _type != MessageType.Agent)
            {
                StopStreamingMarkdownSchedule();
                return;
            }

            if (_streamingMarkdownDirty)
                RenderStreamingMarkdownNow();

            if (!_streamingMarkdownDirty)
                _streamingMarkdownSchedule?.Pause();
        }

        private void RenderStreamingMarkdownNow()
        {
            if (_lastRenderedRevision == _contentRevision)
                return;

            MarkdownRenderer.RenderTo(_contentContainer, _content.ToString());
            _lastRenderedRevision = _contentRevision;
            _streamingMarkdownDirty = false;
        }

        private void RenderFull()
        {
            var text = _content.ToString();

            if (_type == MessageType.Agent)
            {
                MarkdownRenderer.RenderTo(_contentContainer, text);
                _lastRenderedRevision = _contentRevision;
            }
            else
            {
                _contentContainer.Clear();
                var label = UIHelper.CreateLabel(text, "message-content");
                label.style.whiteSpace = WhiteSpace.Normal;
                _contentContainer.Add(label);
            }
        }
    }

    internal static class AttachmentIconResolver
    {
        public static Texture ResolveIcon(UObject asset, out bool isPreview)
        {
            isPreview = false;
            if (asset == null)
                return null;

            var preview = GetPreview(asset);
            if (preview != null)
            {
                isPreview = true;
                return preview;
            }

            var thumbnail = AssetPreview.GetMiniThumbnail(asset);
            if (thumbnail != null)
                return thumbnail;

            var content = EditorGUIUtility.ObjectContent(asset, asset.GetType());
            return content?.image;
        }

        public static Texture2D GetPreview(UObject asset)
        {
            return asset != null ? AssetPreview.GetAssetPreview(asset) : null;
        }

        public static bool IsPreviewLoading(UObject asset)
        {
            return asset != null && AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID());
        }
    }

    internal sealed class AttachmentChipItem : IChatItem
    {
        private const int PreviewRetryIntervalMs = 150;
        private const int MaxPreviewRetryCount = 20;

        private readonly UObject _asset;
        private readonly Image _icon;
        private IVisualElementScheduledItem _previewSchedule;
        private int _previewRetryCount;

        public VisualElement Element { get; }

        public AttachmentChipItem(UObject asset, Action onRemove = null)
        {
            _asset = asset;
            Element = UIHelper.CreateElement("attachment-item");
            Element.tooltip = BuildTooltip(asset);

            _icon = new Image
            {
                scaleMode = ScaleMode.ScaleAndCrop,
                pickingMode = PickingMode.Ignore
            };
            _icon.AddToClassList("attachment-icon");
            Element.Add(_icon);

            var name = asset != null ? asset.name : "Attachment";
            Element.Add(UIHelper.CreateLabel(name, "attachment-name"));

            if (onRemove != null)
            {
                var removeBtn = UIHelper.CreateButton("×", "remove-attachment");
                removeBtn.clicked += onRemove;
                Element.Add(removeBtn);
            }

            Element.RegisterCallback<DetachFromPanelEvent>(_ => StopPreviewPolling());
            RefreshIcon();
            StartPreviewPollingIfNeeded();
        }

        private static string BuildTooltip(UObject asset)
        {
            if (asset == null)
                return "Attachment";

            var assetPath = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(assetPath)
                ? asset.name
                : $"{asset.name}\n{assetPath}";
        }

        private void RefreshIcon()
        {
            var texture = AttachmentIconResolver.ResolveIcon(_asset, out var isPreview);
            if (texture == null)
                return;

            _icon.image = texture;
            _icon.EnableInClassList("image-preview", isPreview);
            _icon.EnableInClassList("type-icon", !isPreview);
        }

        private void StartPreviewPollingIfNeeded()
        {
            if (!AttachmentIconResolver.IsPreviewLoading(_asset))
                return;

            _previewRetryCount = 0;
            _previewSchedule = Element.schedule
                .Execute(PollPreview)
                .Every(PreviewRetryIntervalMs);
            _previewSchedule.Resume();
        }

        private void PollPreview()
        {
            if (_asset == null)
            {
                StopPreviewPolling();
                return;
            }

            var preview = AttachmentIconResolver.GetPreview(_asset);
            if (preview != null)
            {
                _icon.image = preview;
                _icon.EnableInClassList("image-preview", true);
                _icon.EnableInClassList("type-icon", false);
                StopPreviewPolling();
                return;
            }

            _previewRetryCount++;
            if (_previewRetryCount >= MaxPreviewRetryCount || !AttachmentIconResolver.IsPreviewLoading(_asset))
                StopPreviewPolling();
        }

        private void StopPreviewPolling()
        {
            _previewSchedule?.Pause();
            _previewSchedule = null;
        }
    }

    /// <summary>
    /// Collapsed summary for a completed run of consecutive tool calls.
    /// </summary>
    public sealed class ToolCallGroupItem : IChatItem
    {
        private readonly VisualElement _element;
        private readonly VisualElement _body;
        private readonly Button _toggle;
        private bool _expanded;

        public VisualElement Element => _element;

        public ToolCallGroupItem(IReadOnlyList<ToolCallItem> items)
        {
            _element = UIHelper.CreateElement("tool-call-group");

            var header = UIHelper.CreateElement("tool-call-group-header");
            UIHelper.SetPointerCursor(header);

            _toggle = UIHelper.CreateButton("▶", "tool-call-group-toggle");
            header.Add(_toggle);
            header.Add(UIHelper.CreateLabel(BuildSummary(items), "tool-call-group-title"));

            _element.Add(header);

            _body = UIHelper.CreateElement("tool-call-group-body");
            foreach (var item in items)
                _body.Add(item.Element);

            _element.Add(_body);

            header.RegisterCallback<ClickEvent>(_ => SetExpanded(!_expanded));
            SetExpanded(false);
        }

        private static string BuildSummary(IReadOnlyList<ToolCallItem> items)
        {
            return ChatPanelText.ToolGroupSummary(items);
        }

        private void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            _body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _toggle.text = expanded ? "▼" : "▶";
        }
    }

    /// <summary>
    /// Collapses intermediate work after a turn finishes, keeping the final response visible.
    /// </summary>
    public sealed class TurnCollapsedSummaryItem : IChatItem
    {
        private readonly VisualElement _element;
        private readonly VisualElement _body;
        private readonly Button _toggle;
        private bool _expanded;

        public VisualElement Element => _element;

        public TurnCollapsedSummaryItem(TimeSpan elapsed, IReadOnlyList<IChatItem> items)
        {
            _element = UIHelper.CreateElement("turn-collapsed-summary");

            var header = UIHelper.CreateElement("turn-collapsed-header");
            UIHelper.SetPointerCursor(header);

            _toggle = UIHelper.CreateButton("▶", "turn-collapsed-toggle");
            header.Add(_toggle);
            header.Add(UIHelper.CreateLabel(ChatPanelText.ProcessedIn(elapsed), "turn-collapsed-title"));

            _element.Add(header);

            _body = UIHelper.CreateElement("turn-collapsed-body");
            foreach (var item in items)
                _body.Add(item.Element);

            _element.Add(_body);

            header.RegisterCallback<ClickEvent>(_ => SetExpanded(!_expanded));
            SetExpanded(false);
        }

        private void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            _body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _toggle.text = expanded ? "▼" : "▶";
        }
    }

    /// <summary>
    /// Represents a tool call in the chat.
    /// </summary>
    public sealed class ToolCallItem : IChatItem
    {
        private readonly VisualElement _element;
        private readonly VisualElement _iconElement;
        private readonly Label _iconLabel;
        private readonly Label _titleLabel;
        private readonly Label _outputLabel;
        private readonly StringBuilder _output = new();
        private bool _expanded;

        // New: status indicator, duration timer, file locations
        private readonly Label _durationLabel;
        private readonly VisualElement _filesContainer;
        private readonly Stopwatch _stopwatch = new();
        private IVisualElementScheduledItem _runningSchedule;
        private string _status = AcpToolStatus.Pending;
        private string _title = "";

        public VisualElement Element => _element;
        public string DisplayTitle => string.IsNullOrWhiteSpace(_title) ? "tool" : _title;
        public bool IsTerminal => _status == AcpToolStatus.Completed || _status == AcpToolStatus.Failed;

        public ToolCallItem()
        {
            _element = UIHelper.CreateElement("tool-call");

            // Header (clickable to expand)
            var header = UIHelper.CreateElement("tool-call-header");
            UIHelper.SetPointerCursor(header);

            _titleLabel = UIHelper.CreateLabel("", "tool-call-title");

            _iconElement = UIHelper.CreateElement("tool-call-status-icon");

            // Symbol label inside the colored icon square (fallback when no editor icon found)
            _iconLabel = new Label();
            _iconLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _iconLabel.style.fontSize = 10;
            _iconLabel.style.color = Color.white;
            _iconLabel.style.flexGrow = 1;
            _iconElement.Add(_iconLabel);

            // Duration label
            _durationLabel = UIHelper.CreateLabel("", "tool-call-duration");

            // Spacer to push duration and status to the right
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;

            header.Add(_iconElement);
            header.Add(_titleLabel);
            header.Add(spacer);
            header.Add(_durationLabel);
            _element.Add(header);

            // Body (expandable)
            var body = UIHelper.CreateElement("tool-call-body");
            _outputLabel = UIHelper.CreateLabel("", "tool-call-code");
#if UNITY_6000_3_OR_NEWER
            _outputLabel.style.whiteSpace = WhiteSpace.PreWrap;
#else
            _outputLabel.style.whiteSpace = WhiteSpace.Normal;
#endif
            var outputContainer = UIHelper.CreateElement("tool-call-output");
            outputContainer.Add(UIHelper.CreateLabel("Output", "tool-call-label"));
            outputContainer.Add(_outputLabel);
            body.Add(outputContainer);

            // File locations container (hidden by default)
            _filesContainer = UIHelper.CreateElement("tool-call-files");
            _filesContainer.style.display = DisplayStyle.None;
            body.Add(_filesContainer);

            _element.Add(body);

            // Click to expand
            header.RegisterCallback<ClickEvent>(evt =>
            {
                _expanded = !_expanded;
                body.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            });

            _element.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (_status == AcpToolStatus.InProgress)
                    StartRunningAnimation();
            });

            _element.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _runningSchedule?.Pause();
            });

            // Stopwatch is started only when status transitions to in_progress
            // (avoids misleading "0.0s" for history-replayed tool calls)
        }

        public void SetTitle(string title)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "tool" : title;
            _titleLabel.text = _title;
        }

        public void SetKind(string kind)
        {
            // Remove all kind classes
            _iconElement.RemoveFromClassList("kind-read");
            _iconElement.RemoveFromClassList("kind-edit");
            _iconElement.RemoveFromClassList("kind-delete");
            _iconElement.RemoveFromClassList("kind-move");
            _iconElement.RemoveFromClassList("kind-search");
            _iconElement.RemoveFromClassList("kind-execute");
            _iconElement.RemoveFromClassList("kind-think");
            _iconElement.RemoveFromClassList("kind-fetch");
            _iconElement.RemoveFromClassList("kind-unity");
            _iconElement.RemoveFromClassList("kind-other");

            // Add specific kind class and set color
            var color = kind switch
            {
                AcpToolKind.Read => new Color(0.129f, 0.588f, 0.953f),      // #2196F3 Blue
                AcpToolKind.Edit => new Color(1.0f, 0.596f, 0.0f),          // #FF9800 Orange
                AcpToolKind.Delete => new Color(0.957f, 0.263f, 0.212f),    // #F44336 Red
                AcpToolKind.Move => new Color(0.612f, 0.153f, 0.690f),      // #9C27B0 Purple
                AcpToolKind.Search => new Color(0.0f, 0.737f, 0.831f),      // #00BCD4 Cyan
                AcpToolKind.Execute => new Color(0.298f, 0.686f, 0.314f),   // #4CAF50 Green
                AcpToolKind.Think => new Color(0.620f, 0.620f, 0.620f),     // #9E9E9E Gray
                AcpToolKind.Fetch => new Color(0.247f, 0.318f, 0.710f),     // #3F51B5 Indigo
                AcpToolKind.Unity => new Color(0.855f, 0.271f, 0.098f),     // #DA4519 Unity Orange
                _ => new Color(0.4f, 0.4f, 0.4f)                            // #666666 Default
            };

            var kindClass = $"kind-{kind ?? "other"}";
            _iconElement.AddToClassList(kindClass);
            _iconElement.style.backgroundColor = color;

            // Try to use a Unity built-in editor icon for the kind.
            // Falls back to a Unicode symbol when the icon texture is not found.
            var iconName = kind switch
            {
                AcpToolKind.Read    => "d_TextAsset Icon",
                AcpToolKind.Edit    => "d_editicon.sml",
                AcpToolKind.Delete  => "d_TreeEditor.Trash",
                AcpToolKind.Move    => "d_MoveTool",
                AcpToolKind.Search  => "d_ViewToolZoom",
                AcpToolKind.Execute => "d_PlayButton",
                AcpToolKind.Think   => "d_UnityEditor.ConsoleWindow",
                AcpToolKind.Fetch   => "d_CloudConnect",
                AcpToolKind.Unity   => "d_UnityLogo",
                _                   => null
            };

            var iconTexture = !string.IsNullOrEmpty(iconName)
                ? EditorGUIUtility.FindTexture(iconName)
                : null;

            if (iconTexture != null)
            {
                // Use the editor icon as a background image with white tint
                _iconLabel.text = "";
                _iconElement.style.backgroundImage = new StyleBackground(iconTexture);
                _iconElement.style.unityBackgroundImageTintColor = Color.white;
#if UNITY_2022_1_OR_NEWER
                _iconElement.style.backgroundSize = new BackgroundSize(14, 14);
#endif
                _iconElement.style.alignItems = Align.Center;
                _iconElement.style.justifyContent = Justify.Center;
            }
            else
            {
                // Fallback to Unicode symbol
                _iconElement.style.backgroundImage = StyleKeyword.None;
                _iconLabel.text = kind switch
                {
                    AcpToolKind.Read    => "≡",   // ≡  horizontal lines (document)
                    AcpToolKind.Edit    => "✎",   // ✎  pencil
                    AcpToolKind.Delete  => "✕",   // ✕  cross
                    AcpToolKind.Move    => "⇄",   // ⇄  left-right arrows
                    AcpToolKind.Search  => "⌕",   // ⌕  magnifier
                    AcpToolKind.Execute => "▶",   // ▶  play
                    AcpToolKind.Think   => "⚡",   // ⚡  lightning
                    AcpToolKind.Fetch   => "↓",   // ↓  down arrow
                    AcpToolKind.Unity   => "▲",   // ▲  Unity triangle
                    _                   => "●"    // ●  circle
                };
            }
        }

        public void SetStatus(string status)
        {
            _status = string.IsNullOrEmpty(status) ? AcpToolStatus.Pending : status;
            _iconElement.RemoveFromClassList("status-pending");
            _iconElement.RemoveFromClassList("status-running");
            _iconElement.RemoveFromClassList("status-completed");
            _iconElement.RemoveFromClassList("status-failed");

            var isRunning = _status == AcpToolStatus.InProgress;

            if (_status == AcpToolStatus.InProgress)
            {
                _iconElement.AddToClassList("status-running");
                _iconElement.style.backgroundImage = StyleKeyword.None;
                _iconElement.style.backgroundColor = Color.clear;
                _iconLabel.style.color = new Color(0.78f, 0.78f, 0.78f);
                _iconLabel.text = "";
            }
            else if (_status == AcpToolStatus.Completed)
            {
                _iconElement.AddToClassList("status-completed");
                _iconElement.style.backgroundImage = StyleKeyword.None;
                _iconElement.style.backgroundColor = Color.clear;
                _iconLabel.style.color = new Color(0.45f, 0.74f, 0.49f);
                _iconLabel.text = "✓";
            }
            else if (_status == AcpToolStatus.Failed)
            {
                _iconElement.AddToClassList("status-failed");
                _iconElement.style.backgroundImage = StyleKeyword.None;
                _iconElement.style.backgroundColor = Color.clear;
                _iconLabel.style.color = new Color(0.93f, 0.33f, 0.31f);
                _iconLabel.text = "✕";
            }
            else
            {
                _iconElement.AddToClassList("status-pending");
            }

            // Start stopwatch when tool begins running
            if (isRunning)
            {
                StartRunningAnimation();
            }

            // Update duration on completion (hide if stopwatch never ran, e.g. history replay)
            if (_status == AcpToolStatus.Completed || _status == AcpToolStatus.Failed)
            {
                _stopwatch.Stop();
                _runningSchedule?.Pause();
                var elapsed = _stopwatch.Elapsed;
                if (elapsed.TotalMilliseconds > 100)
                {
                    _durationLabel.text = elapsed.TotalSeconds < 60
                        ? $"{elapsed.TotalSeconds:F1}s"
                        : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
                }
            }
        }

        private void StartRunningAnimation()
        {
            if (!_stopwatch.IsRunning)
                _stopwatch.Start();

            if (_element.panel == null)
                return;

            _runningSchedule ??= _element.schedule.Execute(UpdateRunningVisuals).Every(250);
            _runningSchedule.Resume();
            UpdateRunningVisuals();
        }

        private void UpdateRunningVisuals()
        {
            if (_stopwatch.IsRunning)
                _durationLabel.text = ChatPanel.FormatElapsed(_stopwatch.Elapsed);
        }

        /// <summary>
        /// Renders clickable file location links that open files in Unity Editor.
        /// </summary>
        public void SetFileLocations(List<AcpFileLocation> locations)
        {
            if (locations == null || locations.Count == 0)
            {
                _filesContainer.style.display = DisplayStyle.None;
                return;
            }

            _filesContainer.Clear();
            _filesContainer.style.display = DisplayStyle.Flex;

            foreach (var loc in locations)
            {
                var link = UIHelper.CreateLabel(loc.Uri, "tool-call-file-link");
                link.style.whiteSpace = WhiteSpace.NoWrap;
                UIHelper.SetPointerCursor(link);
                var uri = loc.Uri;
                link.RegisterCallback<ClickEvent>(evt =>
                {
                    // Try to open the file in Unity Editor
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(uri);
                    if (asset != null)
                        AssetDatabase.OpenAsset(asset);
                    else
                        EditorUtility.RevealInFinder(uri);
                });
                _filesContainer.Add(link);
            }
        }

        public void SetOutput(string text)
        {
            _output.Clear();
            _output.Append(text);
            UpdateOutput();
        }

        public void AppendOutput(string text)
        {
            _output.AppendLine();
            _output.Append(text);
            UpdateOutput();
        }

        private void UpdateOutput()
        {
            _outputLabel.text = _output.ToString();
        }
    }

    /// <summary>
    /// Represents a plan in the chat.
    /// </summary>
    public sealed class PlanItem : IChatItem
    {
        private readonly VisualElement _element;
        private readonly VisualElement _entriesContainer;

        public VisualElement Element => _element;
        public bool IsTerminal { get; private set; }

        public PlanItem()
        {
            _element = UIHelper.CreateElement("plan-container");
            var header = UIHelper.CreateElement("plan-header");
            header.Add(UIHelper.CreateLabel("Implementation Plan", "plan-title"));
            header.Add(UIHelper.CreateLabel("Live plan", "plan-badge"));
            _element.Add(header);

            _entriesContainer = UIHelper.CreateElement("plan-entries");
            _element.Add(_entriesContainer);
        }

        public void SetEntries(List<AcpPlanEntry> entries)
        {
            if (entries == null) return;

            _entriesContainer.Clear();
            IsTerminal = entries.Count > 0 && entries.All(entry =>
                entry.Status == AcpToolStatus.Completed || entry.Status == AcpToolStatus.Failed);

            foreach (var entry in entries)
            {
                var entryElement = UIHelper.CreateElement("plan-entry");

                // Status icon instead of colored bullet
                var statusIcon = UIHelper.CreateLabel("", "plan-status-icon");
                var status = entry.Status ?? AcpToolStatus.Pending;
                statusIcon.AddToClassList(status);

                statusIcon.text = status switch
                {
                    AcpToolStatus.Pending => "○",      // ○ empty circle
                    AcpToolStatus.InProgress => "◑",    // ◑ half circle
                    AcpToolStatus.Completed => "✓",     // ✓ checkmark
                    AcpToolStatus.Failed => "✕",        // ✕ cross
                    _ => "○"
                };

                var textLabel = UIHelper.CreateLabel(entry.Content, "plan-text");

                // Strikethrough style for completed entries
                if (status == AcpToolStatus.Completed)
                    textLabel.AddToClassList("completed");

                entryElement.Add(statusIcon);
                entryElement.Add(textLabel);

                _entriesContainer.Add(entryElement);
            }
        }
    }

    /// <summary>
    /// UIElements-based Markdown renderer for chat messages.
    /// Supports headings, code blocks, lists, blockquotes, bold/italic, inline code.
    /// </summary>
    internal static class MarkdownRenderer
    {
        /// <summary>
        /// Renders markdown text into a container VisualElement.
        /// </summary>
        public static void RenderTo(VisualElement container, string markdown)
        {
            container.Clear();

            if (string.IsNullOrEmpty(markdown))
                return;

            var lines = markdown.Split('\n');
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i];

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                // Code block (```)
                if (line.TrimStart().StartsWith("```"))
                {
                    var codeBlock = ParseCodeBlock(lines, ref i);
                    container.Add(codeBlock);
                    continue;
                }

                // Horizontal rule
                if (Regex.IsMatch(line.Trim(), @"^(\*\*\*+|---+|___+)$"))
                {
                    var hr = UIHelper.CreateElement("md-hr");
                    hr.style.height = 1;
                    hr.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    hr.style.marginTop = 4;
                    hr.style.marginBottom = 4;
                    container.Add(hr);
                    i++;
                    continue;
                }

                // Headings
                var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
                if (headingMatch.Success)
                {
                    var level = headingMatch.Groups[1].Length;
                    var content = headingMatch.Groups[2].Value;
                    var heading = CreateInlineLabel(content, "md-heading", $"md-h{level}");
                    heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                    heading.style.fontSize = level switch
                    {
                        1 => 22,
                        2 => 18,
                        3 => 15,
                        4 => 13,
                        _ => 12
                    };
                    heading.style.marginTop = 6;
                    heading.style.marginBottom = 4;
                    container.Add(heading);
                    i++;
                    continue;
                }

                // Ordered list
                var orderedListMatch = Regex.Match(line, @"^(\s*)(\d+)\.\s+(.+)$");
                if (orderedListMatch.Success)
                {
                    var indent = orderedListMatch.Groups[1].Length / 2;
                    var num = orderedListMatch.Groups[2].Value;
                    var content = orderedListMatch.Groups[3].Value;
                    var listItem = CreateListItem($"{num}. ", content, indent);
                    container.Add(listItem);
                    i++;
                    continue;
                }

                // Unordered list
                var listMatch = Regex.Match(line, @"^(\s*)([-*+])\s+(.+)$");
                if (listMatch.Success)
                {
                    var indent = listMatch.Groups[1].Length / 2;
                    var content = listMatch.Groups[3].Value;
                    var listItem = CreateListItem("• ", content, indent);
                    container.Add(listItem);
                    i++;
                    continue;
                }

                // Block quote
                if (line.TrimStart().StartsWith(">"))
                {
                    var content = line.TrimStart().Substring(1).TrimStart();
                    var quote = CreateInlineLabel(content, "md-blockquote");
                    quote.style.borderLeftWidth = 3;
                    quote.style.borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                    quote.style.paddingLeft = 8;
                    quote.style.marginTop = 2;
                    quote.style.marginBottom = 2;
                    quote.style.color = new Color(0.7f, 0.7f, 0.7f);
                    container.Add(quote);
                    i++;
                    continue;
                }

                // Table (lines starting with |)
                if (line.TrimStart().StartsWith("|"))
                {
                    var tableElement = ParseTable(lines, ref i);
                    container.Add(tableElement);
                    continue;
                }

                // Regular paragraph
                var paragraphText = ParseParagraph(lines, ref i);
                if (!string.IsNullOrWhiteSpace(paragraphText))
                {
                    var paragraph = CreateInlineLabel(paragraphText, "md-paragraph");
                    paragraph.style.marginTop = 2;
                    paragraph.style.marginBottom = 2;
                    container.Add(paragraph);
                }
            }
        }

        private static VisualElement ParseCodeBlock(string[] lines, ref int index)
        {
            var startLine = lines[index];
            var languageMatch = Regex.Match(startLine.TrimStart(), @"```(\w*)");
            var language = languageMatch.Groups[1].Value;

            index++; // Move past opening ```

            var codeLines = new List<string>();
            while (index < lines.Length)
            {
                if (lines[index].TrimStart().StartsWith("```"))
                {
                    index++; // Move past closing ```
                    break;
                }
                codeLines.Add(lines[index]);
                index++;
            }

            var codeText = string.Join("\n", codeLines);

            // Outer container wrapping header + code
            var codeBlockContainer = UIHelper.CreateElement("code-block-container");
            codeBlockContainer.style.marginTop = 4;
            codeBlockContainer.style.marginBottom = 4;

            // Header bar with language label and copy button
            var headerBar = UIHelper.CreateElement("code-block-header");
            headerBar.style.flexDirection = FlexDirection.Row;
            headerBar.style.justifyContent = Justify.SpaceBetween;
            headerBar.style.alignItems = Align.Center;
            headerBar.style.backgroundColor = new Color(0, 0, 0, 0.3f);
            headerBar.style.borderTopLeftRadius = 4;
            headerBar.style.borderTopRightRadius = 4;
            headerBar.style.paddingTop = 3;
            headerBar.style.paddingBottom = 3;
            headerBar.style.paddingLeft = 8;
            headerBar.style.paddingRight = 4;

            var langLabel = UIHelper.CreateLabel(
                string.IsNullOrEmpty(language) ? "code" : language, "code-block-language");
            langLabel.style.fontSize = 10;
            langLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            headerBar.Add(langLabel);

            var copyButton = UIHelper.CreateButton("Copy", "code-block-copy-button");
            var capturedCode = codeText;
            copyButton.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = capturedCode;
                copyButton.text = "Copied!";
                // Reset text after a short delay via EditorApplication
                EditorApplication.delayCall += () => copyButton.text = "Copy";
            };
            headerBar.Add(copyButton);

            codeBlockContainer.Add(headerBar);

            // Code content area
            var codeContainer = UIHelper.CreateElement("md-code-block");
            codeContainer.style.backgroundColor = new Color(0, 0, 0, 0.2f);
            codeContainer.style.borderBottomLeftRadius = 4;
            codeContainer.style.borderBottomRightRadius = 4;
            codeContainer.style.paddingTop = 6;
            codeContainer.style.paddingBottom = 6;
            codeContainer.style.paddingLeft = 8;
            codeContainer.style.paddingRight = 8;

            var codeLabel = UIHelper.CreateLabel(codeText, "md-code-text");
#if UNITY_6000_3_OR_NEWER
            codeLabel.style.whiteSpace = WhiteSpace.PreWrap;
#else
            codeLabel.style.whiteSpace = WhiteSpace.Normal;
#endif
            codeLabel.style.fontSize = 11;
            codeContainer.Add(codeLabel);

            codeBlockContainer.Add(codeContainer);

            return codeBlockContainer;
        }

        private static VisualElement ParseTable(string[] lines, ref int index)
        {
            // Collect all consecutive | lines
            var tableLines = new List<string>();
            while (index < lines.Length && lines[index].TrimStart().StartsWith("|"))
            {
                tableLines.Add(lines[index]);
                index++;
            }

            var table = UIHelper.CreateElement("md-table");
            table.style.marginTop = 4;
            table.style.marginBottom = 4;

            bool firstDataRow = true;
            foreach (var tableLine in tableLines)
            {
                // Skip separator rows (e.g. |---|---|)
                var trimmed = tableLine.Trim();
                var stripped = trimmed.Trim('|');
                if (Regex.IsMatch(stripped, @"^[\s\-|:]+$"))
                    continue;

                // Split cells: trim leading/trailing | then split on |
                var cells = stripped.Split('|');

                var row = UIHelper.CreateElement("md-table-row");
                row.style.flexDirection = FlexDirection.Row;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);

                if (firstDataRow)
                {
                    row.AddToClassList("md-table-header");
                    row.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                }

                foreach (var cell in cells)
                {
                    var cellText = cell.Trim();
                    var cellLabel = CreateInlineLabel(cellText, "md-table-cell");
                    cellLabel.style.flexGrow = 1;
                    cellLabel.style.flexShrink = 1;
                    cellLabel.style.flexBasis = 0;
                    cellLabel.style.paddingTop = 3;
                    cellLabel.style.paddingBottom = 3;
                    cellLabel.style.paddingLeft = 6;
                    cellLabel.style.paddingRight = 6;

                    if (firstDataRow)
                        cellLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

                    row.Add(cellLabel);
                }

                table.Add(row);
                firstDataRow = false;
            }

            return table;
        }

        private static string ParseParagraph(string[] lines, ref int index)
        {
            var paragraphLines = new List<string>();

            while (index < lines.Length)
            {
                var line = lines[index];

                if (string.IsNullOrWhiteSpace(line))
                {
                    index++;
                    break;
                }

                // Stop at special syntax (including table lines).
                // Use a regex for headings so that bare tokens like "##" (no trailing space)
                // are NOT treated as heading boundaries — they fall through as plain text and
                // prevent an infinite loop where the main RenderTo loop never advances past them.
                if (Regex.IsMatch(line.TrimStart(), @"^#{1,6}\s") ||
                    line.TrimStart().StartsWith("```") ||
                    line.TrimStart().StartsWith(">") ||
                    line.TrimStart().StartsWith("|") ||
                    Regex.IsMatch(line.Trim(), @"^[-*+]\s") ||
                    Regex.IsMatch(line.Trim(), @"^\d+\.\s") ||
                    Regex.IsMatch(line.Trim(), @"^(\*\*\*+|---+|___+)$"))
                {
                    break;
                }

                paragraphLines.Add(line);
                index++;
            }

            return string.Join(" ", paragraphLines);
        }

        private static VisualElement CreateListItem(string bullet, string content, int indent)
        {
            var row = UIHelper.CreateElement("md-list-item");
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginLeft = indent * 16;
            row.style.marginTop = 1;
            row.style.marginBottom = 1;

            var bulletLabel = UIHelper.CreateLabel(bullet, "md-bullet");
            bulletLabel.style.flexShrink = 0;
            row.Add(bulletLabel);

            var contentLabel = CreateInlineLabel(content, "md-list-content");
            contentLabel.style.flexGrow = 1;
            contentLabel.style.flexShrink = 1;
            row.Add(contentLabel);

            return row;
        }

        /// <summary>
        /// Creates a label with inline markdown processed (bold, italic, inline code, links).
        /// Uses Unity's rich text tags.
        /// </summary>
        private static Label CreateInlineLabel(string text, params string[] classNames)
        {
            var processed = ProcessInlineMarkdown(text);
            var label = UIHelper.CreateLabel(processed, classNames);
            label.enableRichText = true;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>
        /// Processes inline markdown: bold, italic, inline code, strikethrough, links.
        /// Converts to Unity rich text tags.
        /// </summary>
        private static string ProcessInlineMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Process inline code first (to avoid processing markdown inside code)
            text = Regex.Replace(text, @"`([^`]+)`", match =>
            {
                var code = match.Groups[1].Value;
                return $"<color=#D4D4D4><b>{EscapeRichText(code)}</b></color>";
            });

            // Bold and Italic (***text*** or ___text___)
            text = Regex.Replace(text, @"(\*\*\*|___)(.+?)\1", "<b><i>$2</i></b>");

            // Bold (**text** or __text__)
            text = Regex.Replace(text, @"(\*\*|__)(.+?)\1", "<b>$2</b>");

            // Italic (*text* or _text_)
            text = Regex.Replace(text, @"(\*|_)(.+?)\1", "<i>$2</i>");

            // Strikethrough (~~text~~)
            text = Regex.Replace(text, @"~~(.+?)~~", "<s>$1</s>");

            // Links [text](url) - show the text
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");

            return text;
        }

        private static string EscapeRichText(string text)
        {
            return text.Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }

    /// <summary>
    /// Renders a permission approval request as an inline chat card,
    /// replacing the floating overlay approval-panel.
    /// </summary>
    public sealed class ApprovalItem : IChatItem
    {
        public VisualElement Element { get; }

        public ApprovalItem(RequestPermissionParams request, Action<RequestPermissionResult> callback)
        {
            Element = UIHelper.CreateElement("approval-inline");

            // Header with shield icon and tool call title
            var header = UIHelper.CreateElement("approval-inline-header");
            header.Add(UIHelper.CreateLabel("⚠", "approval-inline-icon")); // ⚠ warning icon
            header.Add(UIHelper.CreateLabel(
                request.ToolCall?.Title ?? "Permission Request", "approval-inline-title"));
            if (!string.IsNullOrEmpty(request.ToolCall?.Kind))
            {
                header.Add(UIHelper.CreateLabel($"({request.ToolCall.Kind})", "approval-inline-kind"));
            }
            Element.Add(header);

            // Description
            var desc = UIHelper.CreateLabel(
                $"Tool '{request.ToolCall?.ToolCallId}' is requesting permission.",
                "approval-inline-description");
            desc.style.whiteSpace = WhiteSpace.Normal;
            Element.Add(desc);

            // Buttons row
            var buttonsRow = UIHelper.CreateElement("approval-inline-buttons");

            foreach (var option in request.Options ?? new List<PermissionOption>())
            {
                var btn = UIHelper.CreateButton(option.Name, "approval-inline-button");

                // Style based on kind
                if (option.Kind == AcpPermissionKind.AllowOnce)
                    btn.AddToClassList("allow");
                else if (option.Kind == AcpPermissionKind.AllowAlways)
                    btn.AddToClassList("allow-always");
                else if (option.Kind == AcpPermissionKind.RejectOnce)
                    btn.AddToClassList("reject");

                var capturedOption = option;
                btn.clicked += () =>
                {
                    callback?.Invoke(new RequestPermissionResult
                    {
                        Outcome = new PermissionOutcome
                        {
                            Outcome = "selected",
                            OptionId = capturedOption.OptionId
                        }
                    });

                    // Disable all buttons after a choice is made
                    foreach (var child in buttonsRow.Children())
                    {
                        if (child is Button b)
                            b.SetEnabled(false);
                    }
                    Element.AddToClassList("responded");
                };

                buttonsRow.Add(btn);
            }

            Element.Add(buttonsRow);
        }
    }

    /// <summary>
    /// Helper methods for creating UIElements (Unity 2022.3 compatible).
    /// </summary>
    internal static class UIHelper
    {
        public static VisualElement CreateElement(params string[] classNames)
        {
            var element = new VisualElement();
            foreach (var className in classNames)
            {
                element.AddToClassList(className);
            }
            return element;
        }

        public static Label CreateLabel(string text, params string[] classNames)
        {
            var label = new Label(text);
            foreach (var className in classNames)
            {
                label.AddToClassList(className);
            }
            return label;
        }

        public static Button CreateButton(string text, params string[] classNames)
        {
            var button = new Button { text = text };
            foreach (var className in classNames)
            {
                button.AddToClassList(className);
            }
            return button;
        }

        public static TextField CreateTextField(bool multiline, params string[] classNames)
        {
            var textField = new TextField { multiline = multiline };
            foreach (var className in classNames)
            {
                textField.AddToClassList(className);
            }
            return textField;
        }

        public static ScrollView CreateScrollView(params string[] classNames)
        {
            var scrollView = new ScrollView();
            foreach (var className in classNames)
            {
                scrollView.AddToClassList(className);
            }
            return scrollView;
        }

        /// <summary>
        /// Sets pointer cursor on an element (Unity 2022.3 compatible).
        /// </summary>
        public static void SetPointerCursor(VisualElement element)
        {
            element.RegisterCallback<MouseEnterEvent>(_ =>
            {
                UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            });
        }
    }
}

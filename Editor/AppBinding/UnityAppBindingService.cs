using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Extensions;
using DotCraft.Editor.RuntimeTools;
using DotCraft.Editor.Settings;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DotCraft.Editor.AppBinding
{
    internal sealed class UnityAppBindingService
    {
        private static readonly Lazy<UnityAppBindingService> LazyInstance =
            new(() => new UnityAppBindingService());

        private readonly ConcurrentDictionary<string, ActiveBinding> _activeBindings = new(StringComparer.Ordinal);
        private readonly object _handoffSnapshotGate = new();
        private readonly UnityAppBindingLocalServer _localServer;
        private HandoffSnapshot _handoffSnapshot;

        private UnityAppBindingService()
        {
            _localServer = new UnityAppBindingLocalServer(HandleHandoffAsync);
            RefreshHandoffSnapshot();
        }

        public static UnityAppBindingService Instance => LazyInstance.Value;

        public bool IsLocalServerRunning => _localServer.IsRunning;

        public string LocalServerUrl => _localServer.ListenUrl;

        public string LastError => _localServer.LastError;

        public IReadOnlyCollection<ActiveBinding> ActiveBindings => _activeBindings.Values.ToArray();

        internal event Action ActiveBindingsChanged;

        public void ApplySettings()
        {
            if (DotCraftSettings.Instance.EnableAppBindingLocalServer)
                StartLocalServer();
            else
                StopLocalServer();
        }

        public void StartLocalServer()
        {
            RefreshHandoffSnapshot();
            _localServer.Start();
        }

        public void StopLocalServer()
        {
            _localServer.Stop();
        }

        public void RestartLocalServer()
        {
            RefreshHandoffSnapshot();
            _localServer.Restart();
        }

        public bool RemoveActiveBinding(string bindingId)
        {
            return RemoveActiveBinding(bindingId, "Removed locally from Unity settings.");
        }

        internal void RefreshHandoffSnapshot()
        {
            var settings = DotCraftSettings.Instance;
            var enabledPluginToolIds = settings.DynamicToolEnabledById
                .Where(pair => pair.Value)
                .Select(pair => pair.Key)
                .ToArray();
            var snapshot = new HandoffSnapshot
            {
                AccountLabel = BuildAccountLabel(Application.productName),
                EnableBuiltinUnityTools = settings.EnableBuiltinUnityTools,
                EnabledPluginToolIds = enabledPluginToolIds,
                RequestTimeoutSeconds = Math.Max(5, settings.RequestTimeoutSeconds),
                RuntimeTools = RuntimeToolCatalog.Discover()
            };

            lock (_handoffSnapshotGate)
            {
                _handoffSnapshot = snapshot;
            }
        }

        public void Shutdown()
        {
            StopLocalServer();
            var hadBindings = !_activeBindings.IsEmpty;
            foreach (var binding in _activeBindings.Values)
                binding.Client?.Dispose();
            _activeBindings.Clear();
            if (hadBindings)
                NotifyActiveBindingsChanged();
        }

        private async Task<string> HandleHandoffAsync(UnityAppBindingHandoff handoff, CancellationToken ct)
        {
            if (!string.Equals(handoff.AppId, UnityAppBindingConstants.AppId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected app id '{handoff.AppId}'.");

            var snapshot = GetHandoffSnapshot();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(snapshot.RequestTimeoutSeconds));

            if (string.Equals(handoff.Operation, "connect", StringComparison.Ordinal))
                return await HandleConnectAsync(handoff, snapshot, timeout.Token).ConfigureAwait(false);

            if (string.Equals(handoff.Operation, "bind", StringComparison.Ordinal))
                return await HandleBindAsync(handoff, snapshot, timeout.Token).ConfigureAwait(false);

            throw new InvalidOperationException($"Unsupported App Binding operation '{handoff.Operation}'.");
        }

        private HandoffSnapshot GetHandoffSnapshot()
        {
            lock (_handoffSnapshotGate)
            {
                return _handoffSnapshot ?? throw new InvalidOperationException("App Binding handoff snapshot is not initialized.");
            }
        }

        private async Task<string> HandleConnectAsync(
            UnityAppBindingHandoff handoff,
            HandoffSnapshot snapshot,
            CancellationToken ct)
        {
            using var client = await DotCraftAppServerClient.ConnectAsync(handoff.Endpoint, ct).ConfigureAwait(false);
            await client.InitializeAsync(ct).ConfigureAwait(false);
            var request = await client.GetAppConnectionRequestAsync(
                handoff.AppId,
                handoff.RequestId,
                handoff.RequestToken,
                ct).ConfigureAwait(false);

            var status = await client.CompleteAppConnectionAsync(
                request.ConnectionRequestId,
                handoff.RequestToken,
                handoff.AppId,
                snapshot.AccountLabel,
                ct).ConfigureAwait(false);

            Debug.Log($"[DotCraft] App Binding connected to workspace '{request.WorkspaceLabel}' as '{status.State}'.");
            return $"Connected Unity Editor project '{snapshot.AccountLabel}' to DotCraft workspace '{request.WorkspaceLabel}'.";
        }

        private async Task<string> HandleBindAsync(
            UnityAppBindingHandoff handoff,
            HandoffSnapshot snapshot,
            CancellationToken ct)
        {
            var client = await DotCraftAppServerClient.ConnectAsync(handoff.Endpoint, ct).ConfigureAwait(false);
            try
            {
                await client.InitializeAsync(ct).ConfigureAwait(false);
                var request = await client.GetAppBindingRequestAsync(
                    handoff.AppId,
                    handoff.RequestId,
                    handoff.RequestToken,
                    ct).ConfigureAwait(false);

                var grantedScopes = (request.RequestedScopes ?? new List<string>())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var grantId = $"unity_grant_{Guid.NewGuid():N}";
                var accept = await client.AcceptAppBindingAsync(
                    request.BindingRequestId,
                    handoff.RequestToken,
                    grantId,
                    grantedScopes,
                    snapshot.AccountLabel,
                    ct).ConfigureAwait(false);

                var attachment = UnityAppBindingToolCatalogAdapter.Build(
                    snapshot.RuntimeTools,
                    snapshot.EnableBuiltinUnityTools,
                    snapshot.EnabledPluginToolIds,
                    grantedScopes);
                foreach (var diagnostic in attachment.Diagnostics.Take(8))
                    Debug.LogWarning($"[DotCraft] App Binding runtime tool discovery: {diagnostic}");

                if (attachment.Tools.Count == 0)
                    throw new InvalidOperationException("No enabled Unity runtime tools match the granted App Binding scopes.");

                var bindingId = accept.Binding.BindingId;
                client.SetDynamicToolHandler((call, token) => HandleToolCallAsync(bindingId, attachment.ToolsByName, call, token));
                client.SetNotificationHandler((method, @params, token) =>
                    HandleAppServerNotificationAsync(bindingId, accept.Binding.ThreadId, method, @params, token));
                client.Disconnected += reason => RemoveActiveBinding(bindingId, reason);

                var attach = await client.AttachToolsAsync(
                    bindingId,
                    accept.Binding.ThreadId,
                    UnityAppBindingConstants.AppId,
                    grantId,
                    attachment,
                    ct).ConfigureAwait(false);

                ReplaceActiveBinding(new ActiveBinding
                {
                    BindingId = bindingId,
                    ThreadId = accept.Binding.ThreadId,
                    ToolCount = attach.AcceptedToolCount,
                    Client = client,
                    ConnectedAt = DateTimeOffset.UtcNow
                });

                Debug.Log($"[DotCraft] App Binding attached {attach.AcceptedToolCount} Unity tool(s) to thread '{accept.Binding.ThreadId}'.");
                return $"Bound Unity Editor to DotCraft thread '{accept.Binding.ThreadId}' with {attach.AcceptedToolCount} tool(s).";
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private void ReplaceActiveBinding(ActiveBinding binding)
        {
            foreach (var existing in _activeBindings.Values.Where(item => item.ThreadId == binding.ThreadId).ToArray())
            {
                if (_activeBindings.TryRemove(existing.BindingId, out var removed))
                {
                    removed.Client?.Dispose();
                }
            }
            _activeBindings[binding.BindingId] = binding;
            NotifyActiveBindingsChanged();
        }

        private bool RemoveActiveBinding(string bindingId, string reason)
        {
            if (string.IsNullOrWhiteSpace(bindingId))
                return false;

            if (!_activeBindings.TryRemove(bindingId, out var removed))
                return false;

            removed.Client?.Dispose();
            Debug.Log($"[DotCraft] App Binding removed for thread '{removed.ThreadId}': {reason}");
            NotifyActiveBindingsChanged();
            return true;
        }

        private void NotifyActiveBindingsChanged()
        {
            MainThreadDispatcher.RunOrEnqueue(() => ActiveBindingsChanged?.Invoke());
        }

        private Task HandleAppServerNotificationAsync(
            string bindingId,
            string threadId,
            string method,
            JToken @params,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (string.Equals(method, "thread/appBindings/changed", StringComparison.Ordinal))
            {
                var changedBindingId = @params.Value<string>("bindingId");
                var appId = @params.Value<string>("appId");
                var state = @params.Value<string>("state");
                var changeKind = @params.Value<string>("changeKind");
                if (string.Equals(changedBindingId, bindingId, StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(appId)
                        || string.Equals(appId, UnityAppBindingConstants.AppId, StringComparison.Ordinal))
                    && IsInactiveBindingState(state))
                {
                    RemoveActiveBinding(bindingId, $"DotCraft marked binding {changeKind ?? state}.");
                }
            }
            else if (string.Equals(method, "thread/deleted", StringComparison.Ordinal)
                     && string.Equals(@params.Value<string>("threadId"), threadId, StringComparison.Ordinal))
            {
                RemoveActiveBinding(bindingId, "DotCraft deleted the bound thread.");
            }

            return Task.CompletedTask;
        }

        private static bool IsInactiveBindingState(string state)
        {
            return string.Equals(state, "offline", StringComparison.Ordinal)
                   || string.Equals(state, "revoked", StringComparison.Ordinal)
                   || string.Equals(state, "expired", StringComparison.Ordinal)
                   || string.Equals(state, "cancelled", StringComparison.Ordinal);
        }

        private async Task<AppServerDynamicToolResult> HandleToolCallAsync(
            string bindingId,
            IReadOnlyDictionary<string, RuntimeToolDefinition> toolsByName,
            AppServerDynamicToolCall call,
            CancellationToken ct)
        {
            if (!_activeBindings.ContainsKey(bindingId))
                return AppServerDynamicToolResult.Failed("UnityBindingClosed", "Unity App Binding is no longer active.");

            if (!string.Equals(call.Namespace, UnityAppBindingConstants.ToolNamespace, StringComparison.Ordinal))
                return AppServerDynamicToolResult.Failed("UnityToolNamespaceMismatch", "Tool namespace is not unity.");

            if (string.IsNullOrWhiteSpace(call.Tool) || !toolsByName.TryGetValue(call.Tool, out var tool))
                return AppServerDynamicToolResult.Failed("UnityToolUnavailable", $"Unity tool '{call.Tool}' is not available.");

            try
            {
                var args = call.Arguments ?? new JObject();
                var result = await MainThreadDispatcher.RunOnMainThread(
                    () => RuntimeToolInvoker.InvokeAsync(tool, args),
                    timeoutMs: Math.Max(5000, DotCraftSettings.Instance.RequestTimeoutSeconds * 1000));
                return AppServerDynamicToolResult.Ok(result);
            }
            catch (Exception ex)
            {
                return AppServerDynamicToolResult.Failed("UnityToolFailed", ex.Message);
            }
        }

        private static string BuildAccountLabel(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
                project = "Unity Editor";
            return project;
        }

        internal sealed class HandoffSnapshot
        {
            public string AccountLabel { get; set; }
            public bool EnableBuiltinUnityTools { get; set; }
            public string[] EnabledPluginToolIds { get; set; } = Array.Empty<string>();
            public int RequestTimeoutSeconds { get; set; }
            public RuntimeToolCatalogSnapshot RuntimeTools { get; set; }
        }

        internal sealed class ActiveBinding
        {
            public string BindingId { get; set; }
            public string ThreadId { get; set; }
            public int ToolCount { get; set; }
            public DotCraftAppServerClient Client { get; set; }
            public DateTimeOffset ConnectedAt { get; set; }
        }
    }
}

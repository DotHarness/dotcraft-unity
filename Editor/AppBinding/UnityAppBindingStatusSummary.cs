using System;
using System.Collections.Generic;
using System.Linq;

namespace DotCraft.Editor.AppBinding
{
    internal sealed class UnityAppBindingStatusSummary
    {
        private UnityAppBindingStatusSummary(
            bool isLocalServerRunning,
            string localServerUrl,
            string lastError,
            int bindingCount,
            int threadCount,
            int toolCount)
        {
            IsLocalServerRunning = isLocalServerRunning;
            LocalServerUrl = localServerUrl ?? string.Empty;
            LastError = lastError ?? string.Empty;
            BindingCount = bindingCount;
            ThreadCount = threadCount;
            ToolCount = toolCount;
            GatewayMcpUrl = BuildGatewayMcpUrl(LocalServerUrl);
            IsVisible = isLocalServerRunning || bindingCount > 0;
            Tooltip = BuildTooltip();
        }

        public static UnityAppBindingStatusSummary Empty { get; } =
            new(false, string.Empty, string.Empty, 0, 0, 0);

        public bool IsVisible { get; }

        public bool IsLocalServerRunning { get; }

        public string LocalServerUrl { get; }

        public string GatewayMcpUrl { get; }

        public string LastError { get; }

        public int BindingCount { get; }

        public int ThreadCount { get; }

        public int ToolCount { get; }

        public string Tooltip { get; }

        public static UnityAppBindingStatusSummary FromBindings(IEnumerable<UnityAppBindingService.ActiveBinding> bindings)
        {
            return FromState(false, string.Empty, string.Empty, bindings);
        }

        public static UnityAppBindingStatusSummary FromState(
            bool isLocalServerRunning,
            string localServerUrl,
            string lastError,
            IEnumerable<UnityAppBindingService.ActiveBinding> bindings)
        {
            if (bindings == null)
            {
                return new UnityAppBindingStatusSummary(
                    isLocalServerRunning,
                    localServerUrl,
                    lastError,
                    0,
                    0,
                    0);
            }

            var snapshot = bindings
                .Where(binding => binding != null)
                .ToList();

            var threadCount = snapshot
                .Select(binding => binding.ThreadId)
                .Where(threadId => !string.IsNullOrWhiteSpace(threadId))
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (threadCount == 0)
                threadCount = snapshot.Count;

            var toolCount = snapshot.Sum(binding => Math.Max(0, binding.ToolCount));
            return new UnityAppBindingStatusSummary(
                isLocalServerRunning,
                localServerUrl,
                lastError,
                snapshot.Count,
                threadCount,
                toolCount);
        }

        private string BuildTooltip()
        {
            if (!IsVisible)
                return string.Empty;

            if (BindingCount > 0)
            {
                var serverText = IsLocalServerRunning
                    ? $" Tool Gateway MCP: {GatewayMcpUrl}."
                    : " Local server is stopped.";
                return $"DotCraft App Binding: connected to {ThreadCount} thread(s), {ToolCount} tool(s)." +
                       serverText +
                       " Click for status and actions.";
            }

            return $"DotCraft Tool Gateway running. MCP endpoint: {GatewayMcpUrl}. Click for status and actions.";
        }

        private static string BuildGatewayMcpUrl(string localServerUrl)
        {
            if (string.IsNullOrWhiteSpace(localServerUrl))
                return string.Empty;

            var trimmed = localServerUrl.Trim();
            if (trimmed.EndsWith("/dotcraft/", StringComparison.Ordinal))
                return trimmed + "mcp";
            if (trimmed.EndsWith("/dotcraft", StringComparison.Ordinal))
                return trimmed + "/mcp";
            return trimmed.TrimEnd('/') + "/dotcraft/mcp";
        }
    }
}

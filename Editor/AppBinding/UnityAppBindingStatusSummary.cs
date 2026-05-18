using System;
using System.Collections.Generic;
using System.Linq;

namespace DotCraft.Editor.AppBinding
{
    internal sealed class UnityAppBindingStatusSummary
    {
        private UnityAppBindingStatusSummary(int threadCount, int toolCount)
        {
            ThreadCount = threadCount;
            ToolCount = toolCount;
            IsVisible = threadCount > 0;
            Tooltip = IsVisible
                ? $"DotCraft App Binding: connected to {threadCount} thread(s), {toolCount} tool(s). Click to open DotCraft Assistant."
                : string.Empty;
        }

        public static UnityAppBindingStatusSummary Empty { get; } = new(0, 0);

        public bool IsVisible { get; }

        public int ThreadCount { get; }

        public int ToolCount { get; }

        public string Tooltip { get; }

        public static UnityAppBindingStatusSummary FromBindings(IEnumerable<UnityAppBindingService.ActiveBinding> bindings)
        {
            if (bindings == null)
                return Empty;

            var snapshot = bindings
                .Where(binding => binding != null)
                .ToList();
            if (snapshot.Count == 0)
                return Empty;

            var threadCount = snapshot
                .Select(binding => binding.ThreadId)
                .Where(threadId => !string.IsNullOrWhiteSpace(threadId))
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (threadCount == 0)
                threadCount = snapshot.Count;

            var toolCount = snapshot.Sum(binding => Math.Max(0, binding.ToolCount));
            return new UnityAppBindingStatusSummary(threadCount, toolCount);
        }
    }
}

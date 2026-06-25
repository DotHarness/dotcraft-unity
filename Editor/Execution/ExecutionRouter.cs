using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DotCraft.Editor.Execution
{
    internal sealed class ExecutionRouter
    {
        public static ExecutionRouter Instance { get; } = new();

        private readonly Dictionary<string, IExecutionEngine> _engines = new(StringComparer.Ordinal);

        private ExecutionRouter()
        {
            Register(new RoslynCSharpExecutionEngine());
        }

        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var engine = Normalize(request.Engine);
            var mode = Normalize(request.Mode) ?? UnityExecutionModes.Editor;
            if (engine == null || !_engines.TryGetValue(engine, out var executionEngine))
            {
                return Task.FromResult(ExecutionResult.Failed(
                    mode,
                    "UnsupportedExecutionEngine",
                    $"Execution engine '{request.Engine}' is not registered.",
                    0));
            }

            return executionEngine.ExecuteAsync(request);
        }

        private void Register(IExecutionEngine engine)
        {
            _engines[engine.Engine] = engine;
        }

        private static string Normalize(string value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}

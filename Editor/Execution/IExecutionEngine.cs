using System.Threading.Tasks;

namespace DotCraft.Editor.Execution
{
    internal interface IExecutionEngine
    {
        string Engine { get; }

        Task<ExecutionResult> ExecuteAsync(ExecutionRequest request);
    }
}

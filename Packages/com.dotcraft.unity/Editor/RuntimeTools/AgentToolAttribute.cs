using System;
using DotCraft.Editor.Protocol;

namespace DotCraft.Editor.RuntimeTools
{
    /// <summary>
    /// Marks a static editor method as an agent runtime dynamic tool.
    /// These tools are DotCraft-specific ACP extensions and are not declared for Custom ACP agents.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class AgentToolAttribute : Attribute
    {
        /// <summary>
        /// Model-visible tool name. When omitted, the method name is used.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional model-visible namespace.
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Model-visible tool description. When omitted, DescriptionAttribute on the method is used.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Tool-call kind used by DotCraft UI. Defaults to "other".
        /// </summary>
        public string Kind { get; set; } = AcpToolKind.Other;

        /// <summary>
        /// Whether DotCraft should defer loading this tool into the model context until needed.
        /// </summary>
        public bool DeferLoading { get; set; } = true;

        /// <summary>
        /// Optional DotCraft approval category, such as "file", "shell", or "remoteResource".
        /// </summary>
        public string ApprovalKind { get; set; }

        /// <summary>
        /// Name of the string argument that contains the approval target.
        /// </summary>
        public string ApprovalTargetArgument { get; set; }

        /// <summary>
        /// Optional static approval operation.
        /// Exactly one of ApprovalOperation or ApprovalOperationArgument may be set when approval is declared.
        /// </summary>
        public string ApprovalOperation { get; set; }

        /// <summary>
        /// Optional argument name whose runtime value supplies the approval operation.
        /// Exactly one of ApprovalOperation or ApprovalOperationArgument may be set when approval is declared.
        /// </summary>
        public string ApprovalOperationArgument { get; set; }
    }
}

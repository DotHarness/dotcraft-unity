using System.Collections.Generic;
using System.Reflection;
using DotCraft.Editor.Protocol;

namespace DotCraft.Editor.RuntimeTools
{
    internal sealed class RuntimeToolDefinition
    {
        public RuntimeToolDefinition(
            string id,
            RuntimeToolSource source,
            MethodInfo method,
            IReadOnlyList<RuntimeToolParameter> parameters,
            AcpRuntimeToolDescriptor descriptor)
        {
            Id = id;
            Source = source;
            Method = method;
            Parameters = parameters;
            Descriptor = descriptor;
        }

        public string Id { get; }

        public RuntimeToolSource Source { get; }

        public MethodInfo Method { get; }

        public IReadOnlyList<RuntimeToolParameter> Parameters { get; }

        public AcpRuntimeToolDescriptor Descriptor { get; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Descriptor.Namespace)
                ? Descriptor.Name
                : $"{Descriptor.Namespace}.{Descriptor.Name}";
    }

    internal enum RuntimeToolSource
    {
        Builtin,
        Plugin
    }

    internal sealed class RuntimeToolParameter
    {
        public RuntimeToolParameter(ParameterInfo parameter, string jsonName)
        {
            Parameter = parameter;
            JsonName = jsonName;
        }

        public ParameterInfo Parameter { get; }

        public string JsonName { get; }
    }

    internal sealed class RuntimeToolCatalogSnapshot
    {
        public RuntimeToolCatalogSnapshot(
            IReadOnlyList<RuntimeToolDefinition> tools,
            IReadOnlyList<string> diagnostics)
        {
            Tools = tools;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<RuntimeToolDefinition> Tools { get; }

        public IReadOnlyList<string> Diagnostics { get; }
    }

    internal sealed class RuntimeToolResolution
    {
        public RuntimeToolResolution(
            IReadOnlyList<RuntimeToolDefinition> tools,
            IReadOnlyList<string> diagnostics)
        {
            Tools = tools;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<RuntimeToolDefinition> Tools { get; }

        public IReadOnlyList<string> Diagnostics { get; }
    }
}

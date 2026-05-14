using System;

namespace DotCraft.Editor.RuntimeTools
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class DotCraftBuiltinRuntimeToolAttribute : Attribute
    {
        public string AcpMethod { get; set; }
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    internal sealed class DotCraftRuntimeToolSchemaHintAttribute : Attribute
    {
        public int Minimum { get; set; } = int.MinValue;

        public string[] EnumValues { get; set; }
    }
}
